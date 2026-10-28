using UnityEngine;

namespace HeatmapTool
{
    public static class Heatmap
    {
        static bool recordingAllowed = true;

        public static bool RecordingAllowed
        {
            get => recordingAllowed;
            set
            {
                if (recordingAllowed == value) return;
                recordingAllowed = value;
                var trackers = HeatmapTracker.Active;
                for (int i = 0; i < trackers.Count; i++) trackers[i].RefreshRecordingState();
            }
        }

        public static bool IsRecording
        {
            get
            {
                var trackers = HeatmapTracker.Active;
                for (int i = 0; i < trackers.Count; i++)
                    if (trackers[i].IsRecording) return true;
                return false;
            }
        }

        public static string DefaultFolder => HeatmapIO.DefaultFolder;

        public static void LogEvent(string eventName, Vector3 position, float value = 0f)
        {
            var trackers = HeatmapTracker.Active;
            for (int i = 0; i < trackers.Count; i++)
            {
                if (!trackers[i].IsRecording) continue;
                trackers[i].RecordEvent(eventName, position, value);
                return;
            }
        }

        public static void FlushAll()
        {
            var trackers = HeatmapTracker.Active;
            for (int i = 0; i < trackers.Count; i++) trackers[i].Flush();
        }
    }
}
