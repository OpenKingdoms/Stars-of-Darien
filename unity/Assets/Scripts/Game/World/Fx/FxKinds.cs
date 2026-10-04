// FxKinds.cs - which kind of blast a weapon makes, from the data's own words:
// its type, subtype, damage kind, explosion class, flags and name, and who
// cast it. The look of each kind is in FxBlast, and the scars, fire and magic
// streams pick their own parts by the same kinds.
using System.Collections.Generic;

namespace OpenKingdomsUnity.Game.World
{
    public enum BlastKind : byte
    {
        None,       // nothing past the original's picture: a sword, a claw, a spell with no burst
        Gunpowder,  // cannon, mortar, musket, bomb and every death blast
        Siege,      // thrown stone, catapult and trebuchet shot, harpoons
        Arrow,      // arrows, bolts, spears and daggers
        Fire,       // fireballs, meteors, fire waves and storms
        Breath,     // a dragon's or a demon's stream of flame
        Lightning,  // bolts, ball lightning and shock rings
        Water,      // water balls, bursts, tsunamis and vortices
        Frost,      // hail, ice storms and freezing
        Earth,      // earthquakes and the earthen wave
        Wind,       // tornadoes, hurricanes and wind waves
        Dark,       // death auras, mind control, turning to stone, Belial's vortex
        Holy,       // a divine caster's magic that is no element
    }

    public static class FxKinds
    {
        // The divine casters: Aramon's Acolyte and Avatar of Anu, Veruna's
        // Priest and Angel of Lihr.
        static readonly HashSet<string> Divine = new HashSet<string> { "ARAPRIES", "ARAPRIE2", "ARAGOD", "VERLIHR", "VERGOD" };

        // Whether a unit, by its internal name, casts divine magic.
        public static bool IsDivine(string unit) => !string.IsNullOrEmpty(unit) && Divine.Contains(unit.ToUpperInvariant());

        // The kind of a blast. A blast with no weapon behind it is a death
        // or a feature bursting, gunpowder when it reaches anything.
        public static BlastKind Of(in BlastEvent b, string caster = null)
        {
            if (b.Weapon != null) return Of(b.Weapon, caster);
            return b.Cause != BlastCause.Weapon && b.Radius > 0f ? BlastKind.Gunpowder : BlastKind.None;
        }

        // The kind of a weapon. caster is the internal name of the unit that
        // fires it, when known, as divine magic is told by its caster.
        public static BlastKind Of(WeaponInfo w, string caster = null)
        {
            if (w == null) return BlastKind.None;
            string type = Low(w.Type), sub = Low(w.Subtype), damage = Low(w.DamageKind), art = Low(w.ExplosionClass), name = Low(w.Name);
            bool fire = (w.Flags & WeaponFlags.FireStarter) != 0;
            bool divine = IsDivine(caster) || IsDivine(FirstWord(w.Name));
            bool thrown = type == "ballistic" || type == "guided" || type == "line of sight";

            if (sub == "earthquake" || Has(name, "earth", "quake")) return BlastKind.Earth;
            if (sub == "turntostone") return divine ? BlastKind.Holy : BlastKind.Dark;
            if (sub == "mindcontrol" || Has(name, "mind control", "aura", "soul")) return divine ? BlastKind.Holy : BlastKind.Dark;
            if (sub == "turntofrozen" || Has(sub, "freez") || Has(art, "ice") || Has(name, "ice", "frost", "freez", "hail", "snow")) return BlastKind.Frost;
            if (Has(art, "water", "tsunami", "splash") || Has(name, "water", "tsunami", "trident")) return BlastKind.Water;
            if (Has(name, "tornado", "hurricane", "wind", "cyclone")) return BlastKind.Wind;
            if (Has(name, "vortex")) return BlastKind.Dark;
            // An arrow stays an arrow when it paralyses or burns.
            if (thrown && Has(name, "arrow", "bolt", "spear", "dagger", "bolo", "javelin") && (art == "teeny explosion" || !Has(art, "explosion")))
                return BlastKind.Arrow;
            if (sub == "lightning" || Has(art, "lightning") || Has(art, "shockring") && damage != "explosion" || Has(name, "lightning", "thunder", "shock", "taser"))
                return BlastKind.Lightning;
            if (sub == "fire") return BlastKind.Breath;
            if (fire && art != "teeny explosion" || Has(art, "fireball", "flame", "volc", "explodeb") || Has(name, "fire", "flame", "meteor", "inferno"))
                return BlastKind.Fire;
            if (Has(art, "dust puff", "dirt", "rock"))
                return damage == "explosion" || w.AreaOfEffect * 16f >= 30f ? BlastKind.Siege : BlastKind.Arrow;
            if (art == "teeny explosion" && type == "guided") return BlastKind.Arrow;
            if (fire) return BlastKind.Fire;
            if (damage == "explosion" || Has(art, "explosion", "shockring")) return BlastKind.Gunpowder;
            if (type == "melee" || type == "remote effect" || type == "wandering") return divine && type != "melee" ? BlastKind.Holy : BlastKind.None;
            if (thrown) return BlastKind.Arrow;
            return BlastKind.None;
        }

        static string Low(string s) => string.IsNullOrEmpty(s) ? "" : s.ToLowerInvariant();

        static bool Has(string s, params string[] words)
        {
            foreach (var w in words) if (s.Contains(w)) return true;
            return false;
        }

        // The mock names its weapons by unit and slot, "ARAPRIES 3".
        static string FirstWord(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            int space = name.IndexOf(' ');
            return space > 0 ? name.Substring(0, space) : name;
        }
    }
}
