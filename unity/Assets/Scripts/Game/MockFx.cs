// MockFx.cs - the mock's shots and effects, one weapon per family the engine
// fires, with the contract's fields filled as the engine will fill them, and
// the effects reference stage's scenes.
using System.Collections.Generic;
using UnityEngine;

namespace OpenKingdomsUnity.Game
{
    public sealed partial class MockBackend
    {
        public enum FxDraw { Model, Picture, Dot, Beam, Flame, Ring, Rain, Wander }
        public enum FxMotion { Arc, Straight, Homing, Drop }

        public sealed class FxWeapon
        {
            public string Name;
            public FxDraw Draw;
            public string Art, Impact;          // strip or model, and the impact's strip
            public FxMotion Motion = FxMotion.Arc;
            public float Speed = 16f;           // units a second
            public FxLight Light = FxLight.None;
            public bool Shadow, Nimbus;
            public BeamKind Beam;
            public int Emit = 30;               // ticks a beam or a breath holds
            public Vector2 Spin;                // a model's tumble and turn, degrees a tick
            public int Rings = 3, RingSprites = 16;
            public float Radius = 5f;
            public float RainPerSecond, RainSeconds;
        }

        static FxWeapon W(string name, FxDraw draw, string art, string impact = null, FxMotion motion = FxMotion.Arc, float speed = 16f,
            FxLight light = FxLight.None, bool shadow = false, bool nimbus = false) =>
            new FxWeapon { Name = name, Draw = draw, Art = art, Impact = impact, Motion = motion, Speed = speed, Light = light, Shadow = shadow, Nimbus = nimbus };

        static FxWeapon Beam(string name, BeamKind kind, int emit, string impact = null, FxLight light = FxLight.Small, bool nimbus = false) =>
            new FxWeapon { Name = name, Draw = FxDraw.Beam, Beam = kind, Emit = emit, Impact = impact, Light = light, Nimbus = nimbus };

