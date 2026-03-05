using System.Collections.Generic;
using UnityEngine;

namespace CDE2501.Wayfinding.IndoorGraph
{
    public class GraphRuntimeVisualizer : MonoBehaviour
    {
        [SerializeField] private GraphLoader graphLoader;
        [SerializeField] private bool autoLoadGraphIfNeeded = false;
        [SerializeField] private bool autoBuildOnGraphLoaded = true;
        [SerializeField] private bool clearBeforeBuild = true;
        [SerializeField] private PrimitiveType markerPrimitive = PrimitiveType.Cube;
        [SerializeField] private float nodeScale = 0.3f;
        [SerializeField] private float edgeWidth = 0.05f;
        [SerializeField] private Color nodeColor = new Color(0.1f, 0.85f, 0.35f, 1f);
        [SerializeField] private Color edgeColor = new Color(0.2f, 0.7f, 1f, 1f);
        [SerializeField] private float yOffset = 0.05f;

        private readonly List<GameObject> _spawned = new List<GameObject>();
        private Material _lineMaterial;

        private void Awake()
        {
            if (graphLoader == null)
            {
                graphLoader = FindObjectOfType<GraphLoader>();
            }
        }

        private void OnEnable()
        {
            if (graphLoader != null)
            {
                graphLoader.OnGraphLoaded += HandleGraphLoaded;
            }
        }

        private void Start()
        {
            if (graphLoader == null)
            {
                return;
            }

            if (graphLoader.GraphData != null && graphLoader.NodesById.Count > 0)
            {
                if (autoBuildOnGraphLoaded)
                {
                    BuildGraphPreview();
                }
                return;
            }

            if (autoLoadGraphIfNeeded)
            {
                graphLoader.LoadGraph();
            }
        }

        private void OnDisable()
        {
            if (graphLoader != null)
            {
                graphLoader.OnGraphLoaded -= HandleGraphLoaded;
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

        public void BuildGraphPreview()
        {
            if (graphLoader == null || graphLoader.NodesById.Count == 0)
            {
                return;
            }

            if (clearBeforeBuild)
            {
                ClearPreview();
            }

            EnsureLineMaterial();

            foreach (Node node in graphLoader.NodesById.Values)
            {
                GameObject marker = GameObject.CreatePrimitive(markerPrimitive);
                marker.name = node.id;
                marker.transform.SetParent(transform, worldPositionStays: true);
                marker.transform.position = node.position + new Vector3(0f, yOffset, 0f);
                marker.transform.localScale = Vector3.one * nodeScale;

                var renderer = marker.GetComponent<Renderer>();
                if (renderer != null)
                {
                    renderer.material = new Material(Shader.Find("Standard"));
                    renderer.material.color = nodeColor;
                }

                var collider = marker.GetComponent<Collider>();
                if (collider != null)
                {
                    collider.enabled = false;
                }

                _spawned.Add(marker);
            }

            foreach (Edge edge in graphLoader.Edges)
            {
                Node from = graphLoader.GetNode(edge.fromNode);
                Node to = graphLoader.GetNode(edge.toNode);
                if (from == null || to == null)
                {
                    continue;
                }

                GameObject lineObj = new GameObject($"Edge_{edge.fromNode}_{edge.toNode}");
                lineObj.transform.SetParent(transform, worldPositionStays: true);
                var line = lineObj.AddComponent<LineRenderer>();
                line.material = _lineMaterial;
                line.startColor = edgeColor;
                line.endColor = edgeColor;
                line.positionCount = 2;
                line.useWorldSpace = true;
                line.startWidth = edgeWidth;
                line.endWidth = edgeWidth;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.receiveShadows = false;
                line.SetPosition(0, from.position + new Vector3(0f, yOffset, 0f));
                line.SetPosition(1, to.position + new Vector3(0f, yOffset, 0f));
                _spawned.Add(lineObj);
            }
        }

        public void ClearPreview()
        {
            for (int i = _spawned.Count - 1; i >= 0; i--)
            {
                GameObject go = _spawned[i];
                if (go != null)
                {
                    Destroy(go);
                }
            }

            _spawned.Clear();
        }

        private void HandleGraphLoaded(bool success, string message)
        {
            if (!success)
            {
                return;
            }

            if (autoBuildOnGraphLoaded)
            {
                BuildGraphPreview();
            }
        }

        private void EnsureLineMaterial()
        {
            if (_lineMaterial != null)
            {
                return;
            }

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }

            _lineMaterial = new Material(shader);
            _lineMaterial.color = edgeColor;
        }
    }
}
