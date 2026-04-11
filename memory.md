# CDE2501 AR Wayfinding — Memory

> Living document. Updated: 2026-04-11

---

## Current Goal

**Generate a thin APK** where the Android app:
1. Ships only the minimum required files in the APK itself
2. Downloads all large/regeneratable data files from Google Drive on first launch
3. Caches downloaded files in `Application.persistentDataPath/Data/`
4. Updates automatically (hourly) when Drive files change

---

## Architecture Decisions

### Thin APK Split

| What | Location | Reason |
|---|---|---|
| Map atlas `.json` manifests | StreamingAssets/Data/ (bundled) | Tiny (< 2 KB each), tile index only |
| Graph `.json`, locations, boundaries, routing profiles | StreamingAssets/Data/archived/ → Drive | Large, regeneratable, updated between builds |
| Map tile `.png` atlases | StreamingAssets/Data/archived/ | 1–4 MB each, graceful degradation without them |
| `street_view_manifest.json` | StreamingAssets/Data/ (bundled) | ⚠️ 2.8 MB — investigate moving to Drive |
| APK architecture | ARM64 only | ARMv7 dropped to reduce size |
| Min SDK | 24 | ARCore compatibility |

### Data Sync Flow

```
App launch
  ↓
DataSyncManager.Start()
  ├── All 7 files exist locally? → SyncComplete immediately → QuickStartBootstrap.StartAfterSync()
  └── Missing any? → SyncRoutine() → download from Drive → progress bar UI → StartAfterSync()
       └── Hourly update check: HEAD request per file, compare Content-Length, re-download if changed
```

### File Path Resolution (QuickStartBootstrap)

`DataFileExists(fileName)` checks in order:
1. `Application.streamingAssetsPath/Data/{fileName}` — bundled (editor + device)
2. `Application.persistentDataPath/Data/{fileName}` — downloaded cache (device only)

Pattern-based discovery (`FindBestDataFileByPattern`) probes both directories, picks alphabetically latest filename match.

### Drive File IDs (as of cd42bee — 2026-04-10)

Source: `CDE2501-AR-Wayfinding/Assets/StreamingAssets/Data/archived/` (synced via Google Drive desktop)  
Single source of truth — regenerate locally → Drive auto-syncs → app picks up on next hourly check.

| File | Drive ID |
|---|---|
| estate_graph.json | 1rdVh89zKpehzd1_pjbiO15xQluxg-S3B |
| nus_estate_graph.json | 19mJFjc_52qA30apecosIkI4bEit6Jff5 |
| locations.json | 1iudrdcjUA4axr7OlbVNtlX3sHB0351Ru |
| nus_locations.json | 1qAwOHLs0zzBNby82dH4ha98JitLL9m2g |
| routing_profiles.json | 1BgyDOE4ts3V-o5Na4NzfSJic7BZX5HiZ |
| queenstown_boundary.geojson | 1C2j_1QC9jyjbto7bCPQYkSEX6WjzHYze |
| nus_boundary.geojson | 1LZWYApt484SDCGqcK8Q4xXj2cD0AOFMx |

Download URL pattern: `https://drive.usercontent.google.com/download?id={fileId}&export=download`  
**All files must be shared "Anyone with link → Viewer" on Drive.**

---

## Key Scripts / Components

| Component | File | Role |
|---|---|---|
| DataSyncManager | Assets/Scripts/Data/DataSyncManager.cs | Download + cache 7 data files from Drive. OnGUI progress bar. Hourly update check. Share button (Android intent). |
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

## Build State

### APK Build Command
```bash
python scripts/unity_cached_builder.py --force --target Android --output Builds/Android/CDE2501-Wayfinding.apk
```

### What's Bundled vs Downloaded

**Bundled in APK (StreamingAssets/Data/):**
- Map atlas JSON manifests (6 × ~1 KB)
- `street_view_manifest.json` (2.8 MB — ⚠️ candidate for Drive offload)

**Downloaded on first launch from Drive:**
- `estate_graph.json` (~1.1 MB)
- `nus_estate_graph.json` (~421 KB)
- `locations.json` (~1.3 KB)
- `nus_locations.json` (~2.5 KB)
- `routing_profiles.json` (~0.7 KB)
- `queenstown_boundary.geojson` (~1 KB)
- `nus_boundary.geojson` (~2.4 KB)

**Excluded entirely (archived/, not in APK):**
- All map tile PNGs (~1–4 MB each) — minimap and routing work without them

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
2. First launch: Drive sync UI shows (downloads 7 files, ~2 MB total)
3. Tester selects destination, taps **Rec: OFF** to start telemetry
4. Walk route — auto-screenshots at turns and every 10 s
5. Tap **Stop** / **Share** to send CSVs + paths + crash logs via Android share intent
6. Data: `/sdcard/Android/data/<bundle-id>/files/Telemetry/`, `RecordedPaths/`, `Crashes/`

---

## Known Issues / TODOs

- [ ] `street_view_manifest.json` (2.8 MB) still bundled — consider moving to Drive
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

After regenerating → Drive auto-syncs → app downloads on next hourly check (or `ForceReSync()`).
