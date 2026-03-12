#!/usr/bin/env python3
"""
Builds mapped video frame data for Unity:
1) Reads curated video CSV.
2) Resolves start/end graph nodes (overrides + title inference + defaults).
3) Generates route on estate graph.
4) Extracts frame images (yt-dlp + ffmpeg), with thumbnail fallback.
5) Writes manifest JSON and image files into StreamingAssets/Data.
"""

from __future__ import annotations

import argparse
import csv
import datetime as dt
import heapq
import json
import math
import os
import re
import shutil
import subprocess
import sys
import urllib.parse
import urllib.request
from dataclasses import dataclass
from pathlib import Path
from typing import Dict, List, Optional, Tuple


@dataclass
class Node:
    node_id: str
    x: float
    y: float
    z: float


def normalize_text(text: str) -> str:
    text = (text or "").lower().strip()
    text = re.sub(r"[^a-z0-9\s]", " ", text)
    text = re.sub(r"\s+", " ", text).strip()
    return text


def extract_video_id(url: str) -> str:
    if not url:
        return "unknown"

    parsed = urllib.parse.urlparse(url)
    if parsed.hostname and "youtube.com" in parsed.hostname:
        qs = urllib.parse.parse_qs(parsed.query)
        if "v" in qs and qs["v"]:
            return qs["v"][0]
    if parsed.hostname and "youtu.be" in parsed.hostname:
        return parsed.path.strip("/") or "unknown"
    return re.sub(r"[^a-zA-Z0-9_-]", "_", url)[:40] or "unknown"


def parse_duration_seconds(value: str) -> Optional[float]:
    if value is None:
        return None
    value = value.strip()
    if not value:
        return None

    if ":" in value:
        parts = value.split(":")
        try:
            parts_i = [int(p) for p in parts]
        except ValueError:
            return None
        if len(parts_i) == 3:
            h, m, s = parts_i
            return float(h * 3600 + m * 60 + s)
        if len(parts_i) == 2:
            m, s = parts_i
            return float(m * 60 + s)
        return None

    try:
        n = float(value)
    except ValueError:
        return None

    # Heuristic: very small plain numbers in this dataset are likely minutes.
    if n <= 240:
        return n * 60.0
    return n


def euclid_2d(a: Node, b: Node) -> float:
    dx = a.x - b.x
    dz = a.z - b.z
    return math.sqrt(dx * dx + dz * dz)


def load_graph(graph_path: Path) -> Tuple[Dict[str, Node], Dict[str, List[Tuple[str, float]]]]:
    with graph_path.open("r", encoding="utf-8") as f:
        data = json.load(f)

    nodes: Dict[str, Node] = {}
    for raw in data.get("nodes", []):
        node_id = raw.get("id")
        pos = raw.get("position", {})
        if not node_id:
            continue
        nodes[node_id] = Node(
            node_id=node_id,
            x=float(pos.get("x", 0.0)),
            y=float(pos.get("y", 0.0)),
            z=float(pos.get("z", 0.0)),
        )

    adjacency: Dict[str, List[Tuple[str, float]]] = {nid: [] for nid in nodes.keys()}
    for edge in data.get("edges", []):
        a = edge.get("fromNode")
        b = edge.get("toNode")
        if a not in nodes or b not in nodes:
            continue
        d = float(edge.get("distance", 0.0) or 0.0)
        if d <= 0.0:
            d = euclid_2d(nodes[a], nodes[b])
        adjacency[a].append((b, d))
        adjacency[b].append((a, d))

    return nodes, adjacency


def dijkstra_path(
    start_node: str,
    end_node: str,
    adjacency: Dict[str, List[Tuple[str, float]]],
) -> Tuple[List[str], float]:
    if start_node == end_node:
        return [start_node], 0.0
    if start_node not in adjacency or end_node not in adjacency:
        return [], float("inf")

    dist: Dict[str, float] = {start_node: 0.0}
    prev: Dict[str, str] = {}
    heap: List[Tuple[float, str]] = [(0.0, start_node)]
    visited = set()

    while heap:
        cur_dist, cur = heapq.heappop(heap)
        if cur in visited:
            continue
        visited.add(cur)
        if cur == end_node:
            break

        for nxt, w in adjacency.get(cur, []):
            nd = cur_dist + w
            if nd < dist.get(nxt, float("inf")):
                dist[nxt] = nd
                prev[nxt] = cur
                heapq.heappush(heap, (nd, nxt))

    if end_node not in dist:
        return [], float("inf")

    path = [end_node]
    cur = end_node
    while cur != start_node:
        cur = prev[cur]
        path.append(cur)
    path.reverse()
    return path, dist[end_node]


