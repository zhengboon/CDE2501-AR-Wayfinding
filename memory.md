# CDE2501 AR Wayfinding — Memory

> Living document. Updated: 2026-04-16 (Crash triage pass 2: Android XR startup disabled, rebuild blocked by Unity license token)

---

## Current Goal

**Generate a thin APK** where the Android app:
1. Ships only the minimum required files in the APK itself
2. Downloads all large/regeneratable data files from Google Drive on first launch
3. Caches downloaded files in `Application.persistentDataPath/Data/`
4. Updates automatically every 15 minutes when Drive files change (including background checks while app is open)

---

## Active Incident (2026-04-16)

### Symptom

- Latest APK still **instantly closes on launch** on target Android device (user report).

### Verified So Far

1. APK build succeeds in batch mode (`Build Finished, Result: Success`).
2. Final generated Android manifest is now patched late-postprocess to:
   - `com.google.ar.core = optional`
   - `android.hardware.camera.ar required=false`
   - `com.google.ar.core.depth required=false`
   - `UnityPlayerActivity hardwareAccelerated=true`
3. Build pipeline no longer failing from stale Bee/Gradle lock folders after process cleanup.
4. Android XR startup dependence was removed:
   - `Assets/XR/XRGeneralSettingsPerBuildTarget.asset` now has Android `m_Loaders: []`
   - Android `m_InitManagerOnStart: 0`
5. Two rebuild attempts after this change failed before build execution due Unity auth:
   - `UnityBuildCache/logs/unity_build_20260416_014650.log`
   - `UnityBuildCache/logs/unity_build_20260416_015002.log`
   - both report `[Licensing::Module] Error: Access token is unavailable; failed to update`

### Next Immediate Triage Step

1. Refresh Unity Hub login/token and rerun Android build.
2. Install rebuilt APK on target device and re-check launch behavior.

---

## Resume Completion (2026-04-16)

### Footage Integration Status

1. **Integrated into runtime routing:** added `Assets/StreamingAssets/Data/video_route_hints.json` generated from NUS annotation docs.
2. **A* hook completed end-to-end:** `RouteCalculator` now loads route hints and passes edge multipliers into `AStarPathfinder.FindPath(...)`.
3. **Graph-aware loading:** route hints are only applied when manifest `graphHint` matches active graph context.
4. **Cache safety:** route cache key now includes route-hint revision so stale pre-hint routes are not reused.
5. **Annotation docs marked:** integration state/mapped node IDs are recorded in:
   - `Docs/nus_video_annotations.tsv`
   - `Docs/nus_path_segments.tsv`
   - `Docs/nus_landmark_reference.tsv`
6. **Partial segment flag retained:** Home-linked segments remain marked `partial` pending explicit `Home` node confirmation in NUS graph.

### Build Recovery Notes

1. Repeated Android build failures were caused by stale/locked Bee artifacts (`Library/Bee/artifacts/Android/Split`, `ManagedStripped`, `il2cppOutput`).
2. Additional intermittent failure came from stale XR temp files (`Assets/XR/Temp/XRSimulationPreferences.asset*`, `XRSimulationRuntimeSettings.asset*`).
3. Clearing those stale folders/files before rebuild restored successful APK generation.

---

## Sleep Handoff Checkpoint (2026-04-15, late session)

### What I Have Done

1. Logged all newly provided NUS route videos and cues into repo docs:
   - `Docs/nus_video_annotations.tsv`
   - `Docs/nus_path_segments.tsv`
   - `Docs/nus_landmark_reference.tsv`
2. Confirmed the repeated `1MU0UwMaRMlhYXNFY8caOgxxDrzam2unW` input was deduplicated (no duplicate annotation row kept).
3. Started route-bias implementation for AR red-line quality by adding optional edge weighting to A*:
   - `Assets/Scripts/Routing/AStarPathfinder.cs`
   - `FindPath(...)` now accepts `Func<string, string, float> edgeCostMultiplierProvider`
   - Multiplier is sanitized and clamped before applying to edge cost.
