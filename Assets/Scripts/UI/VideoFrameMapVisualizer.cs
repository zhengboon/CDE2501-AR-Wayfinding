using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using CDE2501.Wayfinding.IndoorGraph;
using UnityEngine;
using UnityEngine.Networking;

namespace CDE2501.Wayfinding.UI
{
    [Serializable]
    public class VideoFrameMapFrame
    {
        public string image;
        public float timeSeconds;
        public string nodeId;
        public Vector3 position;
    }

    [Serializable]
    public class VideoFrameMapVideo
    {
        public string videoId;
        public string title;
        public string uploader;
        public string url;
        public string mode;
        public string duration;
        public float durationSeconds;
        public string startNodeId;
        public string endNodeId;
        public string inferenceMode;
        public float routeDistanceMeters;
        public List<string> routeNodePath = new List<string>();
        public string frameSource;
        public List<VideoFrameMapFrame> frames = new List<VideoFrameMapFrame>();
    }

    [Serializable]
    public class VideoFrameMapManifest
    {
        public string version;
        public string generatedAtUtc;
        public string sourceCsv;
        public List<VideoFrameMapVideo> videos = new List<VideoFrameMapVideo>();
        public List<string> warnings = new List<string>();
    }

    public class VideoFrameMapVisualizer : MonoBehaviour
    {
        [SerializeField] private GraphLoader graphLoader;
        [SerializeField] private string manifestFileName = "video_frame_map.json";
        [SerializeField] private bool refreshFromStreamingAssetsOnLoad = true;
        [SerializeField] private bool autoLoadOnStart = true;

        [Header("Marker Limits")]
        [SerializeField, Min(1)] private int maxVideos = 10;
        [SerializeField, Min(1)] private int maxFramesPerVideo = 8;

        [Header("Marker Style")]
        [SerializeField] private float markerSize = 2.2f;
        [SerializeField] private float yOffset = 1.2f;
        [SerializeField, Range(0.2f, 1f)] private float markerAlpha = 1f;
        [SerializeField] private bool billboardTowardsCamera = true;
        [SerializeField] private bool clearExistingBeforeBuild = true;

        private readonly List<GameObject> _markers = new List<GameObject>();
        private readonly List<Transform> _billboardMarkers = new List<Transform>();
        private readonly List<Material> _materials = new List<Material>();
        private VideoFrameMapManifest _manifest;
        private bool _manifestReady;
        private bool _graphReady;

        public bool ManifestReady => _manifestReady;
        public int MarkerCount => _markers.Count;

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

        private void OnDisable()
        {
            if (graphLoader != null)
            {
                graphLoader.OnGraphLoaded -= HandleGraphLoaded;
            }
        }

        private void Start()
        {
            _graphReady = graphLoader != null && graphLoader.NodesById.Count > 0;
            if (!_graphReady && graphLoader != null)
            {
                graphLoader.LoadGraph();
            }

            if (autoLoadOnStart)
            {
                LoadManifest();
            }
        }

        private void OnDestroy()
        {
            ClearMarkers();
        }

