// EntityRenderer.Breaking.cs - the renderer's side of breaking: it reads the
// backend's blasts, feature events and thrown pieces each frame, starts each
// dying feature's fall from where it is drawn, fades the next stage in at
// the swap, and draws stages with no model of their own from an earlier
// stage's chunks.
using System.Collections.Generic;
using UnityEngine;

namespace OpenKingdomsUnity.Game.World
{
    public sealed partial class EntityRenderer
    {
        int lastBlast, lastFeatureEvent, lastPieceEvent;
        bool caughtUp;
        readonly BlastEvent[] blastBuf = new BlastEvent[64];
        readonly FeatureEvent[] featureEventBuf = new FeatureEvent[64];
        readonly PieceEvent[] pieceEventBuf = new PieceEvent[64];
        // The last blasts by id, for the way each shot travelled.
        const int BlastMemory = 256;
        readonly int[] blastIds = new int[BlastMemory];
        readonly Vector3[] blastDirs = new Vector3[BlastMemory];
        // Bumped when a split finishes, so stages drawn from it are built again.
        int fallbackVersion, readySeen;
        int[] ancestors;
        bool[] burntStage;
        sbyte[] modelled;
        int chipsThisFrame;
        // Where the camera is, and how far off a break shows its parts whole.
        Vector3 eye;
        public const float FarBreak = 140f;
        const int ChipsPerFrame = 6;
        readonly List<string> nameScratch = new List<string>(3);

        // How many features are breaking now, for tests.
        public int Breaking
        {
            get
            {
                int n = 0;
                foreach (var e in featureEntries) if (e.Hold != 0) n++;
                return n;
            }
        }

        void ReadDestruction()
        {
            chipsThisFrame = 0;
            int n;
            while ((n = backend.ReadBlasts(lastBlast, blastBuf)) > 0)
            {
                for (int i = 0; i < n; i++)
                {
                    int slot = blastBuf[i].Id & (BlastMemory - 1);
                    blastIds[slot] = blastBuf[i].Id;
                    blastDirs[slot] = blastBuf[i].Direction;
                }
                lastBlast = blastBuf[n - 1].Id;
                if (n < blastBuf.Length) break;
            }
            // News older than a couple of seconds when the battle is first
            // drawn, such as from before a saved game was loaded, is let go.
            uint stale = caughtUp ? 0u : (uint)(backend.TicksPerSecond * 2);
            while ((n = backend.ReadFeatureEvents(lastFeatureEvent, featureEventBuf)) > 0)
            {
                for (int i = 0; i < n; i++)
                {
                    if (featureEventBuf[i].Tick + stale >= backend.Tick || stale == 0) OnFeature(featureEventBuf[i]);
                    FeatureNews?.Invoke(featureEventBuf[i]);
                }
                lastFeatureEvent = featureEventBuf[n - 1].Id;
                if (n < featureEventBuf.Length) break;
            }
            while ((n = backend.ReadPieceEvents(lastPieceEvent, pieceEventBuf)) > 0)
            {
                for (int i = 0; i < n; i++)
                    if (pieceEventBuf[i].Tick + stale >= backend.Tick || stale == 0) OnPiece(pieceEventBuf[i]);
                lastPieceEvent = pieceEventBuf[n - 1].Id;
                if (n < pieceEventBuf.Length) break;
            }
            caughtUp = true;
        }

        Vector3 BlastDirection(int id)
        {
            int slot = id & (BlastMemory - 1);
            return id != 0 && blastIds[slot] == id ? blastDirs[slot] : Vector3.zero;
        }

        FeatureDef FeatureDefOf(int def) => def >= 0 && def < backend.FeatureDefs.Count ? backend.FeatureDefs[def] : null;

