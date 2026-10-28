using System;
using UnityEngine;

namespace HeatmapTool
{
    [AddComponentMenu("")]
    public sealed class HeatmapViewHandle : MonoBehaviour
    {
        [NonSerialized] internal HeatmapView owner;
    }
}
