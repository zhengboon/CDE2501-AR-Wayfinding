#!/usr/bin/env python3
import json
import math
import pathlib
import sys
import time
import urllib.parse
import urllib.request
from typing import Dict, List, Tuple

EARTH_RADIUS_M = 6378137.0
OVERPASS_URL = "https://overpass-api.de/api/interpreter"


def clamp(value: float, low: float, high: float) -> float:
    return max(low, min(high, value))


def parse_float(value, default: float = 0.0) -> float:
    try:
        return float(value)
    except (TypeError, ValueError):
        return default


def haversine_m(lat1: float, lon1: float, lat2: float, lon2: float) -> float:
    p1 = math.radians(lat1)
    p2 = math.radians(lat2)
    dp = p2 - p1
    dl = math.radians(lon2 - lon1)
    a = math.sin(dp * 0.5) ** 2 + math.cos(p1) * math.cos(p2) * (math.sin(dl * 0.5) ** 2)
    c = 2.0 * math.atan2(math.sqrt(a), math.sqrt(max(1e-12, 1.0 - a)))
    return EARTH_RADIUS_M * c


def latlon_to_local(anchor_lat: float, anchor_lon: float, lat: float, lon: float) -> Tuple[float, float]:
    d_lat = math.radians(lat - anchor_lat)
    d_lon = math.radians(lon - anchor_lon)
    x = EARTH_RADIUS_M * d_lon * math.cos(math.radians(anchor_lat))
    z = EARTH_RADIUS_M * d_lat
    return x, z


def highway_defaults(highway: str) -> Tuple[float, float, float]:
    # Returns (width_level, clutter_level, lighting_level)
    h = (highway or "").lower()
    if h in {"footway", "path", "pedestrian", "corridor", "steps"}:
        return 0.50, 0.28, 0.62
    if h in {"service", "living_street", "residential", "unclassified"}:
        return 0.72, 0.24, 0.70
    if h in {"primary", "secondary", "tertiary"}:
        return 0.90, 0.18, 0.74
    if h in {"cycleway", "track"}:
        return 0.58, 0.22, 0.62
    return 0.68, 0.25, 0.68


def normalize_width_level(tags: Dict[str, str], default_level: float) -> float:
    width_raw = tags.get("width") if tags else None
    if not width_raw:
        return default_level

    width_m = parse_float(str(width_raw).split(";")[0], default=-1.0)
    if width_m <= 0.0:
        return default_level

    # Normalize roughly: 1.0m -> 0.25, 4.0m -> 1.0
    normalized = 0.25 + ((width_m - 1.0) / 3.0) * 0.75
    return clamp(normalized, 0.25, 1.0)


def build_edge_attributes(tags: Dict[str, str], distance_m: float) -> Dict[str, float]:
    highway = (tags.get("highway", "") if tags else "").lower()
    width_default, clutter_default, lighting_default = highway_defaults(highway)
    width_level = normalize_width_level(tags, width_default)

    lit = (tags.get("lit", "") if tags else "").lower()
    if lit in {"yes", "true", "1"}:
        lighting = max(lighting_default, 0.8)
    elif lit in {"no", "false", "0"}:
        lighting = min(lighting_default, 0.45)
    else:
        lighting = lighting_default

    sheltered = (tags.get("covered", "") if tags else "").lower() in {"yes", "true", "1"}

    return {
        "distance": round(max(0.5, distance_m), 3),
        "slope": 0.05,
        "hasStairs": highway == "steps",
        "sheltered": sheltered,
        "clutter": round(clamp(clutter_default, 0.0, 1.0), 3),
        "lighting": round(clamp(lighting, 0.0, 1.0), 3),
        "width": round(clamp(width_level, 0.0, 1.0), 3),
    }


def build_node_attributes(highway_hint: str = "") -> Dict[str, float]:
    width_default, clutter_default, lighting_default = highway_defaults(highway_hint)
    return {
        "elevationLevel": 0,
        "hasStairs": highway_hint == "steps",
        "slopeLevel": 0.05,
        "lightingLevel": round(clamp(lighting_default, 0.0, 1.0), 3),
        "clutterLevel": round(clamp(clutter_default, 0.0, 1.0), 3),
        "widthLevel": round(clamp(width_default, 0.0, 1.0), 3),
        "sheltered": False,
    }


