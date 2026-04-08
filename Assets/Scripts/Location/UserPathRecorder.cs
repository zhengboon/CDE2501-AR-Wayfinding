using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace CDE2501.Wayfinding.Location
{
    [Serializable]
    public class RecordedPathPoint
    {
        public double latitude;
        public double longitude;
        public float heading;
        public float altitude;
        public int estimatedFloor;
        public float timestamp;
        public bool gpsLost;
    }

    [Serializable]
    public class RecordedPath
    {
        public string id;
        public string fromLabel;
        public string toLabel;
        public string recordedAt;
        public float durationSeconds;
        public float distanceMeters;
        public int pointCount;
        public int screenshotCount;
        public bool uploaded;
        public List<RecordedPathPoint> points = new List<RecordedPathPoint>();
    }

    [Serializable]
    public class RecordedPathIndex
    {
        public List<RecordedPath> paths = new List<RecordedPath>();
    }

    public class UserPathRecorder : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] private float recordIntervalSeconds = 1f;
        [SerializeField] private float minPointDistanceMeters = 1f;

        [Header("Dependencies")]
        [SerializeField] private GPSManager gpsManager;
        [SerializeField] private CompassManager compassManager;
        [SerializeField] private TelemetryRecorder telemetryRecorder;

        public bool IsRecordingPath { get; private set; }
        public string CurrentFromLabel { get; private set; }
        public string CurrentToLabel { get; private set; }
        public int CurrentPointCount => _currentPoints.Count;
        public RecordedPathIndex PathIndex { get; private set; }

        private readonly List<RecordedPathPoint> _currentPoints = new List<RecordedPathPoint>();
        private float _nextRecordTime;
        private float _pathStartTime;
        private string _currentPathId;

        private static string PathStorageDir =>
            Path.Combine(Application.persistentDataPath, "RecordedPaths");

        private static string IndexFilePath =>
            Path.Combine(PathStorageDir, "path_index.json");

        private void Awake()
        {
            if (gpsManager == null) gpsManager = FindObjectOfType<GPSManager>();
            if (compassManager == null) compassManager = FindObjectOfType<CompassManager>();
            if (telemetryRecorder == null) telemetryRecorder = FindObjectOfType<TelemetryRecorder>();
            LoadPathIndex();
        }

        public void StartPathRecording(string fromLabel, string toLabel)
        {
            if (IsRecordingPath) return;
            if (string.IsNullOrWhiteSpace(fromLabel) || string.IsNullOrWhiteSpace(toLabel)) return;

            CurrentFromLabel = fromLabel.Trim();
            CurrentToLabel = toLabel.Trim();
            _currentPoints.Clear();
            _pathStartTime = Time.time;
            _currentPathId = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            _nextRecordTime = Time.time;
            IsRecordingPath = true;

            Debug.Log($"[UserPathRecorder] Started: {CurrentFromLabel} -> {CurrentToLabel}");
        }

        public void StopPathRecording()
        {
            if (!IsRecordingPath) return;
            IsRecordingPath = false;

            if (_currentPoints.Count < 2)
            {
                Debug.LogWarning("[UserPathRecorder] Path too short (< 2 points), discarding.");
                _currentPoints.Clear();
                return;
            }

            float duration = Time.time - _pathStartTime;
            float distance = ComputePathDistance(_currentPoints);
            int screenshots = telemetryRecorder != null && telemetryRecorder.IsRecording
                ? FindObjectOfType<PathScreenshotRecorder>()?.ScreenshotCount ?? 0
                : 0;

            var path = new RecordedPath
            {
                id = _currentPathId,
                fromLabel = CurrentFromLabel,
                toLabel = CurrentToLabel,
                recordedAt = DateTime.UtcNow.ToString("o"),
                durationSeconds = duration,
                distanceMeters = distance,
                pointCount = _currentPoints.Count,
                screenshotCount = screenshots,
                uploaded = false,
                points = new List<RecordedPathPoint>(_currentPoints)
            };

            SavePath(path);
            _currentPoints.Clear();
            Debug.Log($"[UserPathRecorder] Saved: {path.fromLabel} -> {path.toLabel} ({path.pointCount} pts, {distance:F0}m)");
        }

        private void Update()
        {
            if (!IsRecordingPath) return;

            if (Time.time >= _nextRecordTime)
            {
                RecordPoint();
                _nextRecordTime = Time.time + recordIntervalSeconds;
            }
        }

        private void RecordPoint()
        {
            if (gpsManager == null || !gpsManager.IsReady) return;

            double lat = gpsManager.SmoothedPoint.latitude;
            double lon = gpsManager.SmoothedPoint.longitude;

            if (_currentPoints.Count > 0)
            {
                var last = _currentPoints[_currentPoints.Count - 1];
                float dist = HaversineMeters(last.latitude, last.longitude, lat, lon);
                if (dist < minPointDistanceMeters) return;
            }

            float altitude = 0f;
            if (Input.location.status == LocationServiceStatus.Running)
            {
                altitude = Input.location.lastData.altitude;
            }

            float heading = compassManager != null && compassManager.IsReady
                ? compassManager.SmoothedHeading : 0f;

            int floor = telemetryRecorder != null ? telemetryRecorder.EstimatedFloor : 0;
            bool gpsLost = telemetryRecorder != null && telemetryRecorder.IsGpsLost;

            _currentPoints.Add(new RecordedPathPoint
            {
                latitude = lat,
                longitude = lon,
                heading = heading,
                altitude = altitude,
                estimatedFloor = floor,
                timestamp = Time.time - _pathStartTime,
                gpsLost = gpsLost
            });
        }

        private void SavePath(RecordedPath path)
        {
            if (!Directory.Exists(PathStorageDir))
            {
                Directory.CreateDirectory(PathStorageDir);
            }

            string pathFile = Path.Combine(PathStorageDir, $"path_{path.id}.json");
            string json = JsonUtility.ToJson(path, true);
            File.WriteAllText(pathFile, json, Encoding.UTF8);

            if (PathIndex == null) PathIndex = new RecordedPathIndex();
            PathIndex.paths.Add(path);
            SavePathIndex();
        }

        private void LoadPathIndex()
        {
            PathIndex = new RecordedPathIndex();
            if (!File.Exists(IndexFilePath)) return;

            try
            {
                string json = File.ReadAllText(IndexFilePath, Encoding.UTF8);
                var loaded = JsonUtility.FromJson<RecordedPathIndex>(json);
                if (loaded != null && loaded.paths != null)
                {
                    PathIndex = loaded;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[UserPathRecorder] Failed to load path index: {e.Message}");
            }
        }

        private void SavePathIndex()
        {
            if (!Directory.Exists(PathStorageDir))
            {
                Directory.CreateDirectory(PathStorageDir);
            }

            // Save index without full point data (just metadata)
            var indexCopy = new RecordedPathIndex();
            foreach (var p in PathIndex.paths)
            {
                indexCopy.paths.Add(new RecordedPath
                {
                    id = p.id,
                    fromLabel = p.fromLabel,
                    toLabel = p.toLabel,
                    recordedAt = p.recordedAt,
                    durationSeconds = p.durationSeconds,
                    distanceMeters = p.distanceMeters,
                    pointCount = p.pointCount,
                    screenshotCount = p.screenshotCount,
                    uploaded = p.uploaded
                });
            }

            string json = JsonUtility.ToJson(indexCopy, true);
            File.WriteAllText(IndexFilePath, json, Encoding.UTF8);
        }

        public void MarkPathUploaded(string pathId)
        {
            if (PathIndex == null) return;
            for (int i = 0; i < PathIndex.paths.Count; i++)
            {
                if (string.Equals(PathIndex.paths[i].id, pathId, StringComparison.Ordinal))
                {
                    PathIndex.paths[i].uploaded = true;
                    SavePathIndex();
                    return;
                }
            }
        }

        public string GetExportDirectory()
        {
            return PathStorageDir;
        }

        private static float ComputePathDistance(List<RecordedPathPoint> points)
        {
            float total = 0f;
            for (int i = 1; i < points.Count; i++)
            {
                total += HaversineMeters(
                    points[i - 1].latitude, points[i - 1].longitude,
                    points[i].latitude, points[i].longitude);
            }
            return total;
        }

        private static float HaversineMeters(double lat1, double lon1, double lat2, double lon2)
        {
            const double R = 6371000.0;
            double dLat = (lat2 - lat1) * Mathf.Deg2Rad;
            double dLon = (lon2 - lon1) * Mathf.Deg2Rad;
            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                       Math.Cos(lat1 * Mathf.Deg2Rad) * Math.Cos(lat2 * Mathf.Deg2Rad) *
                       Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return (float)(R * c);
        }
    }
}
