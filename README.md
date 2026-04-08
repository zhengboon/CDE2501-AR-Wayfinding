# CDE2501 AR Wayfinding

> Unity AR navigation MVP with elderly-first guidance and safety-weighted routing for **Queenstown Estate** and **NUS Engineering Campus**.

Last updated: 2026-04-09

---

## Overview

| | Queenstown | NUS Engineering |
|---|---|---|
| **Nodes** | 1,295 | 424 |
| **Edges** | 2,795 | 1,080 |
| **Profiles** | Elderly (1.0 m/s), Wheelchair (0.8 m/s) | Same |

**Core stack:** Unity 2022.3.62f3, C# (AR/GPS/Routing), Python (data generation & build automation), ARCore/ARKit, OSM/OneMap integration.

**Key capabilities:**
- GPS + Compass sensor fusion with simulation mode for laptop-first testing
- Weighted A* route computation with ETA display
- Minimap with follow-heading, pan/zoom, destination selection
- Flight-tracker AR view — point phone at destinations to see labels, distance, ETA overlaid on camera
- Runtime map-area switching (Queenstown <-> NUS) swapping texture + graph + locations
- User path recording — testers walk labeled paths that feed back into the graph
- Enhanced telemetry with altitude, floor estimation, GPS loss detection, auto-screenshots
- Cached auto-build launcher with no-change skip logic
- KML-based data generation pipeline (graphs, maps, boundaries)

A project website is served from the repo root (`index.html`, `styles.css`, `app.js`).

---

## Quick Start

### Prerequisites
- **Unity Hub** + Editor **2022.3.62f3** (with Android Build Support, SDK/NDK, OpenJDK)
- **Python 3.9+** with pip

### Setup
```bash
# Install Python dependencies for data generation scripts
pip install -r requirements.txt

# Open project in Unity
# 1. Let packages restore (AR Foundation/ARCore/ARKit/Recorder/TMP)
# 2. Open Assets/Scenes/Main.unity
# 3. Press Play — verify route/minimap overlays are visible
```

### Cached Build Launch (Recommended)

Skip full rebuilds when nothing changed:
```bash
# Check build status
python scripts/unity_cached_builder.py --status

# Open Unity Editor via cache launcher
python scripts/unity_cached_builder.py --launch-editor

# Force Android APK build
python scripts/unity_cached_builder.py --force --target Android --output Builds/Android/CDE2501-Wayfinding.apk
```
Or double-click `launch_unity_cached_build.bat`.

### Generate Maps from KML

```bash
# One-click NUS + Queenstown generation (uses OneMap by default)
build_engineering_nus_map.bat

# Or run individually:
# NUS map atlas
python scripts/generate_map_atlas_from_kml.py --kml "CDE2501 NUS map.kml" --polygon-name "Map of Engine" --zoom-levels 18,19 --output-prefix nus_map --provider onemap --out-dir Assets/StreamingAssets/Data

# NUS graph + locations + boundary
python scripts/generate_osm_graph_from_kml.py --kml "CDE2501 NUS map.kml" --polygon-name "Map of Engine" --graph-output "Assets/StreamingAssets/Data/nus_estate_graph.json" --locations-output "Assets/StreamingAssets/Data/nus_locations.json" --boundary-output "Assets/StreamingAssets/Data/nus_boundary.geojson" --area-name "NUS Engineering Wayfinding" --anchor-name "NUS Engineering Anchor" --node-prefix NUS --raw-cache "Docs/nus_osm_raw.json"
```

For Google Map Tiles (higher quality), set `GOOGLE_MAPS_API_KEY` and use `--provider google --fallback-to-osm`.

### OneMap API Integration

OneMap is used as the default map tile provider (free, no key required):
```bash
python scripts/generate_map_atlas_from_kml.py ... --provider onemap --onemap-style Default
```

