using System;
using System.Collections.Generic;
using CDE2501.Wayfinding.Data;
using CDE2501.Wayfinding.Location;
using CDE2501.Wayfinding.Routing;
using UnityEngine;

namespace CDE2501.Wayfinding.AR
{
    /// <summary>
    /// Flight-tracker style AR overlay using WebCamTexture + device compass/gyroscope.
    /// Correct Android gyro-to-Unity transform, pitch-driven label vertical placement,
    /// and a persistent status HUD drawn inside the AR view itself.
    /// </summary>
    public class FlightTrackerARView : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] private GPSManager gpsManager;
        [SerializeField] private CompassManager compassManager;
        [SerializeField] private LocationManager locationManager;
        [SerializeField] private RouteCalculator routeCalculator;

        [Header("Camera FOV")]
        [SerializeField] private float horizontalFovDegrees = 68f;
        [SerializeField] private float verticalFovDegrees   = 50f;

        [Header("Label Appearance")]
        [SerializeField] private float maxVisibleDistanceMeters = 2000f;
        [SerializeField] private float labelMinScale  = 0.65f;
        [SerializeField] private float labelMaxScale  = 1.4f;
        [SerializeField] private Color labelColor         = Color.white;
        [SerializeField] private Color labelBgColor       = new Color(0f, 0f, 0f, 0.72f);
        [SerializeField] private Color selectedLabelColor = new Color(1f, 0.55f, 0.05f, 1f);
        [SerializeField] private Color distanceColor      = new Color(0.6f, 0.9f, 1f, 1f);
        [SerializeField] private Color accentColor        = new Color(0.1f, 0.85f, 1f, 1f);

        [Header("Route Overlay")]
        [SerializeField] private bool showRouteDirection = true;
        [SerializeField] private Color routeArrowColor   = new Color(1f, 0.3f, 0.1f, 0.95f);

        [Header("Gyro Smoothing")]
        [SerializeField, Range(0.01f, 1f)] private float pitchSmoothingAlpha = 0.15f;

        // ── public state ──────────────────────────────────────────────────────────
        public bool IsActive { get; private set; }

        // ── private state ──────────────────────────────────────────────────────────
        private WebCamTexture _webcam;

        // gyro
        private bool      _gyroEnabled;
        private float     _rawPitch;       // degrees, updated every frame (gyro or accel)
        private float     _smoothPitch;    // exponentially smoothed
        private float     _pitchOffset;    // set by SyncGyro / auto-init

        // selection / route
        private string      _selectedDestination;
        private RouteResult _lastRoute;
        private Coroutine _cameraPermissionRoutine;

        // textures / styles (created lazily on first OnGUI)
        private Texture2D _bgTex;
        private Texture2D _whiteTex;
        private Texture2D _hudBgTex;
        private GUIStyle  _labelStyle;
        private GUIStyle  _subStyle;
        private GUIStyle  _hudStyle;
        private GUIStyle  _arrowStyle;

        // scratch list – re-used every frame to avoid GC
        private readonly List<DestinationLabel> _visibleLabels = new List<DestinationLabel>(32);

        private bool _gyroAutoSynced; // true once first auto-sync has run
        private float _arActivatedAtTime;

        // ── inner struct ──────────────────────────────────────────────────────────
        private struct DestinationLabel
        {
            public string name;
            public float  bearing;
            public float  distance;
            public float  screenX;
            public float  screenY;
            public float  scale;
            public bool   isSelected;
        }

        // ═════════════════════════════════════════════════════════════════════════
        // Unity lifecycle
        // ═════════════════════════════════════════════════════════════════════════

        private void Awake()
        {
            if (gpsManager     == null) gpsManager     = FindObjectOfType<GPSManager>();
            if (compassManager == null) compassManager = FindObjectOfType<CompassManager>();
            if (locationManager == null) locationManager = FindObjectOfType<LocationManager>();
            if (routeCalculator == null) routeCalculator = FindObjectOfType<RouteCalculator>();
        }

        private void OnEnable()
        {
            if (routeCalculator != null)
                routeCalculator.OnRouteUpdated += OnRouteUpdated;
        }

        private void OnDisable()
        {
            if (routeCalculator != null)
                routeCalculator.OnRouteUpdated -= OnRouteUpdated;
        }

        private void Update()
        {
            if (!IsActive) return;
            UpdatePitch();
        }

        private void OnDestroy()
        {
            StopCameraPermissionRoutine();
            StopCameraInternal();
            DestroyTex(ref _bgTex);
            DestroyTex(ref _whiteTex);
            DestroyTex(ref _hudBgTex);
        }

        // ═════════════════════════════════════════════════════════════════════════
        // Public API
        // ═════════════════════════════════════════════════════════════════════════

        public void Activate()
        {
            if (IsActive) return;
            IsActive = true;
            _arActivatedAtTime = Time.time;
            _gyroAutoSynced = false;
            StartCamera();
            EnableGyro();
        }

        public void Deactivate()
        {
            if (!IsActive) return;
            IsActive = false;
            StopCameraPermissionRoutine();
            StopCameraInternal();
        }

        public void Toggle()
        {
            if (IsActive) Deactivate();
            else          Activate();
        }

        public void SetSelectedDestination(string destinationName)
        {
            _selectedDestination = destinationName;
        }

        /// <summary>
        /// Zeros the pitch so that the current phone tilt becomes "level".
        /// Call this whenever the user has raised/lowered the phone to a comfortable viewing angle.
        /// </summary>
        public void SyncGyro()
        {
            _pitchOffset = _rawPitch;
            Debug.Log($"[FlightTrackerARView] SyncGyro: offset set to {_pitchOffset:F1}°");
        }

        // ═════════════════════════════════════════════════════════════════════════
        // Camera
        // ═════════════════════════════════════════════════════════════════════════

        private void StartCamera()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera))
            {
                UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Camera);
                if (_cameraPermissionRoutine == null)
                {
                    _cameraPermissionRoutine = StartCoroutine(CameraPermissionRoutine());
                }
                return;
            }
