CDE2501 AR Wayfinding - Handoff Guide
Last updated: 2026-03-18

Overview
- Unity 2022.3 LTS cross-platform AR wayfinding prototype (AR Foundation, ARCore, ARKit).
- Focus area is Queenstown estate simulation and route guidance with safety-weighted indoor routing.
- Current workflow supports PC simulation, minimap, route preview, Street View-like fallback imagery, and auto session recording.

Core Features (Current)
- Weighted A* routing over `estate_graph.json` with profile-based penalties.
- Route modes: Normal Elderly and Wheelchair.
- Rain mode toggle.
- Continuous route refresh and off-route revalidation.
- Start node resolved from current player/camera position.
- Minimap with zoom, pan, route line, and destination selection.
- Minimap runs map-image-first (OneMap/Google tile image) with YouTube overlays disabled by default.
- First frame suppression logic to avoid title-card style `frame_0001` dominance.
- Auto recording of every Play session to MP4 in Editor.

Architecture Snapshot (Current)
- `Location`:
  - `GPSManager`, `CompassManager`, `SimulationProvider`, `LocationSmoother`
  - Handles live device sensors or PC simulation input.
- `Data`:
  - `GraphLoader`, `LocationManager`, `BoundaryConstraintManager`
  - Loads JSON from `StreamingAssets`/`persistentDataPath`, supports AOI CRUD.
- `Routing`:
  - `RouteCalculator` + `AStarPathfinder`
  - Safety-weighted pathing with wheelchair/rain penalties, cache, and cooldown.
- `UI + Visualization`:
  - `QuickStartBootstrap`, `MiniMapOverlay`, `RoutePathVisualizer`,
    `DestinationSelectorUI`, `StreetViewExplorer`, `VideoFrameMapVisualizer`
  - Provides desktop simulation controls, route overlays, map interactions, and
    immersive image-based Street View fallback.

Recent Engineering Improvements (2026-03-18)
- Added directed edge-distance lookup in `RouteCalculator` to speed route distance
  computation and improve continuous refresh performance.
- Added route-signature change detection in:
  - `StreetViewExplorer` (avoid re-filtering/re-snapping scene images on identical routes)
  - `VideoFrameMapVisualizer` (avoid marker clear/rebuild thrash on identical routes)
- Hardened `LocationManager` CRUD name matching (trim + case-insensitive) for
  update/delete/get consistency.

Important Data Notes
- Generated artifacts are intentionally git-ignored:
  - `Assets/StreamingAssets/Data/video_frames/`
  - `Assets/StreamingAssets/Data/video_frame_map.json`
  - `Assets/StreamingAssets/Data/street_view/`
  - `Assets/StreamingAssets/Data/street_view_manifest.json`
- On a new machine, regenerate these files before testing Street View/video mapping.

Project Requirements (New Computer)
- Unity Hub
- Unity Editor `2022.3.62f1` (recommended for this project)
- Unity modules:
  - Android Build Support
  - Android SDK & NDK Tools
  - OpenJDK
- Python 3.10+ (for data build scripts)
- Optional but recommended for better frame extraction:
  - `yt-dlp`
  - `ffmpeg`
- Optional for real Google Street View imagery:
  - Google Maps API key with Street View Static API enabled
- Internet access is required for thumbnail fallback and Google Street View fetches.

OS Dependency Notes
- Ubuntu/Debian (optional tools):
  - `sudo apt update && sudo apt install -y python3 python3-pip ffmpeg`
  - `python3 -m pip install --user yt-dlp`
- Windows:
  - Install Python 3.x
  - Optional: install `ffmpeg` and `yt-dlp` (or keep thumbnail-only mode)

Unity Packages Used
From `Packages/manifest.json`:
- `com.unity.xr.arfoundation` 5.2.2
- `com.unity.xr.arcore` 5.2.2
- `com.unity.xr.arkit` 5.2.2
- `com.unity.textmeshpro` 3.0.9
- `com.unity.recorder` 4.0.3

First-Time Setup
1. Clone repository.
2. Open project in Unity 2022.3.62f1.
3. Wait for package import/compile to finish.
4. Verify XR provider setup:
   - Project Settings > XR Plug-in Management > Android: ARCore enabled.
   - Project Settings > XR Plug-in Management > iOS: ARKit enabled.
5. Open scene `Assets/Scenes/Main.unity` (if not already open).
6. Press Play to validate baseline simulation.

Data Generation (Optional: only if re-enabling Street View / YouTube fallback systems)
Run commands from repository root.

1) Build mapped video frames (fallback thumbnail set)
```bash
python3 scripts/build_video_frame_map.py --limit 24 --thumbnail-only --max-fallback-thumbnails 6
```

2) Build Street View manifest (fallback mode, frame_0001 avoided by default)
```bash
python3 scripts/build_street_view_map.py --kml cde2501.kml --polygon-name "Site area"
```