4. Decoded user plus-code landmarks in terminal for mapping preparation (session computation, not yet persisted into runtime config).

### What I Am Doing

1. Wiring the video-derived path knowledge into runtime routing so frequently walked segments are preferred where appropriate.
2. Preparing to connect landmark/segment hints to graph node pairs and pass multipliers through `RouteCalculator` into `AStarPathfinder`.
3. Keeping Street View datasets outside Unity assets (no delete), aligned with your earlier instruction.

### What I Plan To Do Next (resume order)

1. Add a route-hints manifest (likely in `StreamingAssets/Data/`) built from the annotation TSV files.
2. Implement route-hints loader and apply edge multipliers in route computation.
3. Validate no regressions with a quick route sanity pass (NUS start/end pairs).
4. Update `README.md`, `index.html`, and this `memory.md` to mark footage integration status as completed.
5. Run the Android build command again and record new APK size + build report.

### Important Current Working State

- `git status` currently includes modified tracked files from prior sessions:
  - `Assets/Plugins/Android/AndroidManifest.xml`
  - `Assets/Scripts/Data/DataSyncManager.cs`
  - `Assets/Scripts/Routing/AStarPathfinder.cs`
  - `Assets/Scripts/UI/QuickStartBootstrap.cs`
  - `Assets/XR/Resources/XRSimulationRuntimeSettings.asset.meta`
  - `Assets/XR/UserSimulationSettings/Resources/XRSimulationPreferences.asset.meta`
  - `ProjectSettings/ProjectSettings.asset`
  - `scripts/unity_cached_builder.py`
- New untracked items:
  - `.tmp_pluscode_venv/`
  - `Docs/nus_video_annotations.tsv`
  - `Docs/nus_path_segments.tsv`
  - `Docs/nus_landmark_reference.tsv`

This checkpoint is intentionally explicit so work can resume safely after machine sleep with no context loss.

---

## Latest Verified Android Build (2026-04-16)

- Command used:
  `python scripts/unity_cached_builder.py --force --target Android --output Builds/Android/CDE2501-Wayfinding.apk --unity-exe "C:\Program Files\Unity\Hub\Editor\2022.3.62f3-x86_64\Editor\Unity.exe"`
- Result: **Build Succeeded**
- Output: `Builds/Android/CDE2501-Wayfinding.apk`
- File size: **33,609,658 bytes** (~32.1 MB)
- Build duration: **291.409 s** (cached builder report)
- Report: `UnityBuildCache/latest_build_report.md`
- Notes:
  - Log scanner still catches licensing client warnings early in startup, but Unity resolves entitlement and the final build succeeds.
  - Unity build log confirms Android target with `Build Finished, Result: Success` and `Total errors: 0`.
  - `CDE2501BuildRunner` reported build stage time `235.850 s`.
  - Project-level C# warnings are cleared for this pass; remaining warnings are immutable package warnings from Unity packages.
  - `ProjectSettings.asset` currently keeps `preloadedAssets: []` for build stability and parity with latest successful run.
  - `unity_cached_builder.py` now passes explicit CLI args to `CDE2501BuildRunner` so WSL-launched Windows Unity picks Android target/output reliably.

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
           - Compare remote metadata fingerprint (Content-Length + Last-Modified + ETag) and re-download changed files
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

## Code Review Fixes (2026-04-15 pass 6 - Sync resilience + UI polish)

1. **Drive probe fallback**: `DataSyncManager` now falls back from `HEAD` to ranged `GET` (`Range: bytes=0-0`) when metadata HEAD checks are blocked by Drive/CDN/network policy.
2. **Remote size parsing hardening**: runtime checks now parse `Content-Range` totals (when available) and still compare reliable size/fingerprint metadata before deciding to redownload.
3. **Quick Start sync visibility**: overlay status strip now includes a detailed Drive status line (checking, retry-needed, updated, up-to-date) instead of only coarse state.
4. **Overlay lifecycle cleanup**: `QuickStartBootstrap` now releases its generated panel texture on destroy to avoid long-session GUI texture leaks.
5. **AR permission lifecycle safety**: `FlightTrackerARView` now cancels pending camera permission polling when AR is deactivated, preventing camera startup after AR was toggled off.
6. **Warning cleanup**: removed the `CS0162` unreachable warning introduced in the AR permission coroutine by making branch exits explicit per platform.

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
3. **Gyroscope Sync**: Added `SyncGyro()` and a Sync Gyro button in the AR overlay. Captures current pitch as offset so the view zeroes at the user's holding angle.

