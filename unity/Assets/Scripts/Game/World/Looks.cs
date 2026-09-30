// Looks.cs - the presentation's shaders and shared materials in one place,
// so a move to another render pipeline changes one file.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace OpenKingdomsUnity.Game.World
{
    public static class Looks
    {
        static readonly Dictionary<string, Shader> shaders = new Dictionary<string, Shader>();

        // The project draws with URP when a pipeline asset is set, and the
        // shaders carry a subshader for it and one for the built-in pipeline.
        public static UniversalRenderPipelineAsset Urp => GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;

        // How far the sun's shadows reach, in whichever pipeline runs.
        public static void ShadowDistance(float d)
        {
            var urp = Urp;
            if (urp != null) urp.shadowDistance = d;
            else QualitySettings.shadowDistance = d;
        }

        public static Shader Find(string name, string fallback)
        {
            if (shaders.TryGetValue(name, out var s) && s != null) return s;
            var own = Resources.Load<Shader>("Shaders/" + name);
            s = own;
            // A built player carries no fallback it never uses, and a batch
            // run without graphics supports no shader, so ours stays then.
            if (s == null || !s.isSupported) s = Shader.Find(fallback);
            if (s == null) s = own;
            shaders[name] = s;
            return s;
        }

        public static Material Model(Texture tex)
        {
            var m = new Material(Find("OkuModel", "Standard")) { enableInstancing = true, hideFlags = HideFlags.DontSave };
            if (tex != null) m.mainTexture = tex;
            return m;
        }

        public static Material Terrain(Texture tex, float seaLevel)
        {
            var m = new Material(Find("OkuTerrain", "Standard")) { hideFlags = HideFlags.DontSave };
            m.mainTexture = tex;
            m.SetFloat("_SeaLevel", seaLevel);
            return m;
        }

        public static Material Overlay(Color c)
        {
            var m = new Material(Find("OkuOverlay", "Sprites/Default")) { enableInstancing = true, hideFlags = HideFlags.DontSave };
            m.color = c;
            return m;
        }

        public static Material Water() =>
            new Material(Find("OkuWater", "Standard")) { hideFlags = HideFlags.DontSave };

        public static void Release(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o);
        }
    }
}
