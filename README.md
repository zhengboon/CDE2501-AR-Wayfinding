# CDE2501 AR Wayfinding

> Unity AR navigation MVP with elderly-first guidance and safety-weighted routing for **Queenstown Estate** and **NUS Engineering Campus**.

Last updated: 2026-04-15

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

`--force` now skips fingerprint scanning and jumps straight to Unity build invocation.

If multiple Unity editors are installed, pin the executable explicitly:
```bash
python scripts/unity_cached_builder.py --force --target Android --output Builds/Android/CDE2501-Wayfinding.apk --unity-exe "C:\Program Files\Unity\Hub\Editor\2022.3.62f3-x86_64\Editor\Unity.exe"
```

### Latest Build Snapshot (2026-04-15)

- Build command: `python scripts/unity_cached_builder.py --force --target Android --output Builds/Android/CDE2501-Wayfinding.apk --unity-exe "C:\Program Files\Unity\Hub\Editor\2022.3.62f3-x86_64\Editor\Unity.exe"`
- Result: **Build Succeeded**
- APK output: `Builds/Android/CDE2501-Wayfinding.apk`
- APK size: **33,443,534 bytes** (~31.9 MB)
- Build duration: **153.803 s** (cached builder report)
- Build report: `UnityBuildCache/latest_build_report.md`

### Latest Logic/UI Review (2026-04-15)

- Drive runtime update detection now uses remote metadata fingerprint (`Content-Length + Last-Modified + ETag`) instead of size-only checks, so same-size file edits are detected.
- Failed update checks no longer silently report "up to date" and no longer advance the schedule marker; the app surfaces "Drive update check incomplete. Will retry."
- Added **Sync Now** button to Quick Start overlay for immediate Drive checks (no need to wait for the 15-minute interval).
- Quick Start overlay UI refreshed with a runtime status strip (GPS / Compass / Drive state) and cleaner visual panel styling.
- Cached build launcher now measures duration with `time.perf_counter()` for stable timing even if system clock changes.

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
| **Data** | LocationManager, BoundaryConstraintManager, DataSyncManager, CrashReporter | Destination persistence, boundary filtering, Google Drive data sync, crash log collection |
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

- **Android:** ARCore plugin enabled in XR Plug-in Management. Min SDK 24, ARM64-only (ARMv7 dropped).
- **iOS:** ARKit plugin enabled; requires `Info.plist` strings:
  - `NSCameraUsageDescription`
  - `NSLocationWhenInUseUsageDescription`
  - `NSMotionUsageDescription`

---

## Thin APK Deployment

The APK ships only the minimum required files. Large data files are downloaded from Google Drive on first launch and cached in `Application.persistentDataPath/Data/`.

### What's bundled vs downloaded

| File type | Location | Bundled? |
|---|---|---|
| Map atlas `.json` manifests | StreamingAssets/Data/ | ✅ Yes (~1 KB each) |
| Street View dataset (`street_view/`, `street_view_manifest.json`) | `.tmp-streetview/manual_move/` (outside Unity assets) | ❌ Not bundled |
| Graph + locations + boundaries + profiles | archived/ → Drive | ❌ Downloaded on launch |
| Map tile `.png` + atlas `.json` metadata | archived/ → Drive | ❌ Downloaded as optional runtime assets |

### Drive File IDs

Files are sourced from the synced project folder: `CDE2501-AR-Wayfinding/Assets/StreamingAssets/Data/archived/`.  
When you regenerate data locally, Google Drive desktop auto-syncs. The app picks up updates on its next scheduled check.

> **All Drive files must be shared as "Anyone with link → Viewer".**

| File | Drive File ID |
|---|---|
| estate_graph.json | `1rdVh89zKpehzd1_pjbiO15xQluxg-S3B` |
| nus_estate_graph.json | `19mJFjc_52qA30apecosIkI4bEit6Jff5` |
| locations.json | `1iudrdcjUA4axr7OlbVNtlX3sHB0351Ru` |
| nus_locations.json | `1iw1X8HGkigw5P0K5w08dXMFcQ1Bx1Gv9` |
| routing_profiles.json | `1BgyDOE4ts3V-o5Na4NzfSJic7BZX5HiZ` |
| queenstown_boundary.geojson | `1gXoUXctD0tI-T8mrXIZ5Eeo3h3Mz9PL0` |
| nus_boundary.geojson | `1LZWYApt484SDCGqcK8Q4xXj2cD0AOFMx` |

### Sync behaviour

- **First launch (no cached files):** progress bar UI downloads the 7 required startup files. Optional map assets are also downloaded when enabled.
- **Subsequent launches:** files already cached → app starts immediately. A metadata-based update check runs every **15 minutes** and re-downloads only changed files.
- **While app is open:** background checks continue every 15 minutes, and updated files are hot-reloaded for graph/locations/boundary/profiles/minimap textures where supported.
- **Manual trigger:** tap **Sync Now** in the Quick Start overlay to force an immediate runtime check.
- **Safety check:** if Drive returns an HTML page (auth/permission page), sync fails fast and logs a clear sharing-permission error instead of saving invalid JSON.
- **Force re-sync:** call `DataSyncManager.ForceReSync()` (or clear `persistentDataPath/Data/` manually).

