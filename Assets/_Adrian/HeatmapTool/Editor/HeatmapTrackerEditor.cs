using UnityEditor;
using UnityEngine;

namespace HeatmapTool
{
    [CustomEditor(typeof(HeatmapTracker))]
    sealed class HeatmapTrackerEditor : UnityEditor.Editor
    {
        public override bool RequiresConstantRepaint() => Application.isPlaying;

        public override void OnInspectorGUI()
        {
            var tracker = (HeatmapTracker)target;

            if (Application.isPlaying) DrawLiveStatus(tracker);

            DrawDefaultInspector();

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Open Heatmap Viewer")) HeatmapWindow.Open();
                if (GUILayout.Button("Open Data Folder")) HeatmapWindow.OpenDataFolder();
            }
        }

        static void DrawLiveStatus(HeatmapTracker tracker)
        {
            if (!tracker.IsRecording)
            {
                EditorGUILayout.HelpBox("Not recording. Check 'Record In Editor', and that Heatmap.RecordingAllowed is true.",
                    MessageType.Warning);
                return;
            }

            EditorGUILayout.HelpBox(
                $"Recording  ·  {HeatmapWindow.FormatDuration(tracker.SessionTime)}  ·  {tracker.SampleCount:N0} samples  ·  " +
                $"{tracker.EventCount:N0} events{(tracker.IsAirborne ? "  ·  airborne" : "")}\n{tracker.CurrentFilePath}",
                MessageType.Info);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Flush Now")) tracker.Flush();
                if (GUILayout.Button("Start New Session")) tracker.StartNewSession();
                if (GUILayout.Button("Show File")) EditorUtility.RevealInFinder(tracker.CurrentFilePath);
            }
            EditorGUILayout.Space();
        }
    }
}
