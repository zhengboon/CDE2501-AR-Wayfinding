#!/usr/bin/env python3
"""
Build hybrid Street View data for Unity:
- Real Google Street View images (where API coverage exists inside the KML polygon)
- YouTube frame fallback (nearest mapped frame) for uncovered points
"""

from __future__ import annotations

import argparse
import datetime as dt
import json
import math
import os
import re
import urllib.parse
import urllib.request
import xml.etree.ElementTree as ET
from concurrent.futures import ThreadPoolExecutor, as_completed
from dataclasses import dataclass
from pathlib import Path
from typing import Dict, List, Optional, Sequence, Tuple


KML_NS = {"kml": "http://www.opengis.net/kml/2.2"}


@dataclass
class Node:
    node_id: str
    x: float
    y: float
    z: float


@dataclass
class FramePoint:
    image: str
    node_id: str
    x: float
    y: float
    z: float
    frame_index: int


def normalize_spaces(text: str) -> str:
    return " ".join((text or "").split())


def parse_kml_polygon(kml_path: Path, polygon_name: str) -> List[Tuple[float, float]]:
    tree = ET.parse(kml_path)
    root = tree.getroot()
    for placemark in root.findall(".//kml:Placemark", KML_NS):
        name_el = placemark.find("kml:name", KML_NS)
        if name_el is None:
            continue
        if normalize_spaces(name_el.text) != normalize_spaces(polygon_name):
            continue
        coords_el = placemark.find(".//kml:Polygon//kml:outerBoundaryIs//kml:LinearRing//kml:coordinates", KML_NS)
        if coords_el is None or not coords_el.text:
            continue

        out: List[Tuple[float, float]] = []
        parts = coords_el.text.strip().split()
        for raw in parts:
            xyz = raw.split(",")
            if len(xyz) < 2:
                continue
            lon = float(xyz[0])
            lat = float(xyz[1])
            out.append((lon, lat))
        return out

    return []


def point_in_polygon(lon: float, lat: float, polygon_lon_lat: Sequence[Tuple[float, float]]) -> bool:
    if len(polygon_lon_lat) < 3:
        return False

    inside = False
    j = len(polygon_lon_lat) - 1
    for i in range(len(polygon_lon_lat)):
        xi, yi = polygon_lon_lat[i]
        xj, yj = polygon_lon_lat[j]
        intersect = ((yi > lat) != (yj > lat)) and (
            lon < (xj - xi) * (lat - yi) / ((yj - yi) if (yj - yi) != 0 else 1e-12) + xi
        )
        if intersect:
            inside = not inside
        j = i
    return inside


def solve_3x3(a: List[List[float]], b: List[float]) -> Optional[List[float]]:
    # Gaussian elimination with partial pivoting
    m = [row[:] + [b[i]] for i, row in enumerate(a)]
    n = 3
    for col in range(n):
        pivot = col
        for r in range(col + 1, n):
            if abs(m[r][col]) > abs(m[pivot][col]):
                pivot = r
        if abs(m[pivot][col]) < 1e-12:
            return None
        m[col], m[pivot] = m[pivot], m[col]
        factor_base = m[col][col]
        for c in range(col, n + 1):
            m[col][c] /= factor_base
        for r in range(n):
            if r == col:
                continue
            factor = m[r][col]
            for c in range(col, n + 1):
                m[r][c] -= factor * m[col][c]
    return [m[i][n] for i in range(n)]


def fit_world_to_geo(
    refs: List[Tuple[float, float, float, float]],
) -> Optional[Tuple[List[float], List[float]]]:
    """
    Fit:
      lat = a0 + a1*x + a2*z
      lon = b0 + b1*x + b2*z
    refs: (x, z, lat, lon)
    """
    if len(refs) < 3:
        return None

    # Build normal equations for A^T A and A^T y
    ata = [[0.0, 0.0, 0.0], [0.0, 0.0, 0.0], [0.0, 0.0, 0.0]]
    at_lat = [0.0, 0.0, 0.0]
    at_lon = [0.0, 0.0, 0.0]
    for x, z, lat, lon in refs:
        v = [1.0, x, z]
        for r in range(3):
            for c in range(3):
                ata[r][c] += v[r] * v[c]
            at_lat[r] += v[r] * lat
            at_lon[r] += v[r] * lon

    coeff_lat = solve_3x3(ata, at_lat)
    coeff_lon = solve_3x3(ata, at_lon)
    if coeff_lat is None or coeff_lon is None:
        return None
    return coeff_lat, coeff_lon