3) Optional: allow frame_0001 fallback if needed for coverage
```bash
python3 scripts/build_street_view_map.py --kml cde2501.kml --polygon-name "Site area" --allow-frame0001-fallback
```

4) Optional: if `yt-dlp` and `ffmpeg` are installed, build from actual video frames
```bash
python3 scripts/build_video_frame_map.py --limit 24 --frame-interval 12
python3 scripts/build_street_view_map.py --kml cde2501.kml --polygon-name "Site area"
```

5) Optional: Google Street View enabled build
```bash
export GOOGLE_MAPS_API_KEY="YOUR_KEY"
python3 scripts/build_street_view_map.py --kml cde2501.kml --polygon-name "Site area"
```
PowerShell equivalent:
```powershell
$env:GOOGLE_MAPS_API_KEY="YOUR_KEY"
py -3 scripts/build_street_view_map.py --kml cde2501.kml --polygon-name "Site area"
```

Runtime Controls
Quick Start Overlay
- `N` / `P`: destination next/previous
- `R`: recalculate route
- `1`: Normal Elderly mode
- `2`: Wheelchair mode
- `T`: rain mode toggle
- Street View/YouTube frame systems are disabled by default

PC Simulation Controls
- WASD: move
- `Shift`: sprint
- `Q` / `E` or Left/Right arrows: yaw turn
- `R` / `F` or Up/Down arrows: pitch look
- `PageUp` / `PageDown`: vertical offset
- `F2`: simulation mode toggle
- `F1`: simulation panel toggle

Auto Session Recording
- Implemented in `Assets/Scripts/Editor/AutoPlaySessionRecorder.cs`.
- Every Play session is automatically saved as:
  - `Recordings/AutoSessions/session_YYYYMMDD_HHMMSS.mp4`
- Menu:
  - `CDE2501 > Session Recorder > Enabled`
  - `CDE2501 > Session Recorder > Open Output Folder`

Verification Checklist (After Setup)
1. Press Play.
2. Confirm Quick Start shows routing ready, graph loaded, and destinations available.
3. Toggle destination and recalculate route (`R`).
4. Confirm minimap route line updates from current position.
5. Toggle Street View (`Y`) and verify image changes while moving nodes.
6. Stop Play and confirm auto-recording file is created.

Troubleshooting
- Issue: Street View is blank or says waiting for route.
  - Cause: route-only filtering is enabled.
  - Fix: choose destination and press `R` to produce a valid route.

- Issue: Street View starts on intro/title-card images.
  - Fix: in `StreetViewExplorer`, keep `filterEarlyFallbackFrames=true` and set `minAllowedFallbackFrameIndex=5` (or higher).
  - Fix: regenerate mappings with real extracted frames (`yt-dlp` + `ffmpeg`) instead of thumbnail-only fallback.

- Issue: YouTube/image overlays appear again unexpectedly.
  - Fix: in `QuickStartBootstrap`, keep `disableYoutubeImageSystems=true`.
  - Fix: keep `autoCreateVideoFrameMap=false` and `autoCreateStreetViewExplorer=false`.

- Issue: Old images still appear after regeneration.
  - Fix: run `Assets > Refresh` and restart Play Mode.
  - Fix: clear persistent app data or keep `resetPersistentDataOnStart` enabled in `QuickStartBootstrap`.

- Issue: Recorder compile/package error.
  - Fix: open Package Manager and confirm `Unity Recorder` is installed.
  - Fix: re-resolve packages after network recovery.

- Issue: Too many repeated fallback images.
  - Fix: install `yt-dlp` and `ffmpeg`, rebuild with non-thumbnail mode.
  - Fix: tune `video_route_overrides.json` and rebuild manifests.

- Issue: iOS/Android deploy problems.
  - Fix: verify permissions and XR provider settings in Player Settings.

Handoff Notes
- If this project is passed to another machine, do these first:
  - Open in Unity 2022.3.62f1.
  - Let packages restore.
  - Regenerate `video_frame_map.json` and `street_view_manifest.json` using commands above.
- Do not rely on existing generated `video_frames` and `street_view` folders being present in git.

Main Files to Know
- `Assets/Scripts/UI/QuickStartBootstrap.cs`
- `Assets/Scripts/UI/MiniMapOverlay.cs`
- `Assets/Scripts/UI/StreetViewExplorer.cs`
- `Assets/Scripts/UI/VideoFrameMapVisualizer.cs`
- `Assets/Scripts/Routing/RouteCalculator.cs`
- `scripts/build_video_frame_map.py`
- `scripts/build_street_view_map.py`
- `Assets/StreamingAssets/Data/estate_graph.json`
- `Assets/StreamingAssets/Data/locations.json`
- `Assets/StreamingAssets/Data/routing_profiles.json`
