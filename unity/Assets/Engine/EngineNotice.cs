// EngineNotice.cs - what Play shows in a scene without the game when the
// engine cannot run: what is wrong and what to do, in a dialog on vellum.
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
            canvas.gameObject.AddComponent<Resharpen>();
            // The game folder screen's book: vellum, the name on its strip.
            UiKit.VellumPage(canvas, "Page");
            UiKit.TitleStrip(canvas, GameRoot.Title);
            var box = DialogLayout.ForScreen(Screen.width, Screen.height).Notice();
            var d = UiKit.MakeDialog(canvas, box, Heading, "");
            var rt = (RectTransform)d.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, -DialogLayout.StripH / 2f);
            Message = UiKit.Words(d.transform, "Message", box.Local(box.Note), problem, DialogLayout.Sentence, HudArt.Ink, UiKit.BodyFont);
        }
    }
}