        // The families of the Iron Plague weapons, one line per look.
        public static readonly FxWeapon[] FxWeapons =
        {
            Beam("ARAKING 1", BeamKind.Lightning, 15, "lightning1"),
            new FxWeapon { Name = "ARAKING 2", Draw = FxDraw.Model, Art = "mockmeteor", Impact = "explodeb", Motion = FxMotion.Straight, Speed = 14f, Spin = new Vector2(10f, 3f), Nimbus = true },
            new FxWeapon { Name = "ARAKING 3", Draw = FxDraw.Ring, Art = "ring_fx_red", Rings = 3, RingSprites = 20, Radius = 6f, Nimbus = true },
            W("TARNECRO 1", FxDraw.Picture, "FireballA", "flamestrike", FxMotion.Straight, 14f),
            W("TARNECRO 2", FxDraw.Picture, "FireballB", "VBlast", FxMotion.Homing, 12f),
            new FxWeapon { Name = "TARNECRO 3", Draw = FxDraw.Ring, Art = "ring_fx_white", Rings = 3, RingSprites = 16, Radius = 6f, Nimbus = true },
            W("VERMAGE 1", FxDraw.Dot, null, "WaterBallExplode", FxMotion.Straight, 14f, FxLight.Large, nimbus: true),
            W("VERMAGE 2", FxDraw.Picture, "WaterBall", "WaterBallExplode", FxMotion.Homing, 12f),
            Beam("ZONHUNT 1", BeamKind.Lightning, 10, "lightning1", nimbus: true),
            W("ZONHUNT 2", FxDraw.Picture, "LtngBall_1a", "blue_shockring", FxMotion.Arc, 12f, FxLight.Small),
            Beam("TARPRIES 1", BeamKind.Lightning, 45, "lightning1"),
            W("TARPRIES 2", FxDraw.Picture, "LtngBall_2a", "blue_shockring", FxMotion.Straight, 14f),
            W("TARPRIES 3", FxDraw.Picture, "FireballCa", "explodeb", FxMotion.Homing, 12f),
            W("VERDRAG 2", FxDraw.Picture, "TsunamiSprite", "TsunamiExplode", FxMotion.Homing, 12f, FxLight.Large),
            Beam("CRECHIE 1", BeamKind.CreonLightning, 100, light: FxLight.None),
            W("CRECHIE 2", FxDraw.Picture, "iceburst", "lightning1", FxMotion.Straight, 14f, FxLight.Small),
            Beam("CRECHIE 3", BeamKind.CreonParalyzer, 50, light: FxLight.None),
            Beam("CREPRIS 1", BeamKind.CreonLightbeam, 80, light: FxLight.Large),
            Beam("ZONSHAM 1", BeamKind.Lightning, 34, "lightning1"),
            W("ARABOW 1", FxDraw.Model, "mockarrow", null, FxMotion.Arc, 20f),
            W("ARABOW 2", FxDraw.Model, "mockarrow", "teeny", FxMotion.Homing, 18f, nimbus: true),
            W("TARARCH 1", FxDraw.Model, "mockarrow", "teeny", FxMotion.Arc, 20f),
            W("VERARCH 1", FxDraw.Model, "mockbolt", null, FxMotion.Straight, 24f),
            W("CREGATL 1", FxDraw.Model, "mockarrow", "DirtClodMed", FxMotion.Straight, 24f),
            W("VERKNIGH 1", FxDraw.Model, "mockspear", "DirtClodMed", FxMotion.Arc, 16f),
            W("ZONTER 1", FxDraw.Model, "mockspear", null, FxMotion.Arc, 16f),
            new FxWeapon { Name = "ZONGIANT 1", Draw = FxDraw.Model, Art = "mockstone", Impact = "dustlg", Motion = FxMotion.Arc, Speed = 12f, Spin = new Vector2(12f, 5f) },
            W("ARACAN 1", FxDraw.Picture, "cannbmed", "large", FxMotion.Arc, 18f, shadow: true),
            W("VERMUSK 1", FxDraw.Picture, "cannbsm", "teeny", FxMotion.Straight, 22f, shadow: true),
            W("ARAPULT 1", FxDraw.Picture, "cannblg", "dustlg", FxMotion.Arc, 10f, shadow: true),
            W("VERPULT 1", FxDraw.Picture, "cannbmed", "dustlg", FxMotion.Arc, 10f, shadow: true),
            new FxWeapon { Name = "TARMAGE 3", Draw = FxDraw.Rain, Art = "meteor", Impact = "explodeb", RainPerSecond = 5f, RainSeconds = 3f, Radius = 4f, Nimbus = true },
            new FxWeapon { Name = "ARAPRIES 2", Draw = FxDraw.Rain, Art = "iceballspin", Impact = "iceballexp", RainPerSecond = 5f, RainSeconds = 2.5f, Radius = 4f, Nimbus = true },
            W("ARAPRIES 3", FxDraw.Picture, "FireballA", "blue_shockring", FxMotion.Straight, 14f, FxLight.Medium, nimbus: true),
            new FxWeapon { Name = "ARADRAG 1", Draw = FxDraw.Flame, Art = "flame", Beam = BeamKind.Fire, Emit = 45 },
            W("ARADRAG 2", FxDraw.Picture, "FireballB", "explodeb", FxMotion.Homing, 12f, FxLight.Small),
            new FxWeapon { Name = "TARKNIGH 1", Draw = FxDraw.Flame, Art = "flame", Beam = BeamKind.Fire, Emit = 30 },
            W("TARHEL 1", FxDraw.Picture, "FireballA", "flamestrike", FxMotion.Straight, 14f),
            W("TARDRAG 2", FxDraw.Picture, "FireballB", "explodeb", FxMotion.Homing, 12f, FxLight.Small, shadow: true),
            W("TARMIND 1", FxDraw.Picture, "FireballD", "Lodeexplode", FxMotion.Straight, 12f, FxLight.Small),
            new FxWeapon { Name = "TARWITCH 1", Draw = FxDraw.Wander, Art = "tornadoloop", Emit = 270, Nimbus = true },
            W("ZONSPIDE 1", FxDraw.Dot, null, "green_shockring", FxMotion.Straight, 16f),
            // The mock's own fighters and spells.
            W("MOCK ARROW", FxDraw.Model, "mockarrow", null, FxMotion.Arc, 18f),
            W("MOCK FIREBALL", FxDraw.Picture, "FireballA", "teeny", FxMotion.Arc, 18f),
            W("MOCK FIREBALL SPELL", FxDraw.Picture, "FireballB", "explodeb", FxMotion.Homing, 12f, FxLight.Small, nimbus: true),
            W("MOCK FROST SPELL", FxDraw.Picture, "iceburst", "iceballexp", FxMotion.Straight, 14f, FxLight.Small, nimbus: true),
        };

