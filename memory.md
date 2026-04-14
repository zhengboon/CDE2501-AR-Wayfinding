# CDE2501 AR Wayfinding — Memory

> Living document. Updated: 2026-04-13 (15-minute Drive sync + runtime refresh)

---

## Current Goal

**Generate a thin APK** where the Android app:
1. Ships only the minimum required files in the APK itself
2. Downloads all large/regeneratable data files from Google Drive on first launch
3. Caches downloaded files in `Application.persistentDataPath/Data/`
4. Updates automatically every 15 minutes when Drive files change (including background checks while app is open)

---

## Latest Verified Android Build (2026-04-13)

- Command used:
  `python scripts/unity_cached_builder.py --force --target Android --output Builds/Android/CDE2501-Wayfinding.apk --unity-exe "C:\Program Files\Unity\Hub\Editor\2022.3.62f3-x86_64\Editor\Unity.exe"`
- Result: **Build Succeeded**
- Output: `Builds/Android/CDE2501-Wayfinding.apk`
- File size: **33,503,852 bytes** (~32 MB)
- Build duration: **178.111 s**
- Report: `UnityBuildCache/latest_build_report.md`
- Notes:
  - Log scanner still catches licensing client errors early in startup, but Unity resolves entitlement and the final build succeeds.
  - `ProjectSettings.asset` currently keeps `preloadedAssets: []` for build stability and parity with latest successful run.
  - `scripts/unity_cached_builder_config.json` is pinned to Unity `2022.3.62f3-x86_64` to avoid selecting an editor without Android modules.

---

## Architecture Decisions

### Thin APK Split

| What | Location | Reason |
|---|---|---|
| Map atlas `.json` manifests | StreamingAssets/Data/ (bundled) | Tiny (< 2 KB each), tile index only |
| Graph `.json`, locations, boundaries, routing profiles | StreamingAssets/Data/archived/ → Drive | Large, regeneratable, updated between builds |
| Map tile `.png` atlases | StreamingAssets/Data/archived/ | 1–4 MB each, graceful degradation without them |
| Street View dataset (`street_view/`, `street_view_manifest.json`) | `.tmp-streetview/manual_move/` (outside Unity assets) | Excluded from APK/build scan while keeping local backup |
| APK architecture | ARM64 only | ARMv7 dropped to reduce size |
| Min SDK | 24 | ARCore compatibility |

### Data Sync Flow

```
App launch
  ↓
DataSyncManager.Start()
  ├── All required files exist locally? → SyncComplete immediately → QuickStartBootstrap.StartAfterSync()
  └── Missing any required file? → SyncRoutine() → download required files (and optional map assets if enabled) → progress bar UI → StartAfterSync()
       └── 15-minute update cycle:
           - Check on launch (if interval elapsed)
           - Continue background checks every 15 minutes while app is open
           - Compare Content-Length and re-download changed files
           - Raise OnFilesUpdated event so QuickStartBootstrap hot-refreshes runtime components
```

### File Path Resolution (QuickStartBootstrap)

`DataFileExists(fileName)` checks in order:
1. `Application.streamingAssetsPath/Data/{fileName}` — bundled (editor + device)
2. `Application.persistentDataPath/Data/{fileName}` — downloaded cache (device only)

Pattern-based discovery (`FindBestDataFileByPattern`) probes both directories, picks alphabetically latest filename match.

### Drive File IDs (as of runtime sync expansion — 2026-04-13)

Source: `CDE2501-AR-Wayfinding/Assets/StreamingAssets/Data/archived/` (synced via Google Drive desktop)  
Single source of truth — regenerate locally → Drive auto-syncs → app picks up on next scheduled 15-minute check.

