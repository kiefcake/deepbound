using Unity.Netcode;
using UnityEngine;

namespace Deepbound.Enemies
{
    /// <summary>
    /// ENTRANCES (2026-11-12, the user: "i'd like to see enemies not spawn out of the air, preferrably they climb out of
    /// the walls, or out of the floor, etc, or are summoned through portals"). Anything raised mid-fight (an ambush wave,
    /// a pack, a necromancer's or a guardian's summons, a cleared room erupting) comes in one of three ways:
    /// <list type="bullet">
    /// <item><b>Wall</b>: it claws out of the nearest wall, low and pitched forward, and stands up in the room;</item>
    /// <item><b>Floor</b>: it heaves up through the flagstones in a spray of grave dirt;</item>
    /// <item><b>Portal</b>: a violet rift opens and it steps through.</item>
    /// </list>
    /// It cannot act until it is through (<see cref="Entering"/>), so nothing hits you from inside a wall. The kind, the
    /// wall point and the server time are synced (set before Spawn, so a client's first frame already knows); every
    /// peer plays it with <see cref="Art.EnemyEntrance"/> from server time, so a late look sees the same moment.
    /// </summary>
    public partial class EnemyBrain
    {
        public enum EntranceKind : byte { None, Floor, Wall, Portal }

        public readonly NetworkVariable<byte> Entrance = new NetworkVariable<byte>();
        public readonly NetworkVariable<Vector3> EntranceFrom = new NetworkVariable<Vector3>();
        public readonly NetworkVariable<double> EntranceAt = new NetworkVariable<double>();
        /// <summary>A lesser one (the swarms a cleared room erupts with): small, frail, weak, no loot.</summary>
        public readonly NetworkVariable<bool> Lesser = new NetworkVariable<bool>();
        /// <summary>A breach hunter (synced: every map shows it at any range).</summary>
        public readonly NetworkVariable<bool> Hunting = new NetworkVariable<bool>();

        public const float LesserSize = 0.78f, LesserHealth = 0.3f, LesserDamage = 0.5f;

        public static float EntranceSeconds(EntranceKind k) => k == EntranceKind.Wall ? 1.5f : k == EntranceKind.Portal ? 1.25f : k == EntranceKind.Floor ? 1.3f : 0f;

        /// <summary>
        /// TELEGRAPHED WAYS IN (2026-11-13, the user: "Wall cracks glow or bulge a second before something comes out, so sharp
        /// players can stand at the hole and hit it as it emerges"): for <see cref="EntranceTell"/> seconds the wall glows and
        /// bulges, the floor cracks, or the rift opens, with nothing to hit yet; then it climbs out, and while it does it takes
        /// x<see cref="EmergingTaken"/>.
        /// </summary>
        public const float EntranceTell = 0.9f, EmergingTaken = 1.5f;
        public static float EntranceTotal(EntranceKind k) => k == EntranceKind.None ? 0f : EntranceTell + EntranceSeconds(k);

        /// <summary>Server: still inside the wall / under the floor (takes nothing).</summary>
        public bool Hidden => Entering && Time.time < tellUntil;
        private float tellUntil;
        private bool hunter;
        private float hunterLook;

        /// <summary>
        /// Server: a BREACH HUNTER (2026-11-13): it ignores its post and leash and follows the nearest delver wherever they
        /// go, along the navmesh, through cleared rooms.
        /// </summary>
        public void ServerMakeHunter()
        {
            hunter = true;
            Hunting.Value = true;
            leashRange = 999f;
            ServerAggroNearest();
        }

        /// <summary>Server, from Update: a hunter's post is wherever it is, and it never loses the scent.</summary>
        private void TickHunter()
        {
            if (!hunter) return;
            home = transform.position;
            if (Time.time < hunterLook) return;
            hunterLook = Time.time + 1f;
            if (target == null || !Player.PlayerLife.CanTarget(target)) ServerAggroNearest();
        }

        private float enteringUntil;
        /// <summary>Server: still coming in (it waits, it does not attack).</summary>
        public bool Entering => Time.time < enteringUntil;

        /// <summary>Server, BEFORE Spawn: how this one comes in (<paramref name="from"/>: the wall point for a wall).</summary>
        public void PrepareEntrance(EntranceKind kind, Vector3 from)
        {
            Entrance.Value = (byte)kind;
            EntranceFrom.Value = from;
            var nm = NetworkManager.Singleton;
            EntranceAt.Value = nm != null ? nm.ServerTime.Time : 0.0;
        }

        /// <summary>Server, after Spawn and <see cref="ServerScale"/>: make it one of a lesser swarm.</summary>
        public void ServerMakeLesser()
        {
            if (!IsServer) return;
            NoLoot = true;
            health.ServerSetMax(health.Max.Value * LesserHealth, refill: true);
            ServerScaleDamage(LesserDamage);
        }

        /// <summary>Every peer, from OnNetworkSpawn once the rig exists: the entrance (or the old rise) and the lesser size.</summary>
        private void StartEntrance()
        {
            if (IsServer && Entrance.Value != 0)
            {
                enteringUntil = Time.time + EntranceTotal((EntranceKind)Entrance.Value);
                tellUntil = Time.time + EntranceTell;
                health.IncomingModifiers.Insert(0, (dmg, from) => !Entering || dmg >= BossBrain.KillDamage ? dmg : Time.time < tellUntil ? 0f : dmg * EmergingTaken);
            }
            if (Lesser.Value) Grow(LesserSize);
            Lesser.OnValueChanged += (_, now) => { if (now) Grow(LesserSize); };
            if (Rig == null || IsBoss) return;
            if (Entrance.Value != 0) { Rig.gameObject.AddComponent<Art.EnemyEntrance>().Init(this); return; }
            // A late value (should not happen: it is set before Spawn) still plays.
            Entrance.OnValueChanged += (_, now) =>
            {
                if (now != 0 && Rig != null && Rig.GetComponent<Art.EnemyEntrance>() == null)
                {
                    var rise = Rig.GetComponent<Art.RiseFromGround>();
                    if (rise != null) Destroy(rise);
                    Rig.gameObject.AddComponent<Art.EnemyEntrance>().Init(this);
                }
            };
            if (RisesFromGround(visualKind)) Rig.gameObject.AddComponent<Art.RiseFromGround>();
        }
    }
}
