# CDE2501 AR Wayfinding - Project README
Last updated: 2026-03-26

## 1) Project Summary
This repository is a Unity `2022.3.62f3` AR wayfinding MVP for two test areas:
- Queenstown estate
- NUS Engineering campus

It is designed around elderly-first guidance and safety-weighted routing, with:
- GPS/compass input via Unity APIs
- Weighted A* route computation over a local graph
- Keyboard simulation for laptop-first testing
- Minimap route visualization and destination selection
- Runtime map-area switching that swaps map texture + graph + locations together
- Optional Street View image mode (data-driven)
- Cached auto-build launcher (`Python + Unity batchmode`) with no-change skip logic and build reports

A project website is included in the repo root:
- `index.html`
- `styles.css`
- `app.js`

## 2) What Was Reviewed
I reviewed the full project source/config/data structure relevant to runtime behavior:
- `Assets/Scripts`: 30 C# scripts (plus `.meta`)
- `Assets/StreamingAssets/Data`: graph, locations, profiles, map tiles, boundary, street-view manifest
- `Docs`: architecture/integration docs + video scan artifacts
- `scripts`: data generators + cached Unity build/launch automation
- `Packages/manifest.json`
- `ProjectSettings/ProjectVersion.txt`
- `Assets/Scenes/Main.unity`

Ignored in functional review: Unity-generated build/cache folders like `Library/` and `Logs/`.

## 3) Current Runtime State
- Main scene: `Assets/Scenes/Main.unity`
- Main bootstrap object: `QuickStart` (`QuickStartBootstrap.cs`)
- Runtime can switch between:
  - `estate_graph.json` + `locations.json` (Queenstown)
  - `nus_estate_graph.json` + `nus_locations.json` (NUS)
- Current graph sizes:
  - Queenstown: `1295` nodes, `2795` edges
  - NUS: `402` nodes, `996` edges
- Routing profiles loaded from `Assets/StreamingAssets/Data/routing_profiles.json`
- Street View manifest currently has `0` usable nodes (no active imagery path)
- Manual recalc key handling is improved to avoid input conflict with pitch controls in simulation mode.
- GPS/Compass overlays now include detailed runtime status messages to speed up debugging.

## 3.1 Recent Improvements (This Update)
- Added sensor diagnostics:
  - `GPSManager.StatusMessage`
  - `CompassManager.StatusMessage`
- Updated Quick Start overlay to show readiness + source + status message for GPS and compass.
- Reduced manual recalc key conflict with simulation pitch:
  - in simulation mode: manual recalc uses `Ctrl+R` or `F5`
- Removed obsolete suppression pragmas in `AutoPlaySessionRecorder` around encoder settings.
- Upgraded project website with:
  - troubleshooting section
  - copyable command snippets
  - updated controls and runtime notes
- Added map-area toggle:
  - Quick Start button switches between `Queenstown` and `NUS Engineering`
  - Applies to minimap + map-reference tile + graph file + locations file at runtime
- Added dynamic data-file switching in runtime managers:
  - `GraphLoader.SetGraphFileName(...)`
  - `LocationManager.SetLocationsFileName(...)`
  - `RouteCalculator` now rebuilds pathfinder on graph reload
- Added NUS data generation pipeline from KML:
  - `scripts/generate_osm_graph_from_kml.py`
  - Generates:
    - `Assets/StreamingAssets/Data/nus_estate_graph.json`
    - `Assets/StreamingAssets/Data/nus_locations.json`
    - `Assets/StreamingAssets/Data/nus_boundary.geojson`
  - Includes synthetic inter-building links (assumption: walkable links exist between buildings)
- Added cached build automation:
  - `scripts/unity_cached_builder.py`
  - `scripts/unity_cached_builder_config.json`
  - `Assets/Scripts/Editor/CDE2501BuildRunner.cs`
  - `launch_unity_cached_build.bat`
- Added build review artifacts:
  - `UnityBuildCache/latest_build_summary.json`
  - `UnityBuildCache/latest_build_report.md`
  - `UnityBuildCache/logs/*.log`

## 4) File Review by Module

