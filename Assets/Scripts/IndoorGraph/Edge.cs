using System;

namespace CDE2501.Wayfinding.IndoorGraph
{
    [Serializable]
    public class Edge
    {
        public string fromNode;
        public string toNode;
        public float distance;
        public float slope;
        public bool hasStairs;
        public bool sheltered;
        public float clutter;
        public float lighting;
        public float width;
    }
}
