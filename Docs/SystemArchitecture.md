# CDE2501 AR Wayfinding - System Architecture

## 1) Module Overview
- `Location`: GPS + compass ingestion, smoothing, heading updates, and PC simulation controls.
- `Elevation`: Manual level confirmation and smart/always/manual prompting policy.
- `IndoorGraph`: Multi-level graph data model and JSON loading/validation.
- `Profiles`: Safety weights and constraints for Normal Elderly / Wheelchair modes.
- `Routing`: Safety-weighted A*, route cache/cooldown, and revalidation orchestration.
- `AR`: Stable, high-contrast arrow anchored ahead of user.
- `UI`: Destination selection, distance/status panels, minimap, route lines, and hover tooltips.
- `Street View`: Full-screen image-based scene explorer with route-filtered node sets.
- `Video Mapping`: Route-filtered video frame markers and manifest loading.
- `Data`: Local CRUD for Areas of Interest with JSON persistence and boundary filters.

## 2) Runtime Flow
1. App starts and loaders copy seed JSON from `StreamingAssets/Data` to `persistentDataPath/Data` if missing.
2. Graph, locations, and routing profiles are loaded.
3. GPS/compass begin polling (`Input.location`, `Input.compass`).
4. Level manager evaluates if level confirmation prompt is required.
5. User selects destination, mode, and optional rain mode.
6. Route calculator runs safety-weighted A*.
7. UI renders route line + minimap + optional street-view/video overlays.
8. AR arrow points to next guidance direction while UI shows distance and reassurance text.
9. Route recalculates when destination/mode/rain/level changes, movement checks, or off-route events occur.

## 3) Data Model Diagrams (Text)

### Indoor Graph
```text
GraphData
├── metadata: GraphMetadata
│   ├── estateName: string
│   └── version: string
├── nodes: Node[]
│   ├── id: string
│   ├── position: Vector3(x,y,z)
│   ├── elevationLevel: int
│   ├── hasStairs: bool
│   ├── slopeLevel: float
│   ├── lightingLevel: float
│   ├── clutterLevel: float
│   ├── widthLevel: float
│   └── sheltered: bool
└── edges: Edge[]
    ├── fromNode: string
    ├── toNode: string
    ├── distance: float
    ├── slope: float
    ├── hasStairs: bool
    ├── sheltered: bool
    ├── clutter: float
    ├── lighting: float
    └── width: float
```

### Routing
```text
RouteRequest
├── startNodeId: string
├── endNodeId: string
├── currentLevel: ElevationLevel
├── mode: RoutingMode
└── rainMode: bool

RouteResult
├── nodePath: List<string>
├── totalCost: float
├── totalDistance: float
├── success: bool
└── message: string
```

### AOI / Destinations
```text
LocationPoint
├── name: string
├── type: string
├── gps_lat: double
├── gps_lon: double
└── indoor_node_id: string
```

## 4) Safety Cost Equation
```text
TotalCost =
  wDistance * distance
+ wSlope * effectiveSlope
+ wStairs * hasStairs
+ wClutter * clutter
+ wDarkness * (1 - lighting)
+ wNarrowWidth * (1 - width)
+ rainPenalty

effectiveSlope = rainMode ? slope * rainSlopeMultiplier : slope
rainPenalty = rainMode && !sheltered ? wUnshelteredRain : 0
```

Wheelchair extras:
- stairs: add `wheelchairStairsBlockCost` (effectively blocked)
- narrow edges (`width < minWidthPassable`): add large penalty

## 5) Manual Level Confirmation Policy
- `Always`: prompt at route start / transition checks.
- `Smart` (default): prompt on low confidence, connector transitions, or repeated off-route.
- `ManualOnly`: no auto prompt; user changes level explicitly.

## 6) AR Guidance Constraints
- Show only one large arrow.
- Anchor at `1.5m` forward and `0.2m` above user/camera.
- Smooth rotation to suppress jitter.
- Keep UI minimal: distance + reassurance message.
- No flashing/rapid visual effects.
