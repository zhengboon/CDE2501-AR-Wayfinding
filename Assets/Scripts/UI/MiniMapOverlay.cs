using System;
using System.Collections;
using System.IO;
using CDE2501.Wayfinding.Data;
using CDE2501.Wayfinding.IndoorGraph;
using CDE2501.Wayfinding.Routing;
using UnityEngine;
using UnityEngine.Networking;

namespace CDE2501.Wayfinding.UI
{
    public class MiniMapOverlay : MonoBehaviour
    {
        [Header("Data Sources")]
        [SerializeField] private GraphLoader graphLoader;
        [SerializeField] private RouteCalculator routeCalculator;
        [SerializeField] private LocationManager locationManager;
        [SerializeField] private Transform startReferenceTransform;
        [SerializeField] private bool followMainCameraIfReferenceMissing = true;

        [Header("Window")]
        [SerializeField] private bool showMiniMap = true;
        [SerializeField, Range(0.8f, 2.5f)] private float uiScale = 1f;
        [SerializeField] private Vector2 panelSize = new Vector2(300f, 300f);
        [SerializeField] private Vector2 panelStartPosition = new Vector2(980f, 12f);
        [SerializeField] private bool enableWindowResize = true;
        [SerializeField] private Vector2 minPanelSize = new Vector2(220f, 220f);
        [SerializeField] private Vector2 maxPanelSize = new Vector2(760f, 760f);
        [SerializeField, Min(0f)] private float boundsPaddingMeters = 2f;
        [Header("Map Image")]
        [SerializeField] private bool showMapImage = true;
        [SerializeField] private string mapImageFileName = "map_tile_z16_x51664_y32532.png";
        [SerializeField, Range(0.05f, 1f)] private float mapImageAlpha = 0.75f;
        [Header("Interaction")]
        [SerializeField] private bool enableMapInteraction = true;
        [SerializeField, Min(1f)] private float minMapZoom = 1f;
        [SerializeField, Min(1f)] private float maxMapZoom = 6f;
        [SerializeField, Range(0.01f, 1f)] private float zoomStep = 0.2f;
        [Header("Alignment")]
        [SerializeField] private bool useGeoAnchoredAlignment = true;
        [SerializeField] private bool enableManualAlignment = true;
        [SerializeField] private float worldToMapRotationDegrees = 0f;
        [SerializeField] private Vector2 worldToMapScale = Vector2.one;
        [SerializeField] private Vector2 worldToMapOffsetMeters = Vector2.zero;
        [Header("Hover Details")]
        [SerializeField] private bool showHoverDetails = true;
        [SerializeField, Min(0f)] private float miniMapHoverSearchRadiusMeters = 14f;
        [SerializeField, Min(1f)] private float screenHoverPixelRadius = 26f;
        [SerializeField] private Vector2 hoverTooltipOffset = new Vector2(14f, 16f);

        [Header("Style")]
        [SerializeField] private Color mapBackgroundColor = new Color(0f, 0f, 0f, 0.58f);
        [SerializeField] private Color edgeColor = new Color(1f, 1f, 1f, 0.24f);
        [SerializeField] private Color routeColor = new Color(1f, 0.55f, 0.05f, 1f);
        [SerializeField] private Color playerColor = new Color(0.1f, 0.9f, 1f, 1f);
        [SerializeField] private Color destinationColor = new Color(1f, 0.55f, 0.05f, 1f);
        [SerializeField, Min(1f)] private float edgeLineThickness = 1.2f;
        [SerializeField, Min(1f)] private float routeLineThickness = 3f;
        [SerializeField, Min(1f)] private float markerRadius = 4f;

        private RouteResult _lastRoute;
        private string _selectedDestinationNodeId;
        private Texture2D _pixel;
        private Texture2D _mapTexture;
        private bool _isMapTextureLoading;
        private Rect _panelRect;
        private bool _panelRectInitialized;
        private float _mapZoom = 1f;
        private Vector2 _mapPanPixels;
        private bool _isPanningMap;
        private Vector2 _lastPanMousePosition;
        private bool _isResizingPanel;
        private Vector2 _resizeStartMousePosition;
        private Vector2 _resizeStartPanelSize;
        private bool _geoMappingDirty = true;
        private bool _geoMappingReady;
        private bool _tileMetadataReady;
        private int _tileZoom;
        private int _tileX;
        private int _tileY;
        private double _latBias;
        private double _latXCoef;
        private double _latZCoef;
        private double _lonBias;
        private double _lonXCoef;
        private double _lonZCoef;
        private double _xBias;
        private double _xLatCoef;
        private double _xLonCoef;
        private double _zBias;
        private double _zLatCoef;
        private double _zLonCoef;

        private float _minX;
        private float _maxX;
        private float _minZ;
        private float _maxZ;
        private bool _boundsValid;

        private void Awake()
        {
            if (graphLoader == null)
            {
                graphLoader = FindObjectOfType<GraphLoader>();
            }

            if (routeCalculator == null)
            {
                routeCalculator = FindObjectOfType<RouteCalculator>();
            }

            if (locationManager == null)
            {
                locationManager = FindObjectOfType<LocationManager>();
            }

            EnsurePixelTexture();
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

            if (locationManager != null)
            {
                locationManager.OnLocationsChanged += HandleLocationsChanged;
            }
        }

        private void Start()
        {
            if (graphLoader != null && graphLoader.NodesById.Count > 0)
            {
                RecomputeBounds();
            }

            TryLoadMapTexture();
        }