For routing with Barrier-Free Access (BFA) data:
```bash
python scripts/fetch_onemap_route.py --start 1.296568,103.773253 --end 1.298701,103.771212 --route-type walk --token YOUR_TOKEN
```
> BFA API requires approval via [go.gov.sg/bfa-enquires](https://go.gov.sg/bfa-enquires).

---

## Controls (Editor Simulation)

| Key | Action |
|---|---|
| `N` / `P` | Destination next / prev |
| `Ctrl+R` / `F5` | Recalc route |
| `1` / `2` | Normal Elderly / Wheelchair profile |
| `T` | Rain toggle |
| `F2` | Simulation mode on/off |
| `F1` | Simulation panel on/off |
| `W A S D` | Movement |
| `Shift` | Sprint |
| `Q/E` or `Left/Right` | Yaw |
| `R/F` or `Up/Down` | Pitch |
| `PageUp/PageDown` | Vertical offset |
| Map Area button | Toggle Queenstown / NUS Engineering |

---

## Architecture

### File Structure
```
Assets/
├── Scripts/
│   ├── Location/      GPS, Compass, Smoothing, Simulation, Telemetry
│   ├── IndoorGraph/   Node/Edge models, GraphLoader, Visualizer
│   ├── Routing/       A* Pathfinder, RouteCalculator, Path Visualizer
│   ├── Profiles/      Elderly & Wheelchair routing profiles
│   ├── Data/          LocationManager, BoundaryConstraint, VideoFrameData
│   ├── UI/            Bootstrap, MiniMap, Destinations, Reassurance, Maps
│   ├── AR/            ArrowRenderer
│   ├── Elevation/     Level management
│   ├── Utility/       Shared DataFileUtility helpers
│   └── Editor/        Build runner, Session recorder
├── StreamingAssets/Data/   Graphs, locations, profiles, map tiles, boundaries
└── Scenes/Main.unity       Entry scene
scripts/                    Python data generation & build automation
Docs/                       Architecture docs & raw data caches
```

### Module Overview

| Module | Key Files | Purpose |
|---|---|---|
| **Location** | GPSManager, CompassManager, LocationSmoother, SimulationProvider, TelemetryRecorder, PathScreenshotRecorder, UserPathRecorder | Sensor input, smoothing, simulation fallback, enhanced telemetry, auto-screenshots, user path recording |
| **Graph** | Node, Edge, GraphLoader, GraphRuntimeVisualizer | Graph data model, JSON loading, debug rendering |
| **Routing** | AStarPathfinder, RouteCalculator, RoutePathVisualizer | Weighted A* with lazy gScore init, route cache + cooldown, path rendering |
| **UI** | QuickStartBootstrap, MiniMapOverlay, DestinationSelectorUI | Orchestration, minimap with follow-heading, destination CRUD |
| **Data** | LocationManager, BoundaryConstraintManager | Destination persistence, geojson boundary filtering |
| **AR** | ArrowRenderer, FlightTrackerARView | Direction arrow, camera-based destination overlay (flight-tracker style) |
| **Editor** | AutoPlaySessionRecorder, CDE2501BuildRunner | Play-session MP4 recording with auto-prune, batch build entrypoint |

### Data Generation Scripts

| Script | Purpose |
|---|---|
| `generate_osm_road_graph.py` | OSM roads to graph JSON with safety attributes |
| `generate_osm_graph_from_kml.py` | KML polygon to graph + locations + boundary |
| `generate_map_atlas_from_kml.py` | KML polygon to map tiles (Google/OneMap/OSM fallback) |
| `generate_osm_map_atlas.py` | OSM tile download + atlas assembly |
| `build_video_frame_map.py` | Video manifest generation with route filtering |
| `select_queenstown_videos.py` | Video scoring + selection pipeline |
| `map_videos_to_kml.py` | Route-to-KML export |
| `fetch_onemap_route.py` | OneMap routing query utility |
| `unity_cached_builder.py` | Unity auto-build with fingerprint cache + reports |

---

## Build Targets

- **Android:** ARCore plugin enabled in XR Plug-in Management
- **iOS:** ARKit plugin enabled; requires `Info.plist` strings:
  - `NSCameraUsageDescription`
  - `NSLocationWhenInUseUsageDescription`
  - `NSMotionUsageDescription`

---

## Alpha Testing — Telemetry & Path Recording

### Build & deploy
```bash
python scripts/unity_cached_builder.py --force --target Android --output Builds/Android/CDE2501-Wayfinding.apk
```
No ARCore/Google Play Services required — works on any Android phone with GPS and compass.

### Tester workflow
1. Install APK, open app
2. Wait for GPS/Compass ready indicators
3. Select destination from dropdown
4. Tap **Rec: OFF** to start recording (auto-screenshots begin)
5. Walk the route — tap **Snap** for manual screenshots at notable points
6. Tap **Rec: ON** to stop recording
7. Toggle **Debug** to view full system diagnostics if needed

### Retrieving data
```bash
adb pull /sdcard/Android/data/<bundle-id>/files/Telemetry/ ./FieldData/
```

Each session is stored in its own folder:
```
Session_YYYYMMDD_HHMMSS/
├── telemetry.csv
└── screenshots/
    ├── 20260409_143045_lat1.2965_lon103.7732_h45_heading.jpg
    └── ...
```

### Telemetry CSV columns
`Time, DateTime, Lat, Lon, Heading, Altitude, Accuracy, EstFloor, GpsLost, StartNode, Destination, RouteDistance, IsSimulated`

### Auto-screenshot triggers
- Heading change >30 degrees (captures turns)
- Every 10 seconds while moving (straight segments)
- Manual tap via **Snap** button (stairs, ramps, obstructions)

### GPS loss handling
- GPS lost indicator shown when accuracy >50m or no update for 10s
- Recording continues with `GpsLost=true` flag in CSV
- Altitude tracked for floor estimation (~3.5m per floor from baseline)

### User path recording
Testers can record labeled walking paths that feed back into the graph:
1. Select a destination in the overlay
2. Tap the **Record path: [start] -> [destination]** button
3. Walk the real path — GPS breadcrumbs are recorded every 1s (min 1m apart)
4. Tap **Stop Path** when arrived
5. Path is saved locally as JSON with full GPS trail, altitude, floor, heading

Recorded paths are stored in `Android/data/<bundle-id>/files/RecordedPaths/`:
- `path_index.json` — metadata index of all recorded paths
- `path_YYYYMMDD_HHMMSS.json` — full GPS trail for each path

### Flight-tracker AR view
Tap **AR: OFF** to activate the camera-based destination overlay:
- Phone camera becomes the background (no ARKit/ARCore needed)
- Destinations appear as floating labels based on GPS bearing + compass heading
- Labels show name, distance, and ETA — closer destinations appear larger
- Selected destination is highlighted in orange
- Route bearing indicator at bottom shows turn direction (left/right arrows)
- Uses gyroscope for pitch detection, accelerometer as fallback
- Works on any phone with camera, GPS, and compass

### Alpha tester UI
By default, the overlay shows only essential controls: Wheelchair toggle, Rec, Snap, AR, Path Record, Destination, Map Area. Toggle **Debug** to reveal full diagnostics (Rain, Sim Mode, session recordings, system status).

---

## Troubleshooting

| Symptom | Check | Action |
|---|---|---|
| Route does not update while moving | `Sim Mode` true, `Main Camera` present | Press `F5`, then `WASD` |
| Play mode shows only sky/ground | Scene is `Untitled` not `Main.unity` | Open `Assets/Scenes/Main.unity`, replay |
| GPS/Compass not ready | Overlay status text | Laptop: `F2` simulation; Mobile: enable Location Services |
| Minimap elements drift when turning | Follow-heading mode active | Toggle follow-heading off as fallback |
| Route not refreshing after area switch | Map area just changed | Press `F5` to force recalc |
| NUS area empty after switch | `Map Area: NUS Engineering` shown | Toggle area once, wait for auto-route |
| NUS route failing | Start node shows `QTMRT` in NUS | Switch area again, let auto-route run |
| NUS zero-length route | Start == destination | Auto-selects nearby alternative |
| NUS route refreshes too often | GPS jitter on device | Higher movement threshold applied |
| No session previews in Play Mode | No completed play session | Complete one session; shows latest 5 |
| Unity executable not found | Config path incorrect | Set `unityExecutable` in `scripts/unity_cached_builder_config.json` |
| Cached launcher keeps rebuilding | Files changing in watched roots | Narrow watch roots in config |

---

## Website

```bash
python -m http.server 8000
# Open http://localhost:8000
```

The website includes: feature showcase, OneMap API documentation, map atlas previews (Queenstown + NUS), quick start guide, troubleshooting, and future roadmap.

---

## Known Gaps

- Street View/video-frame environment disabled by default (capture spacing too sparse). Archived assets retained.
- NUS building inter-links include synthetic connectivity assumptions — field data will replace them.
- GPS altitude accuracy is limited (~10-30m error); floor estimation uses relative barometric delta.
- Full AR camera passthrough not yet active (ARCore/ARKit plugins included but no ARSession in scene).

---

## Roadmap

1. **Alpha field testing** — deploy NUS APK, collect walked-path telemetry + screenshots
2. **Path ingestion pipeline** — Douglas-Peucker simplification of GPS trails into graph nodes/edges
3. **Replace synthetic graph** — swap OSM-generated NUS graph with real walked-path graph
4. **Flight-tracker AR view** — point phone toward destination to see description, distance, and direction overlay (like airport flight tracker AR)
5. Integrate OneMap BFA API when approved
6. Dead reckoning fallback when GPS is lost (compass + step counting)
7. Indoor positioning via WiFi fingerprinting or BLE beacons
8. Share/upload recorded paths from device to repo
