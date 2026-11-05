using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace LucyMouldMod
{
    internal static class LucyStereo
    {
        const string Keyword = "LUCY_STEREO";
        static readonly HashSet<Material> materials = new HashSet<Material>();
        static readonly Stack<bool> cameras = new Stack<bool>();
        static bool installed;
        static bool active;

        internal static void Configure(GameObject graphics)
        {
            foreach (var renderer in graphics.GetComponentsInChildren<Renderer>(true))
                foreach (var material in renderer.sharedMaterials) Register(material);
            if (installed) return;
            Camera.onPreCull += Before;
            Camera.onPostRender += After;
            installed = true;
        }

        internal static void Register(Material material)
        {
            if (material == null || material.shader == null || material.shader.name != "Lucy/Lit") return;
            material.enableInstancing = true;
            if (active) material.EnableKeyword(Keyword);
            else material.DisableKeyword(Keyword);
            materials.Add(material);
        }

        static void Before(Camera camera)
        {
            cameras.Push(active);
            Set(camera != null && camera.stereoEnabled
                && XRSettings.stereoRenderingMode == XRSettings.StereoRenderingMode.SinglePassInstanced);
        }

        static void After(Camera camera) { Set(cameras.Count == 0 ? false : cameras.Pop()); }

        static void Set(bool value)
        {
            if (value == active) return;
            active = value;
            foreach (var material in materials)
            {
                if (material == null) continue;
                if (value) material.EnableKeyword(Keyword);
                else material.DisableKeyword(Keyword);
            }
        }

        internal static void Dispose()
        {
            if (installed)
            {
                Camera.onPreCull -= Before;
                Camera.onPostRender -= After;
                installed = false;
            }
            Set(false);
            materials.Clear();
            cameras.Clear();
        }
    }
}
