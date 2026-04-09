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

    [Serializable]
    public class DriveManifest
    {
        public List<DriveFileEntry> files = new List<DriveFileEntry>();
    }

    public class DataSyncManager : MonoBehaviour
    {
        [Header("Status")]
        [SerializeField] private bool showSyncUI = true;

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

        private GUIStyle _bgStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _statusStyle;
        private GUIStyle _buttonStyle;
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
            }
            else
            {
                StartSync();
            }
        }

        public void StartSync()
        {
            if (IsSyncing) return;
            StartCoroutine(SyncRoutine());
        }

        public void ForceReSync()
        {
            SyncComplete = false;
            SyncFailed = false;
            ErrorMessage = null;
            StartSync();
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

        private IEnumerator SyncRoutine()
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

                // Skip if file already exists and has content
                if (File.Exists(localPath) && new FileInfo(localPath).Length > 0)
                {
                    completed++;
                    Progress = (float)completed / totalFiles;
                    StatusMessage = $"Cached: {entry.fileName} ({completed}/{totalFiles})";
                    continue;
                }

                StatusMessage = $"Downloading: {entry.fileName} ({completed + 1}/{totalFiles})";
                string url = BuildDownloadUrl(entry.driveFileId);

                using (UnityWebRequest request = UnityWebRequest.Get(url))
                {
                    request.timeout = 30;
                    UnityWebRequestAsyncOperation op = request.SendWebRequest();

                    while (!op.isDone)
                    {
                        float fileProgress = op.progress;
                        Progress = (completed + fileProgress) / totalFiles;
                        yield return null;
                    }

                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        string error = $"Failed to download {entry.fileName}: {request.error}";
                        Debug.LogError($"[DataSyncManager] {error}");
                        ErrorMessage = error;
                        StatusMessage = error;
                        SyncFailed = true;
                        IsSyncing = false;
                        OnSyncFailed?.Invoke(error);
                        yield break;
                    }

                    try
                    {
                        File.WriteAllBytes(localPath, request.downloadHandler.data);
                    }
                    catch (Exception e)
                    {
                        string error = $"Failed to save {entry.fileName}: {e.Message}";
                        Debug.LogError($"[DataSyncManager] {error}");
                        ErrorMessage = error;
                        StatusMessage = error;
                        SyncFailed = true;
                        IsSyncing = false;
                        OnSyncFailed?.Invoke(error);
                        yield break;
                    }
                }

                completed++;
                Progress = (float)completed / totalFiles;
                StatusMessage = $"Downloaded: {entry.fileName} ({completed}/{totalFiles})";
            }

            IsSyncing = false;
            SyncComplete = true;
            Progress = 1f;
            StatusMessage = $"All {totalFiles} files ready.";
            Debug.Log($"[DataSyncManager] Sync complete. {totalFiles} files in {DataDir}");
            OnSyncComplete?.Invoke();
        }

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

            // Progress bar
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

            _buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 16
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
