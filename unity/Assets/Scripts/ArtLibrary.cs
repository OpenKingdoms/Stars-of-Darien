// ArtLibrary.cs - finds the model for each unit kind. A model dropped in
// Assets/Art/Resources/Units/ named after the kind (soldier, archer) is
// used as is, and a kind without one gets a placeholder capsule. See
// docs/ART.md for scale, pivot and facing.
using System.Collections.Generic;
using UnityEngine;

namespace OpenKingdomsUnity
{
    public static class ArtLibrary
    {
        public static readonly string[] KindNames = { "soldier", "archer" };

        static readonly Dictionary<int, GameObject> cache = new Dictionary<int, GameObject>();

        static GameObject ModelFor(int kind)
        {
            if (!cache.TryGetValue(kind, out var model))
            {
                string name = kind >= 0 && kind < KindNames.Length ? KindNames[kind] : "unit";
                model = Resources.Load<GameObject>("Units/" + name);
                cache[kind] = model;
            }
            return model;
        }

        // A unit's visual under parent, facing +Z with its feet at the
        // parent's origin, in its team's colour.
        public static void Build(Transform parent, int kind, Color team)
        {
            var model = ModelFor(kind);
            if (model != null)
            {
                var go = Object.Instantiate(model, parent, false);
                go.name = "model";
                foreach (var c in go.GetComponentsInChildren<Collider>()) Object.Destroy(c);
                // Materials with "team" in their name take the team colour.
                foreach (var r in go.GetComponentsInChildren<Renderer>())
                    foreach (var m in r.materials)
                        if (m.name.ToLowerInvariant().Contains("team")) Tint(m, team);
                return;
            }

            bool archer = kind == OkNative.KindArcher;
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Object.Destroy(body.GetComponent<Collider>());
            body.name = "placeholder";
            body.transform.SetParent(parent, false);
            float w = archer ? 0.45f : 0.6f, h = archer ? 0.65f : 0.6f;
            body.transform.localScale = new Vector3(w, h, w);
            body.transform.localPosition = new Vector3(0, h, 0);
            Tint(body.GetComponent<Renderer>().material, team);

            // A nose, so the facing shows.
            var nose = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(nose.GetComponent<Collider>());
            nose.transform.SetParent(parent, false);
            nose.transform.localScale = new Vector3(0.15f, 0.15f, 0.3f);
            nose.transform.localPosition = new Vector3(0, h * 1.4f, w * 0.5f);
            Tint(nose.GetComponent<Renderer>().material, archer ? new Color(0.9f, 0.85f, 0.4f) : Color.gray);
        }

        public static void Tint(Material m, Color c)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }
    }
}
