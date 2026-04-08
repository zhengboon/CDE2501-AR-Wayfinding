# Build Notes

## APK Size Reduction

Before building the Android APK, move map tile PNGs out of `Assets/StreamingAssets/Data/`
to reduce APK size. The minimap and routing work without them (graph edges are drawn directly).

### Quick strip before build:
```bash
mkdir -p .build-excluded
mv Assets/StreamingAssets/Data/*.png .build-excluded/
mv Assets/StreamingAssets/Data/*.png.meta .build-excluded/
```

### Restore after build:
```bash
mv .build-excluded/*.png Assets/StreamingAssets/Data/
mv .build-excluded/*.meta Assets/StreamingAssets/Data/
```

### Files excluded:
- `map_tile_z16_*.png` — low-res overview tile
- `queenstown_map_z18_*.png` / `z19_*.png` — Queenstown atlas tiles
- `nus_map_z18_*.png` / `z19_*.png` — NUS atlas tiles

These are only used by `MapReferenceTileVisualizer` (ground-plane map) and `MiniMapOverlay`
(background image). Both degrade gracefully without them — the minimap shows graph edges
and route overlay without the map image background.

## Build Command
```bash
python scripts/unity_cached_builder.py --force --target Android --output Builds/Android/CDE2501-Wayfinding.apk
```