### 4.1 Location / Sensors / Simulation
- `Assets/Scripts/Location/GPSManager.cs`: GPS polling, smoothing, source-mode switch, optional simulation fallback.
- `Assets/Scripts/Location/CompassManager.cs`: compass polling, heading smoothing, source-mode switch.
- `Assets/Scripts/Location/LocationSmoother.cs`: exponential geo smoothing + angle smoothing helpers.
- `Assets/Scripts/Location/SimulationProvider.cs`: WASD movement, yaw/pitch keys, speed/sprint, overlay window.
- `Assets/Scripts/Location/SimulatedObjectDriver.cs`: applies simulated pose to camera/object transform.
- `Assets/Scripts/Location/SensorSourceMode.cs`: `Auto`, `Simulation`, `DeviceSensors` enum.

### 4.2 Graph + Routing
- `Assets/Scripts/IndoorGraph/Node.cs`: node data model.
- `Assets/Scripts/IndoorGraph/Edge.cs`: edge data model.
- `Assets/Scripts/IndoorGraph/GraphLoader.cs`: JSON load + node/edge indexing.
- `Assets/Scripts/IndoorGraph/GraphRuntimeVisualizer.cs`: editor/runtime graph debug rendering.
- `Assets/Scripts/Profiles/RoutingProfile.cs`: profile model + mode selection.
- `Assets/Scripts/Routing/AStarPathfinder.cs`: weighted A* core.
- `Assets/Scripts/Routing/RouteCalculator.cs`: route orchestration, cache, cooldown, boundary checks, revalidation.
- `Assets/Scripts/Routing/RoutePathVisualizer.cs`: path line rendering from route output.

### 4.3 Data + Boundaries
- `Assets/Scripts/Data/LocationManager.cs`: CRUD + persistence for destinations.
- `Assets/Scripts/Data/BoundaryConstraintManager.cs`: boundary import/filtering for nodes and locations.
- `Assets/Scripts/Data/VideoFrameData.cs`: video frame manifest data models.

### 4.4 UI / AR / Bootstrap
- `Assets/Scripts/AR/ArrowRenderer.cs`: directional arrow placement/rotation.
- `Assets/Scripts/UI/QuickStartBootstrap.cs`: auto wiring, route loop, overlay controls, runtime orchestration.
- `Assets/Scripts/UI/MiniMapOverlay.cs`: minimap rendering, pan/zoom/follow, route + hover tooltips.
- `Assets/Scripts/UI/DestinationSelectorUI.cs`: destination controls and toggles.
- `Assets/Scripts/UI/DestinationMarkerVisualizer.cs`: destination markers in world/minimap context.
- `Assets/Scripts/UI/ReassuranceManager.cs`: reassurance text states.
- `Assets/Scripts/UI/MapReferenceTileVisualizer.cs`: map reference texture renderer.
- `Assets/Scripts/UI/StreetViewExplorer.cs`: optional route-filtered street-view image explorer.
- `Assets/Scripts/UI/VideoFrameMapVisualizer.cs`: mapped video frame markers.
- `Assets/Scripts/UI/VideoMappingCsvOverlay.cs`: CSV mapping overlay/debug tooling.

### 4.5 Editor Utility
- `Assets/Scripts/Editor/AutoPlaySessionRecorder.cs`: play-session recording hooks (updated to encoder API pattern).
- `Assets/Scripts/Editor/CDE2501BuildRunner.cs`: batch build entrypoint used by cached Python launcher.

### 4.6 Data Files (Runtime)
- `Assets/StreamingAssets/Data/estate_graph.json`
- `Assets/StreamingAssets/Data/locations.json`
- `Assets/StreamingAssets/Data/nus_estate_graph.json`
- `Assets/StreamingAssets/Data/nus_locations.json`
- `Assets/StreamingAssets/Data/nus_boundary.geojson`
- `Assets/StreamingAssets/Data/routing_profiles.json`
- `Assets/StreamingAssets/Data/queenstown_boundary.geojson`
- `Assets/StreamingAssets/Data/map_tile_z16_x51664_y32532.png`
- `Assets/StreamingAssets/Data/queenstown_map_z18_x206656-206662_y130127-130133.png`
- `Assets/StreamingAssets/Data/queenstown_map_z19_x413314-413324_y260255-260265.png`
- `Assets/StreamingAssets/Data/street_view_manifest.json`
- `Assets/StreamingAssets/Data/nus_map_z18_x206633-206638_y130123-130128.png`
- `Assets/StreamingAssets/Data/nus_map_z18_x206633-206638_y130123-130128.json`
- `Assets/StreamingAssets/Data/nus_map_z19_x413268-413276_y260247-260255.png`
- `Assets/StreamingAssets/Data/nus_map_z19_x413268-413276_y260247-260255.json`

