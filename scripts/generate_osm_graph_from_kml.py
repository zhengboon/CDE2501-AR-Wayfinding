#!/usr/bin/env python3
import argparse
import json
import math
import pathlib
import re
import time
import urllib.parse
import urllib.request
import xml.etree.ElementTree as ET
from typing import Dict, List, Optional, Sequence, Tuple

EARTH_RADIUS_M = 6378137.0
OVERPASS_URL = "https://overpass-api.de/api/interpreter"
KML_NS = {"kml": "http://www.opengis.net/kml/2.2"}

# Through-building corridor definitions for NUS Engineering.
# Each tuple: (building_A, building_B, sheltered, estimated_corridor_distance_m)
# Based on Faculty of Engineering campus layout — covered link bridges and corridors.
CORRIDOR_DEFINITIONS: List[Tuple[str, str, bool, float]] = [
    # EA wing <-> E1A <-> E2 chain (Level 2 link bridge)
    ("EA", "E1A", True, 35.0),
    ("EA", "E3A", True, 50.0),
    ("E1A", "E2", True, 30.0),
    ("E1A", "E1", True, 25.0),
    # E2 wing connections
    ("E2", "E2A", True, 25.0),
    ("E2", "LT1", True, 20.0),
    ("E2", "LT2", True, 15.0),
    ("E2", "E3", True, 30.0),
    # E3 cluster
    ("E3", "E3A", True, 45.0),
    ("E3", "LT1", True, 25.0),
    ("E3", "LT2", True, 20.0),
    ("E3", "T-Labs", True, 55.0),
    # E4 wing
    ("E4", "E4A", True, 30.0),
    ("E4", "LT6", True, 35.0),
    ("E4", "E5", True, 55.0),
    # E1 <-> E2A south path
    ("E1", "E2A", True, 30.0),
    ("E2A", "LT6", True, 40.0),
    # E5/E4A south links
    ("E4A", "E5", True, 40.0),
    ("E5", "IT(Bus Stop)", False, 50.0),
    ("E4A", "IT(Bus Stop)", False, 45.0),
    # E6 east wing
    ("E6", "E4", True, 55.0),
    ("E6", "E4A", True, 40.0),
    ("E6", "T-Labs", True, 60.0),
]


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


def parse_kml_polygon(kml_path: pathlib.Path, polygon_name: str) -> List[Tuple[float, float]]:
    root = ET.parse(kml_path).getroot()

    for placemark in root.findall(".//kml:Placemark", KML_NS):
        name_el = placemark.find("kml:name", KML_NS)
        name_text = (name_el.text.strip() if name_el is not None and name_el.text else "")
        if name_text != polygon_name:
            continue

        coords_el = placemark.find(
            ".//kml:Polygon//kml:outerBoundaryIs//kml:LinearRing//kml:coordinates",
            KML_NS,
        )
        if coords_el is None or not coords_el.text:
            return []

        points: List[Tuple[float, float]] = []
        for token in re.split(r"\s+", coords_el.text.strip()):
            parts = token.split(",")
            if len(parts) < 2:
                continue
            lon = parse_float(parts[0], default=float("nan"))
            lat = parse_float(parts[1], default=float("nan"))
            if math.isnan(lat) or math.isnan(lon):
                continue
            points.append((lat, lon))

        if len(points) >= 3 and points[0] == points[-1]:
            points = points[:-1]
        return points

    return []


def parse_kml_points(kml_path: pathlib.Path, polygon_name: str) -> List[Dict[str, float]]:
    root = ET.parse(kml_path).getroot()
    output: List[Dict[str, float]] = []

    for placemark in root.findall(".//kml:Placemark", KML_NS):
        name_el = placemark.find("kml:name", KML_NS)
        name_text = (name_el.text.strip() if name_el is not None and name_el.text else "")
        if not name_text or name_text == polygon_name:
            continue

        # Skip unnamed/untitled placemarks — they have no meaningful destination name.
        if name_text.lower().startswith("untitled"):
            continue

        point_el = placemark.find(".//kml:Point//kml:coordinates", KML_NS)
        if point_el is None or not point_el.text:
            continue

        token = point_el.text.strip().split()
        if not token:
            continue

        parts = token[0].split(",")
        if len(parts) < 2:
            continue

        lon = parse_float(parts[0], default=float("nan"))
        lat = parse_float(parts[1], default=float("nan"))
        if math.isnan(lat) or math.isnan(lon):
            continue

        output.append({"name": name_text, "lat": lat, "lon": lon})

    return output


