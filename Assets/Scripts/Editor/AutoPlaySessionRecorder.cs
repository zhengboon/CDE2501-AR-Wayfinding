using System;
using System.IO;
using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Encoder;
using UnityEditor.Recorder.Input;
using UnityEngine;

namespace CDE2501.Wayfinding.EditorTools
{
    [InitializeOnLoad]
    public static class AutoPlaySessionRecorder
    {
        private const string EnabledPrefKey = "CDE2501.AutoPlaySessionRecorder.Enabled";
        private const string DisableMigrationPrefKey = "CDE2501.AutoPlaySessionRecorder.DisabledMigration_20260318";
        private const string EnableByDefaultMigrationPrefKey = "CDE2501.AutoPlaySessionRecorder.EnabledMigration_20260326";
        private const string MenuEnabledPath = "CDE2501/Session Recorder/Enabled";
        private const string MenuOpenFolderPath = "CDE2501/Session Recorder/Open Output Folder";
        private const string MenuOpenLatestPath = "CDE2501/Session Recorder/Open Latest Session";
        private const string MenuPruneNowPath = "CDE2501/Session Recorder/Prune Old Sessions Now";

        private const string OutputFolderRelative = "Recordings/AutoSessions";
        private const int OutputWidth = 1920;
        private const int OutputHeight = 1080;
        private const int OutputFrameRate = 30;
        private const int MaxRetainedSessions = 5;

        private static RecorderController _controller;
        private static RecorderControllerSettings _controllerSettings;
        private static MovieRecorderSettings _movieSettings;
        private static bool _isRecording;
        private static string _currentOutputStem;

        static AutoPlaySessionRecorder()
        {
            if (!EditorPrefs.GetBool(DisableMigrationPrefKey, false))
            {
                EditorPrefs.SetBool(EnabledPrefKey, false);
                EditorPrefs.SetBool(DisableMigrationPrefKey, true);
            }

            // New behavior: auto-enable recorder by default so entering Play Mode
            // immediately starts recording unless user explicitly turns it off later.
            if (!EditorPrefs.GetBool(EnableByDefaultMigrationPrefKey, false))
            {
                EditorPrefs.SetBool(EnabledPrefKey, true);
                EditorPrefs.SetBool(EnableByDefaultMigrationPrefKey, true);
            }

            EditorApplication.delayCall += EnsureMenuState;
            EditorApplication.playModeStateChanged += HandlePlayModeChanged;
        }

        private static bool IsEnabled
        {
            get => EditorPrefs.GetBool(EnabledPrefKey, false);
            set
            {
                EditorPrefs.SetBool(EnabledPrefKey, value);
                EnsureMenuState();
            }
        }

        [MenuItem(MenuEnabledPath)]
        private static void ToggleEnabled()
        {
            IsEnabled = !IsEnabled;
            if (!IsEnabled)
            {
                StopRecording("Auto recording disabled.");
            }
        }

        [MenuItem(MenuEnabledPath, true)]
        private static bool ValidateToggleEnabled()
        {
            Menu.SetChecked(MenuEnabledPath, IsEnabled);
            return true;
        }

        [MenuItem(MenuOpenFolderPath)]
        private static void OpenOutputFolder()
        {
            string absoluteFolder = GetAbsoluteOutputFolderPath();
            Directory.CreateDirectory(absoluteFolder);
            EditorUtility.RevealInFinder(absoluteFolder);
        }

        [MenuItem(MenuOpenLatestPath)]
        private static void OpenLatestSession()
        {
            if (!TryGetLatestRecordingPath(out string latestPath))
            {
                Debug.LogWarning("[AutoPlaySessionRecorder] No session recordings found.");
                return;
            }

            try
            {
                Application.OpenURL(new Uri(latestPath).AbsoluteUri);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AutoPlaySessionRecorder] Unable to open latest session '{latestPath}': {ex.Message}");
            }
        }

        [MenuItem(MenuOpenLatestPath, true)]
        private static bool ValidateOpenLatestSession()
        {
            return TryGetLatestRecordingPath(out _);
        }

        [MenuItem(MenuPruneNowPath)]
        private static void PruneNow()
        {
            string absoluteFolder = GetAbsoluteOutputFolderPath();
            if (!Directory.Exists(absoluteFolder))
            {
                Debug.Log("[AutoPlaySessionRecorder] No output folder found to prune.");
                return;
            }

            PruneOlderRecordings(absoluteFolder, MaxRetainedSessions);
        }

        private static void EnsureMenuState()
        {
            Menu.SetChecked(MenuEnabledPath, IsEnabled);
        }

        private static void HandlePlayModeChanged(PlayModeStateChange state)
        {
            switch (state)
            {
                case PlayModeStateChange.EnteredPlayMode:
                    StartRecording();
                    break;

                case PlayModeStateChange.ExitingPlayMode:
                    StopRecording("Play mode exited.");
                    break;
            }
        }

