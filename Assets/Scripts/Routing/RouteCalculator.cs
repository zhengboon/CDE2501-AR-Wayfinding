using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using CDE2501.Wayfinding.Data;
using CDE2501.Wayfinding.IndoorGraph;
using CDE2501.Wayfinding.Profiles;
using CDE2501.Wayfinding.Utility;
using UnityEngine;
using UnityEngine.Networking;

namespace CDE2501.Wayfinding.Routing
{
    public enum ElevationLevel
    {
        Road = 0,
        Deck = 1,
        LiftLobby = 2,
        Corridor = 3
    }

    [Serializable]
    public class RouteRequest
    {
        public string startNodeId;
        public string endNodeId;
        public string recalcReason;
        public ElevationLevel currentLevel;
        public RoutingMode mode;
        public bool rainMode;
        public int boundaryRevision;
    }

    [Serializable]
    public class RouteResult
    {
        public List<string> nodePath = new List<string>();
        public float totalCost;
        public float totalDistance;
        public float estimatedWalkTimeSeconds;
        public bool success;
        public string message;

        public string recalcReason;

        public static RouteResult Failed(string msg, string reason = "Unknown")
        {
            return new RouteResult { success = false, message = msg, recalcReason = reason };
        }

        public static RouteResult Success(List<string> path, float cost)
        {
            return new RouteResult
            {
                success = true,
                nodePath = path,
                totalCost = cost,
                message = "Route calculated",
                recalcReason = "Unknown"
            };
        }
    }

    [Serializable]
    public class RouteHintEdge
    {
        public string fromNodeId;
        public string toNodeId;
        public float multiplier = 0.85f;
    }

    [Serializable]
    public class RouteHintManifest
    {
        public string version;
        public string generatedAtUtc;
        public string source;
        public string graphHint;
        public float defaultMultiplier = 0.85f;
        public List<RouteHintEdge> edges = new List<RouteHintEdge>();
    }

    public class RouteCalculator : MonoBehaviour
    {
        [SerializeField] private GraphLoader graphLoader;
        [SerializeField] private BoundaryConstraintManager boundaryConstraintManager;
        [SerializeField] private string routingProfilesFileName = "routing_profiles.json";
        [SerializeField] private bool enableVideoRouteBias = true;
        [SerializeField] private string routeHintsFileName = "video_route_hints.json";
        [SerializeField, Range(0.5f, 2f)] private float fallbackRouteHintMultiplier = 0.85f;
        [SerializeField] private bool routeHintLogs;
        [SerializeField] private bool rainMode;
        [SerializeField] private RoutingMode routingMode = RoutingMode.NormalElderly;
        [SerializeField] private bool autoInitializeOnStart = true;
        [Header("Performance")]
        [SerializeField] private bool enableRouteCache = true;
        [SerializeField, Min(1)] private int maxCacheEntries = 64;
        [SerializeField, Min(0f)] private float recalcCooldownSeconds = 0f;
        [SerializeField] private bool queueRecalculationDuringCooldown = true;
        [SerializeField, Min(0f)] private float switchRouteCostAdvantage = 2.0f;
        [SerializeField, Min(0.1f)] private float elderlyWalkSpeedMetersPerSecond = 1.0f;
        [SerializeField, Min(0.1f)] private float wheelchairSpeedMetersPerSecond = 0.8f;

        private RoutingProfilesConfig _profilesConfig;
        private AStarPathfinder _pathfinder;
        private bool _isInitializing;
        private float _lastRecalcTimestamp = float.NegativeInfinity;
        private RouteResult _lastComputedResult;
        private RouteRequest _queuedRequest;
        private RoutingProfile _queuedProfile;
        private string _queuedCacheKey;
        private Coroutine _queuedRecalcCoroutine;
        private string _lastRequestCacheKey;
        private readonly Dictionary<string, RouteResult> _routeCache = new Dictionary<string, RouteResult>();
        private readonly LinkedList<string> _routeCacheOrder = new LinkedList<string>();
        private readonly Dictionary<string, LinkedListNode<string>> _cacheOrderIndex = new Dictionary<string, LinkedListNode<string>>();
        private readonly Dictionary<string, float> _edgeDistanceMetersByKey = new Dictionary<string, float>(StringComparer.Ordinal);
        private readonly Dictionary<string, float> _routeHintEdgeMultipliersByKey = new Dictionary<string, float>(StringComparer.Ordinal);
        private string _routeHintVersion = "none";
        private int _routeHintEdgeCount;

