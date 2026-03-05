using UnityEngine;

namespace CDE2501.Wayfinding.Location
{
    public class SimulationProvider : MonoBehaviour
    {
        [Header("Mode")]
        [SerializeField] private bool forceSimulationMode = true;
        [SerializeField] private bool showDebugOverlay = true;

        [Header("Start State")]
        [SerializeField] private double startLatitude = 1.294550851849307;
        [SerializeField] private double startLongitude = 103.8060771559821;
        [SerializeField] private float startHeading = 0f;

        [Header("Movement")]
        [SerializeField] private float moveSpeedMetersPerSecond = 1.2f;
        [SerializeField] private float verticalSpeedMetersPerSecond = 1.2f;
        [SerializeField, Min(1f)] private float sprintMultiplier = 2.5f;
        [SerializeField] private float turnSpeedDegreesPerSecond = 90f;
        [SerializeField] private float pitchSpeedDegreesPerSecond = 80f;
        [SerializeField] private float minPitchDegrees = -80f;
        [SerializeField] private float maxPitchDegrees = 80f;

        [Header("Hotkeys")]
        [SerializeField] private KeyCode toggleModeKey = KeyCode.F2;
        [SerializeField] private KeyCode toggleOverlayKey = KeyCode.F1;
        [Header("Overlay")]
        [SerializeField, Range(0.8f, 2.5f)] private float overlayScale = 1.0f;
        [SerializeField, Range(14, 48)] private int titleFontSize = 28;
        [SerializeField, Range(12, 40)] private int bodyFontSize = 22;
        [SerializeField] private Vector2 panelSize = new Vector2(840f, 220f);
        [SerializeField] private Vector2 panelStartPosition = new Vector2(120f, 12f);

        private GeoPoint _currentPoint;
        private float _currentHeading;
        private float _currentPitch;
        private float _verticalOffsetMeters;
        private GUIStyle _panelStyle;
        private GUIStyle _bodyStyle;
        private Texture2D _panelTexture;
        private Rect _panelRect;
        private bool _panelRectInitialized;
        private Vector2 _panelScroll;

        public GeoPoint CurrentPoint => _currentPoint;
        public float CurrentHeading => NormalizeHeading(_currentHeading);
        public float CurrentPitch => _currentPitch;
        public float VerticalOffsetMeters => _verticalOffsetMeters;

        public bool ForceSimulationMode
        {
            get => forceSimulationMode;
            set => forceSimulationMode = value;
        }

        private void Awake()
        {
            _currentPoint = new GeoPoint(startLatitude, startLongitude);
            _currentHeading = NormalizeHeading(startHeading);
            _currentPitch = 0f;
        }

        private void Update()
        {
            if (Input.GetKeyDown(toggleModeKey))
            {
                forceSimulationMode = !forceSimulationMode;
            }

            if (Input.GetKeyDown(toggleOverlayKey))
            {
                showDebugOverlay = !showDebugOverlay;
            }

            if (!forceSimulationMode)
            {
                return;
            }

            float dt = Time.deltaTime;
            float turnInput = 0f;
            if (Input.GetKey(KeyCode.A))
            {
                turnInput -= 1f;
            }
            if (Input.GetKey(KeyCode.D))
            {
                turnInput += 1f;
            }

            _currentHeading = NormalizeHeading(_currentHeading + turnInput * turnSpeedDegreesPerSecond * dt);

            float pitchInput = 0f;
            if (Input.GetKey(KeyCode.W))
            {
                pitchInput += 1f;
            }
            if (Input.GetKey(KeyCode.S))
            {
                pitchInput -= 1f;
            }
            _currentPitch = Mathf.Clamp(_currentPitch + pitchInput * pitchSpeedDegreesPerSecond * dt, minPitchDegrees, maxPitchDegrees);

            float forwardInput = 0f;
            if (Input.GetKey(KeyCode.UpArrow))
            {
                forwardInput += 1f;
            }
            if (Input.GetKey(KeyCode.DownArrow))
            {
                forwardInput -= 1f;
            }

            float strafeInput = 0f;
            if (Input.GetKey(KeyCode.RightArrow))
            {
                strafeInput += 1f;
            }
            if (Input.GetKey(KeyCode.LeftArrow))
            {
                strafeInput -= 1f;
            }

            float verticalInput = 0f;
            if (Input.GetKey(KeyCode.PageUp))
            {
                verticalInput += 1f;
            }
            if (Input.GetKey(KeyCode.PageDown))
            {
                verticalInput -= 1f;
            }

            bool sprinting = IsSprintPressed();
            float speedScale = sprinting ? Mathf.Max(1f, sprintMultiplier) : 1f;

            _verticalOffsetMeters += verticalInput * verticalSpeedMetersPerSecond * speedScale * dt;

            Vector2 moveLocal = new Vector2(strafeInput, forwardInput);
            if (moveLocal.sqrMagnitude > 1f)
            {
                moveLocal.Normalize();
            }

            float meters = moveSpeedMetersPerSecond * speedScale * dt;
            float forwardMeters = moveLocal.y * meters;
            float strafeMeters = moveLocal.x * meters;

            float headingRad = _currentHeading * Mathf.Deg2Rad;
            double eastMeters = (forwardMeters * Mathf.Sin(headingRad)) + (strafeMeters * Mathf.Cos(headingRad));
            double northMeters = (forwardMeters * Mathf.Cos(headingRad)) - (strafeMeters * Mathf.Sin(headingRad));

            _currentPoint = OffsetLatLon(_currentPoint, northMeters, eastMeters);
        }

