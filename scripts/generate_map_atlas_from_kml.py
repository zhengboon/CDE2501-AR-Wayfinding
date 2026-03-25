#!/usr/bin/env python3
import argparse
import json
import math
import os
import pathlib
import time
import urllib.parse
import urllib.request
import xml.etree.ElementTree as ET
from dataclasses import dataclass
from io import BytesIO
from typing import Dict, List, Optional, Sequence, Tuple

try:
    from PIL import Image
except ImportError as exc:  # pragma: no cover
    raise SystemExit("Pillow is required: pip install pillow") from exc


KML_NS = {"kml": "http://www.opengis.net/kml/2.2"}
OSM_TILE_SERVER = "https://tile.openstreetmap.org/{z}/{x}/{y}.png"
GOOGLE_TILE_SERVER = "https://tile.googleapis.com/v1/2dtiles/{z}/{x}/{y}?session={session}&key={key}"
GOOGLE_CREATE_SESSION = "https://tile.googleapis.com/v1/createSession?key={key}"
USER_AGENT = "CDE2501-AR-Wayfinding/1.0 (educational map atlas generator)"
TILE_SIZE = 256


@dataclass
class Bounds:
    min_lat: float
    min_lon: float
    max_lat: float
    max_lon: float


@dataclass
class GoogleSession:
    token: str
    image_format: str
    expiry: str


def clamp(v: float, lo: float, hi: float) -> float:
    return max(lo, min(hi, v))


def normalize_spaces(value: str) -> str:
    return " ".join((value or "").strip().split())


def parse_kml_polygon(kml_path: pathlib.Path, polygon_name: str) -> List[Tuple[float, float]]:
    tree = ET.parse(kml_path)
    root = tree.getroot()

    wanted = normalize_spaces(polygon_name)
    for placemark in root.findall(".//kml:Placemark", KML_NS):
        name_el = placemark.find("kml:name", KML_NS)
        if name_el is None:
            continue
        if normalize_spaces(name_el.text or "") != wanted:
            continue

        coords_el = placemark.find(".//kml:Polygon//kml:outerBoundaryIs//kml:LinearRing//kml:coordinates", KML_NS)
        if coords_el is None or not (coords_el.text or "").strip():
            continue

        points: List[Tuple[float, float]] = []
        for token in (coords_el.text or "").replace("\n", " ").split():
            parts = token.split(",")
            if len(parts) < 2:
                continue
            lon = float(parts[0])
            lat = float(parts[1])
            points.append((lon, lat))

        if len(points) >= 3:
            if points[0] == points[-1]:
                points = points[:-1]
            return points

    return []


def bounds_from_polygon(polygon_lon_lat: Sequence[Tuple[float, float]]) -> Bounds:
    if len(polygon_lon_lat) < 3:
        raise ValueError("Polygon must contain at least 3 points")

    min_lon = min(lon for lon, _ in polygon_lon_lat)
    max_lon = max(lon for lon, _ in polygon_lon_lat)
    min_lat = min(lat for _, lat in polygon_lon_lat)
    max_lat = max(lat for _, lat in polygon_lon_lat)
    return Bounds(min_lat=min_lat, min_lon=min_lon, max_lat=max_lat, max_lon=max_lon)


def latlon_to_tile(lat: float, lon: float, zoom: int) -> Tuple[int, int]:
    lat = clamp(lat, -85.05112878, 85.05112878)
    lon = clamp(lon, -180.0, 180.0)
    lat_rad = math.radians(lat)
    n = 2 ** zoom
    x = int((lon + 180.0) / 360.0 * n)
    y = int((1.0 - math.log(math.tan(lat_rad) + (1.0 / math.cos(lat_rad))) / math.pi) * 0.5 * n)
    x = int(clamp(x, 0, n - 1))
    y = int(clamp(y, 0, n - 1))
    return x, y


def tile_to_latlon(tile_x: int, tile_y: int, zoom: int) -> Tuple[float, float]:
    n = 2.0 ** zoom
    lon = tile_x / n * 360.0 - 180.0
    lat_rad = math.atan(math.sinh(math.pi * (1.0 - (2.0 * tile_y / n))))
    lat = math.degrees(lat_rad)
    return lat, lon


