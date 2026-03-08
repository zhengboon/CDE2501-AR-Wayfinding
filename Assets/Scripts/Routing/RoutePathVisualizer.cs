using CDE2501.Wayfinding.IndoorGraph;
using System.Collections.Generic;
using UnityEngine;

namespace CDE2501.Wayfinding.Routing
{
    [RequireComponent(typeof(LineRenderer))]
    public class RoutePathVisualizer : MonoBehaviour
    {
        [SerializeField] private RouteCalculator routeCalculator;
        [SerializeField] private GraphLoader graphLoader;
        [SerializeField] private Transform startReferenceTransform;
        [SerializeField] private bool includeStartReferenceSegment = true;
        [SerializeField] private bool followMainCameraIfReferenceMissing = true;
        [SerializeField] private bool renderAtUserFeet = true;
        [SerializeField] private float feetYOffset = -1.6f;
        [SerializeField] private float feetClearance = 0.05f;
        [SerializeField] private float yOffset = 0.12f;
        [Header("Baritone-Style Rendering")]
        [SerializeField] private float currentLineWidth = 0.24f;
        [SerializeField] private float futureLineWidth = 0.18f;
        [SerializeField] private float connectorLineWidth = 0.13f;
        [SerializeField] private Color currentPathColor = Color.red;
        [SerializeField] private Color futurePathColor = Color.magenta;
        [SerializeField] private Color connectorColor = Color.white;
        [SerializeField] private bool fadeFuturePath = true;
        [SerializeField, Min(1)] private int lookBackNodes = 3;
        [SerializeField, Min(1)] private int lookAheadNodes = 32;
        [SerializeField, Min(1)] private int futureFadeStartNodes = 14;
        [SerializeField, Min(2)] private int futureFadeEndNodes = 44;
        [SerializeField, Range(0.9f, 0.9999f)] private float collinearDotThreshold = 0.997f;
        [SerializeField, Min(0f)] private float minConnectorLengthMeters = 0.2f;

        private LineRenderer _currentLine;
        private LineRenderer _futureLine;
        private LineRenderer _connectorLine;
        private Material _sharedMaterial;
        private RouteResult _lastRoute;
        private readonly List<Vector3> _rawPathPoints = new List<Vector3>(256);
        private readonly List<Vector3> _compressedPathPoints = new List<Vector3>(256);
        private readonly List<Vector3> _currentSegmentPoints = new List<Vector3>(128);
        private readonly List<Vector3> _futureSegmentPoints = new List<Vector3>(256);
        private readonly Vector3[] _connectorBuffer = new Vector3[2];

        private void Awake()
        {
            _currentLine = GetComponent<LineRenderer>();
            Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
            _sharedMaterial = shader != null ? new Material(shader) : null;

            ConfigureLineRenderer(_currentLine, "CurrentPathLine", currentLineWidth, currentPathColor);
            _futureLine = CreateChildLine("FuturePathLine", futureLineWidth, futurePathColor);
            _connectorLine = CreateChildLine("ConnectorPathLine", connectorLineWidth, connectorColor);
            ApplyLineFade(_futureLine, futurePathColor, enableFade: fadeFuturePath, pointCount: 0);

            if (routeCalculator == null)
            {
                routeCalculator = FindObjectOfType<RouteCalculator>();
            }

            if (graphLoader == null)
            {
                graphLoader = FindObjectOfType<GraphLoader>();
            }
        }

        private void OnEnable()
        {
            if (routeCalculator != null)
            {
                routeCalculator.OnRouteUpdated += HandleRouteUpdated;
            }
        }

        private void Update()
        {
            if (followMainCameraIfReferenceMissing && startReferenceTransform == null && Camera.main != null)
            {
                startReferenceTransform = Camera.main.transform;
            }

            if (_lastRoute != null && _lastRoute.success)
            {
                DrawRoute(_lastRoute);
            }
        }

