using System;
using System.Collections.Generic;
using CDE2501.Wayfinding.Data;
using CDE2501.Wayfinding.Location;
using CDE2501.Wayfinding.Routing;
using UnityEngine;

namespace CDE2501.Wayfinding.AR
{
    public class FlightTrackerARView : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] private GPSManager gpsManager;
        [SerializeField] private CompassManager compassManager;
        [SerializeField] private LocationManager locationManager;
        [SerializeField] private RouteCalculator routeCalculator;

        [Header("Camera")]
        [SerializeField] private float horizontalFovDegrees = 60f;
        [SerializeField] private float verticalFovDegrees = 45f;

        [Header("Labels")]
        [SerializeField] private float maxVisibleDistanceMeters = 2000f;
        [SerializeField] private float labelMinScale = 0.6f;
        [SerializeField] private float labelMaxScale = 1.4f;
        [SerializeField] private Color labelColor = Color.white;
        [SerializeField] private Color labelBgColor = new Color(0f, 0f, 0f, 0.7f);
        [SerializeField] private Color selectedLabelColor = new Color(1f, 0.55f, 0.05f, 1f);
        [SerializeField] private Color distanceColor = new Color(0.6f, 0.85f, 1f, 1f);
        [SerializeField] private Color directionArrowColor = new Color(0.1f, 0.9f, 1f, 1f);

        [Header("Route Overlay")]
        [SerializeField] private bool showRouteDirection = true;
        [SerializeField] private Color routeBearingColor = new Color(1f, 0.3f, 0.1f, 0.9f);

        public bool IsActive { get; private set; }

        private WebCamTexture _webcam;
        private GUIStyle _labelStyle;
        private GUIStyle _distStyle;
        private GUIStyle _arrowStyle;
        private Texture2D _bgTex;
        private Texture2D _pixel;
        private bool _gyroEnabled;
        private Quaternion _gyroOffset = Quaternion.identity;
        private string _selectedDestination;
        private RouteResult _lastRoute;
        private float _userPitchOffset = 0f;

        private readonly List<DestinationLabel> _visibleLabels = new List<DestinationLabel>();

        private struct DestinationLabel
        {
            public string name;
            public float bearing;
            public float distance;
            public float screenX;
            public float screenY;
            public float scale;
            public bool isSelected;
        }

        private void Awake()
        {
            if (gpsManager == null) gpsManager = FindObjectOfType<GPSManager>();
            if (compassManager == null) compassManager = FindObjectOfType<CompassManager>();
            if (locationManager == null) locationManager = FindObjectOfType<LocationManager>();
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
        }

        private void HandleRouteUpdated(RouteResult result)
        {
            _lastRoute = result;
        }

        public void SetSelectedDestination(string destinationName)
        {
            _selectedDestination = destinationName;
        }

        public void Activate()
        {
            if (IsActive) return;
            IsActive = true;

            StartCamera();
            EnableGyro();
        }

        public void Deactivate()
        {
            if (!IsActive) return;
            IsActive = false;

            StopCamera();
        }

        public void Toggle()
        {
            if (IsActive) Deactivate();
            else Activate();
        }

        private void StartCamera()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera))
            {
                UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Camera);
                StartCoroutine(CameraPermissionRoutine());
                return;
            }
#endif
            if (_webcam != null && _webcam.isPlaying) return;

            WebCamDevice[] devices = WebCamTexture.devices;
            if (devices.Length == 0)
            {
                Debug.LogWarning("[FlightTrackerARView] No camera found.");
                return;
            }

            string backCamera = null;
            for (int i = 0; i < devices.Length; i++)
            {
                if (!devices[i].isFrontFacing)
                {
                    backCamera = devices[i].name;
                    break;
                }
            }

            _webcam = new WebCamTexture(backCamera ?? devices[0].name, Screen.width, Screen.height, 30);
            _webcam.Play();
        }

        private System.Collections.IEnumerator CameraPermissionRoutine()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            while (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera))
            {
                yield return new WaitForSeconds(0.5f);
            }
            StartCamera();