def expand_to_square(min_x: int, max_x: int, min_y: int, max_y: int, zoom: int) -> Tuple[int, int, int, int]:
    n = (2 ** zoom) - 1
    width = (max_x - min_x + 1)
    height = (max_y - min_y + 1)

    while width < height:
        if min_x > 0:
            min_x -= 1
            width += 1
        if width >= height:
            break
        if max_x < n:
            max_x += 1
            width += 1
        else:
            break

    while height < width:
        if min_y > 0:
            min_y -= 1
            height += 1
        if height >= width:
            break
        if max_y < n:
            max_y += 1
            height += 1
        else:
            break

    return min_x, max_x, min_y, max_y


def fetch_image(url: str, retries: int, timeout_seconds: int, sleep_seconds: float) -> Image.Image:
    headers = {"User-Agent": USER_AGENT}

    for attempt in range(1, retries + 1):
        request = urllib.request.Request(url, headers=headers)
        try:
            with urllib.request.urlopen(request, timeout=timeout_seconds) as response:
                payload = response.read()
            return Image.open(BytesIO(payload)).convert("RGB")
        except Exception as exc:  # pragma: no cover
            if attempt >= retries:
                raise RuntimeError(f"Failed to fetch tile URL {url}: {exc}") from exc
            time.sleep(sleep_seconds * attempt)

    raise RuntimeError(f"Failed to fetch tile URL {url}")


def fetch_osm_tile(x: int, y: int, z: int, retries: int, timeout_seconds: int, sleep_seconds: float) -> Image.Image:
    url = OSM_TILE_SERVER.format(z=z, x=x, y=y)
    return fetch_image(url, retries=retries, timeout_seconds=timeout_seconds, sleep_seconds=sleep_seconds)


def create_google_session(api_key: str, map_type: str, language: str, region: str, image_format: str, timeout_seconds: int) -> GoogleSession:
    payload = {
        "mapType": map_type,
        "language": language,
        "region": region,
        "imageFormat": image_format,
    }

    data = json.dumps(payload).encode("utf-8")
    url = GOOGLE_CREATE_SESSION.format(key=urllib.parse.quote(api_key, safe=""))
    request = urllib.request.Request(
        url,
        method="POST",
        data=data,
        headers={
            "Content-Type": "application/json",
            "User-Agent": USER_AGENT,
        },
    )

    with urllib.request.urlopen(request, timeout=timeout_seconds) as response:
        body = json.loads(response.read().decode("utf-8"))

    token = str(body.get("session", "")).strip()
    if not token:
        raise RuntimeError("Google createSession response did not include a session token")

    return GoogleSession(
        token=token,
        image_format=str(body.get("imageFormat", image_format)).strip() or image_format,
        expiry=str(body.get("expiry", "")).strip(),
    )


def fetch_google_tile(
    x: int,
    y: int,
    z: int,
    api_key: str,
    google_session: GoogleSession,
    retries: int,
    timeout_seconds: int,
    sleep_seconds: float,
) -> Image.Image:
    url = GOOGLE_TILE_SERVER.format(
        z=z,
        x=x,
        y=y,
        session=urllib.parse.quote(google_session.token, safe=""),
        key=urllib.parse.quote(api_key, safe=""),
    )
    return fetch_image(url, retries=retries, timeout_seconds=timeout_seconds, sleep_seconds=sleep_seconds)


def parse_zoom_levels(raw: str) -> List[int]:
    levels: List[int] = []
    for part in (raw or "").split(","):
        part = part.strip()
        if not part:
            continue
        zoom = int(part)
        if zoom < 0 or zoom > 22:
            raise ValueError(f"Zoom out of range: {zoom}")
        levels.append(zoom)

    if not levels:
        raise ValueError("No zoom levels supplied")

    return sorted(set(levels))