def interpolate_polyline(nodes: Dict[str, Node], path: List[str], sample_count: int) -> List[Tuple[str, Node]]:
    if not path:
        return []
    if len(path) == 1 or sample_count <= 1:
        nid = path[-1]
        return [(nid, nodes[nid])]

    points = [nodes[nid] for nid in path if nid in nodes]
    if len(points) < 2:
        nid = path[-1]
        return [(nid, nodes[nid])]

    seg_lengths: List[float] = []
    total = 0.0
    for i in range(len(points) - 1):
        seg = euclid_2d(points[i], points[i + 1])
        seg_lengths.append(seg)
        total += seg

    if total <= 0.0:
        nid = path[-1]
        return [(nid, nodes[nid]) for _ in range(sample_count)]

    out: List[Tuple[str, Node]] = []
    for i in range(sample_count):
        t = i / float(sample_count - 1)
        target = t * total
        run = 0.0
        placed = False

        for si, seg_len in enumerate(seg_lengths):
            start = points[si]
            end = points[si + 1]
            if seg_len <= 0.0:
                continue

            nxt_run = run + seg_len
            if target <= nxt_run or si == len(seg_lengths) - 1:
                local_t = (target - run) / seg_len
                local_t = max(0.0, min(1.0, local_t))
                x = start.x + ((end.x - start.x) * local_t)
                y = start.y + ((end.y - start.y) * local_t)
                z = start.z + ((end.z - start.z) * local_t)
                interp_node = Node(node_id=path[si], x=x, y=y, z=z)
                out.append((path[si], interp_node))
                placed = True
                break
            run = nxt_run

        if not placed:
            out.append((path[-1], nodes[path[-1]]))

    return out


def load_locations(locations_path: Path) -> List[dict]:
    with locations_path.open("r", encoding="utf-8") as f:
        data = json.load(f)
    if isinstance(data, list):
        return data
    if isinstance(data, dict) and isinstance(data.get("locations"), list):
        return data["locations"]
    return []


def load_overrides(overrides_path: Path) -> dict:
    if not overrides_path.exists():
        return {"defaults": {}, "videos": {}}
    with overrides_path.open("r", encoding="utf-8") as f:
        data = json.load(f)
    if not isinstance(data, dict):
        return {"defaults": {}, "videos": {}}
    data.setdefault("defaults", {})
    data.setdefault("videos", {})
    return data


def infer_start_end_nodes(
    title: str,
    video_id: str,
    locations: List[dict],
    overrides: dict,
) -> Tuple[Optional[str], Optional[str], str]:
    video_override = overrides.get("videos", {}).get(video_id, {})
    if isinstance(video_override, dict):
        s = (video_override.get("start_node_id") or "").strip()
        e = (video_override.get("end_node_id") or "").strip()
        if s and e:
            return s, e, "override"

    norm_title = normalize_text(title)
    matches: List[Tuple[int, str]] = []

    location_aliases: List[Tuple[str, str]] = []
    for loc in locations:
        name = (loc.get("name") or "").strip()
        node_id = (loc.get("indoor_node_id") or "").strip()
        if not name or not node_id:
            continue
        aliases = {
            normalize_text(name),
            normalize_text(name.replace("(home)", "")),
        }
        for a in aliases:
            if a:
                location_aliases.append((a, node_id))

    # Practical aliases from your map context.
    manual_aliases = {
        "queenstown mrt": "QTMRT",
        "mrt station": "QTMRT",
        "temple": "OSM_5133555909",
        "church": "OSM_1842169698",
        "market": "PC_7RV3_85",
        "clinic": "OSM_10830965796",
        "lions befrienders": "OSM_1842170067",
        "sparkle tots": "OSM_6745311675",
        "block 161": "PC_7RR3_MC",
        "home": "PC_7RR3_MC",
    }
    for alias, nid in manual_aliases.items():
        location_aliases.append((normalize_text(alias), nid))

    seen_pairs = set()
    for alias, node_id in location_aliases:
        key = (alias, node_id)
        if key in seen_pairs:
            continue
        seen_pairs.add(key)
        idx = norm_title.find(alias)
        if idx >= 0:
            matches.append((idx, node_id))

    matches.sort(key=lambda x: x[0])
    ordered_nodes = []
    for _, nid in matches:
        if not ordered_nodes or ordered_nodes[-1] != nid:
            ordered_nodes.append(nid)

    if len(ordered_nodes) >= 2:
        return ordered_nodes[0], ordered_nodes[-1], "title_match"

    default_start = (overrides.get("defaults", {}).get("start_node_id") or "QTMRT").strip()
    default_end = (overrides.get("defaults", {}).get("end_node_id") or "PC_7RV3_XH").strip()

    if len(ordered_nodes) == 1:
        only = ordered_nodes[0]
        if only != default_start:
            return default_start, only, "single_match_default_start"
        if default_end and default_end != only:
            return only, default_end, "single_match_default_end"

    if default_start and default_end:
        return default_start, default_end, "default"

    return None, None, "unresolved"