#else
            yield break;
#endif
        }

        private void StopCamera()
        {
            if (_webcam != null)
            {
                _webcam.Stop();
                Destroy(_webcam);
                _webcam = null;
            }
        }

        private void EnableGyro()
        {
            if (SystemInfo.supportsGyroscope)
            {
                Input.gyro.enabled = true;
                _gyroEnabled = true;
                _gyroOffset = Quaternion.Euler(90f, 0f, 0f);
            }
        }

        private void OnDestroy()
        {
            StopCamera();
            if (_bgTex != null) Destroy(_bgTex);
            if (_pixel != null) Destroy(_pixel);
        }

        private void EnsureStyles()
        {
            if (_labelStyle != null) return;

            if (_bgTex == null)
            {
                _bgTex = new Texture2D(1, 1);
                _bgTex.SetPixel(0, 0, labelBgColor);
                _bgTex.Apply();
            }

            if (_pixel == null)
            {
                _pixel = new Texture2D(1, 1);
                _pixel.SetPixel(0, 0, Color.white);
                _pixel.Apply();
            }

            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 24,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = labelColor, background = _bgTex },
                padding = new RectOffset(12, 12, 6, 6)
            };

            _distStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = distanceColor },
                padding = new RectOffset(8, 8, 2, 2)
            };

            _arrowStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 36,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = directionArrowColor }
            };
        }

        private void OnGUI()
        {
            if (!IsActive) return;

            EnsureStyles();
            DrawCameraBackground();
            ComputeVisibleLabels();
            DrawDestinationLabels();

            if (showRouteDirection && _lastRoute != null && _lastRoute.success)
            {
                DrawRouteBearingIndicator();
            }
        }

        private void DrawCameraBackground()
        {
            if (_webcam == null || !_webcam.isPlaying) return;

            // Handle rotation for mobile camera orientation
            float angle = -_webcam.videoRotationAngle;
            bool mirror = _webcam.videoVerticallyMirrored;

            Matrix4x4 prev = GUI.matrix;
            Vector2 center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

            if (Mathf.Abs(angle) > 0.1f || mirror)
            {
                GUIUtility.RotateAroundPivot(angle, center);
                if (mirror)
                {
                    GUIUtility.ScaleAroundPivot(new Vector2(1f, -1f), center);
                }
            }

            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _webcam, ScaleMode.ScaleAndCrop);
            GUI.matrix = prev;
        }

        private void ComputeVisibleLabels()
        {
            _visibleLabels.Clear();

            if (gpsManager == null || !gpsManager.IsReady) return;
            if (compassManager == null || !compassManager.IsReady) return;
            if (locationManager == null || locationManager.Locations.Count == 0) return;

            double myLat = gpsManager.SmoothedPoint.latitude;
            double myLon = gpsManager.SmoothedPoint.longitude;
            float myHeading = compassManager.SmoothedHeading;

            float pitch = GetDevicePitch();
            float halfH = horizontalFovDegrees * 0.5f;
            float halfV = verticalFovDegrees * 0.5f;

            for (int i = 0; i < locationManager.Locations.Count; i++)
            {
                LocationPoint loc = locationManager.Locations[i];
                if (loc == null || string.IsNullOrWhiteSpace(loc.name)) continue;
                if (loc.gps_lat == 0 && loc.gps_lon == 0) continue;

                float bearing = RouteCalculator.ComputeBearingDegrees(myLat, myLon, loc.gps_lat, loc.gps_lon);
                float distance = RouteCalculator.HaversineDistanceMeters(myLat, myLon, loc.gps_lat, loc.gps_lon);

                if (distance > maxVisibleDistanceMeters || distance < 1f) continue;

                float relativeBearing = Mathf.DeltaAngle(myHeading, bearing);
                if (Mathf.Abs(relativeBearing) > halfH) continue;

                float screenX = (relativeBearing / halfH + 1f) * 0.5f * Screen.width;

                // Use pitch to shift labels vertically — closer to horizon = middle of screen
                float elevAngle = -pitch;
                float screenY = (1f - (elevAngle / halfV + 1f) * 0.5f) * Screen.height;
                // Offset by distance — farther destinations sit closer to horizon
                float distanceFactor = Mathf.Clamp01(distance / maxVisibleDistanceMeters);
                screenY = Mathf.Lerp(Screen.height * 0.35f, Screen.height * 0.5f, distanceFactor);

                float scale = Mathf.Lerp(labelMaxScale, labelMinScale, distanceFactor);

                bool isSelected = !string.IsNullOrWhiteSpace(_selectedDestination) &&
                    string.Equals(loc.name, _selectedDestination, StringComparison.OrdinalIgnoreCase);

                _visibleLabels.Add(new DestinationLabel
                {
                    name = loc.name,
                    bearing = bearing,
                    distance = distance,
                    screenX = screenX,
                    screenY = screenY,
                    scale = scale,
                    isSelected = isSelected
                });
            }
        }

        private void DrawDestinationLabels()
        {
            for (int i = 0; i < _visibleLabels.Count; i++)
            {
                DestinationLabel label = _visibleLabels[i];
                DrawLabel(label);
            }
        }

        private void DrawLabel(DestinationLabel label)
        {
            Matrix4x4 prev = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(label.scale, label.scale, 1f));

            float sx = label.screenX / label.scale;
            float sy = label.screenY / label.scale;

            // Name
            Color origColor = _labelStyle.normal.textColor;
            if (label.isSelected)
            {
                _labelStyle.normal.textColor = selectedLabelColor;
            }

            string distText = label.distance >= 1000f
                ? $"{label.distance / 1000f:F1} km"
                : $"{label.distance:F0} m";

            float etaSeconds = label.distance / 1.0f; // elderly speed
            string etaText = etaSeconds >= 60f
                ? $"~{etaSeconds / 60f:F0} min"
                : $"~{etaSeconds:F0} s";

            GUIContent nameContent = new GUIContent(label.name);
            Vector2 nameSize = _labelStyle.CalcSize(nameContent);
            Rect nameRect = new Rect(sx - nameSize.x * 0.5f, sy - nameSize.y, nameSize.x, nameSize.y);
            GUI.Label(nameRect, nameContent, _labelStyle);

            _labelStyle.normal.textColor = origColor;

            // Distance + ETA
            GUIContent distContent = new GUIContent($"{distText}  {etaText}");
            Vector2 distSize = _distStyle.CalcSize(distContent);
            Rect distRect = new Rect(sx - distSize.x * 0.5f, sy + 2f, distSize.x, distSize.y);
            GUI.Label(distRect, distContent, _distStyle);

            // Direction dot
            float dotSize = 8f;
            Color prevGUIColor = GUI.color;
            GUI.color = label.isSelected ? selectedLabelColor : directionArrowColor;
            if (_pixel != null)
            {
                GUI.DrawTexture(new Rect(sx - dotSize * 0.5f, sy - nameSize.y - dotSize - 4f, dotSize, dotSize), _pixel);
            }
            GUI.color = prevGUIColor;

            GUI.matrix = prev;
        }

        private void DrawRouteBearingIndicator()
        {
            if (gpsManager == null || !gpsManager.IsReady) return;
            if (compassManager == null || !compassManager.IsReady) return;
            if (_lastRoute == null || !_lastRoute.success || _lastRoute.nodePath == null || _lastRoute.nodePath.Count < 2) return;

            // Get the next node on the route to show immediate direction
            // This gives a "turn-by-turn" feel
            float myHeading = compassManager.SmoothedHeading;

            // Show a compass-like bearing indicator at the bottom of screen
            float halfH = horizontalFovDegrees * 0.5f;
            float routeBearing = GetNextRouteBearing();
            if (float.IsNaN(routeBearing)) return;

            float relativeBearing = Mathf.DeltaAngle(myHeading, routeBearing);
            bool inView = Mathf.Abs(relativeBearing) <= halfH;

            float indicatorY = Screen.height - 80f;
            float indicatorX;

            if (inView)
            {
                indicatorX = (relativeBearing / halfH + 1f) * 0.5f * Screen.width;
            }
            else
            {
                indicatorX = relativeBearing > 0 ? Screen.width - 40f : 40f;
            }

            // Draw route direction arrow
            string arrow = inView ? "▼" : (relativeBearing > 0 ? "►" : "◄");
            Color prevColor = GUI.color;
            GUI.color = routeBearingColor;

            GUIStyle arrowDraw = new GUIStyle(_arrowStyle);
            arrowDraw.normal.textColor = routeBearingColor;
            GUI.Label(new Rect(indicatorX - 20f, indicatorY, 40f, 40f), arrow, arrowDraw);

            // Bearing text
            string bearingText = $"{relativeBearing:+0;-0}°";
            GUIStyle bearingStyle = new GUIStyle(_distStyle);
            bearingStyle.normal.textColor = routeBearingColor;
            Vector2 bearingSize = bearingStyle.CalcSize(new GUIContent(bearingText));
            GUI.Label(new Rect(indicatorX - bearingSize.x * 0.5f, indicatorY + 38f, bearingSize.x, bearingSize.y), bearingText, bearingStyle);

            GUI.color = prevColor;
        }

        private float GetNextRouteBearing()
        {
            if (gpsManager == null || !gpsManager.IsReady) return float.NaN;
            if (_lastRoute == null || !_lastRoute.success) return float.NaN;

            double myLat = gpsManager.SmoothedPoint.latitude;
            double myLon = gpsManager.SmoothedPoint.longitude;

            // Find the destination endpoint bearing
            string lastNodeId = _lastRoute.nodePath[_lastRoute.nodePath.Count - 1];
            if (locationManager != null)
            {
                for (int i = 0; i < locationManager.Locations.Count; i++)
                {
                    LocationPoint loc = locationManager.Locations[i];
                    if (loc != null && string.Equals(loc.indoor_node_id, lastNodeId, StringComparison.OrdinalIgnoreCase))
                    {
                        return RouteCalculator.ComputeBearingDegrees(myLat, myLon, loc.gps_lat, loc.gps_lon);
                    }
                }
            }

            return float.NaN;
        }

        private float GetDevicePitch()
        {
            if (_gyroEnabled && SystemInfo.supportsGyroscope)
            {
                Quaternion gyroRot = _gyroOffset * GyroToUnity(Input.gyro.attitude);
                Vector3 euler = gyroRot.eulerAngles;
                float pitch = euler.x;
                if (pitch > 180f) pitch -= 360f;
                return pitch - _userPitchOffset;
            }

            // Fallback: use accelerometer
            Vector3 accel = Input.acceleration;
            float fallbackPitch = Mathf.Atan2(-accel.z, -accel.y) * Mathf.Rad2Deg;
            return fallbackPitch - _userPitchOffset;
        }

        public void SyncGyro()
        {
            if (_gyroEnabled && SystemInfo.supportsGyroscope)
            {
                Quaternion gyroRot = _gyroOffset * GyroToUnity(Input.gyro.attitude);
                Vector3 euler = gyroRot.eulerAngles;
                float rawPitch = euler.x;
                if (rawPitch > 180f) rawPitch -= 360f;
                _userPitchOffset = rawPitch;
            }
            else
            {
                Vector3 accel = Input.acceleration;
                float rawPitch = Mathf.Atan2(-accel.z, -accel.y) * Mathf.Rad2Deg;
                _userPitchOffset = rawPitch;
            }
        }

        private static Quaternion GyroToUnity(Quaternion gyro)
        {
            return new Quaternion(gyro.x, gyro.y, -gyro.z, -gyro.w);
        }
    }
}
