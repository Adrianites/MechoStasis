using System;
using System.Collections.Generic;
using UnityEngine;

namespace HeatmapTool
{
    public enum HeatmapSource
    {
        TimeSpent,
        Events,
    }

    [Serializable]
    public sealed class HeatmapQuery
    {
        public HeatmapSource source = HeatmapSource.TimeSpent;
        public HeatmapStateMask states = HeatmapStateMaskExtensions.All;
        [Tooltip("Event to count when the source is Events. Empty counts every event.")]
        public string eventName = "";
        public bool useTimeRange;
        public float timeMin;
        public float timeMax = 60f;
        [Min(0.05f)] public float cellSize = 1f;
        [Tooltip("Merge all heights into one layer. Good for single-floor maps; leave off for multi-storey levels.")]
        public bool flatten;
        [Tooltip("Blur radius in cells, along the floor only.")]
        [Range(0, 5)] public int smoothing;

        public bool InTimeRange(float t)
        {
            return !useTimeRange || (t >= timeMin && t <= timeMax);
        }
    }

    public struct HeatmapCell
    {
        public Vector3Int index;
        public Vector3 center;
        public float value;
    }

    public sealed class HeatmapGrid
    {
        struct Accumulator
        {
            public Vector3Int index;
            public float weight;
            public double heightSum;
        }

        readonly Dictionary<long, int> lookup = new Dictionary<long, int>();
        readonly List<HeatmapCell> cells = new List<HeatmapCell>();
        Accumulator[] accumulators = new Accumulator[256];
        int accumulatorCount;
        float[] sortedValues = Array.Empty<float>();

        public IReadOnlyList<HeatmapCell> Cells => cells;
        public float CellSize { get; private set; } = 1f;
        public HeatmapSource Source { get; private set; }
        public float MaxValue { get; private set; }
        public float TotalValue { get; private set; }
        public Bounds Bounds { get; private set; }

        public void Build(IReadOnlyList<HeatmapSession> sessions, HeatmapQuery query)
        {
            CellSize = Mathf.Max(0.05f, query.cellSize);
            Source = query.source;
            lookup.Clear();
            accumulatorCount = 0;

            float inverseCellSize = 1f / CellSize;
            for (int s = 0; s < sessions.Count; s++)
            {
                HeatmapSession session = sessions[s];
                if (query.source == HeatmapSource.TimeSpent)
                {
                    float weight = session.sampleInterval;
                    HeatmapSample[] samples = session.samples;
                    for (int i = 0; i < samples.Length; i++)
                    {
                        if (!query.states.Includes(samples[i].state) || !query.InTimeRange(samples[i].t)) continue;
                        Add(samples[i].position, weight, inverseCellSize, query.flatten);
                    }
                }
                else
                {
                    int typeFilter = -1;
                    if (!string.IsNullOrEmpty(query.eventName))
                    {
                        typeFilter = session.eventNames.IndexOf(query.eventName);
                        if (typeFilter < 0) continue;
                    }

                    HeatmapEvent[] events = session.events;
                    for (int i = 0; i < events.Length; i++)
                    {
                        if ((typeFilter >= 0 && events[i].type != typeFilter) || !query.InTimeRange(events[i].t)) continue;
                        Add(events[i].position, 1f, inverseCellSize, query.flatten);
                    }
                }
            }

            if (query.smoothing > 0) Smooth(query.smoothing);
            Finish();
        }

        public float GetPercentileValue(float percentile)
        {
            int count = cells.Count;
            if (count == 0) return 0f;
            int i = Mathf.Clamp(Mathf.CeilToInt(Mathf.Clamp01(percentile) * count) - 1, 0, count - 1);
            return sortedValues[i];
        }

        void Add(Vector3 position, float weight, float inverseCellSize, bool flatten)
        {
            var index = new Vector3Int(
                Mathf.FloorToInt(position.x * inverseCellSize),
                flatten ? 0 : Mathf.FloorToInt(position.y * inverseCellSize),
                Mathf.FloorToInt(position.z * inverseCellSize));
            Accumulate(index, weight, (double)position.y * weight);
        }

        void Accumulate(Vector3Int index, float weight, double heightSum)
        {
            long key = ((long)(index.x & 0x1FFFFF) << 42) | ((long)(index.y & 0x1FFFFF) << 21) | (long)(index.z & 0x1FFFFF);
            if (!lookup.TryGetValue(key, out int i))
            {
                i = accumulatorCount++;
                if (i == accumulators.Length) Array.Resize(ref accumulators, i * 2);
                accumulators[i] = new Accumulator { index = index };
                lookup.Add(key, i);
            }
            accumulators[i].weight += weight;
            accumulators[i].heightSum += heightSum;
        }

        void Smooth(int radius)
        {
            radius = Mathf.Clamp(radius, 1, 5);
            int size = radius * 2 + 1;
            float sigma = (radius + 1) * 0.5f;
            var kernel = new float[size * size];
            float kernelSum = 0f;
            for (int z = -radius; z <= radius; z++)
            for (int x = -radius; x <= radius; x++)
            {
                float k = Mathf.Exp(-(x * x + z * z) / (2f * sigma * sigma));
                kernel[(z + radius) * size + x + radius] = k;
                kernelSum += k;
            }

            int sourceCount = accumulatorCount;
            var source = new Accumulator[sourceCount];
            Array.Copy(accumulators, source, sourceCount);
            lookup.Clear();
            accumulatorCount = 0;

            for (int i = 0; i < sourceCount; i++)
            {
                Accumulator a = source[i];
                double averageHeight = a.heightSum / a.weight;
                for (int z = -radius; z <= radius; z++)
                for (int x = -radius; x <= radius; x++)
                {
                    float w = a.weight * kernel[(z + radius) * size + x + radius] / kernelSum;
                    Accumulate(new Vector3Int(a.index.x + x, a.index.y, a.index.z + z), w, averageHeight * w);
                }
            }
        }

        void Finish()
        {
            cells.Clear();
            if (cells.Capacity < accumulatorCount) cells.Capacity = accumulatorCount;
            if (sortedValues.Length < accumulatorCount) sortedValues = new float[accumulatorCount];

            MaxValue = 0f;
            TotalValue = 0f;
            Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;

            for (int i = 0; i < accumulatorCount; i++)
            {
                Accumulator a = accumulators[i];
                if (a.weight <= 0f) continue;

                var center = new Vector3((a.index.x + 0.5f) * CellSize, (float)(a.heightSum / a.weight), (a.index.z + 0.5f) * CellSize);
                cells.Add(new HeatmapCell { index = a.index, center = center, value = a.weight });
                sortedValues[cells.Count - 1] = a.weight;

                MaxValue = Mathf.Max(MaxValue, a.weight);
                TotalValue += a.weight;
                min = Vector3.Min(min, center);
                max = Vector3.Max(max, center);
            }

            Array.Sort(sortedValues, 0, cells.Count);
            if (cells.Count == 0)
            {
                Bounds = default;
            }
            else
            {
                var bounds = new Bounds();
                bounds.SetMinMax(min, max);
                bounds.Expand(CellSize);
                Bounds = bounds;
            }
        }
    }
}