        public static FxWeapon FxWeaponNamed(string name)
        {
            foreach (var w in FxWeapons) if (w != null && w.Name == name) return w;
            return null;
        }

        // The reference stage's rows: each shooter, its weapon and how far off its target stands (engine pixels).
        public static readonly Dictionary<string, (int spacing, (string weapon, int dist, bool ground)[] pairs)> FxScenes =
            new Dictionary<string, (int, (string, int, bool)[])>
        {
            ["magic"] = (190, new[] { ("ARAKING 1", 180, false), ("ARAKING 2", 280, false), ("TARNECRO 1", 280, false), ("TARNECRO 2", 280, false), ("VERMAGE 2", 300, false), ("ZONHUNT 2", 300, false) }),
            ["magic2"] = (190, new[] { ("TARPRIES 2", 300, false), ("TARPRIES 3", 300, false), ("VERDRAG 2", 320, false), ("CRECHIE 1", 300, false), ("ZONSHAM 1", 250, false), ("ARABOW 2", 350, false) }),
            ["ranged"] = (190, new[] { ("ARABOW 1", 350, false), ("VERKNIGH 1", 250, false), ("ZONGIANT 1", 350, false), ("ARACAN 1", 350, false), ("VERMUSK 1", 350, false), ("ARAPULT 1", 450, false) }),
            ["ground"] = (230, new[] { ("ARAPULT 1", 450, true), ("VERPULT 1", 450, true), ("ARACAN 1", 350, true), ("TARMAGE 3", 300, true), ("ARAPRIES 2", 300, true) }),
            ["fire"] = (260, new[] { ("ARADRAG 1", 250, false), ("ARADRAG 2", 350, false), ("TARKNIGH 1", 250, false), ("TARHEL 1", 300, false) }),
            ["rings"] = (700, new[] { ("ARAKING 3", 100, false), ("TARNECRO 3", 100, false) }),
            ["spells"] = (200, new[] { ("TARWITCH 1", 250, false), ("CREPRIS 1", 250, false), ("TARMIND 1", 250, false), ("ARAPRIES 3", 250, false), ("TARPRIES 1", 220, false), ("CRECHIE 2", 250, false) }),
            ["bolts"] = (190, new[] { ("TARARCH 1", 350, false), ("VERARCH 1", 350, false), ("CREGATL 1", 350, false), ("ZONTER 1", 250, false), ("TARDRAG 2", 350, false), ("ZONHUNT 1", 200, false) }),
        };

        // ── State ─────────────────────────────────────────────────────

        sealed class FxShot
        {
            public int Id, Player, Colour, Shooter, Target, Damage = -1;
            public FxWeapon W;
            public Vector3 Pos, Vel, Aim, Source;
            public int Age, Seed;
            public float Tumble, Turn;
        }

        sealed class FxBlast
        {
            public int Id, Strip, Follow = -1, Struck = -1, Delay, Life, Age;
            public Vector3 Pos, Vel;
            public bool Loops, Hug;     // Hug keeps a mover on the ground
            public FxLight Light;
            public float StopY = float.NegativeInfinity;
            public string OnLand;
            public Vector3 Circle;      // a wanderer's centre, and its radius in y
        }

        sealed class FxStrip
        {
            public MockFxArt.Spec Spec;
            public RgbaImage Image;
            public EffectFrame[] Frames;
        }

        sealed class FxRepeat
        {
            public FxWeapon W;
            public int Shooter, Target;
            public Vector3 From, To;
            public int Every, Next;
        }

        readonly List<FxShot> fxShots = new List<FxShot>();
        readonly List<FxBlast> fxBlasts = new List<FxBlast>();
        readonly List<FxStrip> fxStrips = new List<FxStrip>();
        readonly Dictionary<string, int> fxStripIds = new Dictionary<string, int>();
        readonly List<FxRepeat> fxRepeats = new List<FxRepeat>();
        readonly HashSet<int> staged = new HashSet<int>();
        int nextBlast = 1;

        public int FxBlastCount => fxBlasts.Count;
        public int FxShotCount => fxShots.Count;

        void ClearFx()
        {
            fxShots.Clear();
            fxBlasts.Clear();
            fxRepeats.Clear();
            staged.Clear();
        }

