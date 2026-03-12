using System.Collections;
using System.Collections.Generic;
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
        [SerializeField] private bool autoCreateMapReference = true;
        [SerializeField] private bool autoCreateDestinationMarkers = true;
        [SerializeField] private bool autoCreateRoutePathPreview = true;
        [SerializeField] private bool autoCreateMiniMap = true;
        [SerializeField] private bool autoCreateVideoCsvOverlay = true;
        [SerializeField] private bool autoCreateVideoFrameMap = true;
        [SerializeField] private bool resetPersistentDataOnStart = true;
        [SerializeField] private bool autoRecalcWhenStartNodeChanges = true;
        [SerializeField] private bool alwaysRouteFromCurrentPosition = true;
        [SerializeField] private bool forceImmediateRouteRefresh = true;
        [SerializeField, Min(0.1f)] private float startNodeCheckIntervalSeconds = 0.5f;
        [Header("Baritone-Inspired Pathing")]
        [SerializeField] private bool baritoneStyleStartResolution = true;
        [SerializeField] private bool preferCurrentLevelForStartNode = true;
        [SerializeField, Min(0f)] private float startSnapExtraRadiusMeters = 2f;
        [SerializeField, Min(0f)] private float startVerticalPenaltyWeight = 0.35f;
        [SerializeField, Min(0f)] private float startForwardBiasMeters = 0.75f;
        [SerializeField, Min(0f)] private float startNodeSwitchAdvantageMeters = 0.45f;
        [SerializeField, Min(0f)] private float startNodeMaxHoldDistanceMeters = 2.4f;
        [SerializeField] private bool routeRevalidationEnabled = true;
        [SerializeField, Min(0.05f)] private float routeRevalidationIntervalSeconds = 0.25f;
        [SerializeField, Min(0f)] private float routeOffPathDistanceMeters = 2.5f;
        [SerializeField, Min(1)] private int routeInvalidChecksBeforeRecalc = 2;
        [SerializeField] private bool continuousRouteRefresh = true;
        [SerializeField, Min(0.05f)] private float continuousRefreshIntervalSeconds = 0.25f;
        [SerializeField, Min(0f)] private float continuousRefreshMinMoveMeters = 0.2f;
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
        private MapReferenceTileVisualizer _mapReferenceTileVisualizer;
        private RoutePathVisualizer _routePathVisualizer;
        private DestinationMarkerVisualizer _destinationMarkerVisualizer;
        private MiniMapOverlay _miniMapOverlay;
        private VideoMappingCsvOverlay _videoMappingCsvOverlay;
        private VideoFrameMapVisualizer _videoFrameMapVisualizer;
        private BoundaryConstraintManager _boundaryConstraintManager;

        private string _status = "Initializing...";
        private string _resolvedStartNodeId = "QTMRT";
        private string _lastRecalcReason = "Init";
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
        private string _stableStartNodeId;
        private int _stableStartLevel = int.MinValue;
        private Vector2 _panelScroll;
        private GUIStyle _panelStyle;
        private GUIStyle _bodyStyle;
        private Texture2D _panelTexture;
        private Rect _panelRect;
        private bool _panelRectInitialized;
        private bool _destinationDropdownExpanded;
        private Vector2 _destinationDropdownScroll;
        private readonly List<LocationPoint> _uiDestinations = new List<LocationPoint>();
        private bool _uiDestinationsDirty = true;
        private string _selectedDestinationNodeId;
        private string _selectedDestinationName;

        private void Awake()
        {
            if (continuousRefreshMinMoveMeters < 0.5f)
            {
                continuousRefreshMinMoveMeters = 0.5f;
            }

            if (continuousRefreshIntervalSeconds < 0.5f)
            {
                continuousRefreshIntervalSeconds = 0.5f;
            }

            if (routeRevalidationIntervalSeconds < 0.4f)
            {
                routeRevalidationIntervalSeconds = 0.4f;
            }

            if (startNodeSwitchAdvantageMeters < 1f)
            {
                startNodeSwitchAdvantageMeters = 1f;
            }

            if (startNodeMaxHoldDistanceMeters < 4f)
            {
                startNodeMaxHoldDistanceMeters = 4f;
            }

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
                RecalculateCurrentRoute("Manual (R key)");
            }

            if (Input.GetKeyDown(KeyCode.Alpha1))
            {
                _routeCalculator.CurrentMode = RoutingMode.NormalElderly;
                RecalculateCurrentRoute("Mode changed to Elderly");
            }

            if (Input.GetKeyDown(KeyCode.Alpha2))
            {
                _routeCalculator.CurrentMode = RoutingMode.Wheelchair;
                RecalculateCurrentRoute("Mode changed to Wheelchair");
            }

            if (Input.GetKeyDown(KeyCode.T))
            {
                _routeCalculator.RainMode = !_routeCalculator.RainMode;
                RecalculateCurrentRoute("Rain mode toggled");
            }

            if (_miniMapOverlay != null)
            {
                _miniMapOverlay.SetStartReferenceTransform(GetStartReferenceTransform());
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
            string pendingRecalcReason = null;

            if (_routeCalculator != null)
            {
                bool rain = _routeCalculator.RainMode;
                bool newRain = GUI.Toggle(new Rect(12f, 34f, 140f, 24f), rain, $"Rain: {(rain ? "True" : "False")}");
                if (newRain != rain)
                {
                    _routeCalculator.RainMode = newRain;
                    shouldRecalculate = true;
                    pendingRecalcReason = "Rain mode UI toggle";
                }

                bool wheelchair = _routeCalculator.CurrentMode == RoutingMode.Wheelchair;
                bool newWheelchair = GUI.Toggle(new Rect(162f, 34f, 180f, 24f), wheelchair, $"Wheelchair: {(wheelchair ? "True" : "False")}");
                if (newWheelchair != wheelchair)
                {
                    _routeCalculator.CurrentMode = newWheelchair ? RoutingMode.Wheelchair : RoutingMode.NormalElderly;
                    shouldRecalculate = true;
                    pendingRecalcReason = "Mobility mode UI toggle";
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
                pendingRecalcReason = "Nearest Start UI toggle";
            }

            _resolvedStartNodeId = ResolveStartNodeId();
            RefreshUIDestinations();
            string destinationName = GetCurrentDestinationName();

            float destinationRowY = 64f;
            GUI.Label(new Rect(12f, destinationRowY, 108f, 24f), "Destination:", _bodyStyle);
            Rect destinationButtonRect = new Rect(122f, destinationRowY, 344f, 24f);
            if (GUI.Button(destinationButtonRect, destinationName))
            {
                _destinationDropdownExpanded = !_destinationDropdownExpanded;
            }

            bool hasDestinations = _locationsLoaded && _uiDestinations.Count > 0;
            if (hasDestinations)
            {
                if (GUI.Button(new Rect(476f, destinationRowY, 92f, 24f), "Prev"))
                {
                    CycleDestination(-1);
                }

                if (GUI.Button(new Rect(572f, destinationRowY, 92f, 24f), "Next"))
                {
                    CycleDestination(1);
                }
            }

            float dropdownHeight = 0f;
            if (_destinationDropdownExpanded && hasDestinations)
            {
                float rowHeight = Mathf.Max(30f, bodyFontSize + 8f);
                float maxDropdownHeight = Mathf.Min(180f, _panelRect.height * 0.4f);
                dropdownHeight = Mathf.Min(maxDropdownHeight, (_uiDestinations.Count * rowHeight) + 8f);

                Rect dropdownRect = new Rect(122f, destinationRowY + 28f, 344f, dropdownHeight);
                if (Event.current.type == EventType.MouseDown &&
                    !destinationButtonRect.Contains(Event.current.mousePosition) &&
                    !dropdownRect.Contains(Event.current.mousePosition))
                {
                    _destinationDropdownExpanded = false;
                }

                GUI.Box(dropdownRect, GUIContent.none);

                Rect viewport = new Rect(dropdownRect.x + 4f, dropdownRect.y + 4f, dropdownRect.width - 8f, dropdownRect.height - 8f);
                Rect content = new Rect(0f, 0f, viewport.width - 16f, _uiDestinations.Count * rowHeight);
                _destinationDropdownScroll = GUI.BeginScrollView(viewport, _destinationDropdownScroll, content);
                for (int i = 0; i < _uiDestinations.Count; i++)
                {
                    string itemName = _uiDestinations[i].name;
                    string itemLabel = i == destinationIndex ? $"[x] {itemName}" : itemName;
                    if (GUI.Button(new Rect(0f, i * rowHeight, content.width, rowHeight - 2f), itemLabel))
                    {
                        SetDestinationIndex(i, recalculate: true);
                        _destinationDropdownExpanded = false;
                    }
                }

                GUI.EndScrollView();
            }

            if (shouldRecalculate)
            {
                RecalculateCurrentRoute(pendingRecalcReason ?? "UI State Changed");
            }

            string routeMessage = _lastRouteResult == null
                ? "No route yet."
                : (_lastRouteResult.success
                    ? $"Path nodes: {_lastRouteResult.nodePath.Count}, dist: {_lastRouteResult.totalDistance:0.0} m, msg: {_lastRouteResult.message}"
                    : $"Route failed: {_lastRouteResult.message}");

            string text =
                $"{_status}\n" +
                $"Start node: ME -> {_resolvedStartNodeId} | Destination: {destinationName}\n" +
                $"Recalc Reason: {_lastRecalcReason}\n" +
                $"Mode: {(_routeCalculator != null ? _routeCalculator.CurrentMode.ToString() : "missing")} | Rain: {(_routeCalculator != null && _routeCalculator.RainMode)}\n" +
                $"Baritone-style start: {baritoneStyleStartResolution} | Revalidation: {routeRevalidationEnabled}\n" +
                $"Route engine ready: {(_routeCalculator != null && _routeCalculator.IsInitialized)}\n" +
                $"GPS ready: {(_gpsManager != null && _gpsManager.IsReady)} (sim: {(_gpsManager != null && _gpsManager.IsUsingSimulation)})\n" +
                $"Compass ready: {(_compassManager != null && _compassManager.IsReady)} (sim: {(_compassManager != null && _compassManager.IsUsingSimulation)})\n" +
                $"Locations loaded: {_locationsLoaded} (raw: {(_locationManager != null ? _locationManager.Locations.Count : 0)}, usable: {_uiDestinations.Count})\n" +
                $"Boundary active: {(_boundaryConstraintManager != null && _boundaryConstraintManager.HasBoundary)} (rev: {(_boundaryConstraintManager != null ? _boundaryConstraintManager.BoundaryRevision : 0)})\n" +
                $"Graph preview: {(_graphRuntimeVisualizer != null)}\n" +
                $"Map reference: {(_mapReferenceTileVisualizer != null)}\n" +
                $"Destination markers: {(_destinationMarkerVisualizer != null)}\n" +
                $"Route line preview: {(_routePathVisualizer != null)}\n" +
                $"Mini map: {(_miniMapOverlay != null)}\n" +
                $"Video CSV overlay: {(_videoMappingCsvOverlay != null)}\n" +
                $"Video frame map: {(_videoFrameMapVisualizer != null)} (loaded: {(_videoFrameMapVisualizer != null && _videoFrameMapVisualizer.ManifestReady)}, markers: {(_videoFrameMapVisualizer != null ? _videoFrameMapVisualizer.MarkerCount : 0)})\n" +
                $"{routeMessage}\n" +
                "Keys: N/P next/prev destination, R recalc, 1 Elderly, 2 Wheelchair, T rain toggle, V video list. Move: Arrows, look: A/D + W/S. MiniMap: wheel zoom, left-drag pan/click select, right-drag move window, F follow.";

            float infoTop = destinationRowY + 32f + dropdownHeight + 4f;
            Rect infoViewport = new Rect(12f, infoTop, _panelRect.width - 24f, Mathf.Max(24f, _panelRect.height - infoTop - 8f));
            float contentHeight = Mathf.Max(infoViewport.height, _bodyStyle.CalcHeight(new GUIContent(text), infoViewport.width - 24f) + 12f);
            Rect contentRect = new Rect(0f, 0f, infoViewport.width - 20f, contentHeight);
            _panelScroll = GUI.BeginScrollView(infoViewport, _panelScroll, contentRect);
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
            _boundaryConstraintManager = FindObjectOfType<BoundaryConstraintManager>();

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

            if (_boundaryConstraintManager == null)
            {
                _boundaryConstraintManager = gameObject.AddComponent<BoundaryConstraintManager>();
            }

            if (_boundaryConstraintManager != null)
            {
                _boundaryConstraintManager.SetGraphLoader(_graphLoader);
                _boundaryConstraintManager.SetLocationManager(_locationManager);
            }

            if (_routeCalculator != null)
            {
                _routeCalculator.SetBoundaryConstraintManager(_boundaryConstraintManager);
            }

            if (autoDriveCubeFromSimulation)
            {
                SetupSimulationTargetDriver();
            }

            if (autoCreateGraphPreview)
            {
                SetupGraphPreview();
            }

            if (autoCreateMapReference)
            {
                SetupMapReferencePreview();
            }

            if (autoCreateDestinationMarkers)
            {
                SetupDestinationMarkers();
            }

            if (autoCreateRoutePathPreview)
            {
                SetupRoutePathPreview();
            }

            if (autoCreateMiniMap)
            {
                SetupMiniMap();
            }

            if (autoCreateVideoCsvOverlay)
            {
                SetupVideoCsvOverlay();
            }

            if (autoCreateVideoFrameMap)
            {
                SetupVideoFrameMap();
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

        private void SetupMapReferencePreview()
        {
            _mapReferenceTileVisualizer = FindObjectOfType<MapReferenceTileVisualizer>();
            if (_mapReferenceTileVisualizer == null)
            {
                _mapReferenceTileVisualizer = gameObject.AddComponent<MapReferenceTileVisualizer>();
            }

            _mapReferenceTileVisualizer.SetGraphLoader(_graphLoader);
            _mapReferenceTileVisualizer.SetStartReferenceTransform(GetStartReferenceTransform());
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

        private void SetupMiniMap()
        {
            _miniMapOverlay = FindObjectOfType<MiniMapOverlay>();
            if (_miniMapOverlay == null)
            {
                _miniMapOverlay = gameObject.AddComponent<MiniMapOverlay>();
            }

            _miniMapOverlay.SetGraphLoader(_graphLoader);
            _miniMapOverlay.SetRouteCalculator(_routeCalculator);
            _miniMapOverlay.SetLocationManager(_locationManager);
            _miniMapOverlay.SetBoundaryConstraintManager(_boundaryConstraintManager);
            _miniMapOverlay.SetStartReferenceTransform(GetStartReferenceTransform());
        }

        private void SetupVideoCsvOverlay()
        {
            _videoMappingCsvOverlay = FindObjectOfType<VideoMappingCsvOverlay>();
            if (_videoMappingCsvOverlay == null)
            {
                _videoMappingCsvOverlay = gameObject.AddComponent<VideoMappingCsvOverlay>();
            }
        }

        private void SetupVideoFrameMap()
        {
            _videoFrameMapVisualizer = FindObjectOfType<VideoFrameMapVisualizer>();
            if (_videoFrameMapVisualizer == null)
            {
                _videoFrameMapVisualizer = gameObject.AddComponent<VideoFrameMapVisualizer>();
            }

            _videoFrameMapVisualizer.SetGraphLoader(_graphLoader);
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

            if (_miniMapOverlay != null)
            {
                _miniMapOverlay.OnDestinationNodeClicked += HandleMiniMapDestinationClicked;
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

            if (_miniMapOverlay != null)
            {
                _miniMapOverlay.OnDestinationNodeClicked -= HandleMiniMapDestinationClicked;
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
                    RecalculateCurrentRoute("Initial Auto Route");
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

            _uiDestinationsDirty = true;
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

                RefreshUIDestinations();
                destinationIndex = Mathf.Clamp(destinationIndex, 0, Mathf.Max(0, _uiDestinations.Count - 1));
                _destinationDropdownExpanded = _destinationDropdownExpanded && _uiDestinations.Count > 0;
                UpdateSelectedDestinationMarker();
                _status = "Fallback cube locations seeded.";
                return;
            }

            if (!HasAnyRouteableDestination())
            {
                SeedFallbackCubeLocations();
                _locationsLoaded = _locationManager != null && _locationManager.Locations.Count > 0;
                RefreshUIDestinations();
                destinationIndex = Mathf.Clamp(destinationIndex, 0, Mathf.Max(0, _uiDestinations.Count - 1));
                _destinationDropdownExpanded = _destinationDropdownExpanded && _uiDestinations.Count > 0;
                UpdateSelectedDestinationMarker();
                _status = "No routeable destinations found in data. Fallback cube locations seeded.";
                return;
            }

            RefreshUIDestinations();
            destinationIndex = Mathf.Clamp(destinationIndex, 0, Mathf.Max(0, _uiDestinations.Count - 1));
            _destinationDropdownExpanded = _destinationDropdownExpanded && _uiDestinations.Count > 0;
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
            if (result != null && !string.IsNullOrWhiteSpace(result.recalcReason))
            {
                _lastRecalcReason = result.recalcReason;
            }
        }

        private void HandleMiniMapDestinationClicked(string nodeId, string locationName)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                return;
            }

            RefreshUIDestinations();
            if (_uiDestinations.Count == 0)
            {
                return;
            }

            int matchedIndex = _uiDestinations.FindIndex(location =>
                location != null &&
                !string.IsNullOrWhiteSpace(location.indoor_node_id) &&
                string.Equals(location.indoor_node_id.Trim(), nodeId.Trim(), System.StringComparison.OrdinalIgnoreCase));

            if (matchedIndex < 0 && !string.IsNullOrWhiteSpace(locationName))
            {
                matchedIndex = _uiDestinations.FindIndex(location =>
                    location != null &&
                    !string.IsNullOrWhiteSpace(location.name) &&
                    string.Equals(location.name.Trim(), locationName.Trim(), System.StringComparison.OrdinalIgnoreCase));
            }

            if (matchedIndex < 0)
            {
                _status = $"Mini map click selected '{nodeId}', but no matching destination was found.";
                return;
            }

            SetDestinationIndex(matchedIndex, recalculate: true);
            _status = $"Mini map destination selected: {_uiDestinations[matchedIndex].name}";
        }

        private void RefreshUIDestinations()
        {
            if (!_uiDestinationsDirty && _uiDestinations.Count > 0)
            {
                return;
            }

            string previousNodeId = _selectedDestinationNodeId;
            string previousName = _selectedDestinationName;
            int previousIndex = destinationIndex;
            if (_uiDestinations.Count > 0 && destinationIndex >= 0 && destinationIndex < _uiDestinations.Count)
            {
                LocationPoint previous = _uiDestinations[destinationIndex];
                if (previous != null)
                {
                    if (string.IsNullOrWhiteSpace(previousNodeId))
                    {
                        previousNodeId = previous.indoor_node_id;
                    }

                    if (string.IsNullOrWhiteSpace(previousName))
                    {
                        previousName = previous.name;
                    }
                }
            }

            _uiDestinations.Clear();
            if (_locationManager == null)
            {
                _selectedDestinationNodeId = null;
                _selectedDestinationName = null;
                _uiDestinationsDirty = false;
                return;
            }

            var seen = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < _locationManager.Locations.Count; i++)
            {
                LocationPoint location = _locationManager.Locations[i];
                if (location == null)
                {
                    continue;
                }

                string name = location.name != null ? location.name.Trim() : string.Empty;
                string nodeId = location.indoor_node_id != null ? location.indoor_node_id.Trim() : string.Empty;
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(nodeId))
                {
                    continue;
                }

                if (_graphLoader != null && _graphLoader.NodesById.Count > 0 && _graphLoader.GetNode(nodeId) == null)
                {
                    continue;
                }

                if (_boundaryConstraintManager != null && !_boundaryConstraintManager.IsNodeAllowed(nodeId))
                {
                    continue;
                }

                string dedupeKey = name + "|" + nodeId;
                if (!seen.Add(dedupeKey))
                {
                    continue;
                }

                _uiDestinations.Add(location);
            }

            _uiDestinations.Sort((a, b) => string.Compare(a.name, b.name, System.StringComparison.OrdinalIgnoreCase));

            if (_uiDestinations.Count == 0)
            {
                destinationIndex = 0;
                _selectedDestinationNodeId = null;
                _selectedDestinationName = null;
                _uiDestinationsDirty = false;
                return;
            }

            int resolvedIndex = -1;
            if (!string.IsNullOrWhiteSpace(previousNodeId))
            {
                resolvedIndex = _uiDestinations.FindIndex(l =>
                    l != null &&
                    string.Equals(l.indoor_node_id, previousNodeId, System.StringComparison.OrdinalIgnoreCase) &&
                    (string.IsNullOrWhiteSpace(previousName) ||
                     string.Equals(l.name, previousName, System.StringComparison.OrdinalIgnoreCase)));
            }

            if (resolvedIndex < 0 && !string.IsNullOrWhiteSpace(previousName))
            {
                resolvedIndex = _uiDestinations.FindIndex(l => l != null &&
                                                               string.Equals(l.name, previousName, System.StringComparison.OrdinalIgnoreCase));
            }

            if (resolvedIndex < 0)
            {
                resolvedIndex = Mathf.Clamp(previousIndex, 0, _uiDestinations.Count - 1);
            }

            destinationIndex = resolvedIndex;
            LocationPoint selected = _uiDestinations[destinationIndex];
            _selectedDestinationNodeId = selected != null ? selected.indoor_node_id : null;
            _selectedDestinationName = selected != null ? selected.name : null;
            _uiDestinationsDirty = false;
        }

        private string GetCurrentDestinationName()
        {
            if (!_locationsLoaded)
            {
                return "none";
            }

            RefreshUIDestinations();
            if (_uiDestinations.Count == 0)
            {
                return "none";
            }

            int safeIndex = Mathf.Clamp(destinationIndex, 0, _uiDestinations.Count - 1);
            string name = _uiDestinations[safeIndex].name;
            return string.IsNullOrWhiteSpace(name) ? "(Unnamed Destination)" : name;
        }

        private void SetDestinationIndex(int newIndex, bool recalculate)
        {
            RefreshUIDestinations();
            if (_uiDestinations.Count == 0)
            {
                return;
            }

            int clamped = Mathf.Clamp(newIndex, 0, _uiDestinations.Count - 1);
            if (clamped == destinationIndex && !recalculate)
            {
                return;
            }

            destinationIndex = clamped;
            LocationPoint selected = _uiDestinations[destinationIndex];
            _selectedDestinationNodeId = selected != null ? selected.indoor_node_id : null;
            _selectedDestinationName = selected != null ? selected.name : null;
            UpdateSelectedDestinationMarker();
            if (recalculate)
            {
                RecalculateCurrentRoute("Destination changed via UI");
            }
        }

        private void CycleDestination(int step)
        {
            RefreshUIDestinations();
            if (!_locationsLoaded || _uiDestinations.Count == 0)
            {
                _status = "No destinations available yet.";
                return;
            }

            int count = _uiDestinations.Count;
            destinationIndex = (destinationIndex + step) % count;
            if (destinationIndex < 0)
            {
                destinationIndex += count;
            }

            SetDestinationIndex(destinationIndex, recalculate: true);
        }

        private void RecalculateCurrentRoute(string reason = "Manual")
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

            RefreshUIDestinations();
            if (_uiDestinations.Count == 0)
            {
                _status = "No destinations loaded yet.";
                return;
            }

            destinationIndex = Mathf.Clamp(destinationIndex, 0, _uiDestinations.Count - 1);
            var destination = _uiDestinations[destinationIndex];
            if (_graphLoader != null && _graphLoader.GetNode(destination.indoor_node_id) == null)
            {
                _status = $"Destination '{destination.name}' has no matching graph node.";
                return;
            }

            ElevationLevel level = _levelManager != null ? _levelManager.CurrentLevel : ElevationLevel.Deck;
            _resolvedStartNodeId = ResolveStartNodeIdForRoute();
            _routeCalculator.CalculateIndoorRoute(_resolvedStartNodeId, destination.indoor_node_id, level, forceImmediateRouteRefresh, reason);
        }

        private void UpdateSelectedDestinationMarker()
        {
            RefreshUIDestinations();
            if (_uiDestinations.Count == 0)
            {
                return;
            }

            if (destinationIndex < 0 || destinationIndex >= _uiDestinations.Count)
            {
                return;
            }

            LocationPoint selected = _uiDestinations[destinationIndex];
            if (_destinationMarkerVisualizer != null)
            {
                _destinationMarkerVisualizer.SetSelectedDestination(selected.name);
            }

            if (_miniMapOverlay != null)
            {
                _miniMapOverlay.SetSelectedDestinationNodeId(selected.indoor_node_id);
            }

            _selectedDestinationNodeId = selected.indoor_node_id;
            _selectedDestinationName = selected.name;
        }

        private string ResolveStartNodeIdForRoute()
        {
            string resolved = ResolveStartNodeId();
            if (!string.IsNullOrWhiteSpace(resolved))
            {
                return resolved;
            }

            return startNodeId;
        }

        private string ResolveStartNodeId()
        {
            if (_graphLoader == null || _graphLoader.NodesById.Count == 0)
            {
                return startNodeId;
            }

            bool useDynamicStart = alwaysRouteFromCurrentPosition || useNearestNodeAsStart;
            if (!useDynamicStart)
            {
                return startNodeId;
            }

            Transform reference = GetStartReferenceTransform();
            if (reference == null)
            {
                return startNodeId;
            }

            bool filterToCurrentLevel = preferCurrentLevelForStartNode && _levelManager != null;
            int currentLevel = _levelManager != null ? (int)_levelManager.CurrentLevel : int.MinValue;
            if (_stableStartLevel != currentLevel)
            {
                _stableStartNodeId = null;
                _stableStartLevel = currentLevel;
            }

            string candidate;
            if (baritoneStyleStartResolution)
            {
                candidate = ResolveBaritoneStyleStartNode(reference, filterToCurrentLevel);
                if (string.IsNullOrWhiteSpace(candidate) && filterToCurrentLevel)
                {
                    candidate = ResolveBaritoneStyleStartNode(reference, onlyCurrentLevel: false);
                }
            }
            else
            {
                candidate = FindNearestGraphNodeId(reference.position, filterToCurrentLevel);
                if (string.IsNullOrWhiteSpace(candidate) && filterToCurrentLevel)
                {
                    candidate = FindNearestGraphNodeId(reference.position, onlyCurrentLevel: false);
                }
            }

            string stabilized = StabilizeStartNode(candidate, reference.position);
            return string.IsNullOrWhiteSpace(stabilized) ? startNodeId : stabilized;
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

        private string FindNearestGraphNodeId(Vector3 position, bool onlyCurrentLevel)
        {
            string bestId = null;
            float bestDist = float.PositiveInfinity;

            foreach (var kvp in _graphLoader.NodesById)
            {
                Node node = kvp.Value;
                if (!IsNodeEligibleForStart(node, onlyCurrentLevel))
                {
                    continue;
                }

                float d = HorizontalDistance(position, node.position);
                if (d < bestDist)
                {
                    bestDist = d;
                    bestId = kvp.Key;
                }
            }

            return bestId;
        }

        private string ResolveBaritoneStyleStartNode(Transform reference, bool onlyCurrentLevel)
        {
            // Mirrors Baritone's idea of "pathStart": choose a plausible nearby support point,
            // not just raw nearest distance, with mild forward/vertical bias.
            string nearestId = null;
            float nearestDistance = float.PositiveInfinity;

            foreach (var kvp in _graphLoader.NodesById)
            {
                if (!IsNodeEligibleForStart(kvp.Value, onlyCurrentLevel))
                {
                    continue;
                }

                float d = HorizontalDistance(reference.position, kvp.Value.position);
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
                if (!IsNodeEligibleForStart(node, onlyCurrentLevel))
                {
                    continue;
                }

                Vector3 delta = node.position - reference.position;
                float distXZ = HorizontalDistance(reference.position, node.position);
                if (distXZ > maxCandidateDistance)
                {
                    continue;
                }

                Vector3 deltaXZ = new Vector3(delta.x, 0f, delta.z);
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

        private string StabilizeStartNode(string candidateId, Vector3 referencePosition)
        {
            if (string.IsNullOrWhiteSpace(candidateId))
            {
                return _stableStartNodeId;
            }

            if (string.IsNullOrWhiteSpace(_stableStartNodeId))
            {
                _stableStartNodeId = candidateId;
                return _stableStartNodeId;
            }

            if (string.Equals(_stableStartNodeId, candidateId, System.StringComparison.Ordinal))
            {
                return _stableStartNodeId;
            }

            Node currentStable = _graphLoader.GetNode(_stableStartNodeId);
            Node candidate = _graphLoader.GetNode(candidateId);
            if (currentStable == null || candidate == null)
            {
                _stableStartNodeId = candidateId;
                return _stableStartNodeId;
            }

            float stableDist = HorizontalDistance(referencePosition, currentStable.position);
            float candidateDist = HorizontalDistance(referencePosition, candidate.position);
            bool stableTooFar = stableDist > startNodeMaxHoldDistanceMeters;
            bool candidateSignificantlyBetter = candidateDist + startNodeSwitchAdvantageMeters < stableDist;

            if (stableTooFar || candidateSignificantlyBetter)
            {
                _stableStartNodeId = candidateId;
            }

            return _stableStartNodeId;
        }

        private bool IsNodeEligibleForStart(Node node, bool onlyCurrentLevel)
        {
            if (node == null)
            {
                return false;
            }

            if (_boundaryConstraintManager != null && !_boundaryConstraintManager.IsNodeAllowed(node.id))
            {
                return false;
            }

            if (!onlyCurrentLevel || _levelManager == null)
            {
                return true;
            }

            return node.elevationLevel == (int)_levelManager.CurrentLevel;
        }

        private static float HorizontalDistance(Vector3 from, Vector3 to)
        {
            Vector2 a = new Vector2(from.x, from.z);
            Vector2 b = new Vector2(to.x, to.z);
            return Vector2.Distance(a, b);
        }

        private bool HasAnyRouteableDestination()
        {
            RefreshUIDestinations();
            return _uiDestinations.Count > 0;
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

            RefreshUIDestinations();
            if (_uiDestinations.Count == 0)
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
            RecalculateCurrentRoute("Start node explicitly changed");
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

            RefreshUIDestinations();
            if (_uiDestinations.Count == 0)
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

            float minMove = Mathf.Max(0.2f, continuousRefreshMinMoveMeters);
            bool movedEnough = HorizontalDistance(reference.position, _lastContinuousRefreshPosition) >= minMove;
            if (!movedEnough)
            {
                return;
            }

            _lastContinuousRefreshPosition = reference.position;
            RecalculateCurrentRoute("Continuous movement refresh");
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
            RecalculateCurrentRoute("Off-route revalidation");
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

                float d = HorizontalDistance(position, node.position);
                if (d < best)
                {
                    best = d;
                }
            }

            return best;
        }
    }
}
