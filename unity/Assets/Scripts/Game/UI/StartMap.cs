// StartMap.cs - the map view with its start positions set in it as
// jewels: a pearl where a start is free, the kingdom's colour where a seat
// holds or will take it, numbered as the map numbers them. A click takes a
// start, and where moves are allowed a held jewel drags onto another.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace OpenKingdomsUnity.Game.UI
{
    public sealed class StartMap
    {
        public const float MarkerCp = 13f;

        // Who a start belongs to, for drawing it.
        public struct Owner
        {
            public int Colour;      // -1 free
            public bool Held;       // taken, not only expected
            public bool Mine;
            public string Who;
        }

        public Func<int, bool> Take;             // a click on start i
        public Func<int, int, bool> Move;        // a drag from start a to b, or off the map (-1)
        public Func<int, Owner> OwnerOf;
        public Action Changed;

        readonly GuiPage page;
        readonly RectTransform box;
        readonly RawImage picture;
        readonly List<StartMarker> markers = new List<StartMarker>();
        readonly float boxW, boxH;
        MapInfo map;
        Rect shown;         // the picture inside the box, cp from its top left

        public RectTransform Box => box;
        public IReadOnlyList<StartMarker> Markers => markers;

        public StartMap(GuiPage page, float x, float y, float w, float h)
        {
            this.page = page;
            boxW = w;
            boxH = h;
            box = page.Put("Map view", x, y, w, h);
            picture = page.Picture(box, "Picture", null, 0, 0, w, h);
            picture.color = Color.white;
        }

        public void Show(MapInfo m, Texture2D tex)
        {
            map = m;
            picture.texture = tex;
            picture.enabled = tex != null;
            float a = tex != null ? tex.width / (float)Mathf.Max(1, tex.height) : (m != null && m.Size.y > 0 ? m.Size.x / m.Size.y : 1f);
            float w = boxW, h = boxW / a;
            if (h > boxH) { h = boxH; w = boxH * a; }
            shown = new Rect((boxW - w) / 2f, (boxH - h) / 2f, w, h);
            var rt = picture.rectTransform;
            rt.anchoredPosition = new Vector2(shown.x * page.K, -shown.y * page.K);
            rt.sizeDelta = new Vector2(w * page.K, h * page.K);
            int n = m != null && m.Starts != null ? m.Starts.Length : 0;
            while (markers.Count < n) markers.Add(NewMarker(markers.Count));
            for (int i = 0; i < markers.Count; i++) markers[i].gameObject.SetActive(i < n);
            Refresh();
        }

        // Where start i sits in the box, cp from its top left.
        public Vector2 Spot(int i)
        {
            var s = map.Starts[i];
            float u = map.Size.x > 0 ? s.x / map.Size.x : 0.5f, v = map.Size.y > 0 ? s.y / map.Size.y : 0.5f;
            return new Vector2(shown.x + Mathf.Clamp01(u) * shown.width, shown.y + Mathf.Clamp01(v) * shown.height);
        }

        public void Refresh()
        {
            if (map == null) return;
            for (int i = 0; i < markers.Count && i < map.Starts.Length; i++)
            {
                var o = OwnerOf != null ? OwnerOf(i) : new Owner { Colour = -1 };
                var mk = markers[i];
                var at = Spot(i);
                mk.Rest = new Vector2(at.x * page.K, -at.y * page.K);
                if (!mk.Dragging) ((RectTransform)mk.transform).anchoredPosition = mk.Rest;
                var stone = o.Colour < 0 ? HudArt.Pearl : (Color)LobbyScreens.Tint(o.Colour);
                string key = o.Colour < 0 ? "startFree" : "start" + o.Colour;
                mk.Jewel.texture = page.Paint(key, s => HudArt.Boss(MarkerCp - 1f, s, stone));
                mk.Jewel.color = o.Colour >= 0 && !o.Held ? new Color(1, 1, 1, 0.62f) : Color.white;
                mk.Number.color = stone.grayscale > 0.55f ? HudArt.Ink : Color.white;
                mk.Breath.enabled = o.Mine;
                if (!o.Mine) mk.transform.localScale = Vector3.one;
                mk.Owner = o;
                mk.Help.Line = $"Start {i + 1}: " + (o.Colour < 0 ? "free, click to take it" : o.Held ? o.Who : o.Who + " will start here");
            }
        }

        StartMarker NewMarker(int i)
        {
            var rt = page.Put(box, "Start " + (i + 1), 0, 0, MarkerCp, MarkerCp);
            rt.pivot = new Vector2(0.5f, 0.5f);
            var hit = rt.gameObject.AddComponent<Image>();
            hit.sprite = UiKit.White;
            hit.color = LobbyInk.Clear;
            var mk = rt.gameObject.AddComponent<StartMarker>();
            mk.Map = this;
            mk.Index = i;
            mk.Jewel = page.Picture(rt, "Jewel", null, 0, 0, MarkerCp, MarkerCp);
            mk.Number = page.Label(rt, (i + 1).ToString(), 0, 0, MarkerCp - 1f, MarkerCp - 1f, 8f, HudArt.Ink, TextAnchor.MiddleCenter, LobbyInk.Caps);
            mk.Breath = rt.gameObject.AddComponent<Breathe>();
            page.Hover(rt.gameObject, "");
            mk.Help = rt.GetComponent<HelpHover>();
            return mk;
        }

        internal void Clicked(int i)
        {
            if (Take != null && Take(i)) Changed?.Invoke();
        }

        internal void Dropped(StartMarker from, Vector2 screen, Camera cam)
        {
            if (Move == null) return;
            int to = -1;
            float best = MarkerCp * page.Density;
            foreach (var mk in markers)
            {
                if (mk == from || !mk.gameObject.activeSelf) continue;
                var c = RectTransformUtility.WorldToScreenPoint(cam, mk.transform.position);
                float d = Vector2.Distance(c, screen);
                if (d < best) { best = d; to = mk.Index; }
            }
            bool inside = RectTransformUtility.RectangleContainsScreenPoint(box, screen, cam);
            if (to < 0 && inside) return;
            if (Move(from.Index, to)) Changed?.Invoke();
        }
    }

    public sealed class StartMarker : MonoBehaviour, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public StartMap Map;
        public int Index;
        public RawImage Jewel;
        public Text Number;
        public Breathe Breath;
        public HelpHover Help;
        public StartMap.Owner Owner;
        public Vector2 Rest;
        public bool Dragging;

        public void OnPointerClick(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left || e.dragging) return;
            UiKit.Play("menubutton.wav");
            Map.Clicked(Index);
        }

        public void OnBeginDrag(PointerEventData e)
        {
            Dragging = Map.Move != null && Owner.Colour >= 0 && Owner.Held;
            if (Dragging) transform.SetAsLastSibling();
        }

        public void OnDrag(PointerEventData e)
        {
            if (!Dragging) return;
            var parent = (RectTransform)transform.parent;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, e.position, e.pressEventCamera, out var local))
                ((RectTransform)transform).anchoredPosition = local;
        }

        public void OnEndDrag(PointerEventData e)
        {
            if (!Dragging) return;
            Dragging = false;
            ((RectTransform)transform).anchoredPosition = Rest;
            Map.Dropped(this, e.position, e.pressEventCamera);
        }
    }
}