def point_in_polygon(lat: float, lon: float, polygon_latlon: Sequence[Tuple[float, float]]) -> bool:
    if len(polygon_latlon) < 3:
        return False

    inside = False
    x = lon
    y = lat
    j = len(polygon_latlon) - 1

    for i in range(len(polygon_latlon)):
        yi, xi = polygon_latlon[i]
        yj, xj = polygon_latlon[j]
        intersects = ((yi > y) != (yj > y)) and (x < ((xj - xi) * (y - yi) / ((yj - yi) + 1e-12)) + xi)
        if intersects:
            inside = not inside
        j = i

    return inside


def polygon_centroid(polygon_latlon: Sequence[Tuple[float, float]]) -> Tuple[float, float]:
    if not polygon_latlon:
        return 0.0, 0.0
    lat = sum(p[0] for p in polygon_latlon) / len(polygon_latlon)
    lon = sum(p[1] for p in polygon_latlon) / len(polygon_latlon)
    return lat, lon


def polygon_bounds(polygon_latlon: Sequence[Tuple[float, float]]) -> Tuple[float, float, float, float]:
    lats = [p[0] for p in polygon_latlon]
    lons = [p[1] for p in polygon_latlon]
    return min(lats), min(lons), max(lats), max(lons)


def sanitize_id(text: str) -> str:
    t = (text or "").strip().upper()
    t = re.sub(r"[^A-Z0-9]+", "_", t)
    t = re.sub(r"_+", "_", t).strip("_")
    return t or "NODE"


def highway_defaults(highway: str) -> Tuple[float, float, float]:
    h = (highway or "").lower()
    if h in {"footway", "path", "pedestrian", "corridor", "steps"}:
        return 0.52, 0.25, 0.68
    if h in {"service", "living_street", "residential", "unclassified"}:
        return 0.72, 0.22, 0.74
    if h in {"primary", "secondary", "tertiary"}:
        return 0.86, 0.18, 0.76
    if h in {"cycleway", "track"}:
        return 0.62, 0.20, 0.68
    return 0.68, 0.24, 0.70


def normalize_width_level(tags: Dict[str, str], default_level: float) -> float:
    width_raw = tags.get("width") if tags else None
    if not width_raw:
        return default_level

    width_m = parse_float(str(width_raw).split(";")[0], default=-1.0)
    if width_m <= 0.0:
        return default_level

    normalized = 0.25 + ((width_m - 1.0) / 3.0) * 0.75
    return clamp(normalized, 0.25, 1.0)


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


def build_edge_attributes(tags: Dict[str, str], distance_m: float) -> Dict[str, float]:
    highway = (tags.get("highway", "") if tags else "").lower()
    width_default, clutter_default, lighting_default = highway_defaults(highway)
    width_level = normalize_width_level(tags, width_default)

    lit = (tags.get("lit", "") if tags else "").lower()
    if lit in {"yes", "true", "1"}:
        lighting = max(lighting_default, 0.82)
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


def overpass_query(min_lat: float, min_lon: float, max_lat: float, max_lon: float) -> Dict:
    query = f"""
[out:json][timeout:240];
(
  way["highway"]({min_lat},{min_lon},{max_lat},{max_lon});
);
(._;>;);
out body;
""".strip()

    data = urllib.parse.urlencode({"data": query}).encode("utf-8")
    req = urllib.request.Request(OVERPASS_URL, data=data, headers={"User-Agent": "CDE2501-AR-Wayfinding/1.0"})
    with urllib.request.urlopen(req, timeout=300) as resp:
        raw = resp.read().decode("utf-8")
    return json.loads(raw)


def write_geojson_boundary(path: pathlib.Path, polygon_latlon: Sequence[Tuple[float, float]], name: str) -> None:
    coords = [[lon, lat] for lat, lon in polygon_latlon]
    if coords and coords[0] != coords[-1]:
        coords.append(coords[0])

    geojson = {
        "type": "FeatureCollection",
        "features": [
            {
                "type": "Feature",
                "properties": {
                    "name": name,
                    "source": "Generated from KML polygon",
                },
                "geometry": {
                    "type": "Polygon",
                    "coordinates": [coords],
                },
            }
        ],
    }
    path.write_text(json.dumps(geojson, indent=2), encoding="utf-8")


