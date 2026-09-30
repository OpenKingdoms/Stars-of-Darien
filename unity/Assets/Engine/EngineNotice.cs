// EngineNotice.cs - what Play shows in a scene without the game when the
// engine cannot run: what is wrong and what to do, on the game's own panel.
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.UI;
using UnityEngine;
using UnityEngine.UI;

namespace OpenKingdomsUnity.Engine
{
    public sealed class EngineNotice : MonoBehaviour
    {
        public const string Heading = "The game's engine is not running";
        public string Problem { get; private set; }
        public Text Message { get; private set; }

        public static EngineNotice Show(string problem)
        {
            var n = new GameObject("EngineNotice").AddComponent<EngineNotice>();
            n.Build(problem);
            Debug.LogWarning(GameRoot.Title + ": " + problem);
            return n;
        }

        void Build(string problem)
        {
            Problem = problem;
            // An empty scene has no camera, and the Game view would say so.
            if (FindAnyObjectByType<Camera>() == null)
            {
                var cam = new GameObject("Main Camera").AddComponent<Camera>();
                cam.tag = "MainCamera";
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.black;
                cam.transform.SetParent(transform, false);
            }
            var canvas = UiKit.MakeCanvas("Notice", 10).transform;
            canvas.SetParent(transform, false);
            UiKit.Picture(canvas, "Black", UiKit.White, Color.black).rectTransform.Fill();
            UiKit.Label(canvas, GameRoot.Title, 40, UiKit.Dim, TextAnchor.MiddleCenter, true).rectTransform.Place(0, 0.86f, 1, 0.95f);
            var panel = UiKit.Panel(canvas, "Panel", false).Place(0.5f, 0.5f, 0.5f, 0.5f, -480, -220, -480, -220);
            UiKit.Label(panel, Heading, 44, UiKit.Gold, TextAnchor.MiddleCenter, true).rectTransform.Place(0, 0.7f, 1, 0.95f, 40, 0, 40, 0);
            Message = UiKit.Label(panel, problem, 30, UiKit.Pale, TextAnchor.UpperCenter);
            Message.rectTransform.Place(0, 0, 1, 0.68f, 56, 40, 56, 0);
        }
    }
}
