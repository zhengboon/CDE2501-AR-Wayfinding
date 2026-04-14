using System;
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
        [SerializeField] private string tileFileName = "queenstown_map_z18_x206656-206662_y130127-130133.png";
        [SerializeField] private int zoomLevel = 18;
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
        private int _tilesX = 1;
        private int _tilesY = 1;

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
            PreferHigherResolutionTileIfAvailable();
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

        public void SetTileFileName(string fileName, bool forceReload = false)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return;
            }

            string normalized = fileName.Trim();
            bool isSameFile = string.Equals(tileFileName, normalized, StringComparison.OrdinalIgnoreCase);
            if (isSameFile && !forceReload)
            {
                return;
            }

            tileFileName = normalized;
            ResetLoadedTileTexture();
            if (_graphReady && !_loadingTexture)
            {
                StartCoroutine(LoadTextureRoutine());
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

        private void ResetLoadedTileTexture()
        {
            if (_material != null)
            {
                _material.mainTexture = null;
            }

            if (_texture != null)
            {
                Destroy(_texture);
                _texture = null;
            }

            _tilesX = 1;
            _tilesY = 1;
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
            Renderer quadRenderer = _quad.GetComponent<Renderer>();
            if (quadRenderer != null)
            {
                quadRenderer.material = _material;
            }
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
            float width = tileMeters * Mathf.Max(1, _tilesX);
            float depth = tileMeters * Mathf.Max(1, _tilesY);

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
            string persistentPath = Path.Combine(Application.persistentDataPath, "Data", tileFileName);
            yield return LoadTextureFromPathRoutine(persistentPath);

            if (_texture == null)
            {
                string streamingPath = Path.Combine(Application.streamingAssetsPath, "Data", tileFileName);
                yield return LoadTextureFromPathRoutine(streamingPath);
            }

            _loadingTexture = false;
        }

        private IEnumerator LoadTextureFromPathRoutine(string imagePath)
        {
            if (string.IsNullOrWhiteSpace(imagePath))
            {
                yield break;
            }

            string url = ToUnityWebRequestPath(imagePath);
            yield return LoadAtlasMetadataRoutine(imagePath);

            using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(url))
            {
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    yield break;
                }

                _texture = DownloadHandlerTexture.GetContent(request);
                if (_texture != null)
                {
                    _texture.wrapMode = TextureWrapMode.Clamp;
                    _texture.filterMode = FilterMode.Bilinear;
                    _texture.anisoLevel = 4;
                }

                ApplyTexture();
                FitQuadToGraph();
            }
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

        private void PreferHigherResolutionTileIfAvailable()
        {
            try
            {
                if (!tileFileName.StartsWith("map_tile_", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                string chosen = null;
                ConsiderBestMatchingMapInDirectory(Path.Combine(Application.persistentDataPath, "Data"), ref chosen);
                ConsiderBestMatchingMapInDirectory(Path.Combine(Application.streamingAssetsPath, "Data"), ref chosen);

                string chosenName = Path.GetFileName(chosen);
                if (!string.IsNullOrWhiteSpace(chosenName))
                {
                    tileFileName = chosenName;
                }
            }
            catch (Exception)
            {
                // Keep configured tile when discovery fails.
            }
        }

        private static void ConsiderBestMatchingMapInDirectory(string directoryPath, ref string currentBestPath)
        {
            if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
            {
                return;
            }

            string[] candidates = Directory.GetFiles(directoryPath, "queenstown_map_z*.png");
            if (candidates == null || candidates.Length == 0)
            {
                return;
            }

            for (int i = 0; i < candidates.Length; i++)
            {
                string candidate = candidates[i];
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(currentBestPath))
                {
                    currentBestPath = candidate;
                    continue;
                }

                string candidateName = Path.GetFileName(candidate);
                string currentName = Path.GetFileName(currentBestPath);
                if (string.Compare(candidateName, currentName, StringComparison.OrdinalIgnoreCase) > 0)
                {
                    currentBestPath = candidate;
                }
            }
        }

        private IEnumerator LoadAtlasMetadataRoutine(string imagePath)
        {
            _tilesX = 1;
            _tilesY = 1;
            if (string.IsNullOrWhiteSpace(imagePath))
            {
                yield break;
            }

            string metadataPath = Path.ChangeExtension(imagePath, ".json");
            if (string.IsNullOrWhiteSpace(metadataPath))
            {
                yield break;
            }

            using (UnityWebRequest request = UnityWebRequest.Get(ToUnityWebRequestPath(metadataPath)))
            {
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    yield break;
                }

                try
                {
                    MapImageMetadata metadata = JsonUtility.FromJson<MapImageMetadata>(request.downloadHandler.text);
                    if (metadata == null)
                    {
                        yield break;
                    }

                    if (metadata.zoom > 0)
                    {
                        zoomLevel = metadata.zoom;
                    }

                    int tilesX = metadata.tilesX;
                    int tilesY = metadata.tilesY;
                    if (tilesX <= 0 && metadata.maxTileX >= metadata.minTileX)
                    {
                        tilesX = (metadata.maxTileX - metadata.minTileX) + 1;
                    }

                    if (tilesY <= 0 && metadata.maxTileY >= metadata.minTileY)
                    {
                        tilesY = (metadata.maxTileY - metadata.minTileY) + 1;
                    }

                    _tilesX = Mathf.Max(1, tilesX);
                    _tilesY = Mathf.Max(1, tilesY);

                    if (metadata.geoBounds != null)
                    {
                        tileCenterLatitude = (float)((metadata.geoBounds.minLat + metadata.geoBounds.maxLat) * 0.5);
                    }
                }
                catch (Exception)
                {
                    _tilesX = 1;
                    _tilesY = 1;
                }
            }
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
