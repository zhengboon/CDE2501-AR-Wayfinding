CDE2501 AR Wayfinding - Progress + What To Do Next
Last updated: 2026-03-12

Overview
- Unity 2022.3 LTS AR wayfinding project for Queenstown estate simulation and AR deployment.
- Current focus: route quality, minimap clarity, and Street View-like ground reference inside the `cde2501.kml` polygon.

Current Implemented Features
1) Routing + Simulation
- Weighted A* routing over estate graph (`estate_graph.json`).
- Route starts from current simulated player position.
- Continuous route refresh and revalidation logic.
- Elderly/Wheelchair + Rain modes in quick-start panel.

2) Map + Minimap
- High-resolution stitched OSM map atlas support.
- Movable/zoomable minimap with destination selection and hover.
- On-ground map reference quad in simulation.

3) Video Mapping Pipeline
- Curated video CSV is used as offline input data (no CSV runtime window now).
- Mapped video-frame visualizer in scene (billboard image markers).
- Generator script:
  - `scripts/build_video_frame_map.py`
  - Output:
    - `Assets/StreamingAssets/Data/video_frame_map.json`
    - `Assets/StreamingAssets/Data/video_frames/...`

4) Hybrid Street View Data Builder (NEW)
- Script rebuilt for your KML polygon + hybrid sourcing:
  - `scripts/build_street_view_map.py`
- Reads polygon `Site area` from `cde2501.kml`.
- Converts graph nodes to lat/lon using location fit.
- For nodes inside polygon:
  - Uses Google Street View (if API key + coverage available)
  - Falls back to nearest YouTube mapped frame if Google view unavailable
- Output:
  - `Assets/StreamingAssets/Data/street_view_manifest.json`
  - `Assets/StreamingAssets/Data/street_view/google/...` (when API works)

5) Street View Explorer Runtime (NEW)
- In-Unity Street View immersive mode is implemented (full-screen normal view, no popup window):
  - `Assets/Scripts/UI/StreetViewExplorer.cs`
- Auto-created from Quick Start bootstrap.
- Shows best available image per node:
  - Google Street View heading image when available
  - YouTube fallback image otherwise
- Controls:
  - `Y` toggle Street View mode
  - Mouse drag = look around
  - Click orange `GO` hotspot = move to adjacent/nearby node
  - `[` / `]` previous/next node
  - `,` / `.` previous/next heading
  - `H` snap to nearest node from current player/camera

What You Need To Do Now
1) Rebuild mapped video frames (if needed)
- Command:
  - `python3 scripts/build_video_frame_map.py --limit 24 --thumbnail-only`

2) Build hybrid Street View manifest from your KML
- Without Google key (fallback only):
  - `python3 scripts/build_street_view_map.py --kml /mnt/c/Users/zheng/Downloads/cde2501.kml --polygon-name "Site area"`
- With Google key:
  - `export GOOGLE_MAPS_API_KEY="YOUR_KEY"`
  - `python3 scripts/build_street_view_map.py --kml /mnt/c/Users/zheng/Downloads/cde2501.kml --polygon-name "Site area"`

3) Unity verification
- Open `CDE2501-AR-Wayfinding` project.
- `Assets > Refresh`.
- Press Play.
- In Quick Start status panel, confirm:
  - `Video frame map: ... loaded: True, markers: > 0`
  - `Street view explorer: ... loaded: True, nodes: > 0, active: True`
- Press `Y` if Street View mode is hidden.
- Console should show logs from `VideoFrameMapVisualizer` and (if needed) StreetView load status.

Known Limits Right Now
- This shell cannot run Unity Editor; runtime/visual verification must be done on your machine.
- Google Street View downloads require a valid API key and quota billing enabled.
- Without Google key, Street View runs in YouTube fallback mode only.

Next Engineering Tasks
1) Improve polygon coverage:
- Tune `--min-spacing-m` and `--max-google-nodes`.
- Add optional connector generation between selected street-view nodes.

2) Improve Street View UX:
- Add image prefetch for adjacent nodes.
- Add a compact node list picker by nearest POI name.
- Add route-to-street-view-node jump button from minimap click.

Git Notes
- `.gitignore` updated to exclude generated/regeneratable large artifacts:
  - `.tmp-video-map/`
  - `Assets/StreamingAssets/Data/video_frames/`
  - `Assets/StreamingAssets/Data/video_frame_map.json`
