using System;
using System.Collections.Generic;

namespace CDE2501.Wayfinding.Data
{
    [Serializable]
    public class VideoFrameMapManifest
    {
        public string version;
        public string generatedAtUtc;
        public string sourceCsv;
        public List<VideoEntry> videos;
    }

    [Serializable]
    public class VideoEntry
    {
        public string videoId;
        public string title;
        public string uploader;
        public string url;
        public string duration;
        public float durationSeconds;
        public string startNodeId;
        public string endNodeId;
        public List<string> routeNodePath;
        public List<VideoFrameEntry> frames;
    }

    [Serializable]
    public class VideoFrameEntry
    {
        public string image;
        public float timeSeconds;
        public string nodeId;
        public VideoFramePosition position;
    }

    [Serializable]
    public class VideoFramePosition
    {
        public float x;
        public float y;
        public float z;
    }
}
