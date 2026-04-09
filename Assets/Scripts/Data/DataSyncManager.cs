using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace CDE2501.Wayfinding.Data
{
    [Serializable]
    public class DriveFileEntry
    {
        public string fileName;
        public string driveFileId;
    }

    public class DataSyncManager : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] private bool showSyncUI = true;
        [SerializeField] private bool checkForUpdatesOnLaunch = true;
        [SerializeField] private float updateCheckIntervalHours = 1f;

        public bool IsSyncing { get; private set; }
        public bool SyncComplete { get; private set; }
        public bool SyncFailed { get; private set; }
        public string StatusMessage { get; private set; } = "Waiting...";
        public float Progress { get; private set; }
        public string ErrorMessage { get; private set; }

        public event Action OnSyncComplete;
        public event Action<string> OnSyncFailed;

        private static readonly DriveFileEntry[] RequiredFiles = new[]
        {
            new DriveFileEntry { fileName = "estate_graph.json", driveFileId = "13SCDOzZm8Lb4WNxeOuVVuBDMf0S3rHnS" },
            new DriveFileEntry { fileName = "nus_estate_graph.json", driveFileId = "1D2YZA-3sz9EkVCvbL0hC88ekKP0zGmfg" },
            new DriveFileEntry { fileName = "locations.json", driveFileId = "190AVpJtfGjr4JKy7a7iOE3QZi3M1_gWz" },
            new DriveFileEntry { fileName = "nus_locations.json", driveFileId = "15ZVPFQWAlEZNyknzfeSnbqqugyAP7gSm" },
            new DriveFileEntry { fileName = "routing_profiles.json", driveFileId = "1JyClQDAfaQM15bJnkTuv_lEcDhscKI8b" },
            new DriveFileEntry { fileName = "queenstown_boundary.geojson", driveFileId = "15AeynkQO8KImJ1MUpl48ErY991m1CRHe" },
            new DriveFileEntry { fileName = "nus_boundary.geojson", driveFileId = "1KmZ-_T51WME37eQOjZlx7o7tDbLVl5OF" },
        };

        private const string LastUpdateCheckKey = "DataSync_LastUpdateCheck";

        private GUIStyle _bgStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _statusStyle;
        private Texture2D _bgTex;
        private Texture2D _barBgTex;
        private Texture2D _barFillTex;

        private static string DataDir => Path.Combine(Application.persistentDataPath, "Data");

        private static string BuildDownloadUrl(string fileId)
        {
            return $"https://drive.usercontent.google.com/download?id={fileId}&export=download";
        }

        private void Start()
        {
            if (AllFilesExistLocally())
            {
                StatusMessage = "Data files found locally.";
                SyncComplete = true;
                OnSyncComplete?.Invoke();

                if (checkForUpdatesOnLaunch && ShouldCheckForUpdates())
                {
                    StartCoroutine(CheckForUpdatesRoutine());
                }
            }
            else
            {
                StartSync();
            }
        }

        public void StartSync()
        {
            if (IsSyncing) return;
            StartCoroutine(SyncRoutine(forceRedownload: false));
        }

        public void ForceReSync()
        {
            SyncComplete = false;
            SyncFailed = false;
            ErrorMessage = null;
            if (IsSyncing) return;
            StartCoroutine(SyncRoutine(forceRedownload: true));
        }

        private bool AllFilesExistLocally()
        {
            for (int i = 0; i < RequiredFiles.Length; i++)
            {
                string path = Path.Combine(DataDir, RequiredFiles[i].fileName);
                if (!File.Exists(path) || new FileInfo(path).Length == 0)
                {
                    return false;
                }
            }
            return true;
        }

        private bool ShouldCheckForUpdates()
        {
            string lastCheck = PlayerPrefs.GetString(LastUpdateCheckKey, "");
            if (string.IsNullOrWhiteSpace(lastCheck)) return true;

            if (DateTime.TryParse(lastCheck, out DateTime last))
            {
                return (DateTime.UtcNow - last).TotalHours >= updateCheckIntervalHours;
            }
            return true;
        }

        private IEnumerator CheckForUpdatesRoutine()
        {
            // Download each file and compare size with local version
            // If different, re-download
            int updated = 0;
            for (int i = 0; i < RequiredFiles.Length; i++)
            {
                DriveFileEntry entry = RequiredFiles[i];
                string localPath = Path.Combine(DataDir, entry.fileName);
                if (!File.Exists(localPath)) continue;

                long localSize = new FileInfo(localPath).Length;
                string url = BuildDownloadUrl(entry.driveFileId);

                using (UnityWebRequest request = UnityWebRequest.Head(url))
                {
                    request.timeout = 10;
                    yield return request.SendWebRequest();

                    if (request.result != UnityWebRequest.Result.Success) continue;

                    string contentLength = request.GetResponseHeader("Content-Length");
                    if (long.TryParse(contentLength, out long remoteSize) && remoteSize != localSize)
                    {
                        // File changed on Drive — re-download
                        yield return DownloadFile(entry, i, RequiredFiles.Length);
                        updated++;
                    }
                }
            }

            PlayerPrefs.SetString(LastUpdateCheckKey, DateTime.UtcNow.ToString("o"));
            PlayerPrefs.Save();

            if (updated > 0)
            {
                Debug.Log($"[DataSyncManager] Updated {updated} files from Drive.");
            }
        }

        private IEnumerator SyncRoutine(bool forceRedownload)
        {
            IsSyncing = true;
            SyncFailed = false;
            SyncComplete = false;
            ErrorMessage = null;
            Progress = 0f;

            if (!Directory.Exists(DataDir))
            {
                Directory.CreateDirectory(DataDir);
            }

            int totalFiles = RequiredFiles.Length;
            int completed = 0;

            for (int i = 0; i < totalFiles; i++)
            {
                DriveFileEntry entry = RequiredFiles[i];
                string localPath = Path.Combine(DataDir, entry.fileName);

                if (!forceRedownload && File.Exists(localPath) && new FileInfo(localPath).Length > 0)
                {
                    completed++;
                    Progress = (float)completed / totalFiles;
                    StatusMessage = $"Cached: {entry.fileName} ({completed}/{totalFiles})";
                    continue;
                }

                StatusMessage = $"Downloading: {entry.fileName} ({completed + 1}/{totalFiles})";

                bool success = false;
                yield return DownloadFileWithResult(entry, (s) => success = s);

                if (!success)
                {
                    string error = $"Failed to download {entry.fileName}";
                    ErrorMessage = error;
                    StatusMessage = error;
                    SyncFailed = true;
                    IsSyncing = false;
                    OnSyncFailed?.Invoke(error);
                    yield break;
                }

                completed++;
                Progress = (float)completed / totalFiles;
                StatusMessage = $"Downloaded: {entry.fileName} ({completed}/{totalFiles})";
            }

            PlayerPrefs.SetString(LastUpdateCheckKey, DateTime.UtcNow.ToString("o"));
            PlayerPrefs.Save();

            IsSyncing = false;
            SyncComplete = true;
            Progress = 1f;
            StatusMessage = $"All {totalFiles} files ready.";
            Debug.Log($"[DataSyncManager] Sync complete. {totalFiles} files in {DataDir}");
            OnSyncComplete?.Invoke();
        }

        private IEnumerator DownloadFile(DriveFileEntry entry, int index, int total)
        {
            string localPath = Path.Combine(DataDir, entry.fileName);
            string url = BuildDownloadUrl(entry.driveFileId);

            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                request.timeout = 30;
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        File.WriteAllBytes(localPath, request.downloadHandler.data);
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[DataSyncManager] Failed to save {entry.fileName}: {e.Message}");
                    }
                }
            }
        }

        private IEnumerator DownloadFileWithResult(DriveFileEntry entry, Action<bool> result)
        {
            string localPath = Path.Combine(DataDir, entry.fileName);
            string url = BuildDownloadUrl(entry.driveFileId);

            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                request.timeout = 30;
                UnityWebRequestAsyncOperation op = request.SendWebRequest();

                while (!op.isDone)
                {
                    yield return null;
                }

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"[DataSyncManager] Download failed {entry.fileName}: {request.error}");
                    result?.Invoke(false);
                    yield break;
                }

                try
                {
                    File.WriteAllBytes(localPath, request.downloadHandler.data);
                    result?.Invoke(true);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[DataSyncManager] Save failed {entry.fileName}: {e.Message}");
                    result?.Invoke(false);
                }
            }
        }

        /// <summary>
        /// Share the telemetry/recordings folder via Android share intent.
        /// Call this from UI to let testers send data via Drive/Telegram/email.
        /// </summary>
        public void ShareTelemetryData()
        {
            string telemetryDir = Path.Combine(Application.persistentDataPath, "Telemetry");
            string pathsDir = Path.Combine(Application.persistentDataPath, "RecordedPaths");
            string crashDir = Path.Combine(Application.persistentDataPath, "Crashes");

            // Collect all shareable files
            var files = new List<string>();
            if (Directory.Exists(telemetryDir))
            {
                files.AddRange(Directory.GetFiles(telemetryDir, "*.csv", SearchOption.AllDirectories));
            }
            if (Directory.Exists(pathsDir))
            {
                files.AddRange(Directory.GetFiles(pathsDir, "*.json", SearchOption.AllDirectories));
            }
            if (Directory.Exists(crashDir))
            {
                files.AddRange(Directory.GetFiles(crashDir, "*.txt", SearchOption.AllDirectories));
            }

            if (files.Count == 0)
            {
                StatusMessage = "No data to share yet.";
                return;
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            ShareOnAndroid(files);
#else
            Debug.Log($"[DataSyncManager] Share: {files.Count} files ready at {Application.persistentDataPath}");
            StatusMessage = $"{files.Count} files ready to share. Check persistent data path.";
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private static void ShareOnAndroid(List<string> filePaths)
        {
            try
            {
                using (AndroidJavaClass intentClass = new AndroidJavaClass("android.content.Intent"))
                using (AndroidJavaObject intent = new AndroidJavaObject("android.content.Intent"))
                {
                    intent.Call<AndroidJavaObject>("setAction", "android.intent.action.SEND_MULTIPLE");
                    intent.Call<AndroidJavaObject>("setType", "*/*");

                    using (AndroidJavaClass uriClass = new AndroidJavaClass("android.net.Uri"))
                    using (AndroidJavaObject arrayList = new AndroidJavaObject("java.util.ArrayList"))
                    {
                        using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                        using (AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                        using (AndroidJavaObject context = activity.Call<AndroidJavaObject>("getApplicationContext"))
                        {
                            string authority = context.Call<AndroidJavaObject>("getPackageName").Call<string>("toString") + ".fileprovider";

                            using (AndroidJavaClass fileProviderClass = new AndroidJavaClass("androidx.core.content.FileProvider"))
                            {
                                foreach (string path in filePaths)
                                {
                                    using (AndroidJavaObject file = new AndroidJavaObject("java.io.File", path))
                                    {
                                        AndroidJavaObject uri = fileProviderClass.CallStatic<AndroidJavaObject>("getUriForFile", context, authority, file);
                                        arrayList.Call<bool>("add", uri);
                                    }
                                }
                            }

                            intent.Call<AndroidJavaObject>("putParcelableArrayListExtra", "android.intent.extra.STREAM", arrayList);
                            intent.Call<AndroidJavaObject>("addFlags", 1); // FLAG_GRANT_READ_URI_PERMISSION

                            AndroidJavaObject chooser = intentClass.CallStatic<AndroidJavaObject>("createChooser", intent, "Share telemetry data");
                            activity.Call("startActivity", chooser);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[DataSyncManager] Android share failed: {e.Message}");
            }
        }
#endif

        private void OnGUI()
        {
            if (!showSyncUI) return;
            if (SyncComplete && !SyncFailed) return;

            EnsureStyles();

            float w = Mathf.Min(500f, Screen.width * 0.8f);
            float h = SyncFailed ? 180f : 140f;
            float x = (Screen.width - w) * 0.5f;
            float y = (Screen.height - h) * 0.5f;
            Rect panel = new Rect(x, y, w, h);

            GUI.Box(panel, GUIContent.none, _bgStyle);
            GUI.Label(new Rect(x + 20f, y + 16f, w - 40f, 30f), "CDE2501 AR Wayfinding", _labelStyle);
            GUI.Label(new Rect(x + 20f, y + 50f, w - 40f, 24f), StatusMessage, _statusStyle);

            Rect barBg = new Rect(x + 20f, y + 80f, w - 40f, 18f);
            GUI.DrawTexture(barBg, _barBgTex);
            if (Progress > 0f)
            {
                Rect barFill = new Rect(barBg.x + 2f, barBg.y + 2f, (barBg.width - 4f) * Mathf.Clamp01(Progress), barBg.height - 4f);
                GUI.DrawTexture(barFill, _barFillTex);
            }

            string pctText = $"{Mathf.RoundToInt(Progress * 100f)}%";
            GUI.Label(new Rect(x + 20f, y + 100f, w - 40f, 20f), pctText, _statusStyle);

            if (SyncFailed)
            {
                if (GUI.Button(new Rect(x + w * 0.5f - 60f, y + h - 40f, 120f, 30f), "Retry"))
                {
                    ForceReSync();
                }
            }
        }

        private void EnsureStyles()
        {
            if (_bgStyle != null) return;

            _bgTex = MakeTex(new Color(0.05f, 0.08f, 0.12f, 0.95f));
            _barBgTex = MakeTex(new Color(0.2f, 0.2f, 0.25f, 1f));
            _barFillTex = MakeTex(new Color(0.05f, 0.65f, 0.91f, 1f));

            _bgStyle = new GUIStyle(GUI.skin.box)
            {
                normal = { background = _bgTex },
                border = new RectOffset(2, 2, 2, 2)
            };

            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 22,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };

            _statusStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.7f, 0.8f, 0.9f, 1f) }
            };
        }

        private static Texture2D MakeTex(Color color)
        {
            Texture2D tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, color);
            tex.Apply();
            return tex;
        }

        private void OnDestroy()
        {
            if (_bgTex != null) Destroy(_bgTex);
            if (_barBgTex != null) Destroy(_barBgTex);
            if (_barFillTex != null) Destroy(_barFillTex);
        }
    }
}