        private static void StartRecording()
        {
            if (!IsEnabled || _isRecording)
            {
                return;
            }

            try
            {
                string absoluteFolder = GetAbsoluteOutputFolderPath();
                Directory.CreateDirectory(absoluteFolder);
                PruneOlderRecordings(absoluteFolder, MaxRetainedSessions - 1);

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                _currentOutputStem = $"{OutputFolderRelative}/session_{timestamp}";

                CleanupRecorderObjects();
                _controllerSettings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
                _movieSettings = ScriptableObject.CreateInstance<MovieRecorderSettings>();

                _movieSettings.name = "Auto Play Session";
                _movieSettings.Enabled = true;
                _movieSettings.EncoderSettings = new CoreEncoderSettings
                {
                    Codec = CoreEncoderSettings.OutputCodec.MP4,
                    EncodingQuality = CoreEncoderSettings.VideoEncodingQuality.Medium
                };
                _movieSettings.ImageInputSettings = new GameViewInputSettings
                {
                    OutputWidth = OutputWidth,
                    OutputHeight = OutputHeight
                };
                _movieSettings.OutputFile = _currentOutputStem;

                _controllerSettings.AddRecorderSettings(_movieSettings);
                _controllerSettings.SetRecordModeToManual();
                _controllerSettings.FrameRate = OutputFrameRate;
                _controllerSettings.CapFrameRate = false;

                _controller = new RecorderController(_controllerSettings);
                _controller.PrepareRecording();
                _controller.StartRecording();
                _isRecording = true;

                Debug.Log($"[AutoPlaySessionRecorder] Recording started: {_currentOutputStem}.mp4");
            }
            catch (Exception ex)
            {
                _isRecording = false;
                CleanupRecorderObjects();
                Debug.LogError($"[AutoPlaySessionRecorder] Failed to start recording: {ex.Message}");
            }
        }

        private static void StopRecording(string reason)
        {
            if (!_isRecording)
            {
                return;
            }

            try
            {
                if (_controller != null)
                {
                    _controller.StopRecording();
                }

                Debug.Log($"[AutoPlaySessionRecorder] Recording saved: {_currentOutputStem}.mp4 ({reason})");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AutoPlaySessionRecorder] Failed to stop recording: {ex.Message}");
            }
            finally
            {
                CleanupRecorderObjects();
                _isRecording = false;
                string absoluteFolder = GetAbsoluteOutputFolderPath();
                if (Directory.Exists(absoluteFolder))
                {
                    PruneOlderRecordings(absoluteFolder, MaxRetainedSessions);
                }

                _currentOutputStem = null;
            }
        }

        private static string GetAbsoluteOutputFolderPath()
        {
            string projectRoot = Directory.GetCurrentDirectory();
            return Path.GetFullPath(Path.Combine(projectRoot, OutputFolderRelative));
        }

        private static void PruneOlderRecordings(string absoluteFolder, int keepCount)
        {
            if (string.IsNullOrWhiteSpace(absoluteFolder) || !Directory.Exists(absoluteFolder))
            {
                return;
            }

            if (keepCount < 0)
            {
                keepCount = 0;
            }

            string[] recordings = Directory.GetFiles(absoluteFolder, "session_*.mp4", SearchOption.TopDirectoryOnly);
            if (recordings.Length <= keepCount)
            {
                return;
            }

            Array.Sort(recordings, (a, b) =>
                File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a)));

            for (int i = keepCount; i < recordings.Length; i++)
            {
                try
                {
                    File.Delete(recordings[i]);
                    string metaPath = recordings[i] + ".meta";
                    if (File.Exists(metaPath))
                    {
                        File.Delete(metaPath);
                    }
                    Debug.Log($"[AutoPlaySessionRecorder] Pruned old session: {recordings[i]}");
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[AutoPlaySessionRecorder] Failed to prune session '{recordings[i]}': {ex.Message}");
                }
            }
        }

        private static bool TryGetLatestRecordingPath(out string latestPath)
        {
            latestPath = null;
            string absoluteFolder = GetAbsoluteOutputFolderPath();
            if (!Directory.Exists(absoluteFolder))
            {
                return false;
            }

            string[] recordings = Directory.GetFiles(absoluteFolder, "session_*.mp4", SearchOption.TopDirectoryOnly);
            if (recordings == null || recordings.Length == 0)
            {
                return false;
            }

            Array.Sort(recordings, (a, b) =>
                File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a)));

            latestPath = recordings[0];
            return true;
        }

        private static void CleanupRecorderObjects()
        {
            _controller = null;

            if (_movieSettings != null)
            {
                ScriptableObject.DestroyImmediate(_movieSettings);
                _movieSettings = null;
            }

            if (_controllerSettings != null)
            {
                ScriptableObject.DestroyImmediate(_controllerSettings);
                _controllerSettings = null;
            }
        }
    }
}
