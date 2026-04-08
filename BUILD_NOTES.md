# Build Notes

## APK Size Reduction

Map tile PNGs have been moved to `Assets/StreamingAssets/Data/archived/` so they are
**not included in APK builds**. The minimap and routing work without them — graph edges
and route overlays are drawn directly.

### Archived files:
- `map_tile_z16_*.png` — low-res overview tile
- `queenstown_map_z18_*.png` / `z19_*.png` — Queenstown atlas tiles
- `nus_map_z18_*.png` / `z19_*.png` — NUS atlas tiles

These were used by `MapReferenceTileVisualizer` (ground-plane map) and `MiniMapOverlay`
(background image). Both degrade gracefully without them.

### To restore map tiles in the build (if needed later):
```bash
mv Assets/StreamingAssets/Data/archived/*.png Assets/StreamingAssets/Data/
mv Assets/StreamingAssets/Data/archived/*.meta Assets/StreamingAssets/Data/
```

## Build Command
```bash
python scripts/unity_cached_builder.py --force --target Android --output Builds/Android/CDE2501-Wayfinding.apk
```
