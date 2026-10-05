// PlaytestFixesPlayTests.cs - the owner's playtest of 4 October on the real
// engine, a scenario for each thing he found: a Harpy flock under Kirenna's
// Water Blast, the minimap's look in both schemes, the Harpy's mind control
// over many casts, Ctrl on a Zhon builder's card, and a Zhon army placed on
// one spot. Each logs a PLAYTEST line with what it measured. With
// OKU_PLAYTEST_OUT naming a folder they also write pictures, clip frames and
// a trace of every cast there, OKU_PLAYTEST_SEED picks the battle's seed and
// OKU_PLAYTEST_PREY the unit the Harpies are sent at. Needs okengine and the
// game files, and is ignored without them.
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OpenKingdomsUnity.Engine;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.UI;
using OpenKingdomsUnity.Game.World;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace OpenKingdomsUnity.Tests
{
    public class PlaytestFixesPlayTests
    {
        GameRoot root;
        EngineBackend engine;
        PointerFrame frame;
        readonly UnitState[] units = new UnitState[1024];
        readonly BlastEvent[] blasts = new BlastEvent[256];
        int blastSince;
        const int CmdGiveUnits = 24; // TAK_CMD_GIVE_UNITS, arg the seat receiving
        const float Px = 16f;        // engine pixels to a world unit

        static string OutDir => System.Environment.GetEnvironmentVariable("OKU_PLAYTEST_OUT");

        [TearDown]
        public void CleanUp()
        {
            BattleHud.CtrlKey = () => Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            BattleHud.SizeOverride = null;
            if (root != null) Object.Destroy(root.gameObject);
        }

        IEnumerator Begin(string side)
        {
            if (!EngineSettings.EngineAvailable) Assert.Ignore("okengine or the game files are missing");
            engine = new EngineBackend();
            root = GameRoot.Boot(engine);
            yield return null;
            root.Flow.Fire(FlowEvent.OpenSkirmish);
            root.Setup.MapId = "two castles";
            root.Setup.MapRevealed = true;
            root.Setup.LineOfSight = false;
            root.Setup.Seats[0].Side = side;
            root.Setup.Seats[1].Difficulty = AiDifficulty.Easy;
            // One seed, so a run on another engine meets the same battle.
            root.Setup.Seed = uint.TryParse(System.Environment.GetEnvironmentVariable("OKU_PLAYTEST_SEED"), out uint seed) ? seed : 20261004;
            root.Flow.Fire(FlowEvent.Start);
            float deadline = Time.realtimeSinceStartup + 240f;
            while (root.Flow.State != FlowState.Playing && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.AreEqual(FlowState.Playing, root.Flow.State, "the battle loaded: " + root.LastError);
            root.Options.GameSpeed = 0;
            frame = new PointerFrame { Focused = true, Dpi = 96 };
            root.Orders.Formation.Source = () =>
            {
                var r = frame;
                frame.LeftDown = frame.LeftUp = frame.RightDown = frame.RightUp = false;
                return r;
            };
            blastSince = 0;
            int n;
            while ((n = engine.ReadBlasts(blastSince, blasts)) > 0) blastSince = blasts[n - 1].Id;
        }

        int DefNamed(string name)
        {
            var defs = engine.UnitDefs;
            for (int i = 0; i < defs.Count; i++)
                if (string.Equals(defs[i].Name, name, System.StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        bool TryRead(int handle, out UnitState unit)
        {
            int n = engine.ReadUnits(units);
            for (int i = 0; i < n; i++)
                if (units[i].Handle == handle) { unit = units[i]; return true; }
            unit = default;
            return false;
        }

        UnitState Read(int handle)
        {
            Assert.IsTrue(TryRead(handle, out var u), "unit " + handle + " is gone");
            return u;
        }

        Dictionary<uint, UnitState> ById()
        {
            int n = engine.ReadUnits(units);
            var d = new Dictionary<uint, UnitState>();
            for (int i = 0; i < n; i++) d[units[i].StableId] = units[i];
            return d;
        }

        List<BlastEvent> NewBlasts()
        {
            var got = new List<BlastEvent>();
            int n;
            while ((n = engine.ReadBlasts(blastSince, blasts)) > 0)
            {
                for (int i = 0; i < n; i++) got.Add(blasts[i]);
                blastSince = blasts[n - 1].Id;
                if (n < blasts.Length) break;
            }
            return got;
        }

        Camera Cam => root.World.Camera.GetComponent<Camera>();

        void Look(Vector3 at, float distance, float pitch = GameCamera.ClassicPitch)
        {
            var cam = root.World.Camera;
            cam.focus = new Vector3(at.x, engine.GroundHeight(at.x, at.z), at.z);
            cam.yaw = 0f;
            cam.pitch = pitch;
            cam.Zoom(distance);
        }

        IEnumerator Shoot(string name)
        {
            string dir = OutDir;
            if (string.IsNullOrEmpty(dir)) yield break;
            BattleHud.SizeOverride = new Vector2Int(1280, 720);
            for (int i = 0; i < 3; i++) yield return null;
            yield return HudShots.Shoot(Cam, 1280, 720, Path.Combine(dir, name + ".png"));
            BattleHud.SizeOverride = null;
        }

        // Land a unit of def can stand on, out from a point toward the middle.
        Vector3 LandToward(int def, Vector3 from, float reach)
        {
            var size = engine.Terrain.Size;
            var mid = new Vector3(size.x * 0.5f, 0f, -size.y * 0.5f);
            var way = mid - from;
            way.y = 0f;
            way = way.normalized;
            for (float r = reach; r <= reach + 24f; r += 2f)
                for (int k = 0; k < 12; k++)
                {
                    var at = from + Quaternion.Euler(0f, (k % 2 == 0 ? 1 : -1) * 15f * ((k + 1) / 2), 0f) * way * r;
                    if (engine.CanBuildAt(def, at, 0, out var snapped)) return snapped;
                }
            Assert.Fail("no open ground for " + engine.UnitDefs[def].Name);
            return from;
        }

        static float Flat(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        // The cells a footprint of fp cells covers from its corner.
        static Vector2Int Corner(Vector3 p, int fp) =>
            new Vector2Int(Mathf.FloorToInt(p.x - fp * 0.5f), Mathf.FloorToInt(-p.z - fp * 0.5f));

        // ---- 1. A Harpy flock and Kirenna's Water Blast ----

        [UnityTest, Timeout(1800000)]
        public IEnumerator AHarpyFlockInTheAirOverOnePlaceUnderKirennasWaterBlast() => Flock("air", 420);

        [UnityTest, Timeout(1800000)]
        public IEnumerator AHarpyFlockAtRestOnOnePlaceUnderKirennasWaterBlast() => Flock("rest", 3600);

        // Until hover attack (OpenKingdoms #401) an attacking flock stays bunched,
        // so this one only measures.
        [UnityTest, Timeout(1800000)]
        public IEnumerator AHarpyFlockAttackingOnePlaceUnderKirennasWaterBlast() => Flock("attack", 900);

        IEnumerator Flock(string mode, int ticks)
        {
            yield return Begin("VERUNA");
            int me = engine.LocalPlayer;
            var defs = engine.UnitDefs;
            int harpy = DefNamed("ZONHARP"), mage = DefNamed("VERMAGE");
            if (harpy < 0 || mage < 0) Assert.Ignore("no Harpy or Kirenna in these game files");
            int n = engine.ReadUnits(units);
            var kirenna = units.Take(n).First(u => u.Player == me && u.Def == mage).Handle;
            int fp = Mathf.Max(1, defs[harpy].Footprint.x);

            // Kirenna waits a cast's reach from the place the flock is sent.
            var home = Read(kirenna).Position;
            var place = LandToward(harpy, home, 18f);
            var stand = place + (home - place).normalized * 6f;
            engine.Command(GameCommand.To(CommandKind.Move, kirenna, stand));
            const int Count = 30;
            var flock = new List<int>();
            for (int i = 0; i < Count; i++)
            {
                int h = OkEngine.okx_place_unit(harpy, me);
                Assert.GreaterOrEqual(h, 0, "a Harpy set down");
                flock.Add(h);
            }
            engine.Advance(600);
            for (int w = 0; w < 9000 && Read(kirenna).Mana < 900; w += 60) engine.Advance(60);
            var ids = flock.Select(h => Read(h).StableId).ToList();
            var order = mode == "attack" ? CommandKind.AttackGround : CommandKind.Move;
            foreach (var h in flock) engine.Command(GameCommand.To(order, h, place));
            // A frame a second for the first twenty seconds, for a clip.
            int gone = 0;
            for (int k = 1; k <= 20 && gone + 60 <= ticks && !string.IsNullOrEmpty(OutDir); k++)
            {
                engine.Advance(60);
                gone += 60;
                Look(place, 34f, 70f);
                yield return Shoot($"clip-1-{mode}/{k:00}");
            }
            engine.Advance(ticks - gone);

            var alive = ById();
            var now = ids.Where(alive.ContainsKey).Select(id => alive[id]).ToList();
            var mid = new Vector3(now.Average(u => u.Position.x), 0f, now.Average(u => u.Position.z));
            mid.y = engine.GroundHeight(mid.x, mid.z);
            int shared = 0, near = 0, up = now.Count(u => u.Altitude > 0.5f);
            float closest = float.MaxValue, spread = 0f;
            for (int i = 0; i < now.Count; i++)
            {
                spread = Mathf.Max(spread, Flat(now[i].Position, mid));
                for (int j = 0; j < now.Count; j++)
                {
                    if (i == j) continue;
                    float d = Flat(now[i].Position, now[j].Position);
                    if (d * Px < 50f) near++;
                    if (j < i) continue;
                    closest = Mathf.Min(closest, d);
                    var ca = Corner(now[i].Position, fp);
                    var cb = Corner(now[j].Position, fp);
                    if (Mathf.Abs(ca.x - cb.x) < fp && Mathf.Abs(ca.y - cb.y) < fp) shared++;
                }
            }
            int pairs = now.Count * (now.Count - 1) / 2;
            string shape = $"{now.Count} Harpies, {up} in the air, {shared} of {pairs} pairs share cells, closest pair {closest * Px:0} px, " +
                           $"each has {(float)near / Mathf.Max(1, now.Count):0.0} others within 50 px, farthest {spread * Px:0} px from the middle";
            Look(mid, 34f, 70f);
            yield return Shoot($"1-{mode}-a-flock");

            // The cast, from the HUD's own button, at the middle of the flock.
            engine.Select(new[] { kirenna }, false);
            root.Orders.Selected.Clear();
            root.Orders.Selected.Add(kirenna);
            yield return null;
            var cast = engine.SelectionActions().FirstOrDefault(a => a.Label == "Water Blast");
            Assert.IsNotNull(cast, "Kirenna has her Water Blast: " + string.Join(", ", engine.SelectionActions().Select(a => a.Label)));
            Assert.IsTrue(cast.Enabled, "and the mana for it, " + Read(kirenna).Mana);
            NewBlasts();
            Assert.IsTrue(engine.DoAction(cast.Id, mid, -1, default, false), "the Water Blast cast at the middle of the flock");
            BlastEvent burst = default;
            bool burst_seen = false;
            List<UnitState> under = null;
            for (int t = 0; t < 900 && !burst_seen; t++)
            {
                var before = ById();
                engine.Advance(1);
                foreach (var b in NewBlasts())
                    if (b.Def == mage && (b.Slot == 2 || (b.Weapon != null && b.Weapon.Name == "Water Blast")))
                    {
                        burst = b;
                        burst_seen = true;
                        under = ids.Where(before.ContainsKey).Select(id => before[id]).ToList();
                    }
            }
            Assert.IsTrue(burst_seen, "the Water Blast burst");
            Look(mid, 34f, 70f);
            engine.Advance(20);
            yield return Shoot($"1-{mode}-b-water-blast");
            engine.Advance(160);
            alive = ById();
            int lost = ids.Count(id => !alive.TryGetValue(id, out var u) || (u.Flags & UnitFlags.Dying) != 0 || u.Health <= 0);
            float reach = under.Count > 0 ? under.Max(u => Flat(u.Position, burst.Position)) : 0f;
            float cover = under.Count > 0 ? under.Min(u => Flat(u.Position, burst.Position)) : 0f;
            Debug.Log($"PLAYTEST flock-{mode}: {shape}; the Water Blast burst {Flat(burst.Position, mid) * Px:0} px from the middle " +
                      $"with a radius of {burst.Radius * Px:0} px, the flock {cover * Px:0} to {reach * Px:0} px from it, and took {lost} of {ids.Count}");
            yield return Shoot($"1-{mode}-c-after");
            if (mode == "rest") Assert.AreEqual(0, shared, "no two Harpies at rest share cells");
        }

        // ---- 2. The minimap moves the view ----

        [UnityTest, Timeout(900000)]
        public IEnumerator TheMinimapMovesTheViewInBothSchemesWithOrWithoutASelection()
        {
            yield return Begin("ZHON");
            var input = Object.FindAnyObjectByType<MinimapInput>();
            Assert.IsNotNull(input, "the minimap is up");
            var es = EventSystem.current;
            Assert.IsNotNull(es, "an event system");
            var corners = new Vector3[4];
            ((RectTransform)input.transform).GetWorldCorners(corners);
            Vector2 At(float u, float v) => new Vector2(Mathf.Lerp(corners[0].x, corners[2].x, u), Mathf.Lerp(corners[0].y, corners[2].y, v));
            GameObject Top(Vector2 p, out RaycastResult first)
            {
                var hits = new List<RaycastResult>();
                es.RaycastAll(new PointerEventData(es) { position = p }, hits);
                first = hits.Count > 0 ? hits[0] : default;
                return hits.Count > 0 ? hits[0].gameObject : null;
            }

            int me = engine.LocalPlayer;
            var defs = engine.UnitDefs;
            int n = engine.ReadUnits(units);
            var monarch = units.Take(n).First(x => x.Player == me && !defs[x.Def].IsBuilding);
            var size = engine.Terrain.Size;
            var cam = root.World.Camera;
            var lines = new List<string>();
            int shot = 0;
            foreach (bool classic in new[] { true, false })
                foreach (bool chosen in new[] { true, false })
                {
                    string scheme = (classic ? "classic" : "modern") + (chosen ? ", monarch selected" : ", nothing selected");
                    root.Orders.Classic = classic;
                    engine.Cancel();
                    root.World.Entities.Selected.Clear();
                    engine.Select(chosen ? new[] { monarch.Handle } : new int[0], false);
                    if (chosen) root.World.Entities.Selected.Add(monarch.Handle);
                    yield return null;
                    var look = classic ? PointerEventData.InputButton.Right : PointerEventData.InputButton.Left;
                    Look(new Vector3(size.x * 0.5f, 0f, -size.y * 0.5f), 34f);
                    yield return null;
                    var from = cam.focus;
                    if (shot == 0) yield return Shoot("2-a-before-the-press");
                    // A picture lays the HUD out at its own size, so the
                    // minimap is found again once it is back.
                    for (int i = 0; i < 3; i++) yield return null;
                    input = Object.FindAnyObjectByType<MinimapInput>();
                    ((RectTransform)input.transform).GetWorldCorners(corners);

                    var press = At(0.2f, 0.8f);
                    var top = Top(press, out var first);
                    Assert.AreSame(input.gameObject, top != null ? ExecuteEvents.GetEventHandler<IPointerDownHandler>(top) : null, scheme + ": the press reaches the minimap");
                    var data = new PointerEventData(es) { button = look, position = press, pressPosition = press, pointerPressRaycast = first, pointerCurrentRaycast = first };
                    ExecuteEvents.ExecuteHierarchy(top, data, ExecuteEvents.pointerDownHandler);
                    yield return null;
                    float off = Mathf.Abs(cam.focus.x - 0.2f * size.x) + Mathf.Abs(cam.focus.z + 0.2f * size.y);
                    if (shot == 0) yield return Shoot("2-b-after-the-press");
                    for (int i = 0; i < 3; i++) yield return null;
                    input = Object.FindAnyObjectByType<MinimapInput>();
                    ((RectTransform)input.transform).GetWorldCorners(corners);
                    data.position = At(0.7f, 0.3f);
                    ExecuteEvents.Execute(input.gameObject, data, ExecuteEvents.dragHandler);
                    yield return null;
                    float dragOff = Mathf.Abs(cam.focus.x - 0.7f * size.x) + Mathf.Abs(cam.focus.z + 0.7f * size.y);
                    if (shot == 0) yield return Shoot("2-c-after-the-drag");
                    shot++;
                    lines.Add($"{scheme}: the {look} button moved the view {Flat(from, cam.focus) * Px:0} px, {off * Px:0} px off the press and {dragOff * Px:0} px off the drag");
                    Assert.Less(off, 2f, scheme + ": a press with the look button moves the view there");
                    Assert.Less(dragOff, 2f, scheme + ": the drag keeps the view under the pointer");
                    if (chosen) Assert.IsTrue(root.World.Entities.Selected.Contains(monarch.Handle), scheme + ": the selection stays");
                }
            Debug.Log("PLAYTEST minimap: " + string.Join("; ", lines));
        }

        // ---- 3. Mind control misses some of the time and takes time ----

        [UnityTest, Timeout(1800000)]
        public IEnumerator AHarpysMindControlOverManyCasts()
        {
            yield return Begin("ZHON");
            int me = engine.LocalPlayer;
            // The Acolyte of Anu carries no weapon, so nothing strikes back
            // at a Harpy over it while the casts are counted.
            string preyName = System.Environment.GetEnvironmentVariable("OKU_PLAYTEST_PREY") ?? "ARAPRIE2";
            int harpy = DefNamed("ZONHARP"), sword = DefNamed(preyName); // the target's def
            if (harpy < 0 || sword < 0) Assert.Ignore("no Harpy or " + preyName + " in these game files");
            int n = engine.ReadUnits(units);
            int them = units.Take(n).First(u => u.Player != me && u.Player > 0).Player;
            var home = units.Take(n).First(u => u.Player == me && !engine.UnitDefs[u.Def].IsBuilding).Position;

            // A ring of targets out from the start, then the computer's, and a
            // Harpy for each, sent at it from the start.
            const int Count = 24;
            var spots = new List<Vector3>();
            for (int k = 0; k < Count * 3 && spots.Count < Count; k++)
            {
                float r = 22f + 6f * (k / Count);
                var at = home + Quaternion.Euler(0f, 360f * k / Count, 0f) * new Vector3(r, 0f, 0f);
                if (engine.CanBuildAt(sword, at, 0, out var s) && spots.All(o => Flat(o, s) * Px > 90f)) spots.Add(s);
            }
            Assert.GreaterOrEqual(spots.Count, 12, "room for the targets");
            var prey = new List<int>();
            var hunters = new List<int>();
            foreach (var s in spots)
            {
                int p = OkEngine.okx_place_unit(sword, me), h = OkEngine.okx_place_unit(harpy, me);
                Assert.IsTrue(p >= 0 && h >= 0, "a target and a Harpy set down");
                engine.Command(GameCommand.To(CommandKind.Move, p, s));
                prey.Add(p);
                hunters.Add(h);
            }
            engine.Advance(1200);
            for (int w = 0; w < 6000 && hunters.Any(h => Read(h).Mana < 900); w += 60) engine.Advance(60);
            float off = hunters.Select((h, i) => Flat(Read(h).Position, Read(prey[i]).Position)).Average() * Px;
            foreach (var p in prey) Assert.AreEqual(0, OkEngine.okx_command(CmdGiveUnits, p, 0, 0, -1, -1, them), "given to the computer");
            engine.Advance(1);
            foreach (var p in prey) Assert.AreEqual(them, Read(p).Player);
            Look(home, 60f, 75f);
            yield return Shoot("3-a-before");
            NewBlasts();
            for (int i = 0; i < prey.Count; i++)
                Assert.IsTrue(engine.Command(new GameCommand { Kind = CommandKind.Attack, Unit = hunters[i], Target = Read(prey[i]).Position, TargetUnit = prey[i], BuildDef = -1 }), "Harpy " + i + " sent");

            // A capture replaces the unit with a new one of the Harpy's side
            // (legacy:227130), so a target that leaves is looked for
            // again among the units that were not there before.
            var all0 = ById();
            var known = new HashSet<uint>(all0.Keys);
            var preyId = prey.Select(p => Read(p).StableId).ToList();
            var huntId = hunters.Select(h => Read(h).StableId).ToList();
            var last = prey.Select(p => Read(p).Position).ToList();
            var mana = hunters.Select(h => Read(h).Mana).ToList();
            var hp0 = hunters.Select(h => Read(h).Health).ToList();
            var lastShot = new int[hunters.Count];
            var firstShot = new int[hunters.Count];
            var taken = new int[prey.Count];
            var died = new bool[prey.Count];
            var shotsBefore = new int[prey.Count];
            for (int i = 0; i < prey.Count; i++) { taken[i] = -1; firstShot[i] = -1; lastShot[i] = -1; }
            int shots = 0, hits = 0, hitsOnWalkers = 0, gone = 0;
            var flights = new List<int>();
            var trace = new List<string> { "tick,event,harpy,target,px_apart,detail" };
            var preyOfHandle = new Dictionary<int, int>();
            var hunterOfHandle = new Dictionary<int, int>();
            for (int i = 0; i < prey.Count; i++) { preyOfHandle[prey[i]] = i; hunterOfHandle[hunters[i]] = i; }
            const int Ticks = 3600;
            for (int t = 1; t <= Ticks; t++)
            {
                engine.Advance(1);
                var all = ById();
                for (int i = 0; i < hunters.Count; i++)
                {
                    if (!all.TryGetValue(huntId[i], out var h)) continue;
                    if (h.Mana < mana[i] - 100)
                    {
                        shots++;
                        lastShot[i] = t;
                        if (firstShot[i] < 0) firstShot[i] = t;
                        if (taken[i] < 0 && !died[i]) shotsBefore[i]++;
                        trace.Add($"{t},shot,{i},{i},{Flat(h.Position, last[i]) * Px:0},alt {h.Altitude * Px:0}");
                    }
                    mana[i] = h.Mana;
                }
                foreach (var b in NewBlasts())
                {
                    if (b.Unit >= 0 && hunterOfHandle.TryGetValue(b.Unit, out int hit))
                        trace.Add($"{t},harpy-hit,{hit},,,by {(b.Def >= 0 && b.Def < engine.UnitDefs.Count ? engine.UnitDefs[b.Def].Name : "?")} slot {b.Slot} cause {b.Cause} damage {b.Damage} shooter {b.Shooter} owner {b.Player}");
                    if (b.Def != harpy || b.Unit < 0 || !preyOfHandle.TryGetValue(b.Unit, out int which)) continue;
                    hits++;
                    int by = b.Shooter >= 0 && hunterOfHandle.TryGetValue(b.Shooter, out int hb) ? hb : -1;
                    bool walking = all0.TryGetValue(preyId[which], out var was) && was.Speed > 0.05f;
                    if (all.TryGetValue(preyId[which], out var still) && still.Speed > 0.05f) walking = true;
                    if (walking) hitsOnWalkers++;
                    if (by >= 0 && lastShot[by] > 0) flights.Add(t - lastShot[by]);
                    string hp = all.TryGetValue(preyId[which], out var hurt) ? $"{hurt.Health}/{hurt.MaxHealth} owner {hurt.Player} flags {hurt.Flags}" : "gone";
                    trace.Add($"{t},struck,{by},{which},,{(walking ? "walking" : "still")} {hp}");
                }
                for (int i = 0; i < prey.Count; i++)
                {
                    if (taken[i] >= 0 || died[i]) continue;
                    if (all.TryGetValue(preyId[i], out var v)) { last[i] = v.Position; continue; }
                    uint turned = 0;
                    foreach (var kv in all)
                        if (!known.Contains(kv.Key) && kv.Value.Def == sword && kv.Value.Player == me && Flat(kv.Value.Position, last[i]) < 2f) { turned = kv.Key; break; }
                    preyOfHandle.Remove(prey[i]);
                    if (turned != 0) { taken[i] = t; known.Add(turned); trace.Add($"{t},taken,,{i},,"); }
                    else { died[i] = true; gone++; trace.Add($"{t},died,,{i},,"); }
                }
                for (int i = 0; i < hunters.Count; i++)
                {
                    if (huntId[i] != 0 && !all.ContainsKey(huntId[i])) { trace.Add($"{t},harpy-gone,{i},,,"); huntId[i] = 0; continue; }
                    if (huntId[i] != 0 && all[huntId[i]].Health < hp0[i]) { trace.Add($"{t},harpy-hurt,{i},,,{all[huntId[i]].Health} flags {all[huntId[i]].Flags} alt {all[huntId[i]].Altitude * Px:0}"); hp0[i] = all[huntId[i]].Health; }
                }
                foreach (var id in all.Keys) known.Add(id);
                if (t == 120) yield return Shoot("3-b-two-seconds-in");
                if (t % 30 == 0 && t <= 900 && !string.IsNullOrEmpty(OutDir)) { Look(home, 60f, 75f); yield return Shoot($"clip-3/{t / 30:00}"); }
            }
            Look(home, 60f, 75f);
            yield return Shoot("3-c-after-a-minute");
            if (!string.IsNullOrEmpty(OutDir)) File.WriteAllLines(Path.Combine(OutDir, "3-mind-control-trace.csv"), trace);
            var times = taken.Where(x => x > 0).ToList();
            int captured = times.Count;
            int firstShotTook = Enumerable.Range(0, prey.Count).Count(i => taken[i] > 0 && shotsBefore[i] == 1);
            float meanTicks = times.Count > 0 ? (float)times.Average() : 0f;
            var gaps = Enumerable.Range(0, prey.Count).Where(i => taken[i] > 0 && firstShot[i] > 0).Select(i => taken[i] - firstShot[i]).ToList();
            float odds = hits > 0 ? 100f * captured / hits : 0f;
            Debug.Log($"PLAYTEST mindcontrol: {prey.Count} Harpies each sent at a {engine.UnitDefs[sword].Name} {off:0} px off on average; {shots} shots, {hits} struck " +
                      $"({hitsOnWalkers} on a walker), {shots - hits} missed or were still in the air; {captured} taken, {odds:0}% of the shots that struck " +
                      $"(the original rolls 80% for a recruit); {firstShotTook} by the first shot; {captured} of {prey.Count} taken in {Ticks / 60} s, " +
                      $"from the order {meanTicks / 60f:0.00} s on average (fastest {(times.Count > 0 ? times.Min() / 60f : 0f):0.00} s), " +
                      $"from the first shot {(gaps.Count > 0 ? gaps.Average() / 60f : 0f):0.00} s; a shot flew {(flights.Count > 0 ? flights.Average() / 60f : 0f):0.00} s " +
                      $"({(flights.Count > 0 ? flights.Min() : 0)} to {(flights.Count > 0 ? flights.Max() : 0)} ticks) before it struck; {gone} died");
            Assert.Greater(hits, 0, "shots struck");
            Assert.Less(captured, hits, "not every shot that struck took its swordsman");
            Assert.Greater(meanTicks, 60f, "a capture takes more than a second from the order");
        }

        // ---- 4. Ctrl on a Zhon builder's card summons without end ----

        IEnumerator Card(string name, System.Action<Button> found)
        {
            Button card = null;
            float deadline = Time.realtimeSinceStartup + 5f;
            while (card == null && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                var builds = GameObject.Find("Builds");
                if (builds != null)
                    foreach (var x in builds.GetComponentsInChildren<Button>()) if (x.name == "Build " + name) card = x;
            }
            Assert.IsNotNull(card, "the HUD shows " + name);
            found(card);
        }

        IEnumerator PointAt(Vector3 at)
        {
            Look(at, 30f);
            for (int i = 0; i < 3; i++) yield return null;
            frame.Screen = Cam.WorldToScreenPoint(at);
            yield return null;
            yield return null;
        }

        IEnumerator LeftClick()
        {
            frame.LeftDown = frame.LeftHeld = true;
            yield return null;
            frame.LeftHeld = false;
            frame.LeftUp = true;
            yield return null;
            yield return null;
        }

        int Made(int def, int since)
        {
            int n = engine.ReadUnits(units), made = 0;
            for (int i = 0; i < n; i++)
                if (units[i].Player == engine.LocalPlayer && units[i].Def == def && units[i].BuildProgress >= 1f) made++;
            return made - since;
        }

        void Lodestones(int me)
        {
            int lode = DefNamed("ZONMANA");
            for (int k = 0; k < 4 && lode >= 0 && OkEngine.okx_place_unit(lode, me) >= 0; k++) { }
        }

        Vector3 OpenBeside(int def, int builder)
        {
            var from = Read(builder).Position;
            for (float r = 6; r <= 40; r += 2)
                for (int k = 0; k < 8; k++)
                    if (engine.CanBuildAt(def, from + Quaternion.Euler(0, k * 45f, 0) * new Vector3(r, 0, 0), 0, out var site))
                    {
                        site.y = engine.GroundHeight(site.x, site.z);
                        return site;
                    }
            Assert.Fail("no open ground for " + engine.UnitDefs[def].Name);
            return from;
        }

        [UnityTest, Timeout(1800000)]
        public IEnumerator CtrlAsZhonSummonsHarpiesWithoutEndInTheClassicScheme() => Endless(true);

        [UnityTest, Timeout(1800000)]
        public IEnumerator CtrlAsZhonSummonsHarpiesWithoutEndInTheModernScheme() => Endless(false);

        IEnumerator Endless(bool classic)
        {
            yield return Begin("ZHON");
            string scheme = classic ? "classic" : "modern";
            int me = engine.LocalPlayer;
            int tamer = DefNamed("ZONTRAIN"), harpy = DefNamed("ZONHARP");
            if (tamer < 0 || harpy < 0) Assert.Ignore("no Beast Tamer or Harpy in these game files");
            root.Orders.Classic = classic;
            Lodestones(me);
            int t = OkEngine.okx_place_unit(tamer, me);
            Assert.GreaterOrEqual(t, 0, "a Beast Tamer set down");
            engine.Advance(2);
            engine.Select(new[] { t }, false);
            root.Orders.Selected.Clear();
            root.Orders.Selected.Add(t);
            Button card = null;
            yield return Card(engine.UnitDefs[harpy].Name, b => card = b);
            BattleHud.CtrlKey = () => true;
            card.onClick.Invoke();
            BattleHud.CtrlKey = () => false;
            Assert.IsTrue(root.Orders.ArmedRepeat, "Ctrl arms a summons without end");
            var site = OpenBeside(harpy, t);
            int before = Made(harpy, 0);
            yield return PointAt(site);
            yield return LeftClick();
            Assert.IsNull(root.Orders.Armed, "placed with one click");
            engine.Advance(2);
            Assert.AreEqual(harpy, engine.RepeatOf(t), "the Beast Tamer summons Harpies without end");
            int made = 0, worst = 0, gap = 0, ticks = 0;
            bool waiting = false, shot = false;
            for (; ticks < 60 * 600 && made < 8; ticks += 5)
            {
                engine.Advance(5);
                int now = Made(harpy, before);
                if (now > made) { made = now; waiting = true; gap = 0; }
                if (classic && ticks % 800 == 0 && !string.IsNullOrEmpty(OutDir)) { Look(site, 40f, 65f); yield return Shoot($"clip-4/{ticks / 800:00}"); }
                if (!waiting) continue;
                int next = engine.ReadOrder(t).Building;
                if (next >= 0 && TryRead(next, out var f) && f.BuildProgress < 1f) waiting = false;
                else worst = Mathf.Max(worst, gap += 5);
                if (made == 4 && classic && !shot) { shot = true; Look(site, 40f, 65f); yield return Shoot("4-a-the-fourth-harpy"); }
            }
            Look(site, 40f, 65f);
            if (classic) yield return Shoot("4-b-eight-harpies-from-one-click");
            Debug.Log($"PLAYTEST ctrl-{scheme}: one Ctrl click made {made} Harpies in {ticks / 60f:0} s, the longest wait for the next frame {worst} ticks, still summoning {engine.RepeatOf(t) == harpy}");
            Assert.GreaterOrEqual(made, 8, "Harpy after Harpy from one click");
            Assert.LessOrEqual(worst, 60, "the next frame goes up as the last Harpy leaves");
            Assert.AreEqual(harpy, engine.RepeatOf(t), "and the summons goes on");
        }

        // ---- 5. A Zhon army placed without hunting for spots ----
        // The original takes a summons over the player's own units and over
        // an earlier frame, and the builder waits for the spot to clear,
        // giving up after 30 looks. A plain summons is not stepped off its
        // spot when done, so a second on the same spot waits on the first.

        [UnityTest, Timeout(1800000)]
        public IEnumerator AZhonArmyShiftPlacedOverItsOwnUnitsInTheClassicScheme() => ArmyPlaced(true);

        [UnityTest, Timeout(1800000)]
        public IEnumerator AZhonArmyShiftPlacedOverItsOwnUnitsInTheModernScheme() => ArmyPlaced(false);

        IEnumerator ShiftPlace(List<Vector3> spots, List<bool> greens)
        {
            frame.Shift = true;
            foreach (var s in spots)
            {
                yield return PointAt(s);
                greens.Add(root.Orders.GhostOk);
                yield return LeftClick();
                Assert.AreEqual(CommandKind.Build, root.Orders.Armed, "Shift keeps the placement in hand");
            }
            frame.Shift = false;
            root.Orders.Disarm();
            engine.Advance(2);
        }

        int Summonses(int builder, int def)
        {
            var legs = new OrderLeg[32];
            int n = engine.ReadOrderQueue(builder, legs);
            return legs.Take(Mathf.Max(0, n)).Count(l => l.Kind == OrderKind.Build && l.BuildDef == def);
        }

        IEnumerator ArmyPlaced(bool classic)
        {
            yield return Begin("ZHON");
            string scheme = classic ? "classic" : "modern";
            int me = engine.LocalPlayer;
            int handler = DefNamed("ZONHAND"), gob = DefNamed("ZONGOB");
            if (handler < 0 || gob < 0) Assert.Ignore("no Beast Handler or goblin in these game files");
            root.Orders.Classic = classic;
            Lodestones(me);
            int h = OkEngine.okx_place_unit(handler, me);
            Assert.GreaterOrEqual(h, 0, "a Beast Handler set down");
            engine.Advance(2);

            // A row of five touching spots, three of the player's goblins on the first two.
            var first = OpenBeside(gob, h);
            var row = new List<Vector3>();
            for (int i = 0; i < 5; i++)
            {
                var want = first + new Vector3(3f * i, 0f, 0f);
                Assert.IsTrue(engine.CanBuildAt(gob, want, 0, out var s), "spot " + i + " takes a goblin");
                row.Add(s);
            }
            var crowd = new List<int>();
            for (int i = 0; i < 3; i++)
            {
                int c = OkEngine.okx_place_unit(gob, me);
                Assert.GreaterOrEqual(c, 0);
                engine.Command(GameCommand.To(CommandKind.Move, c, row[i % 2]));
                crowd.Add(c);
            }
            engine.Advance(600);
            int onRow = crowd.Count(c => row.Take(2).Any(s => Flat(Read(c).Position, s) * Px < 32f));
            engine.Select(new[] { h }, false);
            root.Orders.Selected.Clear();
            root.Orders.Selected.Add(h);
            Button card = null;
            yield return Card(engine.UnitDefs[gob].Name, b => card = b);
            card.onClick.Invoke();
            Assert.AreEqual(CommandKind.Build, root.Orders.Armed);
            yield return PointAt(row[0]);
            yield return Shoot($"5-{scheme}-a-the-ghost-over-the-goblins");
            int before = Made(gob, 0);
            var greens = new List<bool>();
            yield return ShiftPlace(row, greens);
            int queued = Summonses(h, gob);
            // Five seconds on, the player sends the goblins off the row.
            engine.Advance(300);
            int madeWhileHeld = Made(gob, before);
            foreach (var c in crowd) engine.Command(GameCommand.To(CommandKind.Move, c, first + new Vector3(0f, 0f, 14f)));
            int made = madeWhileHeld, ticks = 300;
            for (; ticks < 60 * 300 && made < row.Count; ticks += 10)
            {
                engine.Advance(10);
                made = Made(gob, before);
            }
            Look(row[2], 36f, 65f);
            yield return Shoot($"5-{scheme}-b-the-row");
            string rowLine = $"{onRow} goblins on the first two of five touching spots, {greens.Count(g => g)} of {row.Count} Shift clicks green, " +
                             $"{queued} queued, {madeWhileHeld} up while the goblins stood there, {made} of {row.Count} summoned in {ticks / 60f:0} s once they walked off";

            // Three on the very spot: the first comes, and the next two wait on it.
            var again = OpenBeside(gob, h);
            int before2 = Made(gob, 0);
            card = null;
            yield return Card(engine.UnitDefs[gob].Name, b => card = b);
            card.onClick.Invoke();
            var same = new List<Vector3> { again, again, again };
            var greens2 = new List<bool>();
            yield return ShiftPlace(same, greens2);
            int queued2 = Summonses(h, gob);
            int made2 = 0, t2 = 0;
            for (; t2 < 60 * 60 && Summonses(h, gob) > 0; t2 += 10)
            {
                engine.Advance(10);
                made2 = Made(gob, before2);
            }
            made2 = Made(gob, before2);
            Debug.Log($"PLAYTEST army-{scheme}: {rowLine}; three on one spot: {greens2.Count(g => g)} of 3 green, {queued2} queued, {made2} summoned, " +
                      $"the rest given up after {t2 / 60f:0} s");
            Assert.AreEqual(row.Count, greens.Count(g => g), "the ghost is green over the player's own goblins and the frames before it");
            Assert.AreEqual(row.Count, made, "every goblin of the row was summoned");
            Assert.AreEqual(3, greens2.Count(g => g), "and on a frame in hand");
        }
    }
}
