// CrowdCpuPlayTests.cs - a bisecting aid, copied in by bisect.sh: the
// process CPU time a frame takes with 500 mock units, which other work on
// the machine disturbs less than the wall clock.
using System.Collections;
using System.Diagnostics;
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using UnityEngine;
using UnityEngine.TestTools;
using Debug = UnityEngine.Debug;

namespace OpenKingdomsUnity.Tests
{
    public class CrowdCpuPlayTests
    {
        [UnityTest]
        public IEnumerator CpuPerFrame()
        {
            var mock = new MockBackend { StageSeconds = 0f, DamageScale = 0f, ExtraSoldiers = 250 };
            var root = GameRoot.Boot(mock);
            yield return null;
            root.Flow.Fire(FlowEvent.OpenSkirmish);
            root.Setup.MapId = "mock_highlands";
            root.Screens.StartGame();
            while (root.Flow.State != FlowState.Playing) yield return null;
            root.World.Camera.Zoom(110f);
            for (int i = 0; i < 30; i++) yield return null;
            string[] names = { "Oku.Shadows", "Oku.Fog", "Oku.Entities", "Oku.Effects", "Oku.Sim", "Oku.Input", "Oku.Render", "Oku.Screens", "Oku.Pointer", "BehaviourUpdate", "LateBehaviourUpdate", "PostLateUpdate.FinishFrameRendering", "Camera.Render",
                "Shadows.RenderShadowmap", "Canvas.SendWillRenderCanvases", "UGUI.Rendering.UpdateBatches", "GUI.Repaint", "PreLateUpdate.ScriptRunBehaviourLateUpdate",
                "Update.ScriptRunBehaviourUpdate", "Gfx.WaitForPresentOnGfxThread", "RenderPipelineManager.DoRenderLoop_Internal()", "Inl_UniversalRenderPipeline.RenderSingleCameraInternal: Main Camera", "PlayerLoop" };
            var recs = new UnityEngine.Profiling.Recorder[names.Length];
            for (int i = 0; i < names.Length; i++) { recs[i] = UnityEngine.Profiling.Recorder.Get(names[i]); recs[i].enabled = true; }
            var sums = new double[names.Length];
            var p = Process.GetCurrentProcess();
            p.Refresh();
            var cpu0 = p.TotalProcessorTime;
            uint tick0 = mock.Tick;
            var wall = Stopwatch.StartNew();
            const int frames = 240;
            for (int i = 0; i < frames; i++)
            {
                yield return null;
                for (int k = 0; k < names.Length; k++) sums[k] += recs[k].isValid ? recs[k].elapsedNanoseconds / 1e6 : -1;
            }
            p.Refresh();
            var parts = new System.Text.StringBuilder();
            for (int k = 0; k < names.Length; k++) parts.Append($" {names[k]}={sums[k] / frames:0.00}");
            Debug.Log("CrowdParts:" + parts);
            double cpu = (p.TotalProcessorTime - cpu0).TotalMilliseconds / frames;
            double tpf = (mock.Tick - tick0) / (double)frames;
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 60; i++) mock.Advance(1);
            double sim = sw.Elapsed.TotalMilliseconds / 60;
            sw.Restart();
            for (int i = 0; i < 30; i++) root.World.Render();
            double render = sw.Elapsed.TotalMilliseconds / 30;
            var units = new UnitState[4096];
            sw.Restart();
            for (int i = 0; i < 100; i++) mock.ReadUnits(units);
            double read = sw.Elapsed.TotalMilliseconds / 100;
            Debug.Log($"CrowdCpu: {cpu:0.0} ms cpu, {wall.Elapsed.TotalMilliseconds / frames:0.0} ms wall a frame; tick {sim:0.00} ms, render {render:0.00} ms, read {read:0.000} ms, {tpf:0.00} ticks a frame, {mock.ReadUnits(units)} units");
            Object.Destroy(root.gameObject);
        }
    }
}