        void OnFeature(in FeatureEvent e)
        {
            switch (e.Kind)
            {
                case FeatureEventKind.Hit: ChipAt(e); break;
                case FeatureEventKind.Dying: StartBreak(e); break;
                case FeatureEventKind.Dead:
                case FeatureEventKind.Burnt: EndBreak(e); break;
                case FeatureEventKind.Swept: SinkAway(e); break;
                case FeatureEventKind.Placed: AskChain(e.Def, null); break;
            }
        }

        // The entry drawn for a feature: near the index it had, of its kind, where it stands.
        FeatureEntry EntryFor(int index, int def, Vector3 at)
        {
            int n = featureEntries.Count;
            for (int r = 0; r < 64; r++)
            {
                int k = index + r;
                if (k >= 0 && k < n && Is(featureEntries[k], def, at)) return featureEntries[k];
                k = index - r;
                if (r > 0 && k >= 0 && k < n && Is(featureEntries[k], def, at)) return featureEntries[k];
            }
            foreach (var e in featureEntries) if (Is(e, def, at)) return e;
            return null;
        }

        static bool Is(FeatureEntry e, int def, Vector3 at) => e.Def == def && e.Position.x == at.x && e.Position.z == at.z;

        // Whether the player would see a break here: in sight and near the camera's view.
        bool Shows(Vector3 at, float reach) => (Unseen == null || !Unseen(at)) && InView(at, reach + 4f);

        void StartBreak(in FeatureEvent e)
        {
            var entry = EntryFor(e.Feature, e.Def, e.Position);
            if (entry == null || entry.Hold != 0) return;
            if (entry.Draws.Count == 0 || !Shows(e.Position, 6f)) return;
            var away = e.Position - e.From;
            away.y = 0f;
            if (away.sqrMagnitude < 0.09f) away = BlastDirection(e.Blast);
            float seconds = e.Ticks / (float)Mathf.Max(1, backend.TicksPerSecond);
            bool far = (e.Position - eye).sqrMagnitude > FarBreak * FarBreak;
            // From where it is drawn, in any crater it stands in.
            int hold = falls.Begin(e.Def, ScarMap.Sink(e.Position) * entry.Basis, e.Position, away, seconds, out bool shown, far);
            if (shown) entry.Hold = hold;
            // What comes down churns the ground under it.
            var kind = falls.LastKind;
            if (shown && (kind == BreakKind.Wall || kind == BreakKind.Hut || kind == BreakKind.Building))
            {
                var d = FeatureDefOf(e.Def);
                float cell = backend.Terrain != null ? backend.Terrain.CellSize : 1f;
                float reach = d != null ? Mathf.Max(1, Mathf.Max(d.Footprint.x, d.Footprint.y)) * cell * 0.6f : 1f;
                ScarMap.Current?.Mark(ScarKind.Dust, e.Position, reach, away, 0.8f);
            }
        }

        // The swap: what stood waiting fades as the next stage fades in, or falls when there is none.
        void EndBreak(in FeatureEvent e)
        {
            var entry = EntryFor(e.Feature, e.Def, e.Position);
            if (entry == null) return;
            if (entry.Hold != 0) { Debris.Release(entry.Hold, e.NewDef >= 0); entry.Hold = 0; }
            if (e.NewDef >= 0) entry.FadeNext = true;
            if (e.NewDef >= 0) AskChain(e.NewDef, null);
        }

        void SinkAway(in FeatureEvent e)
        {
            var entry = EntryFor(e.Feature, e.Def, e.Position);
            if (entry != null && Shows(e.Position, 6f)) falls.Sink(entry.Draws);
        }

        // Chips off what a blast reaches but does not kill.
        void ChipAt(in FeatureEvent e)
        {
            if (e.Blast == 0 || chipsThisFrame >= ChipsPerFrame || !Shows(e.Position, 2f)) return;
            var d = FeatureDefOf(e.Def);
            if (d == null || e.Health <= 0 && e.Damage > 0) return;
            var entry = EntryFor(e.Feature, e.Def, e.Position);
            if (entry == null || entry.Hold != 0) return;
            var kind = Fracture.KindOf(d);
            if (kind == BreakKind.None) return;
            float reach = Mathf.Max(1, Mathf.Max(d.Footprint.x, d.Footprint.y)) * (backend.Terrain != null ? backend.Terrain.CellSize : 1f) * 0.5f;
            falls.Chip(e.Position, e.From, reach, Fracture.InteriorOf(d, kind));
            chipsThisFrame++;
        }

