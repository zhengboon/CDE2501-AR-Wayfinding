CDE2501 AR Wayfinding - Progress and Plan
Last updated: 2026-03-09

Overview
- Unity project for elderly-first AR wayfinding (Android ARCore + iOS ARKit via AR Foundation).
- Includes outdoor GPS/compass direction guidance, indoor graph routing, simulation mode, and route visualization.

Current Progress
1) Core Routing and Data
- Indoor graph routing is implemented with weighted A*.
- Graph data is loaded from JSON in StreamingAssets.
- Routing profiles and rain/wheelchair behavior are supported via JSON/config.

2) Route Visualization
- Route line rendering is active in Scene/Game.
- Path was updated to anchor from the player/current camera position for clearer “from me to destination” behavior.
- Baritone-inspired segmentation and styling are in place.

3) Minimap and Map Reference (Major Upgrade Done)
- Added high-resolution Queenstown map atlas support (not just a single 256x256 tile).
- Generated atlas:
  - Assets/StreamingAssets/Data/queenstown_map_z18_x206656-206662_y130126-130132.png
  - Assets/StreamingAssets/Data/queenstown_map_z18_x206656-206662_y130126-130132.json
- Minimap now reads atlas metadata (tile ranges + geo bounds) for better alignment and scaling.
- Minimap/map textures now use bilinear filtering and clamped wrapping for smoother visuals.

4) Tooling
- Added map atlas generator script:
  - scripts/generate_osm_map_atlas.py
- This script fetches OSM tiles for estate bounds and stitches a high-resolution atlas.

5) Existing Simulation Workflow
- PC simulation controls are available for testing route behavior without a supported AR phone.
- Destination cycling and recalculation are available through UI/keys in the quick-start flow.

Files Recently Updated
- Assets/Scripts/Routing/RoutePathVisualizer.cs
- Assets/Scripts/UI/MiniMapOverlay.cs
- Assets/Scripts/UI/MapReferenceTileVisualizer.cs
- scripts/generate_osm_map_atlas.py
- Assets/StreamingAssets/Data/queenstown_map_z18_x206656-206662_y130126-130132.png
- Assets/StreamingAssets/Data/queenstown_map_z18_x206656-206662_y130126-130132.json

Known Gaps / Notes
- Unity compile/runtime verification is still required in Editor/device.
- This shell cannot run Unity Editor, so Console validation must be done on your machine.
- scripts/__pycache__/ was generated locally and can be ignored/cleaned before commit if desired.

Immediate Verification Checklist (Unity)
1) Open project and let scripts/assets reimport.
2) Check Console for compile errors (must be 0 errors).
3) Enter Play mode:
   - Minimap should be sharper and less clunky.
   - Drag/zoom/minimap controls should work.
   - Route should visibly start from your current position.
4) Switch destinations and confirm route updates continuously.

Future Plans
Priority 1 - Stability and Correctness
1) Run full Unity compile cleanup and resolve any remaining null-reference edge cases.
2) Add explicit on-screen debug values for: current start node, selected destination node, route recompute reason.
3) Add route recalculation hysteresis tuning to reduce jitter while still staying responsive.

Priority 2 - Map Quality and UX
1) Increase atlas resolution to z19 where needed:
   - ./scripts/generate_osm_map_atlas.py --zoom 19 --padding-tiles 1
2) Add optional tiled loading/chunking for larger areas to keep memory stable.
3) Improve minimap UX:
   - Better marker labels
   - Cleaner hover tooltip behavior
   - Optional lock north / follow heading mode

Priority 3 - Real Estate Data Accuracy
1) Validate and refine waypoints/nodes against field-verified paths.
2) Add explicit elevation connectors (stairs/lifts/ramps) with safety attributes.
3) Tune profile weights (elderly/wheelchair/rain) from field trial logs.

Priority 4 - Device Validation
1) Test on ARCore-capable Android device (permissions + tracking + route loop).
2) Test on ARKit iOS device (Info.plist entries + tracking + route loop).
3) Record battery, thermal behavior, and update recalc/cache thresholds accordingly.

Commit Suggestion
- Commit this progress in one checkpoint before further feature work:
  "Upgrade minimap to high-res atlas and anchor route from player"

