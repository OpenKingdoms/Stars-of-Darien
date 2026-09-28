// MockActions.cs - the mock's sidebar: the orders, abilities, stances and
// spells a selection shares, laid out as the original's table is (the ids
// are its widget names), and carried out simply, so the battle HUD can be
// built and tested without the engine. A mage has two spells and its own
// mana, a healer heals, a wagon loads and unloads, and everyone has the
// three stances.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace OpenKingdomsUnity.Game
{
    public sealed partial class MockBackend
    {
        public enum Role { Monarch, Knight, Archer, Lodge, Mage, Healer, Wagon, Boat }
        public enum Stance { Offensive, Defensive, Passive }

        readonly Dictionary<int, Role> roles = new Dictionary<int, Role>();
        readonly Dictionary<int, Stance> stances = new Dictionary<int, Stance>();
        readonly Dictionary<int, float> mana = new Dictionary<int, float>();
        readonly Dictionary<int, string> weapon = new Dictionary<int, string>();
        readonly Dictionary<int, List<int>> carried = new Dictionary<int, List<int>>();
        readonly HashSet<int> aboard = new HashSet<int>();

        public const int MageMana = 100, FireballCost = 30, FrostCost = 20;

        public Role RoleOf(int def) => roles.TryGetValue(def, out var r) ? r : (Role)(def % 4);
        public int ManaOf(int handle) => mana.TryGetValue(handle, out var m) ? Mathf.FloorToInt(m) : 0;
        public Stance StanceOf(int handle) => stances.TryGetValue(handle, out var s) ? s : Stance.Offensive;
        public bool IsAboard(int handle) => aboard.Contains(handle);

        // New defs for every side, after the first four of each.
        void AddSpecialists()
        {
            string[] anims = { "idle", "walk", "attack" };
            foreach (var s in sides)
            {
                string p = s.Id.ToLowerInvariant();
                roles[unitDefs.Count] = Role.Mage;
                AddDef(p + "_mage", p + "archer", s.Id, "mage", "Mage", 160, 180, false, anims);
                roles[unitDefs.Count] = Role.Healer;
                AddDef(p + "_healer", p + "archer", s.Id, "healer", "Healer", 150, 150, false, anims);
                roles[unitDefs.Count] = Role.Wagon;
                AddDef(p + "_wagon", p + "knight", s.Id, "transport", "Wagon", 300, 200, false, anims);
                roles[unitDefs.Count] = Role.Boat;
                AddDef(p + "_boat", "mockboat", s.Id, "BOAT ATTACK", "Boat", 400, 150, false, new[] { "idle" });
            }
        }

        // A boat for a player on the water at a point, for tests, reported
        // at the sea floor as the engine reports ships.
        public int SpawnBoat(int player, Vector3 at)
        {
            int index = players.FindIndex(p => p.Index == player);
            if (index < 0 || Terrain == null) return -1;
            foreach (var kv in roles)
                if (kv.Value == Role.Boat && unitDefs[kv.Key].Side == players[index].Side)
                {
                    var u = Spawn(kv.Key, index, new Vector2(at.x, at.z));
                    u.Heading = 0f;
                    return u.Handle;
                }
            return -1;
        }

        void SpawnSpecialists(PlayerInfo p, int pos, Vector2 home)
        {
            foreach (var kv in roles)
            {
                if (unitDefs[kv.Key].Side != p.Side || kv.Value == Role.Boat) continue;
                int k = (int)kv.Value - (int)Role.Mage;
                var u = Spawn(kv.Key, pos, home + new Vector2(-3 + k * 1.5f, -6));
                if (kv.Value == Role.Mage) mana[u.Handle] = MageMana;
            }
        }

        void TickMana(float dt)
        {
            foreach (var h in mana.Keys.ToList()) mana[h] = Mathf.Min(MageMana, mana[h] + 2f * dt);
        }

        // ---- The table ----

        static UnitAction Order(string id, string label, CommandKind c, ActionTarget t, string key) =>
            new UnitAction { Id = id, Label = label, Kind = ActionKind.Order, Command = c, Target = t, Hotkey = key, StanceGroup = 0 };

        List<UnitAction> ActionsOf(Unit u)
        {
            var list = new List<UnitAction>();
            var role = RoleOf(u.Def);
            if (unitDefs[u.Def].IsBuilding) return list;
            list.Add(Order("MOVE", "Move", CommandKind.Move, ActionTarget.Point, "M"));
            if (role != Role.Healer && role != Role.Wagon) list.Add(Order("ATTACK", "Attack", CommandKind.Attack, ActionTarget.PointOrUnit, "A"));
            list.Add(Order("GUARD", "Guard", CommandKind.Guard, ActionTarget.Unit, "G"));
            list.Add(Order("PATROL", "Patrol", CommandKind.Patrol, ActionTarget.Point, "P"));
            list.Add(Order("STOP", "Stop", CommandKind.Stop, ActionTarget.None, "S"));
            if (role == Role.Healer || role == Role.Monarch)
                list.Add(new UnitAction { Id = "HEAL", Label = "Heal", Kind = ActionKind.Ability, Command = CommandKind.Repair, Target = ActionTarget.Unit, Hotkey = "H", StanceGroup = 0 });
            if (role == Role.Wagon)
            {
                list.Add(new UnitAction { Id = "LOAD", Label = "Load", Kind = ActionKind.Ability, Command = CommandKind.Load, Target = ActionTarget.Area, Hotkey = "L", StanceGroup = 0 });
                list.Add(new UnitAction { Id = "UNLOAD", Label = "Unload", Kind = ActionKind.Ability, Command = CommandKind.Unload, Target = ActionTarget.Point, Hotkey = "U", StanceGroup = 0,
                    Enabled = carried.TryGetValue(u.Handle, out var c) && c.Count > 0, Why = "Nothing aboard" });
            }
            var st = StanceOf(u.Handle);
            foreach (Stance s in Enum.GetValues(typeof(Stance)))
                list.Add(new UnitAction { Id = s.ToString(), Label = s.ToString(), Kind = ActionKind.Stance, Command = CommandKind.SetAggro, Arg = (int)s, Target = ActionTarget.None, StanceGroup = 1, Toggled = st == s });
            if (role == Role.Mage)
            {
                weapon.TryGetValue(u.Handle, out var w);
                int m = ManaOf(u.Handle);
                list.Add(new UnitAction { Id = "PrimaryWeapon", Label = "Fireball", Kind = ActionKind.Spell, Command = CommandKind.SetWeapon, Arg = 0, Target = ActionTarget.PointOrUnit,
                    ManaCost = FireballCost, StanceGroup = 2, Toggled = w == null || w == "PrimaryWeapon", Enabled = m >= FireballCost, Why = "Not enough mana" });
                list.Add(new UnitAction { Id = "SecondaryWeapon", Label = "Frost", Kind = ActionKind.Spell, Command = CommandKind.SetWeapon, Arg = 1, Target = ActionTarget.PointOrUnit,
                    ManaCost = FrostCost, StanceGroup = 2, Toggled = w == "SecondaryWeapon", Enabled = m >= FrostCost, Why = "Not enough mana" });
            }
            return list;
        }

        // What every selected unit has, in the first one's order. An action
        // is enabled when any of them can do it, toggled when all hold it.
        public UnitAction[] SelectionActions()
        {
            var chosen = mockSelection.Where(h => byHandle.TryGetValue(h, out var u) && !u.Dying).Select(h => byHandle[h]).ToList();
            if (chosen.Count == 0) return Array.Empty<UnitAction>();
            var first = ActionsOf(chosen[0]);
            var others = chosen.Skip(1).Select(ActionsOf).ToList();
            var shared = new List<UnitAction>();
            foreach (var a in first)
            {
                bool all = true, anyOn = a.Enabled, allToggled = a.Toggled;
                foreach (var o in others)
                {
                    var m = o.Find(x => x.Id == a.Id);
                    if (m == null) { all = false; break; }
                    anyOn |= m.Enabled;
                    allToggled &= m.Toggled;
                }
                if (!all) continue;
                a.Enabled = anyOn;
                a.Toggled = allToggled;
                shared.Add(a);
            }
            return shared.ToArray();
        }

        bool DoMockAction(string id, Vector3 at, int unit, Rect area, bool queue)
        {
            if (Status != GameStatus.Running) return false;
            var actions = SelectionActions();
            var a = Array.Find(actions, x => x.Id == id);
            if (a == null || !a.Enabled) return false;
            bool hasArea = area.width > 0 && area.height > 0;
            bool done = false;
            foreach (int h in mockSelection.ToArray())
            {
                if (!byHandle.TryGetValue(h, out var u) || u.Dying) continue;
                switch (a.Kind)
                {
                    case ActionKind.Stance:
                        stances[h] = (Stance)a.Arg;
                        done = true;
                        break;
                    case ActionKind.Spell:
                        if (RoleOf(u.Def) != Role.Mage) break;
                        weapon[h] = id;
                        done = true;
                        if (unit < 0 && at == Vector3.zero) break;
                        if (ManaOf(h) < a.ManaCost) break;
                        mana[h] -= a.ManaCost;
                        Cast(u, id, unit, at);
                        break;
                    case ActionKind.Ability when id == "HEAL":
                        if (unit >= 0 && byHandle.TryGetValue(unit, out var hurt) && !hurt.Dying)
                        {
                            hurt.Health = Mathf.Min(hurt.MaxHealth, hurt.Health + 50);
                            done = true;
                        }
                        break;
                    case ActionKind.Ability when id == "LOAD":
                        done |= Load(u, unit, hasArea ? area : (Rect?)null);
                        break;
                    case ActionKind.Ability when id == "UNLOAD":
                        done |= Unload(u, at);
                        break;
                    default:
                        if (id == "ATTACK" && hasArea)
                        {
                            // A box of enemies: attack the nearest in it.
                            int best = -1;
                            float bd = float.MaxValue;
                            foreach (var o in units)
                            {
                                if (o.Dying || o.Player == 0 || !area.Contains(new Vector2(o.Pos.x, o.Pos.z))) continue;
                                float d = (o.Pos - u.Pos).sqrMagnitude;
                                if (d < bd) { bd = d; best = o.Handle; }
                            }
                            if (best >= 0) done |= Command(new GameCommand { Kind = CommandKind.Attack, Unit = h, TargetUnit = best, BuildDef = -1, Queue = queue });
                        }
                        else if (a.Target == ActionTarget.None) done |= Command(GameCommand.To(a.Command, h, Vector3.zero));
                        else if (unit >= 0 && (a.Target == ActionTarget.Unit || a.Target == ActionTarget.PointOrUnit))
                            done |= Command(new GameCommand { Kind = a.Command, Unit = h, TargetUnit = unit, Target = at, BuildDef = -1, Queue = queue });
                        else { var c = GameCommand.To(a.Command, h, at); c.Queue = queue; done |= Command(c); }
                        break;
                }
            }
            return done;
        }

        void Cast(Unit caster, string id, int unit, Vector3 at)
        {
            var point = unit >= 0 && byHandle.TryGetValue(unit, out var t) ? t.Pos : at;
            float radius = id == "PrimaryWeapon" ? 2.5f : 0.8f;
            int damage = id == "PrimaryWeapon" ? 60 : 40;
            foreach (var o in units)
                if (!o.Dying && o.Player != caster.Player && (o.Pos - point).sqrMagnitude < radius * radius) Hurt(o, (int)(damage * Mathf.Max(DamageScale, 0.01f)));
            caster.Heading = Mathf.Atan2(point.x - caster.Pos.x, point.z - caster.Pos.z) * Mathf.Rad2Deg;
        }

        bool Load(Unit wagon, int unit, Rect? area)
        {
            if (!carried.TryGetValue(wagon.Handle, out var list)) carried[wagon.Handle] = list = new List<int>();
            bool any = false;
            foreach (var o in units)
            {
                if (o.Dying || o.Player != wagon.Player || o == wagon || unitDefs[o.Def].IsBuilding || aboard.Contains(o.Handle)) continue;
                if (RoleOf(o.Def) == Role.Wagon) continue;
                bool pick = area is Rect r ? r.Contains(new Vector2(o.Pos.x, o.Pos.z)) : o.Handle == unit;
                if (!pick || list.Count >= 6) continue;
                list.Add(o.Handle);
                aboard.Add(o.Handle);
                mockSelection.Remove(o.Handle);
                any = true;
            }
            return any;
        }

        bool Unload(Unit wagon, Vector3 at)
        {
            if (!carried.TryGetValue(wagon.Handle, out var list) || list.Count == 0) return false;
            int k = 0;
            foreach (int h in list)
            {
                aboard.Remove(h);
                if (!byHandle.TryGetValue(h, out var o)) continue;
                var p = new Vector2(at.x + (k % 3) * 1.2f, at.z - (k / 3) * 1.2f);
                o.Pos = new Vector3(p.x, Terrain.Sample(p.x, p.y), p.y);
                o.Home = p;
                o.Goal = null;
                k++;
            }
            list.Clear();
            return true;
        }
    }
}
