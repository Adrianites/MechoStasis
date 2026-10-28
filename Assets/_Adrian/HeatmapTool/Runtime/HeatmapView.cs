using System;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace HeatmapTool
{
    public sealed class HeatmapView : IDisposable
    {
        const string ShaderName = "Hidden/HeatmapTool/VertexColor";
        static readonly int OpacityId = Shader.PropertyToID("_Opacity");
        static readonly int ZTestId = Shader.PropertyToID("_ZTest");

        sealed class Layer
        {
            public Mesh mesh;
            public MeshRenderer renderer;
        }

        GameObject root;
        Layer heat, markers, paths;
        Material heatMaterial, overlayMaterial;
        float opacity = 0.8f;
        bool seeThrough;

        public Mesh HeatMesh { get { EnsureCreated(); return heat.mesh; } }
        public Mesh MarkerMesh { get { EnsureCreated(); return markers.mesh; } }
        public Mesh PathMesh { get { EnsureCreated(); return paths.mesh; } }

        public void SetLayersVisible(bool showHeat, bool showMarkers, bool showPaths)
        {
            EnsureCreated();
            heat.renderer.enabled = showHeat;
            markers.renderer.enabled = showMarkers;
            paths.renderer.enabled = showPaths;
        }

        public void SetOpacity(float value)
        {
            opacity = Mathf.Clamp01(value);
            if (heatMaterial != null) heatMaterial.SetFloat(OpacityId, opacity);
        }

        public void SetSeeThrough(bool value)
        {
            seeThrough = value;
            float zTest = (float)(value ? CompareFunction.Always : CompareFunction.LessEqual);
            if (heatMaterial != null) heatMaterial.SetFloat(ZTestId, zTest);
            if (overlayMaterial != null) overlayMaterial.SetFloat(ZTestId, zTest);
        }

        public void Dispose()
        {
            if (root != null) DestroyHierarchy(root);
            DestroySafe(heatMaterial);
            DestroySafe(overlayMaterial);
            root = null;
            heat = markers = paths = null;
            heatMaterial = overlayMaterial = null;
        }

        void EnsureCreated()
        {
            if (root != null) return;
            Dispose();
            DestroyOrphans();

            Shader shader = Shader.Find(ShaderName);
            if (shader == null) shader = Shader.Find("Sprites/Default");

            heatMaterial = new Material(shader) { name = "Heatmap", hideFlags = HideFlags.HideAndDontSave };
            overlayMaterial = new Material(shader) { name = "Heatmap Overlay", hideFlags = HideFlags.HideAndDontSave };

            root = new GameObject("Heatmap View") { hideFlags = HideFlags.HideAndDontSave };
            root.AddComponent<HeatmapViewHandle>().owner = this;
            heat = CreateLayer("Heat", heatMaterial);
            markers = CreateLayer("Markers", overlayMaterial);
            paths = CreateLayer("Paths", overlayMaterial);

            SetOpacity(opacity);
            SetSeeThrough(seeThrough);
        }

        Layer CreateLayer(string name, Material material)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
            go.transform.SetParent(root.transform, false);

            var mesh = new Mesh { name = "Heatmap " + name, hideFlags = HideFlags.HideAndDontSave };
            mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;

            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            renderer.allowOcclusionWhenDynamic = false;
            return new Layer { mesh = mesh, renderer = renderer };
        }

        static void DestroyOrphans()
        {
            foreach (HeatmapViewHandle handle in Resources.FindObjectsOfTypeAll<HeatmapViewHandle>())
            {
                if (handle != null && handle.owner == null) DestroyHierarchy(handle.gameObject);
            }
        }

        static void DestroyHierarchy(GameObject go)
        {
            foreach (MeshFilter filter in go.GetComponentsInChildren<MeshFilter>(true))
                DestroySafe(filter.sharedMesh);
            DestroySafe(go);
        }

        static void DestroySafe(Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) Object.Destroy(obj);
            else Object.DestroyImmediate(obj);
        }
    }
}