        // ── Firing ────────────────────────────────────────────────────

        // Fires a weapon from one point to another, from and at units when
        // their handles are given, as a look only: nothing is hurt.
        public void FireFx(string weapon, Vector3 from, Vector3 to, int shooter = -1, int target = -1) =>
            FireFx(FxWeaponNamed(weapon), from, to, shooter, target, -1);

        void FireFx(FxWeapon w, Vector3 from, Vector3 to, int shooter, int target, int damage)
        {
            if (w == null || Terrain == null) return;
            int player = shooter >= 0 && byHandle.TryGetValue(shooter, out var su) ? su.Player : 0;
            int colour = player < players.Count ? players[player].Colour : 0;
            if (w.Nimbus && shooter >= 0 && byHandle.TryGetValue(shooter, out var caster))
            {
                string side = player < players.Count ? (players[player].Side ?? "aramon").ToLowerInvariant() : "aramon";
                int strip = FxStripId("nimbus_" + side);
                if (strip < 0) strip = FxStripId("nimbus_aramon");
                SpawnBlast(strip, caster.Pos + Vector3.up * 0.8f, follow: caster.Handle, light: FxLight.None);
            }
            switch (w.Draw)
            {
                case FxDraw.Ring:
                {
                    int strip = FxStripId(w.Art);
                    int dur = 30;
                    for (int k = 0; k < w.Rings; k++)
                        for (int i = 0; i < w.RingSprites; i++)
                        {
                            float a = 2f * Mathf.PI * (i + 0.5f * (k % 2)) / w.RingSprites;
                            var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                            var b = SpawnBlast(strip, GroundAt(to), loops: true);
                            b.Vel = dir * (w.Radius / dur);
                            b.Hug = true;
                            b.Delay = 6 + k * 8;
                            b.Life = dur;
                        }
                    return;
                }
                case FxDraw.Rain:
                {
                    int strip = FxStripId(w.Art);
                    int n = Mathf.Max(1, Mathf.RoundToInt(w.RainPerSecond * w.RainSeconds));
                    for (int j = 0; j < n; j++)
                    {
                        float a = Hash01(j * 31 + 7) * 2f * Mathf.PI, r = w.Radius * Mathf.Sqrt(Hash01(j * 17 + 3));
                        var at = to + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                        float g = GroundHeight(at.x, at.z);
                        var b = SpawnBlast(strip, new Vector3(at.x, g + 25f, at.z), loops: true);
                        b.Vel = Vector3.down * (25f / 18f);
                        b.StopY = g;
                        b.OnLand = w.Impact;
                        b.Delay = 6 + Mathf.RoundToInt(j * w.RainSeconds * Tps / n);
                    }
                    return;
                }
                case FxDraw.Wander:
                {
                    var b = SpawnBlast(FxStripId(w.Art), GroundAt(to), loops: true);
                    b.Life = w.Emit;
                    b.Circle = new Vector3(to.x, 2.5f, to.z);
                    return;
                }
            }
            var shot = new FxShot
            {
                Id = nextArrow++, W = w, Player = IdOf(player), Colour = colour, Shooter = shooter, Target = target, Damage = damage,
                Pos = from, Aim = to, Source = from, Seed = nextArrow * 7919,
            };
            var d = to - from;
            float time = Mathf.Max(0.2f, d.magnitude / Mathf.Max(1f, w.Speed));
            switch (w.Motion)
            {
                case FxMotion.Arc: shot.Vel = d / time + Vector3.up * 0.5f * 9.8f * time; break;
                case FxMotion.Drop: shot.Pos = to + Vector3.up * 8f; shot.Vel = Vector3.zero; break;
                default: shot.Vel = d.normalized * w.Speed; break;
            }
            fxShots.Add(shot);
            if (w.Draw == FxDraw.Beam && w.Impact != null) SpawnBlast(FxStripId(w.Impact), GroundAt(to), struck: target, light: w.Light);
        }