        public event Action<RouteResult> OnRouteUpdated;

        public bool RainMode
        {
            get => rainMode;
            set => rainMode = value;
        }

        public RoutingMode CurrentMode
        {
            get => routingMode;
            set => routingMode = value;
        }

        public bool IsInitialized => !_isInitializing && _profilesConfig != null && _pathfinder != null;

        private void Awake()
        {
            if (graphLoader == null)
            {
                graphLoader = GetComponent<GraphLoader>();
            }

            if (boundaryConstraintManager == null)
            {
                boundaryConstraintManager = FindObjectOfType<BoundaryConstraintManager>();
            }

            if (boundaryConstraintManager != null)
            {
                boundaryConstraintManager.OnBoundaryUpdated += HandleBoundaryUpdated;
            }
        }

        private void Start()
        {
            if (!autoInitializeOnStart || graphLoader == null)
            {
                return;
            }

            graphLoader.OnGraphLoaded += HandleGraphLoaded;
            _isInitializing = true;
            graphLoader.LoadGraph();
        }

        private void OnDestroy()
        {
            if (graphLoader != null)
            {
                graphLoader.OnGraphLoaded -= HandleGraphLoaded;
            }

            if (boundaryConstraintManager != null)
            {
                boundaryConstraintManager.OnBoundaryUpdated -= HandleBoundaryUpdated;
            }

            StopQueuedRecalculation();
        }

        private void OnDisable()
        {
            StopQueuedRecalculation();
        }

        private void StopQueuedRecalculation()
        {
            if (_queuedRecalcCoroutine != null)
            {
                StopCoroutine(_queuedRecalcCoroutine);
                _queuedRecalcCoroutine = null;
            }
        }

        public void Initialize(RoutingProfilesConfig profilesConfig)
        {
            _profilesConfig = profilesConfig;
            _pathfinder = new AStarPathfinder(graphLoader.NodesById, graphLoader.Edges);
            RebuildEdgeDistanceLookup();
            LoadRouteHintsFromDisk();
            ClearRouteCache();
        }

        public void SetBoundaryConstraintManager(BoundaryConstraintManager manager)
        {
            if (ReferenceEquals(boundaryConstraintManager, manager))
            {
                return;
            }

            if (boundaryConstraintManager != null)
            {
                boundaryConstraintManager.OnBoundaryUpdated -= HandleBoundaryUpdated;
            }

            boundaryConstraintManager = manager;

            if (boundaryConstraintManager != null)
            {
                boundaryConstraintManager.OnBoundaryUpdated += HandleBoundaryUpdated;
            }

            ClearRouteCache();
        }

        public void InitializeFromJson()
        {
            StartCoroutine(LoadProfilesAndInitializeRoutine());
        }

        public RouteResult CalculateIndoorRoute(string startNodeId, string endNodeId, ElevationLevel currentLevel, string reason = "Manual")
        {
            return CalculateIndoorRoute(startNodeId, endNodeId, currentLevel, forceRefresh: false, reason);
        }

