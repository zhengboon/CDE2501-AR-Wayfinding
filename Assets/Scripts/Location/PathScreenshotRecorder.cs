using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace CDE2501.Wayfinding.Location
{
    public class PathScreenshotRecorder : MonoBehaviour
    {
        [Header("Trigger Settings")]
        [SerializeField] private float headingChangeDegrees = 30f;
        [SerializeField] private float intervalSeconds = 10f;
        [SerializeField] private int maxScreenshotsPerSession = 200;

        [Header("Dependencies")]
        [SerializeField] private GPSManager gpsManager;
        [SerializeField] private CompassManager compassManager;
        [SerializeField] private TelemetryRecorder telemetryRecorder;

        public int ScreenshotCount { get; private set; }

        private float _lastHeading;
        private float _lastIntervalTime;
        private bool _hasBaseline;
        private bool _capturing;

        private void Awake()
        {
            if (gpsManager == null) gpsManager = FindObjectOfType<GPSManager>();
            if (compassManager == null) compassManager = FindObjectOfType<CompassManager>();
            if (telemetryRecorder == null) telemetryRecorder = FindObjectOfType<TelemetryRecorder>();
        }

        private void Update()
        {
            if (telemetryRecorder == null || !telemetryRecorder.IsRecording || _capturing)
            {
                return;
            }

            if (ScreenshotCount >= maxScreenshotsPerSession)
            {
                return;
            }

            float heading = compassManager != null && compassManager.IsReady
                ? compassManager.SmoothedHeading
                : 0f;

            if (!_hasBaseline)
            {
                _lastHeading = heading;
                _lastIntervalTime = Time.time;
                _hasBaseline = true;
                return;
            }

            float headingDelta = Mathf.Abs(Mathf.DeltaAngle(_lastHeading, heading));
            bool headingTrigger = headingDelta >= headingChangeDegrees;
            bool intervalTrigger = (Time.time - _lastIntervalTime) >= intervalSeconds;

            if (headingTrigger || intervalTrigger)
            {
                string reason = headingTrigger ? "heading" : "interval";
                StartCoroutine(CaptureScreenshot(reason));
                _lastHeading = heading;
                _lastIntervalTime = Time.time;
            }
        }

        public void CaptureManual()
        {
            if (telemetryRecorder == null || !telemetryRecorder.IsRecording || _capturing)
            {
                return;
            }

            if (ScreenshotCount >= maxScreenshotsPerSession)
            {
                return;
            }

            StartCoroutine(CaptureScreenshot("manual"));
        }

        public void ResetSession()
        {
            ScreenshotCount = 0;
            _hasBaseline = false;
        }

        private IEnumerator CaptureScreenshot(string reason)
        {
            _capturing = true;
            yield return new WaitForEndOfFrame();

            if (telemetryRecorder == null)
            {
                _capturing = false;
                yield break;
            }

            string dir = telemetryRecorder.CurrentSessionDir;
            if (string.IsNullOrWhiteSpace(dir))
            {
                _capturing = false;
                yield break;
            }

            string screenshotsDir = Path.Combine(dir, "screenshots");
            if (!Directory.Exists(screenshotsDir))
            {
                Directory.CreateDirectory(screenshotsDir);
            }

            double lat = 0.0;
            double lon = 0.0;
            if (gpsManager != null && gpsManager.IsReady)
            {
                lat = gpsManager.SmoothedPoint.latitude;
                lon = gpsManager.SmoothedPoint.longitude;
            }

            float heading = compassManager != null && compassManager.IsReady
                ? compassManager.SmoothedHeading
                : 0f;

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            string filename = $"{timestamp}_lat{lat:F4}_lon{lon:F4}_h{heading:F0}_{reason}.jpg";
            string path = Path.Combine(screenshotsDir, filename);

            Texture2D tex = null;
            try
            {
                tex = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
                tex.Apply();

                byte[] jpg = tex.EncodeToJPG(75);
                File.WriteAllBytes(path, jpg);
                ScreenshotCount++;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PathScreenshotRecorder] Failed to capture: {e.Message}");
            }
            finally
            {
                if (tex != null) Destroy(tex);
            }

            _capturing = false;
        }
    }
}