| File | Drive ID |
|---|---|
| estate_graph.json | 1rdVh89zKpehzd1_pjbiO15xQluxg-S3B |
| nus_estate_graph.json | 19mJFjc_52qA30apecosIkI4bEit6Jff5 |
| locations.json | 1iudrdcjUA4axr7OlbVNtlX3sHB0351Ru |
| nus_locations.json | 1iw1X8HGkigw5P0K5w08dXMFcQ1Bx1Gv9 |
| routing_profiles.json | 1BgyDOE4ts3V-o5Na4NzfSJic7BZX5HiZ |
| queenstown_boundary.geojson | 1gXoUXctD0tI-T8mrXIZ5Eeo3h3Mz9PL0 |
| nus_boundary.geojson | 1LZWYApt484SDCGqcK8Q4xXj2cD0AOFMx |

Download URL pattern: `https://drive.usercontent.google.com/download?id={fileId}&export=download`  
**All files must be shared "Anyone with link → Viewer" on Drive.**

---

## Key Scripts / Components

| Component | File | Role |
|---|---|---|
| DataSyncManager | Assets/Scripts/Data/DataSyncManager.cs | Download + cache required + optional Drive assets. OnGUI progress bar. 15-minute on-launch + background update loop. Share button (Android intent). |
| CrashReporter | Assets/Scripts/Data/CrashReporter.cs | Captures UnityEngine errors/exceptions to `persistentDataPath/Crashes/` for tester upload. |
| QuickStartBootstrap | Assets/Scripts/UI/QuickStartBootstrap.cs | Orchestrates everything. Waits for DataSyncManager.SyncComplete before loading graph/locations. Dual-path file resolution (streaming + persistent). |
| GraphLoader | Assets/Scripts/IndoorGraph/GraphLoader.cs | Loads graph JSON. SetGraphFileName(name, reload) called on map-area switch. |
| LocationManager | Assets/Scripts/Data/LocationManager.cs | Loads locations JSON. SetLocationsFileName(name, reload) on area switch. |
| AStarPathfinder | Assets/Scripts/Routing/AStarPathfinder.cs | Weighted A* with lazy gScore init, Baritone-style start node resolution. |
| TelemetryRecorder | Assets/Scripts/Location/TelemetryRecorder.cs | CSV telemetry: GPS, heading, altitude, EstFloor, GpsLost. Auto-screenshot triggers. |
| UserPathRecorder | Assets/Scripts/Location/UserPathRecorder.cs | GPS breadcrumb recording from/to destinations. Min 5 points / 10m validation. |
| FlightTrackerARView | Assets/Scripts/AR/FlightTrackerARView.cs | WebCamTexture + compass + gyroscope AR destination overlay. No ARCore required. |
| CompassManager | Assets/Scripts/Location/CompassManager.cs | SmoothedHeading normalized 0–360. Returns early after enabling to give OS init time. |

---

## Code Review Fixes (2026-04-11)

1. `DataSyncManager` now validates downloaded payloads and rejects HTML/auth pages from Google Drive instead of saving invalid files as JSON.
2. `DataSyncManager` now writes via `*.tmp` and then atomically moves the file into place to avoid partial/corrupted writes.
3. `unity_cached_builder.py --force` now skips expensive full fingerprint scans (critical when very large optional datasets exist).
4. `unity_cached_builder.py` and `unity_cached_builder_config.json` exclude `street_view` and `video_frames` directories from fingerprint scanning.
5. `CDE2501BuildRunner.RunDirectBuild` now mirrors batchmode reliability guards: explicit active-target switch and Android IL2CPP + ARM64 enforcement.
6. Current 7 required Drive IDs were re-verified as direct-download accessible.

---

## Code Review Fixes (2026-04-13)

1. **Android Sensor Initialization**: Disabled `forceSimulationMode` effectively for Android device builds. Previously, it defaulted to `true` which locked location and compass output to the static WASD simulation values.
2. **Camera Permissions**: AR Camera initialization now dynamically checks and requests `android.permission.CAMERA` at runtime.
3. **Gyroscope Sync**: Added `SyncGyro()` functionality and a button in the `QuickStartBootstrap` AR overlay. Captures the device's current pitch to comfortably offset the view angle.

## Runtime Update Expansion (2026-04-13)

