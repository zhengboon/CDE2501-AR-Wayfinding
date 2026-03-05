using System.Collections;
using System.IO;
using CDE2501.Wayfinding.Data;
using CDE2501.Wayfinding.Elevation;
using CDE2501.Wayfinding.IndoorGraph;
using CDE2501.Wayfinding.Location;
using CDE2501.Wayfinding.Profiles;
using CDE2501.Wayfinding.Routing;
using UnityEngine;

namespace CDE2501.Wayfinding.UI
{
    public class QuickStartBootstrap : MonoBehaviour
    {
        [Header("Auto Setup")]
        [SerializeField] private bool autoCreateManagers = true;
        [SerializeField] private bool showStatusOverlay = true;
        [SerializeField] private string startNodeId = "QTMRT";
        [SerializeField] private bool useNearestNodeAsStart = true;
        [SerializeField] private int destinationIndex = 1;
        [SerializeField] private bool autoDriveCubeFromSimulation = true;
        [SerializeField] private string simulationTargetObjectName = "Main Camera";
        [SerializeField] private bool forceCameraPerspectiveControl = true;
        [SerializeField] private bool detachCameraChildrenAtStart = false;
        [SerializeField] private bool autoCreateGraphPreview = false;
        [SerializeField] private bool autoCreateDestinationMarkers = true;
        [SerializeField] private bool autoCreateRoutePathPreview = true;
        [SerializeField] private bool resetPersistentDataOnStart = true;
        [SerializeField] private bool autoRecalcWhenStartNodeChanges = true;
        [SerializeField, Min(0.1f)] private float startNodeCheckIntervalSeconds = 0.5f;
        [Header("Baritone-Inspired Pathing")]
        [SerializeField] private bool baritoneStyleStartResolution = true;
        [SerializeField, Min(0f)] private float startSnapExtraRadiusMeters = 2f;
        [SerializeField, Min(0f)] private float startVerticalPenaltyWeight = 0.35f;
        [SerializeField, Min(0f)] private float startForwardBiasMeters = 0.75f;
        [SerializeField] private bool routeRevalidationEnabled = true;
        [SerializeField, Min(0.05f)] private float routeRevalidationIntervalSeconds = 0.25f;
        [SerializeField, Min(0f)] private float routeOffPathDistanceMeters = 2.5f;
        [SerializeField, Min(1)] private int routeInvalidChecksBeforeRecalc = 2;
        [SerializeField] private bool continuousRouteRefresh = true;
        [SerializeField, Min(0.05f)] private float continuousRefreshIntervalSeconds = 0.25f;
        [SerializeField, Min(0f)] private float continuousRefreshMinMoveMeters = 0f;
        [Header("Overlay")]
        [SerializeField, Range(0.8f, 2.5f)] private float overlayScale = 1.0f;
        [SerializeField, Range(14, 48)] private int titleFontSize = 28;
        [SerializeField, Range(12, 40)] private int bodyFontSize = 22;
        [SerializeField] private Vector2 panelSize = new Vector2(940f, 300f);
        [SerializeField] private Vector2 panelStartPosition = new Vector2(120f, 246f);

        private SimulationProvider _simulationProvider;
        private GPSManager _gpsManager;
        private CompassManager _compassManager;
        private GraphLoader _graphLoader;
        private RouteCalculator _routeCalculator;
        private LocationManager _locationManager;
        private LevelManager _levelManager;
        private SimulatedObjectDriver _simulatedObjectDriver;
        private GraphRuntimeVisualizer _graphRuntimeVisualizer;
        private RoutePathVisualizer _routePathVisualizer;
        private DestinationMarkerVisualizer _destinationMarkerVisualizer;

