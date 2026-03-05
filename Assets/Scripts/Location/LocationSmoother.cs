using System;
using UnityEngine;

namespace CDE2501.Wayfinding.Location
{
    [Serializable]
    public struct GeoPoint
    {
        public double latitude;
        public double longitude;

        public GeoPoint(double latitude, double longitude)
        {
            this.latitude = latitude;
            this.longitude = longitude;
        }
    }

    public static class LocationSmoother
    {
        public static GeoPoint ExponentialSmooth(GeoPoint current, GeoPoint previous, float alpha)
        {
            alpha = Mathf.Clamp01(alpha);
            return new GeoPoint(
                alpha * current.latitude + (1f - alpha) * previous.latitude,
                alpha * current.longitude + (1f - alpha) * previous.longitude
            );
        }

        public static float SmoothAngle(float current, float previous, float alpha)
        {
            alpha = Mathf.Clamp01(alpha);
            return Mathf.LerpAngle(previous, current, alpha);
        }
    }
}
