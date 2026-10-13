using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.Combat
{
    /// <summary>
    /// Who can hurt whom. Props (urns, barrels) are hit by anything aimed at enemies but
    /// are invisible to enemy AI, the radar and relic procs. APPEND ONLY: synced.
    /// </summary>
    public enum Team : byte { Players, Enemies, Props }

    /// <summary>
    /// Hit points for anything that can be hurt. Server-authoritative: only the server
    /// changes the value; every peer reads it.
    ///
    /// <para>
    /// Damage runs through a RotMG-style defence step — flat reduction by DEF, floored at
    /// 15% of the hit so heavy armour blunts small hits but never makes you immune — and
    /// then through any <see cref="IncomingModifiers"/> (blocking, War Cry) the owning
    /// components register.
    /// </para>
    /// </summary>
    public class Health : NetworkBehaviour
    {
        /// <summary>Every live Health on this peer. Targeting and hit queries scan this instead of physics.</summary>
        public static readonly List<Health> All = new List<Health>();

        [SerializeField] private Team team;

        public readonly NetworkVariable<float> Current = new NetworkVariable<float>(100f);
        public readonly NetworkVariable<float> Max = new NetworkVariable<float>(100f);

        /// <summary>
        /// Chilled by frost: slowed, and the setup for a Mage's Shatter (fire on a chilled
        /// target hits harder and consumes the chill). Synced so everyone sees the frost.
        /// </summary>
        public readonly NetworkVariable<bool> Chilled = new NetworkVariable<bool>();
        private float chilledUntil;
        private GameObject chillAura;

        /// <summary>Flat damage reduction. Server-side only; set from the DEF stat.</summary>
        public float Defense { get; set; }

        /// <summary>
        /// Server-side damage modifiers: (damage, attacker position) → new damage. Run in
        /// registration order after defence.
        /// </summary>
        public readonly List<Func<float, Vector3, float>> IncomingModifiers = new List<Func<float, Vector3, float>>();

        /// <summary>Raised on every peer when the value drops. Argument is the amount lost.</summary>
        public event Action<float> Hurt;

        /// <summary>Raised on every peer for any Health losing value. Drives floating damage numbers.</summary>
        public static event Action<Health, float> AnyHurt;

        /// <summary>Raised on the server when the value reaches zero.</summary>
        public event Action<Health> Died;

        /// <summary>Raised on the server for every Health that takes damage: (target, dealt, attacker id). Relics hook this.</summary>
        public static event Action<Health, float, ulong> ServerAnyDamaged;

        /// <summary>Raised on the server after any damage is applied, with the attacker's object id.</summary>
        public event Action<float, ulong> DamagedServer;

        public Team Team => team;
        public bool IsAlive => Current.Value > 0f;
        public float Fraction => Max.Value > 0f ? Current.Value / Max.Value : 0f;

        /// <summary>Time (local clock) this peer last saw it take damage. Drives floating health bars.</summary>
        public float LastHurtTime { get; private set; } = -100f;

        /// <summary>Aim point for hit tests and AI: roughly the centre of mass.</summary>
        public Vector3 Center => transform.position + Vector3.up * centerHeight;

        [SerializeField] private float centerHeight = 1f;
        public float CenterHeight { get => centerHeight; set => centerHeight = value; }

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            spawnedAt = Time.time;
            Current.OnValueChanged += HandleChanged;
        }

        public override void OnNetworkDespawn()
        {
            All.Remove(this);
            Current.OnValueChanged -= HandleChanged;
        }

        public override void OnDestroy()
        {
            All.Remove(this);
            base.OnDestroy();
        }

        private float spawnedAt;

        private void HandleChanged(float previous, float current)
        {
            // The first moments after spawn are the server applying max health and
            // scaling, not damage — do not flash bars or play hurt effects for them.
            if (current < previous && Time.time - spawnedAt > 0.5f)
            {
                LastHurtTime = Time.time;
                Hurt?.Invoke(previous - current);
                AnyHurt?.Invoke(this, previous - current);
            }
        }

        // ------------------------------------------------------------------
        // Server API
        // ------------------------------------------------------------------

        public void ServerSetMax(float max, bool refill)
        {
            if (!IsServer) return;
            Max.Value = max;
            Current.Value = refill ? max : Mathf.Min(Current.Value, max);
        }

        /// <returns>Damage actually dealt after defence and modifiers.</returns>
        public float ServerDamage(float amount, Vector3 attackerPosition, ulong attackerId = ulong.MaxValue)
        {
            if (!IsServer || !IsAlive || amount <= 0f) return 0f;

            float dealt = Mathf.Max(amount - Defense, amount * 0.15f);
            foreach (var modifier in IncomingModifiers) dealt = modifier(dealt, attackerPosition);
            if (dealt <= 0f) return 0f;

            // Overkill is not damage: stats and lifesteal see only the health actually lost.
            float lost = Mathf.Min(dealt, Current.Value);
            Current.Value = Mathf.Max(0f, Current.Value - dealt);
            DamagedServer?.Invoke(lost, attackerId);
            ServerAnyDamaged?.Invoke(this, lost, attackerId);

            if (Current.Value <= 0f) Died?.Invoke(this);
            return dealt;
        }

        public void ServerChill(float seconds)
        {
            if (!IsServer || !IsAlive) return;
            chilledUntil = Mathf.Max(chilledUntil, Time.time + seconds);
            Chilled.Value = true;
        }

        /// <returns>True if the target was chilled (and no longer is).</returns>
        public bool ServerConsumeChill()
        {
            if (!IsServer || !Chilled.Value) return false;
            Chilled.Value = false;
            chilledUntil = 0f;
            return true;
        }

        private void Update()
        {
            if (IsServer && Chilled.Value && (Time.time >= chilledUntil || !IsAlive)) Chilled.Value = false;

            bool show = Chilled.Value && IsAlive;
            if (show && chillAura == null) chillAura = BuildChillAura();
            if (chillAura != null && chillAura.activeSelf != show) chillAura.SetActive(show);
        }

        /// <summary>A ring of frost at the feet and ice shards orbiting the body.</summary>
        private GameObject BuildChillAura()
        {
            var root = new GameObject("ChillAura");
            root.transform.SetParent(transform, false);
            var ice = new Color(0.55f, 0.85f, 1f) * 1.6f;
            var ring = Art.MeshKit.Part(root.transform, "Frost", Art.MeshKit.Lathe("chillRing", new[]
            {
                new Vector2(0.45f, 0f), new Vector2(0.55f, 0.02f), new Vector2(0.45f, 0.04f),
            }, 16, false), Art.Mat.Glow, Vector3.up * 0.03f);
            Art.ArtLibrary.SetGlow(ring.GetComponent<Renderer>(), ice);
            for (int i = 0; i < 5; i++)
            {
                float a = i * 72f;
                var shard = Art.MeshKit.Part(root.transform, "Shard" + i, Art.MeshKit.Lathe("iceShard", new[] { new Vector2(0.05f, 0f), new Vector2(0f, 0.25f) }, 4),
                    Art.Mat.Glow, Quaternion.Euler(0f, a, 0f) * new Vector3(0f, centerHeight * 0.9f, 0.45f), new Vector3(15f, a, 0f));
                Art.ArtLibrary.SetGlow(shard.GetComponent<Renderer>(), ice);
            }
            foreach (var r in root.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return root;
        }

        public void ServerHeal(float amount)
        {
            if (!IsServer || amount <= 0f) return;
            Current.Value = Mathf.Min(Max.Value, Current.Value + amount);
        }

        public void ServerSetFraction(float fraction)
        {
            if (!IsServer) return;
            Current.Value = Max.Value * Mathf.Clamp01(fraction);
        }
    }
}
