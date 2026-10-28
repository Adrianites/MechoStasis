using System;
using System.Collections.Generic;
using UnityEngine;

namespace HeatmapTool
{
    public enum HeatmapMoveState : byte
    {
        Idle = 0,
        Walking = 1,
        Sprinting = 2,
        Airborne = 3,
    }

    [Flags]
    public enum HeatmapStateMask
    {
        Idle = 1 << 0,
        Walking = 1 << 1,
        Sprinting = 1 << 2,
        Airborne = 1 << 3,
    }

    public static class HeatmapStateMaskExtensions
    {
        public const HeatmapStateMask All =
            HeatmapStateMask.Idle | HeatmapStateMask.Walking | HeatmapStateMask.Sprinting | HeatmapStateMask.Airborne;

        public static bool Includes(this HeatmapStateMask mask, HeatmapMoveState state)
        {
            return ((int)mask & (1 << (int)state)) != 0;
        }
    }

    public static class HeatmapEvents
    {
        public const string Jump = "Jump";
        public const string Fall = "Fall";
        public const string Land = "Land";
    }

    [Serializable]
    public struct HeatmapSample
    {
        public float t;
        public Vector3 position;
        public HeatmapMoveState state;
    }

    [Serializable]
    public struct HeatmapEvent
    {
        public float t;
        public Vector3 position;
        public int type;
        public float value;
    }

    [Serializable]
    public sealed class HeatmapSession
    {
        public string sessionId;
        public string sceneName;
        public string scenePath;
        public string playerLabel;
        public long startTicksUtc;
        public string appVersion;
        public string unityVersion;
        public string platform;
        public bool fromEditor;
        public float sampleInterval;
        public List<string> eventNames = new List<string>();
        public HeatmapSample[] samples = Array.Empty<HeatmapSample>();
        public HeatmapEvent[] events = Array.Empty<HeatmapEvent>();

        public DateTime StartTimeUtc => new DateTime(Math.Max(0L, startTicksUtc), DateTimeKind.Utc);

        public float Duration
        {
            get
            {
                float duration = samples.Length > 0 ? samples[samples.Length - 1].t : 0f;
                for (int i = 0; i < events.Length; i++)
                    duration = Mathf.Max(duration, events[i].t);
                return duration;
            }
        }

        public string GetEventName(int type)
        {
            return type >= 0 && type < eventNames.Count ? eventNames[type] : "Unknown";
        }

        internal void Repair()
        {
            eventNames ??= new List<string>();
            samples ??= Array.Empty<HeatmapSample>();
            events ??= Array.Empty<HeatmapEvent>();
            if (string.IsNullOrEmpty(sessionId)) sessionId = Guid.NewGuid().ToString("N");
            if (sampleInterval <= 0f) sampleInterval = 0.2f;
        }
    }
}