def build_tile_range(bounds: Bounds, zoom: int, padding_tiles: int, square: bool) -> Tuple[int, int, int, int]:
    x1, y1 = latlon_to_tile(bounds.max_lat, bounds.min_lon, zoom)  # NW
    x2, y2 = latlon_to_tile(bounds.min_lat, bounds.max_lon, zoom)  # SE

    min_x = min(x1, x2) - max(0, padding_tiles)
    max_x = max(x1, x2) + max(0, padding_tiles)
    min_y = min(y1, y2) - max(0, padding_tiles)
    max_y = max(y1, y2) + max(0, padding_tiles)

    n = (2 ** zoom) - 1
    min_x = int(clamp(min_x, 0, n))
    max_x = int(clamp(max_x, 0, n))
    min_y = int(clamp(min_y, 0, n))
    max_y = int(clamp(max_y, 0, n))

    if square:
        min_x, max_x, min_y, max_y = expand_to_square(min_x, max_x, min_y, max_y, zoom)

    return min_x, max_x, min_y, max_y


def main() -> int:
    parser = argparse.ArgumentParser(description="Generate map atlas images from a KML polygon (Google Map Tiles API or OSM fallback).")
    parser.add_argument("--kml", type=pathlib.Path, default=pathlib.Path("CDE2501 NUS map.kml"), help="Path to KML file")
    parser.add_argument("--polygon-name", default="Map of Engine", help="Polygon placemark name in KML")
    parser.add_argument("--zoom-levels", default="18,19", help="Comma-separated zoom levels (default: 18,19)")
    parser.add_argument("--padding-tiles", type=int, default=1, help="Extra tiles around bbox")
    parser.add_argument("--no-square", action="store_true", help="Do not expand atlas to square tiles")
    parser.add_argument("--provider", choices=["google", "osm"], default="google", help="Tile provider")
    parser.add_argument("--google-api-key", default="", help="Google Maps API key (or use GOOGLE_MAPS_API_KEY env var)")
    parser.add_argument("--google-map-type", choices=["roadmap", "satellite", "terrain"], default="roadmap", help="Google map type")
    parser.add_argument("--google-language", default="en-US", help="Google language")
    parser.add_argument("--google-region", default="SG", help="Google region")
    parser.add_argument("--google-image-format", choices=["png", "jpeg"], default="png", help="Google image format")
    parser.add_argument("--fallback-to-osm", action="store_true", help="Fallback to OSM if Google session setup fails")
    parser.add_argument("--retries", type=int, default=3, help="Retries per tile")
    parser.add_argument("--timeout-seconds", type=int, default=30, help="HTTP timeout")
    parser.add_argument("--request-delay", type=float, default=0.06, help="Delay between tile requests")
    parser.add_argument("--out-dir", type=pathlib.Path, default=pathlib.Path("Assets/StreamingAssets/Data"), help="Output directory")
    parser.add_argument("--output-prefix", default="nus_map", help="Output file prefix")
    args = parser.parse_args()

    if not args.kml.exists():
        raise SystemExit(f"Missing KML file: {args.kml}")

    polygon = parse_kml_polygon(args.kml, args.polygon_name)
    if len(polygon) < 3:
        raise SystemExit(f"Polygon '{args.polygon_name}' not found or invalid in {args.kml}")

    bounds = bounds_from_polygon(polygon)
    zoom_levels = parse_zoom_levels(args.zoom_levels)

    provider = args.provider
    google_key = (args.google_api_key or "").strip() or (os.environ.get("GOOGLE_MAPS_API_KEY", "").strip())
    google_session: Optional[GoogleSession] = None

    source_name = "OpenStreetMap"
    attribution = "© OpenStreetMap contributors"
    tile_server = OSM_TILE_SERVER

    if provider == "google":
        if not google_key:
            if args.fallback_to_osm:
                print("[warn] GOOGLE_MAPS_API_KEY not provided. Falling back to OpenStreetMap.")
                provider = "osm"
            else:
                raise SystemExit("Provider is google but no API key was provided.")
        else:
            try:
                google_session = create_google_session(
                    api_key=google_key,
                    map_type=args.google_map_type,
                    language=args.google_language,
                    region=args.google_region,
                    image_format=args.google_image_format,
                    timeout_seconds=max(5, args.timeout_seconds),
                )
                source_name = "Google Maps Map Tiles API"
                attribution = "Map tiles © Google"
                tile_server = "https://tile.googleapis.com/v1/2dtiles/{z}/{x}/{y}"
                print(f"[info] Google session acquired; expiry={google_session.expiry or 'n/a'}")
            except Exception as exc:
                if args.fallback_to_osm:
                    print(f"[warn] Google session setup failed ({exc}). Falling back to OpenStreetMap.")
                    provider = "osm"
                    google_session = None
                else:
                    raise

    args.out_dir.mkdir(parents=True, exist_ok=True)

    for zoom in zoom_levels:
        min_x, max_x, min_y, max_y = build_tile_range(
            bounds=bounds,
            zoom=zoom,
            padding_tiles=args.padding_tiles,
            square=not args.no_square,
        )

        tiles_x = max_x - min_x + 1
        tiles_y = max_y - min_y + 1
        atlas = Image.new("RGB", (tiles_x * TILE_SIZE, tiles_y * TILE_SIZE), color=(245, 245, 245))

        total = tiles_x * tiles_y
        done = 0

        for row, y in enumerate(range(min_y, max_y + 1)):
            for col, x in enumerate(range(min_x, max_x + 1)):
                if provider == "google":
                    tile = fetch_google_tile(
                        x=x,
                        y=y,
                        z=zoom,
                        api_key=google_key,
                        google_session=google_session,
                        retries=max(1, args.retries),
                        timeout_seconds=max(5, args.timeout_seconds),
                        sleep_seconds=max(0.05, args.request_delay),
                    )
                else:
                    tile = fetch_osm_tile(
                        x=x,
                        y=y,
                        z=zoom,
                        retries=max(1, args.retries),
                        timeout_seconds=max(5, args.timeout_seconds),
                        sleep_seconds=max(0.05, args.request_delay),
                    )

                atlas.paste(tile, (col * TILE_SIZE, row * TILE_SIZE))
                done += 1
                if done % 10 == 0 or done == total:
                    print(f"[z{zoom}] Downloaded {done}/{total} tiles...")

                time.sleep(max(0.0, args.request_delay))

        base_name = f"{args.output_prefix}_z{zoom}_x{min_x}-{max_x}_y{min_y}-{max_y}"
        image_path = args.out_dir / f"{base_name}.png"
        metadata_path = args.out_dir / f"{base_name}.json"

        atlas.save(image_path, format="PNG", optimize=True)

        north, west = tile_to_latlon(min_x, min_y, zoom)
        south, east = tile_to_latlon(max_x + 1, max_y + 1, zoom)

        metadata: Dict[str, object] = {
            "version": 1,
            "source": source_name,
            "tileServer": tile_server,
            "attribution": attribution,
            "zoom": zoom,
            "minTileX": min_x,
            "maxTileX": max_x,
            "minTileY": min_y,
            "maxTileY": max_y,
            "tileSize": TILE_SIZE,
            "tilesX": tiles_x,
            "tilesY": tiles_y,
            "imageWidth": atlas.width,
            "imageHeight": atlas.height,
            "geoBounds": {
                "north": north,
                "south": south,
                "west": west,
                "east": east,
                "minLat": min(south, north),
                "maxLat": max(south, north),
                "minLon": min(west, east),
                "maxLon": max(west, east),
            },
            "requestedBounds": {
                "minLat": bounds.min_lat,
                "maxLat": bounds.max_lat,
                "minLon": bounds.min_lon,
                "maxLon": bounds.max_lon,
            },
            "kml": {
                "path": str(args.kml).replace("\\", "/"),
                "polygonName": args.polygon_name,
                "pointCount": len(polygon),
            },
            "provider": {
                "name": provider,
                "googleMapType": args.google_map_type if provider == "google" else "",
                "googleLanguage": args.google_language if provider == "google" else "",
                "googleRegion": args.google_region if provider == "google" else "",
                "googleImageFormat": google_session.image_format if provider == "google" and google_session else "",
                "googleSessionExpiry": google_session.expiry if provider == "google" and google_session else "",
            },
        }

        metadata_path.write_text(json.dumps(metadata, indent=2), encoding="utf-8")

        print(f"Saved atlas image : {image_path}")
        print(f"Saved atlas meta  : {metadata_path}")
        print(f"Atlas size        : {atlas.width}x{atlas.height} px ({tiles_x}x{tiles_y} tiles)")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
