using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace HeatmapTool
{
    public struct HeatmapMapInfo
    {
        public string image;
        public string scene;
        public int width;
        public int height;
        public float pixelsPerMetre;
        public float minX;
        public float maxX;
        public float minZ;
        public float maxZ;
    }

    public static class HeatmapMapCapture
    {
        public const string ImageSuffix = "_map";
        public const string BoundsSuffix = "_bounds";
        public const int MaxResolution = 8192;

        const float DepthBelow = 1000f;
        static readonly Color Background = new Color(0.12f, 0.12f, 0.12f, 1f);

        public static string BoundsPathFor(string imagePath)
        {
            return Path.Combine(Path.GetDirectoryName(imagePath) ?? "",
                Path.GetFileNameWithoutExtension(imagePath) + BoundsSuffix + HeatmapIO.CsvExtension);
        }

        public static HeatmapMapInfo Save(string imagePath, Bounds area, float hideAbove, int resolution)
        {
            Texture2D texture = Render(area, hideAbove, resolution, out HeatmapMapInfo info);
            try
            {
                HeatmapIO.EnsureFolderFor(imagePath);
                File.WriteAllBytes(imagePath, texture.EncodeToPNG());
            }
            finally
            {
                DestroySafe(texture);
            }

            info.image = Path.GetFileName(imagePath);
            HeatmapIO.SaveMapBoundsCsv(info, BoundsPathFor(imagePath));
            return info;
        }

        // Looks straight down with +Z at the top of the image and +X to the right. Geometry above 'hideAbove'
        // (a world height) is clipped so roofs and ceilings don't cover the floor.
        public static Texture2D Render(Bounds area, float hideAbove, int resolution, out HeatmapMapInfo info)
        {
            info = Frame(area, resolution);
            info.scene = SceneManager.GetActiveScene().name;

            var go = new GameObject("Heatmap Map Camera") { hideFlags = HideFlags.HideAndDontSave };
            go.transform.SetPositionAndRotation(
                new Vector3((info.minX + info.maxX) * 0.5f, hideAbove, (info.minZ + info.maxZ) * 0.5f),
                Quaternion.Euler(90f, 0f, 0f));

            var camera = go.AddComponent<Camera>();
            camera.enabled = false;
            camera.orthographic = true;
            camera.orthographicSize = (info.maxZ - info.minZ) * 0.5f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Background;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = Mathf.Max(1f, hideAbove - area.min.y) + DepthBelow;

            var target = new RenderTexture(info.width, info.height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
            var texture = new Texture2D(info.width, info.height, TextureFormat.RGB24, false);

            RenderTexture previousTarget = RenderTexture.active;
            bool fog = RenderSettings.fog;
            float lodBias = QualitySettings.lodBias;
            try
            {
                // Fog is measured from the camera, and an orthographic camera over a big level drops props to
                // their lowest LOD or culls them, so draw this one frame without either.
                RenderSettings.fog = false;
                QualitySettings.lodBias = lodBias * Mathf.Max(1f, camera.orthographicSize);

                camera.targetTexture = target;
                camera.aspect = (float)info.width / info.height;
                camera.Render();

                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, info.width, info.height), 0, 0);
                texture.Apply();
            }
            catch
            {
                DestroySafe(texture);
                throw;
            }
            finally
            {
                RenderSettings.fog = fog;
                QualitySettings.lodBias = lodBias;
                RenderTexture.active = previousTarget;
                camera.targetTexture = null;
                DestroySafe(target);
                DestroySafe(go);
            }
            return texture;
        }

        static HeatmapMapInfo Frame(Bounds area, int resolution)
        {
            resolution = Mathf.Clamp(resolution, 16, MaxResolution);
            float minX = Mathf.Floor(area.min.x), minZ = Mathf.Floor(area.min.z);
            float sizeX = Mathf.Max(1f, Mathf.Ceil(area.max.x) - minX);
            float sizeZ = Mathf.Max(1f, Mathf.Ceil(area.max.z) - minZ);

            float pixelsPerMetre = resolution / Mathf.Max(sizeX, sizeZ);
            int width = Mathf.Clamp(Mathf.RoundToInt(sizeX * pixelsPerMetre), 1, MaxResolution);
            int height = Mathf.Clamp(Mathf.RoundToInt(sizeZ * pixelsPerMetre), 1, MaxResolution);

            return new HeatmapMapInfo
            {
                width = width,
                height = height,
                pixelsPerMetre = pixelsPerMetre,
                minX = minX,
                maxX = minX + width / pixelsPerMetre,
                minZ = minZ,
                maxZ = minZ + height / pixelsPerMetre,
            };
        }

        static void DestroySafe(Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) Object.Destroy(obj);
            else Object.DestroyImmediate(obj);
        }
    }
}
