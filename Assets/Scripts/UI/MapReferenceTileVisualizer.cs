using System.Collections;
using System.IO;
using CDE2501.Wayfinding.IndoorGraph;
using UnityEngine;
using UnityEngine.Networking;

namespace CDE2501.Wayfinding.UI
{
    public class MapReferenceTileVisualizer : MonoBehaviour
    {
        [SerializeField] private GraphLoader graphLoader;
        [SerializeField] private Transform startReferenceTransform;
        [SerializeField] private bool followMainCameraIfReferenceMissing = true;

        [Header("Tile Source")]
        [SerializeField] private string tileFileName = "map_tile_z16_x51664_y32532.png";
        [SerializeField] private int zoomLevel = 16;
        [SerializeField] private float tileCenterLatitude = 1.2935302390f;

        [Header("Placement")]
        [SerializeField] private bool placeAtFeetLevel = true;
        [SerializeField] private float feetYOffset = -1.62f;
        [SerializeField] private float baseYOffset = -0.05f;
        [SerializeField] private bool fitToGraphBounds = true;
        [SerializeField, Min(0f)] private float graphPaddingMeters = 12f;

        [Header("Style")]
        [SerializeField, Range(0.05f, 1f)] private float mapAlpha = 0.65f;
        [SerializeField] private Color tint = Color.white;

        private GameObject _quad;
        private Material _material;
        private Texture2D _texture;
        private bool _graphReady;
        private bool _loadingTexture;

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
            _graphReady = graphLoader != null && graphLoader.NodesById.Count > 0;
            if (_graphReady)
            {
                EnsureBuilt();
            }
            else if (graphLoader != null)
            {
                graphLoader.LoadGraph();
            }
        }

        private void Update()
        {
            if (_quad == null)
            {
                return;
            }

            if (followMainCameraIfReferenceMissing && startReferenceTransform == null && Camera.main != null)
            {
                startReferenceTransform = Camera.main.transform;
            }

            if (placeAtFeetLevel && startReferenceTransform != null)
            {
                Vector3 pos = _quad.transform.position;
                pos.y = startReferenceTransform.position.y + feetYOffset;
                _quad.transform.position = pos;
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
                _graphReady = graphLoader.NodesById.Count > 0;
                if (_graphReady)
                {
                    EnsureBuilt();
                }
            }
        }

        public void SetStartReferenceTransform(Transform referenceTransform)
        {
            startReferenceTransform = referenceTransform;
            if (_quad != null)
            {
                Update();
            }
        }

        private void HandleGraphLoaded(bool success, string message)
        {
            _graphReady = success && graphLoader != null && graphLoader.NodesById.Count > 0;
            if (_graphReady)
            {
                EnsureBuilt();
            }
        }

        private void EnsureBuilt()
        {
            if (!_graphReady || graphLoader == null || graphLoader.NodesById.Count == 0)
            {
                return;
            }

            EnsureQuad();
            FitQuadToGraph();

            if (!_loadingTexture && _texture == null)
            {
                StartCoroutine(LoadTextureRoutine());
            }
        }

        private void EnsureQuad()
        {
            if (_quad != null)
            {
                return;
            }

            _quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _quad.name = "MapReferenceTile";
            _quad.transform.SetParent(transform, worldPositionStays: true);
            _quad.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            Collider col = _quad.GetComponent<Collider>();
            if (col != null)
            {
                col.enabled = false;
            }

            Shader shader = Shader.Find("Unlit/Transparent");
            if (shader == null)
            {
                shader = Shader.Find("Unlit/Texture");
            }
            _material = new Material(shader);
            _quad.GetComponent<Renderer>().material = _material;
            ApplyMaterialStyle();
        }

        private void FitQuadToGraph()
        {
            float minX = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float minZ = float.PositiveInfinity;
            float maxZ = float.NegativeInfinity;

            foreach (var kvp in graphLoader.NodesById)
            {
                Vector3 p = kvp.Value.position;
                if (p.x < minX) minX = p.x;
                if (p.x > maxX) maxX = p.x;
                if (p.z < minZ) minZ = p.z;
                if (p.z > maxZ) maxZ = p.z;
            }

            Vector3 center = new Vector3((minX + maxX) * 0.5f, baseYOffset, (minZ + maxZ) * 0.5f);
            if (placeAtFeetLevel && startReferenceTransform != null)
            {
                center.y = startReferenceTransform.position.y + feetYOffset;
            }

            float tileMeters = ComputeTileMetersAtLatitude(zoomLevel, tileCenterLatitude);
            float width = tileMeters;
            float depth = tileMeters;

            if (fitToGraphBounds)
            {
                width = Mathf.Max(width, (maxX - minX) + (graphPaddingMeters * 2f));
                depth = Mathf.Max(depth, (maxZ - minZ) + (graphPaddingMeters * 2f));
            }

            _quad.transform.position = center;
            _quad.transform.localScale = new Vector3(width, depth, 1f);
        }

        private IEnumerator LoadTextureRoutine()
        {
            _loadingTexture = true;
            string streamingPath = Path.Combine(Application.streamingAssetsPath, "Data", tileFileName);
            string url = ToUnityWebRequestPath(streamingPath);

            using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(url))
            {
                yield return request.SendWebRequest();
                if (request.result == UnityWebRequest.Result.Success)
                {
                    _texture = DownloadHandlerTexture.GetContent(request);
                    ApplyTexture();
                }
            }

            _loadingTexture = false;
        }

        private void ApplyTexture()
        {
            if (_material == null || _texture == null)
            {
                return;
            }

            _material.mainTexture = _texture;
            // Flip vertically so north-up tile orientation matches +Z direction.
            _material.mainTextureScale = new Vector2(1f, -1f);
            _material.mainTextureOffset = new Vector2(0f, 1f);
            ApplyMaterialStyle();
        }

        private void ApplyMaterialStyle()
        {
            if (_material == null)
            {
                return;
            }

            Color c = tint;
            c.a = Mathf.Clamp01(mapAlpha);
            _material.color = c;
        }

        private static float ComputeTileMetersAtLatitude(int zoom, float latitude)
        {
            const double earthCircumference = 40075016.686;
            double metersPerPixel = System.Math.Cos(latitude * Mathf.Deg2Rad) * earthCircumference / (System.Math.Pow(2, zoom) * 256.0);
            return (float)(metersPerPixel * 256.0);
        }

        private static string ToUnityWebRequestPath(string path)
        {
            if (path.StartsWith("http://") ||
                path.StartsWith("https://") ||
                path.StartsWith("file://") ||
                path.Contains("://"))
            {
                return path;
            }

            return "file://" + path;
        }
    }
}