1. **Update interval changed to 15 minutes** (`updateCheckIntervalHours = 0.25f`), including continuous background checks while app remains open.
2. **Drive sync scope expanded** from only 7 required files to include optional map PNG/JSON runtime assets.
3. **Live runtime refresh**: `QuickStartBootstrap` now subscribes to `DataSyncManager.OnFilesUpdated` and hot-reloads changed graph, locations, boundary, routing profiles, minimap texture, and reference map texture.
4. **Persistent-first loading**: minimap/reference map loaders now prefer `persistentDataPath/Data/` before falling back to bundled `StreamingAssets`.
5. **Stale Drive IDs corrected** for `nus_locations.json` and `queenstown_boundary.geojson`.

---

## Build State

### APK Build Command
```bash
python scripts/unity_cached_builder.py --force --target Android --output Builds/Android/CDE2501-Wayfinding.apk
```

### What's Bundled vs Downloaded

**Bundled in APK (StreamingAssets/Data/):**
- Map atlas JSON manifests (6 × ~1 KB)
- No Street View dataset (moved outside Unity assets)

**Downloaded on first launch from Drive (required):**
- `estate_graph.json` (~1.1 MB)
- `nus_estate_graph.json` (~421 KB)
- `locations.json` (~1.3 KB)
- `nus_locations.json` (~2.5 KB)
- `routing_profiles.json` (~0.7 KB)
- `queenstown_boundary.geojson` (~1 KB)
- `nus_boundary.geojson` (~2.4 KB)

**Downloaded as optional runtime assets (when enabled):**
- Queenstown/NUS map tile PNG atlases
- Corresponding atlas metadata JSON files

**Still excluded from APK:**
- Street View image datasets (`street_view/`)

---

## Recent Git History (last 10 commits)

| Hash | Date | Summary |
|---|---|---|
| cd42bee | 2026-04-10 | Switch DataSyncManager to synced project Drive file IDs |
| c4024eb | 2026-04-09 | Update website and README to reflect current functionality |
| 29c55db | 2026-04-09 | Fix heading clamping, compass init, download errors, path validation |
| 677bc64 | 2026-04-09 | Add From/To location selection for path recording |
| c4fe208 | 2026-04-09 | Thin APK: Drive sync, share button, ARM64-only, progress bar |
| 85e0a61 | 2026-04-09 | Add DataSyncManager and CrashReporter |
| 0bb9920 | 2026-04-09 | Move map tile PNGs to archived/ |
| 14e1e8b | 2026-04-09 | Set Android min SDK 24, OpenGLES3 for ARCore |
| d0bcd66 | earlier | Heading direction arrow on minimap player marker |
| 356b3f7 | earlier | Flight-tracker AR camera view with destination labels |

---

## Alpha Test Workflow

1. Build APK → install on tester's phone
2. First launch: Drive sync UI shows (downloads required files first; optional map assets follow when enabled)
3. Tester selects destination, taps **Rec: OFF** to start telemetry
4. Walk route — auto-screenshots at turns and every 10 s
5. Tap **Stop** / **Share** to send CSVs + paths + crash logs via Android share intent
6. Data: `/sdcard/Android/data/<bundle-id>/files/Telemetry/`, `RecordedPaths/`, `Crashes/`

---

## Known Issues / TODOs

- [ ] Optional Street View dataset is externalized at `.tmp-streetview/manual_move/` and currently excluded from APK
- [ ] Drive file IDs must be **"Anyone with link → Viewer"** — verify after any Drive restructure
- [ ] No Google Sign-In yet — tester must manually share data via Share button
- [ ] NUS graph is OSM-synthetic — replace with walked-path data after alpha testing
- [ ] GPS altitude accuracy limited (~10–30 m error); floor estimation uses relative barometric delta
- [ ] BFA API requires approval at go.gov.sg/bfa-enquires

---

## Map Areas

