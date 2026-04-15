using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace CDE2501.Wayfinding.Data
{
    [Serializable]
    public class DriveFileEntry
    {
        public string fileName;
        public string relativePath;
        public string driveFileId;
        public bool requiredOnColdStart = true;
        public bool allowRuntimeUpdates = true;
    }

    public class DataSyncManager : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] private bool showSyncUI = true;
        [SerializeField] private bool checkForUpdatesOnLaunch = true;
        [SerializeField] private bool runBackgroundUpdateChecks = true;
        [SerializeField] private bool downloadOptionalFilesOnInitialSync = true;
        [SerializeField] private float updateCheckIntervalHours = 0.25f;

        public bool IsSyncing { get; private set; }
        public bool SyncComplete { get; private set; }
        public bool SyncFailed { get; private set; }
        public bool IsCheckingForUpdates => _isCheckingForUpdates;
        public string StatusMessage { get; private set; } = "Waiting...";
        public float Progress { get; private set; }
        public string ErrorMessage { get; private set; }

        public event Action OnSyncComplete;
        public event Action<string> OnSyncFailed;
        public event Action<IReadOnlyList<string>> OnFilesUpdated;

        // Files served from synced project: CDE2501-AR-Wayfinding/Assets/StreamingAssets/Data/archived/
        // Includes both required startup files and optional runtime assets that can be updated from Drive.
        private static readonly DriveFileEntry[] SyncFiles = new[]
        {
            // Required runtime files
            new DriveFileEntry { fileName = "estate_graph.json", driveFileId = "1rdVh89zKpehzd1_pjbiO15xQluxg-S3B", requiredOnColdStart = true, allowRuntimeUpdates = true },
            new DriveFileEntry { fileName = "nus_estate_graph.json", driveFileId = "19mJFjc_52qA30apecosIkI4bEit6Jff5", requiredOnColdStart = true, allowRuntimeUpdates = true },
            new DriveFileEntry { fileName = "locations.json", driveFileId = "1iudrdcjUA4axr7OlbVNtlX3sHB0351Ru", requiredOnColdStart = true, allowRuntimeUpdates = true },
            new DriveFileEntry { fileName = "nus_locations.json", driveFileId = "1iw1X8HGkigw5P0K5w08dXMFcQ1Bx1Gv9", requiredOnColdStart = true, allowRuntimeUpdates = true },
            new DriveFileEntry { fileName = "routing_profiles.json", driveFileId = "1BgyDOE4ts3V-o5Na4NzfSJic7BZX5HiZ", requiredOnColdStart = true, allowRuntimeUpdates = true },
            new DriveFileEntry { fileName = "queenstown_boundary.geojson", driveFileId = "1gXoUXctD0tI-T8mrXIZ5Eeo3h3Mz9PL0", requiredOnColdStart = true, allowRuntimeUpdates = true },
            new DriveFileEntry { fileName = "nus_boundary.geojson", driveFileId = "1LZWYApt484SDCGqcK8Q4xXj2cD0AOFMx", requiredOnColdStart = true, allowRuntimeUpdates = true },

            // Optional map imagery + metadata (updatable without APK reinstall)
            new DriveFileEntry { fileName = "queenstown_map_z18_x206656-206662_y130126-130132.png", driveFileId = "1vY-sAY43iBrE4wmyP7Bkxn7Tb1uYz3Ea", requiredOnColdStart = false, allowRuntimeUpdates = true },
            new DriveFileEntry { fileName = "queenstown_map_z18_x206656-206662_y130127-130133.png", driveFileId = "1BZZwuJV2RKTzbroUEW0IeGfQwtbQXuL8", requiredOnColdStart = false, allowRuntimeUpdates = true },
            new DriveFileEntry { fileName = "queenstown_map_z19_x413313-413325_y260253-260265.png", driveFileId = "1EYasNmW6WiUUwFjnGbl4qNCkVDk1qaOH", requiredOnColdStart = false, allowRuntimeUpdates = true },
            new DriveFileEntry { fileName = "queenstown_map_z19_x413314-413324_y260255-260265.png", driveFileId = "1dcrDHXB_ruCWY7SKWb7IHm1hwl4FnzuT", requiredOnColdStart = false, allowRuntimeUpdates = true },
            new DriveFileEntry { fileName = "nus_map_z18_x206633-206638_y130123-130128.png", driveFileId = "1Y5vU2YP9AgDrg0TGoIlIfuTm0XNEkfcB", requiredOnColdStart = false, allowRuntimeUpdates = true },
            new DriveFileEntry { fileName = "nus_map_z19_x413268-413276_y260247-260255.png", driveFileId = "1mpDJrEsiRk3Q1Gj2PXAICTIyd3JPF2iP", requiredOnColdStart = false, allowRuntimeUpdates = true },
            new DriveFileEntry { fileName = "map_tile_z16_x51664_y32532.png", driveFileId = "17S8b4j3iqzt5IaqUJcHljkszxvXNDFaS", requiredOnColdStart = false, allowRuntimeUpdates = true },
            new DriveFileEntry { fileName = "queenstown_map_z18_x206656-206662_y130126-130132.json", driveFileId = "1K3UINMjNJ8Cb03Mc0hi6Yx-7S05wdKgk", requiredOnColdStart = false, allowRuntimeUpdates = true },
            new DriveFileEntry { fileName = "queenstown_map_z18_x206656-206662_y130127-130133.json", driveFileId = "1LzIhipHsujcvKGRG87ndb4j1q7gc1CD9", requiredOnColdStart = false, allowRuntimeUpdates = true },
            new DriveFileEntry { fileName = "queenstown_map_z19_x413313-413325_y260253-260265.json", driveFileId = "14QtV7eunUqtZFqCUpdSeWeuSYpnH2Lxt", requiredOnColdStart = false, allowRuntimeUpdates = true },
            new DriveFileEntry { fileName = "queenstown_map_z19_x413314-413324_y260255-260265.json", driveFileId = "13JF85GdvNKmV8xV1wooX5KEBdft6lddr", requiredOnColdStart = false, allowRuntimeUpdates = true },
            new DriveFileEntry { fileName = "nus_map_z18_x206633-206638_y130123-130128.json", driveFileId = "1C1gu4-XPbjV4Df_eGHy7N39ixUahOD-_", requiredOnColdStart = false, allowRuntimeUpdates = true },
            new DriveFileEntry { fileName = "nus_map_z19_x413268-413276_y260247-260255.json", driveFileId = "1goLvAiENbnZthOT3qoVpkTTRTb3V1OsC", requiredOnColdStart = false, allowRuntimeUpdates = true },
        };

        private const string LastUpdateCheckKey = "DataSync_LastUpdateCheck";
        private const string RemoteMetaKeyPrefix = "DataSync_RemoteMeta_";

        private GUIStyle _bgStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _statusStyle;
        private Texture2D _bgTex;
        private Texture2D _barBgTex;
        private Texture2D _barFillTex;
        private Coroutine _backgroundUpdateRoutine;
        private bool _isCheckingForUpdates;

        private struct RemoteProbeResult
        {
            public bool success;
            public string fingerprint;
            public string lastModifiedRaw;
            public long remoteSizeBytes;
            public string error;
        }

        private static string DataDir => Path.Combine(Application.persistentDataPath, "Data");

        private static bool IsRequired(DriveFileEntry entry)
        {
            return entry != null && entry.requiredOnColdStart;
        }

        private static bool IsRuntimeUpdatable(DriveFileEntry entry)
        {
            return entry != null && entry.allowRuntimeUpdates;
        }

        private static string GetRelativePath(DriveFileEntry entry)
        {
            if (entry == null)
            {
                return string.Empty;
            }

            string raw = !string.IsNullOrWhiteSpace(entry.relativePath) ? entry.relativePath : entry.fileName;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return string.Empty;
            }

            return raw.Trim().Replace('\\', '/');
        }

        private static string GetDisplayName(DriveFileEntry entry)
        {
            string relativePath = GetRelativePath(entry);
            return string.IsNullOrWhiteSpace(relativePath) ? "<unknown>" : relativePath;
        }

        private static string GetLocalPath(DriveFileEntry entry)
        {
            string relativePath = GetRelativePath(entry);
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                return string.Empty;
            }

            string normalizedRelative = relativePath.Replace('/', Path.DirectorySeparatorChar);
            return Path.Combine(DataDir, normalizedRelative);
        }

        private static string BuildDownloadUrl(string fileId)
        {
            return $"https://drive.usercontent.google.com/download?id={fileId}&export=download";
        }

        private static string BuildRemoteMetaKey(DriveFileEntry entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.driveFileId))
            {
                return string.Empty;
            }

            return RemoteMetaKeyPrefix + entry.driveFileId.Trim();
        }

        private static string BuildRemoteFingerprint(string contentLength, string lastModified, string etag)
        {
            string lengthPart = string.IsNullOrWhiteSpace(contentLength) ? string.Empty : contentLength.Trim();
            string modifiedPart = string.IsNullOrWhiteSpace(lastModified) ? string.Empty : lastModified.Trim();
            string etagPart = string.IsNullOrWhiteSpace(etag) ? string.Empty : etag.Trim();

            if (string.IsNullOrWhiteSpace(lengthPart) &&
                string.IsNullOrWhiteSpace(modifiedPart) &&
                string.IsNullOrWhiteSpace(etagPart))
            {
                return string.Empty;
            }

            return $"{lengthPart}|{modifiedPart}|{etagPart}";
        }

        private static bool TryParseHttpDateUtc(string raw, out DateTime utcDateTime)
        {
            utcDateTime = default;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            return DateTime.TryParse(
                raw.Trim(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                out utcDateTime);
        }

        private static string BuildRemoteFingerprintFromRequest(UnityWebRequest request, int fallbackByteCount = -1)
        {
            if (request == null)
            {
                return string.Empty;
            }

            string contentLength = request.GetResponseHeader("Content-Length");
            if (string.IsNullOrWhiteSpace(contentLength) && fallbackByteCount >= 0)
            {
                contentLength = fallbackByteCount.ToString();
            }

            string lastModified = request.GetResponseHeader("Last-Modified");
            string etag = request.GetResponseHeader("ETag");
            return BuildRemoteFingerprint(contentLength, lastModified, etag);
        }

        private static string GetCachedRemoteFingerprint(DriveFileEntry entry)
        {
            string key = BuildRemoteMetaKey(entry);
            if (string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            return PlayerPrefs.GetString(key, string.Empty);
        }

        private static void CacheRemoteFingerprint(DriveFileEntry entry, string fingerprint)
        {
            if (string.IsNullOrWhiteSpace(fingerprint))
            {
                return;
            }

            string key = BuildRemoteMetaKey(entry);
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            PlayerPrefs.SetString(key, fingerprint);
        }

        private static bool TryExtractTotalBytesFromContentRange(string contentRange, out long totalBytes)
        {
            totalBytes = -1;
            if (string.IsNullOrWhiteSpace(contentRange))
            {
                return false;
            }

            int slashIndex = contentRange.LastIndexOf('/');
            if (slashIndex < 0 || slashIndex + 1 >= contentRange.Length)
            {
                return false;
            }

            string totalPart = contentRange.Substring(slashIndex + 1).Trim();
            if (string.IsNullOrWhiteSpace(totalPart) || string.Equals(totalPart, "*", StringComparison.Ordinal))
            {
                return false;
            }

            return long.TryParse(totalPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out totalBytes);
        }

        private static long GetRemoteSizeFromResponse(UnityWebRequest request, int fallbackByteCount = -1)
        {
            if (request == null)
            {
                return -1;
            }

            if (TryExtractTotalBytesFromContentRange(request.GetResponseHeader("Content-Range"), out long rangedTotalBytes))
            {
                return rangedTotalBytes;
            }

            string contentLengthRaw = request.GetResponseHeader("Content-Length");
            if (long.TryParse(contentLengthRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long contentLength))
            {
                return contentLength;
            }

            return fallbackByteCount >= 0 ? fallbackByteCount : -1;
        }

        private IEnumerator ProbeRemoteFile(DriveFileEntry entry, Action<RemoteProbeResult> onComplete)
        {
            var result = new RemoteProbeResult
            {
                success = false,
                fingerprint = string.Empty,
                lastModifiedRaw = string.Empty,
                remoteSizeBytes = -1,
                error = "unknown error"
            };

            if (entry == null || string.IsNullOrWhiteSpace(entry.driveFileId))
            {
                result.error = "invalid drive file entry";
                onComplete?.Invoke(result);
                yield break;
            }

            string url = BuildDownloadUrl(entry.driveFileId);
            using (UnityWebRequest headRequest = UnityWebRequest.Head(url))
            {
                headRequest.timeout = 10;
                yield return headRequest.SendWebRequest();

                if (headRequest.result == UnityWebRequest.Result.Success)
                {
                    result.success = true;
                    result.fingerprint = BuildRemoteFingerprintFromRequest(headRequest);
                    result.lastModifiedRaw = headRequest.GetResponseHeader("Last-Modified");
                    result.remoteSizeBytes = GetRemoteSizeFromResponse(headRequest);
                    result.error = string.Empty;
                    onComplete?.Invoke(result);
                    yield break;
                }

                result.error = headRequest.error;
            }

            // Some Drive/CDN paths reject HEAD. Fallback to a ranged GET probe.
            using (UnityWebRequest getProbeRequest = UnityWebRequest.Get(url))
            {
                getProbeRequest.timeout = 15;
                getProbeRequest.SetRequestHeader("Range", "bytes=0-0");
                yield return getProbeRequest.SendWebRequest();

                if (getProbeRequest.result == UnityWebRequest.Result.Success)
                {
                    byte[] probeBytes = getProbeRequest.downloadHandler != null
                        ? getProbeRequest.downloadHandler.data
                        : null;
                    int probeByteCount = probeBytes != null ? probeBytes.Length : -1;

                    result.success = true;
                    result.fingerprint = BuildRemoteFingerprintFromRequest(getProbeRequest, probeByteCount);
                    result.lastModifiedRaw = getProbeRequest.GetResponseHeader("Last-Modified");
                    result.remoteSizeBytes = GetRemoteSizeFromResponse(getProbeRequest, probeByteCount);
                    result.error = string.Empty;
                }
                else
                {
                    result.error = getProbeRequest.error;
                }
            }

            onComplete?.Invoke(result);
        }

        private static bool IsLikelyHtmlPayload(byte[] data)
        {
            if (data == null || data.Length == 0) return false;

            int probeLength = Mathf.Min(data.Length, 256);
            string prefix = Encoding.UTF8.GetString(data, 0, probeLength).TrimStart().ToLowerInvariant();
            return prefix.StartsWith("<!doctype") || prefix.StartsWith("<html");
        }

        private void EnsureDataDirectory()
        {
            if (!Directory.Exists(DataDir))
            {
                Directory.CreateDirectory(DataDir);
            }
        }

        private void Start()
        {
            EnsureDataDirectory();

            if (AllRequiredFilesExistLocally())
            {
                StatusMessage = "Data files found locally.";
                SyncComplete = true;
                OnSyncComplete?.Invoke();

                if (checkForUpdatesOnLaunch && ShouldCheckForUpdates())
                {
                    StartCoroutine(CheckForUpdatesRoutine());
                }

                StartBackgroundUpdateChecksIfNeeded();
            }
            else
            {
                StartSync();
            }
        }

        private void StartBackgroundUpdateChecksIfNeeded()
        {
            if (!runBackgroundUpdateChecks || _backgroundUpdateRoutine != null)
            {
                return;
            }

            _backgroundUpdateRoutine = StartCoroutine(BackgroundUpdateLoopRoutine());
        }

        private IEnumerator BackgroundUpdateLoopRoutine()
        {
            while (true)
            {
                float intervalSeconds = Mathf.Max(60f, updateCheckIntervalHours * 3600f);
                yield return new WaitForSecondsRealtime(intervalSeconds);

                if (!SyncComplete || SyncFailed || IsSyncing || _isCheckingForUpdates)
                {
                    continue;
                }

                yield return CheckForUpdatesRoutine(ignoreSchedule: true);
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

        public void CheckForUpdatesNow()
        {
            if (IsSyncing || _isCheckingForUpdates)
            {
                return;
            }

            StartCoroutine(CheckForUpdatesRoutine(ignoreSchedule: true));
        }

        private bool AllRequiredFilesExistLocally()
        {
            for (int i = 0; i < SyncFiles.Length; i++)
            {
                DriveFileEntry entry = SyncFiles[i];
                if (!IsRequired(entry))
                {
                    continue;
                }

                string path = GetLocalPath(entry);
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

        private IEnumerator CheckForUpdatesRoutine(bool ignoreSchedule = false)
        {
            if (_isCheckingForUpdates || IsSyncing)
            {
                yield break;
            }

            if (!ignoreSchedule && !ShouldCheckForUpdates())
            {
                yield break;
            }

            _isCheckingForUpdates = true;
            StatusMessage = "Checking Drive updates...";
            var updatedFiles = new List<string>();
            bool hadCheckError = false;

            for (int i = 0; i < SyncFiles.Length; i++)
            {
                DriveFileEntry entry = SyncFiles[i];
                if (!IsRuntimeUpdatable(entry))
                {
                    continue;
                }

                string localPath = GetLocalPath(entry);
                if (string.IsNullOrWhiteSpace(localPath))
                {
                    continue;
                }

                bool shouldDownload = !File.Exists(localPath) || new FileInfo(localPath).Length == 0;
                long localSize = shouldDownload ? 0 : new FileInfo(localPath).Length;

                if (!shouldDownload)
                {
                    RemoteProbeResult probeResult = default;
                    bool probeCompleted = false;
                    yield return ProbeRemoteFile(entry, (result) =>
                    {
                        probeResult = result;
                        probeCompleted = true;
                    });

                    if (probeCompleted && probeResult.success)
                    {
                        string remoteFingerprint = probeResult.fingerprint;
                        string cachedFingerprint = GetCachedRemoteFingerprint(entry);

                        if (!string.IsNullOrWhiteSpace(remoteFingerprint))
                        {
                            if (string.IsNullOrWhiteSpace(cachedFingerprint))
                            {
                                // Bootstrap for installs from old versions: compare remote Last-Modified
                                // to the local file timestamp before deciding to redownload.
                                string remoteModifiedRaw = probeResult.lastModifiedRaw;
                                DateTime localWriteUtc = File.GetLastWriteTimeUtc(localPath);
                                if (TryParseHttpDateUtc(remoteModifiedRaw, out DateTime remoteModifiedUtc))
                                {
                                    shouldDownload = remoteModifiedUtc > localWriteUtc.AddSeconds(1);
                                    if (!shouldDownload)
                                    {
                                        CacheRemoteFingerprint(entry, remoteFingerprint);
                                    }
                                }
                                else
                                {
                                    // If we cannot parse Last-Modified, force one download to establish baseline.
                                    shouldDownload = true;
                                }
                            }
                            else if (!string.Equals(cachedFingerprint, remoteFingerprint, StringComparison.Ordinal))
                            {
                                shouldDownload = true;
                            }
                            else
                            {
                                // Keep metadata fresh in prefs when unchanged.
                                CacheRemoteFingerprint(entry, remoteFingerprint);
                            }
                        }
                        else
                        {
                            long remoteSize = probeResult.remoteSizeBytes;
                            if (remoteSize >= 0 && remoteSize != localSize)
                            {
                                shouldDownload = true;
                            }
                        }
                    }
                    else
                    {
                        hadCheckError = true;
                        string probeError = probeCompleted && !string.IsNullOrWhiteSpace(probeResult.error)
                            ? probeResult.error
                            : "probe did not complete";
                        Debug.LogWarning($"[DataSyncManager] Update check failed for {GetDisplayName(entry)}: {probeError}");
                    }
                }

                if (!shouldDownload)
                {
                    continue;
                }

                bool success = false;
                yield return DownloadFileWithResult(entry, (s) => success = s);
                if (success)
                {
                    updatedFiles.Add(GetRelativePath(entry));
                }
                else
                {
                    hadCheckError = true;
                }
            }

            if (!hadCheckError)
            {
                PlayerPrefs.SetString(LastUpdateCheckKey, DateTime.UtcNow.ToString("o"));
            }
            PlayerPrefs.Save();
            _isCheckingForUpdates = false;

            if (updatedFiles.Count > 0)
            {
                StatusMessage = $"Drive sync updated {updatedFiles.Count} file(s).";
                Debug.Log($"[DataSyncManager] Updated {updatedFiles.Count} files from Drive.");
                OnFilesUpdated?.Invoke(updatedFiles);
            }
            else if (hadCheckError)
            {
                StatusMessage = "Drive update check incomplete. Will retry.";
                Debug.LogWarning("[DataSyncManager] One or more runtime update checks failed; retaining previous schedule marker.");
            }
            else
            {
                StatusMessage = "Drive data is up to date.";
                Debug.Log("[DataSyncManager] No runtime data updates detected.");
            }
        }

        private List<DriveFileEntry> BuildSyncTargets(bool forceRedownload)
        {
            var targets = new List<DriveFileEntry>(SyncFiles.Length);
            for (int i = 0; i < SyncFiles.Length; i++)
            {
                DriveFileEntry entry = SyncFiles[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.driveFileId) || string.IsNullOrWhiteSpace(GetRelativePath(entry)))
                {
                    continue;
                }

                bool include = forceRedownload || IsRequired(entry) || downloadOptionalFilesOnInitialSync;
                if (include)
                {
                    targets.Add(entry);
                }
            }

            return targets;
        }

        private IEnumerator SyncRoutine(bool forceRedownload)
        {
            IsSyncing = true;
            SyncFailed = false;
            SyncComplete = false;
            ErrorMessage = null;
            Progress = 0f;

            EnsureDataDirectory();

            List<DriveFileEntry> targets = BuildSyncTargets(forceRedownload);
            int totalFiles = Mathf.Max(1, targets.Count);
            int completed = 0;
            var downloadedFiles = new List<string>();

            for (int i = 0; i < targets.Count; i++)
            {
                DriveFileEntry entry = targets[i];
                string localPath = GetLocalPath(entry);
                string displayName = GetDisplayName(entry);

                if (string.IsNullOrWhiteSpace(localPath))
                {
                    completed++;
                    Progress = (float)completed / totalFiles;
                    continue;
                }

                bool hasValidLocalFile = File.Exists(localPath) && new FileInfo(localPath).Length > 0;
                if (!forceRedownload && hasValidLocalFile)
                {
                    completed++;
                    Progress = (float)completed / totalFiles;
                    StatusMessage = $"Cached: {displayName} ({completed}/{totalFiles})";
                    continue;
                }

                StatusMessage = $"Downloading: {displayName} ({completed + 1}/{totalFiles})";

                bool success = false;
                yield return DownloadFileWithResult(entry, (s) => success = s);

                if (!success)
                {
                    if (IsRequired(entry))
                    {
                        string error = $"Failed to download {displayName}";
                        ErrorMessage = error;
                        StatusMessage = error;
                        SyncFailed = true;
                        IsSyncing = false;
                        OnSyncFailed?.Invoke(error);
                        yield break;
                    }

                    Debug.LogWarning($"[DataSyncManager] Optional file download failed: {displayName}");
                    completed++;
                    Progress = (float)completed / totalFiles;
                    continue;
                }

                downloadedFiles.Add(GetRelativePath(entry));
                completed++;
                Progress = (float)completed / totalFiles;
                StatusMessage = $"Downloaded: {displayName} ({completed}/{totalFiles})";
            }

            PlayerPrefs.SetString(LastUpdateCheckKey, DateTime.UtcNow.ToString("o"));
            PlayerPrefs.Save();

            IsSyncing = false;
            SyncComplete = true;
            Progress = 1f;
            StatusMessage = $"All required data files are ready.";
            Debug.Log($"[DataSyncManager] Sync complete. {targets.Count} target files in {DataDir}");
            OnSyncComplete?.Invoke();

            if (downloadedFiles.Count > 0)
            {
                OnFilesUpdated?.Invoke(downloadedFiles);
            }

            StartBackgroundUpdateChecksIfNeeded();
        }

        private IEnumerator DownloadFileWithResult(DriveFileEntry entry, Action<bool> result)
        {
            string localPath = GetLocalPath(entry);
            if (string.IsNullOrWhiteSpace(localPath))
            {
                result?.Invoke(false);
                yield break;
            }

            string folder = Path.GetDirectoryName(localPath);
            if (!string.IsNullOrWhiteSpace(folder) && !Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            string url = BuildDownloadUrl(entry.driveFileId);

            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                request.timeout = 30;
                UnityWebRequestAsyncOperation op = request.SendWebRequest();
                string tempPath = localPath + ".tmp";

                while (!op.isDone)
                {
                    yield return null;
                }

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"[DataSyncManager] Download failed {GetDisplayName(entry)}: {request.error}");
                    result?.Invoke(false);
                    yield break;
                }

                try
                {
                    byte[] bytes = request.downloadHandler.data;
                    if (bytes == null || bytes.Length == 0)
                    {
                        Debug.LogError($"[DataSyncManager] Download returned empty payload for {GetDisplayName(entry)}");
                        result?.Invoke(false);
                        yield break;
                    }

                    // Guard against Drive auth/permission pages being saved as JSON.
                    if (IsLikelyHtmlPayload(bytes))
                    {
                        Debug.LogError($"[DataSyncManager] Download for {GetDisplayName(entry)} returned HTML. Verify file sharing is 'Anyone with link -> Viewer' for Drive file ID {entry.driveFileId}.");
                        result?.Invoke(false);
                        yield break;
                    }

                    string remoteFingerprint = BuildRemoteFingerprintFromRequest(request, bytes.Length);
                    if (!string.IsNullOrWhiteSpace(remoteFingerprint))
                    {
                        CacheRemoteFingerprint(entry, remoteFingerprint);
                    }

                    File.WriteAllBytes(tempPath, bytes);
                    if (File.Exists(localPath))
                    {
                        File.Delete(localPath);
                    }
                    File.Move(tempPath, localPath);
                    result?.Invoke(true);
                }
                catch (Exception e)
                {
                    try
                    {
                        if (File.Exists(tempPath))
                        {
                            File.Delete(tempPath);
                        }
                    }
                    catch
                    {
                        // Ignore cleanup errors after a failed save.
                    }

                    Debug.LogError($"[DataSyncManager] Save failed {GetDisplayName(entry)}: {e.Message}");
                    result?.Invoke(false);
                }
            }
        }

        /// <summary>
        /// Share all telemetry data (CSVs, recorded paths, crash logs, screenshots)
        /// via the Android share intent. Shows a status message for every outcome.
        /// </summary>
        public void ShareTelemetryData()
        {
            // Collect all shareable files under persistentDataPath
            string baseDir = Application.persistentDataPath;

            var searchRoots = new[]
            {
                Path.Combine(baseDir, "Telemetry"),
                Path.Combine(baseDir, "RecordedPaths"),
                Path.Combine(baseDir, "Crashes"),
            };

            var patterns = new[] { "*.csv", "*.json", "*.txt", "*.png", "*.jpg", "*.jpeg" };

            var files = new List<string>();
            foreach (string root in searchRoots)
            {
                if (!Directory.Exists(root)) continue;
                foreach (string pattern in patterns)
                {
                    try
                    {
                        files.AddRange(Directory.GetFiles(root, pattern, SearchOption.AllDirectories));
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[DataSyncManager] Share scan error in {root}: {ex.Message}");
                    }
                }
            }

            // Remove duplicates that might occur due to multiple pattern passes on same file
            var seen = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            files.RemoveAll(f => !seen.Add(f));

            if (files.Count == 0)
            {
                StatusMessage = "No data to share yet. Start a recording session first.";
                Debug.Log($"[DataSyncManager] Share: No files found under {baseDir}");
                return;
            }

            Debug.Log($"[DataSyncManager] Share: Found {files.Count} files to share.");
#if UNITY_ANDROID && !UNITY_EDITOR
            ShareOnAndroid(files);
#else
            StatusMessage = $"{files.Count} files ready (Editor: {baseDir})";
            Debug.Log($"[DataSyncManager] Share: {files.Count} files at {baseDir}");
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private void ShareOnAndroid(List<string> filePaths)
        {
            try
            {
                using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (AndroidJavaObject activity  = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (AndroidJavaObject context   = activity.Call<AndroidJavaObject>("getApplicationContext"))
                {
                    string packageName = context.Call<AndroidJavaObject>("getPackageName").Call<string>("toString");
                    string authority   = packageName + ".fileprovider";

                    using (AndroidJavaClass fileProviderClass = new AndroidJavaClass("androidx.core.content.FileProvider"))
                    using (AndroidJavaObject arrayList = new AndroidJavaObject("java.util.ArrayList"))
                    {
                        int added = 0;
                        foreach (string path in filePaths)
                        {
                            if (!File.Exists(path)) continue;
                            try
                            {
                                using (AndroidJavaObject file = new AndroidJavaObject("java.io.File", path))
                                {
                                    AndroidJavaObject uri = fileProviderClass.CallStatic<AndroidJavaObject>(
                                        "getUriForFile", context, authority, file);
                                    arrayList.Call<bool>("add", uri);
                                    added++;
                                }
                            }
                            catch (Exception fe)
                            {
                                Debug.LogWarning($"[DataSyncManager] Skipping {Path.GetFileName(path)}: {fe.Message}");
                            }
                        }

                        if (added == 0)
                        {
                            StatusMessage = "Share: FileProvider could not package any files. Check AndroidManifest.";
                            Debug.LogError("[DataSyncManager] Share: 0 URIs added.");
                            return;
                        }

                        using (AndroidJavaClass intentClass = new AndroidJavaClass("android.content.Intent"))
                        using (AndroidJavaObject intent     = new AndroidJavaObject("android.content.Intent"))
                        {
                            intent.Call<AndroidJavaObject>("setAction", "android.intent.action.SEND_MULTIPLE");
                            intent.Call<AndroidJavaObject>("setType", "*/*");
                            intent.Call<AndroidJavaObject>("putParcelableArrayListExtra",
                                "android.intent.extra.STREAM", arrayList);
                            // FLAG_GRANT_READ_URI_PERMISSION (0x1) | FLAG_ACTIVITY_NEW_TASK (0x10000000)
                            intent.Call<AndroidJavaObject>("addFlags", 0x10000001);

                            AndroidJavaObject chooser = intentClass.CallStatic<AndroidJavaObject>(
                                "createChooser", intent, $"Share {added} session files");
                            activity.Call("startActivity", chooser);
                            StatusMessage = $"Sharing {added} files...";
                            Debug.Log($"[DataSyncManager] Share intent launched for {added} files.");
                        }
                    }
                }
            }
            catch (Exception e)
            {
                StatusMessage = $"Share error: {e.Message}";
                Debug.LogError($"[DataSyncManager] Android share failed: {e}");
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
            if (_backgroundUpdateRoutine != null)
            {
                StopCoroutine(_backgroundUpdateRoutine);
                _backgroundUpdateRoutine = null;
            }

            if (_bgTex != null) Destroy(_bgTex);
            if (_barBgTex != null) Destroy(_barBgTex);
            if (_barFillTex != null) Destroy(_barFillTex);
        }
    }
}
