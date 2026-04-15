using System.IO;
using System.Xml;
using UnityEditor.Android;
using UnityEngine;

namespace CDE2501.Wayfinding.EditorTools
{
    /// <summary>
    /// Enforces runtime-safe Android manifest flags after XR package post-processors
    /// mutate the merged manifest during Gradle project generation.
    /// </summary>
    public sealed class AndroidManifestPostProcessor : IPostGenerateGradleAndroidProject
    {
        private const string AndroidNamespace = "http://schemas.android.com/apk/res/android";

        public int callbackOrder => 10000;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            string manifestPath = ResolveManifestPath(path);
            if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
            {
                Debug.LogWarning($"[AndroidManifestPostProcessor] Manifest not found under: {path}");
                return;
            }

            try
            {
                var doc = new XmlDocument();
                doc.Load(manifestPath);
                var ns = new XmlNamespaceManager(doc.NameTable);
                ns.AddNamespace("android", AndroidNamespace);

                ForceUsesFeatureRequired(doc, ns, "android.hardware.camera.ar", required: false);
                ForceUsesFeatureRequired(doc, ns, "com.google.ar.core.depth", required: false);
                ForceMetaDataValue(doc, ns, "com.google.ar.core", "optional");
                ForceUnityActivityHardwareAcceleration(doc, ns, enabled: true);

                doc.Save(manifestPath);
                Debug.Log($"[AndroidManifestPostProcessor] Patched manifest: {manifestPath}");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[AndroidManifestPostProcessor] Failed to patch manifest: {ex.Message}");
            }
        }

        private static string ResolveManifestPath(string gradleProjectPath)
        {
            if (string.IsNullOrWhiteSpace(gradleProjectPath))
            {
                return string.Empty;
            }

            string primary = Path.Combine(gradleProjectPath, "src", "main", "AndroidManifest.xml");
            if (File.Exists(primary))
            {
                return primary;
            }

            string fallback = Path.Combine(gradleProjectPath, "unityLibrary", "src", "main", "AndroidManifest.xml");
            return File.Exists(fallback) ? fallback : string.Empty;
        }

        private static void ForceUsesFeatureRequired(XmlDocument doc, XmlNamespaceManager ns, string featureName, bool required)
        {
            if (doc?.DocumentElement == null || string.IsNullOrWhiteSpace(featureName))
            {
                return;
            }

            XmlElement featureElement = doc.SelectSingleNode(
                $"/manifest/uses-feature[@android:name='{featureName}']",
                ns) as XmlElement;

            if (featureElement == null)
            {
                featureElement = doc.CreateElement("uses-feature");
                doc.DocumentElement.AppendChild(featureElement);
            }

            featureElement.SetAttribute("name", AndroidNamespace, featureName);
            featureElement.SetAttribute("required", AndroidNamespace, required ? "true" : "false");
        }

        private static void ForceMetaDataValue(XmlDocument doc, XmlNamespaceManager ns, string metaName, string value)
        {
            if (doc?.DocumentElement == null || string.IsNullOrWhiteSpace(metaName))
            {
                return;
            }

            XmlElement application = doc.SelectSingleNode("/manifest/application", ns) as XmlElement;
            if (application == null)
            {
                return;
            }

            XmlElement metaElement = doc.SelectSingleNode(
                $"/manifest/application/meta-data[@android:name='{metaName}']",
                ns) as XmlElement;

            if (metaElement == null)
            {
                metaElement = doc.CreateElement("meta-data");
                application.AppendChild(metaElement);
            }

            metaElement.SetAttribute("name", AndroidNamespace, metaName);
            metaElement.SetAttribute("value", AndroidNamespace, value ?? string.Empty);
        }

        private static void ForceUnityActivityHardwareAcceleration(XmlDocument doc, XmlNamespaceManager ns, bool enabled)
        {
            if (doc?.DocumentElement == null)
            {
                return;
            }

            XmlElement activity = doc.SelectSingleNode(
                "/manifest/application/activity[@android:name='com.unity3d.player.UnityPlayerActivity']",
                ns) as XmlElement;

            if (activity == null)
            {
                return;
            }

            activity.SetAttribute("hardwareAccelerated", AndroidNamespace, enabled ? "true" : "false");
        }
    }
}