        // A row of shooters and targets at the map's middle, as the engine's
        // reference stage builds it, firing each weapon now and every few
        // seconds. The units stand still and nobody is hurt.
        public bool StageFx(string scene, float everySeconds = 3f)
        {
            if (!FxScenes.TryGetValue(scene, out var sc) || Terrain == null) return false;
            fxRepeats.Clear();
            // The last scene's row leaves the stage.
            foreach (int h in staged)
                if (byHandle.TryGetValue(h, out var old)) { units.Remove(old); byHandle.Remove(h); }
            staged.Clear();
            var centre = new Vector2(Terrain.Size.x * 0.5f, -Terrain.Size.y * 0.5f);
            int n = sc.pairs.Length;
            for (int i = 0; i < n; i++)
            {
                var (weapon, dist, onGround) = sc.pairs[i];
                float x = centre.x + ((i - (n - 1) * 0.5f) * sc.spacing) / 16f;
                var from = new Vector2(x, centre.y - dist / 32f);
                var to = new Vector2(x, centre.y + dist / 32f);
                var shooter = Spawn(DefFor(Role.Mage), 0, from);
                shooter.Heading = 0f;
                staged.Add(shooter.Handle);
                int target = -1;
                if (!onGround && players.Count > 1)
                {
                    var t = Spawn(DefFor(Role.Knight), 1, to);
                    t.Heading = 180f;
                    staged.Add(t.Handle);
                    target = t.Handle;
                }
                fxRepeats.Add(new FxRepeat
                {
                    W = FxWeaponNamed(weapon), Shooter = shooter.Handle, Target = target,
                    From = new Vector3(from.x, 0f, from.y), To = new Vector3(to.x, 0f, to.y),
                    Every = Mathf.Max(1, Mathf.RoundToInt(everySeconds * Tps)), Next = (int)Tick + 2,
                });
            }
            return true;
        }

        public Vector3 StageCentre => Terrain == null ? Vector3.zero :
            new Vector3(Terrain.Size.x * 0.5f, GroundHeight(Terrain.Size.x * 0.5f, -Terrain.Size.y * 0.5f), -Terrain.Size.y * 0.5f);

        int DefFor(Role role)
        {
            for (int d = 0; d < unitDefs.Count; d++) if (RoleOf(d) == role && !unitDefs[d].IsBuilding) return d;
            return DefKnight;
        }

        Vector3 BodyOf(int handle, Vector3 fallback)
        {
            if (handle >= 0 && byHandle.TryGetValue(handle, out var u)) return u.Pos + Vector3.up * (1f + u.Alt);
            return GroundAt(fallback) + Vector3.up * 0.8f;
        }

        Vector3 GroundAt(Vector3 p) => new Vector3(p.x, GroundHeight(p.x, p.z), p.z);

        static float Hash01(int n)
        {
            uint h = (uint)n * 2654435761u;
            h ^= h >> 15; h *= 0x2c1b3c6du; h ^= h >> 12;
            return (h & 0xffffu) / 65535f;
        }

        // ── Stepping ──────────────────────────────────────────────────