        void OnPiece(in PieceEvent e)
        {
            var def = e.Def >= 0 && e.Def < backend.UnitDefs.Count ? backend.UnitDefs[e.Def] : null;
            if (def != null && CardOverride.For(def.ObjectName) != null) return;
            var model = models.Get(e.Model, OverrideKind.Unit, def?.ModelNames);
            bool show = !e.OutOfSight && InView(e.Pose.GetColumn(3), 6f);
            pieceFalls.Throw(e, model, def != null && def.IsBuilding, show);
        }

        void StepDebris(Camera cam)
        {
            // The Battle effects setting changed mid-battle: its caps hold within the pool made at the start.
            if (Debris.Budget.Level != FxQuality.Current.Level) Debris.Budget = BreakBudget.From(FxQuality.Current);
            pieceFalls.Sweep(Units, UnitCount);
            if (Fractures.Waiting > 0) Fractures.Work(SplitSliceMs);
            if (Fractures.Ready != readySeen) { readySeen = Fractures.Ready; fallbackVersion++; }
            var t = backend.Terrain;
            Debris.Sea = t != null && t.SeaLevel > 0 ? t.SeaLevel : float.NaN;
            Dust.Wind = backend.ReadWind(out var wind) ? wind.Toward * (0.3f + 1.2f * wind.Strength) : Vector3.zero;
            Debris.Step(simDt);
            Dust.Step(simDt);
            debrisDraws.Clear();
            smallDebrisDraws.Clear();
            if (!HideModels) Debris.Draw(debrisDraws, smallDebrisDraws, haveFrustum ? frustum : null);
        }

        // ---- What each kind is made of ----

        // A kind's parts at rest in its own space: a feature def's drop-in
        // model or its 3D model's pieces, or a unit model's piece.
        bool KindParts(long key, List<KindPart> into)
        {
            if (key >> 40 != 0) return PieceParts((int)(key >> 12 & 0xFFFFFFF), (int)(key & 0xFFF), into);
            var d = FeatureDefOf((int)key);
            if (d == null) return false;
            var names = new[] { d.Name, d.SequenceName, d.ObjectName };
            if (!string.IsNullOrEmpty(d.ObjectName))
            {
                var model = models.Get(backend.LoadModel(d.ObjectName, 0), OverrideKind.Feature, names);
                if (model == null) return false;
                if (model.Override != null)
                {
                    foreach (var part in model.Override.Parts) into.Add(new KindPart { Mesh = part.Mesh, Submesh = part.Submesh, Material = part.Material, Local = part.NodeToRoot, Flat = part.Flat });
                    return into.Count > 0;
                }
                var data = model.Data;
                for (int p = 0; p < model.Pieces.Length; p++)
                {
                    var mesh = model.Pieces[p];
                    string name = data.Pieces[p].Name ?? "";
                    if (mesh == null || name.EndsWith("_off") || name.EndsWith("_dead")) continue;
                    bool flat = ModelCache.IsFlat(mesh.bounds);
                    var rest = RestOf(data, p);
                    for (int s = 0; s < model.Materials[p].Length; s++)
                        into.Add(new KindPart { Mesh = mesh, Submesh = s, Material = model.Materials[p][s], Local = rest, Flat = flat });
                }
                return into.Count > 0;
            }
            var over = OverrideLoader.Find(OverrideKind.Feature, null, names);
            if (over == null) return false;
            foreach (var part in over.Parts) into.Add(new KindPart { Mesh = part.Mesh, Submesh = part.Submesh, Material = part.Material, Local = part.NodeToRoot, Flat = part.Flat });
            return into.Count > 0;
        }

