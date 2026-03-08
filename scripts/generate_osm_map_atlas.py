#!/usr/bin/env python3
import argparse
import json
import math
import pathlib
import time
import urllib.request
from dataclasses import dataclass
from io import BytesIO
from typing import Tuple

try:
    from PIL import Image
except ImportError as exc:  # pragma: no cover
    raise SystemExit("Pillow is required: pip install pillow") from exc


TILE_SERVER = "https://tile.openstreetmap.org/{z}/{x}/{y}.png"
USER_AGENT = "CDE2501-AR-Wayfinding/1.0 (educational map atlas generator)"
TILE_SIZE = 256


@dataclass
class Bounds:
    min_lat: float
    min_lon: float
    max_lat: float
    max_lon: float


def clamp(v: float, lo: float, hi: float) -> float:
    return max(lo, min(hi, v))


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


def fetch_tile(x: int, y: int, z: int, retries: int = 3, sleep_seconds: float = 0.35) -> Image.Image:
    url = TILE_SERVER.format(z=z, x=x, y=y)
    headers = {"User-Agent": USER_AGENT}

    for attempt in range(1, retries + 1):
        req = urllib.request.Request(url, headers=headers)
        try:
            with urllib.request.urlopen(req, timeout=30) as response:
                payload = response.read()
            image = Image.open(BytesIO(payload)).convert("RGB")
            return image
        except Exception as exc:  # pragma: no cover
            if attempt >= retries:
                raise RuntimeError(f"Failed to download tile {z}/{x}/{y}: {exc}") from exc
            time.sleep(sleep_seconds * attempt)

    raise RuntimeError(f"Failed to download tile {z}/{x}/{y}")


def load_bounds_from_estate_graph(estate_graph_path: pathlib.Path) -> Bounds:
    data = json.loads(estate_graph_path.read_text(encoding="utf-8"))
    area = (data.get("metadata") or {}).get("areaBounds") or {}
    min_lat = float(area.get("minLat"))
    max_lat = float(area.get("maxLat"))
    min_lon = float(area.get("minLon"))
    max_lon = float(area.get("maxLon"))

    if max_lat < min_lat:
        min_lat, max_lat = max_lat, min_lat
    if max_lon < min_lon:
        min_lon, max_lon = max_lon, min_lon

    return Bounds(min_lat=min_lat, min_lon=min_lon, max_lat=max_lat, max_lon=max_lon)


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


def main() -> int:
    parser = argparse.ArgumentParser(description="Generate a high-resolution OSM map atlas for the estate bbox.")
    parser.add_argument("--zoom", type=int, default=18, help="OSM tile zoom level (default: 18)")
    parser.add_argument("--padding-tiles", type=int, default=1, help="Extra tiles around bbox (default: 1)")
    parser.add_argument("--no-square", action="store_true", help="Do not expand the atlas to square tiles")
    parser.add_argument(
        "--estate-graph",
        type=pathlib.Path,
        default=pathlib.Path("Assets/StreamingAssets/Data/estate_graph.json"),
        help="Path to estate_graph.json",
    )
    parser.add_argument(
        "--out-dir",
        type=pathlib.Path,
        default=pathlib.Path("Assets/StreamingAssets/Data"),
        help="Output data directory",
    )
    args = parser.parse_args()

    bounds = load_bounds_from_estate_graph(args.estate_graph)

    x1, y1 = latlon_to_tile(bounds.max_lat, bounds.min_lon, args.zoom)  # NW
    x2, y2 = latlon_to_tile(bounds.min_lat, bounds.max_lon, args.zoom)  # SE

    min_x = min(x1, x2) - max(0, args.padding_tiles)
    max_x = max(x1, x2) + max(0, args.padding_tiles)
    min_y = min(y1, y2) - max(0, args.padding_tiles)
    max_y = max(y1, y2) + max(0, args.padding_tiles)

    n = (2 ** args.zoom) - 1
    min_x = int(clamp(min_x, 0, n))
    max_x = int(clamp(max_x, 0, n))
    min_y = int(clamp(min_y, 0, n))
    max_y = int(clamp(max_y, 0, n))

    if not args.no_square:
        min_x, max_x, min_y, max_y = expand_to_square(min_x, max_x, min_y, max_y, args.zoom)

    tiles_x = max_x - min_x + 1
    tiles_y = max_y - min_y + 1

    atlas = Image.new("RGB", (tiles_x * TILE_SIZE, tiles_y * TILE_SIZE), color=(245, 245, 245))

    total = tiles_x * tiles_y
    done = 0
    for row, y in enumerate(range(min_y, max_y + 1)):
        for col, x in enumerate(range(min_x, max_x + 1)):
            tile = fetch_tile(x, y, args.zoom)
            atlas.paste(tile, (col * TILE_SIZE, row * TILE_SIZE))
            done += 1
            if done % 10 == 0 or done == total:
                print(f"Downloaded {done}/{total} tiles...")
            time.sleep(0.06)  # Keep request rate gentle.

    args.out_dir.mkdir(parents=True, exist_ok=True)

    base_name = f"queenstown_map_z{args.zoom}_x{min_x}-{max_x}_y{min_y}-{max_y}"
    image_path = args.out_dir / f"{base_name}.png"
    metadata_path = args.out_dir / f"{base_name}.json"

    atlas.save(image_path, format="PNG", optimize=True)

    north, west = tile_to_latlon(min_x, min_y, args.zoom)
    south, east = tile_to_latlon(max_x + 1, max_y + 1, args.zoom)

    metadata = {
        "version": 1,
        "source": "OpenStreetMap",
        "tileServer": "https://tile.openstreetmap.org/{z}/{x}/{y}.png",
        "attribution": "© OpenStreetMap contributors",
        "zoom": args.zoom,
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
    }

    metadata_path.write_text(json.dumps(metadata, indent=2), encoding="utf-8")

    print(f"Saved atlas image : {image_path}")
    print(f"Saved atlas meta  : {metadata_path}")
    print(f"Atlas size        : {atlas.width}x{atlas.height} px ({tiles_x}x{tiles_y} tiles)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
