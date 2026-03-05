# Unity Integration and Deployment

## 1) Unity Setup (2022 LTS)
1. Create a 3D project.
2. Install packages:
- AR Foundation
- ARCore XR Plugin
- ARKit XR Plugin
- XR Plugin Management
3. In XR Plugin Management:
- Android: enable ARCore
- iOS: enable ARKit

## 2) Scene Setup
1. Add `ARSession` and `XROrigin` (or `ARSessionOrigin`).
2. Use AR Camera under `XROrigin`.
3. Add a bootstrap GameObject and attach:
- `GraphLoader`
- `RouteCalculator`
- `GPSManager`
- `CompassManager`
- `LevelManager`
- `LocationManager`
- `ReassuranceManager`
- `DestinationSelectorUI`
- `ArrowRenderer`
4. Link serialized references in Inspector.

## 3) Permissions
### Android
- Camera permission
- Fine location / coarse location (for GPS)

### iOS (`Info.plist`)
- `NSCameraUsageDescription`
- `NSLocationWhenInUseUsageDescription`
- `NSMotionUsageDescription`

## 4) Data Files
Seed files are at:
- `Assets/StreamingAssets/Data/estate_graph.json`
- `Assets/StreamingAssets/Data/locations.json`
- `Assets/StreamingAssets/Data/routing_profiles.json`

On first run, files are copied to:
- `Application.persistentDataPath/Data/...`

Runtime CRUD updates are saved only in persistent data.

## 5) Deployment
1. Android: build APK/AAB and verify runtime permissions.
2. iOS: export Xcode project, set signing, verify plist strings.
3. Perform on-site walk tests in both modes:
- Normal Elderly
- Wheelchair
4. Toggle rain mode and compare path decisions.
5. Tune JSON weights based on logs and field feedback.

## 6) Testing Checklist
- GPS drift smoothing reduces heading flicker.
- Compass smoothing stabilizes outdoor arrow rotation.
- Wrong level triggers smart prompt and can be corrected manually.
- Rain mode avoids unsheltered/slippery routes when alternatives exist.
- Wheelchair mode avoids stairs (or heavily penalizes when unavoidable).
- Narrow corridor penalties influence route choice.
- UI remains minimal: next guidance + large distance + reassurance text.
- Android and iOS both pass startup, tracking, route generation, and guidance loop.