        private void Update()
        {
            if (followMainCameraIfReferenceMissing && startReferenceTransform == null && Camera.main != null)
            {
                startReferenceTransform = Camera.main.transform;
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

            if (locationManager != null)
            {
                locationManager.OnLocationsChanged -= HandleLocationsChanged;
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
                RecomputeBounds();
            }
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
        }

        public void SetStartReferenceTransform(Transform referenceTransform)
        {
            startReferenceTransform = referenceTransform;
        }

        public void SetLocationManager(LocationManager manager)
        {
            if (locationManager != null)
            {
                locationManager.OnLocationsChanged -= HandleLocationsChanged;
            }

            locationManager = manager;
            if (locationManager != null && isActiveAndEnabled)
            {
                locationManager.OnLocationsChanged += HandleLocationsChanged;
            }

            _geoMappingDirty = true;
        }

        public void SetSelectedDestinationNodeId(string nodeId)
        {
            _selectedDestinationNodeId = nodeId;
        }

        private void HandleGraphLoaded(bool success, string message)
        {
            if (success)
            {
                RecomputeBounds();
                _geoMappingDirty = true;
            }
        }

        private void HandleLocationsChanged()
        {
            _geoMappingDirty = true;
        }

        private void HandleRouteUpdated(RouteResult result)
        {
            _lastRoute = result;
        }

        private void OnGUI()
        {
            if (!showMiniMap || graphLoader == null || !_boundsValid)
            {
                return;
            }

            EnsurePixelTexture();
            TryLoadMapTexture();
            EnsureGeoMapping();

            float scale = Mathf.Clamp(uiScale, 0.8f, 2.5f);
            Matrix4x4 prev = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

            EnsurePanelRect(scale);
            _panelRect = GUI.Window(GetInstanceID() + 2048, _panelRect, DrawWindow, "Mini Map");

            GUI.matrix = prev;
            DrawScreenHoverTooltip(scale);
        }

        private void DrawWindow(int id)
        {
            HandleWindowResize();

            Rect mapRect = new Rect(8f, 24f, _panelRect.width - 16f, _panelRect.height - 32f);
            HandleMapInteraction(mapRect);
            ClampMapPan(mapRect.size);

            DrawFilledRect(mapRect, mapBackgroundColor);
            DrawMapImageBackground(mapRect);

            GUI.BeginGroup(mapRect);
            DrawGraphEdges(mapRect.size);
            DrawActiveRoute(mapRect.size);
            DrawDestinationMarker(mapRect.size);
            DrawPlayerMarker(mapRect.size);
            GUI.EndGroup();
            DrawMiniMapHoverTooltip(mapRect);

            DrawMapControlButtons(mapRect);
            DrawResizeHandle();

            GUI.DragWindow(new Rect(0f, 0f, _panelRect.width, 22f));
        }

        private void HandleWindowResize()
        {
            if (!enableWindowResize)
            {
                return;
            }

            Event e = Event.current;
            if (e == null)
            {
                return;
            }

            Rect resizeHandleRect = new Rect(_panelRect.width - 18f, _panelRect.height - 18f, 14f, 14f);

            if (e.type == EventType.MouseDown && e.button == 0 && resizeHandleRect.Contains(e.mousePosition))
            {
                _isResizingPanel = true;
                _resizeStartMousePosition = e.mousePosition;
                _resizeStartPanelSize = panelSize;
                e.Use();
                return;
            }

            if (e.type == EventType.MouseDrag && _isResizingPanel)
            {
                Vector2 delta = e.mousePosition - _resizeStartMousePosition;
                float minWidth = Mathf.Max(160f, minPanelSize.x);
                float minHeight = Mathf.Max(140f, minPanelSize.y);
                float maxWidth = Mathf.Max(minWidth, maxPanelSize.x);
                float maxHeight = Mathf.Max(minHeight, maxPanelSize.y);

                panelSize = new Vector2(
                    Mathf.Clamp(_resizeStartPanelSize.x + delta.x, minWidth, maxWidth),
                    Mathf.Clamp(_resizeStartPanelSize.y + delta.y, minHeight, maxHeight));
                _panelRect.width = panelSize.x;
                _panelRect.height = panelSize.y;
                e.Use();
                return;
            }

            if (e.type == EventType.MouseUp && _isResizingPanel)
            {
                _isResizingPanel = false;
                e.Use();
            }
        }

        private void DrawResizeHandle()
        {
            if (!enableWindowResize)
            {
                return;
            }

            Rect resizeHandleRect = new Rect(_panelRect.width - 18f, _panelRect.height - 18f, 14f, 14f);
            GUI.Box(resizeHandleRect, "//");
        }

        private void DrawMapImageBackground(Rect mapRect)
        {
            if (!showMapImage || _mapTexture == null)
            {
                return;
            }

            Color previous = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(mapImageAlpha));
            GUI.BeginGroup(mapRect);
            Rect drawRect = GetMapContentRect(mapRect.size);
            GUI.DrawTexture(drawRect, _mapTexture, ScaleMode.StretchToFill, alphaBlend: true);
            GUI.EndGroup();
            GUI.color = previous;
        }

        private void DrawGraphEdges(Vector2 mapSize)
        {
            if (graphLoader == null || graphLoader.Edges == null || graphLoader.NodesById == null)
            {
                return;
            }

            for (int i = 0; i < graphLoader.Edges.Count; i++)
            {
                Edge edge = graphLoader.Edges[i];
                Node from = graphLoader.GetNode(edge.fromNode);
                Node to = graphLoader.GetNode(edge.toNode);
                if (from == null || to == null)
                {
                    continue;
                }

                Vector2 a = WorldToMiniMap(from.position, mapSize);
                Vector2 b = WorldToMiniMap(to.position, mapSize);
                DrawLine(a, b, edgeColor, edgeLineThickness);
            }
        }

