using System;
using System.Collections.Generic;

namespace CDE2501.Wayfinding.Profiles
{
    public enum RoutingMode
    {
        NormalElderly = 0,
        Wheelchair = 1
    }

    [Serializable]
    public class RoutingProfile
    {
        public string profileName;
        public float wDistance = 1f;
        public float wSlope = 1f;
        public float wStairs = 1f;
        public float wClutter = 1f;
        public float wDarkness = 1f;
        public float wNarrowWidth = 1f;
        public float wUnshelteredRain = 1f;
        public float wheelchairStairsBlockCost = 10000f;
        public float minWidthPassable = 0.35f;
    }

    [Serializable]
    public class RoutingProfilesConfig
    {
        public bool smartPromptDefault = true;
        public float rainSlopeMultiplier = 1.25f;
        public List<RoutingProfile> profiles = new List<RoutingProfile>();

        public RoutingProfile GetByMode(RoutingMode mode)
        {
            string expected = mode == RoutingMode.Wheelchair ? "Wheelchair" : "NormalElderly";
            return profiles.Find(p => p.profileName == expected);
        }
    }
}
