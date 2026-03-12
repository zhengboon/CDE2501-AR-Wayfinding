using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace CDE2501.Wayfinding.UI
{
    [Serializable]
    public class VideoCsvRow
    {
        public string rank;
        public string title;
        public string uploader;
        public string duration;
        public string url;
        public string mode;
        public string thumbnailUrl;
    }

    public class VideoMappingCsvOverlay : MonoBehaviour
    {
        [Header("Display")]
        [SerializeField] private bool showOverlay = true;
        [SerializeField] private KeyCode toggleKey = KeyCode.V;
        [SerializeField, Range(0.8f, 2.5f)] private float overlayScale = 1f;
        [SerializeField] private Vector2 panelSize = new Vector2(980f, 460f);
        [SerializeField] private Vector2 panelStartPosition = new Vector2(120f, 560f);
        [SerializeField, Range(12, 40)] private int titleFontSize = 24;
        [SerializeField, Range(11, 32)] private int bodyFontSize = 16;
        [SerializeField] private bool autoLoadOnStart = true;
        [SerializeField] private bool showThumbnails = true;
        [SerializeField, Range(24, 80)] private int thumbnailSize = 44;

        [Header("Data")]
        [SerializeField] private string csvFileName = "videos_for_mapping.csv";
        [SerializeField] private bool refreshFromStreamingAssetsOnLoad = true;

        private readonly List<VideoCsvRow> _rows = new List<VideoCsvRow>();
        private Rect _panelRect;
        private bool _panelRectInitialized;
        private Vector2 _scroll;
        private GUIStyle _windowStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _titleStyle;
        private Texture2D _panelTexture;
        private string _status = "Not loaded.";
        private bool _isLoading;
        private readonly Dictionary<string, Texture2D> _thumbnailCache = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _thumbnailInFlight = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Queue<string> _thumbnailQueue = new Queue<string>();
        private readonly HashSet<string> _thumbnailQueued = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private int _activeThumbnailDownloads;
        private const int MaxConcurrentThumbnailDownloads = 3;

        public IReadOnlyList<VideoCsvRow> Rows => _rows;
        public bool IsLoaded => !_isLoading && _rows.Count > 0;

        private void Start()
        {
            if (autoLoadOnStart)
            {
                LoadCsv();
            }
        }

        private void Update()
        {
            if (Input.GetKeyDown(toggleKey))
            {
                showOverlay = !showOverlay;
            }

            if (showThumbnails)
            {
                PumpThumbnailRequests();
            }
        }

        private void OnDestroy()
        {
            foreach (var kv in _thumbnailCache)
            {
                if (kv.Value != null)
                {
                    Destroy(kv.Value);
                }
            }

            _thumbnailCache.Clear();
            _thumbnailInFlight.Clear();
            _thumbnailQueue.Clear();
            _thumbnailQueued.Clear();
        }

        public void LoadCsv()
        {
            if (_isLoading)
            {
                return;
            }

            StartCoroutine(LoadCsvRoutine());
        }

        private IEnumerator LoadCsvRoutine()
        {
            _isLoading = true;
            _status = "Loading CSV...";

            string persistentPath = GetPersistentPath(csvFileName);
            string streamingPath = GetStreamingPath(csvFileName);

            if (refreshFromStreamingAssetsOnLoad || !File.Exists(persistentPath))
            {
                yield return CopyFromStreamingAssets(streamingPath, persistentPath);
            }

            if (!File.Exists(persistentPath))
            {
                _rows.Clear();
                _status = $"CSV not found: {persistentPath}";
                _isLoading = false;
                yield break;
            }

            string raw = File.ReadAllText(persistentPath);
            _rows.Clear();
            ParseCsv(raw, _rows);
            _status = $"Loaded {_rows.Count} rows from {csvFileName}";
            _isLoading = false;
        }

        private void OnGUI()
        {
            if (!showOverlay)
            {
                return;
            }

            EnsureStyles();

            float scale = Mathf.Clamp(overlayScale, 0.8f, 2.5f);
            Matrix4x4 prev = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

            EnsurePanelRect(scale);
            _panelRect = GUI.Window(GetInstanceID() + 3050, _panelRect, DrawWindow, "Video Mapping CSV", _windowStyle);
            GUI.matrix = prev;
        }

        private void DrawWindow(int id)
        {
            float y = 34f;

            if (GUI.Button(new Rect(12f, y, 96f, 24f), _isLoading ? "Loading..." : "Reload"))
            {
                LoadCsv();
            }

            GUI.Label(new Rect(118f, y + 2f, _panelRect.width - 130f, 24f), _status, _labelStyle);
            y += 30f;

            float headerHeight = 26f;
            Rect headerRect = new Rect(12f, y, _panelRect.width - 24f, headerHeight);
            GUI.Box(headerRect, GUIContent.none);
            GUI.Label(new Rect(headerRect.x + 8f, headerRect.y + 4f, 44f, 20f), "#", _titleStyle);
            GUI.Label(new Rect(headerRect.x + 56f, headerRect.y + 4f, 48f, 20f), "Img", _titleStyle);
            GUI.Label(new Rect(headerRect.x + 108f, headerRect.y + 4f, 378f, 20f), "Title", _titleStyle);
            GUI.Label(new Rect(headerRect.x + 490f, headerRect.y + 4f, 160f, 20f), "Uploader", _titleStyle);
            GUI.Label(new Rect(headerRect.x + 654f, headerRect.y + 4f, 86f, 20f), "Duration", _titleStyle);
            GUI.Label(new Rect(headerRect.x + 744f, headerRect.y + 4f, 76f, 20f), "Mode", _titleStyle);
            GUI.Label(new Rect(headerRect.x + 826f, headerRect.y + 4f, 80f, 20f), "Link", _titleStyle);
            y += headerHeight + 4f;

            Rect viewport = new Rect(12f, y, _panelRect.width - 24f, Mathf.Max(24f, _panelRect.height - y - 8f));
            float rowHeight = Mathf.Max(showThumbnails ? thumbnailSize + 6f : 24f, bodyFontSize + 8f);
            float contentHeight = Mathf.Max(viewport.height - 4f, _rows.Count * rowHeight);
            Rect content = new Rect(0f, 0f, viewport.width - 20f, contentHeight);

            _scroll = GUI.BeginScrollView(viewport, _scroll, content);
            for (int i = 0; i < _rows.Count; i++)
            {
                VideoCsvRow row = _rows[i];
                float rowY = i * rowHeight;
                Rect rowRect = new Rect(0f, rowY, content.width, rowHeight - 2f);
                if ((i & 1) == 0)
                {
                    DrawFilledRect(rowRect, new Color(1f, 1f, 1f, 0.045f));
                }

                float thumbSize = Mathf.Min(thumbnailSize, rowHeight - 4f);
                Rect thumbRect = new Rect(52f, rowY + 2f, thumbSize, thumbSize);
                DrawThumbnailCell(row, thumbRect);

                GUI.Label(new Rect(6f, rowY + 3f, 40f, rowHeight - 6f), string.IsNullOrWhiteSpace(row.rank) ? (i + 1).ToString() : row.rank, _labelStyle);
                GUI.Label(new Rect(108f, rowY + 3f, 378f, rowHeight - 6f), SafeCut(row.title, 60), _labelStyle);
                GUI.Label(new Rect(486f, rowY + 3f, 160f, rowHeight - 6f), SafeCut(row.uploader, 24), _labelStyle);
                GUI.Label(new Rect(650f, rowY + 3f, 86f, rowHeight - 6f), row.duration ?? string.Empty, _labelStyle);
                GUI.Label(new Rect(740f, rowY + 3f, 76f, rowHeight - 6f), row.mode ?? string.Empty, _labelStyle);

                if (GUI.Button(new Rect(822f, rowY + 2f, 64f, rowHeight - 4f), "Open"))
                {
                    if (!string.IsNullOrWhiteSpace(row.url))
                    {
                        Application.OpenURL(row.url);
                    }
                }
            }

            GUI.EndScrollView();
            GUI.DragWindow(new Rect(0f, 0f, _panelRect.width, 30f));
        }

        private void DrawThumbnailCell(VideoCsvRow row, Rect rect)
        {
            DrawFilledRect(rect, new Color(1f, 1f, 1f, 0.08f));

            if (!showThumbnails || row == null || string.IsNullOrWhiteSpace(row.thumbnailUrl))
            {
                GUI.Label(rect, "-", _labelStyle);
                return;
            }

            if (_thumbnailCache.TryGetValue(row.thumbnailUrl, out Texture2D texture) && texture != null)
            {
                GUI.DrawTexture(rect, texture, ScaleMode.ScaleAndCrop, true);
                return;
            }

            RequestThumbnail(row.thumbnailUrl);
            GUI.Label(rect, "...", _labelStyle);
        }

        private void PumpThumbnailRequests()
        {
            while (_activeThumbnailDownloads < MaxConcurrentThumbnailDownloads && _thumbnailQueue.Count > 0)
            {
                string url = _thumbnailQueue.Dequeue();
                _thumbnailQueued.Remove(url);
                _thumbnailInFlight.Add(url);
                _activeThumbnailDownloads++;
                StartCoroutine(LoadThumbnailRoutine(url));
            }
        }

        private void RequestThumbnail(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            if (_thumbnailCache.ContainsKey(url) || _thumbnailInFlight.Contains(url) || _thumbnailQueued.Contains(url))
            {
                return;
            }

            _thumbnailQueue.Enqueue(url);
            _thumbnailQueued.Add(url);
        }

        private IEnumerator LoadThumbnailRoutine(string url)
        {
            Texture2D texture = null;
            try
            {
                string cacheDir = Path.Combine(Application.persistentDataPath, "Data", "video_thumbnails");
                if (!Directory.Exists(cacheDir))
                {
                    Directory.CreateDirectory(cacheDir);
                }

                string cachePath = Path.Combine(cacheDir, BuildThumbnailFileName(url));

                if (File.Exists(cachePath))
                {
                    byte[] bytes = null;
                    try
                    {
                        bytes = File.ReadAllBytes(cachePath);
                    }
                    catch (Exception)
                    {
                        bytes = null;
                    }

                    if (bytes != null && bytes.Length > 0)
                    {
                        texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                        if (!texture.LoadImage(bytes, markNonReadable: false))
                        {
                            Destroy(texture);
                            texture = null;
                        }
                        else
                        {
                            ConfigureThumbnailTexture(texture);
                        }
                    }
                }

                if (texture == null)
                {
                    using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(url))
                    {
                        request.timeout = 20;
                        yield return request.SendWebRequest();
                        if (request.result == UnityWebRequest.Result.Success)
                        {
                            texture = DownloadHandlerTexture.GetContent(request);
                            ConfigureThumbnailTexture(texture);

                            try
                            {
                                if (request.downloadHandler?.data != null && request.downloadHandler.data.Length > 0)
                                {
                                    File.WriteAllBytes(cachePath, request.downloadHandler.data);
                                }
                            }
                            catch (Exception)
                            {
                                // cache write failure should not block runtime display
                            }
                        }
                    }
                }
            }
            finally
            {
                _thumbnailInFlight.Remove(url);
                _activeThumbnailDownloads = Mathf.Max(0, _activeThumbnailDownloads - 1);
            }

            if (texture != null)
            {
                _thumbnailCache[url] = texture;
            }
        }

        private static void ConfigureThumbnailTexture(Texture2D texture)
        {
            if (texture == null)
            {
                return;
            }

            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
        }

        private static string BuildThumbnailFileName(string url)
        {
            using (var md5 = MD5.Create())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(url);
                byte[] hash = md5.ComputeHash(bytes);
                var sb = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                {
                    sb.Append(hash[i].ToString("x2"));
                }

                return sb.ToString() + ".jpg";
            }
        }

        private void EnsurePanelRect(float scale)
        {
            float virtualScreenWidth = Screen.width / scale;
            float virtualScreenHeight = Screen.height / scale;
            float width = Mathf.Min(virtualScreenWidth - 24f, panelSize.x);
            float height = Mathf.Min(virtualScreenHeight - 24f, panelSize.y);

            if (!_panelRectInitialized)
            {
                float startX = panelStartPosition.x <= 0f ? (virtualScreenWidth - width) * 0.5f : panelStartPosition.x;
                float startY = panelStartPosition.y <= 0f ? (virtualScreenHeight - height) * 0.5f : panelStartPosition.y;
                _panelRect = new Rect(startX, startY, width, height);
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

        private void EnsureStyles()
        {
            if (_windowStyle != null)
            {
                return;
            }

            _panelTexture = MakeSolidTexture(new Color(0f, 0f, 0f, 0.8f));
            _windowStyle = new GUIStyle(GUI.skin.box)
            {
                normal = { background = _panelTexture },
                border = new RectOffset(8, 8, 8, 8),
                padding = new RectOffset(0, 0, 0, 0),
                fontSize = titleFontSize,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.UpperCenter
            };
            _windowStyle.normal.textColor = Color.white;

            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.Max(11, bodyFontSize),
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.UpperLeft,
                clipping = TextClipping.Clip
            };
            _titleStyle.normal.textColor = Color.white;

            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = bodyFontSize,
                alignment = TextAnchor.UpperLeft,
                clipping = TextClipping.Clip,
                wordWrap = false
            };
            _labelStyle.normal.textColor = Color.white;
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

        private static string SafeCut(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            if (value.Length <= maxLength)
            {
                return value;
            }

            return value.Substring(0, Math.Max(0, maxLength - 3)) + "...";
        }

        private static void DrawFilledRect(Rect rect, Color color)
        {
            Color prev = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true);
            GUI.color = prev;
        }

        private static void ParseCsv(string raw, List<VideoCsvRow> output)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return;
            }

            raw = raw.TrimStart('\uFEFF');

            string[] lines = raw.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
            if (lines.Length <= 1)
            {
                return;
            }

            List<string> headers = ParseCsvLine(lines[0]);
            var headerIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < headers.Count; i++)
            {
                string key = headers[i] != null ? headers[i].Trim() : string.Empty;
                if (!string.IsNullOrWhiteSpace(key))
                {
                    headerIndex[key] = i;
                }
            }

            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                List<string> values = ParseCsvLine(line);
                var row = new VideoCsvRow
                {
                    rank = GetCsvValue("rank", headerIndex, values),
                    title = GetCsvValue("title", headerIndex, values),
                    uploader = GetCsvValue("uploader", headerIndex, values),
                    duration = GetCsvValue("duration", headerIndex, values),
                    url = GetCsvValue("url", headerIndex, values),
                    mode = GetCsvValue("mode", headerIndex, values),
                    thumbnailUrl = GetCsvValue("thumbnail_url", headerIndex, values)
                };

                if (string.IsNullOrWhiteSpace(row.title) && string.IsNullOrWhiteSpace(row.url))
                {
                    continue;
                }

                output.Add(row);
            }
        }

        private static string GetCsvValue(string name, Dictionary<string, int> headerIndex, List<string> values)
        {
            if (!headerIndex.TryGetValue(name, out int index))
            {
                return string.Empty;
            }

            if (index < 0 || index >= values.Count)
            {
                return string.Empty;
            }

            return values[index] ?? string.Empty;
        }

        private static List<string> ParseCsvLine(string line)
        {
            var values = new List<string>();
            if (line == null)
            {
                values.Add(string.Empty);
                return values;
            }

            var sb = new StringBuilder(line.Length);
            bool inQuotes = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                        continue;
                    }

                    inQuotes = !inQuotes;
                    continue;
                }

                if (c == ',' && !inQuotes)
                {
                    values.Add(sb.ToString().Trim());
                    sb.Length = 0;
                    continue;
                }

                sb.Append(c);
            }

            values.Add(sb.ToString().Trim());
            return values;
        }

        private static IEnumerator CopyFromStreamingAssets(string sourcePath, string destinationPath)
        {
            EnsureDataFolder(destinationPath);
            UnityWebRequest request = UnityWebRequest.Get(ToUnityWebRequestPath(sourcePath));
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                File.WriteAllText(destinationPath, request.downloadHandler.text);
            }
            else
            {
                Debug.LogError($"Unable to copy CSV from StreamingAssets: {request.error}");
            }

            request.Dispose();
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

        private static string GetStreamingPath(string fileName)
        {
            return Path.Combine(Application.streamingAssetsPath, "Data", fileName);
        }

        private static string GetPersistentPath(string fileName)
        {
            return Path.Combine(Application.persistentDataPath, "Data", fileName);
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
