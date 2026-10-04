// ScarTests.cs - what each kind of blast leaves on the ground: a cannon's
// crater about 6 pixels deep with a rim, frost with no crater, the deepest
// dips for the largest spells and overlapping craters keeping the deeper.
using NUnit.Framework;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.World;
using UnityEngine;

namespace OpenKingdomsUnity.Tests
{
    public class ScarTests
    {
        const float Px = ScarStamps.PixelsPerUnit;

        static WeaponInfo Gun(string name, string type, string subtype, string damageKind, string explosion, int areaPx,
            WeaponFlags flags = WeaponFlags.None) =>
            new WeaponInfo { Name = name, Type = type, Subtype = subtype, DamageKind = damageKind, ExplosionClass = explosion, AreaOfEffect = areaPx / Px, Flags = flags };

        // The mock's weapons, as MockDestruction describes them.
        static readonly WeaponInfo Cannon = Gun("ARACAN 1", "ballistic", "", "explosion", "large explosion", 90);
        static readonly WeaponInfo Catapult = Gun("ARAPULT 1", "ballistic", "", "explosion", "large dust puff", 100);
        static readonly WeaponInfo Hail = Gun("ARAPRIES 2", "remote effect", "hailstorm", "", "iceballexp", 200, WeaponFlags.Spell);

        static BlastEvent Blast(WeaponInfo w, Vector3 at, BlastFlags flags = BlastFlags.None, Vector3 dir = default) =>
            new BlastEvent { Id = 1, Weapon = w, Position = at, Radius = w.AreaOfEffect * 0.5f, Direction = dir, Flags = flags, Unit = -1, Feature = -1 };

        static ScarStamp StampOf(WeaponInfo w, Vector3 at)
        {
            Assert.IsTrue(ScarStamps.Make(Blast(w, at), at.y, out var s), w.Name + " leaves a mark");
            return s;
        }

        // A 64 unit map at a texel for every 4 pixels, as High keeps it.
        static DentField Field() => new DentField(256, 256, 64f, 64f);

        [Test]
        public void EachKindOfWeaponLeavesItsOwnMark()
        {
            var fire = WeaponFlags.FireStarter;
            var spell = WeaponFlags.Spell;
            var table = new (WeaponInfo w, ScarKind kind)[]
            {
                (Cannon, ScarKind.Gunpowder),
                (Gun("VERMUSK 1", "ballistic", "", "", "teeny explosion", 0), ScarKind.Gunpowder),
                (Catapult, ScarKind.Siege),
                (Gun("ZONGIANT 1", "ballistic", "", "explosion", "large dust puff", 32), ScarKind.Siege),
                (Gun("MOCK FIREBALL", "ballistic", "", "", "teeny explosion", 24, fire), ScarKind.Fire),
                (Gun("TARHEL 1", "line of sight", "", "", "flamestrike", 100, fire), ScarKind.Fire),
                (Gun("ARADRAG 1", "line of sight", "fire", "fire", "", 50, fire), ScarKind.Breath),
                (Gun("TARNECRO 2", "guided", "", "", "volcblast", 150, fire | spell), ScarKind.Impact),
                (Gun("TARMAGE 3", "remote effect", "hailstorm", "", "fireball explosion", 200, fire | spell), ScarKind.Impact),
                (Gun("ARAKING 1", "line of sight", "lightning", "", "lightning explosion", 0), ScarKind.Lightning),
                (Gun("TARPRIES 2", "guided", "", "", "blue_shockring", 50, fire | spell), ScarKind.Lightning),
                (Hail, ScarKind.Frost),
                (Gun("CRECHIE 2", "line of sight", "turntofrozen", "", "lightning explosion", 0, spell), ScarKind.Frost),
                (Gun("TARMIND 1", "line of sight", "mindcontrol", "", "mind control", 0, WeaponFlags.UnitsOnly | spell), ScarKind.Dark),
                (Gun("TARLICH 3", "line of sight", "turntostone", "", "blue_shockring", 0, spell), ScarKind.Dark),
                (Gun("ARAPRIES 3", "line of sight", "turntostone", "", "blue_shockring", 0, spell), ScarKind.Holy),
                (Gun("VERMAGE 2", "guided", "", "", "waterballexp", 50, spell), ScarKind.Water),
                (Gun("VERDRAG 2", "guided", "", "", "tsunamiexp", 50, spell), ScarKind.Water),
                (Gun("ARAPRIES 4", "remote effect", "", "", "", 120, spell), ScarKind.Holy),
                (Gun("TARARCH 1", "ballistic", "", "", "teeny explosion", 0, fire), ScarKind.Fire),
                (Gun("TARWITCH 1", "wandering", "", "", "", 30), ScarKind.None),
                (Gun("Earthquake", "remote effect", "earthquake", "", "", 400, spell), ScarKind.Earth),
                (Gun("CREGATL 1", "ballistic", "", "", "medium dust puff", 24), ScarKind.Dust),
                (Gun("MOCK SWORD", "melee", "", "", "", 0), ScarKind.None),
                (Gun("ARABOW 1", "ballistic", "", "", "", 0), ScarKind.None),
                (Gun("ARAKING 3", "remote effect", "", "", "", 500, WeaponFlags.UnitsOnly | spell), ScarKind.None),
            };
            foreach (var (w, kind) in table)
                Assert.AreEqual(kind, ScarStamps.KindOf(Blast(w, Vector3.zero)), w.Name);
        }