        public RouteResult CalculateIndoorRoute(string startNodeId, string endNodeId, ElevationLevel currentLevel, bool forceRefresh, string reason = "Manual")
        {
            if (_pathfinder == null)
            {
                RouteResult failed = RouteResult.Failed("Pathfinder not initialized.", reason);
                OnRouteUpdated?.Invoke(failed);
                return failed;
            }

            RoutingProfile profile = _profilesConfig?.GetByMode(routingMode);
            if (profile == null)
            {
                RouteResult failed = RouteResult.Failed("Routing profile unavailable.", reason);
                OnRouteUpdated?.Invoke(failed);
                return failed;
            }

            var request = new RouteRequest
            {
                startNodeId = startNodeId,
                endNodeId = endNodeId,
                currentLevel = currentLevel,
                mode = routingMode,
                rainMode = rainMode,
                boundaryRevision = boundaryConstraintManager != null ? boundaryConstraintManager.BoundaryRevision : 0,
                recalcReason = reason
            };

            if (boundaryConstraintManager != null && boundaryConstraintManager.HasBoundary)
            {
                if (!boundaryConstraintManager.IsNodeAllowed(startNodeId))
                {
                    RouteResult failed = RouteResult.Failed("Start node is outside boundary.", reason);
                    OnRouteUpdated?.Invoke(failed);
                    return failed;
                }

                if (!boundaryConstraintManager.IsNodeAllowed(endNodeId))
                {
                    RouteResult failed = RouteResult.Failed("Destination is outside boundary.", reason);
                    OnRouteUpdated?.Invoke(failed);
                    return failed;
                }
            }

            string cacheKey = BuildCacheKey(request, profile.profileName);
            if (!forceRefresh && enableRouteCache && TryGetRouteFromCache(cacheKey, out RouteResult cached))
            {
                cached.message = "Route loaded from cache.";
                cached.recalcReason = reason;
                OnRouteUpdated?.Invoke(cached);
                return cached;
            }

            float cooldownRemaining = GetCooldownRemainingSeconds();
            bool sameAsLastRequest = string.Equals(cacheKey, _lastRequestCacheKey, StringComparison.Ordinal);
            bool cooldownActive = !forceRefresh && cooldownRemaining > 0f && sameAsLastRequest;
            if (cooldownActive)
            {
                if (queueRecalculationDuringCooldown)
                {
                    QueueRecalculation(request, profile, cacheKey);

                    if (_lastComputedResult != null)
                    {
                        RouteResult stale = CloneRouteResult(_lastComputedResult);
                        stale.message = $"Reusing last route while recalculation is queued ({cooldownRemaining:0.0}s).";
                        stale.recalcReason = reason;
                        OnRouteUpdated?.Invoke(stale);
                        return stale;
                    }

                    RouteResult queued = RouteResult.Failed($"Recalculation queued. Try again in {cooldownRemaining:0.0}s.", reason);
                    OnRouteUpdated?.Invoke(queued);
                    return queued;
                }

                if (_lastComputedResult != null)
                {
                    RouteResult throttled = CloneRouteResult(_lastComputedResult);
                    throttled.message = $"Recalculation throttled for {cooldownRemaining:0.0}s.";
                    throttled.recalcReason = reason;
                    OnRouteUpdated?.Invoke(throttled);
                    return throttled;
                }
            }

            _lastRequestCacheKey = cacheKey;
            return ComputeRouteNow(request, profile, cacheKey, raiseEvent: true);
        }

        public static float ComputeBearingDegrees(double fromLat, double fromLon, double toLat, double toLon)
        {
            double phi1 = fromLat * Mathf.Deg2Rad;
            double phi2 = toLat * Mathf.Deg2Rad;
            double dLon = (toLon - fromLon) * Mathf.Deg2Rad;

            double y = Math.Sin(dLon) * Math.Cos(phi2);
            double x = Math.Cos(phi1) * Math.Sin(phi2) - Math.Sin(phi1) * Math.Cos(phi2) * Math.Cos(dLon);
            double bearing = Math.Atan2(y, x) * Mathf.Rad2Deg;

            return (float)((bearing + 360.0) % 360.0);
        }

        public static float HaversineDistanceMeters(double fromLat, double fromLon, double toLat, double toLon)
        {
            const double earthRadius = 6371000.0;
            double dLat = (toLat - fromLat) * Mathf.Deg2Rad;
            double dLon = (toLon - fromLon) * Mathf.Deg2Rad;

            double a =
                Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(fromLat * Mathf.Deg2Rad) * Math.Cos(toLat * Mathf.Deg2Rad) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

            double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return (float)(earthRadius * c);
        }

        private float ComputePathDistance(List<string> nodePath)
        {
            if (nodePath == null || nodePath.Count < 2)
            {
                return 0f;
            }

            if (_edgeDistanceMetersByKey.Count == 0)
            {
                RebuildEdgeDistanceLookup();
            }

            float distance = 0f;
            for (int i = 0; i < nodePath.Count - 1; i++)
            {
                string from = nodePath[i];
                string to = nodePath[i + 1];
                string key = BuildDirectedEdgeKey(from, to);
                if (_edgeDistanceMetersByKey.TryGetValue(key, out float edgeDistance))
                {
                    distance += edgeDistance;
                    continue;
                }

                Node fromNode = graphLoader != null ? graphLoader.GetNode(from) : null;
                Node toNode = graphLoader != null ? graphLoader.GetNode(to) : null;
                if (fromNode != null && toNode != null)
                {
                    distance += Vector3.Distance(fromNode.position, toNode.position);
                }
            }

            return distance;
        }

