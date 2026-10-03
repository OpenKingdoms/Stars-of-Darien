// ChartHover.cs - reading a graph under the pointer.
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace OpenKingdomsUnity.Game.UI
{
    // The pointer over a graph: a rule at its point of the battle, and every
    // kingdom's figure there on the help line.
    public sealed class ChartHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public ResultScreen Screen;
        public Image Cursor;
        Camera eye;
        bool over;

        public void OnPointerEnter(PointerEventData e) { over = true; eye = e.enterEventCamera; }

        public void OnPointerExit(PointerEventData e)
        {
            over = false;
            if (Cursor != null) Cursor.gameObject.SetActive(false);
            Screen?.Page?.ShowHelp(null);
        }

        void Update()
        {
            if (!over || Screen == null || Cursor == null) return;
            var rt = (RectTransform)transform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, Input.mousePosition, eye, out var local)) return;
            float f = Mathf.Clamp01((local.x - rt.rect.xMin) / Mathf.Max(1f, rt.rect.width));
            Cursor.gameObject.SetActive(true);
            Cursor.rectTransform.anchoredPosition = new Vector2(f * rt.rect.width, 0);
            Screen.Page?.ShowHelp(Screen.ReadAt(f));
        }
    }
}