        void StepFx(float dt)
        {
            foreach (var r in fxRepeats)
            {
                if (Tick < r.Next || r.W == null) continue;
                r.Next = (int)Tick + r.Every;
                var from = BodyOf(r.Shooter, r.From);
                var to = r.Target >= 0 ? BodyOf(r.Target, r.To) : GroundAt(r.To);
                FireFx(r.W, from, to, r.Shooter, r.Target, -1);
            }

            for (int i = fxShots.Count - 1; i >= 0; i--)
            {
                var s = fxShots[i];
                s.Age++;
                var w = s.W;
                if (s.Shooter >= 0 && byHandle.TryGetValue(s.Shooter, out var su)) s.Source = su.Pos + Vector3.up * 1f;
                if (s.Target >= 0 && byHandle.TryGetValue(s.Target, out var tu) && !tu.Dying) s.Aim = tu.Pos + Vector3.up * 0.8f;
                if (w.Draw == FxDraw.Beam || w.Draw == FxDraw.Flame)
                {
                    s.Pos = s.Aim;
                    if (w.Draw == FxDraw.Flame) Breathe(s);
                    if (s.Age >= w.Emit) fxShots.RemoveAt(i);
                    continue;
                }
                if (w.Motion == FxMotion.Homing)
                {
                    var want = (s.Aim - s.Pos).normalized * w.Speed;
                    s.Vel = Vector3.RotateTowards(s.Vel, want, 4f * dt, w.Speed * dt * 4f);
                }
                else if (w.Motion == FxMotion.Arc || w.Motion == FxMotion.Drop) s.Vel += Vector3.down * 9.8f * dt;
                var step = s.Vel * dt;
                s.Tumble += w.Spin.x;
                s.Turn += w.Spin.y;
                bool hit = (s.Aim - s.Pos).sqrMagnitude <= Mathf.Max(0.36f, step.sqrMagnitude * 1.1f);
                s.Pos += step;
                float ground = GroundHeight(s.Pos.x, s.Pos.z);
                if (!hit && s.Pos.y > ground && s.Age < Tps * 8) continue;
                var at = hit ? s.Aim : new Vector3(s.Pos.x, ground, s.Pos.z);
                if (s.Damage > 0 && s.Target >= 0 && byHandle.TryGetValue(s.Target, out var victim) && hit) Hurt(victim, s.Damage, s.Shooter);
                if (w.Impact != null) SpawnBlast(FxStripId(w.Impact), GroundAt(at), struck: hit ? s.Target : -1, light: w.Light);
                fxShots.RemoveAt(i);
            }

            for (int i = fxBlasts.Count - 1; i >= 0; i--)
            {
                var b = fxBlasts[i];
                if (b.Delay > 0) { b.Delay--; continue; }
                b.Age++;
                if (b.Circle.y > 0f)
                {
                    float a = b.Age * 0.035f;
                    b.Pos = new Vector3(b.Circle.x + Mathf.Cos(a) * b.Circle.y, 0f, b.Circle.z + Mathf.Sin(a * 1.3f) * b.Circle.y);
                    b.Pos.y = GroundHeight(b.Pos.x, b.Pos.z);
                }
                else b.Pos += b.Vel;
                if (b.Hug) b.Pos.y = GroundHeight(b.Pos.x, b.Pos.z);
                if (b.Pos.y <= b.StopY)
                {
                    if (b.OnLand != null) SpawnBlast(FxStripId(b.OnLand), new Vector3(b.Pos.x, b.StopY, b.Pos.z));
                    fxBlasts.RemoveAt(i);
                    continue;
                }
                var strip = fxStrips[b.Strip];
                int life = b.Life > 0 ? b.Life : strip.Spec.Frames * FrameTicksOf(strip);
                if (!b.Loops && b.Age >= life || b.Loops && b.Life > 0 && b.Age >= b.Life) fxBlasts.RemoveAt(i);
            }
        }

        // A breath's flame each tick from its muzzle toward the target.
        void Breathe(FxShot s)
        {
            var d = s.Aim - s.Source;
            float len = d.magnitude;
            if (len < 0.5f) return;
            const float speed = 1f;
            float jitter = 1f + (Hash01(s.Seed + s.Age) - 0.5f) * 0.2f;
            var b = SpawnBlast(FxStripId(s.W.Art), s.Source, loops: true);
            b.Vel = d / len * speed * jitter;
            b.Life = Mathf.CeilToInt(len / speed);
        }

        FxBlast SpawnBlast(int strip, Vector3 at, bool loops = false, int follow = -1, int struck = -1, FxLight light = FxLight.Auto)
        {
            var b = new FxBlast { Id = nextBlast++, Strip = strip, Pos = at, Loops = loops, Follow = follow, Struck = struck, Light = light };
            if (strip >= 0) fxBlasts.Add(b);
            return b;
        }

        int FrameTicksOf(FxStrip s) => World.FxLook.FrameTicks(s.Spec.Duration, Tps);

        // ── The contract ──────────────────────────────────────────────

        int FxStripId(string name)
        {
            if (name == null) return -1;
            if (fxStripIds.TryGetValue(name, out int id)) return id;
            var spec = MockFxArt.Find(name);
            if (spec == null) { fxStripIds[name] = -1; return -1; }
            var s = new FxStrip { Spec = spec, Image = MockFxArt.Draw(spec), Frames = new EffectFrame[spec.Frames] };
            int ticks = World.FxLook.FrameTicks(spec.Duration, Tps);
            for (int f = 0; f < spec.Frames; f++)
            {
                float k = spec.Grows ? Mathf.Lerp(0.2f, 1f, Mathf.Clamp01(f / (spec.Frames * 0.3f))) : 1f;
                float w = spec.W / 16f * k, h = spec.H / 16f * k;
                s.Frames[f] = new EffectFrame
                {
                    Top = spec.AnchorY * h, Bottom = spec.AnchorY * h - h, OffsetX = spec.AnchorX * w, Width = w,
                    UvMin = new Vector2(f / (float)spec.Frames, 0f), UvMax = new Vector2((f + 1) / (float)spec.Frames, 1f),
                    Ticks = ticks, Additive = spec.Additive,
                };
            }
            fxStrips.Add(s);
            fxStripIds[name] = fxStrips.Count - 1;
            return fxStrips.Count - 1;
        }