---

## Code Review Fixes (2026-04-14)

1. **Gyroscope math rewritten**: New transform: `new Quaternion(-x,-y,z,w)` then `Euler(-90,0,0)` portrait correction. Labels now move correctly when phone tilts.
2. **Pitch-driven label Y**: screenY now computed from live gyro pitch. Old distance-based lerp removed.
3. **Auto Gyro Sync**: AR view auto-zeroes pitchOffset 1.5 s after activation. Manual Sync Gyro button still available.
4. **GPS Snap**: Added `GPSManager.SnapToCurrentGPS()` - snaps SmoothedPoint=RawPoint instantly. Shown as Snap GPS button (visible when GPS Ready). Triggers route recalc.
5. **AR status HUD**: Status pill in AR view (top-right) shows GPS/Compass ready and live pitch.
6. **FlightTrackerARView rewrite**: Removed old gyroOffset + GetDevicePitch. Replaced with UpdatePitch() in Update() with correct Android coordinate transform.
7. **Share Intent Fix**: Added FileProvider and file_paths config to AndroidManifest so the Telemetry sharing intent doesn't silent crash.


## Runtime Update Expansion (2026-04-13)

1. **Update interval changed to 15 minutes** (`updateCheckIntervalHours = 0.25f`), including continuous background checks while app remains open.
2. **Drive sync scope expanded** from only 7 required files to include optional map PNG/JSON runtime assets.
3. **Live runtime refresh**: `QuickStartBootstrap` now subscribes to `DataSyncManager.OnFilesUpdated` and hot-reloads changed graph, locations, boundary, routing profiles, minimap texture, and reference map texture.
4. **Persistent-first loading**: minimap/reference map loaders now prefer `persistentDataPath/Data/` before falling back to bundled `StreamingAssets`.
5. **Stale Drive IDs corrected** for `nus_locations.json` and `queenstown_boundary.geojson`.

---

## Latest Commit Reviewed (2026-04-15)

- Commit: `022b64d` — `fix(sync): harden Drive probes, polish overlay UI, and rebuild Android`
- `DataSyncManager`: runtime update checks now include HEAD-to-ranged-GET fallback probe so restrictive networks do not silently stall Drive updates.
- `QuickStartBootstrap`: status strip now includes Drive detail messaging; overlay texture lifecycle cleanup added.
- `FlightTrackerARView`: camera permission coroutine is now tied to AR lifecycle and is canceled when AR is toggled off.
- Current resume-session work (footage routing integration, docs refresh, and 2026-04-16 APK build) is local and not committed yet.

---

## Build State

### APK Build Command
```bash
python scripts/unity_cached_builder.py --force --target Android --output Builds/Android/CDE2501-Wayfinding.apk
```

### What's Bundled vs Downloaded

**Bundled in APK (StreamingAssets/Data/):**
- Map atlas JSON manifests (6 × ~1 KB)
- `video_route_hints.json` (NUS footage-derived A* edge multipliers)
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
| 022b64d | 2026-04-15 | fix(sync): harden Drive probes, polish overlay UI, and rebuild Android |
| d07c9ce | 2026-04-15 | fix(sync): strengthen runtime update logic and polish quick-start UI |
| 827a0be | 2026-04-14 | fix: update AR auto-sync timing, expand share file patterns, and relocate FileProvider resource |
| 84b7f74 | 2026-04-14 | docs: update index/README with share fix troubleshooting, AR HUD, Snap GPS, latest commit hash |
| 105cf13 | 2026-04-14 | docs: update memory.md with share deep-fix notes |
| 46c6ebe | 2026-04-14 | fix(share): proper FLAGS, wider file scan, deduplicate, clearer status messages |
| 4abea0e | 2026-04-14 | docs: update index.html and README.md with Snap GPS, AR HUD, and share features |
| 63c2ddd | 2026-04-14 | docs: update index.html and README.md with newly added features and fixes |
| 90e6746 | 2026-04-14 | docs: update memory.md |
| 8e0dc64 | 2026-04-14 | chore: remove duplicate syncing files, add .tmp.drivedownload to gitignore |

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