        private void OnDisable()
        {
            if (routeCalculator != null)
            {
                routeCalculator.OnRouteUpdated -= HandleRouteUpdated;
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

        public void SetGraphLoader(GraphLoader loader)
        {
            graphLoader = loader;
        }

        public void SetStartReferenceTransform(Transform startTransform)
        {
            startReferenceTransform = startTransform;
        }

        public void SetIncludeStartReferenceSegment(bool include)
        {
            includeStartReferenceSegment = include;
        }

        private void HandleRouteUpdated(RouteResult result)
        {
            if (result == null || graphLoader == null)
            {
                _lastRoute = null;
                ClearAllLines();
                return;
            }

            if (!result.success || result.nodePath == null || result.nodePath.Count < 1)
            {
                _lastRoute = null;
                ClearAllLines();
                return;
            }

            _lastRoute = result;
            DrawRoute(result);
        }

        private void DrawRoute(RouteResult result)
        {
            if (graphLoader == null || result == null || result.nodePath == null || result.nodePath.Count == 0)
            {
                ClearAllLines();
                return;
            }

            float routeY = ResolveRouteY();
            BuildRawPathPoints(result.nodePath, routeY, _rawPathPoints);
            CompressCollinearPoints(_rawPathPoints, _compressedPathPoints);
            if (_compressedPathPoints.Count < 2)
            {
                ClearAllLines();
                return;
            }

            int currentIndex = FindNearestPathIndex(_compressedPathPoints, startReferenceTransform);
            int segmentStart = Mathf.Clamp(currentIndex - Mathf.Max(0, lookBackNodes), 0, Mathf.Max(0, _compressedPathPoints.Count - 1));
            int segmentEnd = Mathf.Clamp(currentIndex + Mathf.Max(1, lookAheadNodes), 0, Mathf.Max(0, _compressedPathPoints.Count - 1));

            FillSegment(_compressedPathPoints, segmentStart, segmentEnd, _currentSegmentPoints);
            FillSegment(_compressedPathPoints, segmentEnd, _compressedPathPoints.Count - 1, _futureSegmentPoints);

            ApplySegmentToLine(_currentLine, _currentSegmentPoints, currentLineWidth, currentPathColor, enableFade: false);
            ApplySegmentToLine(_futureLine, _futureSegmentPoints, futureLineWidth, futurePathColor, fadeFuturePath);
            DrawConnector(routeY, currentIndex);
        }

        private void DrawConnector(float routeY, int nearestPathIndex)
        {
            if (!includeStartReferenceSegment || startReferenceTransform == null || _compressedPathPoints.Count == 0)
            {
                _connectorLine.positionCount = 0;
                return;
            }

            int clamped = Mathf.Clamp(nearestPathIndex, 0, _compressedPathPoints.Count - 1);
            Vector3 startPos = startReferenceTransform.position;
            startPos.y = routeY;
            Vector3 targetPos = _compressedPathPoints[clamped];
            targetPos.y = routeY;

            if (HorizontalDistance(startPos, targetPos) < minConnectorLengthMeters)
            {
                _connectorLine.positionCount = 0;
                return;
            }

            _connectorBuffer[0] = startPos;
            _connectorBuffer[1] = targetPos;
            _connectorLine.positionCount = 2;
            _connectorLine.SetPositions(_connectorBuffer);
            ApplyLineFade(_connectorLine, connectorColor, enableFade: false, pointCount: 2);
        }

        private void BuildRawPathPoints(IList<string> nodePath, float routeYValue, List<Vector3> output)
        {
            output.Clear();
            for (int i = 0; i < nodePath.Count; i++)
            {
                Node node = graphLoader.GetNode(nodePath[i]);
                if (node == null)
                {
                    continue;
                }

                Vector3 p = node.position;
                p.y = routeYValue;
                output.Add(p);
            }
        }

        private void CompressCollinearPoints(List<Vector3> input, List<Vector3> output)
        {
            output.Clear();
            if (input.Count == 0)
            {
                return;
            }

            if (input.Count == 1)
            {
                output.Add(input[0]);
                return;
            }

            output.Add(input[0]);
            for (int i = 1; i < input.Count - 1; i++)
            {
                Vector3 prev = output[output.Count - 1];
                Vector3 current = input[i];
                Vector3 next = input[i + 1];
                if (ShouldKeepPoint(prev, current, next))
                {
                    output.Add(current);
                }
            }

            output.Add(input[input.Count - 1]);
        }

        private bool ShouldKeepPoint(Vector3 prev, Vector3 current, Vector3 next)
        {
            Vector3 a = current - prev;
            Vector3 b = next - current;
            a.y = 0f;
            b.y = 0f;

            if (a.sqrMagnitude < 0.0001f || b.sqrMagnitude < 0.0001f)
            {
                return false;
            }

            float dot = Vector3.Dot(a.normalized, b.normalized);
            return dot < collinearDotThreshold;
        }

        private static void FillSegment(List<Vector3> source, int start, int endInclusive, List<Vector3> destination)
        {
            destination.Clear();
            if (source == null || source.Count == 0 || endInclusive <= start)
            {
                return;
            }

            int clampedStart = Mathf.Clamp(start, 0, source.Count - 1);
            int clampedEnd = Mathf.Clamp(endInclusive, 0, source.Count - 1);
            if (clampedEnd <= clampedStart)
            {
                return;
            }

            for (int i = clampedStart; i <= clampedEnd; i++)
            {
                destination.Add(source[i]);
            }
        }

        private void ApplySegmentToLine(LineRenderer line, List<Vector3> points, float width, Color color, bool enableFade)
        {
            if (line == null)
            {
                return;
            }

            line.startWidth = width;
            line.endWidth = width;

            if (points == null || points.Count < 2)
            {
                line.positionCount = 0;
                return;
            }

            line.positionCount = points.Count;
            for (int i = 0; i < points.Count; i++)
            {
                line.SetPosition(i, points[i]);
            }

            ApplyLineFade(line, color, enableFade, points.Count);
        }

        private void ApplyLineFade(LineRenderer line, Color baseColor, bool enableFade, int pointCount)
        {
            if (line == null)
            {
                return;
            }

            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(baseColor, 0f),
                    new GradientColorKey(baseColor, 1f)
                },
                enableFade
                    ? new[]
                    {
                        new GradientAlphaKey(baseColor.a, 0f),
                        new GradientAlphaKey(baseColor.a, ComputeFadeStartT(pointCount)),
                        new GradientAlphaKey(0f, ComputeFadeEndT(pointCount))
                    }
                    : new[]
                    {
                        new GradientAlphaKey(baseColor.a, 0f),
                        new GradientAlphaKey(baseColor.a, 1f)
                    });
            line.colorGradient = gradient;
        }