        private string _status = "Initializing...";
        private string _resolvedStartNodeId = "QTMRT";
        private RouteResult _lastRouteResult;
        private bool _locationsLoaded;
        private bool _subscribed;
        private bool _isSeedingFallbackLocations;
        private float _nextStartNodeCheckTime;
        private float _nextRouteRevalidationTime;
        private float _nextContinuousRefreshTime;
        private Vector3 _lastContinuousRefreshPosition;
        private bool _hasContinuousRefreshBaseline;
        private int _consecutiveInvalidRouteChecks;
        private string _lastAutoStartNodeId;
        private Vector2 _panelScroll;
        private GUIStyle _panelStyle;
        private GUIStyle _bodyStyle;
        private Texture2D _panelTexture;
        private Rect _panelRect;
        private bool _panelRectInitialized;

        private void Awake()
        {
            EnsureManagers();
        }

        private void Start()
        {
            if (resetPersistentDataOnStart)
            {
                ResetPersistentDataFolder();
            }

            if (_locationManager == null || _routeCalculator == null || _graphLoader == null)
            {
                _status = "Missing core managers. Enable Auto Setup or attach managers manually.";
                return;
            }

            Subscribe();
            _locationManager.LoadLocations();
            StartCoroutine(WaitAndTryAutoRoute());
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.N))
            {
                CycleDestination(1);
            }

            if (Input.GetKeyDown(KeyCode.P))
            {
                CycleDestination(-1);
            }

            if (Input.GetKeyDown(KeyCode.R))
            {
                RecalculateCurrentRoute();
            }

            if (Input.GetKeyDown(KeyCode.Alpha1))
            {
                _routeCalculator.CurrentMode = RoutingMode.NormalElderly;
                RecalculateCurrentRoute();
            }

            if (Input.GetKeyDown(KeyCode.Alpha2))
            {
                _routeCalculator.CurrentMode = RoutingMode.Wheelchair;
                RecalculateCurrentRoute();
            }

            if (Input.GetKeyDown(KeyCode.T))
            {
                _routeCalculator.RainMode = !_routeCalculator.RainMode;
                RecalculateCurrentRoute();
            }