        bool PieceParts(int modelId, int piece, List<KindPart> into)
        {
            var model = models.Get(modelId);
            if (model == null || piece < 0 || piece >= model.Pieces.Length) return false;
            if (model.Override != null)
            {
                foreach (var part in model.Override.Parts)
                    if (part.Piece == piece) into.Add(new KindPart { Mesh = part.Mesh, Submesh = part.Submesh, Material = part.Material, Local = model.RestInverse[piece] * part.NodeToRoot });
                return into.Count > 0;
            }
            var mesh = model.Pieces[piece];
            if (mesh == null) return false;
            for (int s = 0; s < model.Materials[piece].Length; s++)
                into.Add(new KindPart { Mesh = mesh, Submesh = s, Material = model.Materials[piece][s], Local = Matrix4x4.identity });
            return true;
        }

        // ---- Splitting ahead ----

        bool breakingAsked;

        // Splits every kind on the field that can break, and the stages it
        // breaks into, for about budgetMs a call. True once all are split.
        public bool WarmBreaking(double budgetMs)
        {
            if (!breakingAsked)
            {
                breakingAsked = true;
                var seen = new HashSet<int>();
                foreach (var e in featureEntries) AskChain(e.Def, seen);
            }
            return Fractures.Work(budgetMs);
        }

        readonly HashSet<int> chainSeen = new HashSet<int>();

        // A kind and every stage it can become.
        void AskChain(int def, HashSet<int> seen)
        {
            if (seen == null) { chainSeen.Clear(); seen = chainSeen; }
            for (int guard = 0; guard < 16; guard++)
            {
                var d = FeatureDefOf(def);
                if (d == null || !seen.Add(def)) return;
                AskKind(def, d);
                if (d.BurntDef >= 0 && d.BurntDef != d.DeadDef) AskChain(d.BurntDef, seen);
                def = d.DeadDef;
            }
        }

        void AskKind(int def, FeatureDef d)
        {
            var kind = Fracture.KindOf(d);
            if (kind == BreakKind.None || kind == BreakKind.Rock || !d.Breakable || Fractures.Known(def)) return;
            if (!HasModel(def)) return;
            Fractures.Ask(def, d.Name, kind, Fracture.InteriorOf(d, kind));
        }

        // ---- Stages with no model of their own ----

        // Whether a def is drawn as a 3D model rather than its picture.
        bool HasModel(int def)
        {
            int n = backend.FeatureDefs.Count;
            if (modelled == null || modelled.Length != n) modelled = new sbyte[n];
            if (def < 0 || def >= n) return false;
            if (modelled[def] == 0)
            {
                var d = backend.FeatureDefs[def];
                bool has = !string.IsNullOrEmpty(d.ObjectName) || OverrideLoader.Find(OverrideKind.Feature, null, d.Name, d.SequenceName, d.ObjectName) != null;
                modelled[def] = (sbyte)(has ? 1 : -1);
            }
            return modelled[def] > 0;
        }

        // The nearest earlier stage with a model, and whether fire made this one.
        int Ancestor(int def, out bool burnt)
        {
            burnt = false;
            int n = backend.FeatureDefs.Count;
            if (ancestors == null || ancestors.Length != n)
            {
                ancestors = new int[n];
                burntStage = new bool[n];
                for (int i = 0; i < n; i++) ancestors[i] = -1;
                for (int i = 0; i < n; i++)
                {
                    var d = backend.FeatureDefs[i];
                    if (d.DeadDef >= 0 && d.DeadDef < n && ancestors[d.DeadDef] < 0) ancestors[d.DeadDef] = i;
                    if (d.BurntDef >= 0 && d.BurntDef < n && ancestors[d.BurntDef] < 0) { ancestors[d.BurntDef] = i; burntStage[d.BurntDef] = true; }
                }
            }
            if (def < 0 || def >= n) return -1;
            burnt = burntStage[def];
            int a = ancestors[def];
            for (int guard = 0; a >= 0 && guard < 16; guard++, a = ancestors[a])
            {
                burnt |= burntStage[a];
                if (HasModel(a)) return a;
            }
            return -1;
        }