        private void DrawActiveRoute(Vector2 mapSize)
        {
            if (_lastRoute == null || !_lastRoute.success || _lastRoute.nodePath == null || _lastRoute.nodePath.Count == 0)
            {
                return;
            }

            Node firstNode = graphLoader.GetNode(_lastRoute.nodePath[0]);
            if (firstNode != null && startReferenceTransform != null)
            {
                Vector2 player = WorldToMiniMap(startReferenceTransform.position, mapSize);
                Vector2 first = WorldToMiniMap(firstNode.position, mapSize);
                DrawLine(player, first, routeColor, routeLineThickness);
            }

            for (int i = 0; i < _lastRoute.nodePath.Count - 1; i++)
            {
                Node from = graphLoader.GetNode(_lastRoute.nodePath[i]);
                Node to = graphLoader.GetNode(_lastRoute.nodePath[i + 1]);
                if (from == null || to == null)
                {
                    continue;
                }

                Vector2 a = WorldToMiniMap(from.position, mapSize);
                Vector2 b = WorldToMiniMap(to.position, mapSize);
                DrawLine(a, b, routeColor, routeLineThickness);
            }
        }

        private void DrawDestinationMarker(Vector2 mapSize)
        {
            string destinationNodeId = _selectedDestinationNodeId;
            if (string.IsNullOrWhiteSpace(destinationNodeId) &&
                _lastRoute != null &&
                _lastRoute.success &&
                _lastRoute.nodePath != null &&
                _lastRoute.nodePath.Count > 0)
            {
                destinationNodeId = _lastRoute.nodePath[_lastRoute.nodePath.Count - 1];
            }

            if (string.IsNullOrWhiteSpace(destinationNodeId))
            {
                return;
            }

            Node destinationNode = graphLoader.GetNode(destinationNodeId);
            if (destinationNode == null)
            {
                return;
            }

            Vector2 p = WorldToMiniMap(destinationNode.position, mapSize);
            DrawCircle(p, markerRadius + 1f, destinationColor);
        }

        private void DrawPlayerMarker(Vector2 mapSize)
        {
            if (startReferenceTransform == null)
            {
                return;
            }

            Vector2 p = WorldToMiniMap(startReferenceTransform.position, mapSize);
            DrawCircle(p, markerRadius, playerColor);
        }

        private Vector2 WorldToMiniMap(Vector3 world, Vector2 mapSize)
        {
            Vector2 basePoint;
            if (useGeoAnchoredAlignment && TryWorldToTileNormalized(world, out Vector2 uv))
            {
                basePoint = new Vector2(
                    Mathf.Clamp01(uv.x) * mapSize.x,
                    Mathf.Clamp01(uv.y) * mapSize.y);
            }
            else
            {
                Vector2 adjustedWorld = ApplyWorldToMapAlignment(new Vector2(world.x, world.z));
                float nx = Mathf.InverseLerp(_minX, _maxX, adjustedWorld.x);
                float nz = Mathf.InverseLerp(_minZ, _maxZ, adjustedWorld.y);
                basePoint = new Vector2(nx * mapSize.x, (1f - nz) * mapSize.y);
            }

            return ApplyMapViewTransform(basePoint, mapSize);
        }

        private void RecomputeBounds()
        {
            if (graphLoader == null || graphLoader.NodesById == null || graphLoader.NodesById.Count == 0)
            {
                _boundsValid = false;
                return;
            }

            _minX = float.PositiveInfinity;
            _maxX = float.NegativeInfinity;
            _minZ = float.PositiveInfinity;
            _maxZ = float.NegativeInfinity;

            foreach (var kvp in graphLoader.NodesById)
            {
                Vector3 p = kvp.Value.position;
                if (p.x < _minX) _minX = p.x;
                if (p.x > _maxX) _maxX = p.x;
                if (p.z < _minZ) _minZ = p.z;
                if (p.z > _maxZ) _maxZ = p.z;
            }

            if (_maxX - _minX < 0.5f)
            {
                float centerX = (_maxX + _minX) * 0.5f;
                _minX = centerX - 0.5f;
                _maxX = centerX + 0.5f;
            }

            if (_maxZ - _minZ < 0.5f)
            {
                float centerZ = (_maxZ + _minZ) * 0.5f;
                _minZ = centerZ - 0.5f;
                _maxZ = centerZ + 0.5f;
            }

            float pad = Mathf.Max(0f, boundsPaddingMeters);
            _minX -= pad;
            _maxX += pad;
            _minZ -= pad;
            _maxZ += pad;
            _boundsValid = true;
        }

        private void EnsurePanelRect(float scale)
        {
            float virtualScreenWidth = Screen.width / scale;
            float virtualScreenHeight = Screen.height / scale;
            float width = Mathf.Clamp(panelSize.x, 160f, virtualScreenWidth - 8f);
            float height = Mathf.Clamp(panelSize.y, 140f, virtualScreenHeight - 8f);

            if (!_panelRectInitialized)
            {
                float x = panelStartPosition.x;
                if (x <= 0f)
                {
                    x = virtualScreenWidth - width - 12f;
                }

                _panelRect = new Rect(x, panelStartPosition.y, width, height);
                _panelRectInitialized = true;
            }
            else
            {
                _panelRect.width = width;
                _panelRect.height = height;
            }

            _panelRect.x = Mathf.Clamp(_panelRect.x, 0f, Mathf.Max(0f, virtualScreenWidth - _panelRect.width));
            _panelRect.y = Mathf.Clamp(_panelRect.y, 0f, Mathf.Max(0f, virtualScreenHeight - _panelRect.height));
        }

        private void DrawLine(Vector2 a, Vector2 b, Color color, float thickness)
        {
            if (_pixel == null)
            {
                return;
            }

            float len = Vector2.Distance(a, b);
            if (len < 0.001f)
            {
                return;
            }

            Matrix4x4 prev = GUI.matrix;
            Color prevColor = GUI.color;
            GUI.color = color;

            float angle = Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg;
            GUIUtility.RotateAroundPivot(angle, a);
            GUI.DrawTexture(new Rect(a.x, a.y - (thickness * 0.5f), len, thickness), _pixel);

            GUI.matrix = prev;
            GUI.color = prevColor;
        }

