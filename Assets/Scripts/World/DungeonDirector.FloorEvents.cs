using Unity.Netcode;
using UnityEngine;

namespace Deepbound.World
{
    public enum FloorEvent : byte { None, Gilded, Swarm, Marching, BloodTithe, Bountiful, LongNight, Embertide, BoneChoir, Muster, Quicksilver, Ascendant, Restless, EmberMarch }

    public partial class DungeonDirector
    {
        public const float FloorEventChance = 0.4f, TitheHeal = 0.04f, TitheHurt = 1.15f;

        public readonly NetworkVariable<byte> FloorEventValue = new NetworkVariable<byte>();
        public static FloorEvent CurrentEvent => Instance != null && !InLobby ? (FloorEvent)Instance.FloorEventValue.Value : FloorEvent.None;

        public static string EventName(FloorEvent e) =>
            e == FloorEvent.Gilded ? "Gilded Halls" : e == FloorEvent.Swarm ? "The Swarm" : e == FloorEvent.Marching ? "The Dead March"
            : e == FloorEvent.BloodTithe ? "Blood Tithe" : e == FloorEvent.Bountiful ? "Bountiful"
            : e == FloorEvent.LongNight ? "The Long Night" : e == FloorEvent.Embertide ? "Embertide" : e == FloorEvent.BoneChoir ? "The Bone Choir"
            : e == FloorEvent.Muster ? "The Warlords' Muster" : e == FloorEvent.Quicksilver ? "Quicksilver"
            : e == FloorEvent.Ascendant ? "Ascendant" : e == FloorEvent.Restless ? "The Restless Dead" : e == FloorEvent.EmberMarch ? "The Ember March" : "";

        public static string EventText(FloorEvent e) =>
            e == FloorEvent.Gilded ? "enemies drop 60% more gold"
            : e == FloorEvent.Swarm ? "35% more enemies; 40% more embers"
            : e == FloorEvent.Marching ? "additional patrols; 25% more gold"
            : e == FloorEvent.BloodTithe ? "kills restore 4% health; you take 15% more damage"
            : e == FloorEvent.Bountiful ? "every cleared room leaves a reward"
            : e == FloorEvent.LongNight ? "twice as many elites, with richer loot"
            : e == FloorEvent.Embertide ? "struck enemies smoulder and take Thermal Shock from frost; enemies deal 10% more damage"
            : e == FloorEvent.BoneChoir ? "elites fire rings of souls; 30% more embers"
            : e == FloorEvent.Muster ? "three champions, each guarding a hoard"
            : e == FloorEvent.Quicksilver ? "everyone moves 15% faster; 30% more embers"
            : e == FloorEvent.Ascendant ? "ultimates charge 50% faster; enemies deal 10% more damage"
            : e == FloorEvent.Restless ? "slain enemies may rise again unless their bones are destroyed; 40% more embers"
            : e == FloorEvent.EmberMarch ? "every elite leaves a trail of fire; 50% more embers"
            : "";

        public static Color EventColor(FloorEvent e) =>
            e == FloorEvent.Gilded ? new Color(1f, 0.82f, 0.35f) : e == FloorEvent.Swarm ? new Color(0.85f, 0.75f, 0.6f)
            : e == FloorEvent.Marching ? new Color(0.8f, 0.85f, 0.9f) : e == FloorEvent.BloodTithe ? new Color(0.95f, 0.25f, 0.2f)
            : e == FloorEvent.Bountiful ? new Color(0.55f, 0.95f, 0.5f)
            : e == FloorEvent.LongNight ? new Color(0.6f, 0.55f, 0.95f) : e == FloorEvent.Embertide ? new Color(1f, 0.55f, 0.85f)
            : e == FloorEvent.BoneChoir ? new Color(0.85f, 0.95f, 1f)
            : e == FloorEvent.Muster ? new Color(1f, 0.76f, 0.3f) : e == FloorEvent.Quicksilver ? new Color(0.75f, 0.85f, 0.9f)
            : e == FloorEvent.Ascendant ? new Color(1f, 0.95f, 0.75f) : e == FloorEvent.Restless ? new Color(0.75f, 0.7f, 0.6f) : e == FloorEvent.EmberMarch ? new Color(1f, 0.5f, 0.2f) : Color.white;

        public static float EventGold => CurrentEvent == FloorEvent.Gilded ? 1.6f : CurrentEvent == FloorEvent.Marching ? 1.25f : 1f;
        public static float EventCrowd => CurrentEvent == FloorEvent.Swarm ? 1.35f : 1f;
        public static float EventEmbers => CurrentEvent == FloorEvent.EmberMarch ? 1.5f : CurrentEvent == FloorEvent.Swarm || CurrentEvent == FloorEvent.Restless ? 1.4f : CurrentEvent == FloorEvent.BoneChoir || CurrentEvent == FloorEvent.Quicksilver ? 1.3f : 1f;

        public static float EventUltCharge => CurrentEvent == FloorEvent.Ascendant ? 1.5f : 1f;
        public const float AscendantHurt = 1.1f, RestlessChance = 0.2f;

