using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using CDE2501.Wayfinding.IndoorGraph;
using UnityEngine;
using UnityEngine.Networking;

namespace CDE2501.Wayfinding.Data
{
    public class BoundaryConstraintManager : MonoBehaviour
    {
        [SerializeField] private GraphLoader graphLoader;
        [SerializeField] private LocationManager locationManager;
        [SerializeField] private string boundaryGeoJsonFileName = "queenstown_boundary.geojson";
        [SerializeField] private bool includeNodesWithoutGeoData = true;
        [SerializeField] private bool autoLoadOnStart = true;

        private readonly List<Vector2> _polygonLonLat = new List<Vector2>();
        private readonly Dictionary<string, Vector2> _nodeLonLat = new Dictionary<string, Vector2>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, bool> _nodeInsideCache = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private Coroutine _loadRoutine;
        private bool _affineReady;
        private int _boundaryRevision;

        private double _latBias;
        private double _latXCoef;
        private double _latZCoef;
        private double _lonBias;
        private double _lonXCoef;
        private double _lonZCoef;

        public event Action OnBoundaryUpdated;

        public bool HasBoundary => _polygonLonLat.Count >= 3;
        public int BoundaryRevision => _boundaryRevision;
        public string BoundaryGeoJsonFileName => boundaryGeoJsonFileName;

        private void Awake()
        {
            if (graphLoader == null)
            {
                graphLoader = FindObjectOfType<GraphLoader>();
            }

            if (locationManager == null)
            {
                locationManager = FindObjectOfType<LocationManager>();
            }
        }

        private void OnEnable()
        {
            SubscribeGraph();
            SubscribeLocations();
        }

        private void Start()
        {
            if (autoLoadOnStart)
            {
                LoadBoundary();
            }
        }

        private void OnDisable()
        {
            UnsubscribeGraph();
            UnsubscribeLocations();

            if (_loadRoutine != null)
            {
                StopCoroutine(_loadRoutine);
                _loadRoutine = null;
            }
        }

        public void SetGraphLoader(GraphLoader loader)
        {
            if (ReferenceEquals(graphLoader, loader))
            {
                return;
            }

            UnsubscribeGraph();
            graphLoader = loader;
            SubscribeGraph();
            ResolveBoundaryFileFromGraphMetadata();
            RebuildNodeCaches();
        }

        public void SetLocationManager(LocationManager manager)
        {
            if (ReferenceEquals(locationManager, manager))
            {
                return;
            }

            UnsubscribeLocations();
            locationManager = manager;
            SubscribeLocations();
            RebuildNodeCaches();
        }

        public void LoadBoundary()
        {
            if (_loadRoutine != null)
            {
                StopCoroutine(_loadRoutine);
            }

            _loadRoutine = StartCoroutine(LoadBoundaryRoutine());
        }

        public bool IsNodeAllowed(string nodeId)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                return false;
            }

            if (!HasBoundary)
            {
                return true;
            }

            string key = nodeId.Trim();
            if (_nodeInsideCache.TryGetValue(key, out bool inside))
            {
                return inside;
            }