            MaybeContinuouslyRefreshRoute();
            MaybeRevalidateRouteFromPlayer();
            MaybeAutoRecalculateFromMovement();
        }

        private void OnGUI()
        {
            if (!showStatusOverlay)
            {
                return;
            }

            EnsureOverlayStyles();

            float scale = Mathf.Clamp(overlayScale, 0.8f, 2.5f);
            Matrix4x4 prev = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

            EnsurePanelRect(scale);
            _panelRect = GUI.Window(GetInstanceID() + 1000, _panelRect, DrawStatusWindow, "Quick Start Status", _panelStyle);
            GUI.matrix = prev;
        }

        private void DrawStatusWindow(int id)
        {
            bool shouldRecalculate = false;

            if (_routeCalculator != null)
            {
                bool rain = _routeCalculator.RainMode;
                bool newRain = GUI.Toggle(new Rect(12f, 34f, 140f, 24f), rain, $"Rain: {(rain ? "True" : "False")}");
                if (newRain != rain)
                {
                    _routeCalculator.RainMode = newRain;
                    shouldRecalculate = true;
                }

                bool wheelchair = _routeCalculator.CurrentMode == RoutingMode.Wheelchair;
                bool newWheelchair = GUI.Toggle(new Rect(162f, 34f, 180f, 24f), wheelchair, $"Wheelchair: {(wheelchair ? "True" : "False")}");
                if (newWheelchair != wheelchair)
                {
                    _routeCalculator.CurrentMode = newWheelchair ? RoutingMode.Wheelchair : RoutingMode.NormalElderly;
                    shouldRecalculate = true;
                }
            }

            if (_simulationProvider != null)
            {
                bool sim = _simulationProvider.ForceSimulationMode;
                bool newSim = GUI.Toggle(new Rect(352f, 34f, 150f, 24f), sim, $"Sim Mode: {(sim ? "True" : "False")}");
                if (newSim != sim)
                {
                    _simulationProvider.ForceSimulationMode = newSim;
                }
            }

            bool newNearestStart = GUI.Toggle(new Rect(512f, 34f, 220f, 24f), useNearestNodeAsStart, $"Nearest Start: {(useNearestNodeAsStart ? "True" : "False")}");
            if (newNearestStart != useNearestNodeAsStart)
            {
                useNearestNodeAsStart = newNearestStart;
                shouldRecalculate = true;
            }

            if (shouldRecalculate)
            {
                RecalculateCurrentRoute();
            }

            _resolvedStartNodeId = ResolveStartNodeId();
            string destinationName = "none";
            if (_locationsLoaded && _locationManager.Locations.Count > 0 && destinationIndex >= 0 && destinationIndex < _locationManager.Locations.Count)
            {
                destinationName = _locationManager.Locations[destinationIndex].name;
            }

            string routeMessage = _lastRouteResult == null
                ? "No route yet."
                : (_lastRouteResult.success
                    ? $"Path nodes: {_lastRouteResult.nodePath.Count}, dist: {_lastRouteResult.totalDistance:0.0} m, msg: {_lastRouteResult.message}"
                    : $"Route failed: {_lastRouteResult.message}");

            string text =
                $"{_status}\n" +
                $"Start node: ME -> {_resolvedStartNodeId} | Destination: {destinationName}\n" +
                $"Mode: {(_routeCalculator != null ? _routeCalculator.CurrentMode.ToString() : "missing")} | Rain: {(_routeCalculator != null && _routeCalculator.RainMode)}\n" +
                $"Baritone-style start: {baritoneStyleStartResolution} | Revalidation: {routeRevalidationEnabled}\n" +
                $"Route engine ready: {(_routeCalculator != null && _routeCalculator.IsInitialized)}\n" +
                $"GPS ready: {(_gpsManager != null && _gpsManager.IsReady)} (sim: {(_gpsManager != null && _gpsManager.IsUsingSimulation)})\n" +
                $"Compass ready: {(_compassManager != null && _compassManager.IsReady)} (sim: {(_compassManager != null && _compassManager.IsUsingSimulation)})\n" +
                $"Locations loaded: {_locationsLoaded} (count: {(_locationManager != null ? _locationManager.Locations.Count : 0)})\n" +
                $"Graph preview: {(_graphRuntimeVisualizer != null)}\n" +
                $"Destination markers: {(_destinationMarkerVisualizer != null)}\n" +
                $"Route line preview: {(_routePathVisualizer != null)}\n" +
                $"{routeMessage}\n" +
                "Keys: N/P next/prev destination, R recalc, 1 Elderly, 2 Wheelchair, T rain toggle. Move: Arrows, look: A/D + W/S.";

            Rect viewport = new Rect(12f, 64f, _panelRect.width - 24f, _panelRect.height - 72f);
            float contentHeight = Mathf.Max(viewport.height, _bodyStyle.CalcHeight(new GUIContent(text), viewport.width - 24f) + 12f);
            Rect contentRect = new Rect(0f, 0f, viewport.width - 20f, contentHeight);
            _panelScroll = GUI.BeginScrollView(viewport, _panelScroll, contentRect);
            GUI.Label(new Rect(0f, 0f, contentRect.width, contentRect.height), text, _bodyStyle);
            GUI.EndScrollView();
            GUI.DragWindow(new Rect(0f, 0f, _panelRect.width, 30f));
        }

        private void EnsurePanelRect(float scale)
        {
            float virtualScreenWidth = Screen.width / scale;
            float width = Mathf.Min(virtualScreenWidth - 24f, panelSize.x);

            if (!_panelRectInitialized)
            {
                float startX = panelStartPosition.x;
                if (startX <= 0f)
                {
                    startX = (virtualScreenWidth - width) * 0.5f;
                }

                _panelRect = new Rect(startX, panelStartPosition.y, width, panelSize.y);
                _panelRectInitialized = true;
            }
            else
            {
                _panelRect.width = width;
                _panelRect.height = panelSize.y;
            }

            _panelRect.x = Mathf.Clamp(_panelRect.x, 0f, Mathf.Max(0f, virtualScreenWidth - _panelRect.width));
            _panelRect.y = Mathf.Clamp(_panelRect.y, 0f, Mathf.Max(0f, (Screen.height / scale) - _panelRect.height));
        }

        private void EnsureOverlayStyles()
        {
            if (_panelStyle != null && _bodyStyle != null)
            {
                return;
            }

            _panelTexture = MakeSolidTexture(new Color(0f, 0f, 0f, 0.76f));

            _panelStyle = new GUIStyle(GUI.skin.box)
            {
                normal = { background = _panelTexture },
                border = new RectOffset(8, 8, 8, 8),
                padding = new RectOffset(0, 0, 0, 0),
                fontSize = titleFontSize,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.UpperCenter
            };
            _panelStyle.normal.textColor = Color.white;

            _bodyStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = bodyFontSize,
                alignment = TextAnchor.UpperLeft,
                wordWrap = true
            };
            _bodyStyle.normal.textColor = Color.white;
        }

        private static Texture2D MakeSolidTexture(Color color)
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point
            };
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }

        private void EnsureManagers()
        {
            _simulationProvider = FindObjectOfType<SimulationProvider>();
            _gpsManager = FindObjectOfType<GPSManager>();
            _compassManager = FindObjectOfType<CompassManager>();
            _graphLoader = FindObjectOfType<GraphLoader>();
            _routeCalculator = FindObjectOfType<RouteCalculator>();
            _locationManager = FindObjectOfType<LocationManager>();
            _levelManager = FindObjectOfType<LevelManager>();

            if (!autoCreateManagers)
            {
                return;
            }

            if (_simulationProvider == null)
            {
                _simulationProvider = gameObject.AddComponent<SimulationProvider>();
            }

            if (_gpsManager == null)
            {
                _gpsManager = gameObject.AddComponent<GPSManager>();
            }

            if (_compassManager == null)
            {
                _compassManager = gameObject.AddComponent<CompassManager>();
            }

            if (_graphLoader == null)
            {
                _graphLoader = gameObject.AddComponent<GraphLoader>();
            }

            if (_routeCalculator == null)
            {
                _routeCalculator = gameObject.AddComponent<RouteCalculator>();
            }

            if (_locationManager == null)
            {
                _locationManager = gameObject.AddComponent<LocationManager>();
            }

            if (_levelManager == null)
            {
                _levelManager = gameObject.AddComponent<LevelManager>();
            }

            if (autoDriveCubeFromSimulation)
            {
                SetupSimulationTargetDriver();
            }

            if (autoCreateGraphPreview)
            {
                SetupGraphPreview();
            }

            if (autoCreateDestinationMarkers)
            {
                SetupDestinationMarkers();
            }

            if (autoCreateRoutePathPreview)
            {
                SetupRoutePathPreview();
            }
        }

        private void SetupSimulationTargetDriver()
        {
            if (_simulationProvider == null)
            {
                return;
            }

            GameObject targetObject = null;
            if (forceCameraPerspectiveControl && Camera.main != null)
            {
                targetObject = Camera.main.gameObject;
            }

            if (targetObject == null)
            {
                targetObject = GameObject.Find(simulationTargetObjectName);
            }

            if (targetObject == null && Camera.main != null)
            {
                targetObject = Camera.main.gameObject;
            }

            if (targetObject == null)
            {
                return;
            }

            simulationTargetObjectName = targetObject.name;

            if (detachCameraChildrenAtStart && Camera.main != null && targetObject == Camera.main.gameObject)
            {
                DetachDirectChildren(Camera.main.transform);
            }

            SimulatedObjectDriver[] existingDrivers = FindObjectsOfType<SimulatedObjectDriver>();
            for (int i = 0; i < existingDrivers.Length; i++)
            {
                SimulatedObjectDriver driver = existingDrivers[i];
                if (driver != null && driver.gameObject != targetObject)
                {
                    Destroy(driver);
                }
            }

            _simulatedObjectDriver = targetObject.GetComponent<SimulatedObjectDriver>();
            if (_simulatedObjectDriver == null)
            {
                _simulatedObjectDriver = targetObject.AddComponent<SimulatedObjectDriver>();
            }

            _simulatedObjectDriver.SetSimulationProvider(_simulationProvider);
            _simulatedObjectDriver.SetTarget(targetObject.transform);
            _simulatedObjectDriver.SetLockY(false);
        }

        private static void DetachDirectChildren(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                Transform child = parent.GetChild(i);
                child.SetParent(null, true);
            }
        }

        private void SetupGraphPreview()
        {
            _graphRuntimeVisualizer = FindObjectOfType<GraphRuntimeVisualizer>();
            if (_graphRuntimeVisualizer == null)
            {
                _graphRuntimeVisualizer = gameObject.AddComponent<GraphRuntimeVisualizer>();
            }

            _graphRuntimeVisualizer.SetGraphLoader(_graphLoader);
        }

        private void SetupDestinationMarkers()
        {
            _destinationMarkerVisualizer = FindObjectOfType<DestinationMarkerVisualizer>();
            if (_destinationMarkerVisualizer == null)
            {
                _destinationMarkerVisualizer = gameObject.AddComponent<DestinationMarkerVisualizer>();
            }

            _destinationMarkerVisualizer.SetGraphLoader(_graphLoader);
            _destinationMarkerVisualizer.SetLocationManager(_locationManager);
        }

        private void SetupRoutePathPreview()
        {
            _routePathVisualizer = FindObjectOfType<RoutePathVisualizer>();
            if (_routePathVisualizer == null)
            {
                _routePathVisualizer = gameObject.AddComponent<RoutePathVisualizer>();
            }

            _routePathVisualizer.SetGraphLoader(_graphLoader);
            _routePathVisualizer.SetRouteCalculator(_routeCalculator);
            _routePathVisualizer.SetStartReferenceTransform(GetStartReferenceTransform());
            _routePathVisualizer.SetIncludeStartReferenceSegment(true);
        }

        private static void ResetPersistentDataFolder()
        {
            string dataPath = Path.Combine(Application.persistentDataPath, "Data");
            if (Directory.Exists(dataPath))
            {
                try
                {
                    Directory.Delete(dataPath, true);
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"Unable to clear persistent data folder '{dataPath}': {ex.Message}");
                }
            }
        }

        private void Subscribe()
        {
            if (_subscribed)
            {
                return;
            }

            if (_locationManager != null)
            {
                _locationManager.OnLocationsChanged += OnLocationsChanged;
            }

            if (_routeCalculator != null)
            {
                _routeCalculator.OnRouteUpdated += OnRouteUpdated;
            }

            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed)
            {
                return;
            }

            if (_locationManager != null)
            {
                _locationManager.OnLocationsChanged -= OnLocationsChanged;
            }

            if (_routeCalculator != null)
            {
                _routeCalculator.OnRouteUpdated -= OnRouteUpdated;
            }

            _subscribed = false;
        }

        private IEnumerator WaitAndTryAutoRoute()
        {
            float timeout = Time.realtimeSinceStartup + 10f;
            while (Time.realtimeSinceStartup < timeout)
            {
                bool routingReady = _routeCalculator != null && _routeCalculator.IsInitialized;
                bool graphReady = _graphLoader != null && _graphLoader.NodesById.Count > 0;
                if (_locationsLoaded && routingReady && graphReady)
                {
                    RecalculateCurrentRoute();
                    yield break;
                }

                yield return null;
            }

            _status = "Startup timeout. Use R to retry route calculation.";
        }

        private void OnLocationsChanged()
        {
            if (_isSeedingFallbackLocations)
            {
                return;
            }

            _locationsLoaded = _locationManager != null && _locationManager.Locations.Count > 0;
            if (!_locationsLoaded)
            {
                SeedFallbackCubeLocations();
                _locationsLoaded = _locationManager != null && _locationManager.Locations.Count > 0;
                if (!_locationsLoaded)
                {
                    _status = "Locations loaded but no destinations found.";
                    return;
                }

                destinationIndex = Mathf.Clamp(destinationIndex, 0, _locationManager.Locations.Count - 1);
                UpdateSelectedDestinationMarker();
                _status = "Fallback cube locations seeded.";
                return;
            }

            if (!HasAnyRouteableDestination())
            {
                SeedFallbackCubeLocations();
                _locationsLoaded = _locationManager != null && _locationManager.Locations.Count > 0;
                destinationIndex = Mathf.Clamp(destinationIndex, 0, _locationManager.Locations.Count - 1);
                UpdateSelectedDestinationMarker();
                _status = "No routeable destinations found in data. Fallback cube locations seeded.";
                return;
            }

            destinationIndex = Mathf.Clamp(destinationIndex, 0, _locationManager.Locations.Count - 1);
            UpdateSelectedDestinationMarker();
            _status = "Locations loaded.";
        }

        private void SeedFallbackCubeLocations()
        {
            if (_locationManager == null)
            {
                return;
            }

            _isSeedingFallbackLocations = true;
            try
            {
                if (_locationManager.GetByName("Queenstown MRT (EW19)") == null)
                {
                    _locationManager.AddLocation(new LocationPoint
                    {
                        name = "Queenstown MRT (EW19)",
                        type = "MRT",
                        gps_lat = 1.294550851849307,
                        gps_lon = 103.8060771559821,
                        indoor_node_id = "QTMRT"
                    });
                }

                if (_locationManager.GetByName("7RV3+XH Singapore") == null)
                {
                    _locationManager.AddLocation(new LocationPoint
                    {
                        name = "7RV3+XH Singapore",
                        type = "Waypoint",
                        gps_lat = 1.2949375,
                        gps_lon = 103.8039375,
                        indoor_node_id = "PC_7RV3_XH"
                    });
                }
            }
            finally
            {
                _isSeedingFallbackLocations = false;
            }
        }

        private void OnRouteUpdated(RouteResult result)
        {
            _lastRouteResult = result;
            _status = result != null && result.success ? "Route updated." : "Route update failed.";
        }

        private void CycleDestination(int step)
        {
            if (!_locationsLoaded || _locationManager.Locations.Count == 0)
            {
                _status = "No destinations available yet.";
                return;
            }

            int count = _locationManager.Locations.Count;
            destinationIndex = (destinationIndex + step) % count;
            if (destinationIndex < 0)
            {
                destinationIndex += count;
            }

            UpdateSelectedDestinationMarker();
            RecalculateCurrentRoute();
        }

        private void RecalculateCurrentRoute()
        {
            if (_routeCalculator == null)
            {
                _status = "RouteCalculator missing.";
                return;
            }

            if (!_routeCalculator.IsInitialized)
            {
                _status = "Route engine still initializing.";
                return;
            }

            if (_locationManager == null || _locationManager.Locations.Count == 0)
            {
                _status = "No destinations loaded yet.";
                return;
            }

            var destination = _locationManager.Locations[destinationIndex];
            if (_graphLoader != null && _graphLoader.GetNode(destination.indoor_node_id) == null)
            {
                _status = $"Destination '{destination.name}' has no matching graph node.";
                return;
            }

            ElevationLevel level = _levelManager != null ? _levelManager.CurrentLevel : ElevationLevel.Deck;
            _resolvedStartNodeId = ResolveStartNodeId();
            _routeCalculator.CalculateIndoorRoute(_resolvedStartNodeId, destination.indoor_node_id, level);
        }

        private void UpdateSelectedDestinationMarker()
        {
            if (_destinationMarkerVisualizer == null || _locationManager == null || _locationManager.Locations.Count == 0)
            {
                return;
            }

            if (destinationIndex < 0 || destinationIndex >= _locationManager.Locations.Count)
            {
                return;
            }

            _destinationMarkerVisualizer.SetSelectedDestination(_locationManager.Locations[destinationIndex].name);
        }

        private string ResolveStartNodeId()
        {
            if (!useNearestNodeAsStart || _graphLoader == null || _graphLoader.NodesById.Count == 0)
            {
                return startNodeId;
            }

            Transform reference = GetStartReferenceTransform();
            if (reference == null)
            {
                return startNodeId;
            }

            if (baritoneStyleStartResolution)
            {
                return ResolveBaritoneStyleStartNode(reference);
            }

            string nearest = FindNearestGraphNodeId(reference.position);
            return string.IsNullOrWhiteSpace(nearest) ? startNodeId : nearest;
        }

        private Transform GetStartReferenceTransform()
        {
            if (forceCameraPerspectiveControl && Camera.main != null)
            {
                return Camera.main.transform;
            }

            GameObject target = GameObject.Find(simulationTargetObjectName);
            if (target != null)
            {
                return target.transform;
            }

            if (Camera.main != null)
            {
                return Camera.main.transform;
            }

            return null;
        }

        private string FindNearestGraphNodeId(Vector3 position)
        {
            string bestId = null;
            float bestDist = float.PositiveInfinity;

            foreach (var kvp in _graphLoader.NodesById)
            {
                Node node = kvp.Value;
                float d = Vector3.Distance(position, node.position);
                if (d < bestDist)
                {
                    bestDist = d;
                    bestId = kvp.Key;
                }
            }

            return bestId;
        }

        private string ResolveBaritoneStyleStartNode(Transform reference)
        {
            // Mirrors Baritone's idea of "pathStart": choose a plausible nearby support point,
            // not just raw nearest distance, with mild forward/vertical bias.
            string nearestId = null;
            float nearestDistance = float.PositiveInfinity;

            foreach (var kvp in _graphLoader.NodesById)
            {
                float d = Vector3.Distance(reference.position, kvp.Value.position);
                if (d < nearestDistance)
                {
                    nearestDistance = d;
                    nearestId = kvp.Key;
                }
            }

            if (string.IsNullOrWhiteSpace(nearestId))
            {
                return startNodeId;
            }

            float maxCandidateDistance = nearestDistance + Mathf.Max(0f, startSnapExtraRadiusMeters);
            float bestScore = float.PositiveInfinity;
            string bestId = nearestId;

            Vector3 forward = reference.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = Vector3.forward;
            }
            forward.Normalize();

            foreach (var kvp in _graphLoader.NodesById)
            {
                Node node = kvp.Value;
                Vector3 delta = node.position - reference.position;
                float dist3D = delta.magnitude;
                if (dist3D > maxCandidateDistance)
                {
                    continue;
                }

                Vector3 deltaXZ = new Vector3(delta.x, 0f, delta.z);
                float distXZ = deltaXZ.magnitude;
                float verticalPenalty = Mathf.Abs(delta.y) * startVerticalPenaltyWeight;

                float forwardBonus = 0f;
                if (distXZ > 0.001f)
                {
                    float alignment = Mathf.Clamp01(Vector3.Dot(forward, deltaXZ / distXZ));
                    forwardBonus = alignment * startForwardBiasMeters;
                }

                float score = distXZ + verticalPenalty - forwardBonus;
                if (score < bestScore)
                {
                    bestScore = score;
                    bestId = kvp.Key;
                }
            }

            return string.IsNullOrWhiteSpace(bestId) ? nearestId : bestId;
        }

        private bool HasAnyRouteableDestination()
        {
            if (_locationManager == null || _graphLoader == null || _graphLoader.NodesById.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < _locationManager.Locations.Count; i++)
            {
                LocationPoint location = _locationManager.Locations[i];
                if (location != null &&
                    !string.IsNullOrWhiteSpace(location.indoor_node_id) &&
                    _graphLoader.GetNode(location.indoor_node_id) != null)
                {
                    return true;
                }
            }

            return false;
        }

        private void MaybeAutoRecalculateFromMovement()
        {
            if (!autoRecalcWhenStartNodeChanges)
            {
                return;
            }

            if (Time.unscaledTime < _nextStartNodeCheckTime)
            {
                return;
            }

            _nextStartNodeCheckTime = Time.unscaledTime + Mathf.Max(0.1f, startNodeCheckIntervalSeconds);

            if (_locationManager == null || _locationManager.Locations.Count == 0)
            {
                return;
            }

            string currentStart = ResolveStartNodeId();
            if (string.IsNullOrWhiteSpace(currentStart))
            {
                return;
            }

            if (string.Equals(currentStart, _lastAutoStartNodeId, System.StringComparison.Ordinal))
            {
                return;
            }

            _lastAutoStartNodeId = currentStart;
            RecalculateCurrentRoute();
        }

        private void MaybeContinuouslyRefreshRoute()
        {
            if (!continuousRouteRefresh)
            {
                return;
            }

            if (Time.unscaledTime < _nextContinuousRefreshTime)
            {
                return;
            }

            _nextContinuousRefreshTime = Time.unscaledTime + Mathf.Max(0.05f, continuousRefreshIntervalSeconds);

            if (_routeCalculator == null || !_routeCalculator.IsInitialized)
            {
                return;
            }

            if (_locationManager == null || _locationManager.Locations.Count == 0)
            {
                return;
            }

            Transform reference = GetStartReferenceTransform();
            if (reference == null)
            {
                return;
            }

            if (!_hasContinuousRefreshBaseline)
            {
                _lastContinuousRefreshPosition = reference.position;
                _hasContinuousRefreshBaseline = true;
            }

            bool movedEnough = continuousRefreshMinMoveMeters <= 0f ||
                               Vector3.Distance(reference.position, _lastContinuousRefreshPosition) >= continuousRefreshMinMoveMeters;
            if (!movedEnough)
            {
                return;
            }

            _lastContinuousRefreshPosition = reference.position;
            RecalculateCurrentRoute();
        }

        private void MaybeRevalidateRouteFromPlayer()
        {
            if (!routeRevalidationEnabled)
            {
                return;
            }

            if (Time.unscaledTime < _nextRouteRevalidationTime)
            {
                return;
            }

            _nextRouteRevalidationTime = Time.unscaledTime + Mathf.Max(0.05f, routeRevalidationIntervalSeconds);

            if (_lastRouteResult == null || !_lastRouteResult.success || _lastRouteResult.nodePath == null || _lastRouteResult.nodePath.Count == 0)
            {
                _consecutiveInvalidRouteChecks = 0;
                return;
            }

            Transform reference = GetStartReferenceTransform();
            if (reference == null)
            {
                _consecutiveInvalidRouteChecks = 0;
                return;
            }

            string currentStart = ResolveStartNodeId();
            bool routeContainsCurrentStart = !string.IsNullOrWhiteSpace(currentStart) && _lastRouteResult.nodePath.Contains(currentStart);
            float distanceToRoute = ComputeDistanceToRouteNodes(reference.position, _lastRouteResult.nodePath);
            bool closeToRoute = distanceToRoute <= routeOffPathDistanceMeters;

            if (routeContainsCurrentStart || closeToRoute)
            {
                _consecutiveInvalidRouteChecks = 0;
                return;
            }

            _consecutiveInvalidRouteChecks++;
            if (_consecutiveInvalidRouteChecks < Mathf.Max(1, routeInvalidChecksBeforeRecalc))
            {
                return;
            }

            _consecutiveInvalidRouteChecks = 0;
            _status = $"Route revalidation triggered (off-route {distanceToRoute:0.0}m).";
            RecalculateCurrentRoute();
        }

        private float ComputeDistanceToRouteNodes(Vector3 position, System.Collections.Generic.List<string> nodePath)
        {
            if (_graphLoader == null || nodePath == null || nodePath.Count == 0)
            {
                return float.PositiveInfinity;
            }

            float best = float.PositiveInfinity;
            for (int i = 0; i < nodePath.Count; i++)
            {
                Node node = _graphLoader.GetNode(nodePath[i]);
                if (node == null)
                {
                    continue;
                }

                float d = Vector3.Distance(position, node.position);
                if (d < best)
                {
                    best = d;
                }
            }

            return best;
        }
    }
}