        private float ComputeFadeStartT(int pointCount)
        {
            int segments = Mathf.Max(1, pointCount - 1);
            float t = futureFadeStartNodes / (float)segments;
            return Mathf.Clamp01(t);
        }

        private float ComputeFadeEndT(int pointCount)
        {
            int segments = Mathf.Max(1, pointCount - 1);
            float tStart = ComputeFadeStartT(pointCount);
            float tEnd = futureFadeEndNodes / (float)segments;
            return Mathf.Clamp(tEnd, Mathf.Min(1f, tStart + 0.01f), 1f);
        }

        private int FindNearestPathIndex(List<Vector3> points, Transform reference)
        {
            if (points == null || points.Count == 0)
            {
                return 0;
            }

            if (reference == null)
            {
                return 0;
            }

            Vector3 refPos = reference.position;
            int best = 0;
            float bestDist = float.PositiveInfinity;
            for (int i = 0; i < points.Count; i++)
            {
                float d = HorizontalDistance(points[i], refPos);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = i;
                }
            }

            return best;
        }

        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            Vector2 av = new Vector2(a.x, a.z);
            Vector2 bv = new Vector2(b.x, b.z);
            return Vector2.Distance(av, bv);
        }

        private LineRenderer CreateChildLine(string objectName, float width, Color color)
        {
            Transform child = transform.Find(objectName);
            GameObject go = child != null ? child.gameObject : new GameObject(objectName);
            go.transform.SetParent(transform, worldPositionStays: false);
            LineRenderer line = go.GetComponent<LineRenderer>();
            if (line == null)
            {
                line = go.AddComponent<LineRenderer>();
            }

            ConfigureLineRenderer(line, objectName, width, color);
            return line;
        }

        private void ConfigureLineRenderer(LineRenderer line, string objectName, float width, Color color)
        {
            if (line == null)
            {
                return;
            }

            line.name = objectName;
            line.useWorldSpace = true;
            line.startWidth = width;
            line.endWidth = width;
            line.positionCount = 0;
            line.alignment = LineAlignment.View;
            line.numCornerVertices = 4;
            line.numCapVertices = 4;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.textureMode = LineTextureMode.Stretch;
            line.sortingOrder = 5000;
            if (_sharedMaterial != null)
            {
                line.material = _sharedMaterial;
            }

            ApplyLineFade(line, color, enableFade: false, pointCount: 0);
        }

        private void ClearAllLines()
        {
            if (_currentLine != null) _currentLine.positionCount = 0;
            if (_futureLine != null) _futureLine.positionCount = 0;
            if (_connectorLine != null) _connectorLine.positionCount = 0;
        }

        private float ResolveRouteY()
        {
            if (renderAtUserFeet && startReferenceTransform != null)
            {
                return startReferenceTransform.position.y + feetYOffset + feetClearance;
            }

            return (startReferenceTransform != null ? startReferenceTransform.position.y : 0f) + yOffset;
        }
    }
}