| Area | Graph Nodes | Graph Edges | Default Start Node |
|---|---|---|---|
| Queenstown Estate | 1,295 | 2,795 | QTMRT |
| NUS Engineering Campus | 424 | 1,080 | NUS_E1 |

Routing profiles: Elderly (1.0 m/s), Wheelchair (0.8 m/s) — loaded from `routing_profiles.json`.

---

## Data Generation

```bash
# One-click NUS + Queenstown
build_engineering_nus_map.bat

# NUS graph + locations + boundary
python scripts/generate_osm_graph_from_kml.py \
  --kml "CDE2501 NUS map.kml" \
  --polygon-name "Map of Engine" \
  --graph-output Assets/StreamingAssets/Data/archived/nus_estate_graph.json \
  --locations-output Assets/StreamingAssets/Data/archived/nus_locations.json \
  --boundary-output Assets/StreamingAssets/Data/archived/nus_boundary.geojson
```

After regenerating → Drive auto-syncs → app downloads on next scheduled 15-minute check (or `ForceReSync()`).

---

## Build Troubleshooting Log (2026-04-11)

While attempting to generate the thin APK via headless batchmode (`unity_cached_builder.py`), we encountered a series of cascading build errors:

1. **Missing Android Build Support Module**
   - **Error**: `Error building player because build target was unsupported`
   - **Cause**: The default Unity executable mapped to `2022.3.62f3` which lacked Android modules, while the GUI used `2022.3.62f3-x86_64` where modules were installed.
   - **Fix**: Passed explicit `--unity-exe "C:\Program Files\Unity\Hub\Editor\2022.3.62f3-x86_64\Editor\Unity.exe"` to the script.

2. **Active Build Target Lock**
   - **Error**: UnityException during batchmode because the Editor was last open on `StandaloneWindows64`, causing cross-compilation conflicts.
   - **Fix**: Modified `CDE2501BuildRunner.cs` to explicitly call `EditorUserBuildSettings.SwitchActiveBuildTarget` before initiating the build.

3. **Android Scripting Backend / Architecture Mismatch**
   - **Error**: `UnityException: Target architecture not specified`
   - **Cause**: The runner attempted to set `PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;`, but because the scripting backend wasn't explicitly flipped to `IL2CPP` in batchmode, ARM64 was deemed invalid (Mono doesn't support ARM64).
   - **Fix**: Added `PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);` into the build runner.

4. **"Zombie" Unity Processes**
   - **Error**: `Fatal Error! It looks like another Unity instance is running with this project open.`
   - **Cause**: Previous failed batchmode runs left hanging `-batchmode` Unity or `bee_backend.exe` background processes.
   - **Fix**: Terminated rogue Unity and Bee processes; deleted the `Temp/UnityLockfile`.

5. **The `desktop.ini` Cloning Conflict**
   - **Error**: `ArgumentException: You're trying to copy to ... bin/Data/desktop.ini more than once`
   - **Cause**: Windows automatically generates hidden `desktop.ini` files inside standard folders. Because the project includes raw `street_view` frames imported from Google Drive, multiple `desktop.ini` files were caught in the `StreamingAssets` bundle scope, causing Unity's Android build backend (Bee) to try to pack them all to the same build root path.
   - **Fix**: Recursively force-deleted all `desktop.ini` files in the project. Terminated hanging Bee build daemons and completely renamed/deleted `Library/Bee` to bypass the corrupted IL2CPP compilation cache.

6. **`--force` Build Still Spent Minutes Scanning Inputs**
   - **Error**: `unity_cached_builder.py --force` appeared hung before Unity launched.
   - **Cause**: Fingerprint scanning ran even on forced builds.
   - **Fix**: Updated script to skip full fingerprint scan when `--force` is used.

7. **Drive Permission Page Saved as Data File**
   - **Error**: Data sync could save an HTML auth/share page if Drive links were not publicly shared.
   - **Cause**: Download path accepted any successful HTTP payload without content validation.
   - **Fix**: Added HTML payload detection and fail-fast logs with explicit share-permission guidance.