        private IEnumerator LoadProfilesAndInitializeRoutine()
        {
            string persistentPath = Path.Combine(Application.persistentDataPath, "Data", routingProfilesFileName);
            string streamingPath = Path.Combine(Application.streamingAssetsPath, "Data", routingProfilesFileName);

            if (!File.Exists(persistentPath))
            {
                yield return CopyFromStreamingAssets(streamingPath, persistentPath);
            }

            if (!File.Exists(persistentPath))
            {
                Debug.LogError($"Missing routing profiles file at {persistentPath}");
                _isInitializing = false;
                yield break;
            }

            if (enableVideoRouteBias && !string.IsNullOrWhiteSpace(routeHintsFileName))
            {
                string routeHintsPersistentPath = Path.Combine(Application.persistentDataPath, "Data", routeHintsFileName);
                string routeHintsStreamingPath = Path.Combine(Application.streamingAssetsPath, "Data", routeHintsFileName);
                if (!File.Exists(routeHintsPersistentPath))
                {
                    yield return CopyFromStreamingAssets(routeHintsStreamingPath, routeHintsPersistentPath);
                }
            }

            string raw = File.ReadAllText(persistentPath);
            string wrapped = WrapTopLevelArrayIfNeeded(raw, "profiles");
            _profilesConfig = JsonUtility.FromJson<RoutingProfilesConfig>(wrapped);

            if (_profilesConfig == null)
            {
                Debug.LogError("Unable to parse routing profiles JSON.");
                _isInitializing = false;
                yield break;
            }

            _pathfinder = new AStarPathfinder(graphLoader.NodesById, graphLoader.Edges);
            LoadRouteHintsFromDisk();
            _isInitializing = false;
            ClearRouteCache();
        }

        private void HandleGraphLoaded(bool success, string message)
        {
            if (!success)
            {
                Debug.LogError($"Graph load failed: {message}");
                return;
            }

            _pathfinder = new AStarPathfinder(graphLoader.NodesById, graphLoader.Edges);
            RebuildEdgeDistanceLookup();
            LoadRouteHintsFromDisk();
            ClearRouteCache();

            // Always reload profiles when graph loads successfully,
            // even if a previous attempt failed (e.g. files weren't downloaded yet).
            if (_profilesConfig == null)
            {
                _isInitializing = true;
                InitializeFromJson();
            }
            else
            {
                _isInitializing = false;
            }
        }

        public void ClearRouteCache()
        {
            _routeCache.Clear();
            _routeCacheOrder.Clear();
            _cacheOrderIndex.Clear();
            _lastComputedResult = null;
            _lastRecalcTimestamp = float.NegativeInfinity;
            _queuedRequest = null;
            _queuedProfile = null;
            _queuedCacheKey = null;
            _lastRequestCacheKey = null;
        }

        private void RebuildEdgeDistanceLookup()
        {
            _edgeDistanceMetersByKey.Clear();

            if (graphLoader == null || graphLoader.Edges == null)
            {
                return;
            }

            for (int i = 0; i < graphLoader.Edges.Count; i++)
            {
                Edge edge = graphLoader.Edges[i];
                if (edge == null || string.IsNullOrWhiteSpace(edge.fromNode) || string.IsNullOrWhiteSpace(edge.toNode))
                {
                    continue;
                }

                string key = BuildDirectedEdgeKey(edge.fromNode, edge.toNode);
                float sanitizedDistance = Mathf.Max(0f, edge.distance);
                if (_edgeDistanceMetersByKey.TryGetValue(key, out float existing))
                {
                    _edgeDistanceMetersByKey[key] = Mathf.Min(existing, sanitizedDistance);
                }
                else
                {
                    _edgeDistanceMetersByKey[key] = sanitizedDistance;
                }
            }
        }

