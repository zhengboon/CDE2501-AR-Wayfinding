using System;
using UnityEngine;

namespace CDE2501.Wayfinding.IndoorGraph
{
    [Serializable]
    public class Node
    {
        public string id;
        public Vector3 position;
        public int elevationLevel;
        public bool hasStairs;
        public float slopeLevel;
        public float lightingLevel;
        public float clutterLevel;
        public float widthLevel;
        public bool sheltered;
    }
}
