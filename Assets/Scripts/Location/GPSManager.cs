using System;
using System.Collections;
using UnityEngine;

namespace CDE2501.Wayfinding.Location
{
    public class GPSManager : MonoBehaviour
    {
        [Header("Source")]
        [SerializeField] private SensorSourceMode sourceMode = SensorSourceMode.Auto;
        [SerializeField] private SimulationProvider simulationProvider;
        [SerializeField] private bool fallbackToSimulationIfUnavailable = true;
        [SerializeField] private double fallbackSimLatitude = 1.3521;
        [SerializeField] private double fallbackSimLongitude = 103.8198;

        [Header("Smoothing")]
        [SerializeField] private float smoothingAlpha = 0.3f;
        [SerializeField] private float minUpdateDistanceMeters = 1f;
        [Header("Device Location")]
        [SerializeField] private float desiredAccuracyMeters = 10f;
        [SerializeField] private float updateDistanceMeters = 1f;

        public bool IsReady { get; private set; }
        public bool IsUsingSimulation { get; private set; }
        public GeoPoint RawPoint { get; private set; }
        public GeoPoint SmoothedPoint { get; private set; }

        public event Action<GeoPoint, GeoPoint> OnLocationUpdated;

        private Coroutine _gpsCoroutine;
        private bool _hasInitial;
        private bool _locationServiceStarted;
        private float _locationInitStartedTime;
        private const float InitTimeoutSeconds = 20f;

        private void Awake()
        {
            if (simulationProvider == null)
            {
                simulationProvider = FindObjectOfType<SimulationProvider>();
            }
        }

        private void OnEnable()
        {
            _gpsCoroutine = StartCoroutine(StartLocationRoutine());
        }

        private void OnDisable()
        {
            if (_gpsCoroutine != null)
            {
                StopCoroutine(_gpsCoroutine);
            }

            StopLocationServiceIfNeeded();

            IsReady = false;
            IsUsingSimulation = false;
            _hasInitial = false;
        }

        private IEnumerator StartLocationRoutine()
        {
            var wait = new WaitForSeconds(0.5f);
            while (enabled)
            {
                bool useSimulation = ResolveUseSimulationMode();
                IsUsingSimulation = useSimulation;

                if (useSimulation)
                {
                    StopLocationServiceIfNeeded();
                    ReadSimulationLocation();
                    yield return wait;
                    continue;
                }

                if (!TryReadDeviceLocation())
                {
                    yield return wait;
                    continue;
                }

                yield return wait;
            }
        }

        public void SetSourceMode(SensorSourceMode mode)
        {
            sourceMode = mode;
        }

        private bool ResolveUseSimulationMode()
        {
            if (simulationProvider == null)
            {
                simulationProvider = FindObjectOfType<SimulationProvider>();
            }

            if (sourceMode == SensorSourceMode.Simulation)
            {
                return true;
            }

            if (sourceMode == SensorSourceMode.DeviceSensors)
            {
                return false;
            }

            if (simulationProvider != null && simulationProvider.ForceSimulationMode)
            {
                return true;
            }

            if (!Application.isMobilePlatform || Application.isEditor)
            {
                return true;
            }

            return false;
        }

        private void ReadSimulationLocation()
        {
            RawPoint = simulationProvider != null
                ? simulationProvider.CurrentPoint
                : new GeoPoint(fallbackSimLatitude, fallbackSimLongitude);

            IsReady = true;
            PublishSmoothed();
        }

        private bool TryReadDeviceLocation()
        {
            if (!Input.location.isEnabledByUser)
            {
                if (fallbackToSimulationIfUnavailable)
                {
                    ReadSimulationLocation();
                    IsUsingSimulation = true;
                    return true;
                }

                IsReady = false;
                return false;
            }

            if (!_locationServiceStarted)
            {
                Input.location.Start(desiredAccuracyMeters, updateDistanceMeters);
                _locationServiceStarted = true;
                _locationInitStartedTime = Time.time;
            }

            if (Input.location.status == LocationServiceStatus.Initializing)
            {
                bool timedOut = (Time.time - _locationInitStartedTime) > InitTimeoutSeconds;
                if (timedOut && fallbackToSimulationIfUnavailable)
                {
                    ReadSimulationLocation();
                    IsUsingSimulation = true;
                    return true;
                }

                IsReady = false;
                return false;
            }

            if (Input.location.status != LocationServiceStatus.Running)
            {
                if (fallbackToSimulationIfUnavailable)
                {
                    ReadSimulationLocation();
                    IsUsingSimulation = true;
                    return true;
                }

                IsReady = false;
                return false;
            }

            LocationInfo info = Input.location.lastData;
            RawPoint = new GeoPoint(info.latitude, info.longitude);
            IsReady = true;
            PublishSmoothed();
            return true;
        }

        private void PublishSmoothed()
        {
            if (!_hasInitial)
            {
                SmoothedPoint = RawPoint;
                _hasInitial = true;
            }
            else
            {
                float moved = DistanceMeters(SmoothedPoint, RawPoint);
                if (moved >= minUpdateDistanceMeters)
                {
                    SmoothedPoint = LocationSmoother.ExponentialSmooth(RawPoint, SmoothedPoint, smoothingAlpha);
                }
            }

            OnLocationUpdated?.Invoke(RawPoint, SmoothedPoint);
        }

        private void StopLocationServiceIfNeeded()
        {
            if (_locationServiceStarted)
            {
                Input.location.Stop();
                _locationServiceStarted = false;
            }
        }

        private static float DistanceMeters(GeoPoint a, GeoPoint b)
        {
            const double earthRadius = 6371000.0;
            double dLat = (b.latitude - a.latitude) * Mathf.Deg2Rad;
            double dLon = (b.longitude - a.longitude) * Mathf.Deg2Rad;

            double lat1 = a.latitude * Mathf.Deg2Rad;
            double lat2 = b.latitude * Mathf.Deg2Rad;

            double h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                       Math.Cos(lat1) * Math.Cos(lat2) *
                       Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

            double c = 2 * Math.Atan2(Math.Sqrt(h), Math.Sqrt(1 - h));
            return (float)(earthRadius * c);
        }
    }
}
