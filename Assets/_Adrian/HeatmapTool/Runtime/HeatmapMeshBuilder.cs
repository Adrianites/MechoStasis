using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace HeatmapTool
{
    public enum HeatmapStyle
    {
        Tiles,
        Cubes,
        Columns,
    }

    public enum HeatmapScale
    {
        Linear,
        Logarithmic,
    }

    public enum HeatmapPathColors
    {
        ByMovementState,
        BySession,
    }

    [Serializable]
    public sealed class HeatmapAppearance
    {
        public HeatmapStyle style = HeatmapStyle.Tiles;
        public HeatmapScale scale = HeatmapScale.Linear;
        public Gradient gradient = DefaultGradient();
        [Tooltip("Cells at or above this percentile get the hottest colour.")]
        [Range(50f, 100f)] public float clampPercentile = 98f;
        [Tooltip("Hide cells below this fraction of the hottest colour.")]
        [Range(0f, 1f)] public float hideBelow = 0.02f;
        [Tooltip("How much of each cell is filled. Below 1 leaves gaps between cells.")]
        [Range(0.1f, 1f)] public float cellFill = 0.9f;
        [Tooltip("Height of the hottest column, in cells.")]
        [Min(0.1f)] public float columnHeight = 3f;

        public static Gradient DefaultGradient()
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.15f, 0.25f, 0.95f), 0f),
                    new GradientColorKey(new Color(0.1f, 0.85f, 0.95f), 0.25f),
                    new GradientColorKey(new Color(0.2f, 0.9f, 0.3f), 0.5f),
                    new GradientColorKey(new Color(1f, 0.9f, 0.15f), 0.75f),
                    new GradientColorKey(new Color(0.95f, 0.15f, 0.1f), 1f),
                },
                new[] { new GradientAlphaKey(0.45f, 0f), new GradientAlphaKey(0.95f, 1f) });
            return gradient;
        }
    }

    public static class HeatmapMeshBuilder
    {
        const float FloorLift = 0.03f;

        static readonly List<Vector3> vertices = new List<Vector3>();
        static readonly List<Color32> colors = new List<Color32>();
        static readonly List<int> indices = new List<int>();
        static readonly Color32[] gradientLut = new Color32[256];

        static readonly int[] boxIndices =
        {
            0, 2, 1, 0, 3, 2,
            4, 5, 6, 4, 6, 7,
            0, 1, 5, 0, 5, 4,
            1, 2, 6, 1, 6, 5,
            2, 3, 7, 2, 7, 6,
            3, 0, 4, 3, 4, 7,
        };

        static readonly int[] diamondIndices =
        {
            0, 2, 4, 2, 1, 4, 1, 3, 4, 3, 0, 4,
            2, 0, 5, 1, 2, 5, 3, 1, 5, 0, 3, 5,
        };

        public static int BuildHeat(Mesh mesh, HeatmapGrid grid, HeatmapAppearance look)
        {
            Begin();

            float max = grid.GetPercentileValue(look.clampPercentile / 100f);
            if (max <= 0f) max = grid.MaxValue;
            if (max <= 0f)
            {
                Commit(mesh, MeshTopology.Triangles);
                return 0;
            }

            for (int i = 0; i < gradientLut.Length; i++)
                gradientLut[i] = look.gradient.Evaluate(i / 255f);

            float logMax = Mathf.Log(1f + max);
            float half = grid.CellSize * 0.5f * look.cellFill;
            int drawn = 0;

            IReadOnlyList<HeatmapCell> cells = grid.Cells;
            for (int i = 0; i < cells.Count; i++)
            {
                HeatmapCell cell = cells[i];
                float t = look.scale == HeatmapScale.Logarithmic ? Mathf.Log(1f + cell.value) / logMax : cell.value / max;
                t = Mathf.Clamp01(t);
                if (t < look.hideBelow) continue;

                Color32 color = gradientLut[(int)(t * 255f + 0.5f)];
                Vector3 c = cell.center;
                switch (look.style)
                {
                    case HeatmapStyle.Tiles:
                        AddQuad(new Vector3(c.x, c.y + FloorLift, c.z), half, color);
                        break;
                    case HeatmapStyle.Cubes:
                        AddBox(new Vector3(c.x - half, c.y, c.z - half), new Vector3(c.x + half, c.y + half * 2f, c.z + half), color);
                        break;
                    case HeatmapStyle.Columns:
                        float height = Mathf.Max(FloorLift, t * grid.CellSize * look.columnHeight);
                        AddBox(new Vector3(c.x - half, c.y, c.z - half), new Vector3(c.x + half, c.y + height, c.z + half), color);
                        break;
                }
                drawn++;
            }

            Commit(mesh, MeshTopology.Triangles);
            return drawn;
        }

        public static int BuildMarkers(Mesh mesh, IReadOnlyList<HeatmapSession> sessions, HeatmapQuery query, float size)
        {
            Begin();
            bool onlyOneEvent = query.source == HeatmapSource.Events && !string.IsNullOrEmpty(query.eventName);
            int drawn = 0;

            for (int s = 0; s < sessions.Count; s++)
            {
                HeatmapSession session = sessions[s];
                var typeColors = new Color32[session.eventNames.Count];
                for (int i = 0; i < typeColors.Length; i++) typeColors[i] = EventColor(session.eventNames[i]);

                foreach (HeatmapEvent e in session.events)
                {
                    if (!query.InTimeRange(e.t)) continue;
                    if (onlyOneEvent && session.GetEventName(e.type) != query.eventName) continue;
                    Color32 color = e.type < typeColors.Length ? typeColors[e.type] : (Color32)Color.white;
                    AddDiamond(e.position + Vector3.up * size, size, color);
                    drawn++;
                }
            }

            Commit(mesh, MeshTopology.Triangles);
            return drawn;
        }

        public static int BuildPaths(Mesh mesh, IReadOnlyList<HeatmapSession> sessions, HeatmapQuery query,
            HeatmapPathColors colorMode, float breakDistance)
        {
            Begin();
            float breakSqr = breakDistance * breakDistance;
            bool filterStates = query.source == HeatmapSource.TimeSpent;
            var stateColors = new Color32[4];
            for (int i = 0; i < stateColors.Length; i++) stateColors[i] = StateColor((HeatmapMoveState)i);
            int drawn = 0;

            for (int s = 0; s < sessions.Count; s++)
            {
                HeatmapSession session = sessions[s];
                Color32 sessionColor = HashColor(session.sessionId);
                bool hasPrevious = false;
                Vector3 previous = default;

                foreach (HeatmapSample sample in session.samples)
                {
                    if (!query.InTimeRange(sample.t) || (filterStates && !query.states.Includes(sample.state)))
                    {
                        hasPrevious = false;
                        continue;
                    }

                    Vector3 point = sample.position + Vector3.up * 0.1f;
                    if (hasPrevious && (point - previous).sqrMagnitude <= breakSqr)
                    {
                        Color32 color = colorMode == HeatmapPathColors.BySession
                            ? sessionColor
                            : stateColors[Mathf.Min((int)sample.state, stateColors.Length - 1)];
                        indices.Add(vertices.Count);
                        vertices.Add(previous);
                        colors.Add(color);
                        indices.Add(vertices.Count);
                        vertices.Add(point);
                        colors.Add(color);
                        drawn++;
                    }
                    previous = point;
                    hasPrevious = true;
                }
            }

            Commit(mesh, MeshTopology.Lines);
            return drawn;
        }

        public static Color StateColor(HeatmapMoveState state)
        {
            switch (state)
            {
                case HeatmapMoveState.Idle: return new Color(0.65f, 0.65f, 0.7f);
                case HeatmapMoveState.Walking: return new Color(0.3f, 0.85f, 0.35f);
                case HeatmapMoveState.Sprinting: return new Color(1f, 0.45f, 0.1f);
                case HeatmapMoveState.Airborne: return new Color(0.25f, 0.7f, 1f);
                default: return Color.white;
            }
        }

        public static Color EventColor(string eventName)
        {
            switch (eventName)
            {
                case HeatmapEvents.Jump: return new Color(0.2f, 0.9f, 1f);
                case HeatmapEvents.Land: return new Color(1f, 0.75f, 0.1f);
                case HeatmapEvents.Fall: return new Color(0.85f, 0.3f, 1f);
                default: return HashColor(eventName);
            }
        }

        public static Color HashColor(string text)
        {
            uint hash = 2166136261;
            if (text != null)
                foreach (char c in text) hash = (hash ^ c) * 16777619;
            return Color.HSVToRGB((hash % 360) / 360f, 0.7f, 1f);
        }

        static void Begin()
        {
            vertices.Clear();
            colors.Clear();
            indices.Clear();
        }

        static void Commit(Mesh mesh, MeshTopology topology)
        {
            mesh.Clear();
            mesh.indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetIndices(indices, topology, 0, false);
            mesh.RecalculateBounds();
        }

        static void AddQuad(Vector3 center, float half, Color32 color)
        {
            int start = vertices.Count;
            vertices.Add(new Vector3(center.x - half, center.y, center.z - half));
            vertices.Add(new Vector3(center.x + half, center.y, center.z - half));
            vertices.Add(new Vector3(center.x + half, center.y, center.z + half));
            vertices.Add(new Vector3(center.x - half, center.y, center.z + half));
            for (int i = 0; i < 4; i++) colors.Add(color);
            indices.Add(start); indices.Add(start + 2); indices.Add(start + 1);
            indices.Add(start); indices.Add(start + 3); indices.Add(start + 2);
        }

        static void AddBox(Vector3 min, Vector3 max, Color32 color)
        {
            int start = vertices.Count;
            vertices.Add(new Vector3(min.x, min.y, min.z));
            vertices.Add(new Vector3(max.x, min.y, min.z));
            vertices.Add(new Vector3(max.x, min.y, max.z));
            vertices.Add(new Vector3(min.x, min.y, max.z));
            vertices.Add(new Vector3(min.x, max.y, min.z));
            vertices.Add(new Vector3(max.x, max.y, min.z));
            vertices.Add(new Vector3(max.x, max.y, max.z));
            vertices.Add(new Vector3(min.x, max.y, max.z));
            for (int i = 0; i < 8; i++) colors.Add(color);
            foreach (int index in boxIndices) indices.Add(start + index);
        }

        static void AddDiamond(Vector3 center, float size, Color32 color)
        {
            int start = vertices.Count;
            float r = size * 0.6f;
            vertices.Add(center + new Vector3(-r, 0f, 0f));
            vertices.Add(center + new Vector3(r, 0f, 0f));
            vertices.Add(center + new Vector3(0f, 0f, -r));
            vertices.Add(center + new Vector3(0f, 0f, r));
            vertices.Add(center + new Vector3(0f, size, 0f));
            vertices.Add(center + new Vector3(0f, -size, 0f));
            for (int i = 0; i < 6; i++) colors.Add(color);
            foreach (int index in diamondIndices) indices.Add(start + index);
        }
    }
}
