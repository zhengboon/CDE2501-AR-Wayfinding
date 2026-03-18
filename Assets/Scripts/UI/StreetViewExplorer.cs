using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using CDE2501.Wayfinding.IndoorGraph;
using CDE2501.Wayfinding.Location;
using CDE2501.Wayfinding.Routing;
using UnityEngine;
using UnityEngine.Networking;

namespace CDE2501.Wayfinding.UI
{
    [Serializable]
    public class StreetViewHeadingImage
    {
        public int heading;
        public string image;
    }

    [Serializable]
    public class StreetViewNodeData
    {
        public string nodeId;
        public int index;
        public string viewType;
        public Vector3 position;
        public double lat;
        public double lon;
        public List<StreetViewHeadingImage> headingImages = new List<StreetViewHeadingImage>();
        public string fallbackImage;
        public List<string> adjacentNodeIds = new List<string>();
    }

    [Serializable]
    public class StreetViewManifestData
    {
        public string version;
        public string generatedAtUtc;
        public string kmlPath;
        public string polygonName;
        public string fitMode;
        public bool googleStreetViewEnabled;
        public List<int> headings = new List<int>();
        public int nodeCount;
        public int googleNodeCount;
        public int youtubeFallbackCount;
        public List<StreetViewNodeData> nodes = new List<StreetViewNodeData>();
        public List<string> warnings = new List<string>();
    }

