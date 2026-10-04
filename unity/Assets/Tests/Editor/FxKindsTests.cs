using NUnit.Framework;
using OpenKingdomsUnity.Game;
using OpenKingdomsUnity.Game.World;

namespace OpenKingdomsUnity.Tests
{
    public class FxKindsTests
    {
        // Every distinct weapon of the base game's units as the data spells
        // it: caster, name, type, subtype, damagetype, explosionclass,
        // areaofeffect, f for a fire starter and u for units only.
        static readonly string[] Data =
        {
            "ARAARCH|Bow and Arrows|ballistic|||||| Arrow",
            "ARABOW|Tracking Arrow|guided|||teeny explosion||| Arrow",
            "ARABOW|Paralyze Arrow|ballistic||paralyzer|green_shockring||| Arrow",
            "ARABROAD|Sword|melee||||31|| None",
            "ARABUILD|Hammer of the Masonic Temple|ballistic|||small dust puff||| Arrow",
            "ARACAN|Cannon|ballistic||explosion|large explosion|90|| Gunpowder",
            "ARADRAG|Fire Breath|line of sight|fire|fire||50|f| Breath",
            "ARADRAG|Fire Ball|guided|||fireball explosion|45|f| Fire",
            "ARADRAG|Earthquake|remote effect|earthquake|||500|| Earth",
            "ARAGOD|Magical Sword|melee||||100|| None",
            "ARAGOD|Earthquake|remote effect|earthquake|||400|| Earth",
            "ARAKING|Lightning|line of sight|lightning||lightning explosion||| Lightning",
            "ARAKING|Meteor|line of sight|||fireball explosion|100|| Fire",
            "ARAKING|Earthen Wave|remote effect||||500|u| Earth",
            "ARAPRIES|Hail Shower|remote effect|hailstorm||iceballexp|200|| Frost",
            "ARAPRIES|Turn To Stone|line of sight|turntostone||blue_shockring||| Holy",
            "ARAPULT|Cannonball|ballistic||explosion|large dust puff|100|| Siege",
            "ARASPY|Deadly Dagger|ballistic|guided||||| Arrow",
            "ARASSH|ARASSH Cannonballs|line of sight||explosion|large explosion|50|| Gunpowder",
            "ARATRE|Cannon|ballistic||explosion|large dust puff|100|| Siege",
            "ARAWAR|cannon|ballistic||explosion|medium explosion|60|| Gunpowder",
            "NPCAYLA|Area Mind Control|remote effect|mindcontrol|||250|u| Dark",
            "NPCSAIL|Gibberish|melee||paralyzer|||| None",
            "TARARCH|Flame Arrow|ballistic|||teeny explosion||f| Arrow",
            "TARBEAK|Egg Bomb|ballistic|dropped|explosion|medium explosion||| Gunpowder",
            "TARCAGE|fireball|ballistic|||fireball explosion||f| Fire",
            "TARDRAG|Ring of Fire|remote effect||||420|| Fire",
            "TARGOD|Claws of Belial|melee||||100|| None",
            "TARGOD|Fire Vortex|wandering||||30|| Dark",
            "TARKNIGH|fire_breath|line of sight|fire|fire|||f| Breath",
            "TARLICH|Death Aura|remote effect||||350|u| Dark",
            "TARMAGE|Death Breath|line of sight|fire|fire|||f| Breath",
            "TARMAGE|Fire Swirl|guided|||fireball explosion|10|| Fire",
            "TARMAGE|Fire Storm|remote effect|hailstorm||fireball explosion|200|f| Fire",
            "TARMIND|Individual Mind Control|line of sight|mindcontrol||mind control||u| Dark",
            "TARNECRO|Fire Ball|line of sight|||flamestrike|100|f| Fire",
            "TARNECRO|Guided Fire Ball|guided|||volcblast|150|f| Fire",
            "TARNECRO|Fire Wave|remote effect||||500|u| Fire",
            "TARPRIES|Ball Lightning|guided|||blue_shockring|50|f| Lightning",
            "TARPRIES|Fire Bomb|guided|||fireball explosion|80|f| Fire",
            "TARSHIP|Ghost Cannon|ballistic||explosion|blue_shockring|60|| Gunpowder",
            "TARSPOUT|Real fire|line of sight|fire|fire|||f| Breath",
            "TARWITCH|Tornado|wandering||||30|| Wind",
            "TARWITCH|Thunderbolt|line of sight|lightning||lightning explosion||| Lightning",
            "TARWITCH|Ice Storm|remote effect|hailstorm||iceballexp|300|| Frost",
            "VERARCH|Bolt|line of sight|||||| Arrow",
            "VERBALL|dropped cannonball of love|ballistic|dropped|explosion|large dust puff|20|| Siege",
            "VERDRAG|Water Ball|guided|||tsunamiexp|50|| Water",
            "VERDRAG|Tsunami|remote effect||||180|| Water",
            "VERFLTWR|harpoon|ballistic|||medium dust puff|32|| Siege",
            "VERGOD|Trident of the Angel|melee|||tsunamiexp|100|| Water",
            "VERGOD|Water Vortex|wandering||||30|| Water",
            "VERKNIGH|Spear|ballistic|||small dust puff|20|| Arrow",
            "VERLIHR|Water Ball|ballistic|||tsunamiexp||| Water",
            "VERMAGE|Water Ball|line of sight|||water splash|100|| Water",
            "VERMAGE|Water Burst|guided|||waterballexp|50|| Water",
            "VERMAGE|Water Blast|remote effect||||500|u| Water",
            "VERMORT|Mortar|ballistic||explosion|large explosion|110|| Gunpowder",
            "VERMUSK|Musket|ballistic|||teeny explosion||| Gunpowder",
            "VERTRE|trebuchet|ballistic||explosion|large explosion|87|| Gunpowder",
            "ZONBASIL|Individual Turn To Stone|line of sight|turntostone||blue_shockring||| Dark",
            "ZONDRAG|Lightning Ball|guided|||blue_shockring|60|| Lightning",
            "ZONDRAG|Shockring|remote effect||||230|| Lightning",
            "ZONGIANT|Flying Stones|ballistic||explosion|large dust puff|31|| Siege",
            "ZONGOD|Hurricane|wandering||||30|| Wind",
            "ZONGRYP|Throwing spear|ballistic|||small dust puff|20|| Arrow",
            "ZONHUNT|Wind Wave|remote effect||||500|u| Wind",
            "ZONLORD|bolo|ballistic|||||| Arrow",
            "ZONTROLL|Axe Thingy|melee||||29|| None",
        };