### 4.7 Data Generation / Utility Scripts
- `scripts/generate_osm_road_graph.py`: fetches OSM roads and writes graph with safety attributes.
- `scripts/generate_osm_graph_from_kml.py`: generates area graph + locations + boundary directly from KML polygon + point placemarks.
- `scripts/generate_osm_map_atlas.py`: downloads OSM tiles and generates map atlas PNG + metadata.
- `scripts/generate_map_atlas_from_kml.py`: generates area map atlases from KML polygon with Google Map Tiles API support and OSM fallback.
- `scripts/build_street_view_map.py`: builds route-area Street View dataset (Google + fallback logic).
- `scripts/build_video_frame_map.py`: maps videos/frames to route nodes.
- `scripts/select_queenstown_videos.py`: scoring/selection pipeline for candidate videos.
- `scripts/map_videos_to_kml.py`: exports mapped routes back to KML.
- `scripts/unity_cached_builder.py`: Unity auto-build orchestrator with fingerprint cache and report generation.
- `scripts/unity_cached_builder_config.json`: editable config for Unity executable, target, output, and watch roots.
- `build_engineering_nus_map.bat`: one-click map + NUS graph/locations generation.

## 5) Controls (Editor Simulation)
- `N` / `P`: destination next/prev
- `Ctrl+R` or `F5`: recalc route while simulation movement controls are active
- `1`: Normal Elderly
- `2`: Wheelchair
- `T`: rain toggle
- `Y`: Street View toggle
- `F2`: simulation mode on/off
- `F1`: simulation panel on/off
- `W A S D`: movement
- `Shift`: sprint
- `Q/E` or `Left/Right`: yaw
- `R/F` or `Up/Down`: pitch
- `PageUp/PageDown`: vertical offset
- Quick Start overlay button: `Map Area` toggles `Queenstown` / `NUS Engineering`

## 6) Quick Start on a New Computer
1. Install Unity Hub.
2. Install Unity Editor `2022.3.62f3`.
3. Include modules: Android Build Support, Android SDK/NDK, OpenJDK.
4. Open this repo in Unity.
5. Let packages restore (AR Foundation/ARCore/ARKit/Recorder/TMP).
6. Open `Assets/Scenes/Main.unity`.
7. Press Play and verify route/minimap overlays are visible.
8. In overlay, confirm:
   - `GPS ready` line has meaningful status text.
   - `Compass ready` line has meaningful status text.

## 7) Cached Build + Launch (Recommended)
Use this when opening the project daily. It only runs Unity batch build when inputs changed; otherwise it reuses cache.

Windows one-click:
1. Double-click `launch_unity_cached_build.bat`.

CLI usage:
1. `python scripts/unity_cached_builder.py --status`
2. `python scripts/unity_cached_builder.py --launch-editor`
3. `python scripts/unity_cached_builder.py --force --target Android --output Builds/Android/CDE2501-Wayfinding.apk`

Output and review files:
1. `UnityBuildCache/latest_build_report.md`
2. `UnityBuildCache/latest_build_summary.json`
3. `UnityBuildCache/logs/`

## 7.1 NUS + Queenstown Map Import From KML (Google + Fallback)
Generate NUS and Queenstown map assets in the same atlas format, then generate NUS graph/locations from KML.

1. Set API key for true Google tiles (optional but recommended):
   - Windows CMD: `set GOOGLE_MAPS_API_KEY=YOUR_KEY`
   - PowerShell: `$env:GOOGLE_MAPS_API_KEY="YOUR_KEY"`
2. In Google Cloud, ensure this project key has:
   - Billing enabled
   - Map Tiles API enabled
   - Key restrictions that allow Map Tiles API requests
3. Run NUS:
   - `python scripts/generate_map_atlas_from_kml.py --kml "CDE2501 NUS map.kml" --polygon-name "Map of Engine" --zoom-levels 18,19 --output-prefix nus_map --provider google --fallback-to-osm --out-dir Assets/StreamingAssets/Data`
4. Run Queenstown:
   - `python scripts/generate_map_atlas_from_kml.py --kml "cde2501.kml" --polygon-name "Site area" --zoom-levels 18,19 --output-prefix queenstown_map --provider google --fallback-to-osm --out-dir Assets/StreamingAssets/Data`