def ensure_tool(name: str) -> bool:
    return shutil.which(name) is not None


def download_thumbnail(url: str, out_path: Path) -> bool:
    if not url:
        return False
    out_path.parent.mkdir(parents=True, exist_ok=True)
    try:
        req = urllib.request.Request(url, headers={"User-Agent": "Mozilla/5.0"})
        with urllib.request.urlopen(req, timeout=20) as response:
            data = response.read()
        if not data:
            return False
        out_path.write_bytes(data)
        return True
    except Exception:
        return False


def extract_frames_from_video(
    video_url: str,
    video_id: str,
    out_dir: Path,
    interval_seconds: float,
    work_dir: Path,
    keep_video: bool,
) -> List[Path]:
    out_dir.mkdir(parents=True, exist_ok=True)
    for old in out_dir.glob("frame_*.jpg"):
        old.unlink(missing_ok=True)

    temp_video = work_dir / f"{video_id}.mp4"
    work_dir.mkdir(parents=True, exist_ok=True)

    download_cmd = [
        "yt-dlp",
        "-f",
        "mp4[height<=480]/best[height<=480]/best",
        "--no-playlist",
        "-o",
        str(temp_video),
        video_url,
    ]
    result = subprocess.run(download_cmd, capture_output=True, text=True)
    if result.returncode != 0 or not temp_video.exists():
        return []

    fps_expr = f"fps=1/{max(1.0, interval_seconds)}"
    frame_pattern = out_dir / "frame_%04d.jpg"
    ffmpeg_cmd = [
        "ffmpeg",
        "-hide_banner",
        "-loglevel",
        "error",
        "-y",
        "-i",
        str(temp_video),
        "-vf",
        fps_expr,
        str(frame_pattern),
    ]
    ff = subprocess.run(ffmpeg_cmd, capture_output=True, text=True)
    if ff.returncode != 0:
        if not keep_video:
            temp_video.unlink(missing_ok=True)
        return []

    if not keep_video:
        temp_video.unlink(missing_ok=True)

    frames = sorted(out_dir.glob("frame_*.jpg"))
    return frames