            return includeNodesWithoutGeoData;
        }

        public bool TryGetNodeLatLon(string nodeId, out double lat, out double lon)
        {
            lat = 0.0;
            lon = 0.0;
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                return false;
            }

            string key = nodeId.Trim();
            if (!_nodeLonLat.TryGetValue(key, out Vector2 lonLat))
            {
                return false;
            }

            lat = lonLat.y;
            lon = lonLat.x;
            return true;
        }

        public bool IsLatLonInsideBoundary(double lat, double lon)
        {
            if (!HasBoundary)
            {
                return true;
            }

            if (double.IsNaN(lat) || double.IsNaN(lon))
            {
                return false;
            }

            return IsLonLatInsidePolygon(new Vector2((float)lon, (float)lat));
        }

        public bool IsWorldPositionInsideBoundary(Vector3 worldPosition)
        {
            if (!HasBoundary || !_affineReady)
            {
                return true;
            }

            double lat = _latBias + (_latXCoef * worldPosition.x) + (_latZCoef * worldPosition.z);
            double lon = _lonBias + (_lonXCoef * worldPosition.x) + (_lonZCoef * worldPosition.z);
            if (double.IsNaN(lat) || double.IsNaN(lon))
            {
                return true;
            }

            return IsLatLonInsideBoundary(lat, lon);
        }

        private void SubscribeGraph()
        {
            if (graphLoader != null)
            {
                graphLoader.OnGraphLoaded += HandleGraphLoaded;
            }
        }

        private void UnsubscribeGraph()
        {
            if (graphLoader != null)
            {
                graphLoader.OnGraphLoaded -= HandleGraphLoaded;
            }
        }

        private void SubscribeLocations()
        {
            if (locationManager != null)
            {
                locationManager.OnLocationsChanged += HandleLocationsChanged;
            }
        }

        private void UnsubscribeLocations()
        {
            if (locationManager != null)
            {
                locationManager.OnLocationsChanged -= HandleLocationsChanged;
            }
        }

        private void HandleGraphLoaded(bool success, string message)
        {
            if (!success)
            {
                return;
            }

            ResolveBoundaryFileFromGraphMetadata();
            RebuildNodeCaches();
        }

        private void HandleLocationsChanged()
        {
            RebuildNodeCaches();
        }

        private void ResolveBoundaryFileFromGraphMetadata()
        {
            if (graphLoader?.GraphData?.metadata?.areaBounds == null)
            {
                return;
            }

            string fromGraph = graphLoader.GraphData.metadata.areaBounds.boundaryGeoJson;
            if (string.IsNullOrWhiteSpace(fromGraph))
            {
                return;
            }

            string trimmed = fromGraph.Trim();
            if (string.Equals(trimmed, boundaryGeoJsonFileName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            boundaryGeoJsonFileName = trimmed;
            LoadBoundary();
        }

        private IEnumerator LoadBoundaryRoutine()
        {
            string persistentPath = Path.Combine(Application.persistentDataPath, "Data", boundaryGeoJsonFileName);
            string streamingPath = Path.Combine(Application.streamingAssetsPath, "Data", boundaryGeoJsonFileName);

            if (!File.Exists(persistentPath))
            {
                yield return CopyFromStreamingAssets(streamingPath, persistentPath);
            }

            string raw = null;
            if (File.Exists(persistentPath))
            {
                raw = File.ReadAllText(persistentPath);
            }
            else
            {
                yield return ReadTextFromPath(streamingPath, text => raw = text);
            }

            if (string.IsNullOrWhiteSpace(raw))
            {
                _polygonLonLat.Clear();
                _boundaryRevision++;
                RebuildNodeCaches();
                OnBoundaryUpdated?.Invoke();
                _loadRoutine = null;
                yield break;
            }

            if (!TryParsePolygonCoordinates(raw, out List<Vector2> polygon) || polygon.Count < 3)
            {
                _polygonLonLat.Clear();
                _boundaryRevision++;
                RebuildNodeCaches();
                OnBoundaryUpdated?.Invoke();
                _loadRoutine = null;
                yield break;
            }

            _polygonLonLat.Clear();
            _polygonLonLat.AddRange(polygon);
            _boundaryRevision++;

            RebuildNodeCaches();
            OnBoundaryUpdated?.Invoke();
            _loadRoutine = null;
        }

        private void RebuildNodeCaches()
        {
            _nodeLonLat.Clear();
            _nodeInsideCache.Clear();
            _affineReady = false;

            if (graphLoader == null || graphLoader.NodesById == null || graphLoader.NodesById.Count == 0)
            {
                return;
            }

            var worldSamples = new List<Vector2>();
            var geoSamples = new List<Vector2>();

            if (locationManager != null && locationManager.Locations != null)
            {
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

                    string nodeId = location.indoor_node_id.Trim();
                    Node node = graphLoader.GetNode(nodeId);
                    if (node == null)
                    {
                        continue;
                    }

                    Vector2 lonLat = new Vector2((float)location.gps_lon, (float)location.gps_lat);
                    _nodeLonLat[nodeId] = lonLat;
                    worldSamples.Add(new Vector2(node.position.x, node.position.z));
                    geoSamples.Add(new Vector2((float)location.gps_lat, (float)location.gps_lon));
                }
            }

            _affineReady = TryFitAffine(
                worldSamples,
                geoSamples,
                out _latBias,
                out _latXCoef,
                out _latZCoef,
                out _lonBias,
                out _lonXCoef,
                out _lonZCoef);

            foreach (var kvp in graphLoader.NodesById)
            {
                string nodeId = kvp.Key;
                Node node = kvp.Value;
                bool hasLonLat = _nodeLonLat.TryGetValue(nodeId, out Vector2 lonLat);

                if (!hasLonLat && _affineReady)
                {
                    double lat = _latBias + (_latXCoef * node.position.x) + (_latZCoef * node.position.z);
                    double lon = _lonBias + (_lonXCoef * node.position.x) + (_lonZCoef * node.position.z);
                    if (!double.IsNaN(lat) && !double.IsNaN(lon))
                    {
                        lonLat = new Vector2((float)lon, (float)lat);
                        _nodeLonLat[nodeId] = lonLat;
                        hasLonLat = true;
                    }
                }

                bool inside = !HasBoundary || (hasLonLat ? IsLonLatInsidePolygon(lonLat) : includeNodesWithoutGeoData);
                _nodeInsideCache[nodeId] = inside;
            }
        }

        private bool IsLonLatInsidePolygon(Vector2 lonLat)
        {
            int count = _polygonLonLat.Count;
            if (count < 3)
            {
                return true;
            }

            bool inside = false;
            float x = lonLat.x;
            float y = lonLat.y;
            int j = count - 1;

            for (int i = 0; i < count; i++)
            {
                Vector2 a = _polygonLonLat[i];
                Vector2 b = _polygonLonLat[j];

                bool intersects = ((a.y > y) != (b.y > y)) &&
                                  (x < ((b.x - a.x) * (y - a.y) / ((b.y - a.y) + 1e-12f)) + a.x);
                if (intersects)
                {
                    inside = !inside;
                }

                j = i;
            }

            return inside;
        }

        private static bool TryParsePolygonCoordinates(string raw, out List<Vector2> polygon)
        {
            polygon = new List<Vector2>();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            MatchCollection matches = Regex.Matches(
                raw,
                @"\[\s*(-?(?:\d+\.?\d*|\d*\.?\d+))\s*,\s*(-?(?:\d+\.?\d*|\d*\.?\d+))\s*\]");

            for (int i = 0; i < matches.Count; i++)
            {
                Match match = matches[i];
                if (!double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double lon) ||
                    !double.TryParse(match.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double lat))
                {
                    continue;
                }

                if (lat < -90.0 || lat > 90.0 || lon < -180.0 || lon > 180.0)
                {
                    continue;
                }

                polygon.Add(new Vector2((float)lon, (float)lat));
            }

            return polygon.Count >= 3;
        }

        private static bool TryFitAffine(
            List<Vector2> inputs,
            List<Vector2> outputs,
            out double out1Bias,
            out double out1X,
            out double out1Y,
            out double out2Bias,
            out double out2X,
            out double out2Y)
        {
            out1Bias = out1X = out1Y = 0.0;
            out2Bias = out2X = out2Y = 0.0;

            if (inputs == null || outputs == null || inputs.Count != outputs.Count || inputs.Count < 3)
            {
                return false;
            }

            ComputeNormalEquations(inputs, outputs, useOutputX: true, out double[,] a1, out double[] b1);
            ComputeNormalEquations(inputs, outputs, useOutputX: false, out double[,] a2, out double[] b2);
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
            List<Vector2> inputs,
            List<Vector2> outputs,
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

        private static IEnumerator CopyFromStreamingAssets(string sourcePath, string destinationPath)
        {
            string folder = Path.GetDirectoryName(destinationPath);
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            using (UnityWebRequest request = UnityWebRequest.Get(ToUnityWebRequestPath(sourcePath)))
            {
                yield return request.SendWebRequest();
                if (request.result == UnityWebRequest.Result.Success)
                {
                    File.WriteAllText(destinationPath, request.downloadHandler.text);
                }
            }
        }

        private static IEnumerator ReadTextFromPath(string sourcePath, Action<string> onLoaded)
        {
            string text = null;
            using (UnityWebRequest request = UnityWebRequest.Get(ToUnityWebRequestPath(sourcePath)))
            {
                yield return request.SendWebRequest();
                if (request.result == UnityWebRequest.Result.Success)
                {
                    text = request.downloadHandler.text;
                }
            }

            onLoaded?.Invoke(text);
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
    }
}
