using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
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
        public string heightImage;
        public float minY;
        public float maxY;
    }

    public static class HeatmapMapCapture
    {
        public const string ImageSuffix = "_map";
        public const string BoundsSuffix = "_bounds";
        public const string HeightSuffix = "_height";
        public const int MaxResolution = 8192;

        const float DepthBelow = 1000f;
        const int MaxTerrainGrid = 1025;
        const string HeightShaderName = "Hidden/HeatmapTool/Height";
        static readonly Color Background = new Color(0.12f, 0.12f, 0.12f, 1f);
        const double HeightSteps = 16777214.0;

        public static string BoundsPathFor(string imagePath)
        {
            return Path.Combine(Path.GetDirectoryName(imagePath) ?? "",
                Path.GetFileNameWithoutExtension(imagePath) + BoundsSuffix + HeatmapIO.CsvExtension);
        }

        public static string HeightPathFor(string imagePath)
        {
            return Path.Combine(Path.GetDirectoryName(imagePath) ?? "",
                Path.GetFileNameWithoutExtension(imagePath) + HeightSuffix + ".png");
        }

        public static HeatmapMapInfo Save(string imagePath, Bounds area, float hideAbove, int resolution, bool saveHeightmap = false)
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

            if (saveHeightmap)
            {
                float[] heights = RenderHeights(area, hideAbove, info);
                ushort[] pixels = HeightsToGray16(heights, info.width, info.height, out info.minY, out info.maxY);
                string heightPath = HeightPathFor(imagePath);
                File.WriteAllBytes(heightPath, HeatmapIO.EncodeGray16Png(pixels, info.width, info.height));
                info.heightImage = Path.GetFileName(heightPath);
            }

            HeatmapIO.SaveMapBoundsCsv(info, BoundsPathFor(imagePath));
            return info;
        }

        // Looks straight down with +Z at the top of the image and +X to the right. Geometry above 'hideAbove'
        // (a world height) is clipped so roofs and ceilings don't cover the floor.
        public static Texture2D Render(Bounds area, float hideAbove, int resolution, out HeatmapMapInfo info)
        {
            info = Frame(area, resolution);
            info.scene = SceneManager.GetActiveScene().name;

            Camera camera = CreateCamera(info, area, hideAbove);
            GameObject go = camera.gameObject;

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

        public static float[] RenderHeights(Bounds area, float hideAbove, HeatmapMapInfo info)
        {
            Shader shader = Shader.Find(HeightShaderName);
            if (shader == null)
                throw new InvalidOperationException($"{HeightShaderName} is missing. Keep HeatmapHeight.shader in the tool's Resources folder.");

            Camera camera = CreateCamera(info, area, hideAbove);
            float heightMin = hideAbove - camera.farClipPlane;
            float heightRange = camera.farClipPlane;

            var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            material.SetFloat("_HideAbove", hideAbove);
            material.SetFloat("_HeightMin", heightMin);
            material.SetFloat("_HeightRange", heightRange);

            var target = new RenderTexture(info.width, info.height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
            var texture = new Texture2D(info.width, info.height, TextureFormat.RGBA32, false, true);
            var terrainMeshes = new List<Mesh>();
            var commands = new CommandBuffer { name = "Heatmap Heightmap" };

            RenderTexture previousTarget = RenderTexture.active;
            try
            {
                var rect = new Rect(info.minX, info.minZ, info.maxX - info.minX, info.maxZ - info.minZ);
                commands.SetRenderTarget(target);
                commands.ClearRenderTarget(true, true, Color.clear);
                commands.SetViewProjectionMatrices(camera.worldToCameraMatrix, GL.GetGPUProjectionMatrix(camera.projectionMatrix, true));
                DrawRenderers(commands, material, rect, hideAbove);
                DrawTerrains(commands, material, rect, terrainMeshes);
                Graphics.ExecuteCommandBuffer(commands);

                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, info.width, info.height), 0, 0);
                texture.Apply();

                var packed = texture.GetRawTextureData<Color32>();
                var heights = new float[packed.Length];
                for (int i = 0; i < heights.Length; i++) heights[i] = DecodeHeight(packed[i], heightMin, heightRange);
                return heights;
            }
            finally
            {
                RenderTexture.active = previousTarget;
                commands.Release();
                foreach (Mesh mesh in terrainMeshes) DestroySafe(mesh);
                DestroySafe(texture);
                DestroySafe(target);
                DestroySafe(material);
                DestroySafe(camera.gameObject);
            }
        }

        public static float DecodeHeight(Color32 packed, float heightMin, float heightRange)
        {
            int v = (packed.r << 16) | (packed.g << 8) | packed.b;
            if (v == 0) return float.NaN;
            return (float)(heightMin + (v - 1) / HeightSteps * heightRange);
        }

        public static ushort[] HeightsToGray16(float[] heights, int width, int height, out float minY, out float maxY)
        {
            minY = float.PositiveInfinity;
            maxY = float.NegativeInfinity;
            foreach (float h in heights)
            {
                if (float.IsNaN(h)) continue;
                if (h < minY) minY = h;
                if (h > maxY) maxY = h;
            }
            if (minY > maxY) minY = maxY = 0f;

            float range = maxY - minY;
            var pixels = new ushort[width * height];
            for (int y = 0; y < height; y++)
            {
                int source = y * width;
                int destination = (height - 1 - y) * width;
                for (int x = 0; x < width; x++)
                {
                    float h = heights[source + x];
                    if (float.IsNaN(h) || range <= 0f) continue;
                    pixels[destination + x] = (ushort)Mathf.Clamp(Mathf.RoundToInt((h - minY) / range * 65535f), 0, 65535);
                }
            }
            return pixels;
        }

        static Camera CreateCamera(HeatmapMapInfo info, Bounds area, float hideAbove)
        {
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
            camera.aspect = (float)info.width / info.height;
            return camera;
        }

        static void DrawRenderers(CommandBuffer commands, Material material, Rect area, float hideAbove)
        {
            foreach (Renderer renderer in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!renderer.enabled || renderer.forceRenderingOff || renderer.shadowCastingMode == ShadowCastingMode.ShadowsOnly) continue;
                if ((renderer.gameObject.hideFlags & HideFlags.HideInHierarchy) != 0) continue;

                Mesh mesh = null;
                if (renderer is SkinnedMeshRenderer skinned) mesh = skinned.sharedMesh;
                else if (renderer is MeshRenderer && renderer.TryGetComponent(out MeshFilter filter)) mesh = filter.sharedMesh;
                if (mesh == null) continue;

                Bounds bounds = renderer.bounds;
                if (bounds.min.y > hideAbove || bounds.max.x < area.xMin || bounds.min.x > area.xMax
                    || bounds.max.z < area.yMin || bounds.min.z > area.yMax)
                    continue;

                for (int i = 0; i < mesh.subMeshCount; i++) commands.DrawRenderer(renderer, material, i, 0);
            }
        }

        static void DrawTerrains(CommandBuffer commands, Material material, Rect area, List<Mesh> created)
        {
            foreach (Terrain terrain in Terrain.activeTerrains)
            {
                if (terrain == null || !terrain.enabled || !terrain.drawHeightmap || terrain.terrainData == null) continue;

                Vector3 origin = terrain.GetPosition();
                Vector3 size = terrain.terrainData.size;
                if (origin.x > area.xMax || origin.x + size.x < area.xMin || origin.z > area.yMax || origin.z + size.z < area.yMin)
                    continue;

                Mesh mesh = BuildTerrainMesh(terrain.terrainData);
                created.Add(mesh);
                commands.DrawMesh(mesh, Matrix4x4.Translate(origin), material, 0, 0);
            }
        }

        static Mesh BuildTerrainMesh(TerrainData data)
        {
            int resolution = data.heightmapResolution;
            int step = Mathf.Max(1, Mathf.CeilToInt((resolution - 1) / (float)(MaxTerrainGrid - 1)));
            int grid = (resolution - 1) / step + 1;
            float[,] samples = data.GetHeights(0, 0, resolution, resolution);
            Vector3 size = data.size;
            float last = resolution - 1;

            var vertices = new Vector3[grid * grid];
            for (int z = 0; z < grid; z++)
            for (int x = 0; x < grid; x++)
            {
                int sz = Mathf.Min(z * step, resolution - 1);
                int sx = Mathf.Min(x * step, resolution - 1);
                vertices[z * grid + x] = new Vector3(sx / last * size.x, samples[sz, sx] * size.y, sz / last * size.z);
            }

            var indices = new int[(grid - 1) * (grid - 1) * 6];
            int n = 0;
            for (int z = 0; z < grid - 1; z++)
            for (int x = 0; x < grid - 1; x++)
            {
                int a = z * grid + x;
                indices[n++] = a;
                indices[n++] = a + grid;
                indices[n++] = a + 1;
                indices[n++] = a + 1;
                indices[n++] = a + grid;
                indices[n++] = a + grid + 1;
            }

            var mesh = new Mesh { name = "Heatmap Terrain Height", hideFlags = HideFlags.HideAndDontSave, indexFormat = IndexFormat.UInt32 };
            mesh.vertices = vertices;
            mesh.triangles = indices;
            return mesh;
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