def add_edge(edge_by_key: Dict[Tuple[str, str], Dict], src: str, dst: str, attrs: Dict) -> None:
    key = (src, dst)
    edge = {"fromNode": src, "toNode": dst, **attrs}
    existing = edge_by_key.get(key)
    if existing is None or edge["distance"] < existing["distance"]:
        edge_by_key[key] = edge


def main() -> int:
    parser = argparse.ArgumentParser(description="Generate area graph + locations from KML polygon and points.")
    parser.add_argument("--kml", type=pathlib.Path, required=True)
    parser.add_argument("--polygon-name", required=True)
    parser.add_argument("--graph-output", type=pathlib.Path, required=True)
    parser.add_argument("--locations-output", type=pathlib.Path, required=True)
    parser.add_argument("--boundary-output", type=pathlib.Path, required=True)
    parser.add_argument("--area-name", default="Area")
    parser.add_argument("--anchor-name", default="Anchor")
    parser.add_argument("--node-prefix", default="NUS")
    parser.add_argument("--bbox-padding", type=float, default=0.00020)
    parser.add_argument("--connector-k", type=int, default=3)
    parser.add_argument("--connector-max-distance-m", type=float, default=120.0)
    parser.add_argument("--synthetic-k", type=int, default=3)
    parser.add_argument("--synthetic-max-distance-m", type=float, default=220.0)
    parser.add_argument("--through-building-corridors", action="store_true", default=False,
                        help="Generate through-building corridor walkway edges using CORRIDOR_DEFINITIONS")
    parser.add_argument("--raw-cache", type=pathlib.Path, default=None)
    parser.add_argument("--use-cache", action="store_true", default=False,
                        help="Use cached OSM data from --raw-cache instead of querying Overpass API")
    args = parser.parse_args()

    if not args.kml.exists():
        print(f"[error] missing KML file: {args.kml}")
        return 1

    polygon = parse_kml_polygon(args.kml, args.polygon_name)
    if len(polygon) < 3:
        print(f"[error] polygon '{args.polygon_name}' not found or invalid in {args.kml}")
        return 1

    points = parse_kml_points(args.kml, args.polygon_name)
    if not points:
        print("[error] no point placemarks found in KML.")
        return 1

    min_lat, min_lon, max_lat, max_lon = polygon_bounds(polygon)
    min_lat -= args.bbox_padding
    min_lon -= args.bbox_padding
    max_lat += args.bbox_padding
    max_lon += args.bbox_padding

    anchor_lat, anchor_lon = polygon_centroid(polygon)

    # Load OSM data: use cache if available and --use-cache is set, otherwise query Overpass API.
    osm = None
    if args.use_cache and args.raw_cache is not None and args.raw_cache.exists():
        print(f"[info] Loading OSM data from cache: {args.raw_cache}")
        osm = json.loads(args.raw_cache.read_text(encoding="utf-8"))
    else:
        print(f"[info] Overpass bbox: [{min_lat}, {min_lon}, {max_lat}, {max_lon}]")
        osm = overpass_query(min_lat, min_lon, max_lat, max_lon)
        if args.raw_cache is not None:
            args.raw_cache.parent.mkdir(parents=True, exist_ok=True)
            args.raw_cache.write_text(json.dumps(osm, indent=2), encoding="utf-8")

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
        print("[error] no OSM roads returned.")
        return 1

    nodes_by_id: Dict[str, Dict] = {}
    edge_by_key: Dict[Tuple[str, str], Dict] = {}
    osm_node_ids: List[str] = []

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
            osm_node_ids.append(node_id)
        return node_id

    kept_segments = 0
    for way in ways:
        tags = way.get("tags", {})
        highway = (tags.get("highway", "") or "").lower()
        wn = way.get("nodes", [])
        if len(wn) < 2:
            continue

        for i in range(len(wn) - 1):
            a = wn[i]
            b = wn[i + 1]
            if a not in node_geo or b not in node_geo or a == b:
                continue

            lat1, lon1 = node_geo[a]
            lat2, lon2 = node_geo[b]
            mid_lat = (lat1 + lat2) * 0.5
            mid_lon = (lon1 + lon2) * 0.5

            if not (point_in_polygon(lat1, lon1, polygon) or point_in_polygon(lat2, lon2, polygon) or point_in_polygon(mid_lat, mid_lon, polygon)):
                continue

            from_id = ensure_osm_node(a, highway)
            to_id = ensure_osm_node(b, highway)

            distance = haversine_m(lat1, lon1, lat2, lon2)
            attrs = build_edge_attributes(tags, distance)
            add_edge(edge_by_key, from_id, to_id, attrs)
            add_edge(edge_by_key, to_id, from_id, attrs)
            kept_segments += 1

    if not edge_by_key:
        print("[error] no road edges survived polygon filter.")
        return 1

    # Build location nodes and JSON locations list.
    locations_json: List[Dict] = []
    location_node_ids: List[str] = []
    seen_ids: Dict[str, int] = {}

    for p in points:
        base = sanitize_id(p["name"])
        idx = seen_ids.get(base, 0)
        seen_ids[base] = idx + 1
        suffix = f"_{idx+1}" if idx > 0 else ""
        node_id = f"{args.node_prefix}_{base}{suffix}"

        x, z = latlon_to_local(anchor_lat, anchor_lon, p["lat"], p["lon"])
        nodes_by_id[node_id] = {
            "id": node_id,
            "position": {"x": round(x, 3), "y": 0.0, "z": round(z, 3)},
            "elevationLevel": 0,
            "hasStairs": False,
            "slopeLevel": 0.04,
            "lightingLevel": 0.78,
            "clutterLevel": 0.20,
            "widthLevel": 0.80,
            "sheltered": False,
        }
        location_node_ids.append(node_id)
        locations_json.append(
            {
                "name": p["name"],
                "type": "Campus",
                "gps_lat": p["lat"],
                "gps_lon": p["lon"],
                "indoor_node_id": node_id,
            }
        )

    # Connect each location to nearest OSM nodes.
    connector_count = 0
    for loc in locations_json:
        node_id = loc["indoor_node_id"]
        lat = float(loc["gps_lat"])
        lon = float(loc["gps_lon"])

        nearest: List[Tuple[float, str]] = []
        for osm_id in osm_node_ids:
            oid = int(osm_id[4:])
            nlat, nlon = node_geo.get(oid, (None, None))
            if nlat is None:
                continue
            d = haversine_m(lat, lon, nlat, nlon)
            nearest.append((d, osm_id))

        nearest.sort(key=lambda t: t[0])
        used = 0
        for d, osm_id in nearest:
            if used >= max(1, args.connector_k):
                break
            if d > args.connector_max_distance_m and used > 0:
                break

            attrs = {
                "distance": round(max(0.5, d), 3),
                "slope": 0.04,
                "hasStairs": False,
                "sheltered": False,
                "clutter": 0.20,
                "lighting": 0.80,
                "width": 0.80,
            }
            add_edge(edge_by_key, node_id, osm_id, attrs)
            add_edge(edge_by_key, osm_id, node_id, attrs)
            connector_count += 1
            used += 1

    # Synthetic cross-building links (assumption: internal campus links exist).
    synthetic_count = 0
    for i, a in enumerate(locations_json):
        pairs: List[Tuple[float, int]] = []
        for j, b in enumerate(locations_json):
            if i == j:
                continue
            d = haversine_m(float(a["gps_lat"]), float(a["gps_lon"]), float(b["gps_lat"]), float(b["gps_lon"]))
            pairs.append((d, j))
        pairs.sort(key=lambda t: t[0])

        linked = 0
        for d, j in pairs:
            if linked >= max(1, args.synthetic_k):
                break
            if d > args.synthetic_max_distance_m:
                break

            a_id = a["indoor_node_id"]
            b_id = locations_json[j]["indoor_node_id"]
            attrs = {
                "distance": round(max(0.5, d), 3),
                "slope": 0.05,
                "hasStairs": False,
                "sheltered": False,
                "clutter": 0.22,
                "lighting": 0.78,
                "width": 0.78,
            }
            add_edge(edge_by_key, a_id, b_id, attrs)
            add_edge(edge_by_key, b_id, a_id, attrs)
            synthetic_count += 1
            linked += 1

    # Through-building corridor edges (sheltered indoor paths between buildings).
    corridor_count = 0
    if args.through_building_corridors:
        # Build a name→node_id map from the KML points
        name_to_node: Dict[str, str] = {}
        name_to_geo: Dict[str, Tuple[float, float]] = {}
        for loc in locations_json:
            name_to_node[loc["name"]] = loc["indoor_node_id"]
            name_to_geo[loc["name"]] = (float(loc["gps_lat"]), float(loc["gps_lon"]))

        for bldg_a, bldg_b, sheltered, corridor_dist in CORRIDOR_DEFINITIONS:
            a_id = name_to_node.get(bldg_a)
            b_id = name_to_node.get(bldg_b)
            if a_id is None or b_id is None:
                continue
            if a_id not in nodes_by_id or b_id not in nodes_by_id:
                continue

            # Create a corridor junction node at the midpoint
            a_pos = nodes_by_id[a_id]["position"]
            b_pos = nodes_by_id[b_id]["position"]
            mid_x = round((a_pos["x"] + b_pos["x"]) * 0.5, 3)
            mid_z = round((a_pos["z"] + b_pos["z"]) * 0.5, 3)
            junction_id = f"{args.node_prefix}_COR_{sanitize_id(bldg_a)}_{sanitize_id(bldg_b)}"

            nodes_by_id[junction_id] = {
                "id": junction_id,
                "position": {"x": mid_x, "y": 0.0, "z": mid_z},
                "elevationLevel": 0,
                "hasStairs": False,
                "slopeLevel": 0.02,
                "lightingLevel": 0.85,
                "clutterLevel": 0.15,
                "widthLevel": 0.75,
                "sheltered": sheltered,
            }

            half_dist = round(max(0.5, corridor_dist * 0.5), 3)
            corridor_attrs = {
                "distance": half_dist,
                "slope": 0.02,
                "hasStairs": False,
                "sheltered": sheltered,
                "clutter": 0.15,
                "lighting": 0.85,
                "width": 0.75,
            }
            add_edge(edge_by_key, a_id, junction_id, corridor_attrs)
            add_edge(edge_by_key, junction_id, a_id, corridor_attrs)
            add_edge(edge_by_key, junction_id, b_id, corridor_attrs)
            add_edge(edge_by_key, b_id, junction_id, corridor_attrs)
            corridor_count += 1

    node_list = [nodes_by_id[k] for k in sorted(nodes_by_id.keys())]
    edge_list = [edge_by_key[k] for k in sorted(edge_by_key.keys())]

    boundary_name = args.boundary_output.name
    graph = {
        "metadata": {
            "estateName": args.area_name,
            "version": f"{args.node_prefix.lower()}-osm-roads-kml-{time.strftime('%Y%m%d')}",
            "anchorName": args.anchor_name,
            "anchorGps": {"lat": anchor_lat, "lon": anchor_lon},
            "source": {
                "waypoints": "KML point placemarks",
                "boundary": f"KML polygon: {args.polygon_name}",
                "roads": f"OpenStreetMap Overpass API ({time.strftime('%Y-%m-%d')})",
            },
            "areaBounds": {
                "description": f"Derived from {args.kml.name}:{args.polygon_name}",
                "minLat": min_lat,
                "maxLat": max_lat,
                "minLon": min_lon,
                "maxLon": max_lon,
                "boundaryGeoJson": boundary_name,
            },
            "roadGraph": {
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
                "syntheticLinks": synthetic_count,
                "corridorLinks": corridor_count,
                "keptRoadSegments": kept_segments,
            },
        },
        "nodes": node_list,
        "edges": edge_list,
    }

    args.graph_output.parent.mkdir(parents=True, exist_ok=True)
    args.locations_output.parent.mkdir(parents=True, exist_ok=True)
    args.boundary_output.parent.mkdir(parents=True, exist_ok=True)

    args.graph_output.write_text(json.dumps(graph, indent=2), encoding="utf-8")
    args.locations_output.write_text(json.dumps(locations_json, indent=2), encoding="utf-8")
    write_geojson_boundary(args.boundary_output, polygon, f"{args.area_name} Boundary")

    print(f"[ok] graph: {args.graph_output}")
    print(f"[ok] locations: {args.locations_output}")
    print(f"[ok] boundary: {args.boundary_output}")
    print(f"[ok] nodes={len(node_list)} edges={len(edge_list)} osmNodes={len(osm_node_ids)} connectors={connector_count} synthetic={synthetic_count} corridors={corridor_count}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