def build_fallback_world_to_geo(
    graph: dict,
    nodes: Dict[str, Node],
) -> Tuple[List[float], List[float]]:
    # Approximate linear mapping from graph world bounds -> metadata geo bounds
    bounds = ((graph.get("metadata") or {}).get("areaBounds") or {})
    min_lat = float(bounds.get("minLat", 0.0))
    max_lat = float(bounds.get("maxLat", 0.0))
    min_lon = float(bounds.get("minLon", 0.0))
    max_lon = float(bounds.get("maxLon", 0.0))

    min_x = min(n.x for n in nodes.values())
    max_x = max(n.x for n in nodes.values())
    min_z = min(n.z for n in nodes.values())
    max_z = max(n.z for n in nodes.values())

    dx = max(1e-6, max_x - min_x)
    dz = max(1e-6, max_z - min_z)
    # lat = min_lat + (z-min_z)*(lat_range/dz)
    a0 = min_lat - (min_z * ((max_lat - min_lat) / dz))
    a1 = 0.0
    a2 = (max_lat - min_lat) / dz
    # lon = min_lon + (x-min_x)*(lon_range/dx)
    b0 = min_lon - (min_x * ((max_lon - min_lon) / dx))
    b1 = (max_lon - min_lon) / dx
    b2 = 0.0
    return [a0, a1, a2], [b0, b1, b2]


def world_to_geo(x: float, z: float, coeff_lat: Sequence[float], coeff_lon: Sequence[float]) -> Tuple[float, float]:
    lat = coeff_lat[0] + coeff_lat[1] * x + coeff_lat[2] * z
    lon = coeff_lon[0] + coeff_lon[1] * x + coeff_lon[2] * z
    return lat, lon


def load_graph(graph_path: Path) -> Tuple[dict, Dict[str, Node], Dict[str, List[str]], Dict[str, int]]:
    with graph_path.open("r", encoding="utf-8") as f:
        graph = json.load(f)

    nodes: Dict[str, Node] = {}
    for raw in graph.get("nodes", []):
        nid = raw.get("id")
        pos = raw.get("position", {})
        if not nid:
            continue
        nodes[nid] = Node(
            node_id=nid,
            x=float(pos.get("x", 0.0)),
            y=float(pos.get("y", 0.0)),
            z=float(pos.get("z", 0.0)),
        )

    adjacency: Dict[str, List[str]] = {nid: [] for nid in nodes.keys()}
    degree: Dict[str, int] = {nid: 0 for nid in nodes.keys()}
    for e in graph.get("edges", []):
        a = e.get("fromNode")
        b = e.get("toNode")
        if a not in nodes or b not in nodes:
            continue
        adjacency[a].append(b)
        adjacency[b].append(a)
        degree[a] += 1
        degree[b] += 1

    return graph, nodes, adjacency, degree


def load_locations_refs(locations_path: Path, nodes: Dict[str, Node]) -> List[Tuple[float, float, float, float]]:
    with locations_path.open("r", encoding="utf-8") as f:
        raw = json.load(f)
    locations = raw if isinstance(raw, list) else raw.get("locations", [])
    refs: List[Tuple[float, float, float, float]] = []
    for loc in locations:
        node_id = (loc.get("indoor_node_id") or "").strip()
        if not node_id or node_id not in nodes:
            continue
        node = nodes[node_id]
        refs.append((node.x, node.z, float(loc.get("gps_lat", 0.0)), float(loc.get("gps_lon", 0.0))))
    return refs


def load_video_frames(video_map_path: Path) -> List[FramePoint]:
    if not video_map_path.exists():
        return []
    with video_map_path.open("r", encoding="utf-8") as f:
        raw = json.load(f)
    out: List[FramePoint] = []
    for v in raw.get("videos", []):
        for frame in v.get("frames", []):
            image = (frame.get("image") or "").strip()
            pos = frame.get("position") or {}
            if not image:
                continue
            match = re.search(r"frame_(\d+)\.jpg$", image, re.IGNORECASE)
            frame_index = int(match.group(1)) if match else 0
            out.append(
                FramePoint(
                    image=image,
                    node_id=(frame.get("nodeId") or "").strip(),
                    x=float(pos.get("x", 0.0)),
                    y=float(pos.get("y", 0.0)),
                    z=float(pos.get("z", 0.0)),
                    frame_index=frame_index,
                )
            )
    return out


def sqr_dist_2d(ax: float, az: float, bx: float, bz: float) -> float:
    dx = ax - bx
    dz = az - bz
    return dx * dx + dz * dz