def overpass_query(min_lat: float, min_lon: float, max_lat: float, max_lon: float) -> Dict:
    query = f"""
[out:json][timeout:180];
(
  way["highway"]({min_lat},{min_lon},{max_lat},{max_lon});
);
(._;>;);
out body;
""".strip()

    data = urllib.parse.urlencode({"data": query}).encode("utf-8")
    req = urllib.request.Request(OVERPASS_URL, data=data, headers={"User-Agent": "CDE2501-AR-Wayfinding/1.0"})
    with urllib.request.urlopen(req, timeout=240) as resp:
        raw = resp.read().decode("utf-8")
    return json.loads(raw)


def main() -> int:
    project_root = pathlib.Path(__file__).resolve().parents[1]
    data_dir = project_root / "Assets" / "StreamingAssets" / "Data"
    estate_graph_path = data_dir / "estate_graph.json"
    locations_path = data_dir / "locations.json"
    overpass_cache_path = project_root / "Docs" / "queenstown_osm_raw.json"

    estate_graph = json.loads(estate_graph_path.read_text(encoding="utf-8"))
    locations = json.loads(locations_path.read_text(encoding="utf-8"))

    metadata = estate_graph.get("metadata", {})
    area = metadata.get("areaBounds", {})

    min_lat = parse_float(area.get("minLat"), None)
    max_lat = parse_float(area.get("maxLat"), None)
    min_lon = parse_float(area.get("minLon"), None)
    max_lon = parse_float(area.get("maxLon"), None)
    if None in {min_lat, max_lat, min_lon, max_lon}:
        print("Missing areaBounds in estate_graph metadata.", file=sys.stderr)
        return 1

    if max_lat < min_lat:
        min_lat, max_lat = max_lat, min_lat
    if max_lon < min_lon:
        min_lon, max_lon = max_lon, min_lon

    anchor = metadata.get("anchorGps", {})
    anchor_lat = parse_float(anchor.get("lat"), (min_lat + max_lat) * 0.5)
    anchor_lon = parse_float(anchor.get("lon"), (min_lon + max_lon) * 0.5)

    print(f"Fetching OSM highways in bbox: [{min_lat}, {min_lon}, {max_lat}, {max_lon}]")
    osm = overpass_query(min_lat, min_lon, max_lat, max_lon)
    overpass_cache_path.parent.mkdir(parents=True, exist_ok=True)
    overpass_cache_path.write_text(json.dumps(osm, indent=2), encoding="utf-8")

    elements = osm.get("elements", [])
    node_geo: Dict[int, Tuple[float, float]] = {}
    ways: List[Dict] = []

    for element in elements:
        if element.get("type") == "node":
            node_geo[element["id"]] = (parse_float(element.get("lat")), parse_float(element.get("lon")))
        elif element.get("type") == "way":
            tags = element.get("tags", {})
            if "highway" not in tags:
                continue
            ways.append(element)

    if not ways or not node_geo:
        print("No OSM roads returned for bbox.", file=sys.stderr)
        return 1

    nodes_by_id: Dict[str, Dict] = {}
    edge_by_key: Dict[Tuple[str, str], Dict] = {}

    def ensure_osm_node(osm_id: int, highway_hint: str) -> str:
        lat, lon = node_geo[osm_id]
        node_id = f"OSM_{osm_id}"
        if node_id not in nodes_by_id:
            x, z = latlon_to_local(anchor_lat, anchor_lon, lat, lon)
            attrs = build_node_attributes(highway_hint)
            nodes_by_id[node_id] = {
                "id": node_id,
                "position": {"x": round(x, 3), "y": 0.0, "z": round(z, 3)},
                **attrs,
            }
        return node_id

    for way in ways:
        tags = way.get("tags", {})
        highway = (tags.get("highway", "") or "").lower()
        way_nodes = way.get("nodes", [])
        if len(way_nodes) < 2:
            continue

        for i in range(len(way_nodes) - 1):
            a = way_nodes[i]
            b = way_nodes[i + 1]
            if a not in node_geo or b not in node_geo or a == b:
                continue

            from_id = ensure_osm_node(a, highway)
            to_id = ensure_osm_node(b, highway)

            lat1, lon1 = node_geo[a]
            lat2, lon2 = node_geo[b]
            distance = haversine_m(lat1, lon1, lat2, lon2)
            edge_attrs = build_edge_attributes(tags, distance)

            for src, dst in ((from_id, to_id), (to_id, from_id)):
                key = (src, dst)
                edge = {
                    "fromNode": src,
                    "toNode": dst,
                    **edge_attrs,
                }
                existing = edge_by_key.get(key)
                if existing is None or edge["distance"] < existing["distance"]:
                    edge_by_key[key] = edge

    # Keep current destination node IDs and connect them to nearest road nodes.
    osm_node_ids = [nid for nid in nodes_by_id.keys() if nid.startswith("OSM_")]

    def nearest_osm_node(lat: float, lon: float) -> Tuple[str, float]:
        best_id = ""
        best_d = float("inf")
        for node_id in osm_node_ids:
            osm_numeric = int(node_id[4:])
            nlat, nlon = node_geo.get(osm_numeric, (None, None))
            if nlat is None:
                continue
            d = haversine_m(lat, lon, nlat, nlon)
            if d < best_d:
                best_d = d
                best_id = node_id
        return best_id, best_d

    connector_count = 0
    for location in locations:
        node_id = (location.get("indoor_node_id") or "").strip()
        lat = parse_float(location.get("gps_lat"), float("nan"))
        lon = parse_float(location.get("gps_lon"), float("nan"))
        if not node_id or math.isnan(lat) or math.isnan(lon):
            continue

        x, z = latlon_to_local(anchor_lat, anchor_lon, lat, lon)
        nodes_by_id[node_id] = {
            "id": node_id,
            "position": {"x": round(x, 3), "y": 0.0, "z": round(z, 3)},
            "elevationLevel": 0,
            "hasStairs": False,
            "slopeLevel": 0.05,
            "lightingLevel": 0.72,
            "clutterLevel": 0.22,
            "widthLevel": 0.72,
            "sheltered": False,
        }

        nearest_id, nearest_distance = nearest_osm_node(lat, lon)
        if not nearest_id or nearest_distance > 200.0:
            continue

        connector_distance = max(0.5, nearest_distance)
        connector_edge = {
            "distance": round(connector_distance, 3),
            "slope": 0.04,
            "hasStairs": False,
            "sheltered": False,
            "clutter": 0.2,
            "lighting": 0.72,
            "width": 0.72,
        }

        for src, dst in ((node_id, nearest_id), (nearest_id, node_id)):
            key = (src, dst)
            edge_by_key[key] = {
                "fromNode": src,
                "toNode": dst,
                **connector_edge,
            }
        connector_count += 1

    node_list = [nodes_by_id[nid] for nid in sorted(nodes_by_id.keys())]
    edge_list = [edge_by_key[key] for key in sorted(edge_by_key.keys())]

    metadata.setdefault("source", {})
    metadata["source"]["roads"] = f"OpenStreetMap Overpass API ({time.strftime('%Y-%m-%d')})"
    metadata["version"] = "3.2-queenstown-osm-roads-bbox"
    metadata["roadGraph"] = {
        "bbox": {
            "minLat": min_lat,
            "maxLat": max_lat,
            "minLon": min_lon,
            "maxLon": max_lon,
        },
        "osmRoadNodes": len(osm_node_ids),
        "totalNodes": len(node_list),
        "totalEdges": len(edge_list),
        "locationConnectors": connector_count,
    }

    estate_graph["nodes"] = node_list
    estate_graph["edges"] = edge_list
    estate_graph["metadata"] = metadata

    estate_graph_path.write_text(json.dumps(estate_graph, indent=2), encoding="utf-8")
    print(f"Wrote {estate_graph_path}")
    print(f"Nodes: {len(node_list)} | Edges: {len(edge_list)} | Connectors: {connector_count}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
