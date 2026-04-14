using UnityEngine;

namespace CDE2501.Wayfinding.Location
{
    public class SimulatedObjectDriver : MonoBehaviour
    {
        [SerializeField] private SimulationProvider simulationProvider;
        [SerializeField] private GPSManager gpsManager;
        [SerializeField] private CompassManager compassManager;
        [SerializeField] private Transform targetTransform;
        [SerializeField] private bool rotateWithHeading = true;
        [SerializeField] private bool applyPitchFromSimulation = true;
        [SerializeField] private bool driveOnlyWhenSimulationModeEnabled = true;
        [SerializeField] private bool lockYToInitial = false;
        [SerializeField] private float metersPerUnityUnit = 1f;

        private GeoPoint _originGeo;
        private Vector3 _originWorld;
        private float _originY;
        private bool _isInitialized;
        private bool _wasDrivingLastFrame;

        private void Awake()
        {
            if (targetTransform == null)
            {
                targetTransform = transform;
            }

            if (simulationProvider == null)
            {
                simulationProvider = FindObjectOfType<SimulationProvider>();
            }

            if (gpsManager == null)
            {
                gpsManager = FindObjectOfType<GPSManager>();
            }

            if (compassManager == null)
            {
                compassManager = FindObjectOfType<CompassManager>();
            }
        }

        private void LateUpdate()
        {
            if (simulationProvider == null || targetTransform == null)
            {
                return;
            }

            if (gpsManager == null)
            {
                gpsManager = FindObjectOfType<GPSManager>();
            }

            if (compassManager == null)
            {
                compassManager = FindObjectOfType<CompassManager>();
            }

            bool isSimulating = simulationProvider.ForceSimulationMode;
            bool hasRealLocation = !isSimulating && gpsManager != null && gpsManager.IsReady;
            bool shouldDrive = !driveOnlyWhenSimulationModeEnabled || isSimulating || hasRealLocation;

            if (!shouldDrive)
            {
                _wasDrivingLastFrame = false;
                return;
            }

            // Re-anchor whenever driving resumes to prevent camera jump.
            if (!_isInitialized || !_wasDrivingLastFrame)
            {
                _originGeo = isSimulating || !hasRealLocation ? simulationProvider.CurrentPoint : gpsManager.SmoothedPoint;
                _originWorld = targetTransform.position;
                _originY = targetTransform.position.y;
                _isInitialized = true;
            }

            if (metersPerUnityUnit <= 0.0001f)
            {
                metersPerUnityUnit = 1f;
            }

            GeoPoint currentGeo = isSimulating || !hasRealLocation ? simulationProvider.CurrentPoint : gpsManager.SmoothedPoint;
            Vector2 offsetMeters = GeoOffsetMeters(_originGeo, currentGeo);
            
            Vector3 newPos = _originWorld + new Vector3(
                offsetMeters.x / metersPerUnityUnit,
                0f,
                offsetMeters.y / metersPerUnityUnit
            );

            if (lockYToInitial)
            {
                newPos.y = _originY;
            }
            else
            {
                float simVerticalOffset = isSimulating ? simulationProvider.VerticalOffsetMeters : 0f;
                newPos.y = _originY + (simVerticalOffset / metersPerUnityUnit);
            }

            targetTransform.position = newPos;

            if (rotateWithHeading)
            {
                float currentHeading = isSimulating || compassManager == null || !compassManager.IsReady ? 
                    simulationProvider.CurrentHeading : compassManager.SmoothedHeading;
                float currentPitch = isSimulating ? -simulationProvider.CurrentPitch : targetTransform.rotation.eulerAngles.x;

                targetTransform.rotation = Quaternion.Euler(
                    applyPitchFromSimulation && isSimulating ? currentPitch : targetTransform.rotation.eulerAngles.x, 
                    currentHeading, 
                    targetTransform.rotation.eulerAngles.z
                );
            }

            _wasDrivingLastFrame = true;
        }

        public void SetSimulationProvider(SimulationProvider provider)
        {
            simulationProvider = provider;
            _isInitialized = false;
        }

        public void SetTarget(Transform target)
        {
            targetTransform = target;
            _isInitialized = false;
        }

        public void SetLockY(bool shouldLock)
        {
            lockYToInitial = shouldLock;
        }

        public void ReanchorToCurrentPose()
        {
            if (simulationProvider == null || targetTransform == null)
            {
                return;
            }

            bool isSimulating = simulationProvider.ForceSimulationMode;
            bool hasRealLocation = !isSimulating && gpsManager != null && gpsManager.IsReady;

            _originGeo = isSimulating || !hasRealLocation ? simulationProvider.CurrentPoint : gpsManager.SmoothedPoint;
            _originWorld = targetTransform.position;
            _originY = targetTransform.position.y;
            _isInitialized = true;
            _wasDrivingLastFrame = true;
        }

        private static Vector2 GeoOffsetMeters(GeoPoint origin, GeoPoint current)
        {
            const double metersPerDegreeLat = 111320.0;
            double avgLatRad = ((origin.latitude + current.latitude) * 0.5) * Mathf.Deg2Rad;
            double metersPerDegreeLon = metersPerDegreeLat * System.Math.Cos(avgLatRad);

            double dLat = current.latitude - origin.latitude;
            double dLon = current.longitude - origin.longitude;

            float eastMeters = (float)(dLon * metersPerDegreeLon);
            float northMeters = (float)(dLat * metersPerDegreeLat);
            return new Vector2(eastMeters, northMeters);
        }
    }
}