        private RouteResult ComputeRouteNow(RouteRequest request, RoutingProfile profile, string cacheKey, bool raiseEvent)
        {
            Func<string, bool> nodeEligibility = boundaryConstraintManager != null && boundaryConstraintManager.HasBoundary
                ? new Func<string, bool>(boundaryConstraintManager.IsNodeAllowed)
                : null;
            Func<string, string, float> edgeCostMultiplierProvider = _routeHintEdgeMultipliersByKey.Count > 0
                ? new Func<string, string, float>(GetRouteHintEdgeMultiplier)
                : null;

            RouteResult result = _pathfinder.FindPath(
                request,
                profile,
                _profilesConfig.rainSlopeMultiplier,
                nodeEligibility,
                edgeCostMultiplierProvider);
            result.recalcReason = request.recalcReason;
            if (result.success)
            {
                result.totalDistance = ComputePathDistance(result.nodePath);
                float speed = request.mode == RoutingMode.Wheelchair
                    ? Mathf.Max(0.1f, wheelchairSpeedMetersPerSecond)
                    : Mathf.Max(0.1f, elderlyWalkSpeedMetersPerSecond);
                result.estimatedWalkTimeSeconds = result.totalDistance / speed;

                // Apply Hysteresis: Only switch if the new route is significantly better, and destination matches.
                if (_lastComputedResult != null && _lastComputedResult.success && _lastComputedResult.nodePath != null && _lastComputedResult.nodePath.Count > 0)
                {
                    string lastDest = _lastComputedResult.nodePath[_lastComputedResult.nodePath.Count - 1];
                    string newDest = result.nodePath.Count > 0 ? result.nodePath[result.nodePath.Count - 1] : null;

                    if (string.Equals(lastDest, newDest, StringComparison.Ordinal))
                    {
                        var oldPathSet = new HashSet<string>(_lastComputedResult.nodePath, StringComparer.Ordinal);
                        bool isOnOldPath = oldPathSet.Contains(request.startNodeId);
                        if (isOnOldPath && result.totalCost >= _lastComputedResult.totalCost - switchRouteCostAdvantage)
                        {
                            result = CloneRouteResult(_lastComputedResult);
                            result.message = "Maintained previous route (hysteresis).";
                            result.recalcReason = request.recalcReason;
                        }
                    }
                }

                if (enableRouteCache)
                {
                    SaveRouteToCache(cacheKey, result);
                }
            }

            _lastRecalcTimestamp = Time.unscaledTime;
            _lastComputedResult = CloneRouteResult(result);

            if (raiseEvent)
            {
                OnRouteUpdated?.Invoke(result);
            }

            return result;
        }

        private void QueueRecalculation(RouteRequest request, RoutingProfile profile, string cacheKey)
        {
            _queuedRequest = CloneRouteRequest(request);
            _queuedProfile = profile;
            _queuedCacheKey = cacheKey;

            if (_queuedRecalcCoroutine == null)
            {
                _queuedRecalcCoroutine = StartCoroutine(ProcessQueuedRecalculation());
            }
        }

        private IEnumerator ProcessQueuedRecalculation()
        {
            while (enabled)
            {
                float remaining = GetCooldownRemainingSeconds();
                if (remaining > 0f)
                {
                    yield return new WaitForSeconds(remaining);
                }

                if (_queuedRequest == null || _queuedProfile == null || string.IsNullOrWhiteSpace(_queuedCacheKey))
                {
                    _queuedRecalcCoroutine = null;
                    yield break;
                }

                RouteRequest request = _queuedRequest;
                RoutingProfile profile = _queuedProfile;
                string cacheKey = _queuedCacheKey;
                _queuedRequest = null;
                _queuedProfile = null;
                _queuedCacheKey = null;

                ComputeRouteNow(request, profile, cacheKey, raiseEvent: true);

                if (_queuedRequest == null)
                {
                    _queuedRecalcCoroutine = null;
                    yield break;
                }
            }

            _queuedRecalcCoroutine = null;
        }

        private float GetCooldownRemainingSeconds()
        {
            if (recalcCooldownSeconds <= 0f)
            {
                return 0f;
            }

            float elapsed = Time.unscaledTime - _lastRecalcTimestamp;
            return Mathf.Max(0f, recalcCooldownSeconds - elapsed);
        }

        private bool TryGetRouteFromCache(string cacheKey, out RouteResult routeResult)
        {
            routeResult = null;
            if (!_routeCache.TryGetValue(cacheKey, out RouteResult cached))
            {
                return false;
            }

            if (_cacheOrderIndex.TryGetValue(cacheKey, out LinkedListNode<string> node))
            {
                _routeCacheOrder.Remove(node);
                _routeCacheOrder.AddLast(node);
            }

            routeResult = CloneRouteResult(cached);
            return true;
        }