---

## Code Review Fixes (2026-04-14 pass 2 - Share)

1. **FLAG_ACTIVITY_NEW_TASK added**: share intent now includes `0x10000001` (FLAG_GRANT_READ_URI_PERMISSION | FLAG_ACTIVITY_NEW_TASK) instead of just `1`, preventing crashes when called from application context.
2. **Wider file scan**: ShareTelemetryData now searches Telemetry/, RecordedPaths/, and Crashes/ for csv/json/txt/png/jpg screenshots from field sessions.
3. **Deduplication**: HashSet-based dedup prevents duplicate URIs when multiple patterns match the same file.
4. **Descriptive status messages**: Every failure path now shows a visible StatusMessage so the user knows exactly what went wrong instead of a silent no-op.
5. **Cleaner Android Java flow**: activity acquired first, then context from it; intent constructed after URI list is ready, avoiding partially-initialized intent bugs.

---

## Code Review Fixes (2026-04-14 pass 3 - AR/Share follow-up)

1. **Auto gyro sync timing corrected**: `FlightTrackerARView` now waits 1.5 seconds from AR activation (`Time.time - _arActivatedAtTime`) instead of global uptime, so "wait 1.5 s then Sync Gyro" troubleshooting is behaviorally correct.
2. **Share scan expanded again**: added `*.jpeg` to share file patterns so camera/export variants are included consistently.

---

## Code Review Fixes (2026-04-14 pass 4 - Build hardening)

1. **Android resource packaging fix**: moved FileProvider XML resource out of deprecated `Assets/Plugins/Android/res/...` into `Assets/Plugins/Android/FileProviderLib.androidlib/res/xml/file_paths.xml` with a library manifest, matching Unity 2022 Android plugin expectations.
2. **Project settings normalization**: reset `ProjectSettings/ProjectSettings.asset` back to `preloadedAssets: []` to avoid accidental XR preloads changing runtime behavior.
3. **XR GUID stability restored**: reverted unexpected GUID churn in `Assets/XR/Resources/XRSimulationRuntimeSettings.asset.meta` and `Assets/XR/UserSimulationSettings/Resources/XRSimulationPreferences.asset.meta`.
4. **Stale XR temp asset cleanup**: removed generated files under `Assets/XR/Temp/` before build to prevent simulation-preprocessor move/collision failures.

---

## Code Review Fixes (2026-04-15 pass 5 - Sync + UI)

1. **Drive sync detection upgraded**: DataSyncManager no longer relies on `Content-Length` alone; runtime update checks now use `Content-Length + Last-Modified + ETag` fingerprint to catch same-size file edits.
2. **Bootstrap metadata seeding**: for installs without cached remote metadata, sync now seeds fingerprint using HEAD metadata and local file timestamp, preventing silent stale-cache states while avoiding unnecessary full redownloads.
3. **Failure handling fixed**: if runtime HEAD/GET checks fail, status now reports `Drive update check incomplete. Will retry.` and the last-check schedule marker is not advanced.
4. **Manual sync action added**: QuickStart overlay now includes `Sync Now` button to force immediate Drive update checks without waiting for the 15-minute interval.
5. **Overlay UI polish**: refreshed QuickStart panel tint and added a compact runtime status strip (`GPS`, `Compass`, `Drive`) for clearer field-test diagnostics.
6. **Build duration accuracy fix**: `unity_cached_builder.py` now uses `time.perf_counter()` instead of wall-clock time so reported build duration is robust to system clock jumps.

