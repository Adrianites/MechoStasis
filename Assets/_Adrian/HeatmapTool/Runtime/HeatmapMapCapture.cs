using System;
using System.Collections.Generic;
using System.IO;
using Unity.Collections;
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
        public float heightFit;
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
        const float HeightCameraRise = 1000f;
        const float MarkerValue = 1e20f;

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
        
        public static HeatmapMapInfo Save(string imagePath, Bounds area, float hideAbove, int resolution, bool saveHeightmap = false,
            IReadOnlyList<Vector3> standing = null)
        {
            Texture2D texture = Render(area, hideAbove, resolution, out HeatmapMapInfo info);
            float[] heights = null;
            try
            {
                if (saveHeightmap || CanRenderHeights)
                {
                    heights = RenderHeights(area, hideAbove, info, out bool[] cutSolid);
                    PaintCutSolids(texture, area, hideAbove, resolution, heights, cutSolid);
                }
                HeatmapIO.EnsureFolderFor(imagePath);
                File.WriteAllBytes(imagePath, texture.EncodeToPNG());
            }
            finally
            {
                DestroySafe(texture);
            }
            info.image = Path.GetFileName(imagePath);
            info.heightFit = -1f;

            if (saveHeightmap)
            {
                FillHoles(heights, info.width, info.height);
                if (standing != null) info.heightFit = FitToPlayers(heights, info, standing);
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

        // Hide Above clips everything over the cut, so in the map picture a mountain or tall rock it passes through
        // shows its hollow inside, or nothing. Those pixels are taken from a second picture drawn from just above the
        // highest of them instead, so the picture matches the heightmap.
        static void PaintCutSolids(Texture2D texture, Bounds area, float hideAbove, int resolution, float[] heights, bool[] cutSolid)
        {
            float top = float.NegativeInfinity;
            for (int i = 0; i < cutSolid.Length; i++)
                if (cutSolid[i]) top = Mathf.Max(top, heights[i]);
            if (float.IsNegativeInfinity(top)) return;

            Texture2D above = Render(area, Mathf.Max(top, hideAbove) + 1f, resolution, out _);
            try
            {
                Color32[] pixels = texture.GetPixels32();
                Color32[] abovePixels = above.GetPixels32();
                for (int i = 0; i < pixels.Length; i++)
                    if (cutSolid[i]) pixels[i] = abovePixels[i];
                texture.SetPixels32(pixels);
                texture.Apply();
            }
            finally
            {
                DestroySafe(above);
            }
        }

        public static bool CanRenderHeights => SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBFloat)
                                               && SystemInfo.SupportsBlendingOnRenderTextureFormat(RenderTextureFormat.ARGBFloat);

        // Each pixel gets the highest surface below 'hideAbove'. Where the cut passes through something solid (a tall
        // rock, a mountain, a wall) the pixel gets that solid's real top instead of whatever is inside it.
        //
        // This deliberately avoids the depth buffer: depth and flip conventions differ between graphics APIs, and
        // earlier versions kept the lowest surface instead of the highest and came back upside down. Instead,
        // blending keeps the highest surface (Max), surfaces above the cut are counted (Add), and a marker drawn in the
        // map's top-left corner shows which way up the result came back. 'cutSolid' marks the pixels where the cut
        // passes through something solid. Both are in Unity's texture order (row 0 at min_z).
        public static float[] RenderHeights(Bounds area, float hideAbove, HeatmapMapInfo info, out bool[] cutSolid)
        {
            Shader shader = Shader.Find(HeightShaderName);
            if (shader == null)
                throw new InvalidOperationException($"{HeightShaderName} is missing. Keep HeatmapHeight.shader in the tool's Resources folder.");
            if (!CanRenderHeights)
                throw new InvalidOperationException("This graphics card can't blend floating-point render targets, which the heightmap needs.");

            // the height camera sits well above the cut, so it also sees the surfaces the cut removes
            Camera camera = CreateCamera(info, area, hideAbove);
            camera.transform.position += Vector3.up * HeightCameraRise;
            camera.farClipPlane += HeightCameraRise;
            float heightBase = camera.transform.position.y - camera.farClipPlane - 1f;

            var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            material.SetFloat("_HideAbove", hideAbove);
            material.SetFloat("_HeightBase", heightBase);
            var markerMaterial = new Material(material) { hideFlags = HideFlags.HideAndDontSave };
            markerMaterial.SetFloat("_Marker", 1f);
            Mesh marker = BuildCornerMarker(info, hideAbove - 1f);

            var target = new RenderTexture(info.width, info.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
            var texture = new Texture2D(info.width, info.height, TextureFormat.RGBAFloat, false, true);
            var terrainMeshes = new List<Mesh>();
            var commands = new CommandBuffer { name = "Heatmap Heightmap" };

            RenderTexture previousTarget = RenderTexture.active;
            try
            {
                var rect = new Rect(info.minX, info.minZ, info.maxX - info.minX, info.maxZ - info.minZ);
                commands.SetRenderTarget(target);
                commands.ClearRenderTarget(false, true, Color.clear);
                // Unity converts this projection for the graphics API itself; converting it here as well flips the
                // render and reverses which surface the depth test keeps
                commands.SetViewProjectionMatrices(camera.worldToCameraMatrix, camera.projectionMatrix);
                DrawRenderers(commands, material, rect, hideAbove);
                DrawTerrains(commands, material, rect, terrainMeshes);
                commands.DrawMesh(marker, Matrix4x4.identity, markerMaterial, 0, 0);
                Graphics.ExecuteCommandBuffer(commands);

                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, info.width, info.height), 0, 0);
                texture.Apply();
                cutSolid = new bool[info.width * info.height];
                return DecodeHeights(texture.GetRawTextureData<Color>(), info.width, info.height, hideAbove, heightBase, cutSolid);
            }
            finally
            {
                RenderTexture.active = previousTarget;
                commands.Release();
                foreach (Mesh mesh in terrainMeshes) DestroySafe(mesh);
                DestroySafe(marker);
                DestroySafe(texture);
                DestroySafe(target);
                DestroySafe(markerMaterial);
                DestroySafe(material);
                DestroySafe(camera.gameObject);
            }
        }

        // Turns the render into heights in Unity's texture order (row 0 at min_z), undoing any flip the platform made.
        static float[] DecodeHeights(NativeArray<Color> data, int width, int height, float hideAbove, float heightBase, bool[] cutSolid)
        {
            bool topLeft = MarkerNear(data, width, height, right: false, top: true);
            bool bottomLeft = MarkerNear(data, width, height, right: false, top: false);
            bool topRight = MarkerNear(data, width, height, right: true, top: true);
            bool bottomRight = MarkerNear(data, width, height, right: true, top: false);
            bool flipV = !topLeft && (bottomLeft || bottomRight);
            bool flipH = !topLeft && (topRight || bottomRight);
            if (!(topLeft || bottomLeft || topRight || bottomRight))
                Debug.LogWarning("[Heatmap] The heightmap's corner marker is missing, so it may be flipped compared with the map.");

            var heights = new float[width * height];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                Color c = data[(flipV ? height - 1 - y : y) * width + (flipH ? width - 1 - x : x)];
                float h;
                if (c.r > MarkerValue * 0.5f) h = float.NaN;   // the marker itself, filled in from its neighbours
                else if (c.a > 0.5f)                           // the cut passes through something solid: its top
                {
                    h = hideAbove + c.g;
                    cutSolid[y * width + x] = true;
                }
                else if (c.r > 0f) h = heightBase + c.r;       // the highest surface below the cut
                else h = float.NaN;                            // nothing under this pixel
                heights[y * width + x] = h;
            }
            return heights;
        }

        static bool MarkerNear(NativeArray<Color> data, int width, int height, bool right, bool top)
        {
            for (int dy = 0; dy < 4; dy++)
            for (int dx = 0; dx < 4; dx++)
            {
                int x = right ? width - 1 - dx : dx, y = top ? height - 1 - dy : dy;
                if (x >= 0 && x < width && y >= 0 && y < height && data[y * width + x].r > MarkerValue * 0.5f) return true;
            }
            return false;
        }

        // Share of standing positions in the tightest 0.6 m cluster of (player height - surface height): a standing
        // player is the same distance above the surface under them everywhere when the heightmap is right.
        // Returns -1 when there are too few positions to tell.
        public static float FitToPlayers(float[] heights, HeatmapMapInfo info, IReadOnlyList<Vector3> standing)
        {
            var gaps = new List<float>();
            int step = Mathf.Max(1, standing.Count / 6000);
            for (int i = 0; i < standing.Count; i += step)
            {
                Vector3 p = standing[i];
                int x = Mathf.FloorToInt((p.x - info.minX) * info.pixelsPerMetre);
                int y = Mathf.FloorToInt((p.z - info.minZ) * info.pixelsPerMetre); // heights run from min_z upwards
                if (x < 0 || x >= info.width || y < 0 || y >= info.height) continue;
                float h = heights[y * info.width + x];
                if (!float.IsNaN(h)) gaps.Add(p.y - h);
            }
            if (gaps.Count < 20) return -1f;

            gaps.Sort();
            int most = 0;
            for (int i = 0, j = 0; i < gaps.Count; i++)
            {
                while (j < gaps.Count && gaps[j] - gaps[i] <= 0.6f) j++;
                most = Mathf.Max(most, j - i);
            }
            return (float)most / gaps.Count;
        }

        // A pixel-and-a-half square in the map's top-left (min_x, max_z) corner.
        static Mesh BuildCornerMarker(HeatmapMapInfo info, float y)
        {
            float size = 1.5f / info.pixelsPerMetre;
            var mesh = new Mesh { name = "Heatmap Corner Marker", hideFlags = HideFlags.HideAndDontSave };
            mesh.vertices = new[]
            {
                new Vector3(info.minX, y, info.maxZ - size), new Vector3(info.minX, y, info.maxZ),
                new Vector3(info.minX + size, y, info.maxZ), new Vector3(info.minX + size, y, info.maxZ - size),
            };
            mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            return mesh;
        }

        // Pixels with nothing under them (outside the level, gaps between meshes) take the height of the nearest
        // surface, so a heightmap used as terrain or displacement has no wells dropping to min_y.
        public static void FillHoles(float[] heights, int width, int height)
        {
            var queue = new int[heights.Length];
            int head = 0, tail = 0;
            for (int i = 0; i < heights.Length; i++)
                if (!float.IsNaN(heights[i])) queue[tail++] = i;
            if (tail == 0 || tail == heights.Length) return;

            while (head < tail)
            {
                int i = queue[head++], x = i % width, y = i / width;
                float h = heights[i];
                if (x > 0 && float.IsNaN(heights[i - 1])) { heights[i - 1] = h; queue[tail++] = i - 1; }
                if (x < width - 1 && float.IsNaN(heights[i + 1])) { heights[i + 1] = h; queue[tail++] = i + 1; }
                if (y > 0 && float.IsNaN(heights[i - width])) { heights[i - width] = h; queue[tail++] = i - width; }
                if (y < height - 1 && float.IsNaN(heights[i + width])) { heights[i + width] = h; queue[tail++] = i + width; }
            }
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
            HashSet<Renderer> lowerLods = LowerLodRenderers();
            foreach (Renderer renderer in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!renderer.enabled || renderer.forceRenderingOff || renderer.shadowCastingMode == ShadowCastingMode.ShadowsOnly) continue;
                if ((renderer.gameObject.hideFlags & HideFlags.HideInHierarchy) != 0) continue;
                if (lowerLods.Contains(renderer)) continue;

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

        // Renderers that only appear in a LOD group's coarser levels. Only the most detailed level is drawn, so
        // rougher copies of the same object don't poke through it.
        static HashSet<Renderer> LowerLodRenderers()
        {
            var lower = new HashSet<Renderer>();
            foreach (LODGroup group in Object.FindObjectsByType<LODGroup>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                LOD[] lods = group.GetLODs();
                for (int i = 1; i < lods.Length; i++)
                    foreach (Renderer renderer in lods[i].renderers)
                        if (renderer != null) lower.Add(renderer);
                if (lods.Length > 0)
                    foreach (Renderer renderer in lods[0].renderers)
                        if (renderer != null) lower.Remove(renderer);
            }
            return lower;
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
            mesh.RecalculateNormals(); // the height shader tells tops from undersides by their normals
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