        private void SaveRouteToCache(string cacheKey, RouteResult result)
        {
            if (maxCacheEntries < 1)
            {
                return;
            }

            RouteResult cachedCopy = CloneRouteResult(result);
            if (_routeCache.ContainsKey(cacheKey))
            {
                _routeCache[cacheKey] = cachedCopy;

                if (_cacheOrderIndex.TryGetValue(cacheKey, out LinkedListNode<string> existingNode))
                {
                    _routeCacheOrder.Remove(existingNode);
                    _routeCacheOrder.AddLast(existingNode);
                }

                return;
            }

            _routeCache[cacheKey] = cachedCopy;
            LinkedListNode<string> orderNode = _routeCacheOrder.AddLast(cacheKey);
            _cacheOrderIndex[cacheKey] = orderNode;

            while (_routeCacheOrder.Count > maxCacheEntries)
            {
                LinkedListNode<string> first = _routeCacheOrder.First;
                if (first == null)
                {
                    break;
                }

                string removeKey = first.Value;
                _routeCacheOrder.RemoveFirst();
                _cacheOrderIndex.Remove(removeKey);
                _routeCache.Remove(removeKey);
            }
        }

        private string BuildCacheKey(RouteRequest request, string profileName)
        {
            string graphVersion = graphLoader?.GraphData?.metadata?.version ?? "unknown";
            string mode = ((int)request.mode).ToString();
            string level = ((int)request.currentLevel).ToString();
            string rain = request.rainMode ? "1" : "0";
            string boundaryRev = request.boundaryRevision.ToString();
            string routeHintRevision = $"{_routeHintVersion}:{_routeHintEdgeCount}";
            return $"{graphVersion}|{request.startNodeId}|{request.endNodeId}|{level}|{mode}|{rain}|{profileName}|b{boundaryRev}|rh{routeHintRevision}";
        }

        private static string BuildDirectedEdgeKey(string fromNodeId, string toNodeId)
        {
            return $"{fromNodeId}->{toNodeId}";
        }

        private float GetRouteHintEdgeMultiplier(string fromNodeId, string toNodeId)
        {
            if (string.IsNullOrWhiteSpace(fromNodeId) || string.IsNullOrWhiteSpace(toNodeId))
            {
                return 1f;
            }

            string key = BuildDirectedEdgeKey(fromNodeId.Trim(), toNodeId.Trim());
            return _routeHintEdgeMultipliersByKey.TryGetValue(key, out float multiplier) ? multiplier : 1f;
        }

        private void LoadRouteHintsFromDisk()
        {
            _routeHintEdgeMultipliersByKey.Clear();
            _routeHintVersion = "none";
            _routeHintEdgeCount = 0;

            if (!enableVideoRouteBias || string.IsNullOrWhiteSpace(routeHintsFileName))
            {
                return;
            }

            string persistentPath = Path.Combine(Application.persistentDataPath, "Data", routeHintsFileName);
            string streamingPath = Path.Combine(Application.streamingAssetsPath, "Data", routeHintsFileName);
            string sourcePath = null;

            if (File.Exists(persistentPath))
            {
                sourcePath = persistentPath;
            }
            else if (File.Exists(streamingPath))
            {
                sourcePath = streamingPath;
            }

            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                if (routeHintLogs)
                {
                    Debug.Log($"[RouteCalculator] Route hints file not found: {routeHintsFileName}");
                }

                return;
            }