def nearest_frame(node: Node, frames: List[FramePoint], max_distance_m: float) -> Optional[FramePoint]:
    if not frames:
        return None
    max_sqr = max_distance_m * max_distance_m
    best: Optional[FramePoint] = None
    best_sqr = float("inf")
    for f in frames:
        d2 = sqr_dist_2d(node.x, node.z, f.x, f.z)
        if d2 < best_sqr:
            best_sqr = d2
            best = f
            continue

        if abs(d2 - best_sqr) <= 1e-6 and best is not None:
            # Tie-break toward later frames to avoid title-card frame_0001.
            if f.frame_index > best.frame_index:
                best = f
    if best is None or best_sqr > max_sqr:
        return None
    return best


def filter_preferred_frames(frames: List[FramePoint], skip_frame0001: bool) -> List[FramePoint]:
    if not skip_frame0001:
        return list(frames)

    non_first = [f for f in frames if f.frame_index != 1]
    if non_first:
        return non_first
    return list(frames)


def pick_better_frame(a: Optional[FramePoint], b: FramePoint) -> FramePoint:
    if a is None:
        return b

    # Prefer any frame that is not frame_0001.
    a_non_first = a.frame_index != 1
    b_non_first = b.frame_index != 1
    if b_non_first and not a_non_first:
        return b
    if a_non_first and not b_non_first:
        return a

    # Otherwise prefer later frame index.
    if b.frame_index > a.frame_index:
        return b
    return a


def request_json(url: str, timeout: int = 20) -> Optional[dict]:
    try:
        req = urllib.request.Request(url, headers={"User-Agent": "Mozilla/5.0"})
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            data = resp.read()
        return json.loads(data.decode("utf-8"))
    except Exception:
        return None


def download_image(url: str, path: Path, timeout: int = 20) -> bool:
    path.parent.mkdir(parents=True, exist_ok=True)
    if path.exists() and path.stat().st_size > 0:
        return True
    try:
        req = urllib.request.Request(url, headers={"User-Agent": "Mozilla/5.0"})
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            data = resp.read()
        if not data:
            return False
        path.write_bytes(data)
        return True
    except Exception:
        return False


def collect_existing_heading_images(data_dir: Path, street_view_dir: str, node_id: str) -> List[dict]:
    node_dir = data_dir / Path(street_view_dir) / node_id
    if not node_dir.exists():
        return []

    out: List[dict] = []
    for image_path in sorted(node_dir.glob("h*.jpg")):
        match = re.match(r"h(\d{3})\.jpg$", image_path.name, re.IGNORECASE)
        if match is None:
            continue
        heading = int(match.group(1)) % 360
        rel_path = f"{street_view_dir}/{node_id}/{image_path.name}".replace("\\", "/")
        out.append({"heading": heading, "image": rel_path})

    out.sort(key=lambda item: item["heading"])
    return out


def build_streetview_urls(
    lat: float,
    lon: float,
    api_key: str,
    image_size: str,
    heading: int,
    fov: int,
) -> Tuple[str, str]:
    metadata_params = {
        "location": f"{lat:.8f},{lon:.8f}",
        "source": "outdoor",
        "key": api_key,
    }
    static_params = {
        "size": image_size,
        "location": f"{lat:.8f},{lon:.8f}",
        "source": "outdoor",
        "heading": str(heading),
        "pitch": "0",
        "fov": str(max(10, min(120, int(fov)))),
        "return_error_code": "true",
        "key": api_key,
    }
    meta_url = "https://maps.googleapis.com/maps/api/streetview/metadata?" + urllib.parse.urlencode(metadata_params)
    static_url = "https://maps.googleapis.com/maps/api/streetview?" + urllib.parse.urlencode(static_params)
    return meta_url, static_url


