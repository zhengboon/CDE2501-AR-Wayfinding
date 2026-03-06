using CDE2501.Wayfinding.IndoorGraph;
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
        [SerializeField] private float lineWidth = 0.22f;
        [SerializeField] private Color lineColor = new Color(1f, 0.85f, 0.2f, 1f);

        private LineRenderer _line;
        private RouteResult _lastRoute;

        private void Awake()
        {
            _line = GetComponent<LineRenderer>();
            _line.useWorldSpace = true;
            _line.startWidth = lineWidth;
            _line.endWidth = lineWidth;
            _line.positionCount = 0;
            _line.alignment = LineAlignment.View;
            _line.numCornerVertices = 4;
            _line.numCapVertices = 4;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _line.receiveShadows = false;
            _line.startColor = lineColor;
            _line.endColor = lineColor;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }
            _line.material = new Material(shader);
            _line.material.color = lineColor;
            _line.sortingOrder = 5000;

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
                _line.positionCount = 0;
                return;
            }

            if (!result.success || result.nodePath == null || result.nodePath.Count < 1)
            {
                _lastRoute = null;
                _line.positionCount = 0;
                return;
            }

            _lastRoute = result;
            DrawRoute(result);
        }

        private void DrawRoute(RouteResult result)
        {
            int startOffset = includeStartReferenceSegment && startReferenceTransform != null ? 1 : 0;
            _line.positionCount = result.nodePath.Count + startOffset;
            float routeY = ResolveRouteY();

            int writeIndex = 0;
            if (startOffset == 1)
            {
                Vector3 startPos = startReferenceTransform.position;
                startPos.y = routeY;
                _line.SetPosition(writeIndex++, startPos);
            }

            for (int i = 0; i < result.nodePath.Count; i++)
            {
                Node node = graphLoader.GetNode(result.nodePath[i]);
                if (node == null)
                {
                    _line.positionCount = 0;
                    return;
                }

                Vector3 nodePos = node.position;
                nodePos.y = routeY;
                _line.SetPosition(writeIndex++, nodePos);
            }
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
