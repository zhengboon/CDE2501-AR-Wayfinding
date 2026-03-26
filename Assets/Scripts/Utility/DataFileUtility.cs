using System;
using UnityEngine;

namespace CDE2501.Wayfinding.Utility
{
    /// <summary>
    /// Shared helpers previously duplicated across GraphLoader, LocationManager,
    /// RouteCalculator, BoundaryConstraintManager, QuickStartBootstrap, and SimulationProvider.
    /// </summary>
    public static class DataFileUtility
    {
        /// <summary>
        /// Converts a local file path to a URI that UnityWebRequest can load.
        /// </summary>
        public static string ToUnityWebRequestPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return path;
            }

            if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("file://", StringComparison.OrdinalIgnoreCase) ||
                path.Contains("://"))
            {
                return path;
            }

            return "file://" + path;
        }

        /// <summary>
        /// Wraps a top-level JSON array in an object with the given key so that
        /// Unity's JsonUtility can deserialize it.
        /// </summary>
        public static string WrapTopLevelArrayIfNeeded(string rawJson, string key)
        {
            if (string.IsNullOrWhiteSpace(rawJson))
            {
                return "{}";
            }

            string trimmed = rawJson.TrimStart();
            if (trimmed.StartsWith("["))
            {
                return "{\"" + key + "\":" + rawJson + "}";
            }

            return rawJson;
        }

        /// <summary>
        /// Creates a 1x1 solid-color texture suitable for IMGUI backgrounds.
        /// </summary>
        public static Texture2D MakeSolidTexture(Color color)
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
    }
}