#endif
            StartCameraInternal();
        }

        private void StartCameraInternal()
        {
            if (_webcam != null && _webcam.isPlaying) return;

            WebCamDevice[] devices = WebCamTexture.devices;
            if (devices.Length == 0)
            {
                Debug.LogWarning("[FlightTrackerARView] No camera found.");
                return;
            }

            string backName = null;
            foreach (var d in devices)
            {
                if (!d.isFrontFacing) { backName = d.name; break; }
            }

            _webcam = new WebCamTexture(backName ?? devices[0].name, Screen.width, Screen.height, 30);
            _webcam.Play();
            Debug.Log($"[FlightTrackerARView] Camera started: {_webcam.deviceName}");
        }

        private void StopCameraInternal()
        {
            if (_webcam == null) return;
            _webcam.Stop();
            Destroy(_webcam);
            _webcam = null;
        }

        private void StopCameraPermissionRoutine()
        {
            if (_cameraPermissionRoutine == null)
            {
                return;
            }

            StopCoroutine(_cameraPermissionRoutine);
            _cameraPermissionRoutine = null;
        }

        private System.Collections.IEnumerator CameraPermissionRoutine()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            while (IsActive && !UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera))
                yield return new WaitForSeconds(0.5f);

            if (IsActive && UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera))
            {
                StartCameraInternal();
            }

            _cameraPermissionRoutine = null;
            yield break;
#else
            _cameraPermissionRoutine = null;
            yield break;
