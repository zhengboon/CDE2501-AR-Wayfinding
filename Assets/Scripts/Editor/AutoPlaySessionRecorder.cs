using System;
using System.IO;
using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Input;
using UnityEngine;

namespace CDE2501.Wayfinding.EditorTools
{
    [InitializeOnLoad]
    public static class AutoPlaySessionRecorder
    {
        private const string EnabledPrefKey = "CDE2501.AutoPlaySessionRecorder.Enabled";
        private const string MenuEnabledPath = "CDE2501/Session Recorder/Enabled";
        private const string MenuOpenFolderPath = "CDE2501/Session Recorder/Open Output Folder";

        private const string OutputFolderRelative = "Recordings/AutoSessions";
        private const int OutputWidth = 1920;
        private const int OutputHeight = 1080;
        private const int OutputFrameRate = 30;

        private static RecorderController _controller;
        private static bool _isRecording;
        private static string _currentOutputStem;

        static AutoPlaySessionRecorder()
        {
            EditorApplication.delayCall += EnsureMenuState;
            EditorApplication.playModeStateChanged += HandlePlayModeChanged;
        }

        private static bool IsEnabled
        {
            get => EditorPrefs.GetBool(EnabledPrefKey, true);
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

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                _currentOutputStem = $"{OutputFolderRelative}/session_{timestamp}";

                var controllerSettings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
                var movieSettings = ScriptableObject.CreateInstance<MovieRecorderSettings>();

                movieSettings.name = "Auto Play Session";
                movieSettings.Enabled = true;
                movieSettings.OutputFormat = MovieRecorderSettings.VideoRecorderOutputFormat.MP4;
                movieSettings.ImageInputSettings = new GameViewInputSettings
                {
                    OutputWidth = OutputWidth,
                    OutputHeight = OutputHeight
                };
                movieSettings.OutputFile = _currentOutputStem;

                controllerSettings.AddRecorderSettings(movieSettings);
                controllerSettings.SetRecordModeToManual();
                controllerSettings.FrameRate = OutputFrameRate;
                controllerSettings.CapFrameRate = false;

                _controller = new RecorderController(controllerSettings);
                _controller.PrepareRecording();
                _controller.StartRecording();
                _isRecording = true;

                Debug.Log($"[AutoPlaySessionRecorder] Recording started: {_currentOutputStem}.mp4");
            }
            catch (Exception ex)
            {
                _isRecording = false;
                _controller = null;
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
                _controller = null;
                _isRecording = false;
            }
        }

        private static string GetAbsoluteOutputFolderPath()
        {
            string projectRoot = Directory.GetCurrentDirectory();
            return Path.GetFullPath(Path.Combine(projectRoot, OutputFolderRelative));
        }
    }
}