    public class StreetViewExplorer : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] private GraphLoader graphLoader;
        [SerializeField] private Transform startReferenceTransform;
        [SerializeField] private SimulationProvider simulationProvider;
        [SerializeField] private RouteCalculator routeCalculator;
        [SerializeField] private bool followMainCameraIfReferenceMissing = true;
        [SerializeField] private bool driveSimulationProvider = true;

        [Header("Data")]
        [SerializeField] private string manifestFileName = "street_view_manifest.json";
        [SerializeField] private bool refreshManifestFromStreamingOnLoad = true;
        [SerializeField] private bool autoLoadOnStart = true;

        [Header("Mode")]
        [SerializeField] private bool showStreetView = true;
        [SerializeField] private KeyCode toggleKey = KeyCode.Y;
        [SerializeField] private KeyCode prevNodeKey = KeyCode.LeftBracket;
        [SerializeField] private KeyCode nextNodeKey = KeyCode.RightBracket;
        [SerializeField] private KeyCode turnLeftKey = KeyCode.Comma;
        [SerializeField] private KeyCode turnRightKey = KeyCode.Period;
        [SerializeField] private KeyCode snapToNearestKey = KeyCode.H;
        [SerializeField] private bool followReferenceMovement = true;
        [SerializeField, Min(0.05f)] private float followUpdateIntervalSeconds = 0.2f;
        [SerializeField, Min(0f)] private float followSwitchAdvantageMeters = 0.35f;
        [SerializeField] private bool movementOnlyMode = true;
        [SerializeField] private bool allowClickMove = false;

        [Header("Path Filter")]
        [SerializeField] private bool onlyUseCurrentRouteNodes = true;
        [SerializeField] private bool skipFirstPathNodeImage = true;
        [SerializeField] private bool filterEarlyFallbackFrames = true;
        [SerializeField, Min(1)] private int minAllowedFallbackFrameIndex = 5;

        [Header("Look Controls")]
        [SerializeField, Range(0.05f, 1.5f)] private float mouseLookSensitivity = 0.22f;
        [SerializeField, Range(-85f, 0f)] private float minPitchDegrees = -70f;
        [SerializeField, Range(0f, 85f)] private float maxPitchDegrees = 70f;
        [SerializeField, Min(2f)] private float clickDragThresholdPixels = 8f;
        [SerializeField, Min(0.05f)] private float maxClickDurationSeconds = 0.35f;

        [Header("Hotspots")]
        [SerializeField] private bool showNavigationHotspots = false;
        [SerializeField, Min(1)] private int fallbackNeighborCount = 6;
        [SerializeField, Min(1f)] private float fallbackNeighborMaxDistanceMeters = 120f;
        [SerializeField, Range(35f, 170f)] private float hotspotVisibleHalfAngle = 80f;
        [SerializeField, Min(14f)] private float hotspotMinSize = 22f;
        [SerializeField, Min(14f)] private float hotspotMaxSize = 42f;
        [SerializeField, Min(1f)] private float hotspotClickAssistPixels = 56f;
        [SerializeField] private Color hotspotColor = new Color(1f, 0.55f, 0.05f, 0.86f);
        [SerializeField] private Color hotspotTextColor = Color.white;

        [Header("HUD")]
        [SerializeField] private bool showHud = true;
        [SerializeField, Range(0.7f, 1.8f)] private float hudScale = 1f;
        [SerializeField, Range(11, 30)] private int hudBodyFontSize = 15;
        [SerializeField] private bool showControlHints = true;

        private readonly List<StreetViewNodeData> _nodes = new List<StreetViewNodeData>();
        private readonly List<StreetViewNodeData> _allNodes = new List<StreetViewNodeData>();
        private readonly Dictionary<string, int> _nodeIndexById = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Texture2D> _textureCache = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _textureInFlight = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _routeNodeFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<int> _neighborIndexBuffer = new List<int>(16);
        private readonly List<Hotspot> _hotspots = new List<Hotspot>(16);
        private readonly List<CandidateDistance> _candidateDistanceBuffer = new List<CandidateDistance>(128);
        private static readonly Regex FrameIndexRegex = new Regex(@"frame_(\d+)\.", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private GUIStyle _hudBodyStyle;
        private GUIStyle _hotspotLabelStyle;
        private Rect _hudBlockRect;

        private StreetViewManifestData _manifest;
        private bool _manifestReady;
        private int _currentNodeIndex;
        private int _currentHeadingIndex;
        private string _currentImagePath;
        private Texture2D _currentTexture;
        private string _status = "Street View idle.";
        private RouteResult _latestRouteResult;
        private string _lastRouteSignature = string.Empty;

        private float _viewYaw;
        private float _viewPitch;
        private bool _viewInitialized;
        private float _nextFollowUpdateTime;
        private bool _leftMouseDown;
        private bool _isDraggingLook;
        private Vector2 _mouseDownGui;
        private Vector2 _lastMouseGui;
        private float _mouseDownTime;

        private struct Hotspot
        {
            public int targetNodeIndex;
            public Rect rect;
            public float relativeAngle;
            public float distanceMeters;
            public string nodeId;
        }

        private struct CandidateDistance
        {
            public int index;
            public float sqrDistance;
        }

        public bool ManifestReady => _manifestReady;
        public int NodeCount => _nodes.Count;
        public int GoogleNodeCount => _manifest != null ? _manifest.googleNodeCount : 0;
        public int YoutubeFallbackNodeCount => _manifest != null ? _manifest.youtubeFallbackCount : 0;
        public bool IsStreetViewActive => showStreetView;

        private void Awake()
        {
            if (graphLoader == null)
            {
                graphLoader = FindObjectOfType<GraphLoader>();
            }

            if (simulationProvider == null)
            {
                simulationProvider = FindObjectOfType<SimulationProvider>();
            }

            if (routeCalculator == null)
            {
                routeCalculator = FindObjectOfType<RouteCalculator>();
            }
        }

        private void OnEnable()
        {
            if (graphLoader != null)
            {
                graphLoader.OnGraphLoaded += HandleGraphLoaded;
            }

            if (routeCalculator != null)
            {
                routeCalculator.OnRouteUpdated += HandleRouteUpdated;
            }
        }

        private void OnDisable()
        {
            if (graphLoader != null)
            {
                graphLoader.OnGraphLoaded -= HandleGraphLoaded;
            }

            if (routeCalculator != null)
            {
                routeCalculator.OnRouteUpdated -= HandleRouteUpdated;
            }

            _leftMouseDown = false;
            _isDraggingLook = false;
        }

        private void Start()
        {
            if (graphLoader != null && graphLoader.NodesById.Count == 0)
            {
                graphLoader.LoadGraph();
            }

            if (autoLoadOnStart)
            {
                LoadManifest();
            }
        }

        private void Update()
        {
            if (Input.GetKeyDown(toggleKey))
            {
                showStreetView = !showStreetView;
                _status = showStreetView ? "Street View enabled." : "Street View disabled.";
            }

            if (simulationProvider == null)
            {
                simulationProvider = FindObjectOfType<SimulationProvider>();
            }

            if (routeCalculator == null)
            {
                routeCalculator = FindObjectOfType<RouteCalculator>();
                if (routeCalculator != null)
                {
                    routeCalculator.OnRouteUpdated += HandleRouteUpdated;
                }
            }

            if (followMainCameraIfReferenceMissing && startReferenceTransform == null && Camera.main != null)
            {
                startReferenceTransform = Camera.main.transform;
            }

            if (!showStreetView || !_manifestReady || _nodes.Count == 0)
            {
                return;
            }

            EnsureViewInitialized();
            HandleKeyboardNavigation();
            FollowNearestNodeFromReferenceIfNeeded();
            HandleMouseLookAndClickMove();
            SyncImageWithViewYaw();
            PushViewToSimulation();
        }

        private void OnDestroy()
        {
            foreach (var kv in _textureCache)
            {
                if (kv.Value != null)
                {
                    Destroy(kv.Value);
                }
            }

            _textureCache.Clear();
            _textureInFlight.Clear();
        }

        private void OnGUI()
        {
            if (!showStreetView)
            {
                return;
            }

            EnsureStyles();
            Rect viewRect = new Rect(0f, 0f, Screen.width, Screen.height);
            DrawImageArea(viewRect);

            if (_manifestReady && _nodes.Count > 0 && showNavigationHotspots && !movementOnlyMode)
            {
                DrawHotspots(viewRect);
            }
            else
            {
                _hotspots.Clear();
            }

            if (showHud)
            {
                DrawHud(viewRect);
            }
            else
            {
                _hudBlockRect = Rect.zero;
            }
        }

        public void SetGraphLoader(GraphLoader loader)
        {
            if (graphLoader != null)
            {
                graphLoader.OnGraphLoaded -= HandleGraphLoaded;
            }

            graphLoader = loader;
            if (graphLoader != null && isActiveAndEnabled)
            {
                graphLoader.OnGraphLoaded += HandleGraphLoaded;
            }

        }

        public void SetStartReferenceTransform(Transform referenceTransform)
        {
            startReferenceTransform = referenceTransform;
        }

        public void SetRouteCalculator(RouteCalculator calculator)
        {
            if (routeCalculator != null)
            {
                routeCalculator.OnRouteUpdated -= HandleRouteUpdated;
            }

            routeCalculator = calculator;
            if (routeCalculator != null && isActiveAndEnabled)
            {
                routeCalculator.OnRouteUpdated += HandleRouteUpdated;
            }

            RebuildActiveNodeList();
        }

        public void LoadManifest()
        {
            StartCoroutine(LoadManifestRoutine());
        }

        public void ToggleStreetView()
        {
            showStreetView = !showStreetView;
        }

        public void SetStreetViewActive(bool active)
        {
            showStreetView = active;
        }

        private void HandleKeyboardNavigation()
        {
            if (Input.GetKeyDown(prevNodeKey))
            {
                StepNode(-1);
            }

            if (Input.GetKeyDown(nextNodeKey))
            {
                StepNode(1);
            }

            if (Input.GetKeyDown(turnLeftKey))
            {
                StepHeading(-1);
            }

            if (Input.GetKeyDown(turnRightKey))
            {
                StepHeading(1);
            }

            if (Input.GetKeyDown(snapToNearestKey))
            {
                SnapToNearestNodeFromReference();
            }
        }

        private void HandleMouseLookAndClickMove()
        {
            Vector2 guiMouse = GetMousePositionGui();

            if (Input.GetMouseButtonDown(0))
            {
                _leftMouseDown = true;
                _isDraggingLook = false;
                _mouseDownGui = guiMouse;
                _lastMouseGui = guiMouse;
                _mouseDownTime = Time.unscaledTime;
            }

            if (_leftMouseDown && Input.GetMouseButton(0))
            {
                Vector2 dragFromStart = guiMouse - _mouseDownGui;
                if (!_isDraggingLook && dragFromStart.sqrMagnitude >= clickDragThresholdPixels * clickDragThresholdPixels)
                {
                    _isDraggingLook = true;
                }

                if (_isDraggingLook)
                {
                    Vector2 delta = guiMouse - _lastMouseGui;
                    _viewYaw = NormalizeDegrees(_viewYaw + (delta.x * mouseLookSensitivity));
                    _viewPitch = Mathf.Clamp(_viewPitch - (delta.y * mouseLookSensitivity), minPitchDegrees, maxPitchDegrees);
                }

                _lastMouseGui = guiMouse;
            }

            if (_leftMouseDown && Input.GetMouseButtonUp(0))
            {
                bool isClick = !_isDraggingLook && (Time.unscaledTime - _mouseDownTime) <= maxClickDurationSeconds;
                if (!movementOnlyMode && allowClickMove && isClick)
                {
                    TryMoveToClickedNeighbor(guiMouse);
                }

                _leftMouseDown = false;
                _isDraggingLook = false;
            }
        }

        private void DrawImageArea(Rect rect)
        {
            if (_nodes.Count == 0)
            {
                DrawFilledRect(rect, new Color(0f, 0f, 0f, 1f));
                GUI.Label(new Rect(18f, 14f, rect.width - 32f, rect.height - 28f), _status, _hudBodyStyle);
                return;
            }

            if (_currentTexture != null)
            {
                GUI.DrawTexture(rect, _currentTexture, ScaleMode.ScaleAndCrop, true);
                return;
            }

            DrawFilledRect(rect, new Color(0f, 0f, 0f, 1f));
            GUI.Label(new Rect(18f, 14f, rect.width - 32f, 28f), "Loading Street View image...", _hudBodyStyle);
        }

        private void DrawHotspots(Rect viewRect)
        {
            _hotspots.Clear();
            StreetViewNodeData current = GetCurrentNode();
            if (current == null)
            {
                return;
            }

            CollectNeighborIndices(current, _neighborIndexBuffer);
            if (_neighborIndexBuffer.Count == 0)
            {
                return;
            }

            float halfFov = Mathf.Max(20f, hotspotVisibleHalfAngle);
            float horizonY = viewRect.y + (viewRect.height * 0.74f);
            float horizontalExtent = viewRect.width * 0.44f;
            float maxDist = Mathf.Max(5f, fallbackNeighborMaxDistanceMeters);

            for (int i = 0; i < _neighborIndexBuffer.Count; i++)
            {
                int neighborIndex = _neighborIndexBuffer[i];
                if (neighborIndex < 0 || neighborIndex >= _nodes.Count)
                {
                    continue;
                }

                StreetViewNodeData neighbor = _nodes[neighborIndex];
                Vector3 toNeighbor = neighbor.position - current.position;
                float planarMagnitude = new Vector2(toNeighbor.x, toNeighbor.z).magnitude;
                if (planarMagnitude <= 0.01f)
                {
                    continue;
                }

                float worldYaw = Mathf.Atan2(toNeighbor.x, toNeighbor.z) * Mathf.Rad2Deg;
                float relativeAngle = Mathf.DeltaAngle(_viewYaw, worldYaw);
                if (Mathf.Abs(relativeAngle) > halfFov)
                {
                    continue;
                }

                float t = Mathf.Clamp(relativeAngle / halfFov, -1f, 1f);
                float x = viewRect.center.x + (t * horizontalExtent);
                float dist01 = Mathf.Clamp01(planarMagnitude / maxDist);
                float y = horizonY - ((1f - dist01) * 32f);
                float size = Mathf.Lerp(hotspotMaxSize, hotspotMinSize, dist01);
                Rect hotspotRect = new Rect(x - (size * 0.5f), y - (size * 0.5f), size, size);

                _hotspots.Add(new Hotspot
                {
                    targetNodeIndex = neighborIndex,
                    rect = hotspotRect,
                    relativeAngle = relativeAngle,
                    distanceMeters = planarMagnitude,
                    nodeId = neighbor.nodeId
                });

                DrawFilledRect(hotspotRect, hotspotColor);
                GUI.Label(hotspotRect, "GO", _hotspotLabelStyle);
            }

            if (_hotspots.Count == 0 && _neighborIndexBuffer.Count > 0)
            {
                int bestIndex = -1;
                float bestDistance = float.PositiveInfinity;
                for (int i = 0; i < _neighborIndexBuffer.Count; i++)
                {
                    int neighborIndex = _neighborIndexBuffer[i];
                    if (neighborIndex < 0 || neighborIndex >= _nodes.Count)
                    {
                        continue;
                    }

                    float d = Vector3.Distance(current.position, _nodes[neighborIndex].position);
                    if (d < bestDistance)
                    {
                        bestDistance = d;
                        bestIndex = neighborIndex;
                    }
                }

                if (bestIndex >= 0)
                {
                    Rect fallbackRect = new Rect(viewRect.center.x - 20f, viewRect.y + (viewRect.height * 0.74f) - 20f, 40f, 40f);
                    _hotspots.Add(new Hotspot
                    {
                        targetNodeIndex = bestIndex,
                        rect = fallbackRect,
                        relativeAngle = 0f,
                        distanceMeters = bestDistance,
                        nodeId = _nodes[bestIndex].nodeId
                    });
                    DrawFilledRect(fallbackRect, hotspotColor);
                    GUI.Label(fallbackRect, "GO", _hotspotLabelStyle);
                }
            }
        }

        private void DrawHud(Rect viewRect)
        {
            float scale = Mathf.Clamp(hudScale, 0.7f, 1.8f);
            float width = Mathf.Min(viewRect.width - 16f, 640f * scale);
            float height = Mathf.Min(viewRect.height * 0.38f, 160f * scale);
            _hudBlockRect = new Rect(8f, 8f, width, height);

            DrawFilledRect(_hudBlockRect, new Color(0f, 0f, 0f, 0.5f));

            StreetViewNodeData node = GetCurrentNode();
            string nodeLabel = node != null
                ? $"Node {(_currentNodeIndex + 1)}/{_nodes.Count} | {node.nodeId} | {node.viewType}"
                : "Node: none";

            string message =
                $"{nodeLabel}\n" +
                $"Heading: {NormalizeDegrees(_viewYaw):0.#} deg | Pitch: {_viewPitch:0.#} deg\n" +
                $"{_status}";

            if (showControlHints)
            {
                message += "\nMouse drag look. Background follows your movement.";
                message += "\nMove with WASD. Yaw: Q/E or Left/Right arrows. Pitch: R/F or Up/Down arrows. H snap, Y toggle.";
                message += movementOnlyMode ? "\nClick-to-move is disabled." : "\nClick-to-move is enabled.";
            }

            Rect contentRect = new Rect(_hudBlockRect.x + 10f, _hudBlockRect.y + 8f, _hudBlockRect.width - 18f, _hudBlockRect.height - 14f);
            GUI.Label(contentRect, message, _hudBodyStyle);
        }

        private void StepNode(int delta)
        {
            if (_nodes.Count == 0)
            {
                return;
            }

            int count = _nodes.Count;
            _currentNodeIndex = (_currentNodeIndex + delta) % count;
            if (_currentNodeIndex < 0)
            {
                _currentNodeIndex += count;
            }

            _currentHeadingIndex = 0;
            EnsureViewInitialized();
            AlignViewYawToCurrentHeadingImage();
            _status = $"Moved to node {_currentNodeIndex + 1}/{_nodes.Count}.";
            RefreshCurrentTexture();
            ApplyCurrentNodeToSimulation();
        }

        private void StepHeading(int delta)
        {
            StreetViewNodeData node = GetCurrentNode();
            if (node == null || node.headingImages == null || node.headingImages.Count == 0)
            {
                return;
            }

            SortHeadingImages(node.headingImages);
            int count = node.headingImages.Count;
            _currentHeadingIndex = (_currentHeadingIndex + delta) % count;
            if (_currentHeadingIndex < 0)
            {
                _currentHeadingIndex += count;
            }

            _viewYaw = NormalizeDegrees(node.headingImages[_currentHeadingIndex].heading);
            _status = $"Heading changed to {node.headingImages[_currentHeadingIndex].heading} deg.";
            RefreshCurrentTexture();
            PushViewToSimulation();
        }

        private void SnapToNearestNodeFromReference()
        {
            if (_nodes.Count == 0)
            {
                return;
            }

            Vector3 from;
            if (startReferenceTransform != null)
            {
                from = startReferenceTransform.position;
            }
            else
            {
                from = _nodes[_currentNodeIndex].position;
            }

            float best = float.PositiveInfinity;
            int bestIndex = _currentNodeIndex;
            for (int i = 0; i < _nodes.Count; i++)
            {
                Vector3 p = _nodes[i].position;
                float d = Vector2.Distance(new Vector2(from.x, from.z), new Vector2(p.x, p.z));
                if (d < best)
                {
                    best = d;
                    bestIndex = i;
                }
            }

            _currentNodeIndex = bestIndex;
            _currentHeadingIndex = 0;
            EnsureViewInitialized();
            AlignViewYawToCurrentHeadingImage();
            _status = $"Snapped to nearest node ({best:0.0}m).";
            RefreshCurrentTexture();
            ApplyCurrentNodeToSimulation();
        }

        private void FollowNearestNodeFromReferenceIfNeeded()
        {
            if (!followReferenceMovement || startReferenceTransform == null || _nodes.Count == 0)
            {
                return;
            }

            if (Time.unscaledTime < _nextFollowUpdateTime)
            {
                return;
            }

            _nextFollowUpdateTime = Time.unscaledTime + followUpdateIntervalSeconds;

            Vector3 from = startReferenceTransform.position;
            int bestIndex = _currentNodeIndex;
            float bestDistance = float.PositiveInfinity;

            for (int i = 0; i < _nodes.Count; i++)
            {
                Vector3 p = _nodes[i].position;
                float d = Vector2.Distance(new Vector2(from.x, from.z), new Vector2(p.x, p.z));
                if (d < bestDistance)
                {
                    bestDistance = d;
                    bestIndex = i;
                }
            }

            if (bestIndex == _currentNodeIndex)
            {
                return;
            }

            Vector3 currentPos = _nodes[_currentNodeIndex].position;
            float currentDistance = Vector2.Distance(new Vector2(from.x, from.z), new Vector2(currentPos.x, currentPos.z));
            if ((currentDistance - bestDistance) < followSwitchAdvantageMeters)
            {
                return;
            }

            _currentNodeIndex = bestIndex;
            _currentHeadingIndex = 0;
            RefreshCurrentTexture();
            _status = $"Following movement. Nearest node: {bestDistance:0.0}m.";
        }

        private bool TryMoveToClickedNeighbor(Vector2 clickGuiPosition)
        {
            if (_hotspots.Count == 0)
            {
                return false;
            }

            if (_hudBlockRect.Contains(clickGuiPosition))
            {
                return false;
            }

            int chosenIndex = -1;
            float bestDistance = float.PositiveInfinity;

            for (int i = 0; i < _hotspots.Count; i++)
            {
                Hotspot hotspot = _hotspots[i];
                if (hotspot.rect.Contains(clickGuiPosition))
                {
                    chosenIndex = hotspot.targetNodeIndex;
                    break;
                }

                Vector2 center = hotspot.rect.center;
                float d = Vector2.Distance(center, clickGuiPosition);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    chosenIndex = hotspot.targetNodeIndex;
                }
            }

            if (chosenIndex < 0)
            {
                return false;
            }

            if (bestDistance > hotspotClickAssistPixels && !ContainsHotspot(chosenIndex, clickGuiPosition))
            {
                return false;
            }

            MoveToNodeIndex(chosenIndex, "Moved to clicked location.");
            return true;
        }

        private bool ContainsHotspot(int targetNodeIndex, Vector2 clickGuiPosition)
        {
            for (int i = 0; i < _hotspots.Count; i++)
            {
                Hotspot hotspot = _hotspots[i];
                if (hotspot.targetNodeIndex != targetNodeIndex)
                {
                    continue;
                }

                return hotspot.rect.Contains(clickGuiPosition);
            }

            return false;
        }

        private void MoveToNodeIndex(int nodeIndex, string statusMessage)
        {
            if (nodeIndex < 0 || nodeIndex >= _nodes.Count || nodeIndex == _currentNodeIndex)
            {
                return;
            }

            _currentNodeIndex = nodeIndex;
            _currentHeadingIndex = 0;
            AlignViewYawToCurrentHeadingImage();
            _status = statusMessage;
            RefreshCurrentTexture();
            ApplyCurrentNodeToSimulation();
        }

        private void HandleRouteUpdated(RouteResult result)
        {
            _latestRouteResult = result;
            if (!onlyUseCurrentRouteNodes)
            {
                return;
            }

            string signature = BuildRouteSignature(result);
            if (string.Equals(signature, _lastRouteSignature, StringComparison.Ordinal))
            {
                return;
            }

            _lastRouteSignature = signature;
            RebuildActiveNodeList();
        }

        private void RebuildActiveNodeList()
        {
            string previousNodeId = GetCurrentNode() != null ? GetCurrentNode().nodeId : string.Empty;

            _nodes.Clear();
            _nodeIndexById.Clear();
            _routeNodeFilter.Clear();

            if (_allNodes.Count == 0)
            {
                _manifestReady = false;
                _currentNodeIndex = 0;
                _currentTexture = null;
                _currentImagePath = string.Empty;
                return;
            }

            bool routeFilterEnabled = onlyUseCurrentRouteNodes;
            bool hasRoute = _latestRouteResult != null &&
                            _latestRouteResult.success &&
                            _latestRouteResult.nodePath != null &&
                            _latestRouteResult.nodePath.Count > 0;

            if (routeFilterEnabled && !hasRoute)
            {
                _manifestReady = false;
                _currentNodeIndex = 0;
                _currentTexture = null;
                _currentImagePath = string.Empty;
                _status = "Street View waiting for a route. Calculate route first.";
                return;
            }

            if (routeFilterEnabled && hasRoute)
            {
                int startPathIndex = (skipFirstPathNodeImage && _latestRouteResult.nodePath.Count > 1) ? 1 : 0;
                for (int i = startPathIndex; i < _latestRouteResult.nodePath.Count; i++)
                {
                    string nodeId = _latestRouteResult.nodePath[i];
                    if (!string.IsNullOrWhiteSpace(nodeId))
                    {
                        _routeNodeFilter.Add(nodeId);
                    }
                }
            }

            var routeMatchedNodes = new List<StreetViewNodeData>(_allNodes.Count);
            for (int i = 0; i < _allNodes.Count; i++)
            {
                StreetViewNodeData node = _allNodes[i];
                if (node == null || string.IsNullOrWhiteSpace(node.nodeId))
                {
                    continue;
                }

                if (routeFilterEnabled && !_routeNodeFilter.Contains(node.nodeId))
                {
                    continue;
                }

                routeMatchedNodes.Add(node);
            }

            if (filterEarlyFallbackFrames && routeMatchedNodes.Count > 0)
            {
                for (int i = 0; i < routeMatchedNodes.Count; i++)
                {
                    StreetViewNodeData node = routeMatchedNodes[i];
                    if (IsNodeImageUsable(node))
                    {
                        _nodes.Add(node);
                    }
                }

                // Safety fallback: if strict filtering removed every node, keep original route-matched set.
                if (_nodes.Count == 0)
                {
                    _nodes.AddRange(routeMatchedNodes);
                }
            }
            else
            {
                _nodes.AddRange(routeMatchedNodes);
            }

            _manifestReady = _nodes.Count > 0;
            if (!_manifestReady)
            {
                _currentNodeIndex = 0;
                _currentTexture = null;
                _currentImagePath = string.Empty;
                _status = "No Street View images on current path.";
                return;
            }

            _nodes.Sort((a, b) => a.index.CompareTo(b.index));
            for (int i = 0; i < _nodes.Count; i++)
            {
                StreetViewNodeData node = _nodes[i];
                if (!_nodeIndexById.ContainsKey(node.nodeId))
                {
                    _nodeIndexById[node.nodeId] = i;
                }
            }

            if (!string.IsNullOrWhiteSpace(previousNodeId) && _nodeIndexById.TryGetValue(previousNodeId, out int preservedIndex))
            {
                _currentNodeIndex = preservedIndex;
            }
            else
            {
                _currentNodeIndex = Mathf.Clamp(_currentNodeIndex, 0, _nodes.Count - 1);
            }

            _currentHeadingIndex = 0;
            _viewInitialized = false;
            EnsureViewInitialized();
            SnapToNearestNodeFromReference();
            RefreshCurrentTexture();
        }

        private bool IsNodeImageUsable(StreetViewNodeData node)
        {
            if (node == null)
            {
                return false;
            }

            if (node.headingImages != null && node.headingImages.Count > 0)
            {
                return true;
            }

            string fallback = node.fallbackImage;
            if (string.IsNullOrWhiteSpace(fallback))
            {
                return false;
            }

            if (!filterEarlyFallbackFrames)
            {
                return true;
            }

            if (!TryExtractFrameIndex(fallback, out int frameIndex))
            {
                return true;
            }

            return frameIndex >= Mathf.Max(1, minAllowedFallbackFrameIndex);
        }

        private static bool TryExtractFrameIndex(string imagePath, out int frameIndex)
        {
            frameIndex = 0;
            if (string.IsNullOrWhiteSpace(imagePath))
            {
                return false;
            }

            Match match = FrameIndexRegex.Match(imagePath);
            if (!match.Success || match.Groups.Count < 2)
            {
                return false;
            }

            return int.TryParse(match.Groups[1].Value, out frameIndex);
        }

        private void CollectNeighborIndices(StreetViewNodeData node, List<int> output)
        {
            output.Clear();
            if (node == null)
            {
                return;
            }

            if (node.adjacentNodeIds != null)
            {
                for (int i = 0; i < node.adjacentNodeIds.Count; i++)
                {
                    string adjacentNodeId = node.adjacentNodeIds[i];
                    if (string.IsNullOrWhiteSpace(adjacentNodeId))
                    {
                        continue;
                    }

                    if (_nodeIndexById.TryGetValue(adjacentNodeId, out int adjacentIndex) &&
                        adjacentIndex >= 0 &&
                        adjacentIndex < _nodes.Count &&
                        adjacentIndex != _currentNodeIndex &&
                        !output.Contains(adjacentIndex))
                    {
                        output.Add(adjacentIndex);
                    }
                }
            }

            if (output.Count > 0)
            {
                return;
            }

            _candidateDistanceBuffer.Clear();
            StreetViewNodeData current = GetCurrentNode();
            if (current == null)
            {
                return;
            }

            float maxDistSqr = fallbackNeighborMaxDistanceMeters * fallbackNeighborMaxDistanceMeters;
            for (int i = 0; i < _nodes.Count; i++)
            {
                if (i == _currentNodeIndex)
                {
                    continue;
                }

                StreetViewNodeData candidate = _nodes[i];
                float distSqr = (candidate.position - current.position).sqrMagnitude;
                if (distSqr > maxDistSqr)
                {
                    continue;
                }

                _candidateDistanceBuffer.Add(new CandidateDistance
                {
                    index = i,
                    sqrDistance = distSqr
                });
            }

            _candidateDistanceBuffer.Sort((a, b) => a.sqrDistance.CompareTo(b.sqrDistance));
            int take = Mathf.Min(fallbackNeighborCount, _candidateDistanceBuffer.Count);
            for (int i = 0; i < take; i++)
            {
                output.Add(_candidateDistanceBuffer[i].index);
            }
        }

        private StreetViewNodeData GetCurrentNode()
        {
            if (_nodes.Count == 0)
            {
                return null;
            }

            _currentNodeIndex = Mathf.Clamp(_currentNodeIndex, 0, _nodes.Count - 1);
            return _nodes[_currentNodeIndex];
        }

        private void EnsureViewInitialized()
        {
            if (_viewInitialized)
            {
                return;
            }

            float defaultYaw = 0f;
            float defaultPitch = 0f;
            if (simulationProvider != null)
            {
                defaultYaw = simulationProvider.CurrentHeading;
                defaultPitch = simulationProvider.CurrentPitch;
            }
            else if (startReferenceTransform != null)
            {
                defaultYaw = startReferenceTransform.rotation.eulerAngles.y;
            }

            _viewYaw = NormalizeDegrees(defaultYaw);
            _viewPitch = Mathf.Clamp(defaultPitch, minPitchDegrees, maxPitchDegrees);
            _viewInitialized = true;
            SyncImageWithViewYaw(force: true);
        }

        private void SyncImageWithViewYaw(bool force = false)
        {
            StreetViewNodeData node = GetCurrentNode();
            if (node == null)
            {
                return;
            }

            if (node.headingImages != null && node.headingImages.Count > 0)
            {
                SortHeadingImages(node.headingImages);
                int nearest = FindNearestHeadingIndex(node.headingImages, _viewYaw);
                if (force || nearest != _currentHeadingIndex)
                {
                    _currentHeadingIndex = nearest;
                    RefreshCurrentTexture();
                }
            }
            else if (force)
            {
                _currentHeadingIndex = 0;
                RefreshCurrentTexture();
            }
        }

        private void AlignViewYawToCurrentHeadingImage()
        {
            StreetViewNodeData node = GetCurrentNode();
            if (node == null || node.headingImages == null || node.headingImages.Count == 0)
            {
                return;
            }

            SortHeadingImages(node.headingImages);
            _currentHeadingIndex = Mathf.Clamp(_currentHeadingIndex, 0, node.headingImages.Count - 1);
            _viewYaw = NormalizeDegrees(node.headingImages[_currentHeadingIndex].heading);
        }

        private string GetCurrentImagePath()
        {
            StreetViewNodeData node = GetCurrentNode();
            if (node == null)
            {
                return string.Empty;
            }

            if (node.headingImages != null && node.headingImages.Count > 0)
            {
                SortHeadingImages(node.headingImages);
                _currentHeadingIndex = Mathf.Clamp(_currentHeadingIndex, 0, node.headingImages.Count - 1);
                return node.headingImages[_currentHeadingIndex].image ?? string.Empty;
            }

            return node.fallbackImage ?? string.Empty;
        }

        private void RefreshCurrentTexture()
        {
            _currentImagePath = GetCurrentImagePath();
            if (string.IsNullOrWhiteSpace(_currentImagePath))
            {
                _currentTexture = null;
                return;
            }

            if (_textureCache.TryGetValue(_currentImagePath, out Texture2D tex) && tex != null)
            {
                _currentTexture = tex;
                return;
            }

            _currentTexture = null;
            RequestTexture(_currentImagePath);
        }

        private void PushViewToSimulation()
        {
            if (driveSimulationProvider && simulationProvider != null && simulationProvider.ForceSimulationMode)
            {
                simulationProvider.SetView(_viewYaw, _viewPitch);
            }
            else if (startReferenceTransform != null)
            {
                startReferenceTransform.rotation = Quaternion.Euler(-_viewPitch, _viewYaw, 0f);
            }
        }

        private void ApplyCurrentNodeToSimulation()
        {
            StreetViewNodeData node = GetCurrentNode();
            if (node == null)
            {
                return;
            }

            if (driveSimulationProvider && simulationProvider != null && simulationProvider.ForceSimulationMode)
            {
                simulationProvider.TeleportTo(new GeoPoint(node.lat, node.lon), _viewYaw, _viewPitch);
                return;
            }

            if (startReferenceTransform != null)
            {
                startReferenceTransform.position = node.position;
                startReferenceTransform.rotation = Quaternion.Euler(-_viewPitch, _viewYaw, 0f);
            }
        }

        private void RequestTexture(string relativeImagePath)
        {
            if (string.IsNullOrWhiteSpace(relativeImagePath))
            {
                return;
            }

            if (_textureCache.ContainsKey(relativeImagePath) || _textureInFlight.Contains(relativeImagePath))
            {
                return;
            }

            _textureInFlight.Add(relativeImagePath);
            StartCoroutine(LoadTextureRoutine(relativeImagePath));
        }

        private IEnumerator LoadTextureRoutine(string relativeImagePath)
        {
            string persistentPath = Path.Combine(Application.persistentDataPath, "Data", relativeImagePath);
            string streamingPath = Path.Combine(Application.streamingAssetsPath, "Data", relativeImagePath);

            if (!File.Exists(persistentPath))
            {
                yield return CopyBinaryFromStreamingAssets(streamingPath, persistentPath);
            }

            string finalPath = File.Exists(persistentPath) ? persistentPath : streamingPath;
            using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(ToUnityWebRequestPath(finalPath)))
            {
                yield return request.SendWebRequest();
                if (request.result == UnityWebRequest.Result.Success)
                {
                    Texture2D texture = DownloadHandlerTexture.GetContent(request);
                    if (texture != null)
                    {
                        texture.wrapMode = TextureWrapMode.Clamp;
                        texture.filterMode = FilterMode.Bilinear;
                    }

                    _textureCache[relativeImagePath] = texture;
                    if (string.Equals(_currentImagePath, relativeImagePath, StringComparison.OrdinalIgnoreCase))
                    {
                        _currentTexture = texture;
                    }
                }
            }

            _textureInFlight.Remove(relativeImagePath);
        }

        private IEnumerator LoadManifestRoutine()
        {
            _status = "Loading street_view_manifest.json...";
            string persistentPath = GetPersistentPath(manifestFileName);
            string streamingPath = GetStreamingPath(manifestFileName);

            if (refreshManifestFromStreamingOnLoad || !File.Exists(persistentPath))
            {
                yield return CopyTextFromStreamingAssets(streamingPath, persistentPath);
            }

            if (!File.Exists(persistentPath))
            {
                _manifestReady = false;
                _status = "street_view_manifest.json not found.";
                yield break;
            }

            string json = File.ReadAllText(persistentPath);
            _manifest = JsonUtility.FromJson<StreetViewManifestData>(json);

            _allNodes.Clear();
            _nodeIndexById.Clear();

            if (_manifest != null && _manifest.nodes != null)
            {
                _allNodes.AddRange(_manifest.nodes);
                _allNodes.Sort((a, b) => a.index.CompareTo(b.index));
            }

            RebuildActiveNodeList();

            if (_manifestReady)
            {
                _status = $"Street View loaded: {_nodes.Count} active path nodes (google {_manifest.googleNodeCount}, fallback {_manifest.youtubeFallbackCount}).";
            }
            else
            {
                _status = onlyUseCurrentRouteNodes
                    ? "Street View manifest loaded. No route-matched nodes are available yet."
                    : "Street View manifest loaded but no usable nodes are available.";
            }
        }

        private void EnsureStyles()
        {
            if (_hudBodyStyle != null && _hotspotLabelStyle != null)
            {
                return;
            }

            _hudBodyStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = hudBodyFontSize,
                alignment = TextAnchor.UpperLeft,
                wordWrap = true,
                clipping = TextClipping.Clip
            };
            _hudBodyStyle.normal.textColor = Color.white;

            _hotspotLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.Max(12, hudBodyFontSize - 1),
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            _hotspotLabelStyle.normal.textColor = hotspotTextColor;
        }

        private void HandleGraphLoaded(bool success, string message)
        {
            _ = success;
            _ = message;
        }

        private static int FindNearestHeadingIndex(List<StreetViewHeadingImage> headingImages, float yawDegrees)
        {
            int bestIndex = 0;
            float bestAbsDelta = float.PositiveInfinity;
            for (int i = 0; i < headingImages.Count; i++)
            {
                float d = Mathf.Abs(Mathf.DeltaAngle(yawDegrees, headingImages[i].heading));
                if (d < bestAbsDelta)
                {
                    bestAbsDelta = d;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        private static void SortHeadingImages(List<StreetViewHeadingImage> images)
        {
            if (images == null || images.Count <= 1)
            {
                return;
            }

            images.Sort((a, b) => a.heading.CompareTo(b.heading));
        }

        private static float NormalizeDegrees(float degrees)
        {
            float normalized = degrees % 360f;
            return normalized < 0f ? normalized + 360f : normalized;
        }

        private static string BuildRouteSignature(RouteResult route)
        {
            if (route == null || !route.success || route.nodePath == null || route.nodePath.Count == 0)
            {
                return "none";
            }

            var builder = new StringBuilder(route.nodePath.Count * 10);
            for (int i = 0; i < route.nodePath.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append('|');
                }

                builder.Append(route.nodePath[i]);
            }

            return builder.ToString();
        }

        private static Vector2 GetMousePositionGui()
        {
            return new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
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

        private static void DrawFilledRect(Rect rect, Color color)
        {
            Color prev = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true);
            GUI.color = prev;
        }

        private static IEnumerator CopyTextFromStreamingAssets(string sourcePath, string destinationPath)
        {
            EnsureDataFolder(destinationPath);
            UnityWebRequest request = UnityWebRequest.Get(ToUnityWebRequestPath(sourcePath));
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                File.WriteAllText(destinationPath, request.downloadHandler.text);
            }

            request.Dispose();
        }

        private static IEnumerator CopyBinaryFromStreamingAssets(string sourcePath, string destinationPath)
        {
            EnsureDataFolder(destinationPath);
            UnityWebRequest request = UnityWebRequest.Get(ToUnityWebRequestPath(sourcePath));
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                File.WriteAllBytes(destinationPath, request.downloadHandler.data);
            }

            request.Dispose();
        }

        private static string GetStreamingPath(string fileName)
        {
            return Path.Combine(Application.streamingAssetsPath, "Data", fileName);
        }

        private static string GetPersistentPath(string fileName)
        {
            return Path.Combine(Application.persistentDataPath, "Data", fileName);
        }

        private static string ToUnityWebRequestPath(string path)
        {
            if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("file://", StringComparison.OrdinalIgnoreCase) ||
                path.Contains("://"))
            {
                return path;
            }

            return "file://" + path;
        }

        private static void EnsureDataFolder(string filePath)
        {
            string dir = Path.GetDirectoryName(filePath);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }
    }
}