def main() -> int:
    parser = argparse.ArgumentParser(description="Build hybrid Street View manifest (Google + YouTube fallback).")
    parser.add_argument("--data-dir", default="Assets/StreamingAssets/Data")
    parser.add_argument("--kml", default="cde2501.kml")
    parser.add_argument("--polygon-name", default="Site area")
    parser.add_argument("--graph-file", default="estate_graph.json")
    parser.add_argument("--locations-file", default="locations.json")
    parser.add_argument("--video-map-file", default="video_frame_map.json")
    parser.add_argument("--out-manifest", default="street_view_manifest.json")
    parser.add_argument("--street-view-dir", default="street_view/google")
    parser.add_argument("--headings", default="")
    parser.add_argument("--heading-step", type=int, default=15)
    parser.add_argument("--fov", type=int, default=90)
    parser.add_argument("--image-size", default="640x640")
    parser.add_argument("--min-spacing-m", type=float, default=28.0)
    parser.add_argument("--max-google-nodes", type=int, default=80)
    parser.add_argument("--all-candidate-nodes", action="store_true")
    parser.add_argument("--max-fallback-distance-m", type=float, default=220.0)
    parser.add_argument("--google-api-key", default=os.environ.get("GOOGLE_MAPS_API_KEY", ""))
    parser.add_argument("--timeout-seconds", type=int, default=20)
    parser.add_argument("--download-workers", type=int, default=1)
    parser.add_argument("--allow-frame0001-fallback", action="store_true")
    args = parser.parse_args()

    data_dir = Path(args.data_dir)
    graph_path = data_dir / args.graph_file
    locations_path = data_dir / args.locations_file
    video_map_path = data_dir / args.video_map_file
    out_manifest_path = data_dir / args.out_manifest
    kml_path = Path(args.kml)

    if not graph_path.exists():
        print(f"[error] missing graph file: {graph_path}")
        return 1
    if not locations_path.exists():
        print(f"[error] missing locations file: {locations_path}")
        return 1
    if not kml_path.exists():
        print(f"[error] missing KML file: {kml_path}")
        return 1

    polygon = parse_kml_polygon(kml_path, args.polygon_name)
    if len(polygon) < 3:
        print(f"[error] polygon '{args.polygon_name}' not found or invalid in {kml_path}")
        return 1

    graph, nodes, adjacency, degree = load_graph(graph_path)
    refs = load_locations_refs(locations_path, nodes)
    fit = fit_world_to_geo(refs)
    if fit is None:
        coeff_lat, coeff_lon = build_fallback_world_to_geo(graph, nodes)
        fit_mode = "metadata_bounds_fallback"
    else:
        coeff_lat, coeff_lon = fit
        fit_mode = "location_least_squares"

    headings: List[int] = []
    if args.headings.strip():
        for h in args.headings.split(","):
            h = h.strip()
            if not h:
                continue
            headings.append(int(h))
    else:
        step = max(1, min(180, int(args.heading_step)))
        headings.extend(list(range(0, 360, step)))

    headings = sorted(set(int(h) % 360 for h in headings))
    if not headings:
        headings = [0, 90, 180, 270]

    frames = load_video_frames(video_map_path)
    frames = filter_preferred_frames(frames, skip_frame0001=(not args.allow_frame0001_fallback))

    # Candidate nodes inside polygon, road-level-ish, and connected
    candidates: List[Tuple[str, float, float, float, float]] = []
    for nid, node in nodes.items():
        if degree.get(nid, 0) < 2:
            continue
        if node.y > 5.0:
            continue
        lat, lon = world_to_geo(node.x, node.z, coeff_lat, coeff_lon)
        if point_in_polygon(lon, lat, polygon):
            candidates.append((nid, node.x, node.y, node.z, lat))

    # Greedy spacing filter in world coordinates
    selected_ids: List[str] = []
    spacing_sqr = args.min_spacing_m * args.min_spacing_m
    for nid, x, _y, z, _lat in sorted(candidates, key=lambda item: item[0]):
        keep = True
        for sid in selected_ids:
            s = nodes[sid]
            if sqr_dist_2d(x, z, s.x, s.z) < spacing_sqr:
                keep = False
                break
        if keep:
            selected_ids.append(nid)
        if (not args.all_candidate_nodes) and len(selected_ids) >= max(1, args.max_google_nodes):
            break

    selected_set = set(selected_ids)
    warnings: List[str] = []
    entries: List[dict] = []

    # For fallback nearest lookup speed
    frames_by_node: Dict[str, FramePoint] = {}
    for fp in frames:
        if not fp.node_id:
            continue
        existing = frames_by_node.get(fp.node_id)
        frames_by_node[fp.node_id] = pick_better_frame(existing, fp)

    google_enabled = bool(args.google_api_key.strip())
    google_success = 0
    fallback_used = 0

    for idx, nid in enumerate(selected_ids):
        node = nodes[nid]
        lat, lon = world_to_geo(node.x, node.z, coeff_lat, coeff_lon)
        neighbor_ids: List[str] = []
        seen_neighbors = set()
        for neighbor in adjacency.get(nid, []):
            if neighbor == nid:
                continue
            if neighbor not in selected_set:
                continue
            if neighbor in seen_neighbors:
                continue
            seen_neighbors.add(neighbor)
            neighbor_ids.append(neighbor)

        existing_heading_images = collect_existing_heading_images(data_dir, args.street_view_dir, nid)
        heading_image_map: Dict[int, str] = {int(item["heading"]) % 360: item["image"] for item in existing_heading_images}
        heading_images: List[dict] = []
        view_type = "none"

        metadata_status = "NO_KEY"
        if google_enabled:
            meta_url, _ = build_streetview_urls(
                lat,
                lon,
                args.google_api_key,
                args.image_size,
                headings[0],
                args.fov,
            )
            meta = request_json(meta_url, timeout=args.timeout_seconds)
            metadata_status = (meta or {}).get("status", "REQUEST_FAILED")
            if metadata_status == "OK":
                workers = max(1, int(args.download_workers))
                if workers == 1:
                    for heading in headings:
                        _meta_url, static_url = build_streetview_urls(
                            lat,
                            lon,
                            args.google_api_key,
                            args.image_size,
                            heading,
                            args.fov,
                        )
                        rel_path = f"{args.street_view_dir}/{nid}/h{heading:03d}.jpg".replace("\\", "/")
                        abs_path = data_dir / rel_path
                        ok = download_image(static_url, abs_path, timeout=args.timeout_seconds)
                        if ok:
                            heading_image_map[int(heading) % 360] = rel_path
                else:
                    futures = {}
                    with ThreadPoolExecutor(max_workers=workers) as executor:
                        for heading in headings:
                            _meta_url, static_url = build_streetview_urls(
                                lat,
                                lon,
                                args.google_api_key,
                                args.image_size,
                                heading,
                                args.fov,
                            )
                            rel_path = f"{args.street_view_dir}/{nid}/h{heading:03d}.jpg".replace("\\", "/")
                            abs_path = data_dir / rel_path
                            future = executor.submit(download_image, static_url, abs_path, args.timeout_seconds)
                            futures[future] = (heading, rel_path)

                        for future in as_completed(futures):
                            heading, rel_path = futures[future]
                            try:
                                ok = bool(future.result())
                            except Exception:
                                ok = False
                            if ok:
                                heading_image_map[int(heading) % 360] = rel_path

                heading_images = [{"heading": h, "image": heading_image_map[h]} for h in sorted(heading_image_map)]
                if heading_images:
                    view_type = f"google_{len(heading_images)}dir"
                    google_success += 1
            else:
                if heading_image_map:
                    heading_images = [{"heading": h, "image": heading_image_map[h]} for h in sorted(heading_image_map)]
                    view_type = f"google_{len(heading_images)}dir_cached"
                    google_success += 1
                    warnings.append(f"{nid}: Street View metadata status={metadata_status}; reused cached images")
                else:
                    warnings.append(f"{nid}: Street View metadata status={metadata_status}")
        elif heading_image_map:
            heading_images = [{"heading": h, "image": heading_image_map[h]} for h in sorted(heading_image_map)]
            view_type = f"google_{len(heading_images)}dir_cached"
            google_success += 1

        fallback = frames_by_node.get(nid)
        if fallback is None:
            fallback = nearest_frame(node, frames, args.max_fallback_distance_m)
        fallback_image = fallback.image if fallback is not None else ""
        if not view_type.startswith("google_") and fallback_image:
            view_type = "youtube_fallback"
            fallback_used += 1
        elif view_type.startswith("google_") and fallback_image:
            # keep fallback as backup even when google exists
            pass
        elif view_type == "none":
            warnings.append(f"{nid}: no google image and no youtube fallback within {args.max_fallback_distance_m}m")

        if view_type == "none":
            continue

        entries.append(
            {
                "nodeId": nid,
                "index": idx,
                "viewType": view_type,
                "position": {"x": round(node.x, 3), "y": round(node.y, 3), "z": round(node.z, 3)},
                "lat": round(lat, 8),
                "lon": round(lon, 8),
                "headingImages": heading_images,
                "fallbackImage": fallback_image,
                "adjacentNodeIds": neighbor_ids,
            }
        )

    manifest = {
        "version": "1.0",
        "generatedAtUtc": dt.datetime.now(dt.timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z"),
        "kmlPath": str(kml_path).replace("\\", "/"),
        "polygonName": args.polygon_name,
        "fitMode": fit_mode,
        "googleStreetViewEnabled": google_enabled,
        "headings": headings,
        "nodeCount": len(entries),
        "googleNodeCount": google_success,
        "youtubeFallbackCount": fallback_used,
        "nodes": entries,
        "warnings": warnings,
    }

    out_manifest_path.parent.mkdir(parents=True, exist_ok=True)
    out_manifest_path.write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    print(
        f"[done] {out_manifest_path} | nodes={len(entries)} google={google_success} fallback={fallback_used} "
        f"warnings={len(warnings)} fit={fit_mode}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