### Runtime Update Scope

- **Can update from Drive without reinstall:** graph JSON, locations JSON, boundary GeoJSON, routing profiles, map PNG atlases, map metadata JSON.
- **Still requires new APK:** C# code, Unity scenes/prefabs, and project settings.

### Share / upload telemetry

The **Share** button in the overlay invokes the Android share intent with all files from:
- `persistentDataPath/Telemetry/` — CSV + screenshots
- `persistentDataPath/RecordedPaths/` — GPS trail JSONs
- `persistentDataPath/Crashes/` — crash logs

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
- Uses gyroscope + accelerometer to orient floating markers properly regardless of phone holding angle
- Includes an AR Status HUD (top-right) showing GPS/Compass ready state and live pitch angle
- Auto Gyro Sync runs 1.5 s after AR initialization to automatically level the horizon
- Manual "Sync Gyro" button to re-zero the vertical pitch to your current comfortable phone angle
- Works on any phone with camera, GPS, and compass
- **Snap GPS** button instantly locks position to the raw GPS fix (bypasses smoothing filter) — tap it immediately after AR activates to get accurate label positions
- **`Sync Gyro`** button re-zeroes pitch offset at any time for re-calibration mid-session

### Alpha tester UI
By default, the overlay shows only essential controls: Wheelchair toggle, Rec, Snap, AR, Sync Now, Share, Path Record, Destination, Map Area. A compact status strip now shows GPS/Compass/Drive sync state. Toggle **Debug** to reveal full diagnostics (Rain, Sim Mode, session recordings, system status).

---

## Troubleshooting

| Symptom | Check | Action |
|---|---|---|
| Route does not update while moving | `Sim Mode` true, `Main Camera` present | Press `F5`, then `WASD` |
| Play mode shows only sky/ground | Scene is `Untitled` not `Main.unity` | Open `Assets/Scenes/Main.unity`, replay |
| GPS/Compass not ready | Overlay status text | Laptop: `F2` simulation; Mobile: enable Location Services |
| AR labels don't move when tilting | Gyro not yet synced | Wait 1.5 s for auto-sync or tap **Sync Gyro**; check AR HUD top-right |
| Route starts from wrong location | GPS smoothing hasn't converged | Tap **Snap GPS** in overlay to instantly lock to raw fix |
| Drive file changed but app did not refresh | Old APK using size-only update checks | Install latest APK and tap **Sync Now**; runtime sync now compares remote metadata fingerprint |
| Drive status shows "update check incomplete" | Network/share permission issue during runtime check | Verify internet + Drive sharing (`Anyone with link -> Viewer`), then tap **Sync Now** again |
| Share shows "No data to share yet" | No recordings exist yet | Start **Rec: ON**, walk a path, stop it, then Share |
| Share shows "Share error: ..." | FileProvider not registered | Rebuild APK — config now in `Assets/Plugins/Android/AndroidManifest.xml` |
| Android build fails with `OBSOLETE ... Assets/Plugins/Android/res` | Legacy Android resource path used | Keep `file_paths.xml` under `Assets/Plugins/Android/FileProviderLib.androidlib/res/xml/` (not `Assets/Plugins/Android/res/`) |
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

- Street View assets are intentionally externalized from Unity `StreamingAssets` and kept in local backup (`.tmp-streetview/manual_move/`) to keep APKs thin.
- Street View/video-frame environment disabled by default (capture spacing too sparse). Archived assets retained.
- NUS building inter-links include synthetic connectivity assumptions — field data will replace them after alpha testing.
- GPS altitude accuracy is limited (~10–30 m error); floor estimation uses relative barometric delta.
- Flight-tracker AR view uses WebCamTexture + compass (no ARCore/ARKit dependency). AR plugins remain in project for future plane-detection features.
- No Google Sign-In yet — testers share data manually via the Share button (with full Android 7+ FileProvider support).
- Drive file IDs must remain **"Anyone with link → Viewer"**; verify after any Drive folder restructuring.

---

## Roadmap

1. ✅ **Thin APK + Drive sync** — required data files downloaded on first launch, 15-minute update checks with background polling while app is open
2. ✅ **Share button** — Android intent sends telemetry CSVs, recorded paths, screenshots, crash logs; Android 7+ FileProvider with FLAG_ACTIVITY_NEW_TASK
3. **Alpha field testing** — deploy APK, collect walked-path telemetry + screenshots at NUS/Queenstown
4. **Path ingestion pipeline** — Douglas-Peucker simplification of GPS trails into graph nodes/edges
5. **Replace synthetic graph** — swap OSM-generated NUS graph with real walked-path data
6. **Google Sign-In** — direct upload of telemetry/paths to Drive without manual sharing
7. Integrate OneMap BFA API when approved
8. Dead reckoning fallback when GPS is lost (compass + step counting)
9. Indoor positioning via WiFi fingerprinting or BLE beacons