        public RgbaImage EffectStrip(int strip) => strip >= 0 && strip < fxStrips.Count ? fxStrips[strip].Image : null;

        public EffectFrame[] EffectFrames(int strip) => strip >= 0 && strip < fxStrips.Count ? fxStrips[strip].Frames : null;

        // The strips of every weapon the mock fires, and their blasts, and
        // the builders' sparkles.
        public IReadOnlyList<int> WarmEffectStrips()
        {
            var ids = new List<int>();
            foreach (var w in FxWeapons)
            {
                if (w == null) continue;
                foreach (var name in new[] { w.Draw == FxDraw.Model ? null : w.Art, w.Impact })
                {
                    int id = FxStripId(name);
                    if (id >= 0 && !ids.Contains(id)) ids.Add(id);
                }
            }
            foreach (var p in players)
            {
                int id = FxStripId(BuildStrip(p.Side));
                if (id >= 0 && !ids.Contains(id)) ids.Add(id);
            }
            return ids;
        }

        static string BuildStrip(string side) => (string.IsNullOrEmpty(side) ? "aramon" : side.ToLowerInvariant()) + "build";

        // A builder raising a building throws its kingdom's sparkle over the
        // site, a still picture the engine reports with no light of its own.
        void BuildSparkle(Unit u)
        {
            if (Tick % 5 != 0 || !(u.BuildAt is Vector3 site)) return;
            int pos = PosOf(u.Player);
            int strip = FxStripId(BuildStrip(pos >= 0 ? players[pos].Side : null));
            if (strip < 0) strip = FxStripId(BuildStrip(null));
            float a = Hash01((int)Tick * 31 + u.Handle) * 2f * Mathf.PI, r = Hash01((int)Tick * 17 + u.Handle) * 1.2f;
            SpawnBlast(strip, GroundAt(site + new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r)) + Vector3.up * 0.4f);
        }

        // Shows one picture of the mock's art standing at a point for a
        // number of ticks, for tests of how art lands on the screen.
        public bool ShowFx(string art, Vector3 at, int ticks)
        {
            int strip = FxStripId(art);
            if (strip < 0) return false;
            var b = SpawnBlast(strip, at, loops: true, light: FxLight.None);
            b.Life = ticks;
            return true;
        }

        public int ReadEffects(EffectState[] into)
        {
            int n = 0;
            foreach (var b in fxBlasts)
            {
                if (b.Delay > 0) continue;
                var s = fxStrips[b.Strip];
                int tpf = FrameTicksOf(s), nf = s.Frames.Length;
                int frame = b.Age / tpf;
                if (frame >= nf) { if (!b.Loops) continue; frame %= nf; }
                var pos = b.Pos;
                if (b.Follow >= 0 && byHandle.TryGetValue(b.Follow, out var u)) pos = u.Pos + Vector3.up * (0.8f + u.Alt);
                if (n < (into?.Length ?? 0)) into[n] = Effect(b.Id, b.Strip, false, pos, frame, (b.Age % tpf) / (float)tpf, b.Loops, b.Light, b.Follow, b.Struck, b.Age);
                n++;
            }
            foreach (var shot in fxShots)
            {
                if (shot.W.Draw != FxDraw.Picture) continue;
                int strip = FxStripId(shot.W.Art);
                if (strip < 0) continue;
                var s = fxStrips[strip];
                int tpf = FrameTicksOf(s);
                if (n < (into?.Length ?? 0)) into[n] = Effect(-1 - shot.Id, strip, true, shot.Pos, (shot.Age / tpf) % s.Frames.Length, (shot.Age % tpf) / (float)tpf, true, shot.W.Light, -1, -1, shot.Age);
                n++;
            }
            foreach (var a in arrows)
            {
                if (a.Look == null || a.Look.Draw != FxDraw.Picture) continue;
                int strip = FxStripId(a.Look.Art);
                if (strip < 0) continue;
                var s = fxStrips[strip];
                int tpf = FrameTicksOf(s), age = (int)(Tick - a.Born);
                if (n < (into?.Length ?? 0)) into[n] = Effect(-1 - a.Id, strip, true, a.Pos, (age / tpf) % s.Frames.Length, (age % tpf) / (float)tpf, true, FxLight.Auto, -1, -1, age);
                n++;
            }
            return n;
        }

