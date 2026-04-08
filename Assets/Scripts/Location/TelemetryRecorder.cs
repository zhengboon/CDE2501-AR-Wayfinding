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
        
        [Header("Dependencies")]
        [SerializeField] private GPSManager gpsManager;
        [SerializeField] private CompassManager compassManager;
        [SerializeField] private RouteCalculator routeCalculator;
        
        public bool IsRecording { get; private set; }
        public string CurrentSessionFile { get; private set; }
        
        private StreamWriter _writer;
        private float _nextRecordTime;
        
        private string _lastStartNode = "";
        private string _lastDestination = "";
        private float _lastRouteDistance = 0f;

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
            
            string dir = Path.Combine(Application.persistentDataPath, "Telemetry");
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            
            string filename = $"Session_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
            CurrentSessionFile = Path.Combine(dir, filename);
            
            try
            {
                _writer = new StreamWriter(CurrentSessionFile, false, Encoding.UTF8);
                _writer.WriteLine("Time,DateTime,Lat,Lon,Heading,StartNode,Destination,RouteDistance,IsSimulated");
                IsRecording = true;
                _nextRecordTime = Time.time;
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
            
            if (Time.time >= _nextRecordTime)
            {
                RecordDataPoint();
                _nextRecordTime = Time.time + recordIntervalSeconds;
            }
        }

        private void RecordDataPoint()
        {
            float t = Time.time;
            string dt = DateTime.UtcNow.ToString("o");
            
            double lat = 0.0;
            double lon = 0.0;
            if (gpsManager != null && gpsManager.IsReady)
            {
                lat = gpsManager.SmoothedPoint.latitude;
                lon = gpsManager.SmoothedPoint.longitude;
            }
            
            float heading = 0f;
            if (compassManager != null && compassManager.IsReady)
            {
                heading = compassManager.SmoothedHeading;
            }
            
            bool isSimulated = (gpsManager != null && gpsManager.IsUsingSimulation) || 
                               (compassManager != null && compassManager.IsUsingSimulation);
            
            string line = $"{t:F2},{dt},{lat:F6},{lon:F6},{heading:F1},{_lastStartNode},{_lastDestination},{_lastRouteDistance:F1},{isSimulated}";
            
            try
            {
                _writer.WriteLine(line);
            }
            catch (Exception e)
            {
                Debug.LogError($"Error writing telemetry line: {e.Message}");
                StopRecording();
            }
        }
    }
}