            try
            {
                string raw = File.ReadAllText(sourcePath);
                RouteHintManifest manifest = JsonUtility.FromJson<RouteHintManifest>(raw);
                if (manifest == null || manifest.edges == null || manifest.edges.Count == 0)
                {
                    return;
                }

                if (!IsRouteHintManifestForCurrentGraph(manifest))
                {
                    if (routeHintLogs)
                    {
                        Debug.Log($"[RouteCalculator] Route hints ignored for graph '{graphLoader?.GraphFileName}'. Hint graph='{manifest.graphHint}'.");
                    }

                    return;
                }

                float defaultMultiplier = manifest.defaultMultiplier > 0f
                    ? manifest.defaultMultiplier
                    : fallbackRouteHintMultiplier;
                defaultMultiplier = Mathf.Clamp(defaultMultiplier, 0.5f, 2f);

                for (int i = 0; i < manifest.edges.Count; i++)
                {
                    RouteHintEdge entry = manifest.edges[i];
                    if (entry == null)
                    {
                        continue;
                    }

                    float multiplier = entry.multiplier > 0f ? entry.multiplier : defaultMultiplier;
                    multiplier = Mathf.Clamp(multiplier, 0.5f, 2f);
                    AddRouteHintEdgeMultiplier(entry.fromNodeId, entry.toNodeId, multiplier);
                }

                _routeHintVersion = string.IsNullOrWhiteSpace(manifest.version)
                    ? "unversioned"
                    : manifest.version.Trim();
                _routeHintEdgeCount = _routeHintEdgeMultipliersByKey.Count;

                if (routeHintLogs)
                {
                    Debug.Log($"[RouteCalculator] Loaded {_routeHintEdgeCount} route hint edges (v={_routeHintVersion}) from {sourcePath}.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RouteCalculator] Failed to load route hints from '{sourcePath}': {ex.Message}");
            }
        }

        private bool IsRouteHintManifestForCurrentGraph(RouteHintManifest manifest)
        {
            if (manifest == null || string.IsNullOrWhiteSpace(manifest.graphHint))
            {
                return true;
            }

            string graphHint = manifest.graphHint.Trim();
            string graphFileName = graphLoader != null ? graphLoader.GraphFileName : string.Empty;
            string estateName = graphLoader?.GraphData?.metadata?.estateName ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(graphFileName) &&
                graphFileName.IndexOf(graphHint, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(estateName) &&
                estateName.IndexOf(graphHint, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            return false;
        }

        private void AddRouteHintEdgeMultiplier(string fromNodeId, string toNodeId, float multiplier)
        {
            if (string.IsNullOrWhiteSpace(fromNodeId) || string.IsNullOrWhiteSpace(toNodeId))
            {
                return;
            }

            string key = BuildDirectedEdgeKey(fromNodeId.Trim(), toNodeId.Trim());
            if (!_edgeDistanceMetersByKey.ContainsKey(key))
            {
                return;
            }

            if (_routeHintEdgeMultipliersByKey.TryGetValue(key, out float existing))
            {
                _routeHintEdgeMultipliersByKey[key] = Mathf.Min(existing, multiplier);
            }
            else
            {
                _routeHintEdgeMultipliersByKey[key] = multiplier;
            }
        }

        private static RouteRequest CloneRouteRequest(RouteRequest request)
        {
            if (request == null)
            {
                return null;
            }

            return new RouteRequest
            {
                startNodeId = request.startNodeId,
                endNodeId = request.endNodeId,
                currentLevel = request.currentLevel,
                mode = request.mode,
                rainMode = request.rainMode,
                boundaryRevision = request.boundaryRevision,
                recalcReason = request.recalcReason
            };
        }

        private void HandleBoundaryUpdated()
        {
            ClearRouteCache();
        }

        private static RouteResult CloneRouteResult(RouteResult result)
        {
            if (result == null)
            {
                return null;
            }

            return new RouteResult
            {
                success = result.success,
                totalCost = result.totalCost,
                totalDistance = result.totalDistance,
                estimatedWalkTimeSeconds = result.estimatedWalkTimeSeconds,
                message = result.message,
                recalcReason = result.recalcReason,
                nodePath = result.nodePath != null ? new List<string>(result.nodePath) : new List<string>()
            };
        }

        private static string WrapTopLevelArrayIfNeeded(string rawJson, string key)
        {
            return DataFileUtility.WrapTopLevelArrayIfNeeded(rawJson, key);
        }

        private static IEnumerator CopyFromStreamingAssets(string sourcePath, string destinationPath)
        {
            string folder = Path.GetDirectoryName(destinationPath);
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            using (UnityWebRequest request = UnityWebRequest.Get(ToUnityWebRequestPath(sourcePath)))
            {
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    File.WriteAllText(destinationPath, request.downloadHandler.text);
                }
                else
                {
                    Debug.LogError($"Unable to copy routing profiles JSON: {request.error}");
                }
            }
        }

        private static string ToUnityWebRequestPath(string path)
        {
            return DataFileUtility.ToUnityWebRequestPath(path);
        }
    }
}
