// BlendedPlayTests.cs - a model material set to blend, as a glTF BLEND
// material is, shows what is behind it in proportion to its alpha.
using System.Collections;
using NUnit.Framework;
using OpenKingdomsUnity.Game.World;
using UnityEngine;
using UnityEngine.TestTools;

namespace OpenKingdomsUnity.Tests
{
    public class BlendedPlayTests
    {
        static Color Centre(Camera cam, RenderTexture rt)
        {
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;
            var c = tex.GetPixel(rt.width / 2, rt.height / 2);
            Object.Destroy(tex);
            return c;
        }

        [UnityTest]
        public IEnumerator AHalfClearGlowShowsWhatIsBehindIt()
        {
            yield return null;
            var cam = new GameObject("blend camera").AddComponent<Camera>();
            cam.enabled = false;
            cam.orthographic = true;
            cam.orthographicSize = 1f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.transform.position = new Vector3(0, 0, -10);
            var back = GameObject.CreatePrimitive(PrimitiveType.Quad);
            back.transform.position = new Vector3(0, 0, 1);
            back.transform.localScale = Vector3.one * 4;
            var backMat = Looks.Model(null);
            backMat.SetFloat("_Cull", 0f);
            backMat.color = Color.red;
            backMat.SetVector("_EmissionColor", new Vector4(1, 0, 0, 1));
            backMat.EnableKeyword("_EMISSION");
            back.GetComponent<MeshRenderer>().sharedMaterial = backMat;
            var front = GameObject.CreatePrimitive(PrimitiveType.Quad);
            var frontMat = Looks.Model(null);
            frontMat.SetFloat("_Cull", 0f);
            frontMat.SetVector("_EmissionColor", new Vector4(0, 0, 1, 1));
            frontMat.EnableKeyword("_EMISSION");
            GlbLoader.Blended(frontMat);
            front.GetComponent<MeshRenderer>().sharedMaterial = frontMat;
            var rt = RenderTexture.GetTemporary(64, 64, 24);
            try
            {
                // Shaders may still be compiling their variants at first.
                float until = Time.realtimeSinceStartup + 20f;
                front.SetActive(false);
                while (Centre(cam, rt).r < 0.5f && Time.realtimeSinceStartup < until) yield return null;
                front.SetActive(true);
                frontMat.color = new Color(0, 0, 0, 1f);
                while (Centre(cam, rt).b < 0.5f && Time.realtimeSinceStartup < until) yield return null;
                frontMat.color = new Color(0, 0, 0, 1f);
                var solid = Centre(cam, rt);
                frontMat.color = new Color(0, 0, 0, 0.5f);
                var half = Centre(cam, rt);
                front.SetActive(false);
                var behind = Centre(cam, rt);
                Debug.Log($"Blended: behind {behind}, solid {solid}, half {half}");
                Assert.Greater(behind.r, 0.5f, "the red behind shows alone");
                Assert.Less(solid.r, 0.1f, "a solid front hides it");
                Assert.Greater(solid.b, 0.5f, "and shows its own glow");
                Assert.That(half.r, Is.InRange(behind.r * 0.3f, behind.r * 0.8f), "half clear shows part of it");
                Assert.Greater(half.b, 0.2f, "over it the front's own glow");
            }
            finally
            {
                RenderTexture.ReleaseTemporary(rt);
                Object.Destroy(cam.gameObject);
                Object.Destroy(back);
                Object.Destroy(front);
            }
        }
    }
}
