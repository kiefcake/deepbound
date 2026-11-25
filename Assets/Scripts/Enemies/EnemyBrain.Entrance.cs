using Unity.Netcode;
using UnityEngine;

namespace Deepbound.Enemies
{
    public partial class EnemyBrain
    {
        public enum EntranceKind : byte { None, Floor, Wall, Portal }

        public readonly NetworkVariable<byte> Entrance = new NetworkVariable<byte>();
        public readonly NetworkVariable<Vector3> EntranceFrom = new NetworkVariable<Vector3>();
        public readonly NetworkVariable<double> EntranceAt = new NetworkVariable<double>();

        public readonly NetworkVariable<bool> Lesser = new NetworkVariable<bool>();

        public readonly NetworkVariable<bool> Hunting = new NetworkVariable<bool>();

        public const float LesserSize = 0.78f, LesserHealth = 0.3f, LesserDamage = 0.5f;

        public static float EntranceSeconds(EntranceKind k) => k == EntranceKind.Wall ? 1.5f : k == EntranceKind.Portal ? 1.25f : k == EntranceKind.Floor ? 1.3f : 0f;

        public const float EntranceTell = 0.9f, EmergingTaken = 1.5f;
        public static float EntranceTotal(EntranceKind k) => k == EntranceKind.None ? 0f : EntranceTell + EntranceSeconds(k);

        public bool Hidden => Entering && Time.time < tellUntil;
        private float tellUntil;
        private bool hunter;
        private float hunterLook;

        public void ServerMakeHunter()
        {
            hunter = true;
            Hunting.Value = true;
            leashRange = 999f;
            ServerAggroNearest();
        }

        private void TickHunter()
        {
            if (!hunter) return;
            home = transform.position;
            if (Time.time < hunterLook) return;
            hunterLook = Time.time + 1f;
            if (target == null || !Player.PlayerLife.CanTarget(target)) ServerAggroNearest();
        }

        private float enteringUntil;

        public bool Entering => Time.time < enteringUntil;

        public void PrepareEntrance(EntranceKind kind, Vector3 from)
        {
            Entrance.Value = (byte)kind;
            EntranceFrom.Value = from;
            var nm = NetworkManager.Singleton;
            EntranceAt.Value = nm != null ? nm.ServerTime.Time : 0.0;
        }

        public void ServerMakeLesser()
        {
            if (!IsServer) return;
            NoLoot = true;
            health.ServerSetMax(health.Max.Value * LesserHealth, refill: true);
            ServerScaleDamage(LesserDamage);
        }

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