        EffectState Effect(int id, int strip, bool shot, Vector3 at, int frame, float phase, bool loops, FxLight light, int follow, int struck, int age)
        {
            var f = fxStrips[strip].Frames[frame];
            return new EffectState
            {
                Id = id, Strip = strip, IsProjectile = shot, Position = at,
                Top = f.Top, Bottom = f.Bottom, OffsetX = f.OffsetX, Width = f.Width, UvMin = f.UvMin, UvMax = f.UvMax,
                Frame = frame, Phase = phase, Loops = loops, Additive = f.Additive, Light = light,
                Follow = follow, Struck = struck, Age = age,
            };
        }

        public int ReadProjectiles(ProjectileState[] into)
        {
            int n = 0, cap = into?.Length ?? 0;
            foreach (var a in arrows)
            {
                var w = a.Look ?? FxWeaponNamed("MOCK ARROW");
                if (n < cap) into[n] = Shot(a.Id, IdOf(a.Player), a.Player < players.Count ? players[a.Player].Colour : 0, w, a.Pos, a.Vel, a.Pos, (int)(Tick - a.Born), a.Id * 7919, 0f, 0f);
                n++;
            }
            foreach (var s in fxShots)
            {
                if (n < cap) into[n] = Shot(s.Id, s.Player, s.Colour, s.W, s.Pos, s.Vel, s.Source, s.Age, s.Seed, s.Tumble, s.Turn);
                n++;
            }
            return n;
        }

        ProjectileState Shot(int id, int player, int colour, FxWeapon w, Vector3 pos, Vector3 vel, Vector3 source, int age, int seed, float tumble, float turn)
        {
            var p = new ProjectileState
            {
                Id = id, Player = player, Colour = colour, Position = pos, Velocity = vel, Model = -1,
                Source = source, Age = age, Seed = seed, Light = w.Light, Shadow = w.Shadow,
            };
            switch (w.Draw)
            {
                case FxDraw.Model:
                    p.Kind = ShotKind.Model;
                    p.Model = LoadModel(w.Art, colour);
                    break;
                case FxDraw.Picture: p.Kind = ShotKind.Picture; break;
                case FxDraw.Beam:
                case FxDraw.Flame:
                    p.Kind = ShotKind.Beam;
                    p.Beam = w.Draw == FxDraw.Flame ? BeamKind.Fire : w.Beam;
                    p.Life = w.Emit;
                    World.FxLook.BeamColours(p.Beam, out p.BeamInner, out p.BeamMiddle, out p.BeamOuter);
                    break;
                default: p.Kind = ShotKind.Dot; break;
            }
            var dir = vel.sqrMagnitude > 1e-6f ? vel.normalized : Vector3.forward;
            p.Heading = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg + turn;
            p.Pitch = -Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) * Mathf.Rad2Deg + tumble;
            return p;
        }

        public int ReadProjectilePose(int id, PiecePose[] into)
        {
            ProjectileState s = default;
            bool found = false;
            foreach (var a in arrows)
                if (a.Id == id) { s = Shot(a.Id, 0, 0, a.Look ?? FxWeaponNamed("MOCK ARROW"), a.Pos, a.Vel, a.Pos, 0, 0, 0f, 0f); found = true; break; }
            if (!found)
                foreach (var f in fxShots)
                    if (f.Id == id) { s = Shot(f.Id, f.Player, f.Colour, f.W, f.Pos, f.Vel, f.Source, f.Age, f.Seed, f.Tumble, f.Turn); found = true; break; }
            if (!found || s.Kind != ShotKind.Model || s.Model < 0) return 0;
            int n = PoseModel(s.Model, "idle", 0f, into);
            var world = Matrix4x4.TRS(s.Position, Quaternion.Euler(s.Pitch, s.Heading, s.Roll), Vector3.one);
            for (int i = 0; i < n; i++) into[i].Matrix = world * into[i].Matrix;
            return n;
        }
    }
}