        // A stage with no model, reached by breaking, drawn from the nearest
        // earlier stage's chunks: what of it still stands, charred if fire
        // made it, or its rubble on the ground.
        bool DrawFallback(FeatureEntry e, in FeatureState f, Matrix4x4 basis)
        {
            int from = Ancestor(f.Def, out bool burnt);
            if (from < 0) return false;
            e.Fallback = fallbackVersion;
            var set = Fractures.Get(from);
            if (set == null) { AskChain(from, null); return false; }
            var d = backend.FeatureDefs[f.Def];
            var a = backend.FeatureDefs[from];
            float bottom = Mathf.Min(0f, set.Bounds.min.y), h = set.Bounds.max.y - bottom;
            float share = d.Height > 0f && a.Height > 0f ? Mathf.Clamp01(d.Height / a.Height) : 0.5f;
            bool rubble = share < 0.3f;
            float line = bottom + (rubble ? 0.6f : set.Kind == BreakKind.Tree ? FeatureFalls.CrownLine : share) * h;
            uint seed = (uint)Mathf.RoundToInt(f.Position.x * 131f) * 73856093u ^ (uint)Mathf.RoundToInt(f.Position.z * 131f) * 19349663u | 1u;
            float spread = Mathf.Max(0.5f, Mathf.Max(set.Bounds.extents.x, set.Bounds.extents.z));
            for (int i = 0; i < set.Count; i++)
            {
                if (set.Centre[i].y >= line) continue;
                Matrix4x4 m;
                if (!rubble) m = basis * set.Place[i];
                else
                {
                    // Lying where it fell, turned any way, on the ground.
                    var turn = Quaternion.Euler(Fracture.Rand01(ref seed) * 360f, Fracture.Rand01(ref seed) * 360f, Fracture.Rand01(ref seed) * 360f);
                    var lin = basis * set.Place[i];
                    lin.m03 = lin.m13 = lin.m23 = 0f;
                    var shape = Matrix4x4.Rotate(turn) * lin * Matrix4x4.Translate(-set.Pivot[i]);
                    float low = float.MaxValue;
                    foreach (var s in set.Support[i]) low = Mathf.Min(low, shape.MultiplyPoint3x4(s).y);
                    if (low == float.MaxValue) low = 0f;
                    float ang = Fracture.Rand01(ref seed) * Mathf.PI * 2f, r = spread * Mathf.Sqrt(Fracture.Rand01(ref seed));
                    var at = f.Position + new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang)) * r;
                    at.y = groundAt(at.x, at.z) - low;
                    m = Matrix4x4.Translate(at) * shape;
                }
                foreach (var draw in set.Draws[i])
                    e.Draws.Add((draw.Mesh, draw.Submesh, burnt ? Fractures.CharMaterial(draw.Material) : draw.Material, m, false));
            }
            return e.Draws.Count > 0;
        }

        // The breaking stages with no model of their own, by name, for the list of models still to make.
        public List<string> StagesWithoutModels()
        {
            var list = new List<string>();
            var defs = backend.FeatureDefs;
            var stage = new bool[defs.Count];
            foreach (var d in defs)
            {
                if (d.DeadDef >= 0 && d.DeadDef < stage.Length) stage[d.DeadDef] = true;
                if (d.BurntDef >= 0 && d.BurntDef < stage.Length) stage[d.BurntDef] = true;
            }
            for (int i = 0; i < defs.Count; i++)
                if (stage[i] && !HasModel(i) && !string.IsNullOrEmpty(defs[i].SequenceName)) list.Add(defs[i].Name);
            return list;
        }
    }
}
