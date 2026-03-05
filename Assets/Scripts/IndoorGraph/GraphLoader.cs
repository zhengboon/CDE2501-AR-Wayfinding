using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace CDE2501.Wayfinding.IndoorGraph
{
    [Serializable]
    public class GraphMetadata
    {
        public string estateName;
        public string version;
    }

    [Serializable]
    public class GraphData
    {
        public GraphMetadata metadata;
        public List<Node> nodes = new List<Node>();
        public List<Edge> edges = new List<Edge>();
    }

    public class GraphLoader : MonoBehaviour
    {
        [SerializeField] private string graphFileName = "estate_graph.json";

        public Dictionary<string, Node> NodesById { get; private set; } = new Dictionary<string, Node>();
        public List<Edge> Edges { get; private set; } = new List<Edge>();
        public GraphData GraphData { get; private set; }

        public event Action<bool, string> OnGraphLoaded;

        public void LoadGraph()
        {
            StartCoroutine(LoadGraphRoutine());
        }

        private IEnumerator LoadGraphRoutine()
        {
            string persistentPath = GetPersistentPath(graphFileName);
            string streamingPath = GetStreamingPath(graphFileName);

            if (!File.Exists(persistentPath))
            {
                yield return CopyFromStreamingAssets(streamingPath, persistentPath);
            }

            if (!File.Exists(persistentPath))
            {
                OnGraphLoaded?.Invoke(false, $"Missing graph file at {persistentPath}");
                yield break;
            }

            string json = File.ReadAllText(persistentPath);
            GraphData = JsonUtility.FromJson<GraphData>(json);

            if (GraphData == null)
            {
                OnGraphLoaded?.Invoke(false, "Failed to parse graph JSON.");
                yield break;
            }

            BuildCaches();
            OnGraphLoaded?.Invoke(true, "Graph loaded.");
        }

        public Node GetNode(string nodeId)
        {
            NodesById.TryGetValue(nodeId, out Node node);
            return node;
        }

        private void BuildCaches()
        {
            NodesById.Clear();
            Edges.Clear();

            foreach (Node node in GraphData.nodes)
            {
                if (!string.IsNullOrWhiteSpace(node.id) && !NodesById.ContainsKey(node.id))
                {
                    NodesById[node.id] = node;
                }
            }

            foreach (Edge edge in GraphData.edges)
            {
                if (NodesById.ContainsKey(edge.fromNode) && NodesById.ContainsKey(edge.toNode))
                {
                    Edges.Add(edge);
                }
            }
        }

        private static IEnumerator CopyFromStreamingAssets(string sourcePath, string destinationPath)
        {
            string folder = Path.GetDirectoryName(destinationPath);
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            UnityWebRequest request = UnityWebRequest.Get(ToUnityWebRequestPath(sourcePath));
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                File.WriteAllText(destinationPath, request.downloadHandler.text);
            }
            else
            {
                Debug.LogError($"Unable to copy graph from StreamingAssets: {request.error}");
            }

            request.Dispose();
        }

        private static string ToUnityWebRequestPath(string path)
        {
            if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("file://", StringComparison.OrdinalIgnoreCase) ||
                path.Contains("://"))
            {
                return path;
            }

            return "file://" + path;
        }

        private static string GetStreamingPath(string fileName)
        {
            return Path.Combine(Application.streamingAssetsPath, "Data", fileName);
        }

        private static string GetPersistentPath(string fileName)
        {
            return Path.Combine(Application.persistentDataPath, "Data", fileName);
        }
    }
}
