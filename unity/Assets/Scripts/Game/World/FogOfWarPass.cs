// FogOfWarPass.cs - the fog of war under URP, laid over the finished
// picture after tonemapping and grading, as the original darkens its
// finished frame: black where never seen and a little over half bright
// where seen before. Each pixel's ground point comes from the depth
// buffer, so land, sea, features and effects all take it alike.
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace OpenKingdomsUnity.Game.World
{
    public sealed class FogOfWarPass : ScriptableRenderPass
    {
        static readonly int DepthId = Shader.PropertyToID("_OkuDepth");
        static readonly int InvViewProjId = Shader.PropertyToID("_OkuInvViewProj");
        static readonly int BlitTextureId = Shader.PropertyToID("_BlitTexture");
        static readonly int BlitScaleBiasId = Shader.PropertyToID("_BlitScaleBias");
        static readonly MaterialPropertyBlock block = new MaterialPropertyBlock();

        readonly Material material;
        Camera target;

        public FogOfWarPass(Material material)
        {
            this.material = material;
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
            requiresIntermediateTexture = true;
            ConfigureInput(ScriptableRenderPassInput.Depth);
            profilingSampler = new ProfilingSampler("Oku fog of war");
        }

        public bool Attached => target != null;

        // Draws over one camera's pictures until Detach.
        public void Attach(Camera cam)
        {
            if (cam == null) return;
            if (target == null) RenderPipelineManager.beginCameraRendering += OnBegin;
            target = cam;
        }

        public void Detach()
        {
            if (target != null) RenderPipelineManager.beginCameraRendering -= OnBegin;
            target = null;
        }

        void OnBegin(ScriptableRenderContext context, Camera cam)
        {
            if (cam != target || material == null) return;
            cam.GetUniversalAdditionalCameraData().scriptableRenderer?.EnqueuePass(this);
        }

        class PassData
        {
            public Material material;
            public TextureHandle source, depth;
            public Matrix4x4 invViewProj;
        }

        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
            var res = frameData.Get<UniversalResourceData>();
            var cam = frameData.Get<UniversalCameraData>();
            if (res.isActiveTargetBackBuffer || !res.cameraDepthTexture.IsValid()) return;
            var source = res.activeColorTexture;
            var desc = graph.GetTextureDesc(source);
            desc.name = "_OkuFogOfWar";
            desc.clearBuffer = false;
            var dest = graph.CreateTexture(desc);
            using (var builder = graph.AddRasterRenderPass<PassData>("Oku fog of war", out var data, profilingSampler))
            {
                data.material = material;
                data.source = source;
                data.depth = res.cameraDepthTexture;
                data.invViewProj = (cam.GetProjectionMatrix() * cam.GetViewMatrix()).inverse;
                builder.UseTexture(source);
                builder.UseTexture(data.depth);
                builder.SetRenderAttachment(dest, 0);
                builder.SetRenderFunc(static (PassData d, RasterGraphContext ctx) =>
                {
                    block.Clear();
                    block.SetTexture(BlitTextureId, d.source);
                    block.SetTexture(DepthId, d.depth);
                    block.SetVector(BlitScaleBiasId, new Vector4(1, 1, 0, 0));
                    block.SetMatrix(InvViewProjId, d.invViewProj);
                    ctx.cmd.DrawProcedural(Matrix4x4.identity, d.material, 0, MeshTopology.Triangles, 3, 1, block);
                });
            }
            res.cameraColor = dest;
        }
    }
}