        static (WeaponInfo w, string caster, BlastKind want) Row(string row)
        {
            var f = row.Split('|');
            var w = new WeaponInfo
            {
                Name = f[1], Type = f[2], Subtype = f[3], DamageKind = f[4], ExplosionClass = f[5],
                AreaOfEffect = f[6].Length > 0 ? float.Parse(f[6]) / 16f : 0f,
                Flags = (f[7].Contains("f") ? WeaponFlags.FireStarter : 0) | (f[7].Contains("u") ? WeaponFlags.UnitsOnly : 0),
            };
            return (w, f[0], (BlastKind)System.Enum.Parse(typeof(BlastKind), f[8].Trim()));
        }

        [Test]
        public void EveryWeaponOfTheDataFallsInItsKind()
        {
            var wrong = new System.Collections.Generic.List<string>();
            foreach (var row in Data)
            {
                var (w, caster, want) = Row(row);
                var got = FxKinds.Of(w, caster);
                if (got != want) wrong.Add($"{caster} {w.Name}: {got}, not {want}");
            }
            Assert.IsEmpty(wrong, string.Join("\n", wrong));
        }

        [Test]
        public void TheMocksWeaponsFallInTheirKindsByTheirWords()
        {
            var seen = new System.Collections.Generic.HashSet<BlastKind>();
            foreach (var name in new[] { "ARACAN 1", "ARAPULT 1", "MOCK ARROW", "TARDRAG 2", "ARADRAG 1", "ARAKING 1", "VERMAGE 2", "ARAPRIES 2", "TARMIND 1", "ARAPRIES 3" })
            {
                var info = MockBackend.FxWeaponNamed(name)?.Info;
                Assert.IsNotNull(info, name);
                seen.Add(FxKinds.Of(info));
            }
            foreach (var k in new[] { BlastKind.Gunpowder, BlastKind.Siege, BlastKind.Arrow, BlastKind.Fire, BlastKind.Breath, BlastKind.Lightning, BlastKind.Water, BlastKind.Frost, BlastKind.Dark, BlastKind.Holy })
                Assert.IsTrue(seen.Contains(k), k + " among the mock's weapons");
        }

        [Test]
        public void ABlastWithNoWeaponIsADeathOrNothing()
        {
            Assert.AreEqual(BlastKind.Gunpowder, FxKinds.Of(new BlastEvent { Cause = BlastCause.Death, Radius = 3f }));
            Assert.AreEqual(BlastKind.None, FxKinds.Of(new BlastEvent { Cause = BlastCause.Death }));
            Assert.AreEqual(BlastKind.None, FxKinds.Of(new BlastEvent { Cause = BlastCause.Weapon, Radius = 3f }));
            Assert.IsTrue(FxKinds.IsDivine("VERGOD"));
            Assert.IsFalse(FxKinds.IsDivine("TARPRIES"));
        }
    }
}