#endif
        }

        // ═════════════════════════════════════════════════════════════════════════
        // Gyroscope / pitch
        // ═════════════════════════════════════════════════════════════════════════

        private void EnableGyro()
        {
            if (SystemInfo.supportsGyroscope)
            {
                Input.gyro.enabled = true;
                _gyroEnabled = true;
            }
            _smoothPitch = 0f;
            _rawPitch    = 0f;
        }

        private void UpdatePitch()
        {
            float newRaw;

            if (_gyroEnabled && SystemInfo.supportsGyroscope)
            {
                // Android → Unity coordinate conversion:
                //   Android gyro uses a right-handed system with Z pointing out of the screen.
                //   Unity uses left-handed. The standard conversion for portrait-landscape is:
                //     unityQ = Quaternion(-gx, -gy, gz, gw)
                //   Then we rotate by -90° around X to go from landscape-native to portrait-up.
                Quaternion raw    = Input.gyro.attitude;
                Quaternion unity  = new Quaternion(-raw.x, -raw.y, raw.z, raw.w);
                Quaternion adjust = Quaternion.Euler(-90f, 0f, 0f); // portrait correction
                Quaternion final  = adjust * unity;

                // Extract pitch (X rotation in Unity = tilt forward/back)
                Vector3 euler = final.eulerAngles;
                float px = euler.x;
                if (px > 180f) px -= 360f; // wrap to [-180, 180]
                newRaw = px;
            }
            else
            {
                // Accelerometer fallback: phone laying flat = 0, pointing up = -90
                Vector3 g = Input.acceleration;
                newRaw = Mathf.Atan2(-g.z, Mathf.Sqrt(g.x * g.x + g.y * g.y)) * Mathf.Rad2Deg;
            }

            _rawPitch    = newRaw;
            _smoothPitch = Mathf.LerpAngle(_smoothPitch, _rawPitch, pitchSmoothingAlpha);

            // Auto-sync once we have a stable first reading after AR activation.
            if (!_gyroAutoSynced && (Time.time - _arActivatedAtTime) > 1.5f)
            {
                _pitchOffset    = _smoothPitch;
                _gyroAutoSynced = true;
            }
        }

        /// <summary>Corrected pitch in degrees relative to the user's synced zero angle.</summary>
        private float CorrectedPitch => _smoothPitch - _pitchOffset;

        // ═════════════════════════════════════════════════════════════════════════
        // Route callback
        // ═════════════════════════════════════════════════════════════════════════

        private void OnRouteUpdated(RouteResult result) => _lastRoute = result;

        // ═════════════════════════════════════════════════════════════════════════
        // Rendering
        // ═════════════════════════════════════════════════════════════════════════

        private void OnGUI()
        {
            if (!IsActive) return;

            EnsureStyles();
            DrawCameraBackground();
            ComputeLabels();
            DrawLabels();

            if (showRouteDirection && _lastRoute != null && _lastRoute.success)
                DrawRouteBearing();

            DrawStatusHUD();
        }

        // ── camera background ─────────────────────────────────────────────────────

        private void DrawCameraBackground()
        {
            if (_webcam == null || !_webcam.isPlaying) return;

            float  angle  = -_webcam.videoRotationAngle;
            bool   mirror = _webcam.videoVerticallyMirrored;
            Vector2 center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

            Matrix4x4 prev = GUI.matrix;
            if (Mathf.Abs(angle) > 0.5f)
                GUIUtility.RotateAroundPivot(angle, center);
            if (mirror)
                GUIUtility.ScaleAroundPivot(new Vector2(1f, -1f), center);

            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _webcam, ScaleMode.ScaleAndCrop);
            GUI.matrix = prev;
        }

        // ── destination labels ────────────────────────────────────────────────────

        private void ComputeLabels()
        {
            _visibleLabels.Clear();

            if (gpsManager     == null || !gpsManager.IsReady)     return;
            if (compassManager == null || !compassManager.IsReady)  return;
            if (locationManager == null || locationManager.Locations.Count == 0) return;

            double myLat     = gpsManager.SmoothedPoint.latitude;
            double myLon     = gpsManager.SmoothedPoint.longitude;
            float  myHeading = compassManager.SmoothedHeading;
            float  pitch     = CorrectedPitch;            // positive = tilted up (phone held up)

            float halfH = horizontalFovDegrees * 0.5f;
            float halfV = verticalFovDegrees   * 0.5f;

            foreach (LocationPoint loc in locationManager.Locations)
            {
                if (loc == null || string.IsNullOrWhiteSpace(loc.name)) continue;
                if (loc.gps_lat == 0 && loc.gps_lon == 0) continue;

                float bearing  = RouteCalculator.ComputeBearingDegrees(myLat, myLon, loc.gps_lat, loc.gps_lon);
                float distance = RouteCalculator.HaversineDistanceMeters(myLat, myLon, loc.gps_lat, loc.gps_lon);

                if (distance > maxVisibleDistanceMeters || distance < 1f) continue;

                float relBearing = Mathf.DeltaAngle(myHeading, bearing);
                if (Mathf.Abs(relBearing) > halfH) continue;

                // ── Horizontal position: linear mapping from FOV to screen width
                float sx = (relBearing / halfH + 1f) * 0.5f * Screen.width;

                // ── Vertical position: driven by gyro pitch.
                //    pitch = 0  → label at screen centre (horizon)
                //    pitch > 0  → phone tilted up → label moves UP
                //    pitch < 0  → phone tilted down → label moves DOWN
                //    Clamp so labels never leave the usable screen area.
                float normV = Mathf.Clamp(pitch / halfV, -1f, 1f);       // [-1, 1]
                float sy    = (0.5f - normV * 0.5f) * Screen.height;     // screen Y

                // Keep labels away from extreme edges
                sy = Mathf.Clamp(sy, Screen.height * 0.1f, Screen.height * 0.85f);

                float distFactor = Mathf.Clamp01(distance / maxVisibleDistanceMeters);
                float scale = Mathf.Lerp(labelMaxScale, labelMinScale, distFactor);

                bool isSelected = !string.IsNullOrWhiteSpace(_selectedDestination) &&
                    string.Equals(loc.name, _selectedDestination, StringComparison.OrdinalIgnoreCase);

                _visibleLabels.Add(new DestinationLabel
                {
                    name       = loc.name,
                    bearing    = bearing,
                    distance   = distance,
                    screenX    = sx,
                    screenY    = sy,
                    scale      = scale,
                    isSelected = isSelected
                });
            }
        }

        private void DrawLabels()
        {
            foreach (var lbl in _visibleLabels)
                DrawOneLabel(lbl);
        }

        private void DrawOneLabel(DestinationLabel lbl)
        {
            // Scale the GUI matrix around the label's screen position for size variation
            Matrix4x4 prev = GUI.matrix;
            Vector2 pivot = new Vector2(lbl.screenX, lbl.screenY);
            GUIUtility.ScaleAroundPivot(new Vector2(lbl.scale, lbl.scale), pivot);

            float sx = lbl.screenX;
            float sy = lbl.screenY;

            // --- Pill background ---
            float pilW = 180f;
            float pilH = 52f;
            Rect pilRect = new Rect(sx - pilW * 0.5f, sy - pilH * 0.5f, pilW, pilH);
            Color prevBg = GUI.backgroundColor;
            GUI.color = lbl.isSelected
                ? new Color(selectedLabelColor.r, selectedLabelColor.g, selectedLabelColor.b, 0.82f)
                : new Color(0f, 0f, 0f, 0.72f);
            GUI.DrawTexture(pilRect, _bgTex, ScaleMode.StretchToFill);
            GUI.color = Color.white;

            // --- Name ---
            Color nameCol = lbl.isSelected ? selectedLabelColor : labelColor;
            _labelStyle.normal.textColor = nameCol;
            GUI.Label(new Rect(sx - 86f, sy - 26f, 172f, 26f), lbl.name, _labelStyle);

            // --- Distance + ETA ---
            string distTxt = lbl.distance >= 1000f
                ? $"{lbl.distance / 1000f:F1} km"
                : $"{lbl.distance:F0} m";
            float etaSec = lbl.distance / 1.0f;
            string etaTxt = etaSec >= 60f ? $"~{etaSec / 60f:F0} min" : $"~{etaSec:F0} s";
            GUI.Label(new Rect(sx - 86f, sy, 172f, 24f), $"{distTxt}  {etaTxt}", _subStyle);

            GUI.matrix = prev;
        }

        // ── route bearing ─────────────────────────────────────────────────────────

        private void DrawRouteBearing()
        {
            if (gpsManager == null || !gpsManager.IsReady) return;
            if (compassManager == null || !compassManager.IsReady) return;
            if (_lastRoute == null || !_lastRoute.success ||
                _lastRoute.nodePath == null || _lastRoute.nodePath.Count < 2) return;

            float routeBearing = GetNextRouteBearing();
            if (float.IsNaN(routeBearing)) return;

            float myHeading   = compassManager.SmoothedHeading;
            float relBearing  = Mathf.DeltaAngle(myHeading, routeBearing);
            float halfH       = horizontalFovDegrees * 0.5f;
            bool  inView      = Mathf.Abs(relBearing) <= halfH;

            float indY = Screen.height - 90f;
            float indX = inView
                ? (relBearing / halfH + 1f) * 0.5f * Screen.width
                : (relBearing > 0f ? Screen.width - 50f : 50f);

            // Arrow + bearing
            string arrow = inView ? "▼" : (relBearing > 0f ? "►" : "◄");

            _arrowStyle.normal.textColor = routeArrowColor;
            GUI.Label(new Rect(indX - 24f, indY - 40f, 48f, 48f), arrow, _arrowStyle);

            string bearingTxt = $"{relBearing:+0;-0}°";
            _subStyle.normal.textColor = routeArrowColor;
            Vector2 bsz = _subStyle.CalcSize(new GUIContent(bearingTxt));
            GUI.Label(new Rect(indX - bsz.x * 0.5f, indY + 10f, bsz.x, bsz.y), bearingTxt, _subStyle);
            _subStyle.normal.textColor = distanceColor; // reset
        }

        private float GetNextRouteBearing()
        {
            if (gpsManager == null || !gpsManager.IsReady) return float.NaN;
            if (_lastRoute == null || !_lastRoute.success) return float.NaN;

            double myLat = gpsManager.SmoothedPoint.latitude;
            double myLon = gpsManager.SmoothedPoint.longitude;

            string lastNodeId = _lastRoute.nodePath[_lastRoute.nodePath.Count - 1];
            if (locationManager == null) return float.NaN;

            foreach (var loc in locationManager.Locations)
            {
                if (loc != null && string.Equals(loc.indoor_node_id, lastNodeId, StringComparison.OrdinalIgnoreCase))
                    return RouteCalculator.ComputeBearingDegrees(myLat, myLon, loc.gps_lat, loc.gps_lon);
            }
            return float.NaN;
        }

        // ── status HUD ────────────────────────────────────────────────────────────

        private void DrawStatusHUD()
        {
            // Small pill in the top-right corner showing GPS / Compass / Gyro status
            bool gpsOk     = gpsManager     != null && gpsManager.IsReady;
            bool compassOk = compassManager  != null && compassManager.IsReady;

            string gyroLabel = _gyroEnabled
                ? (_gyroAutoSynced ? $"Gyro ✓ {CorrectedPitch:+0;-0}°" : "Gyro init…")
                : $"Accel {CorrectedPitch:+0;-0}°";

            string line1 = $"GPS {(gpsOk ? "✓" : "…")}  Compass {(compassOk ? "✓" : "…")}";
            string line2 = gyroLabel;
            if (_webcam == null || !_webcam.isPlaying) line2 += "  CAM ✗";

            float pw = 230f, ph = 50f;
            float px = Screen.width - pw - 8f, py = 8f;

            GUI.color = new Color(0f, 0f, 0f, 0.65f);
            GUI.DrawTexture(new Rect(px, py, pw, ph), _hudBgTex, ScaleMode.StretchToFill);
            GUI.color = Color.white;

            _hudStyle.normal.textColor = gpsOk ? new Color(0.3f, 1f, 0.5f) : new Color(1f, 0.6f, 0.1f);
            GUI.Label(new Rect(px + 8f, py + 4f, pw - 16f, 22f), line1, _hudStyle);
            _hudStyle.normal.textColor = new Color(0.7f, 0.9f, 1f);
            GUI.Label(new Rect(px + 8f, py + 24f, pw - 16f, 22f), line2, _hudStyle);
        }

        // ═════════════════════════════════════════════════════════════════════════
        // Style initialisation
        // ═════════════════════════════════════════════════════════════════════════

        private void EnsureStyles()
        {
            if (_labelStyle != null) return;

            _bgTex    = MakeTex(new Color(0f, 0f, 0f, 0.72f));
            _whiteTex = MakeTex(Color.white);
            _hudBgTex = MakeTex(new Color(0f, 0f, 0f, 0.65f));

            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize  = 20,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                padding   = new RectOffset(0, 0, 0, 0)
            };
            _labelStyle.normal.textColor = labelColor;

            _subStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize  = 15,
                alignment = TextAnchor.MiddleCenter,
                padding   = new RectOffset(0, 0, 0, 0)
            };
            _subStyle.normal.textColor = distanceColor;

            _hudStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize  = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft
            };
            _hudStyle.normal.textColor = Color.white;

            _arrowStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize  = 40,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            _arrowStyle.normal.textColor = routeArrowColor;
        }

        // ═════════════════════════════════════════════════════════════════════════
        // Helpers
        // ═════════════════════════════════════════════════════════════════════════

        private static Texture2D MakeTex(Color c)
        {
            var t = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                wrapMode   = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point
            };
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        private static void DestroyTex(ref Texture2D t)
        {
            if (t != null) { Destroy(t); t = null; }
        }
    }
}