        private void OnGUI()
        {
            if (!showDebugOverlay)
            {
                return;
            }

            EnsureOverlayStyles();

            float scale = Mathf.Clamp(overlayScale, 0.8f, 2.5f);
            Matrix4x4 prev = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

            EnsurePanelRect(scale);
            _panelRect = GUI.Window(GetInstanceID(), _panelRect, DrawPanelWindow, "PC Simulation", _panelStyle);

            GUI.matrix = prev;
        }

        private void DrawPanelWindow(int id)
        {
            bool sprinting = IsSprintPressed();
            float speedScale = sprinting ? Mathf.Max(1f, sprintMultiplier) : 1f;

            string text =
                $"Mode: {(forceSimulationMode ? "Simulation" : "Device Sensors")}\n" +
                $"Lat: {_currentPoint.latitude:F7}\n" +
                $"Lon: {_currentPoint.longitude:F7}\n" +
                $"Heading: {CurrentHeading:F1} deg\n" +
                $"Pitch: {CurrentPitch:F1} deg\n" +
                $"Height Offset: {VerticalOffsetMeters:F1} m\n" +
                $"Speed: {(sprinting ? "Sprint" : "Normal")} x{speedScale:0.0}\n" +
                "Controls: Arrows move, Shift sprint, A/D look left-right, W/S look up-down, PgUp/PgDn height, F2 mode, F1 panel";

            Rect viewport = new Rect(12f, 36f, _panelRect.width - 24f, _panelRect.height - 44f);
            float contentHeight = Mathf.Max(viewport.height, _bodyStyle.CalcHeight(new GUIContent(text), viewport.width - 16f) + 12f);
            Rect contentRect = new Rect(0f, 0f, viewport.width - 16f, contentHeight);
            _panelScroll = GUI.BeginScrollView(viewport, _panelScroll, contentRect);
            GUI.Label(new Rect(0f, 0f, contentRect.width, contentRect.height), text, _bodyStyle);
            GUI.EndScrollView();
            GUI.DragWindow(new Rect(0f, 0f, _panelRect.width, 30f));
        }

        private static bool IsSprintPressed()
        {
            return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        }

        private void EnsurePanelRect(float scale)
        {
            float virtualScreenWidth = Screen.width / scale;
            float width = Mathf.Min(virtualScreenWidth - 24f, panelSize.x);

            if (!_panelRectInitialized)
            {
                float startX = panelStartPosition.x;
                if (startX <= 0f)
                {
                    startX = (virtualScreenWidth - width) * 0.5f;
                }

                _panelRect = new Rect(startX, panelStartPosition.y, width, panelSize.y);
                _panelRectInitialized = true;
            }
            else
            {
                _panelRect.width = width;
                _panelRect.height = panelSize.y;
            }

            _panelRect.x = Mathf.Clamp(_panelRect.x, 0f, Mathf.Max(0f, virtualScreenWidth - _panelRect.width));
            _panelRect.y = Mathf.Clamp(_panelRect.y, 0f, Mathf.Max(0f, (Screen.height / scale) - _panelRect.height));
        }

        private void EnsureOverlayStyles()
        {
            if (_panelStyle != null && _bodyStyle != null)
            {
                return;
            }

            _panelTexture = MakeSolidTexture(new Color(0f, 0f, 0f, 0.76f));

            _panelStyle = new GUIStyle(GUI.skin.box)
            {
                normal = { background = _panelTexture },
                border = new RectOffset(8, 8, 8, 8),
                padding = new RectOffset(0, 0, 0, 0),
                fontSize = titleFontSize,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.UpperCenter
            };
            _panelStyle.normal.textColor = Color.white;

            _bodyStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = bodyFontSize,
                alignment = TextAnchor.UpperLeft,
                wordWrap = true
            };
            _bodyStyle.normal.textColor = Color.white;
        }

        private static Texture2D MakeSolidTexture(Color color)
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

        private static GeoPoint OffsetLatLon(GeoPoint origin, double northMeters, double eastMeters)
        {
            const double metersPerDegreeLat = 111320.0;
            double latRad = origin.latitude * Mathf.Deg2Rad;
            double metersPerDegreeLon = metersPerDegreeLat * System.Math.Cos(latRad);
            if (System.Math.Abs(metersPerDegreeLon) < 0.000001)
            {
                metersPerDegreeLon = 0.000001;
            }

            double deltaLat = northMeters / metersPerDegreeLat;
            double deltaLon = eastMeters / metersPerDegreeLon;

            return new GeoPoint(origin.latitude + deltaLat, origin.longitude + deltaLon);
        }

        private static float NormalizeHeading(float heading)
        {
            float normalized = heading % 360f;
            return normalized < 0f ? normalized + 360f : normalized;
        }
    }
}
