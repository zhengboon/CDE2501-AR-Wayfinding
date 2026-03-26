using System;
using System.Collections.Generic;
using CDE2501.Wayfinding.IndoorGraph;
using CDE2501.Wayfinding.Profiles;
using UnityEngine;

namespace CDE2501.Wayfinding.Routing
{
    public class AStarPathfinder
    {
        private const float LargePenalty = 10000f;

        private readonly Dictionary<string, Node> _nodes;
        private readonly Dictionary<string, List<Edge>> _adjacency;

        public AStarPathfinder(Dictionary<string, Node> nodes, List<Edge> edges)
        {
            _nodes = nodes ?? throw new ArgumentNullException(nameof(nodes));
            _adjacency = BuildAdjacency(edges ?? new List<Edge>());
        }

        public RouteResult FindPath(
            RouteRequest request,
            RoutingProfile profile,
            float rainSlopeMultiplier = 1.25f,
            Func<string, bool> nodeEligibility = null)
        {
            if (request == null)
            {
                return RouteResult.Failed("Route request is null.");
            }

            if (profile == null)
            {
                return RouteResult.Failed("Routing profile is null.");
            }

            if (!_nodes.ContainsKey(request.startNodeId) || !_nodes.ContainsKey(request.endNodeId))
            {
                return RouteResult.Failed("Start or end node not found.");
            }

            if (nodeEligibility != null &&
                (!nodeEligibility(request.startNodeId) || !nodeEligibility(request.endNodeId)))
            {
                return RouteResult.Failed("Start or destination is outside the active boundary.");
            }

            var openSet = new MinHeap();
            var cameFrom = new Dictionary<string, string>();
            var gScore = new Dictionary<string, float>();

            // Lazy initialization: only set start node score.
            // Unvisited nodes are implicitly float.PositiveInfinity.
            if (nodeEligibility != null &&
                (!nodeEligibility(request.startNodeId) || !nodeEligibility(request.endNodeId)))
            {
                return RouteResult.Failed("Start or destination is outside the active boundary.");
            }

            gScore[request.startNodeId] = 0f;
            openSet.Push(request.startNodeId, Heuristic(request.startNodeId, request.endNodeId));

            while (openSet.Count > 0)
            {
                string current = openSet.Pop();
                if (nodeEligibility != null && !nodeEligibility(current))
                {
                    continue;
                }

                if (current == request.endNodeId)
                {
                    return BuildResult(current, cameFrom, gScore[current]);
                }

                if (!_adjacency.TryGetValue(current, out List<Edge> neighbors))
                {
                    continue;
                }

                float currentG = gScore.TryGetValue(current, out float cg) ? cg : float.PositiveInfinity;

                foreach (Edge edge in neighbors)
                {
                    string neighbor = edge.toNode;
                    if (nodeEligibility != null && !nodeEligibility(neighbor))
                    {
                        continue;
                    }

                    if (!_nodes.ContainsKey(neighbor))
                    {
                        continue;
                    }

                    float tentativeG = currentG + ComputeEdgeCost(edge, request, profile, rainSlopeMultiplier);
                    float neighborG = gScore.TryGetValue(neighbor, out float ng) ? ng : float.PositiveInfinity;

                    if (tentativeG >= neighborG)
                    {
                        continue;
                    }

                    cameFrom[neighbor] = current;
                    gScore[neighbor] = tentativeG;
                    float fScore = tentativeG + Heuristic(neighbor, request.endNodeId);
                    openSet.Push(neighbor, fScore);
                }
            }

            return RouteResult.Failed("No route found.");
        }

        private float ComputeEdgeCost(Edge edge, RouteRequest request, RoutingProfile profile, float rainSlopeMultiplier)
        {
            float slope = request.rainMode ? edge.slope * rainSlopeMultiplier : edge.slope;
            float rainPenalty = request.rainMode && !edge.sheltered ? profile.wUnshelteredRain : 0f;

            float baseCost =
                (profile.wDistance * edge.distance) +
                (profile.wSlope * slope) +
                (profile.wStairs * (edge.hasStairs ? 1f : 0f)) +
                (profile.wClutter * edge.clutter) +
                (profile.wDarkness * Mathf.Clamp01(1f - edge.lighting)) +
                (profile.wNarrowWidth * Mathf.Clamp01(1f - edge.width)) +
                rainPenalty;

            if (request.mode == RoutingMode.Wheelchair)
            {
                if (edge.hasStairs)
                {
                    baseCost += profile.wheelchairStairsBlockCost;
                }

                if (edge.width < profile.minWidthPassable)
                {
                    baseCost += LargePenalty;
                }
            }

            return baseCost;
        }

        private float Heuristic(string nodeA, string nodeB)
        {
            return Vector3.Distance(_nodes[nodeA].position, _nodes[nodeB].position);
        }

        private RouteResult BuildResult(string current, Dictionary<string, string> cameFrom, float totalCost)
        {
            var path = new List<string> { current };
            while (cameFrom.TryGetValue(current, out string parent))
            {
                current = parent;
                path.Add(current);
            }

            path.Reverse();
            return RouteResult.Success(path, totalCost);
        }

        private static Dictionary<string, List<Edge>> BuildAdjacency(List<Edge> edges)
        {
            var adjacency = new Dictionary<string, List<Edge>>();
            foreach (Edge edge in edges)
            {
                if (!adjacency.ContainsKey(edge.fromNode))
                {
                    adjacency[edge.fromNode] = new List<Edge>();
                }

                adjacency[edge.fromNode].Add(edge);
            }

            return adjacency;
        }

        private class MinHeap
        {
            private readonly List<(string nodeId, float priority)> _heap = new List<(string, float)>();

            public int Count => _heap.Count;

            public void Push(string nodeId, float priority)
            {
                _heap.Add((nodeId, priority));
                SiftUp(_heap.Count - 1);
            }

            public string Pop()
            {
                if (_heap.Count == 0)
                {
                    throw new InvalidOperationException("Heap is empty.");
                }

                string root = _heap[0].nodeId;
                (string nodeId, float priority) last = _heap[_heap.Count - 1];
                _heap.RemoveAt(_heap.Count - 1);

                if (_heap.Count > 0)
                {
                    _heap[0] = last;
                    SiftDown(0);
                }

                return root;
            }

            private void SiftUp(int index)
            {
                while (index > 0)
                {
                    int parent = (index - 1) / 2;
                    if (_heap[parent].priority <= _heap[index].priority)
                    {
                        break;
                    }

                    (_heap[parent], _heap[index]) = (_heap[index], _heap[parent]);
                    index = parent;
                }
            }

            private void SiftDown(int index)
            {
                int count = _heap.Count;
                while (true)
                {
                    int left = index * 2 + 1;
                    int right = left + 1;
                    int smallest = index;

                    if (left < count && _heap[left].priority < _heap[smallest].priority)
                    {
                        smallest = left;
                    }

                    if (right < count && _heap[right].priority < _heap[smallest].priority)
                    {
                        smallest = right;
                    }

                    if (smallest == index)
                    {
                        break;
                    }

                    (_heap[index], _heap[smallest]) = (_heap[smallest], _heap[index]);
                    index = smallest;
                }
            }
        }
    }
}
