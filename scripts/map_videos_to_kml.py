#!/usr/bin/env python3
import argparse
import json
import math
import xml.etree.ElementTree as ET
from pathlib import Path

EARTH_RADIUS_M = 6378137.0
DEFAULT_ANCHOR_LAT = 1.294550851849307
DEFAULT_ANCHOR_LON = 103.8060771559821


def local_to_latlon(anchor_lat: float, anchor_lon: float, x: float, z: float) -> tuple[float, float]:
    d_lat = z / EARTH_RADIUS_M
    lat = math.degrees(d_lat) + anchor_lat
    d_lon = x / (EARTH_RADIUS_M * math.cos(math.radians(anchor_lat)))
    lon = math.degrees(d_lon) + anchor_lon
    return lat, lon


def resolve_path(project_root: Path, raw_path: str) -> Path:
    candidate = Path(raw_path).expanduser()
    if candidate.is_absolute():
        return candidate
    return (project_root / candidate).resolve()


def parse_args() -> argparse.Namespace:
    project_root_default = Path(__file__).resolve().parents[1]
    parser = argparse.ArgumentParser(description="Map video routes/frames to KML paths.")
    parser.add_argument("--project-root", default=str(project_root_default), help="Repo root (defaults to script parent repo)")
    parser.add_argument("--graph", default="Assets/StreamingAssets/Data/estate_graph.json", help="Path to graph JSON")
    parser.add_argument("--video-map", default="Assets/StreamingAssets/Data/video_frame_map.json", help="Path to video_frame_map JSON")
    parser.add_argument("--kml", default="cde2501.kml", help="Input KML path")
    parser.add_argument("--output-kml", default="", help="Output KML path (default: overwrite --kml)")
    parser.add_argument("--anchor-lat", type=float, default=DEFAULT_ANCHOR_LAT, help="Anchor latitude for local->geo conversion")
    parser.add_argument("--anchor-lon", type=float, default=DEFAULT_ANCHOR_LON, help="Anchor longitude for local->geo conversion")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    project_root = Path(args.project_root).expanduser().resolve()

    graph_path = resolve_path(project_root, args.graph)
    video_map_path = resolve_path(project_root, args.video_map)
    kml_path = resolve_path(project_root, args.kml)
    output_kml_path = resolve_path(project_root, args.output_kml) if args.output_kml else kml_path

    if not graph_path.exists():
        raise FileNotFoundError(f"Missing graph file: {graph_path}")
    if not video_map_path.exists():
        raise FileNotFoundError(f"Missing video map file: {video_map_path}")
    if not kml_path.exists():
        raise FileNotFoundError(f"Missing KML file: {kml_path}")

    graph = json.loads(graph_path.read_text(encoding="utf-8"))
    nodes = {n["id"]: n["position"] for n in graph.get("nodes", []) if isinstance(n, dict) and "id" in n and "position" in n}
    vmap = json.loads(video_map_path.read_text(encoding="utf-8"))

    ET.register_namespace("", "http://www.opengis.net/kml/2.2")
    ET.register_namespace("gx", "http://www.google.com/kml/ext/2.2")
    ET.register_namespace("kml", "http://www.opengis.net/kml/2.2")
    ET.register_namespace("atom", "http://www.w3.org/2005/Atom")

    tree = ET.parse(kml_path)
    root = tree.getroot()
    doc = root.find("{http://www.opengis.net/kml/2.2}Document")
    if doc is None:
        doc = root

    folder = ET.SubElement(doc, "{http://www.opengis.net/kml/2.2}Folder")
    name = ET.SubElement(folder, "{http://www.opengis.net/kml/2.2}name")
    name.text = "Mapped Videos"

    for video in vmap.get("videos", []):
        title = video.get("title", "Unknown Video")
        url = video.get("url", "")
        route_path = video.get("routeNodePath", [])

        coords: list[str] = []
        for node_id in route_path:
            if node_id not in nodes:
                continue
            pos = nodes[node_id]
            lat, lon = local_to_latlon(args.anchor_lat, args.anchor_lon, pos["x"], pos["z"])
            ele = pos.get("y", 0)
            coords.append(f"{lon},{lat},{ele}")

        if not coords:
            for frame in video.get("frames", []):
                pos = frame.get("position")
                if not pos:
                    continue
                lat, lon = local_to_latlon(args.anchor_lat, args.anchor_lon, pos["x"], pos["z"])
                ele = pos.get("y", 0)
                coords.append(f"{lon},{lat},{ele}")

        if not coords:
            continue

        pm = ET.SubElement(folder, "{http://www.opengis.net/kml/2.2}Placemark")
        pm_name = ET.SubElement(pm, "{http://www.opengis.net/kml/2.2}name")
        pm_name.text = title

        desc = ET.SubElement(pm, "{http://www.opengis.net/kml/2.2}description")
        desc.text = f'<a href="{url}">{url}</a>'

        style = ET.SubElement(pm, "{http://www.opengis.net/kml/2.2}Style")
        lstyle = ET.SubElement(style, "{http://www.opengis.net/kml/2.2}LineStyle")
        color = ET.SubElement(lstyle, "{http://www.opengis.net/kml/2.2}color")
        color.text = "ff0000ff"
        width = ET.SubElement(lstyle, "{http://www.opengis.net/kml/2.2}width")
        width.text = "4"

        line_string = ET.SubElement(pm, "{http://www.opengis.net/kml/2.2}LineString")
        alt_mode = ET.SubElement(line_string, "{http://www.opengis.net/kml/2.2}altitudeMode")
        alt_mode.text = "relativeToGround"
        tess = ET.SubElement(line_string, "{http://www.opengis.net/kml/2.2}tessellate")
        tess.text = "1"
        coord_el = ET.SubElement(line_string, "{http://www.opengis.net/kml/2.2}coordinates")
        coord_el.text = " ".join(coords)

    output_kml_path.parent.mkdir(parents=True, exist_ok=True)
    tree.write(output_kml_path, encoding="utf-8", xml_declaration=True)
    print(f"Successfully mapped videos to KML: {output_kml_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