        private void LateUpdate()
        {
            if (!billboardTowardsCamera || _billboardMarkers.Count == 0 || Camera.main == null)
            {
                return;
            }

            Transform cam = Camera.main.transform;
            for (int i = 0; i < _billboardMarkers.Count; i++)
            {
                Transform marker = _billboardMarkers[i];
                if (marker == null)
                {
                    continue;
                }

                Vector3 toCamera = cam.position - marker.position;
                if (toCamera.sqrMagnitude < 0.0001f)
                {
                    continue;
                }

                marker.rotation = Quaternion.LookRotation(toCamera.normalized, Vector3.up);
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

            _graphReady = graphLoader != null && graphLoader.NodesById.Count > 0;
            TryBuildMarkers(force: true);
        }

        public void LoadManifest()
        {
            StartCoroutine(LoadManifestRoutine());
        }

        public void Rebuild()
        {
            TryBuildMarkers(force: true);
        }

        private void HandleGraphLoaded(bool success, string message)
        {
            _graphReady = success && graphLoader != null && graphLoader.NodesById.Count > 0;
            TryBuildMarkers();
        }

        private IEnumerator LoadManifestRoutine()
        {
            string persistentPath = GetPersistentPath(manifestFileName);
            string streamingPath = GetStreamingPath(manifestFileName);

            if (refreshFromStreamingAssetsOnLoad || !File.Exists(persistentPath))
            {
                yield return CopyFromStreamingAssets(streamingPath, persistentPath);
            }

            if (!File.Exists(persistentPath))
            {
                _manifestReady = false;
                Debug.LogWarning($"VideoFrameMapVisualizer: manifest not found at '{persistentPath}'.");
                yield break;
            }

            string json = File.ReadAllText(persistentPath);
            _manifest = JsonUtility.FromJson<VideoFrameMapManifest>(json);
            _manifestReady = _manifest != null && _manifest.videos != null && _manifest.videos.Count > 0;
            Debug.Log($"VideoFrameMapVisualizer: manifest loaded={_manifestReady}, videos={(_manifest != null && _manifest.videos != null ? _manifest.videos.Count : 0)}.");
            TryBuildMarkers(force: true);
        }

        private void TryBuildMarkers(bool force = false)
        {
            if (!force && (!_graphReady || !_manifestReady))
            {
                return;
            }

            if (graphLoader == null || graphLoader.NodesById.Count == 0 || _manifest == null || _manifest.videos == null)
            {
                return;
            }

            if (clearExistingBeforeBuild)
            {
                ClearMarkers();
            }

            int usedVideos = 0;
            for (int vi = 0; vi < _manifest.videos.Count && usedVideos < maxVideos; vi++)
            {
                VideoFrameMapVideo video = _manifest.videos[vi];
                if (video == null || video.frames == null || video.frames.Count == 0)
                {
                    continue;
                }

                int step = Mathf.Max(1, Mathf.CeilToInt((float)video.frames.Count / Mathf.Max(1, maxFramesPerVideo)));
                Color baseColor = Color.HSVToRGB((vi * 0.127f) % 1f, 0.75f, 1f);
                baseColor.a = markerAlpha;

                int framesSpawnedForVideo = 0;
                for (int fi = 0; fi < video.frames.Count && framesSpawnedForVideo < maxFramesPerVideo; fi += step)
                {
                    VideoFrameMapFrame frame = video.frames[fi];
                    if (frame == null)
                    {
                        continue;
                    }

                    CreateMarker(video, frame, baseColor, framesSpawnedForVideo);
                    framesSpawnedForVideo++;
                }

                usedVideos++;
            }

            Debug.Log($"VideoFrameMapVisualizer: spawned {_markers.Count} markers from {usedVideos} videos.");
        }

        private void CreateMarker(VideoFrameMapVideo video, VideoFrameMapFrame frame, Color baseColor, int localIndex)
        {
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Quad);
            marker.name = $"VideoFrame_{video.videoId}_{localIndex:D2}";
            marker.transform.SetParent(transform, worldPositionStays: true);
            marker.transform.position = frame.position + new Vector3(0f, yOffset, 0f);
            marker.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            marker.transform.localScale = new Vector3(markerSize, markerSize, markerSize);

            Collider col = marker.GetComponent<Collider>();
            if (col != null)
            {
                col.enabled = false;
            }

            Renderer renderer = marker.GetComponent<Renderer>();
            if (renderer != null)
            {
                Material material = CreateMaterial(baseColor);
                if (material != null)
                {
                    renderer.material = material;
                    _materials.Add(material);
                    StartCoroutine(LoadFrameTextureRoutine(material, frame.image));
                }
            }

            _markers.Add(marker);
            _billboardMarkers.Add(marker.transform);
        }

        private IEnumerator LoadFrameTextureRoutine(Material material, string relativeImagePath)
        {
            if (material == null || string.IsNullOrWhiteSpace(relativeImagePath))
            {
                yield break;
            }

            string persistentPath = Path.Combine(Application.persistentDataPath, "Data", relativeImagePath);
            string streamingPath = Path.Combine(Application.streamingAssetsPath, "Data", relativeImagePath);

            if (!File.Exists(persistentPath))
            {
                yield return CopyFromStreamingAssets(streamingPath, persistentPath);
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

                    material.mainTexture = texture;
                }
            }
        }

        private void ClearMarkers()
        {
            for (int i = _markers.Count - 1; i >= 0; i--)
            {
                if (_markers[i] != null)
                {
                    Destroy(_markers[i]);
                }
            }
            _markers.Clear();
            _billboardMarkers.Clear();

            for (int i = _materials.Count - 1; i >= 0; i--)
            {
                if (_materials[i] != null)
                {
                    Destroy(_materials[i]);
                }
            }
            _materials.Clear();
        }

        private static Material CreateMaterial(Color color)
        {
            Shader shader =
                Shader.Find("Unlit/Transparent") ??
                Shader.Find("Unlit/Texture") ??
                Shader.Find("Standard");

            if (shader == null)
            {
                return null;
            }

            Material material = new Material(shader);
            material.color = color;
            return material;
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

        private static IEnumerator CopyFromStreamingAssets(string sourcePath, string destinationPath)
        {
            string folder = Path.GetDirectoryName(destinationPath);
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            UnityWebRequest request = UnityWebRequest.Get(ToUnityWebRequestPath(sourcePath));
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                File.WriteAllBytes(destinationPath, request.downloadHandler.data);
            }

            request.Dispose();
        }
    }
}
