using UnityEngine;

namespace HeatmapTool
{
    public sealed class HeatmapSettings : ScriptableObject
    {
        public const string ResourceName = "HeatmapSettings";

        [Tooltip("Builds save sessions to a Heatmaps folder next to the game's executable instead of persistentDataPath.")]
        public bool saveBuildsNextToExecutable;

        static HeatmapSettings instance;

        public static HeatmapSettings Instance
        {
            get
            {
                if (instance == null) instance = Resources.Load<HeatmapSettings>(ResourceName);
                return instance;
            }
        }
    }
}