        public static bool ForceRestless;

        public static float EventSpeed => CurrentEvent == FloorEvent.Quicksilver ? 1.15f : 1f;
        public static int EventChampions => CurrentEvent == FloorEvent.Muster ? 3 : 1;
        public static float EventElites => CurrentEvent == FloorEvent.LongNight ? 2f : 1f;
        public static float EventEliteLoot => CurrentEvent == FloorEvent.LongNight ? 1.5f : 1f;
        public const float EmbertideHurt = 1.1f, ChoirInterval = 6f, ChoirRange = 18f;
        public static int EventPatrols => CurrentEvent == FloorEvent.Marching ? 2 : 0;
        public static float EventClearChance => CurrentEvent == FloorEvent.Bountiful ? 1f : RoomClearRewardChance;

        private void RollFloorEvent(int depth)
        {
            var e = FloorEvent.None;
            if (!TestMode && depth >= 1 && (Random.value < FloorEventChance || EndlessLap(depth) > 0)) e = (FloorEvent)Random.Range(1, 14);
            FloorEventValue.Value = (byte)e;
            Combat.Health.FloorPlayerDamageScale = PlayerHurtFor(e);
        }

        private void ClearFloorEvent()
        {
            FloorEventValue.Value = 0;
            Combat.Health.FloorPlayerDamageScale = 1f;
        }

        public void ServerDebugEvent(FloorEvent e)
        {
            HookEmbertide();
            FloorEventValue.Value = (byte)e;
            Combat.Health.FloorPlayerDamageScale = PlayerHurtFor(e);
        }

        private static float PlayerHurtFor(FloorEvent e) => e == FloorEvent.BloodTithe ? TitheHurt : e == FloorEvent.Embertide ? EmbertideHurt : e == FloorEvent.Ascendant ? AscendantHurt : 1f;

        public void ServerRestless(Enemies.EnemyBrain brain)
        {
            if (CurrentEvent != FloorEvent.Restless || brain == null || brain.Elite.Value || brain is Enemies.BossBrain || Random.value >= (ForceRestless ? 1f : RestlessChance)) return;
            ServerGravebound(brain);
        }

        private static bool embertideHooked;

        private static void HookEmbertide()
        {
            if (embertideHooked) return;
            embertideHooked = true;
            Combat.Health.ServerAnyDamaged += (h, dealt, by) =>
            {
                if (CurrentEvent != FloorEvent.Embertide || h == null || h.Team != Combat.Team.Enemies || !h.IsAlive || h.Scorched.Value || h.Chilled.Value) return;
                var nm = NetworkManager.Singleton;
                if (nm == null || by == ulong.MaxValue || !nm.SpawnManager.SpawnedObjects.TryGetValue(by, out var o) || !o.TryGetComponent<Progression.PlayerProgress>(out _)) return;
                h.ServerScorch(6f, by);
            };
        }

        private float nextChoir;

        private void TickBoneChoir()
        {
            HookEmbertide();
            if (CurrentEvent != FloorEvent.BoneChoir || Time.time < nextChoir) return;
            nextChoir = Time.time + 0.5f;
            foreach (var b in Enemies.EnemyBrain.All)
            {
                if (b == null || b.IsDead || !b.Elite.Value || b is Enemies.BossBrain || Time.time < b.ChoirReadyAt) continue;
                var nearest = Player.PlayerLife.All.Find(p => p != null && !p.Downed.Value && Vector3.Distance(p.transform.position, b.transform.position) < ChoirRange);
                if (nearest == null) continue;
                b.ChoirReadyAt = Time.time + ChoirInterval + Random.Range(0f, 1.5f);
                var origin = b.transform.position + Vector3.up * 1.4f;
                float start = Random.Range(0f, 360f);
                const int arms = 10;
                for (int i = 0; i < arms; i++)
                {
                    var dir = Quaternion.Euler(0f, start + i * 360f / arms, 0f) * Vector3.forward;
                    Combat.Arrows.ServerFire(origin, dir, 9f * Enemies.Difficulty.DepthDamage(Layout != null ? Layout.Depth : 0), Combat.Team.Players, b.NetworkObjectId, 7.5f, null, false, 16f);
                }
                ChoirRingRpc(origin, start, arms);
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void ChoirRingRpc(Vector3 origin, float start, int arms)
        {
            for (int i = 0; i < arms; i++)
                Combat.Arrows.SpawnVisual(origin, Quaternion.Euler(0f, start + i * 360f / arms, 0f) * Vector3.forward, Combat.ProjectileKind.Soul, 7.5f, 16f);
            Art.Sfx.Play(Art.Sound.Whisper, origin, 0.6f, 0.8f);
        }

        public static void ServerOnPlayerKill(Combat.Health killer)
        {
            if (CurrentEvent != FloorEvent.BloodTithe || killer == null || !killer.IsAlive) return;
            killer.ServerHeal(killer.Max.Value * TitheHeal);
        }
    }
}
