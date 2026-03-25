using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace CDE2501.Wayfinding.EditorTools
{
    public static class CDE2501BuildRunner
    {
        private const string EnvBuildTarget = "CDE2501_BUILD_TARGET";
        private const string EnvBuildOutput = "CDE2501_BUILD_OUTPUT";
        private const string EnvScenePath = "CDE2501_SCENE_PATH";
        private const string EnvDevelopmentBuild = "CDE2501_DEVELOPMENT_BUILD";

        private const string DefaultScenePath = "Assets/Scenes/Main.unity";

        [MenuItem("CDE2501/Build/Build From Environment")]
        public static void BuildFromEnvironment()
        {
            string targetRaw = ReadEnv(EnvBuildTarget, BuildTarget.StandaloneWindows64.ToString());
            string outputRaw = ReadEnv(EnvBuildOutput, "Builds/Windows/CDE2501-Wayfinding.exe");
            string sceneRaw = ReadEnv(EnvScenePath, DefaultScenePath);
            string developmentRaw = ReadEnv(EnvDevelopmentBuild, "0");

            if (!TryParseBuildTarget(targetRaw, out BuildTarget target))
            {
                Fail("Unknown build target: " + targetRaw);
                return;
            }

            string projectRoot = Directory.GetCurrentDirectory();
            string outputPath = ResolveOutputPath(projectRoot, outputRaw);
            string[] scenes = ResolveScenes(sceneRaw);
            if (scenes == null || scenes.Length == 0)
            {
                Fail("No valid scenes found for build. Requested scene: " + sceneRaw);
                return;
            }

            bool developmentBuild = ParseBool(developmentRaw);
            BuildOptions options = developmentBuild ? BuildOptions.Development : BuildOptions.None;

            EnsureOutputDirectory(outputPath);
            Debug.Log("[CDE2501BuildRunner] Starting build");
            Debug.Log("[CDE2501BuildRunner] Target: " + target);
            Debug.Log("[CDE2501BuildRunner] Output: " + outputPath);
            Debug.Log("[CDE2501BuildRunner] Scene count: " + scenes.Length);
            Debug.Log("[CDE2501BuildRunner] Development: " + developmentBuild);

            var buildPlayerOptions = new BuildPlayerOptions
            {
                target = target,
                locationPathName = outputPath,
                scenes = scenes,
                options = options
            };

            BuildReport report = BuildPipeline.BuildPlayer(buildPlayerOptions);
            BuildSummary summary = report.summary;

            Debug.Log("[CDE2501BuildRunner] Result: " + summary.result);
            Debug.Log("[CDE2501BuildRunner] Total errors: " + summary.totalErrors + ", warnings: " + summary.totalWarnings);
            Debug.Log("[CDE2501BuildRunner] Output path: " + summary.outputPath);
            Debug.Log("[CDE2501BuildRunner] Total size bytes: " + summary.totalSize);
            Debug.Log("[CDE2501BuildRunner] Build time seconds: " + summary.totalTime.TotalSeconds.ToString("0.000"));

            bool succeeded = summary.result == BuildResult.Succeeded;
            if (!succeeded)
            {
                Fail("Build failed with result: " + summary.result);
                return;
            }

            if (Application.isBatchMode)
            {
                EditorApplication.Exit(0);
            }
        }

        [MenuItem("CDE2501/Build/Build Windows64")]
        public static void BuildWindows64()
        {
            RunDirectBuild(BuildTarget.StandaloneWindows64, "Builds/Windows/CDE2501-Wayfinding.exe", developmentBuild: false);
        }

        [MenuItem("CDE2501/Build/Build Android APK")]
        public static void BuildAndroid()
        {
            RunDirectBuild(BuildTarget.Android, "Builds/Android/CDE2501-Wayfinding.apk", developmentBuild: false);
        }

        [MenuItem("CDE2501/Build/Build iOS Project")]
        public static void BuildIOS()
        {
            RunDirectBuild(BuildTarget.iOS, "Builds/iOS", developmentBuild: false);
        }

        private static void RunDirectBuild(BuildTarget target, string relativeOutputPath, bool developmentBuild)
        {
            string projectRoot = Directory.GetCurrentDirectory();
            string outputPath = ResolveOutputPath(projectRoot, relativeOutputPath);
            string[] scenes = ResolveScenes(DefaultScenePath);
            if (scenes == null || scenes.Length == 0)
            {
                Fail("Default scene missing: " + DefaultScenePath);
                return;
            }

            BuildOptions options = developmentBuild ? BuildOptions.Development : BuildOptions.None;
            EnsureOutputDirectory(outputPath);

            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                target = target,
                locationPathName = outputPath,
                scenes = scenes,
                options = options
            });

            BuildSummary summary = report.summary;
            Debug.Log("[CDE2501BuildRunner] Direct build result: " + summary.result + " -> " + summary.outputPath);
            if (summary.result != BuildResult.Succeeded)
            {
                Fail("Direct build failed: " + summary.result);
            }
        }

        private static string ResolveOutputPath(string projectRoot, string path)
        {
            if (Path.IsPathRooted(path))
            {
                return path;
            }

            return Path.GetFullPath(Path.Combine(projectRoot, path));
        }

        private static void EnsureOutputDirectory(string outputPath)
        {
            string directory = outputPath;
            if (HasFileExtension(outputPath))
            {
                directory = Path.GetDirectoryName(outputPath);
            }

            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        private static bool HasFileExtension(string outputPath)
        {
            string extension = Path.GetExtension(outputPath);
            return !string.IsNullOrWhiteSpace(extension);
        }

        private static string[] ResolveScenes(string preferredScenePath)
        {
            if (!string.IsNullOrWhiteSpace(preferredScenePath) && File.Exists(preferredScenePath))
            {
                return new[] { preferredScenePath };
            }

            int enabledCount = 0;
            for (int i = 0; i < EditorBuildSettings.scenes.Length; i++)
            {
                if (EditorBuildSettings.scenes[i] != null && EditorBuildSettings.scenes[i].enabled)
                {
                    enabledCount++;
                }
            }

            if (enabledCount == 0)
            {
                return Array.Empty<string>();
            }

            string[] enabledScenes = new string[enabledCount];
            int index = 0;
            for (int i = 0; i < EditorBuildSettings.scenes.Length; i++)
            {
                EditorBuildSettingsScene scene = EditorBuildSettings.scenes[i];
                if (scene != null && scene.enabled)
                {
                    enabledScenes[index] = scene.path;
                    index++;
                }
            }

            return enabledScenes;
        }

        private static bool TryParseBuildTarget(string raw, out BuildTarget target)
        {
            target = BuildTarget.NoTarget;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            string normalized = raw.Trim();
            if (string.Equals(normalized, "windows", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, "windows64", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, "standalonewindows64", StringComparison.OrdinalIgnoreCase))
            {
                target = BuildTarget.StandaloneWindows64;
                return true;
            }

            if (string.Equals(normalized, "android", StringComparison.OrdinalIgnoreCase))
            {
                target = BuildTarget.Android;
                return true;
            }

            if (string.Equals(normalized, "ios", StringComparison.OrdinalIgnoreCase))
            {
                target = BuildTarget.iOS;
                return true;
            }

            return Enum.TryParse(normalized, ignoreCase: true, result: out target);
        }

        private static bool ParseBool(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            string value = raw.Trim().ToLowerInvariant();
            return value == "1" || value == "true" || value == "yes" || value == "y" || value == "on";
        }

        private static string ReadEnv(string key, string fallback)
        {
            string value = Environment.GetEnvironmentVariable(key);
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        private static void Fail(string message)
        {
            Debug.LogError("[CDE2501BuildRunner] " + message);
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(1);
            }
        }
    }
}