        [Test]
        public void BlastsOnWaterAndShotsThatStruckAUnitLeaveNothing()
        {
            Assert.AreEqual(ScarKind.None, ScarStamps.KindOf(Blast(Cannon, Vector3.zero, BlastFlags.Water)));
            Assert.AreEqual(ScarKind.None, ScarStamps.KindOf(Blast(Cannon, Vector3.zero, BlastFlags.DirectHit)));
            // High in the air, a flyer struck: nothing reaches the ground.
            Assert.IsFalse(ScarStamps.Make(Blast(Cannon, new Vector3(0f, 9f, 0f)), 0f, out _));
        }

        [Test]
        public void ACannonDentsTheGroundAboutSixPixelsAtItsCentreWithARim()
        {
            var at = new Vector3(32f, 0f, -32f);
            var s = StampOf(Cannon, at);
            Assert.AreEqual(ScarKind.Gunpowder, s.Kind);
            Assert.AreEqual(6f, s.Depth, 0.01f, "a cannon digs the owner's 6 pixels");
            var field = Field();
            Assert.Greater(field.Stamp(s), 0);
            Assert.AreEqual(-6f, field.Height(at.x, at.z) * Px, 0.5f, "about 6 pixels down at its centre");
            // The thrown rim stands round the dip, highest just past its edge.
            float rim = float.MinValue;
            for (int a = 0; a < 32; a++)
            {
                float ang = a * Mathf.PI / 16f, r = s.Dent * 1.05f;
                rim = Mathf.Max(rim, field.Height(at.x + Mathf.Cos(ang) * r, at.z + Mathf.Sin(ang) * r) * Px);
            }
            Debug.Log($"Cannon crater: radius {s.Dent:0.00} units, {field.Height(at.x, at.z) * Px:0.0} px at the centre, rim {rim:0.0} px");
            Assert.Greater(rim, 1f, "a rim of thrown earth at least a pixel high");
            Assert.LessOrEqual(rim, s.Rim + 0.1f);
            Assert.AreEqual(0f, field.Height(at.x + s.Dent * 2f, at.z), 1e-4f, "flat ground past the rim");
        }

        [Test]
        public void TheLargestSpellsDigTenPixelsAndCannonsNoMoreThanSix()
        {
            var meteor = Gun("TARMAGE 3", "remote effect", "hailstorm", "", "fireball explosion", 600, WeaponFlags.FireStarter | WeaponFlags.Spell);
            Assert.AreEqual(ScarStamps.SpellDepthPx, StampOf(meteor, Vector3.zero).Depth, 0.01f);
            var bigGun = Gun("Bombard", "ballistic", "", "explosion", "large explosion", 300);
            Assert.AreEqual(ScarStamps.CannonDepthPx, StampOf(bigGun, Vector3.zero).Depth, 0.01f);
            Assert.Less(StampOf(Catapult, Vector3.zero).Depth, ScarStamps.CannonDepthPx);
        }

        [Test]
        public void AFrostSpellLeavesFrostAndCracksButNoCrater()
        {
            var at = new Vector3(20f, 0f, -20f);
            var s = StampOf(Hail, at);
            Assert.AreEqual(ScarKind.Frost, s.Kind);
            Assert.AreEqual(0f, s.Dent);
            Assert.AreEqual(0f, s.Depth);
            Assert.Greater(s.Frost, 30f, "rime that lasts");
            Assert.Greater(s.Crack, 0f, "and cracks");
            var field = Field();
            Assert.AreEqual(0, field.Stamp(s));
            Assert.AreEqual(0f, field.Height(at.x, at.z));
        }

        [Test]
        public void OverlappingCratersKeepTheDeeperRatherThanAddingUp()
        {
            var at = new Vector3(32f, 0f, -32f);
            var field = Field();
            field.Stamp(StampOf(Cannon, at));
            field.Stamp(StampOf(Cannon, at));
            field.Stamp(StampOf(Catapult, at));
            Assert.AreEqual(-6f, field.Height(at.x, at.z) * Px, 0.5f);
        }

        [Test]
        public void TheDentFieldFiltersAsTheTextureDoes()
        {
            // Texel centres sit half a texel in, and between them it blends:
            // here two on the crater's wall, where the dip changes fastest.
            var field = Field();
            field.Stamp(StampOf(Cannon, new Vector3(10.125f, 0f, -10.125f)));
            float a = field.Height(11.125f, -10.125f), b = field.Height(11.375f, -10.125f), mid = field.Height(11.25f, -10.125f);
            Assert.Greater(b - a, 0.5f / Px, "the wall rises between them");
            Assert.AreEqual((a + b) * 0.5f, mid, 0.02f / Px);
            Assert.AreEqual(0f, Field().Height(10f, -10f), "an undented field answers 0");
        }
    }
}
