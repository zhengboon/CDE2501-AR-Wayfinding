using System.Collections.Generic;
using CDE2501.Wayfinding.Data;
using CDE2501.Wayfinding.IndoorGraph;
using UnityEngine;

namespace CDE2501.Wayfinding.UI
{
    public class DestinationMarkerVisualizer : MonoBehaviour
    {
        [SerializeField] private GraphLoader graphLoader;
        [SerializeField] private LocationManager locationManager;
        [SerializeField] private PrimitiveType markerPrimitive = PrimitiveType.Cube;
        [SerializeField] private float markerScale = 0.6f;
        [SerializeField] private float yOffset = 0.2f;
        [SerializeField] private Color selectedColor = new Color(1f, 0.5f, 0.1f, 1f);
        [SerializeField] private Color unselectedColor = Color.white;
        [SerializeField] private bool clearExistingBeforeBuild = true;

        private readonly List<GameObject> _markers = new List<GameObject>();
        private readonly Dictionary<string, Renderer> _markerByLocationName = new Dictionary<string, Renderer>();
        private bool _graphReady;
        private bool _locationsReady;
        private string _selectedLocationName;

        private void Awake()
        {
            if (graphLoader == null)
            {
                graphLoader = FindObjectOfType<GraphLoader>();
            }

            if (locationManager == null)
            {
                locationManager = FindObjectOfType<LocationManager>();
            }
        }

        private void OnEnable()
        {
            if (graphLoader != null)
            {
                graphLoader.OnGraphLoaded += HandleGraphLoaded;
            }

            if (locationManager != null)
            {
                locationManager.OnLocationsChanged += HandleLocationsChanged;
            }
        }

        private void Start()
        {
            _graphReady = graphLoader != null && graphLoader.NodesById.Count > 0;
            _locationsReady = locationManager != null && locationManager.Locations.Count > 0;
            TryBuildMarkers();
        }

        private void OnDisable()
        {
            if (graphLoader != null)
            {
                graphLoader.OnGraphLoaded -= HandleGraphLoaded;
            }

            if (locationManager != null)
            {
                locationManager.OnLocationsChanged -= HandleLocationsChanged;
            }
        }

        public void SetGraphLoader(GraphLoader loader)
        {
            if (graphLoader != null)
            {
                graphLoader.OnGraphLoaded -= HandleGraphLoaded;
            }

            graphLoader = loader;

            if (graphLoader != null && isActiveAndEnabled)
            {
                graphLoader.OnGraphLoaded += HandleGraphLoaded;
            }

            _graphReady = graphLoader != null && graphLoader.NodesById.Count > 0;
        }

        public void SetLocationManager(LocationManager manager)
        {
            if (locationManager != null)
            {
                locationManager.OnLocationsChanged -= HandleLocationsChanged;
            }

            locationManager = manager;

            if (locationManager != null && isActiveAndEnabled)
            {
                locationManager.OnLocationsChanged += HandleLocationsChanged;
            }

            _locationsReady = locationManager != null && locationManager.Locations.Count > 0;
        }

        public void Rebuild()
        {
            TryBuildMarkers(force: true);
        }

        public void SetSelectedDestination(string locationName)
        {
            _selectedLocationName = locationName;
            UpdateMarkerColors();
        }

        private void HandleGraphLoaded(bool success, string message)
        {
            _graphReady = success && graphLoader != null && graphLoader.NodesById.Count > 0;
            TryBuildMarkers();
        }

        private void HandleLocationsChanged()
        {
            _locationsReady = locationManager != null && locationManager.Locations.Count > 0;
            TryBuildMarkers();
        }

        private void TryBuildMarkers(bool force = false)
        {
            if (!force && (!_graphReady || !_locationsReady))
            {
                return;
            }

            if (graphLoader == null || locationManager == null)
            {
                return;
            }

            if (clearExistingBeforeBuild)
            {
                ClearMarkers();
            }

            foreach (LocationPoint location in locationManager.Locations)
            {
                if (string.IsNullOrWhiteSpace(location.indoor_node_id))
                {
                    continue;
                }

                Node node = graphLoader.GetNode(location.indoor_node_id);
                if (node == null)
                {
                    continue;
                }

                GameObject marker = GameObject.CreatePrimitive(markerPrimitive);
                marker.name = $"Destination_{location.name}";
                marker.transform.SetParent(transform, worldPositionStays: true);
                marker.transform.position = node.position + new Vector3(0f, yOffset, 0f);
                marker.transform.localScale = Vector3.one * markerScale;

                Renderer renderer = marker.GetComponent<Renderer>();
                if (renderer != null)
                {
                    Material markerMaterial = CreateMaterial(unselectedColor);
                    if (markerMaterial != null)
                    {
                        renderer.material = markerMaterial;
                    }
                    renderer.material.color = unselectedColor;
                    _markerByLocationName[location.name] = renderer;
                }

                Collider col = marker.GetComponent<Collider>();
                if (col != null)
                {
                    col.enabled = false;
                }

                _markers.Add(marker);
            }

            UpdateMarkerColors();
        }

        private void ClearMarkers()
        {
            for (int i = _markers.Count - 1; i >= 0; i--)
            {
                if (_markers[i] != null)
                {
                    Destroy(_markers[i]);
                }
            }

            _markers.Clear();
            _markerByLocationName.Clear();
        }

        private void UpdateMarkerColors()
        {
            foreach (var kv in _markerByLocationName)
            {
                Renderer renderer = kv.Value;
                if (renderer == null || renderer.material == null)
                {
                    continue;
                }

                bool isSelected = !string.IsNullOrWhiteSpace(_selectedLocationName) &&
                                  string.Equals(kv.Key, _selectedLocationName, System.StringComparison.Ordinal);
                renderer.material.color = isSelected ? selectedColor : unselectedColor;
            }
        }

        private static Material CreateMaterial(Color color)
        {
            Shader shader =
                Shader.Find("Standard") ??
                Shader.Find("Universal Render Pipeline/Lit") ??
                Shader.Find("Sprites/Default") ??
                Shader.Find("Unlit/Color");

            if (shader == null)
            {
                return null;
            }

            Material material = new Material(shader);
            material.color = color;
            return material;
        }
    }
}
