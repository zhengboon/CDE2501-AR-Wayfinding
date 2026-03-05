using System;
using UnityEngine;

namespace CDE2501.Wayfinding.Location
{
    public class CompassManager : MonoBehaviour
    {
        [Header("Source")]
        [SerializeField] private SensorSourceMode sourceMode = SensorSourceMode.Auto;
        [SerializeField] private SimulationProvider simulationProvider;
        [SerializeField] private bool fallbackToSimulationIfUnavailable = true;

        [Header("Smoothing")]
        [SerializeField] private float headingSmoothingAlpha = 0.2f;

        public bool IsReady { get; private set; }
        public bool IsUsingSimulation { get; private set; }
        public float RawHeading { get; private set; }
        public float SmoothedHeading { get; private set; }

        public event Action<float, float> OnHeadingUpdated;

        private bool _hasInitial;

        private void Awake()
        {
            if (simulationProvider == null)
            {
                simulationProvider = FindObjectOfType<SimulationProvider>();
            }
        }

        private void OnEnable()
        {
            if (sourceMode != SensorSourceMode.Simulation)
            {
                Input.compass.enabled = true;
            }

            IsReady = false;
            _hasInitial = false;
        }

        private void OnDisable()
        {
            Input.compass.enabled = false;
            IsReady = false;
            _hasInitial = false;
        }

        private void Update()
        {
            bool useSimulation = ResolveUseSimulationMode();
            IsUsingSimulation = useSimulation;

            if (useSimulation)
            {
                if (simulationProvider == null && !fallbackToSimulationIfUnavailable)
                {
                    IsReady = false;
                    return;
                }

                RawHeading = simulationProvider != null ? simulationProvider.CurrentHeading : RawHeading;
                IsReady = true;
            }
            else
            {
                if (!Input.compass.enabled)
                {
                    Input.compass.enabled = true;
                }

                if (!Input.compass.enabled)
                {
                    if (fallbackToSimulationIfUnavailable && simulationProvider != null)
                    {
                        RawHeading = simulationProvider.CurrentHeading;
                        IsUsingSimulation = true;
                        IsReady = true;
                    }
                    else
                    {
                        IsReady = false;
                    }

                    return;
                }

                RawHeading = Input.compass.trueHeading;
                IsReady = true;
            }

            if (!IsReady)
            {
                return;
            }

            if (!_hasInitial)
            {
                SmoothedHeading = RawHeading;
                _hasInitial = true;
            }
            else
            {
                SmoothedHeading = LocationSmoother.SmoothAngle(RawHeading, SmoothedHeading, headingSmoothingAlpha);
            }

            OnHeadingUpdated?.Invoke(RawHeading, SmoothedHeading);
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
    }
}
