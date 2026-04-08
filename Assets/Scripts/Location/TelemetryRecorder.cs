using System;
using System.IO;
using System.Text;
using UnityEngine;
using CDE2501.Wayfinding.Routing;

namespace CDE2501.Wayfinding.Location
{
    public class TelemetryRecorder : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] private float recordIntervalSeconds = 1.0f;
        [SerializeField] private float gpsLostTimeoutSeconds = 10f;
        [SerializeField] private float gpsLostAccuracyThreshold = 50f;
        [SerializeField] private float floorHeightMeters = 3.5f;

        [Header("Dependencies")]
        [SerializeField] private GPSManager gpsManager;
        [SerializeField] private CompassManager compassManager;
        [SerializeField] private RouteCalculator routeCalculator;

        public bool IsRecording { get; private set; }
        public string CurrentSessionFile { get; private set; }
        public string CurrentSessionDir { get; private set; }
        public bool IsGpsLost { get; private set; }
        public int EstimatedFloor { get; private set; }

        private StreamWriter _writer;
        private float _nextRecordTime;
        private float _lastGpsUpdateTime;
        private float _baselineAltitude = float.NaN;

        private string _lastStartNode = "";
        private string _lastDestination = "";
        private float _lastRouteDistance;
        private int _writesSinceFlush;

        private void Awake()
        {
            if (gpsManager == null) gpsManager = FindObjectOfType<GPSManager>();
            if (compassManager == null) compassManager = FindObjectOfType<CompassManager>();
            if (routeCalculator == null) routeCalculator = FindObjectOfType<RouteCalculator>();
        }

        private void OnEnable()
        {
            if (routeCalculator != null)
            {
                routeCalculator.OnRouteUpdated += HandleRouteUpdated;
            }
        }

        private void OnDisable()
        {
            if (routeCalculator != null)
            {
                routeCalculator.OnRouteUpdated -= HandleRouteUpdated;
            }
            StopRecording();
        }

        private void HandleRouteUpdated(RouteResult result)
        {
            if (result != null && result.success && result.nodePath != null && result.nodePath.Count > 0)
            {
                _lastStartNode = result.nodePath[0];
                _lastDestination = result.nodePath[result.nodePath.Count - 1];
                _lastRouteDistance = result.totalDistance;
            }
        }

        public void StartRecording()
        {
            if (IsRecording) return;

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            CurrentSessionDir = Path.Combine(Application.persistentDataPath, "Telemetry", $"Session_{timestamp}");
            if (!Directory.Exists(CurrentSessionDir))
            {
                Directory.CreateDirectory(CurrentSessionDir);
            }

            string screenshotsDir = Path.Combine(CurrentSessionDir, "screenshots");
            if (!Directory.Exists(screenshotsDir))
            {
                Directory.CreateDirectory(screenshotsDir);
            }

            CurrentSessionFile = Path.Combine(CurrentSessionDir, "telemetry.csv");

            try
            {
                _writer = new StreamWriter(CurrentSessionFile, false, Encoding.UTF8);
                _writer.WriteLine("Time,DateTime,Lat,Lon,Heading,Altitude,Accuracy,EstFloor,GpsLost,StartNode,Destination,RouteDistance,IsSimulated");
                IsRecording = true;
                _nextRecordTime = Time.time;
                _lastGpsUpdateTime = Time.time;
                _baselineAltitude = float.NaN;
                IsGpsLost = false;
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to start telemetry recording: {e.Message}");
                if (_writer != null)
                {
                    try { _writer.Dispose(); } catch { /* best effort */ }
                    _writer = null;
                }
                CurrentSessionFile = null;
                CurrentSessionDir = null;
            }
        }

        public void StopRecording()
        {
            if (!IsRecording) return;

            if (_writer != null)
            {
                try
                {
                    _writer.Flush();
                    _writer.Close();
                    _writer.Dispose();
                }
                catch (Exception e)
                {
                    Debug.LogError($"Error closing telemetry file: {e.Message}");
                }
                _writer = null;
            }

            IsRecording = false;
        }

        private void Update()
        {
            if (!IsRecording || _writer == null) return;

            UpdateGpsLostState();

            if (Time.time >= _nextRecordTime)
            {
                RecordDataPoint();
                _nextRecordTime = Time.time + recordIntervalSeconds;
            }
        }

        private void UpdateGpsLostState()
        {
            if (gpsManager == null)
            {
                IsGpsLost = true;
                return;
            }

            if (gpsManager.IsReady && !gpsManager.IsUsingSimulation)
            {
                float accuracy = Input.location.status == LocationServiceStatus.Running
                    ? Input.location.lastData.horizontalAccuracy
                    : float.PositiveInfinity;

                if (accuracy < gpsLostAccuracyThreshold)
                {
                    _lastGpsUpdateTime = Time.time;
                    IsGpsLost = false;
                    return;
                }
            }

            if (gpsManager.IsUsingSimulation)
            {
                _lastGpsUpdateTime = Time.time;
                IsGpsLost = false;
                return;
            }

            IsGpsLost = (Time.time - _lastGpsUpdateTime) > gpsLostTimeoutSeconds;
        }

        private void RecordDataPoint()
        {
            float t = Time.time;
            string dt = DateTime.UtcNow.ToString("o");

            double lat = 0.0;
            double lon = 0.0;
            float altitude = 0f;
            float accuracy = -1f;

            if (gpsManager != null && gpsManager.IsReady)
            {
                lat = gpsManager.SmoothedPoint.latitude;
                lon = gpsManager.SmoothedPoint.longitude;
            }

            if (Input.location.status == LocationServiceStatus.Running)
            {
                LocationInfo info = Input.location.lastData;
                altitude = info.altitude;
                accuracy = info.horizontalAccuracy;

                if (float.IsNaN(_baselineAltitude))
                {
                    _baselineAltitude = altitude;
                }
            }

            EstimatedFloor = EstimateFloor(altitude);

            float heading = 0f;
            if (compassManager != null && compassManager.IsReady)
            {
                heading = compassManager.SmoothedHeading;
            }

            bool isSimulated = (gpsManager != null && gpsManager.IsUsingSimulation) ||
                               (compassManager != null && compassManager.IsUsingSimulation);

            string line = $"{t:F2},{dt},{lat:F6},{lon:F6},{heading:F1},{altitude:F1},{accuracy:F1},{EstimatedFloor},{IsGpsLost},{_lastStartNode},{_lastDestination},{_lastRouteDistance:F1},{isSimulated}";

            try
            {
                _writer.WriteLine(line);
                _writesSinceFlush++;
                if (_writesSinceFlush >= 10)
                {
                    _writer.Flush();
                    _writesSinceFlush = 0;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Error writing telemetry line: {e.Message}");
                StopRecording();
            }
        }

        private int EstimateFloor(float currentAltitude)
        {
            if (float.IsNaN(_baselineAltitude) || floorHeightMeters <= 0f)
            {
                return 0;
            }

            float delta = currentAltitude - _baselineAltitude;
            return Mathf.RoundToInt(delta / floorHeightMeters);
        }
    }
}