5. Generate NUS graph + locations + boundary:
   - `python scripts/generate_osm_graph_from_kml.py --kml "CDE2501 NUS map.kml" --polygon-name "Map of Engine" --graph-output "Assets/StreamingAssets/Data/nus_estate_graph.json" --locations-output "Assets/StreamingAssets/Data/nus_locations.json" --boundary-output "Assets/StreamingAssets/Data/nus_boundary.geojson" --area-name "NUS Engineering Wayfinding" --anchor-name "NUS Engineering Anchor" --node-prefix NUS --raw-cache "Docs/nus_osm_raw.json"`
6. Generated files:
   - `Assets/StreamingAssets/Data/nus_map_z18_x206633-206638_y130123-130128.png`
   - `Assets/StreamingAssets/Data/nus_map_z18_x206633-206638_y130123-130128.json`
   - `Assets/StreamingAssets/Data/nus_map_z19_x413268-413276_y260247-260255.png`
   - `Assets/StreamingAssets/Data/nus_map_z19_x413268-413276_y260247-260255.json`
   - `Assets/StreamingAssets/Data/nus_estate_graph.json`
   - `Assets/StreamingAssets/Data/nus_locations.json`
   - `Assets/StreamingAssets/Data/nus_boundary.geojson`
   - `Assets/StreamingAssets/Data/queenstown_map_z18_x206656-206662_y130127-130133.png`
   - `Assets/StreamingAssets/Data/queenstown_map_z18_x206656-206662_y130127-130133.json`
   - `Assets/StreamingAssets/Data/queenstown_map_z19_x413314-413324_y260255-260265.png`
   - `Assets/StreamingAssets/Data/queenstown_map_z19_x413314-413324_y260255-260265.json`
7. Optional one-click:
   - `build_engineering_nus_map.bat`

## 8) Build Targets
- Android: ARCore plugin enabled in XR Plug-in Management.
- iOS: ARKit plugin enabled in XR Plug-in Management.
- iOS `Info.plist` strings required:
  - `NSCameraUsageDescription`
  - `NSLocationWhenInUseUsageDescription`
  - `NSMotionUsageDescription`

## 9) Known Gaps
- Street View currently has no usable node imagery in manifest; new image data generation is required.
- Indoor elevation realism still depends on graph annotation quality and field tuning.
- Full AR runtime validation still requires real ARCore/ARKit capable hardware.
- NUS building inter-links include a synthetic connectivity assumption for MVP routing and should be field-validated.

## 9.1 Troubleshooting Matrix
- Symptom: route does not update while moving
  - Check: `Sim Mode` is true and `Main Camera` is present as start reference.
  - Action: press `F5` for manual recalc, then move with `WASD`.
- Symptom: Play mode shows only sky/ground and nothing else
  - Check: active scene is `Untitled` instead of `Assets/Scenes/Main.unity`.
  - Action: stop Play mode, open `Assets/Scenes/Main.unity`, then Play again.
- Symptom: GPS shows not ready
  - Check: overlay GPS status text for OS disabled / initializing / service status.
  - Action: enable OS location services or use simulation mode.
- Symptom: Compass shows not ready
  - Check: overlay compass status text for heading accuracy unavailable.
  - Action: on laptop, use simulation heading; on phone, calibrate compass and retry.
- Symptom: Street View toggle shows empty view
  - Check: `Assets/StreamingAssets/Data/street_view_manifest.json` node count.
  - Action: regenerate imagery dataset using `scripts/build_street_view_map.py`.
- Symptom: cached launcher says Unity executable not found
  - Check: `scripts/unity_cached_builder_config.json` -> `unityExecutable`.
  - Action: set full path, e.g. `C:\\Program Files\\Unity\\Hub\\Editor\\2022.3.62f3\\Editor\\Unity.exe`.
- Symptom: cached launcher keeps rebuilding even with no edits
  - Check: files changing under watched roots (`Assets`, `Packages`, `ProjectSettings`, `scripts`).
  - Action: narrow watch roots or include/exclude patterns in `scripts/unity_cached_builder_config.json`.

## 10) Website Usage
To view the generated project website:
1. Open `index.html` in browser.
2. Website assets use local `styles.css` and `app.js`.
3. Content is aligned with this README and current project state.

## 11) Recommended Next Work
1. Regenerate Street View nodes/images for actual route coverage.
2. Do one end-to-end device field walk in both routing modes.
3. Tune routing weights in `routing_profiles.json` using field observations.
4. Lock a stable baseline commit before expanding UI/feature scope.
5. Add a second production UI profile (compact mode) that hides debug-heavy overlays.
