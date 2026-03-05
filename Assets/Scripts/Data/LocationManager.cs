using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace CDE2501.Wayfinding.Data
{
    [Serializable]
    public class LocationPoint
    {
        public string name;
        public string type;
        public double gps_lat;
        public double gps_lon;
        public string indoor_node_id;
    }

    [Serializable]
    public class LocationListWrapper
    {
        public List<LocationPoint> locations = new List<LocationPoint>();
    }

    public class LocationManager : MonoBehaviour
    {
        [SerializeField] private string locationsFileName = "locations.json";

        private readonly List<LocationPoint> _locations = new List<LocationPoint>();

        public IReadOnlyList<LocationPoint> Locations => _locations;

        public event Action OnLocationsChanged;

        public void LoadLocations()
        {
            StartCoroutine(LoadLocationsRoutine());
        }

        public void SaveLocations()
        {
            string path = GetPersistentPath(locationsFileName);
            EnsureDataFolder(path);

            var wrapper = new LocationListWrapper { locations = _locations };
            string json = JsonUtility.ToJson(wrapper, true);
            File.WriteAllText(path, json);
        }

        public void AddLocation(LocationPoint point)
        {
            if (point == null || string.IsNullOrWhiteSpace(point.name))
            {
                return;
            }

            _locations.Add(point);
            SaveLocations();
            OnLocationsChanged?.Invoke();
        }

        public bool UpdateLocation(string originalName, LocationPoint updated)
        {
            int index = _locations.FindIndex(l => l.name == originalName);
            if (index < 0 || updated == null)
            {
                return false;
            }

            _locations[index] = updated;
            SaveLocations();
            OnLocationsChanged?.Invoke();
            return true;
        }

        public bool DeleteLocation(string locationName)
        {
            int index = _locations.FindIndex(l => l.name == locationName);
            if (index < 0)
            {
                return false;
            }

            _locations.RemoveAt(index);
            SaveLocations();
            OnLocationsChanged?.Invoke();
            return true;
        }

        public LocationPoint GetByName(string locationName)
        {
            return _locations.Find(l => l.name == locationName);
        }

        private IEnumerator LoadLocationsRoutine()
        {
            string persistentPath = GetPersistentPath(locationsFileName);
            string streamingPath = GetStreamingPath(locationsFileName);

            if (!File.Exists(persistentPath))
            {
                yield return CopyFromStreamingAssets(streamingPath, persistentPath);
            }

            if (!File.Exists(persistentPath))
            {
                Debug.LogError($"Unable to find locations file: {persistentPath}");
                yield break;
            }

            string raw = File.ReadAllText(persistentPath);
            string wrappedJson = WrapTopLevelArrayIfNeeded(raw, "locations");

            LocationListWrapper wrapper = JsonUtility.FromJson<LocationListWrapper>(wrappedJson);
            _locations.Clear();
            if (wrapper != null && wrapper.locations != null)
            {
                _locations.AddRange(wrapper.locations);
            }

            OnLocationsChanged?.Invoke();
        }

        private static string WrapTopLevelArrayIfNeeded(string rawJson, string key)
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
                Debug.LogError($"Unable to copy file from StreamingAssets: {request.error}");
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
