using System.Collections.Generic;
using CDE2501.Wayfinding.Data;
using CDE2501.Wayfinding.Elevation;
using CDE2501.Wayfinding.IndoorGraph;
using CDE2501.Wayfinding.Routing;
using TMPro;
using UnityEngine;

namespace CDE2501.Wayfinding.UI
{
    public class DestinationSelectorUI : MonoBehaviour
    {
        [SerializeField] private TMP_Dropdown destinationDropdown;
        [SerializeField] private TMP_Text distanceText;
        [SerializeField] private bool wheelchairModeEnabled;
        [SerializeField] private bool rainModeEnabled;
        [SerializeField] private string startNodeId = "QTMRT";
        [SerializeField] private bool useNearestNodeAsStart = true;
        [SerializeField] private Transform startReference;

        [SerializeField] private LocationManager locationManager;
        [SerializeField] private LevelManager levelManager;
        [SerializeField] private RouteCalculator routeCalculator;
        [SerializeField] private GraphLoader graphLoader;

        private readonly List<LocationPoint> _cachedLocations = new List<LocationPoint>();

        private void Start()
        {
            if (locationManager != null)
            {
                locationManager.OnLocationsChanged += RefreshDestinationList;
                locationManager.LoadLocations();
            }

            if (routeCalculator != null)
            {
                routeCalculator.OnRouteUpdated += OnRouteUpdated;
            }
        }

        private void OnDestroy()
        {
            if (locationManager != null)
            {
                locationManager.OnLocationsChanged -= RefreshDestinationList;
            }

            if (routeCalculator != null)
            {
                routeCalculator.OnRouteUpdated -= OnRouteUpdated;
            }
        }

        public void OnDestinationSelected(int index)
        {
            if (index < 0 || index >= _cachedLocations.Count)
            {
                return;
            }

            if (routeCalculator == null || levelManager == null)
            {
                return;
            }

            var destination = _cachedLocations[index];
            string resolvedStart = ResolveStartNodeId();
            if (string.IsNullOrWhiteSpace(resolvedStart))
            {
                resolvedStart = startNodeId;
            }

            routeCalculator.CurrentMode = wheelchairModeEnabled
                ? Profiles.RoutingMode.Wheelchair
                : Profiles.RoutingMode.NormalElderly;
            routeCalculator.RainMode = rainModeEnabled;

            routeCalculator.CalculateIndoorRoute(resolvedStart, destination.indoor_node_id, levelManager.CurrentLevel, forceRefresh: true);
        }

        private void RefreshDestinationList()
        {
            _cachedLocations.Clear();
            if (locationManager == null)
            {
                return;
            }
            _cachedLocations.AddRange(locationManager.Locations);

            if (destinationDropdown == null)
            {
                return;
            }

            destinationDropdown.ClearOptions();
            var options = new List<TMP_Dropdown.OptionData>();
            foreach (var location in _cachedLocations)
            {
                options.Add(new TMP_Dropdown.OptionData(location.name));
            }

            destinationDropdown.AddOptions(options);
            destinationDropdown.onValueChanged.RemoveListener(OnDestinationSelected);
            destinationDropdown.onValueChanged.AddListener(OnDestinationSelected);
        }

        public void SetWheelchairMode(bool enabled)
        {
            wheelchairModeEnabled = enabled;
        }

        public void SetRainMode(bool enabled)
        {
            rainModeEnabled = enabled;
        }

        private void OnRouteUpdated(RouteResult result)
        {
            if (distanceText == null)
            {
                return;
            }

            if (!result.success)
            {
                distanceText.text = result.message;
                return;
            }

            distanceText.text = $"{result.totalDistance:0} m";
        }

        private string ResolveStartNodeId()
        {
            if (!useNearestNodeAsStart || graphLoader == null || graphLoader.NodesById.Count == 0)
            {
                return startNodeId;
            }

            Transform reference = startReference != null ? startReference : Camera.main != null ? Camera.main.transform : null;
            if (reference == null)
            {
                return startNodeId;
            }

            string bestId = null;
            float bestDist = float.PositiveInfinity;
            foreach (var kvp in graphLoader.NodesById)
            {
                float d = Vector3.Distance(reference.position, kvp.Value.position);
                if (d < bestDist)
                {
                    bestDist = d;
                    bestId = kvp.Key;
                }
            }

            return string.IsNullOrWhiteSpace(bestId) ? startNodeId : bestId;
        }
    }
}