        private void DrawCircle(Vector2 center, float radius, Color color)
        {
            DrawFilledRect(new Rect(center.x - radius, center.y - radius, radius * 2f, radius * 2f), color);
        }

        private void DrawFilledRect(Rect rect, Color color)
        {
            if (_pixel == null)
            {
                return;
            }

            Color prev = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, _pixel);
            GUI.color = prev;
        }

        private void EnsurePixelTexture()
        {
            if (_pixel != null)
            {
                return;
            }

            _pixel = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point
            };
            _pixel.SetPixel(0, 0, Color.white);
            _pixel.Apply();
        }

        private void TryLoadMapTexture()
        {
            if (!showMapImage || _mapTexture != null || _isMapTextureLoading || string.IsNullOrWhiteSpace(mapImageFileName))
            {
                return;
            }

            StartCoroutine(LoadMapTextureRoutine());
        }

        private IEnumerator LoadMapTextureRoutine()
        {
            _isMapTextureLoading = true;

            string streamingPath = Path.Combine(Application.streamingAssetsPath, "Data", mapImageFileName);
            yield return LoadTextureFromPathRoutine(streamingPath);

            if (_mapTexture == null)
            {
                string persistentPath = Path.Combine(Application.persistentDataPath, "Data", mapImageFileName);
                yield return LoadTextureFromPathRoutine(persistentPath);
            }

            _isMapTextureLoading = false;
        }

        private IEnumerator LoadTextureFromPathRoutine(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                yield break;
            }

            using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(ToUnityWebRequestPath(path)))
            {
                yield return request.SendWebRequest();
                if (request.result == UnityWebRequest.Result.Success)
                {
                    _mapTexture = DownloadHandlerTexture.GetContent(request);
                }
            }
        }

        private static string ToUnityWebRequestPath(string path)
        {
            if (path.StartsWith("http://", System.StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("https://", System.StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("file://", System.StringComparison.OrdinalIgnoreCase) ||
                path.Contains("://"))
            {
                return path;
            }

            return "file://" + path;
        }

        private void EnsureGeoMapping()
        {
            if (!_geoMappingDirty)
            {
                return;
            }

            _geoMappingDirty = false;
            _geoMappingReady = false;
            _tileMetadataReady = TryParseTileMetadata(mapImageFileName, out _tileZoom, out _tileX, out _tileY);
            if (!useGeoAnchoredAlignment || !_tileMetadataReady || graphLoader == null || graphLoader.NodesById == null || locationManager == null)
            {
                return;
            }

            var worldSamples = new System.Collections.Generic.List<Vector2>();
            var geoSamples = new System.Collections.Generic.List<Vector2>();
            for (int i = 0; i < locationManager.Locations.Count; i++)
            {
                LocationPoint location = locationManager.Locations[i];
                if (location == null ||
                    string.IsNullOrWhiteSpace(location.indoor_node_id) ||
                    double.IsNaN(location.gps_lat) ||
                    double.IsNaN(location.gps_lon))
                {
                    continue;
                }

                if (Math.Abs(location.gps_lat) < 0.000001 && Math.Abs(location.gps_lon) < 0.000001)
                {
                    continue;
                }

                Node node = graphLoader.GetNode(location.indoor_node_id.Trim());
                if (node == null)
                {
                    continue;
                }

                worldSamples.Add(new Vector2(node.position.x, node.position.z));
                geoSamples.Add(new Vector2((float)location.gps_lat, (float)location.gps_lon));
            }

            if (worldSamples.Count < 3)
            {
                return;
            }

            if (!TryFitAffine(worldSamples, geoSamples, out _latBias, out _latXCoef, out _latZCoef,
                              out _lonBias, out _lonXCoef, out _lonZCoef))
            {
                return;
            }

            if (!TryFitAffine(geoSamples, worldSamples, out _xBias, out _xLatCoef, out _xLonCoef,
                              out _zBias, out _zLatCoef, out _zLonCoef))
            {
                return;
            }

            _geoMappingReady = true;
        }

        private bool TryWorldToTileNormalized(Vector3 world, out Vector2 tileUV)
        {
            tileUV = Vector2.zero;
            if (!_geoMappingReady || !_tileMetadataReady)
            {
                return false;
            }

            double lat = _latBias + (_latXCoef * world.x) + (_latZCoef * world.z);
            double lon = _lonBias + (_lonXCoef * world.x) + (_lonZCoef * world.z);
            if (!TryLatLonToTileNormalized(lat, lon, _tileZoom, _tileX, _tileY, out tileUV))
            {
                return false;
            }

            return true;
        }

        private bool TryTileNormalizedToWorld(Vector2 tileUV, out Vector3 world)
        {
            world = Vector3.zero;
            if (!_geoMappingReady || !_tileMetadataReady)
            {
                return false;
            }

            if (!TryTileNormalizedToLatLon(tileUV, _tileZoom, _tileX, _tileY, out double lat, out double lon))
            {
                return false;
            }

            double x = _xBias + (_xLatCoef * lat) + (_xLonCoef * lon);
            double z = _zBias + (_zLatCoef * lat) + (_zLonCoef * lon);
            float y = startReferenceTransform != null ? startReferenceTransform.position.y : 0f;
            world = new Vector3((float)x, y, (float)z);
            return true;
        }

        private static bool TryParseTileMetadata(string fileName, out int zoom, out int x, out int y)
        {
            zoom = 0;
            x = 0;
            y = 0;
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return false;
            }

            string name = Path.GetFileNameWithoutExtension(fileName);
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            int zIndex = name.IndexOf("_z", StringComparison.OrdinalIgnoreCase);
            int xIndex = name.IndexOf("_x", StringComparison.OrdinalIgnoreCase);
            int yIndex = name.IndexOf("_y", StringComparison.OrdinalIgnoreCase);
            if (zIndex < 0 || xIndex < 0 || yIndex < 0 || !(zIndex < xIndex && xIndex < yIndex))
            {
                return false;
            }

            string zStr = name.Substring(zIndex + 2, xIndex - (zIndex + 2));
            string xStr = name.Substring(xIndex + 2, yIndex - (xIndex + 2));
            string yStr = name.Substring(yIndex + 2);

            return int.TryParse(zStr, out zoom) &&
                   int.TryParse(xStr, out x) &&
                   int.TryParse(yStr, out y);
        }

        private static bool TryLatLonToTileNormalized(double lat, double lon, int zoom, int tileX, int tileY, out Vector2 uv)
        {
            uv = Vector2.zero;
            if (double.IsNaN(lat) || double.IsNaN(lon))
            {
                return false;
            }

            lat = Math.Max(-85.05112878, Math.Min(85.05112878, lat));
            lon = Math.Max(-180.0, Math.Min(180.0, lon));

            double n = Math.Pow(2.0, zoom);
            double pixelX = ((lon + 180.0) / 360.0) * (256.0 * n);
            double latRad = lat * Math.PI / 180.0;
            double mercatorY = Math.Log(Math.Tan(latRad) + (1.0 / Math.Cos(latRad)));
            double pixelY = (1.0 - (mercatorY / Math.PI)) * 0.5 * (256.0 * n);

            double localX = pixelX - (tileX * 256.0);
            double localY = pixelY - (tileY * 256.0);
            uv = new Vector2((float)(localX / 256.0), (float)(localY / 256.0));
            return true;
        }

        private static bool TryTileNormalizedToLatLon(Vector2 uv, int zoom, int tileX, int tileY, out double lat, out double lon)
        {
            lat = 0;
            lon = 0;

            double n = Math.Pow(2.0, zoom);
            double pixelX = (tileX * 256.0) + (Math.Max(0.0, Math.Min(1.0, uv.x)) * 256.0);
            double pixelY = (tileY * 256.0) + (Math.Max(0.0, Math.Min(1.0, uv.y)) * 256.0);

            lon = (pixelX / (256.0 * n)) * 360.0 - 180.0;
            double mercN = Math.PI - (2.0 * Math.PI * pixelY) / (256.0 * n);
            lat = (180.0 / Math.PI) * Math.Atan(Math.Sinh(mercN));
            return true;
        }

        private static bool TryFitAffine(
            System.Collections.Generic.List<Vector2> inputs,
            System.Collections.Generic.List<Vector2> outputs,
            out double out1Bias, out double out1X, out double out1Y,
            out double out2Bias, out double out2X, out double out2Y)
        {
            out1Bias = out1X = out1Y = 0.0;
            out2Bias = out2X = out2Y = 0.0;

            if (inputs == null || outputs == null || inputs.Count != outputs.Count || inputs.Count < 3)
            {
                return false;
            }

            ComputeNormalEquations(inputs, outputs, true, out double[,] a1, out double[] b1);
            ComputeNormalEquations(inputs, outputs, false, out double[,] a2, out double[] b2);
            if (!Solve3x3(a1, b1, out double[] c1) || !Solve3x3(a2, b2, out double[] c2))
            {
                return false;
            }

            out1Bias = c1[0];
            out1X = c1[1];
            out1Y = c1[2];
            out2Bias = c2[0];
            out2X = c2[1];
            out2Y = c2[2];
            return true;
        }

        private static void ComputeNormalEquations(
            System.Collections.Generic.List<Vector2> inputs,
            System.Collections.Generic.List<Vector2> outputs,
            bool useOutputX,
            out double[,] a,
            out double[] b)
        {
            a = new double[3, 3];
            b = new double[3];

            double s00 = 0.0;
            double s01 = 0.0;
            double s02 = 0.0;
            double s11 = 0.0;
            double s12 = 0.0;
            double s22 = 0.0;
            double t0 = 0.0;
            double t1 = 0.0;
            double t2 = 0.0;

            for (int i = 0; i < inputs.Count; i++)
            {
                double u = inputs[i].x;
                double v = inputs[i].y;
                double target = useOutputX ? outputs[i].x : outputs[i].y;

                s00 += 1.0;
                s01 += u;
                s02 += v;
                s11 += u * u;
                s12 += u * v;
                s22 += v * v;

                t0 += target;
                t1 += target * u;
                t2 += target * v;
            }

            a[0, 0] = s00; a[0, 1] = s01; a[0, 2] = s02;
            a[1, 0] = s01; a[1, 1] = s11; a[1, 2] = s12;
            a[2, 0] = s02; a[2, 1] = s12; a[2, 2] = s22;

            b[0] = t0;
            b[1] = t1;
            b[2] = t2;
        }

        private static bool Solve3x3(double[,] a, double[] b, out double[] x)
        {
            x = new double[3];
            if (a == null || b == null || a.GetLength(0) != 3 || a.GetLength(1) != 3 || b.Length != 3)
            {
                return false;
            }

            // Gaussian elimination with partial pivoting.
            double[,] m = (double[,])a.Clone();
            double[] v = (double[])b.Clone();

            for (int col = 0; col < 3; col++)
            {
                int pivot = col;
                double pivotAbs = Math.Abs(m[col, col]);
                for (int row = col + 1; row < 3; row++)
                {
                    double abs = Math.Abs(m[row, col]);
                    if (abs > pivotAbs)
                    {
                        pivotAbs = abs;
                        pivot = row;
                    }
                }

                if (pivotAbs < 1e-12)
                {
                    return false;
                }

                if (pivot != col)
                {
                    for (int c = col; c < 3; c++)
                    {
                        double tmp = m[col, c];
                        m[col, c] = m[pivot, c];
                        m[pivot, c] = tmp;
                    }
                    double tv = v[col];
                    v[col] = v[pivot];
                    v[pivot] = tv;
                }

                double div = m[col, col];
                for (int c = col; c < 3; c++)
                {
                    m[col, c] /= div;
                }
                v[col] /= div;

                for (int row = 0; row < 3; row++)
                {
                    if (row == col)
                    {
                        continue;
                    }

                    double factor = m[row, col];
                    if (Math.Abs(factor) < 1e-18)
                    {
                        continue;
                    }

                    for (int c = col; c < 3; c++)
                    {
                        m[row, c] -= factor * m[col, c];
                    }
                    v[row] -= factor * v[col];
                }
            }

            x[0] = v[0];
            x[1] = v[1];
            x[2] = v[2];
            return true;
        }

        private void DrawMapControlButtons(Rect mapRect)
        {
            if (!enableMapInteraction)
            {
                return;
            }

            float buttonSize = 20f;
            float padding = 6f;
            Rect plusRect = new Rect(mapRect.xMax - (buttonSize * 3f) - (padding * 2f), mapRect.y + padding, buttonSize, buttonSize);
            Rect minusRect = new Rect(mapRect.xMax - (buttonSize * 2f) - padding, mapRect.y + padding, buttonSize, buttonSize);
            Rect centerRect = new Rect(mapRect.xMax - buttonSize - 1f, mapRect.y + padding, buttonSize, buttonSize);

            if (GUI.Button(plusRect, "+"))
            {
                SetMapZoom(_mapZoom * (1f + zoomStep), mapRect.size, mapRect.size * 0.5f);
            }

            if (GUI.Button(minusRect, "-"))
            {
                SetMapZoom(_mapZoom / (1f + zoomStep), mapRect.size, mapRect.size * 0.5f);
            }

            if (GUI.Button(centerRect, "C"))
            {
                _mapPanPixels = Vector2.zero;
                SetMapZoom(1f, mapRect.size, mapRect.size * 0.5f);
            }
        }

        private void HandleMapInteraction(Rect mapRect)
        {
            if (!enableMapInteraction)
            {
                return;
            }

            Event e = Event.current;
            if (e == null)
            {
                return;
            }

            Vector2 mouse = e.mousePosition;
            bool mouseInsideMap = mapRect.Contains(mouse);
            bool mouseOnControls = IsPointOnMapControls(mapRect, mouse);

            if (mouseInsideMap && !mouseOnControls && e.type == EventType.ScrollWheel)
            {
                float direction = -Mathf.Sign(e.delta.y);
                float targetZoom = direction > 0f
                    ? _mapZoom * (1f + zoomStep)
                    : _mapZoom / (1f + zoomStep);

                Vector2 pivotLocal = mouse - mapRect.position;
                SetMapZoom(targetZoom, mapRect.size, pivotLocal);
                e.Use();
                return;
            }

            if (e.type == EventType.MouseDown && e.button == 0 && mouseInsideMap && !mouseOnControls)
            {
                _isPanningMap = true;
                _lastPanMousePosition = mouse;
                e.Use();
                return;
            }

            if (e.type == EventType.MouseDrag && _isPanningMap)
            {
                Vector2 delta = mouse - _lastPanMousePosition;
                _lastPanMousePosition = mouse;
                _mapPanPixels += delta;
                ClampMapPan(mapRect.size);
                e.Use();
                return;
            }

            if (e.type == EventType.MouseUp && _isPanningMap)
            {
                _isPanningMap = false;
                e.Use();
            }
        }

        private bool IsPointOnMapControls(Rect mapRect, Vector2 point)
        {
            float buttonSize = 20f;
            float padding = 6f;
            Rect plusRect = new Rect(mapRect.xMax - (buttonSize * 3f) - (padding * 2f), mapRect.y + padding, buttonSize, buttonSize);
            Rect minusRect = new Rect(mapRect.xMax - (buttonSize * 2f) - padding, mapRect.y + padding, buttonSize, buttonSize);
            Rect centerRect = new Rect(mapRect.xMax - buttonSize - 1f, mapRect.y + padding, buttonSize, buttonSize);
            return plusRect.Contains(point) || minusRect.Contains(point) || centerRect.Contains(point);
        }

        private void SetMapZoom(float targetZoom, Vector2 mapSize, Vector2 pivotLocal)
        {
            float clampedMin = Mathf.Max(1f, minMapZoom);
            float clampedMax = Mathf.Max(clampedMin, maxMapZoom);
            float newZoom = Mathf.Clamp(targetZoom, clampedMin, clampedMax);
            if (Mathf.Approximately(newZoom, _mapZoom))
            {
                return;
            }

            Vector2 center = mapSize * 0.5f;
            Vector2 before = pivotLocal - center - _mapPanPixels;
            Vector2 scaledBefore = before * (newZoom / _mapZoom);
            _mapPanPixels = pivotLocal - center - scaledBefore;
            _mapZoom = newZoom;
            ClampMapPan(mapSize);
        }

        private void ClampMapPan(Vector2 mapSize)
        {
            if (_mapZoom <= 1.0001f)
            {
                _mapZoom = 1f;
                _mapPanPixels = Vector2.zero;
                return;
            }

            float maxPanX = Mathf.Max(0f, (mapSize.x * _mapZoom - mapSize.x) * 0.5f);
            float maxPanY = Mathf.Max(0f, (mapSize.y * _mapZoom - mapSize.y) * 0.5f);
            _mapPanPixels.x = Mathf.Clamp(_mapPanPixels.x, -maxPanX, maxPanX);
            _mapPanPixels.y = Mathf.Clamp(_mapPanPixels.y, -maxPanY, maxPanY);
        }

        private Rect GetMapContentRect(Vector2 mapSize)
        {
            Vector2 center = mapSize * 0.5f;
            float width = mapSize.x * _mapZoom;
            float height = mapSize.y * _mapZoom;
            float x = center.x - (width * 0.5f) + _mapPanPixels.x;
            float y = center.y - (height * 0.5f) + _mapPanPixels.y;
            return new Rect(x, y, width, height);
        }

        private Vector2 ApplyMapViewTransform(Vector2 basePoint, Vector2 mapSize)
        {
            Vector2 center = mapSize * 0.5f;
            return center + ((basePoint - center) * _mapZoom) + _mapPanPixels;
        }

        private Vector2 InverseMapViewTransform(Vector2 mappedPoint, Vector2 mapSize)
        {
            Vector2 center = mapSize * 0.5f;
            float zoom = Mathf.Max(0.0001f, _mapZoom);
            return center + ((mappedPoint - center - _mapPanPixels) / zoom);
        }

        private Vector2 ApplyWorldToMapAlignment(Vector2 worldXZ)
        {
            if (!enableManualAlignment)
            {
                return worldXZ;
            }

            Vector2 center = new Vector2((_minX + _maxX) * 0.5f, (_minZ + _maxZ) * 0.5f);
            Vector2 delta = worldXZ - center;
            delta = Rotate2D(delta, worldToMapRotationDegrees);
            Vector2 scale = new Vector2(
                Mathf.Max(0.01f, worldToMapScale.x),
                Mathf.Max(0.01f, worldToMapScale.y));
            delta = new Vector2(delta.x * scale.x, delta.y * scale.y);
            delta += worldToMapOffsetMeters;
            return center + delta;
        }

        private Vector2 InverseWorldToMapAlignment(Vector2 alignedXZ)
        {
            if (!enableManualAlignment)
            {
                return alignedXZ;
            }

            Vector2 center = new Vector2((_minX + _maxX) * 0.5f, (_minZ + _maxZ) * 0.5f);
            Vector2 delta = alignedXZ - center;
            delta -= worldToMapOffsetMeters;
            Vector2 scale = new Vector2(
                Mathf.Max(0.01f, worldToMapScale.x),
                Mathf.Max(0.01f, worldToMapScale.y));
            delta = new Vector2(delta.x / scale.x, delta.y / scale.y);
            delta = Rotate2D(delta, -worldToMapRotationDegrees);
            return center + delta;
        }

        private static Vector2 Rotate2D(Vector2 vector, float degrees)
        {
            if (Mathf.Abs(degrees) < 0.0001f)
            {
                return vector;
            }

            float radians = degrees * Mathf.Deg2Rad;
            float cos = Mathf.Cos(radians);
            float sin = Mathf.Sin(radians);
            return new Vector2(
                (vector.x * cos) - (vector.y * sin),
                (vector.x * sin) + (vector.y * cos));
        }

        private Vector3 MiniMapPointToWorld(Vector2 miniMapPoint, Vector2 mapSize)
        {
            Vector2 basePoint = InverseMapViewTransform(miniMapPoint, mapSize);
            if (useGeoAnchoredAlignment)
            {
                float ux = Mathf.Clamp01(mapSize.x > 0.0001f ? basePoint.x / mapSize.x : 0f);
                float uy = Mathf.Clamp01(mapSize.y > 0.0001f ? basePoint.y / mapSize.y : 0f);
                if (TryTileNormalizedToWorld(new Vector2(ux, uy), out Vector3 worldFromGeo))
                {
                    return worldFromGeo;
                }
            }

            float nx = Mathf.Clamp01(mapSize.x > 0.0001f ? basePoint.x / mapSize.x : 0f);
            float nz = Mathf.Clamp01(1f - (mapSize.y > 0.0001f ? basePoint.y / mapSize.y : 0f));

            Vector2 alignedXZ = new Vector2(
                Mathf.Lerp(_minX, _maxX, nx),
                Mathf.Lerp(_minZ, _maxZ, nz));
            Vector2 worldXZ = InverseWorldToMapAlignment(alignedXZ);

            float y = startReferenceTransform != null ? startReferenceTransform.position.y : 0f;
            return new Vector3(worldXZ.x, y, worldXZ.y);
        }

        private void DrawMiniMapHoverTooltip(Rect mapRect)
        {
            if (!showHoverDetails || graphLoader == null || graphLoader.NodesById == null || graphLoader.NodesById.Count == 0)
            {
                return;
            }

            Event e = Event.current;
            if (e == null)
            {
                return;
            }

            Vector2 mouse = e.mousePosition;
            if (!mapRect.Contains(mouse))
            {
                return;
            }

            Vector2 mapLocalPoint = mouse - mapRect.position;
            Vector3 worldEstimate = MiniMapPointToWorld(mapLocalPoint, mapRect.size);
            if (!TryGetNearestNode(worldEstimate, out Node nearestNode, out float distanceMeters))
            {
                return;
            }

            if (distanceMeters > miniMapHoverSearchRadiusMeters)
            {
                return;
            }

            Vector2 tooltipPos = mouse + hoverTooltipOffset;
            DrawTooltip(tooltipPos, BuildNodeTooltipText(nearestNode, "MiniMap", distanceMeters));
        }

        private void DrawScreenHoverTooltip(float scale)
        {
            if (!showHoverDetails || graphLoader == null || graphLoader.NodesById == null || graphLoader.NodesById.Count == 0 || Camera.main == null)
            {
                return;
            }

            Event e = Event.current;
            if (e == null)
            {
                return;
            }

            Vector2 mouse = e.mousePosition;
            Rect miniMapScreenRect = new Rect(_panelRect.x * scale, _panelRect.y * scale, _panelRect.width * scale, _panelRect.height * scale);
            if (miniMapScreenRect.Contains(mouse))
            {
                return;
            }

            if (!TryGetNearestNodeByScreen(mouse, out Node nearestNode, out float screenDistance))
            {
                return;
            }

            if (screenDistance > Mathf.Max(1f, screenHoverPixelRadius))
            {
                return;
            }

            Vector2 tooltipPos = mouse + hoverTooltipOffset;
            DrawTooltip(tooltipPos, BuildNodeTooltipText(nearestNode, "Screen", null));
        }

        private bool TryGetNearestNodeByScreen(Vector2 mouseGuiPos, out Node nearestNode, out float distancePixels)
        {
            nearestNode = null;
            distancePixels = float.PositiveInfinity;

            foreach (var kvp in graphLoader.NodesById)
            {
                Node node = kvp.Value;
                Vector3 screen = Camera.main.WorldToScreenPoint(node.position);
                if (screen.z <= 0f)
                {
                    continue;
                }

                Vector2 guiPoint = new Vector2(screen.x, Screen.height - screen.y);
                float d = Vector2.Distance(mouseGuiPos, guiPoint);
                if (d < distancePixels)
                {
                    distancePixels = d;
                    nearestNode = node;
                }
            }

            return nearestNode != null;
        }

        private bool TryGetNearestNode(Vector3 worldPosition, out Node nearestNode, out float distanceMeters)
        {
            nearestNode = null;
            distanceMeters = float.PositiveInfinity;
            if (graphLoader == null || graphLoader.NodesById == null)
            {
                return false;
            }

            Vector2 worldXZ = new Vector2(worldPosition.x, worldPosition.z);
            foreach (var kvp in graphLoader.NodesById)
            {
                Node node = kvp.Value;
                Vector2 nodeXZ = new Vector2(node.position.x, node.position.z);
                float d = Vector2.Distance(worldXZ, nodeXZ);
                if (d < distanceMeters)
                {
                    distanceMeters = d;
                    nearestNode = node;
                }
            }

            return nearestNode != null;
        }

        private string BuildLocationLabel(string nodeId)
        {
            if (locationManager == null || locationManager.Locations == null || string.IsNullOrWhiteSpace(nodeId))
            {
                return "Location: -";
            }

            string locationName = null;
            for (int i = 0; i < locationManager.Locations.Count; i++)
            {
                LocationPoint location = locationManager.Locations[i];
                if (location == null || string.IsNullOrWhiteSpace(location.indoor_node_id))
                {
                    continue;
                }

                if (string.Equals(location.indoor_node_id.Trim(), nodeId, System.StringComparison.OrdinalIgnoreCase))
                {
                    locationName = location.name;
                    break;
                }
            }

            return string.IsNullOrWhiteSpace(locationName) ? "Location: -" : $"Location: {locationName}";
        }

        private string BuildNodeTooltipText(Node node, string sourceLabel, float? distanceMeters)
        {
            if (node == null)
            {
                return string.IsNullOrWhiteSpace(sourceLabel) ? "Node: -" : $"{sourceLabel}: -";
            }

            string tooltip =
                $"{sourceLabel}: {node.id}\n" +
                $"{BuildLocationLabel(node.id)}\n" +
                $"Level: {node.elevationLevel}\n" +
                $"World XZ: {node.position.x:0.0}, {node.position.z:0.0}";

            if (TryGetNodeLatLon(node, out double lat, out double lon))
            {
                tooltip += $"\nGPS: {lat:0.000000}, {lon:0.000000}";
            }
            else
            {
                tooltip += "\nGPS: -";
            }

            if (distanceMeters.HasValue)
            {
                tooltip += $"\nDist: {distanceMeters.Value:0.0} m";
            }

            return tooltip;
        }

        private bool TryGetNodeLatLon(Node node, out double lat, out double lon)
        {
            lat = 0.0;
            lon = 0.0;
            if (node == null)
            {
                return false;
            }

            if (locationManager != null && locationManager.Locations != null)
            {
                for (int i = 0; i < locationManager.Locations.Count; i++)
                {
                    LocationPoint location = locationManager.Locations[i];
                    if (location == null || string.IsNullOrWhiteSpace(location.indoor_node_id))
                    {
                        continue;
                    }

                    if (!string.Equals(location.indoor_node_id.Trim(), node.id, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (double.IsNaN(location.gps_lat) ||
                        double.IsNaN(location.gps_lon) ||
                        (Math.Abs(location.gps_lat) < 0.000001 && Math.Abs(location.gps_lon) < 0.000001))
                    {
                        break;
                    }

                    lat = location.gps_lat;
                    lon = location.gps_lon;
                    return true;
                }
            }

            if (!_geoMappingReady)
            {
                return false;
            }

            lat = _latBias + (_latXCoef * node.position.x) + (_latZCoef * node.position.z);
            lon = _lonBias + (_lonXCoef * node.position.x) + (_lonZCoef * node.position.z);
            return !(double.IsNaN(lat) || double.IsNaN(lon));
        }

        private void DrawTooltip(Vector2 position, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            GUIStyle style = new GUIStyle(GUI.skin.box)
            {
                fontSize = 12,
                alignment = TextAnchor.UpperLeft,
                wordWrap = true,
                padding = new RectOffset(6, 6, 4, 4)
            };
            style.normal.textColor = Color.white;

            Vector2 size = style.CalcSize(new GUIContent(text));
            float width = Mathf.Clamp(size.x + 10f, 140f, 360f);
            float height = Mathf.Clamp(style.CalcHeight(new GUIContent(text), width - 8f) + 8f, 36f, 220f);

            float x = Mathf.Min(position.x, Screen.width - width - 8f);
            float y = Mathf.Min(position.y, Screen.height - height - 8f);
            GUI.Box(new Rect(x, y, width, height), text, style);
        }
    }
}
