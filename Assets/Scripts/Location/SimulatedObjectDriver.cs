using UnityEngine;

namespace CDE2501.Wayfinding.Location
{
    public class SimulatedObjectDriver : MonoBehaviour
    {
        [SerializeField] private SimulationProvider simulationProvider;
        [SerializeField] private Transform targetTransform;
        [SerializeField] private bool rotateWithHeading = true;
        [SerializeField] private bool applyPitchFromSimulation = true;
        [SerializeField] private bool lockYToInitial = false;
        [SerializeField] private float metersPerUnityUnit = 1f;

        private GeoPoint _originGeo;
        private Vector3 _originWorld;
        private float _originY;
        private bool _isInitialized;

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
        }

        private void LateUpdate()
        {
            if (simulationProvider == null || targetTransform == null)
            {
                return;
            }

            if (!_isInitialized)
            {
                _originGeo = simulationProvider.CurrentPoint;
                _originWorld = targetTransform.position;
                _originY = targetTransform.position.y;
                _isInitialized = true;
            }

            if (metersPerUnityUnit <= 0.0001f)
            {
                metersPerUnityUnit = 1f;
            }

            Vector2 offsetMeters = GeoOffsetMeters(_originGeo, simulationProvider.CurrentPoint);
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
                newPos.y = _originY + (simulationProvider.VerticalOffsetMeters / metersPerUnityUnit);
            }

            targetTransform.position = newPos;

            if (rotateWithHeading)
            {
                Vector3 euler = targetTransform.rotation.eulerAngles;
                float pitch = applyPitchFromSimulation ? -simulationProvider.CurrentPitch : euler.x;
                targetTransform.rotation = Quaternion.Euler(pitch, simulationProvider.CurrentHeading, euler.z);
            }
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