def build_manifest(args: argparse.Namespace) -> int:
    csv_path = Path(args.csv)
    graph_path = Path(args.graph)
    locations_path = Path(args.locations)
    overrides_path = Path(args.overrides)
    output_manifest = Path(args.output_manifest)
    frames_root = Path(args.frames_dir)
    work_dir = Path(args.work_dir)

    if not csv_path.exists():
        print(f"[error] missing CSV: {csv_path}")
        return 1
    if not graph_path.exists():
        print(f"[error] missing graph: {graph_path}")
        return 1
    if not locations_path.exists():
        print(f"[error] missing locations: {locations_path}")
        return 1

    nodes, adjacency = load_graph(graph_path)
    locations = load_locations(locations_path)
    overrides = load_overrides(overrides_path)

    has_ytdlp = ensure_tool("yt-dlp")
    has_ffmpeg = ensure_tool("ffmpeg")
    can_extract_frames = has_ytdlp and has_ffmpeg and (not args.thumbnail_only)

    output_manifest.parent.mkdir(parents=True, exist_ok=True)
    frames_root.mkdir(parents=True, exist_ok=True)

    rows: List[dict] = []
    with csv_path.open("r", encoding="utf-8-sig", newline="") as f:
        reader = csv.DictReader(f)
        for row in reader:
            rows.append(row)

    if args.limit and args.limit > 0:
        rows = rows[: args.limit]

    videos_out = []
    warnings: List[str] = []

    for row in rows:
        title = (row.get("title") or "").strip()
        url = (row.get("url") or "").strip()
        if not title or not url:
            continue

        video_id = extract_video_id(url)
        start_node, end_node, inference_mode = infer_start_end_nodes(title, video_id, locations, overrides)
        if not start_node or not end_node or start_node not in nodes or end_node not in nodes:
            warnings.append(f"{video_id}: unresolved start/end node ({start_node} -> {end_node})")
            continue

        path_nodes, total_dist = dijkstra_path(start_node, end_node, adjacency)
        if not path_nodes:
            warnings.append(f"{video_id}: no route between {start_node} and {end_node}")
            continue

        video_frame_dir = frames_root / video_id
        video_frame_dir.mkdir(parents=True, exist_ok=True)

        frame_paths: List[Path] = []
        frame_source = "thumbnail_fallback"

        if can_extract_frames:
            frame_paths = extract_frames_from_video(
                video_url=url,
                video_id=video_id,
                out_dir=video_frame_dir,
                interval_seconds=args.frame_interval,
                work_dir=work_dir,
                keep_video=args.keep_video,
            )
            if frame_paths:
                frame_source = "video_frames"

        if not frame_paths:
            thumb_url = (row.get("thumbnail_url") or "").strip()
            if not thumb_url:
                thumb_url = f"https://i.ytimg.com/vi/{video_id}/hqdefault.jpg"
            thumb_path = video_frame_dir / "frame_0001.jpg"
            if download_thumbnail(thumb_url, thumb_path):
                frame_paths = [thumb_path]
                frame_source = "thumbnail_fallback"

        if not frame_paths:
            warnings.append(f"{video_id}: failed to obtain any image frame")
            continue

        sample_positions = interpolate_polyline(nodes, path_nodes, len(frame_paths))
        duration_seconds = parse_duration_seconds(row.get("duration") or "")

        frames_out = []
        for i, frame_path in enumerate(frame_paths):
            node_id, p = sample_positions[min(i, len(sample_positions) - 1)]
            t_sec = 0.0
            if duration_seconds and len(frame_paths) > 1:
                t_sec = (duration_seconds * i) / float(len(frame_paths) - 1)
            elif duration_seconds and len(frame_paths) == 1:
                t_sec = duration_seconds

            rel_image = frame_path.as_posix().split("/Assets/StreamingAssets/Data/")[-1]
            if rel_image == frame_path.as_posix():
                # still relative to Data root path when script runs from project root
                rel_image = os.path.relpath(frame_path.as_posix(), frames_root.parent.as_posix()).replace("\\", "/")

            frames_out.append(
                {
                    "image": rel_image,
                    "timeSeconds": round(float(t_sec), 3),
                    "nodeId": node_id,
                    "position": {"x": round(p.x, 3), "y": round(p.y, 3), "z": round(p.z, 3)},
                }
            )

        videos_out.append(
            {
                "videoId": video_id,
                "title": title,
                "uploader": (row.get("uploader") or "").strip(),
                "url": url,
                "mode": (row.get("mode") or "").strip(),
                "duration": (row.get("duration") or "").strip(),
                "durationSeconds": round(float(duration_seconds), 3) if duration_seconds else 0.0,
                "startNodeId": start_node,
                "endNodeId": end_node,
                "inferenceMode": inference_mode,
                "routeDistanceMeters": round(float(total_dist), 3),
                "routeNodePath": path_nodes,
                "frameSource": frame_source,
                "frames": frames_out,
            }
        )

        print(f"[ok] {video_id}: {len(frames_out)} frames, {start_node} -> {end_node}, mode={inference_mode}, source={frame_source}")

    manifest = {
        "version": "1.0",
        "generatedAtUtc": dt.datetime.now(dt.timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z"),
        "sourceCsv": str(csv_path).replace("\\", "/"),
        "tools": {
            "ytDlpAvailable": has_ytdlp,
            "ffmpegAvailable": has_ffmpeg,
            "videoExtractionEnabled": can_extract_frames,
            "frameIntervalSeconds": float(args.frame_interval),
        },
        "videos": videos_out,
        "warnings": warnings,
    }

    output_manifest.write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    print(f"[done] manifest: {output_manifest} ({len(videos_out)} videos)")
    if warnings:
        print(f"[warn] {len(warnings)} warnings (see manifest.warnings)")
    return 0


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Build mapped video frames for Unity.")
    parser.add_argument("--csv", default="Assets/StreamingAssets/Data/videos_for_mapping.csv")
    parser.add_argument("--graph", default="Assets/StreamingAssets/Data/estate_graph.json")
    parser.add_argument("--locations", default="Assets/StreamingAssets/Data/locations.json")
    parser.add_argument("--overrides", default="Assets/StreamingAssets/Data/video_route_overrides.json")
    parser.add_argument("--output-manifest", default="Assets/StreamingAssets/Data/video_frame_map.json")
    parser.add_argument("--frames-dir", default="Assets/StreamingAssets/Data/video_frames")
    parser.add_argument("--work-dir", default=".tmp-video-map")
    parser.add_argument("--frame-interval", type=float, default=12.0)
    parser.add_argument("--limit", type=int, default=12)
    parser.add_argument("--thumbnail-only", action="store_true")
    parser.add_argument("--keep-video", action="store_true")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    return build_manifest(args)


if __name__ == "__main__":
    sys.exit(main())
