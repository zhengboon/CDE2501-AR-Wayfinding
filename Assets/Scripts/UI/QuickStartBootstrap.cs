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
        [SerializeField] private bool autoCreateVideoFrameMap = false;
        [SerializeField] private bool autoCreateStreetViewExplorer = false;
        [SerializeField] private bool disableYoutubeImageSystems = true;
        [SerializeField] private bool resetPersistentDataOnStart = false;
        [SerializeField] private bool autoRecalcWhenStartNodeChanges = true;
        [SerializeField] private bool alwaysRouteFromCurrentPosition = true;
        [SerializeField] private bool forceImmediateRouteRefresh = true;
        [SerializeField] private bool snapViewToStartOnInitialRoute = true;
        [SerializeField, Min(0.2f)] private float initialViewEyeHeightMeters = 1.65f;
        [SerializeField, Min(0f)] private float initialViewBackOffsetMeters = 4.5f;
        [SerializeField, Min(0.1f)] private float startNodeCheckIntervalSeconds = 0.5f;
        [SerializeField] private KeyCode manualRecalculateKey = KeyCode.R;
        [SerializeField] private KeyCode manualRecalculateAltKey = KeyCode.F5;
        [SerializeField] private bool requireCtrlForManualRecalcWhileSimulating = true;
        [Header("Map Area Toggle")]
        [SerializeField] private bool useNusMapArea;
        [SerializeField] private bool fallbackToAnyDetectedMapFile = true;
        [SerializeField] private string queenstownGraphFile = "estate_graph.json";
        [SerializeField] private string queenstownLocationsFile = "locations.json";
        [SerializeField] private string nusGraphFile = "nus_estate_graph.json";
        [SerializeField] private string nusLocationsFile = "nus_locations.json";
        [SerializeField] private string queenstownDefaultStartNodeId = "QTMRT";
        [SerializeField] private string nusDefaultStartNodeId = "NUS_E1";
        [SerializeField] private string queenstownMiniMapImageFile = "queenstown_map_z19_x413314-413324_y260255-260265.png";
        [SerializeField] private string queenstownReferenceMapImageFile = "queenstown_map_z18_x206656-206662_y130127-130133.png";
        [SerializeField] private string nusMiniMapImageFile = "nus_map_z19_x413268-413276_y260247-260255.png";
        [SerializeField] private string nusReferenceMapImageFile = "nus_map_z18_x206633-206638_y130123-130128.png";
        [Header("Area Anchors")]
        [SerializeField] private bool teleportToAreaAnchorOnMapSwitch = true;
        [SerializeField] private bool forceSimulationModeOnAreaTeleport = true;
        [SerializeField] private bool forceSimulationModeOnAreaTeleportInEditorOnly = true;
        [SerializeField] private GeoPoint queenstownAnchorCoordinates = new GeoPoint(1.294550851849307, 103.8060771559821);
        [SerializeField] private float queenstownAnchorHeading = 0f;
        [SerializeField] private GeoPoint nusAnchorCoordinates = new GeoPoint(1.300429766736533, 103.7713240720471);
        [SerializeField] private float nusAnchorHeading = 0f;
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
        [SerializeField, Min(0f)] private float deviceSensorContinuousRefreshMinMoveMeters = 2.0f;
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
        private VideoFrameMapVisualizer _videoFrameMapVisualizer;
        private StreetViewExplorer _streetViewExplorer;
        private BoundaryConstraintManager _boundaryConstraintManager;
        private TelemetryRecorder _telemetryRecorder;
        private PathScreenshotRecorder _pathScreenshotRecorder;
        private UserPathRecorder _userPathRecorder;
        private CDE2501.Wayfinding.AR.FlightTrackerARView _flightTrackerARView;
        private DataSyncManager _dataSyncManager;

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
        private string _activeMiniMapImageFile;
        private string _activeReferenceMapImageFile;
        private string _activeGraphFile;
        private string _activeLocationsFile;
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
        private Coroutine _autoRouteRoutine;
        private readonly List<string> _recentPlaySessionRecordings = new List<string>(5);
        private float _nextPlaySessionRefreshTime;
        private bool _pendingForceSnapAfterAreaSwitch;
        private bool _showDebugInfo;

        private const string AutoSessionRecordingsFolderRelative = "Recordings/AutoSessions";
        private const int MaxPlaySessionPreviewCount = 5;
        private const float PlaySessionRefreshIntervalSeconds = 1f;

        private void Awake()
        {
            if (continuousRefreshMinMoveMeters < 0.05f)
            {
                continuousRefreshMinMoveMeters = 0.05f;
            }

            if (continuousRefreshIntervalSeconds < 0.1f)
            {
                continuousRefreshIntervalSeconds = 0.1f;
            }

            if (routeRevalidationIntervalSeconds < 0.1f)
            {
                routeRevalidationIntervalSeconds = 0.1f;
            }

            if (startNodeSwitchAdvantageMeters < 0f)
            {
                startNodeSwitchAdvantageMeters = 0f;
            }

            if (startNodeMaxHoldDistanceMeters < 0.5f)
            {
                startNodeMaxHoldDistanceMeters = 0.5f;
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

            // Wait for data sync before loading
            _dataSyncManager = FindObjectOfType<DataSyncManager>();
            if (_dataSyncManager == null)
            {
                _dataSyncManager = gameObject.AddComponent<DataSyncManager>();
            }

            if (_dataSyncManager.SyncComplete)
            {
                StartAfterSync();
            }
            else
            {
                _status = "Downloading data files...";
                _dataSyncManager.OnSyncComplete += StartAfterSync;
                _dataSyncManager.OnSyncFailed += HandleSyncFailed;
            }
        }

        private void StartAfterSync()
        {
            if (_dataSyncManager != null)
            {
                _dataSyncManager.OnSyncComplete -= StartAfterSync;
                _dataSyncManager.OnSyncFailed -= HandleSyncFailed;
            }

            _status = "Data ready. Loading...";
            ApplyMapAreaSelection();
            Subscribe();
            _locationManager.LoadLocations();
            StartAutoRouteWait();
        }

        private void HandleSyncFailed(string error)
        {
            _status = $"Data sync failed: {error}. Tap Retry in the download panel.";
        }

        private void OnDestroy()
        {
            StopAutoRouteWait();
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

            string manualRecalcReason = ConsumeManualRecalculateReason();
            if (!string.IsNullOrEmpty(manualRecalcReason))
            {
                RecalculateCurrentRoute(manualRecalcReason);
            }

            if (_routeCalculator != null && Input.GetKeyDown(KeyCode.Alpha1))
            {
                _routeCalculator.CurrentMode = RoutingMode.NormalElderly;
                RecalculateCurrentRoute("Mode changed to Elderly");
            }

            if (_routeCalculator != null && Input.GetKeyDown(KeyCode.Alpha2))
            {
                _routeCalculator.CurrentMode = RoutingMode.Wheelchair;
                RecalculateCurrentRoute("Mode changed to Wheelchair");
            }

            if (_routeCalculator != null && Input.GetKeyDown(KeyCode.T))
            {
                _routeCalculator.RainMode = !_routeCalculator.RainMode;
                RecalculateCurrentRoute("Rain mode toggled");
            }

            if (_miniMapOverlay != null)
            {
                _miniMapOverlay.SetStartReferenceTransform(GetStartReferenceTransform());
            }

            if (_streetViewExplorer != null)
            {
                _streetViewExplorer.SetStartReferenceTransform(GetStartReferenceTransform());
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

            // --- Essential controls (always visible) ---
            float essentialX = 12f;

            if (_routeCalculator != null)
            {
                bool wheelchair = _routeCalculator.CurrentMode == RoutingMode.Wheelchair;
                bool newWheelchair = GUI.Toggle(new Rect(essentialX, 34f, 180f, 24f), wheelchair, $"Wheelchair: {(wheelchair ? "True" : "False")}");
                if (newWheelchair != wheelchair)
                {
                    _routeCalculator.CurrentMode = newWheelchair ? RoutingMode.Wheelchair : RoutingMode.NormalElderly;
                    shouldRecalculate = true;
                    pendingRecalcReason = "Mobility mode UI toggle";
                }
                essentialX += 186f;
            }

            if (_telemetryRecorder != null)
            {
                bool isRecording = _telemetryRecorder.IsRecording;
                bool newRecording = GUI.Toggle(new Rect(essentialX, 34f, 80f, 24f), isRecording, isRecording ? "Rec: ON" : "Rec: OFF");
                if (newRecording != isRecording)
                {
                    if (newRecording) _telemetryRecorder.StartRecording();
                    else _telemetryRecorder.StopRecording();
                }
                essentialX += 86f;
            }

            if (_pathScreenshotRecorder != null && _telemetryRecorder != null && _telemetryRecorder.IsRecording)
            {
                if (GUI.Button(new Rect(essentialX, 34f, 50f, 24f), "Snap"))
                {
                    _pathScreenshotRecorder.CaptureManual();
                }
                essentialX += 56f;
            }

            if (_flightTrackerARView != null)
            {
                bool arActive = _flightTrackerARView.IsActive;
                if (GUI.Button(new Rect(essentialX, 34f, 62f, 24f), arActive ? "AR: ON" : "AR: OFF"))
                {
                    _flightTrackerARView.Toggle();
                    if (_flightTrackerARView.IsActive)
                    {
                        string destName = GetCurrentDestinationName();
                        _flightTrackerARView.SetSelectedDestination(destName);
                    }
                }
                essentialX += 68f;
            }

            if (_dataSyncManager != null)
            {
                if (GUI.Button(new Rect(essentialX, 34f, 56f, 24f), "Share"))
                {
                    _dataSyncManager.ShareTelemetryData();
                }
                essentialX += 62f;
            }

            bool newDebug = GUI.Toggle(new Rect(essentialX, 34f, 80f, 24f), _showDebugInfo, "Debug");
            if (newDebug != _showDebugInfo)
            {
                _showDebugInfo = newDebug;
                if (_graphRuntimeVisualizer != null) _graphRuntimeVisualizer.enabled = _showDebugInfo;
                if (_destinationMarkerVisualizer != null) _destinationMarkerVisualizer.enabled = _showDebugInfo;
            }

            // --- Debug controls (hidden unless Debug is on) ---
            if (_showDebugInfo)
            {
                float debugX = essentialX + 86f;

                if (_routeCalculator != null)
                {
                    bool rain = _routeCalculator.RainMode;
                    bool newRain = GUI.Toggle(new Rect(debugX, 34f, 140f, 24f), rain, $"Rain: {(rain ? "True" : "False")}");
                    if (newRain != rain)
                    {
                        _routeCalculator.RainMode = newRain;
                        shouldRecalculate = true;
                        pendingRecalcReason = "Rain mode UI toggle";
                    }
                    debugX += 146f;
                }

                if (_simulationProvider != null)
                {
                    bool sim = _simulationProvider.ForceSimulationMode;
                    bool newSim = GUI.Toggle(new Rect(debugX, 34f, 150f, 24f), sim, $"Sim Mode: {(sim ? "True" : "False")}");
                    if (newSim != sim)
                    {
                        _simulationProvider.ForceSimulationMode = newSim;
                    }
                    debugX += 156f;
                }

                bool newNearestStart = GUI.Toggle(new Rect(debugX, 34f, 140f, 24f), useNearestNodeAsStart, $"Nearest Start: {(useNearestNodeAsStart ? "True" : "False")}");
                if (newNearestStart != useNearestNodeAsStart)
                {
                    useNearestNodeAsStart = newNearestStart;
                    shouldRecalculate = true;
                    pendingRecalcReason = "Nearest Start UI toggle";
                }
            }

            if (!disableYoutubeImageSystems && _showDebugInfo && _streetViewExplorer != null)
            {
                bool streetViewActive = _streetViewExplorer.IsStreetViewActive;
                bool newStreetViewActive = GUI.Toggle(new Rect(742f, 34f, 188f, 24f), streetViewActive, $"StreetView: {(streetViewActive ? "True" : "False")}");
                if (newStreetViewActive != streetViewActive)
                {
                    _streetViewExplorer.SetStreetViewActive(newStreetViewActive);
                }
            }

            _resolvedStartNodeId = ResolveStartNodeId();
            RefreshUIDestinations();
            string destinationName = GetCurrentDestinationName();

            float destinationRowY = 64f;
            string mapAreaLabel = useNusMapArea ? "NUS Engineering" : "Queenstown";
            if (GUI.Button(new Rect(668f, destinationRowY, 262f, 24f), $"Map Area: {mapAreaLabel}"))
            {
                ToggleMapAreaSelection();
            }

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

            // --- Path recording UI (always visible) ---
            float pathRowY = destinationRowY + 32f + dropdownHeight + 2f;
            float pathRecordHeight = 0f;
            if (_userPathRecorder != null)
            {
                if (_userPathRecorder.IsRecordingPath)
                {
                    string pathLabel = $"Recording: {_userPathRecorder.CurrentFromLabel} -> {_userPathRecorder.CurrentToLabel} ({_userPathRecorder.CurrentPointCount} pts)";
                    GUI.Label(new Rect(12f, pathRowY, _panelRect.width - 120f, 24f), pathLabel, _bodyStyle);
                    if (GUI.Button(new Rect(_panelRect.width - 108f, pathRowY, 96f, 24f), "Stop Path"))
                    {
                        _userPathRecorder.StopPathRecording();
                    }
                    pathRecordHeight = 28f;
                }
                else
                {
                    GUI.Label(new Rect(12f, pathRowY, 100f, 24f), "Record path:", _bodyStyle);
                    string fromLabel = _resolvedStartNodeId ?? "here";
                    if (GUI.Button(new Rect(116f, pathRowY, 260f, 24f), $"{fromLabel} -> {destinationName}"))
                    {
                        _userPathRecorder.StartPathRecording(fromLabel, destinationName);
                    }

                    int savedPaths = _userPathRecorder.PathIndex != null ? _userPathRecorder.PathIndex.paths.Count : 0;
                    GUI.Label(new Rect(386f, pathRowY, 200f, 24f), $"Saved paths: {savedPaths}", _bodyStyle);
                    pathRecordHeight = 28f;
                }
            }

            float sessionPreviewHeight = 0f;
            if (_showDebugInfo && Application.isEditor)
            {
                RefreshPlaySessionRecordingsIfNeeded();

                float sessionRowY = pathRowY + pathRecordHeight + 2f;
                GUI.Label(new Rect(12f, sessionRowY, 190f, 24f), $"Sessions ({_recentPlaySessionRecordings.Count}/{MaxPlaySessionPreviewCount}):", _bodyStyle);

                float refreshButtonX = Mathf.Max(12f, _panelRect.width - 178f);
                if (GUI.Button(new Rect(refreshButtonX, sessionRowY, 80f, 24f), "Refresh"))
                {
                    RefreshPlaySessionRecordingsIfNeeded(forceRefresh: true);
                }

                if (GUI.Button(new Rect(refreshButtonX + 86f, sessionRowY, 80f, 24f), "Folder"))
                {
                    OpenPlaySessionFolder();
                }

                float buttonsStartX = 204f;
                float buttonWidth = 74f;
                float buttonGap = 6f;
                float availableWidth = Mathf.Max(0f, _panelRect.width - buttonsStartX - 12f);
                int buttonsPerRow = Mathf.Max(1, Mathf.FloorToInt((availableWidth + buttonGap) / (buttonWidth + buttonGap)));
                int sessionRows = Mathf.Max(1, Mathf.CeilToInt(_recentPlaySessionRecordings.Count / (float)buttonsPerRow));

                for (int i = 0; i < _recentPlaySessionRecordings.Count; i++)
                {
                    int row = i / buttonsPerRow;
                    int col = i % buttonsPerRow;
                    float buttonX = buttonsStartX + (col * (buttonWidth + buttonGap));
                    float buttonY = sessionRowY + (row * 26f);

                    if (GUI.Button(new Rect(buttonX, buttonY, buttonWidth, 24f), $"Open {i + 1}"))
                    {
                        OpenPlaySessionRecording(_recentPlaySessionRecordings[i]);
                    }
                }

                float latestRowY = sessionRowY + Mathf.Max(24f, sessionRows * 26f);
                string latestSessionText = _recentPlaySessionRecordings.Count > 0
                    ? $"Latest: {Path.GetFileName(_recentPlaySessionRecordings[0])}"
                    : "Latest: none";
                GUI.Label(new Rect(12f, latestRowY, _panelRect.width - 24f, 24f), latestSessionText, _bodyStyle);
                sessionPreviewHeight = (latestRowY - sessionRowY) + 24f;
            }

            if (shouldRecalculate)
            {
                RecalculateCurrentRoute(pendingRecalcReason ?? "UI State Changed");
            }

            string routeMessage = _lastRouteResult == null
                ? "No route yet."
                : (_lastRouteResult.success
                    ? $"Route: {_lastRouteResult.totalDistance:0.0} m, ~{_lastRouteResult.estimatedWalkTimeSeconds / 60f:0.1} min ({_lastRouteResult.nodePath.Count} nodes)"
                    : $"Route failed: {_lastRouteResult.message}");

            string text;
            if (_showDebugInfo)
            {
                string mediaSystemsText = disableYoutubeImageSystems
                    ? "Video frame map: disabled\nStreet view explorer: disabled\n"
                    : $"Video frame map: {(_videoFrameMapVisualizer != null)} (loaded: {(_videoFrameMapVisualizer != null && _videoFrameMapVisualizer.ManifestReady)}, markers: {(_videoFrameMapVisualizer != null ? _videoFrameMapVisualizer.MarkerCount : 0)})\n" +
                      $"Street view explorer: {(_streetViewExplorer != null)} (loaded: {(_streetViewExplorer != null && _streetViewExplorer.ManifestReady)}, nodes: {(_streetViewExplorer != null ? _streetViewExplorer.NodeCount : 0)}, google: {(_streetViewExplorer != null ? _streetViewExplorer.GoogleNodeCount : 0)}, fallback: {(_streetViewExplorer != null ? _streetViewExplorer.YoutubeFallbackNodeCount : 0)}, active: {(_streetViewExplorer != null && _streetViewExplorer.IsStreetViewActive)})\n";

                string recalcKeyHint = BuildManualRecalcKeyHint();
                string keyHelpText = disableYoutubeImageSystems
                    ? $"Keys: N/P next/prev destination, {recalcKeyHint} recalc, 1 Elderly, 2 Wheelchair, T rain toggle. Move: WASD. Yaw: Q/E or Left/Right arrows. Pitch: R/F or Up/Down arrows. MiniMap: wheel zoom, left-drag pan (click selects destination only), right-drag move window, F follow."
                    : $"Keys: N/P next/prev destination, {recalcKeyHint} recalc, 1 Elderly, 2 Wheelchair, T rain toggle, Y street view. StreetView: non-click movement mode with auto-follow nearest panorama while moving, [ ] node, , . heading, H nearest node. Move: WASD. Yaw: Q/E or Left/Right arrows. Pitch: R/F or Up/Down arrows. MiniMap: wheel zoom, left-drag pan (click selects destination only), right-drag move window, F follow.";

                text =
                    $"{_status}\n" +
                    $"Start node: ME -> {_resolvedStartNodeId} | Destination: {destinationName}\n" +
                    $"Recalc Reason: {_lastRecalcReason}\n" +
                    $"Mode: {(_routeCalculator != null ? _routeCalculator.CurrentMode.ToString() : "missing")} | Rain: {(_routeCalculator != null && _routeCalculator.RainMode)}\n" +
                    $"Baritone-style start: {baritoneStyleStartResolution} | Revalidation: {routeRevalidationEnabled}\n" +
                    $"Route engine ready: {(_routeCalculator != null && _routeCalculator.IsInitialized)}\n" +
                    $"GPS ready: {(_gpsManager != null && _gpsManager.IsReady)} (sim: {(_gpsManager != null && _gpsManager.IsUsingSimulation)}) | {(_gpsManager != null ? _gpsManager.StatusMessage : "missing")}\n" +
                    $"Compass ready: {(_compassManager != null && _compassManager.IsReady)} (sim: {(_compassManager != null && _compassManager.IsUsingSimulation)}) | {(_compassManager != null ? _compassManager.StatusMessage : "missing")}\n" +
                    $"Locations loaded: {_locationsLoaded} (raw: {(_locationManager != null ? _locationManager.Locations.Count : 0)}, usable: {_uiDestinations.Count})\n" +
                    $"Boundary active: {(_boundaryConstraintManager != null && _boundaryConstraintManager.HasBoundary)} (rev: {(_boundaryConstraintManager != null ? _boundaryConstraintManager.BoundaryRevision : 0)})\n" +
                    $"Graph preview: {(_graphRuntimeVisualizer != null)}\n" +
                    $"Map reference: {(_mapReferenceTileVisualizer != null)}\n" +
                    $"Destination markers: {(_destinationMarkerVisualizer != null)}\n" +
                    $"Route line preview: {(_routePathVisualizer != null)}\n" +
                    $"Mini map: {(_miniMapOverlay != null)}\n" +
                    $"Telemetry: {(_telemetryRecorder != null ? (_telemetryRecorder.IsRecording ? $"Rec -> {_telemetryRecorder.CurrentSessionFile}" : "Ready") : "missing")}\n" +
                    $"Play sessions retained: {(_recentPlaySessionRecordings.Count > 0 ? $"{_recentPlaySessionRecordings.Count}/{MaxPlaySessionPreviewCount} (latest: {Path.GetFileName(_recentPlaySessionRecordings[0])})" : $"0/{MaxPlaySessionPreviewCount}")}\n" +
                    $"Map area: {(useNusMapArea ? "NUS Engineering" : "Queenstown")}\n" +
                    $"Map files: mini={(_activeMiniMapImageFile ?? "n/a")}, ref={(_activeReferenceMapImageFile ?? "n/a")}\n" +
                    $"Data files: graph={(_activeGraphFile ?? "n/a")}, locations={(_activeLocationsFile ?? "n/a")}\n" +
                    mediaSystemsText +
                    $"{routeMessage}\n" +
                    keyHelpText;
            }
            else
            {
                // Compact alpha tester view
                bool gpsLost = _telemetryRecorder != null && _telemetryRecorder.IsGpsLost;
                string gpsStatus = gpsLost
                    ? "GPS LOST"
                    : (_gpsManager != null && _gpsManager.IsReady) ? "Ready" : "Waiting...";
                string compassStatus = (_compassManager != null && _compassManager.IsReady) ? "Ready" : "Waiting...";
                int floor = _telemetryRecorder != null ? _telemetryRecorder.EstimatedFloor : 0;
                string floorText = floor == 0 ? "Ground" : $"Floor {floor}";
                string recStatus = _telemetryRecorder != null && _telemetryRecorder.IsRecording ? "Recording" : "Idle";
                int screenshots = _pathScreenshotRecorder != null ? _pathScreenshotRecorder.ScreenshotCount : 0;
                text =
                    $"{routeMessage}\n" +
                    $"GPS: {gpsStatus} | Compass: {compassStatus} | {floorText}\n" +
                    $"Telemetry: {recStatus} | Screenshots: {screenshots}\n" +
                    $"Destination: {destinationName}";
            }

            float infoTop = pathRowY + pathRecordHeight + sessionPreviewHeight + 4f;
            Rect infoViewport = new Rect(12f, infoTop, _panelRect.width - 24f, Mathf.Max(24f, _panelRect.height - infoTop - 8f));
            float contentHeight = Mathf.Max(infoViewport.height, _bodyStyle.CalcHeight(new GUIContent(text), infoViewport.width - 24f) + 12f);
            Rect contentRect = new Rect(0f, 0f, infoViewport.width - 20f, contentHeight);
            _panelScroll = GUI.BeginScrollView(infoViewport, _panelScroll, contentRect);
            GUI.Label(new Rect(0f, 0f, contentRect.width, contentRect.height), text, _bodyStyle);
            GUI.EndScrollView();
            GUI.DragWindow(new Rect(0f, 0f, _panelRect.width, 30f));
        }

        private string ConsumeManualRecalculateReason()
        {
            bool primaryPressed = Input.GetKeyDown(manualRecalculateKey);
            bool altPressed = manualRecalculateAltKey != KeyCode.None && Input.GetKeyDown(manualRecalculateAltKey);
            if (!primaryPressed && !altPressed)
            {
                return null;
            }

            if (primaryPressed &&
                requireCtrlForManualRecalcWhileSimulating &&
                _simulationProvider != null &&
                _simulationProvider.ForceSimulationMode &&
                !IsControlModifierPressed())
            {
                _status = $"Manual recalc blocked. Use Ctrl+{manualRecalculateKey} while simulation movement controls are active.";
                return null;
            }

            if (altPressed)
            {
                return $"Manual ({manualRecalculateAltKey} key)";
            }

            if (primaryPressed &&
                requireCtrlForManualRecalcWhileSimulating &&
                _simulationProvider != null &&
                _simulationProvider.ForceSimulationMode &&
                IsControlModifierPressed())
            {
                return $"Manual (Ctrl+{manualRecalculateKey} key)";
            }

            return $"Manual ({manualRecalculateKey} key)";
        }

        private string BuildManualRecalcKeyHint()
        {
            string primary = manualRecalculateKey.ToString();
            bool hasAlt = manualRecalculateAltKey != KeyCode.None;
            string alt = hasAlt ? manualRecalculateAltKey.ToString() : string.Empty;

            if (requireCtrlForManualRecalcWhileSimulating)
            {
                return hasAlt ? $"Ctrl+{primary}/{alt}" : $"Ctrl+{primary}";
            }

            return hasAlt ? $"{primary}/{alt}" : primary;
        }

        private static bool IsControlModifierPressed()
        {
            return Input.GetKey(KeyCode.LeftControl) ||
                   Input.GetKey(KeyCode.RightControl) ||
                   Input.GetKey(KeyCode.LeftCommand) ||
                   Input.GetKey(KeyCode.RightCommand);
        }

        private void ToggleMapAreaSelection()
        {
            useNusMapArea = !useNusMapArea;
            ApplyMapAreaSelection();
        }

        private void ApplyMapAreaSelection()
        {
            // Force fresh readiness gating so auto-route waits for the newly selected
            // locations file to finish loading instead of reusing stale readiness state.
            _locationsLoaded = false;
            _uiDestinationsDirty = true;
            _pendingForceSnapAfterAreaSwitch = true;

            string selectedMiniMapFile = useNusMapArea ? nusMiniMapImageFile : queenstownMiniMapImageFile;
            string selectedReferenceMapFile = useNusMapArea ? nusReferenceMapImageFile : queenstownReferenceMapImageFile;
            string fallbackMiniMapFile = useNusMapArea ? queenstownMiniMapImageFile : nusMiniMapImageFile;
            string fallbackReferenceMapFile = useNusMapArea ? queenstownReferenceMapImageFile : nusReferenceMapImageFile;
            string selectedGraphFile = useNusMapArea ? nusGraphFile : queenstownGraphFile;
            string selectedLocationsFile = useNusMapArea ? nusLocationsFile : queenstownLocationsFile;
            string fallbackGraphFile = useNusMapArea ? queenstownGraphFile : nusGraphFile;
            string fallbackLocationsFile = useNusMapArea ? queenstownLocationsFile : nusLocationsFile;

            string miniMapPrefix = useNusMapArea ? "nus_map_z" : "queenstown_map_z";
            string referenceMapPrefix = useNusMapArea ? "nus_map_z" : "queenstown_map_z";
            string graphPattern = useNusMapArea ? "nus*_graph*.json" : "*estate_graph*.json";
            string locationsPattern = useNusMapArea ? "nus*locations*.json" : "locations.json";

            string resolvedMiniMapFile = ResolveAvailableMapFileName(selectedMiniMapFile, fallbackMiniMapFile, miniMapPrefix);
            string resolvedReferenceMapFile = ResolveAvailableMapFileName(selectedReferenceMapFile, fallbackReferenceMapFile, referenceMapPrefix);
            string resolvedGraphFile = ResolveAvailableDataFileName(selectedGraphFile, fallbackGraphFile, graphPattern);
            string resolvedLocationsFile = ResolveAvailableDataFileName(selectedLocationsFile, fallbackLocationsFile, locationsPattern);

            if (_miniMapOverlay != null && !string.IsNullOrWhiteSpace(resolvedMiniMapFile))
            {
                _miniMapOverlay.SetMapImageFileName(resolvedMiniMapFile, resetView: true);
            }

            if (_mapReferenceTileVisualizer != null && !string.IsNullOrWhiteSpace(resolvedReferenceMapFile))
            {
                _mapReferenceTileVisualizer.SetTileFileName(resolvedReferenceMapFile);
            }

            if (_graphLoader != null && !string.IsNullOrWhiteSpace(resolvedGraphFile))
            {
                _graphLoader.SetGraphFileName(resolvedGraphFile, reload: true);
            }

            if (_locationManager != null && !string.IsNullOrWhiteSpace(resolvedLocationsFile))
            {
                _locationManager.SetLocationsFileName(resolvedLocationsFile, reload: true);
            }

            _activeMiniMapImageFile = resolvedMiniMapFile;
            _activeReferenceMapImageFile = resolvedReferenceMapFile;
            _activeGraphFile = resolvedGraphFile;
            _activeLocationsFile = resolvedLocationsFile;

            ResetStartNodeContextForAreaSwitch();
            TeleportToAreaAnchor();
            StartAutoRouteWait();

            string selectedAreaName = useNusMapArea ? "NUS Engineering" : "Queenstown";
            GeoPoint anchor = GetSelectedAreaAnchor();
            string sensorModeNote = ShouldForceSimulationModeOnAreaTeleport()
                ? "Simulation mode forced for area teleport."
                : "Sensor mode preserved for area teleport.";
            bool selectedMapMissing = !string.IsNullOrWhiteSpace(selectedMiniMapFile) && !DataFileExists(selectedMiniMapFile);
            string fallbackNote = selectedMapMissing && !string.Equals(selectedMiniMapFile, resolvedMiniMapFile, System.StringComparison.OrdinalIgnoreCase)
                ? $" (fallback map: {resolvedMiniMapFile})"
                : string.Empty;
            _status = $"Map area switched to {selectedAreaName}{fallbackNote}. Data: graph={resolvedGraphFile}, locations={resolvedLocationsFile}. Teleport anchor: {anchor.latitude:F6}, {anchor.longitude:F6}. {sensorModeNote}";
        }

        private void TeleportToAreaAnchor()
        {
            if (!teleportToAreaAnchorOnMapSwitch || _simulationProvider == null)
            {
                return;
            }

            if (ShouldForceSimulationModeOnAreaTeleport())
            {
                _simulationProvider.ForceSimulationMode = true;
            }

            GeoPoint anchor = GetSelectedAreaAnchor();
            float heading = useNusMapArea ? nusAnchorHeading : queenstownAnchorHeading;
            _simulationProvider.TeleportTo(anchor, heading, 0f, 0f);

            // Re-anchor simulation world mapping immediately so area teleports do not
            // leave the camera offset by the large geo delta between map regions.
            if (_simulatedObjectDriver != null)
            {
                Transform reference = GetStartReferenceTransform();
                if (reference != null)
                {
                    _simulatedObjectDriver.SetTarget(reference);
                }

                _simulatedObjectDriver.SetSimulationProvider(_simulationProvider);
                _simulatedObjectDriver.ReanchorToCurrentPose();
            }
        }

        private GeoPoint GetSelectedAreaAnchor()
        {
            return useNusMapArea ? nusAnchorCoordinates : queenstownAnchorCoordinates;
        }

        private bool ShouldForceSimulationModeOnAreaTeleport()
        {
            if (!forceSimulationModeOnAreaTeleport)
            {
                return false;
            }

            if (!forceSimulationModeOnAreaTeleportInEditorOnly)
            {
                return true;
            }

#if UNITY_EDITOR || UNITY_STANDALONE
            return true;
#else
            return false;
#endif
        }

        private void ResetStartNodeContextForAreaSwitch()
        {
            _lastAutoStartNodeId = null;
            _stableStartNodeId = null;
            _stableStartLevel = int.MinValue;
            _resolvedStartNodeId = null;
            _consecutiveInvalidRouteChecks = 0;
            _hasContinuousRefreshBaseline = false;

            string areaDefaultStartNodeId = useNusMapArea ? nusDefaultStartNodeId : queenstownDefaultStartNodeId;
            if (!string.IsNullOrWhiteSpace(areaDefaultStartNodeId))
            {
                startNodeId = areaDefaultStartNodeId.Trim();
            }
        }

        private string ResolveAvailableMapFileName(string preferredFileName, string fallbackFileName, string preferredPrefix)
        {
            if (DataFileExists(preferredFileName))
            {
                return preferredFileName;
            }

            if (fallbackToAnyDetectedMapFile && !string.IsNullOrWhiteSpace(preferredPrefix))
            {
                string discoveredPreferred = FindBestDataFileByPattern(preferredPrefix + "*.png");
                if (!string.IsNullOrWhiteSpace(discoveredPreferred))
                {
                    return discoveredPreferred;
                }
            }

            if (DataFileExists(fallbackFileName))
            {
                return fallbackFileName;
            }

            if (fallbackToAnyDetectedMapFile)
            {
                string discoveredAnyMap = FindBestDataFileByPattern("*map_z*.png");
                if (!string.IsNullOrWhiteSpace(discoveredAnyMap))
                {
                    return discoveredAnyMap;
                }
            }

            return preferredFileName;
        }

        private string ResolveAvailableDataFileName(string preferredFileName, string fallbackFileName, string preferredPattern)
        {
            if (DataFileExists(preferredFileName))
            {
                return preferredFileName;
            }

            if (fallbackToAnyDetectedMapFile && !string.IsNullOrWhiteSpace(preferredPattern))
            {
                string discoveredPreferred = FindBestDataFileByPattern(preferredPattern);
                if (!string.IsNullOrWhiteSpace(discoveredPreferred))
                {
                    return discoveredPreferred;
                }
            }

            if (DataFileExists(fallbackFileName))
            {
                return fallbackFileName;
            }

            return preferredFileName;
        }

        private static string FindBestDataFileByPattern(string pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern))
            {
                return null;
            }

            string bestFileName = null;
            ConsiderPatternInDirectory(Path.Combine(Application.streamingAssetsPath, "Data"), pattern, ref bestFileName);
            ConsiderPatternInDirectory(Path.Combine(Application.persistentDataPath, "Data"), pattern, ref bestFileName);
            return bestFileName;
        }

        private static void ConsiderPatternInDirectory(string directoryPath, string pattern, ref string currentBestFileName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
                {
                    return;
                }

                string[] matches = Directory.GetFiles(directoryPath, pattern);
                if (matches == null || matches.Length == 0)
                {
                    return;
                }

                for (int i = 0; i < matches.Length; i++)
                {
                    string candidateName = Path.GetFileName(matches[i]);
                    if (string.IsNullOrWhiteSpace(candidateName))
                    {
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(currentBestFileName) ||
                        string.Compare(candidateName, currentBestFileName, System.StringComparison.OrdinalIgnoreCase) > 0)
                    {
                        currentBestFileName = candidateName;
                    }
                }
            }
            catch (System.Exception)
            {
                // Keep current best file when directory probing fails.
            }
        }

        private static bool DataFileExists(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return false;
            }

            if (Path.IsPathRooted(fileName) && File.Exists(fileName))
            {
                return true;
            }

            string streamingPath = Path.Combine(Application.streamingAssetsPath, "Data", fileName);
            if (File.Exists(streamingPath))
            {
                return true;
            }

            string persistentPath = Path.Combine(Application.persistentDataPath, "Data", fileName);
            if (File.Exists(persistentPath))
            {
                return true;
            }

            return false;
        }

        private void RefreshPlaySessionRecordingsIfNeeded(bool forceRefresh = false)
        {
            if (!forceRefresh && Time.unscaledTime < _nextPlaySessionRefreshTime)
            {
                return;
            }

            _nextPlaySessionRefreshTime = Time.unscaledTime + PlaySessionRefreshIntervalSeconds;
            _recentPlaySessionRecordings.Clear();

            try
            {
                string projectRoot = Directory.GetCurrentDirectory();
                string recordingsFolder = Path.GetFullPath(Path.Combine(projectRoot, AutoSessionRecordingsFolderRelative));
                if (!Directory.Exists(recordingsFolder))
                {
                    return;
                }

                string[] recordings = Directory.GetFiles(recordingsFolder, "session_*.mp4", SearchOption.TopDirectoryOnly);
                if (recordings == null || recordings.Length == 0)
                {
                    return;
                }

                System.Array.Sort(recordings, (a, b) =>
                    File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a)));

                int keep = Mathf.Min(MaxPlaySessionPreviewCount, recordings.Length);
                for (int i = 0; i < keep; i++)
                {
                    _recentPlaySessionRecordings.Add(recordings[i]);
                }
            }
            catch (System.Exception)
            {
                _recentPlaySessionRecordings.Clear();
            }
        }

        private static void OpenPlaySessionFolder()
        {
            try
            {
                string projectRoot = Directory.GetCurrentDirectory();
                string recordingsFolder = Path.GetFullPath(Path.Combine(projectRoot, AutoSessionRecordingsFolderRelative));
                if (!Directory.Exists(recordingsFolder))
                {
                    return;
                }

                string uri = new System.Uri(recordingsFolder).AbsoluteUri;
                Application.OpenURL(uri);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"Unable to open play session folder: {ex.Message}");
            }
        }

        private static void OpenPlaySessionRecording(string absolutePath)
        {
            if (string.IsNullOrWhiteSpace(absolutePath) || !File.Exists(absolutePath))
            {
                return;
            }

            try
            {
                string uri = new System.Uri(absolutePath).AbsoluteUri;
                Application.OpenURL(uri);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"Unable to open play session recording '{absolutePath}': {ex.Message}");
            }
        }

        private void StartAutoRouteWait()
        {
            StopAutoRouteWait();
            _autoRouteRoutine = StartCoroutine(WaitAndTryAutoRoute());
        }

        private void StopAutoRouteWait()
        {
            if (_autoRouteRoutine != null)
            {
                StopCoroutine(_autoRouteRoutine);
                _autoRouteRoutine = null;
            }
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
            _telemetryRecorder = FindObjectOfType<TelemetryRecorder>();

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

            if (_telemetryRecorder == null)
            {
                _telemetryRecorder = gameObject.AddComponent<TelemetryRecorder>();
            }

            _pathScreenshotRecorder = FindObjectOfType<PathScreenshotRecorder>();
            if (_pathScreenshotRecorder == null)
            {
                _pathScreenshotRecorder = gameObject.AddComponent<PathScreenshotRecorder>();
            }

            _userPathRecorder = FindObjectOfType<UserPathRecorder>();
            if (_userPathRecorder == null)
            {
                _userPathRecorder = gameObject.AddComponent<UserPathRecorder>();
            }

            _flightTrackerARView = FindObjectOfType<CDE2501.Wayfinding.AR.FlightTrackerARView>();
            if (_flightTrackerARView == null)
            {
                _flightTrackerARView = gameObject.AddComponent<CDE2501.Wayfinding.AR.FlightTrackerARView>();
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

            if (autoCreateVideoFrameMap && !disableYoutubeImageSystems)
            {
                SetupVideoFrameMap();
            }

            if (autoCreateStreetViewExplorer && !disableYoutubeImageSystems)
            {
                SetupStreetViewExplorer();
            }

            if (disableYoutubeImageSystems)
            {
                DisableYoutubeImageSystems();
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

        private void SetupVideoFrameMap()
        {
            _videoFrameMapVisualizer = FindObjectOfType<VideoFrameMapVisualizer>();
            if (_videoFrameMapVisualizer == null)
            {
                _videoFrameMapVisualizer = gameObject.AddComponent<VideoFrameMapVisualizer>();
            }

            _videoFrameMapVisualizer.SetGraphLoader(_graphLoader);
            _videoFrameMapVisualizer.SetRouteCalculator(_routeCalculator);
        }

        private void SetupStreetViewExplorer()
        {
            _streetViewExplorer = FindObjectOfType<StreetViewExplorer>();
            if (_streetViewExplorer == null)
            {
                _streetViewExplorer = gameObject.AddComponent<StreetViewExplorer>();
            }

            _streetViewExplorer.SetGraphLoader(_graphLoader);
            _streetViewExplorer.SetStartReferenceTransform(GetStartReferenceTransform());
            _streetViewExplorer.SetRouteCalculator(_routeCalculator);
        }

        private void DisableYoutubeImageSystems()
        {
            StreetViewExplorer[] streetViewExplorers = FindObjectsOfType<StreetViewExplorer>();
            for (int i = 0; i < streetViewExplorers.Length; i++)
            {
                StreetViewExplorer explorer = streetViewExplorers[i];
                if (explorer == null)
                {
                    continue;
                }

                explorer.SetStreetViewActive(false);
                explorer.enabled = false;
            }

            VideoFrameMapVisualizer[] videoVisualizers = FindObjectsOfType<VideoFrameMapVisualizer>();
            for (int i = 0; i < videoVisualizers.Length; i++)
            {
                VideoFrameMapVisualizer visualizer = videoVisualizers[i];
                if (visualizer != null)
                {
                    visualizer.enabled = false;
                }
            }

            VideoMappingCsvOverlay[] csvOverlays = FindObjectsOfType<VideoMappingCsvOverlay>();
            for (int i = 0; i < csvOverlays.Length; i++)
            {
                VideoMappingCsvOverlay overlay = csvOverlays[i];
                if (overlay != null)
                {
                    overlay.enabled = false;
                }
            }

            _streetViewExplorer = null;
            _videoFrameMapVisualizer = null;

            if (_miniMapOverlay != null)
            {
                _miniMapOverlay.SetShowMapImage(true);
                _miniMapOverlay.SetShowVideoFrames(false);
            }
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

            if (_graphLoader != null)
            {
                _graphLoader.OnGraphLoaded += OnGraphLoaded;
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

            if (_graphLoader != null)
            {
                _graphLoader.OnGraphLoaded -= OnGraphLoaded;
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
                    SnapViewToStartNodeIfNeeded();
                    RecalculateCurrentRoute("Initial Auto Route");
                    _autoRouteRoutine = null;
                    yield break;
                }

                yield return null;
            }

            _status = $"Startup timeout. Use {BuildManualRecalcKeyHint()} to retry route calculation.";
            _autoRouteRoutine = null;
        }

        private void OnGraphLoaded(bool success, string message)
        {
            // Re-validate destinations after graph loads — they may have been
            // deferred if locations arrived before the graph was ready.
            _uiDestinationsDirty = true;
            RefreshUIDestinations();
            destinationIndex = Mathf.Clamp(destinationIndex, 0, Mathf.Max(0, _uiDestinations.Count - 1));
            UpdateSelectedDestinationMarker();

            if (success && _pendingForceSnapAfterAreaSwitch)
            {
                SnapViewToStartNodeIfNeeded(force: true);
                _pendingForceSnapAfterAreaSwitch = false;
            }
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

            // Defer routability check if graph hasn't loaded yet — the check
            // requires graph nodes to exist. Once the graph loads, the UI
            // destinations will be re-validated via the normal refresh cycle.
            bool graphReady = _graphLoader != null && _graphLoader.NodesById.Count > 0;
            if (!graphReady)
            {
                RefreshUIDestinations();
                destinationIndex = Mathf.Clamp(destinationIndex, 0, Mathf.Max(0, _uiDestinations.Count - 1));
                _destinationDropdownExpanded = _destinationDropdownExpanded && _uiDestinations.Count > 0;
                UpdateSelectedDestinationMarker();
                _status = "Locations loaded (graph pending).";
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

            if (useNusMapArea)
            {
                SeedFallbackLocationsFromCurrentGraph("NUS Fallback");
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

        private void SeedFallbackLocationsFromCurrentGraph(string prefix)
        {
            if (_locationManager == null || _graphLoader == null || _graphLoader.NodesById == null || _graphLoader.NodesById.Count == 0)
            {
                return;
            }

            _isSeedingFallbackLocations = true;
            try
            {
                var preferred = new List<string>();
                var others = new List<string>();
                foreach (var kvp in _graphLoader.NodesById)
                {
                    if (kvp.Value == null || string.IsNullOrWhiteSpace(kvp.Key))
                    {
                        continue;
                    }

                    if (kvp.Key.StartsWith("NUS_", System.StringComparison.OrdinalIgnoreCase))
                    {
                        preferred.Add(kvp.Key);
                    }
                    else
                    {
                        others.Add(kvp.Key);
                    }
                }

                preferred.Sort(System.StringComparer.OrdinalIgnoreCase);
                others.Sort(System.StringComparer.OrdinalIgnoreCase);

                int seeded = 0;
                for (int i = 0; i < preferred.Count && seeded < 6; i++)
                {
                    string nodeId = preferred[i];
                    string name = $"{prefix} {seeded + 1}";
                    if (_locationManager.GetByName(name) != null)
                    {
                        continue;
                    }

                    _locationManager.AddLocation(new LocationPoint
                    {
                        name = name,
                        type = "Campus",
                        gps_lat = 0.0,
                        gps_lon = 0.0,
                        indoor_node_id = nodeId
                    });
                    seeded++;
                }

                for (int i = 0; i < others.Count && seeded < 6; i++)
                {
                    string nodeId = others[i];
                    string name = $"{prefix} {seeded + 1}";
                    if (_locationManager.GetByName(name) != null)
                    {
                        continue;
                    }

                    _locationManager.AddLocation(new LocationPoint
                    {
                        name = name,
                        type = "Campus",
                        gps_lat = 0.0,
                        gps_lon = 0.0,
                        indoor_node_id = nodeId
                    });
                    seeded++;
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
            if (_flightTrackerARView != null && _flightTrackerARView.IsActive)
            {
                _flightTrackerARView.SetSelectedDestination(_selectedDestinationName);
            }
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
            if (_graphLoader != null && _graphLoader.GetNode(_resolvedStartNodeId) == null)
            {
                string correctedStart = ResolveFallbackStartNodeId();
                if (!string.IsNullOrWhiteSpace(correctedStart) && _graphLoader.GetNode(correctedStart) != null)
                {
                    _resolvedStartNodeId = correctedStart;
                    _stableStartNodeId = correctedStart;
                }
                else
                {
                    _status = $"Start node '{_resolvedStartNodeId}' is not in the active graph.";
                    return;
                }
            }

            if (ShouldAutoAvoidTrivialDestination(reason) &&
                !string.IsNullOrWhiteSpace(destination.indoor_node_id) &&
                string.Equals(destination.indoor_node_id, _resolvedStartNodeId, System.StringComparison.OrdinalIgnoreCase) &&
                TrySelectAlternativeDestinationForStart(_resolvedStartNodeId, out LocationPoint alternativeDestination))
            {
                destination = alternativeDestination;
                _status = $"Auto-selected nearby destination '{destination.name}' for better wayfinding in {GetActiveAreaLabel()}.";
            }

            _routeCalculator.CalculateIndoorRoute(_resolvedStartNodeId, destination.indoor_node_id, level, forceImmediateRouteRefresh, reason);
        }

        private void SnapViewToStartNodeIfNeeded(bool force = false)
        {
            if (!force && !snapViewToStartOnInitialRoute)
            {
                return;
            }

            if (_graphLoader == null || _graphLoader.NodesById == null || _graphLoader.NodesById.Count == 0)
            {
                return;
            }

            Transform reference = GetStartReferenceTransform();
            if (reference == null)
            {
                return;
            }

            string targetNodeId = startNodeId;
            if (string.IsNullOrWhiteSpace(targetNodeId) || _graphLoader.GetNode(targetNodeId) == null)
            {
                targetNodeId = ResolveStartNodeId();
            }

            if (string.IsNullOrWhiteSpace(targetNodeId))
            {
                targetNodeId = FindNearestGraphNodeId(reference.position, onlyCurrentLevel: false);
            }

            Node targetNode = string.IsNullOrWhiteSpace(targetNodeId) ? null : _graphLoader.GetNode(targetNodeId);
            if (targetNode == null)
            {
                return;
            }

            float eyeHeight = Mathf.Max(0.2f, initialViewEyeHeightMeters);
            float backOffset = Mathf.Max(0f, initialViewBackOffsetMeters);

            Vector3 focusPoint = new Vector3(targetNode.position.x, targetNode.position.y + 0.2f, targetNode.position.z);
            Vector3 initialPosition = new Vector3(targetNode.position.x, targetNode.position.y + eyeHeight, targetNode.position.z - backOffset);

            reference.position = initialPosition;
            reference.LookAt(focusPoint);

            if (_simulationProvider != null)
            {
                _simulationProvider.SetView(reference.rotation.eulerAngles.y, 0f);
            }

            if (_simulatedObjectDriver != null)
            {
                _simulatedObjectDriver.SetTarget(reference);
                _simulatedObjectDriver.SetSimulationProvider(_simulationProvider);
                _simulatedObjectDriver.SetLockY(false);
            }
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

        private static bool ShouldAutoAvoidTrivialDestination(string reason)
        {
            return string.Equals(reason, "Initial Auto Route", System.StringComparison.OrdinalIgnoreCase);
        }

        private bool TrySelectAlternativeDestinationForStart(string startNodeId, out LocationPoint selectedDestination)
        {
            selectedDestination = null;
            if (string.IsNullOrWhiteSpace(startNodeId) || _graphLoader == null || _uiDestinations.Count <= 1)
            {
                return false;
            }

            Node startNode = _graphLoader.GetNode(startNodeId);
            if (startNode == null)
            {
                return false;
            }

            int bestIndex = -1;
            float bestDistance = float.PositiveInfinity;
            for (int i = 0; i < _uiDestinations.Count; i++)
            {
                LocationPoint candidate = _uiDestinations[i];
                if (candidate == null || string.IsNullOrWhiteSpace(candidate.indoor_node_id))
                {
                    continue;
                }

                if (string.Equals(candidate.indoor_node_id, startNodeId, System.StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                Node destinationNode = _graphLoader.GetNode(candidate.indoor_node_id);
                if (destinationNode == null)
                {
                    continue;
                }

                float distance = HorizontalDistance(startNode.position, destinationNode.position);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestIndex = i;
                }
            }

            if (bestIndex < 0)
            {
                return false;
            }

            destinationIndex = bestIndex;
            selectedDestination = _uiDestinations[bestIndex];
            _destinationDropdownExpanded = false;
            UpdateSelectedDestinationMarker();
            return true;
        }

        private string GetActiveAreaLabel()
        {
            return useNusMapArea ? "NUS Engineering" : "Queenstown";
        }

        private string ResolveStartNodeId()
        {
            string fallbackStartNodeId = ResolveFallbackStartNodeId();
            if (_graphLoader == null || _graphLoader.NodesById.Count == 0)
            {
                return fallbackStartNodeId;
            }

            bool useDynamicStart = alwaysRouteFromCurrentPosition || useNearestNodeAsStart;
            if (!useDynamicStart)
            {
                return fallbackStartNodeId;
            }

            Transform reference = GetStartReferenceTransform();
            if (reference == null)
            {
                return fallbackStartNodeId;
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
            return string.IsNullOrWhiteSpace(stabilized) ? fallbackStartNodeId : stabilized;
        }

        private string ResolveFallbackStartNodeId()
        {
            string areaDefaultStartNodeId = useNusMapArea ? nusDefaultStartNodeId : queenstownDefaultStartNodeId;
            string configuredStartNodeId = !string.IsNullOrWhiteSpace(startNodeId)
                ? startNodeId.Trim()
                : null;

            if (_graphLoader == null || _graphLoader.NodesById == null || _graphLoader.NodesById.Count == 0)
            {
                if (!string.IsNullOrWhiteSpace(areaDefaultStartNodeId))
                {
                    return areaDefaultStartNodeId.Trim();
                }

                return configuredStartNodeId;
            }

            if (IsValidFallbackStartNode(configuredStartNodeId))
            {
                return configuredStartNodeId;
            }

            if (IsValidFallbackStartNode(areaDefaultStartNodeId))
            {
                return areaDefaultStartNodeId.Trim();
            }

            string locationStartNodeId = FindFallbackStartNodeFromLocations();
            if (IsValidFallbackStartNode(locationStartNodeId))
            {
                return locationStartNodeId;
            }

            string anyGraphNodeId = FindAnyEligibleGraphNodeId();
            if (!string.IsNullOrWhiteSpace(anyGraphNodeId))
            {
                return anyGraphNodeId;
            }

            return configuredStartNodeId;
        }

        private bool IsValidFallbackStartNode(string nodeId)
        {
            if (string.IsNullOrWhiteSpace(nodeId) || _graphLoader == null)
            {
                return false;
            }

            string trimmed = nodeId.Trim();
            if (_graphLoader.GetNode(trimmed) == null)
            {
                return false;
            }

            if (_boundaryConstraintManager != null && !_boundaryConstraintManager.IsNodeAllowed(trimmed))
            {
                return false;
            }

            return true;
        }

        private string FindFallbackStartNodeFromLocations()
        {
            if (_locationManager == null || _locationManager.Locations == null)
            {
                return null;
            }

            IReadOnlyList<LocationPoint> locations = _locationManager.Locations;
            for (int i = 0; i < locations.Count; i++)
            {
                LocationPoint location = locations[i];
                if (location == null || string.IsNullOrWhiteSpace(location.indoor_node_id))
                {
                    continue;
                }

                string nodeId = location.indoor_node_id.Trim();
                if (IsValidFallbackStartNode(nodeId))
                {
                    return nodeId;
                }
            }

            return null;
        }

        private string FindAnyEligibleGraphNodeId()
        {
            if (_graphLoader == null || _graphLoader.NodesById == null)
            {
                return null;
            }

            foreach (var kvp in _graphLoader.NodesById)
            {
                Node node = kvp.Value;
                if (node == null || !IsNodeEligibleForStart(node, onlyCurrentLevel: false))
                {
                    continue;
                }

                return kvp.Key;
            }

            return null;
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

            float minMove = GetEffectiveContinuousRefreshMinMoveMeters();
            bool movedEnough = HorizontalDistance(reference.position, _lastContinuousRefreshPosition) >= minMove;
            if (!movedEnough)
            {
                return;
            }

            _lastContinuousRefreshPosition = reference.position;
            RecalculateCurrentRoute("Continuous movement refresh");
        }

        private float GetEffectiveContinuousRefreshMinMoveMeters()
        {
            float simulationThreshold = Mathf.Max(0.2f, continuousRefreshMinMoveMeters);
            if (_gpsManager == null || _gpsManager.IsUsingSimulation)
            {
                return simulationThreshold;
            }

            float deviceThreshold = Mathf.Max(0f, deviceSensorContinuousRefreshMinMoveMeters);
            return Mathf.Max(simulationThreshold, deviceThreshold);
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
