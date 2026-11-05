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
    public enum Team : byte { Players, Enemies, Props, Allies }

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

        /// <summary>
        /// Scorched by fire (Ember Rune, Ashen Shroud, Pyromancer bolts, meteors, Judgement): on its own
        /// it does nothing, but frost meeting fire (or fire meeting frost) sets off a THERMAL SHOCK
        /// (2026-10-23: burn had no combo of its own). Synced for the tag.
        /// </summary>
        public readonly NetworkVariable<bool> Scorched = new NetworkVariable<bool>();
        private float scorchedUntil, shockReadyAt;
        private ulong scorchedBy = ulong.MaxValue;

        /// <summary>Server: deals the shock (set by <see cref="Classes.Combos"/>). Args: target, the attacker to credit.</summary>
        public static Action<Health, ulong> ThermalShock;
        public const float ShockCooldown = 4f;

        /// <summary>Server: fire singes this for <paramref name="seconds"/>; on a chilled target it shocks instead.</summary>
        public void ServerScorch(float seconds, ulong by)
        {
            if (!IsServer || !IsAlive) return;
            if (Chilled.Value && TryShock(by)) return;
            scorchedUntil = Mathf.Max(scorchedUntil, Time.time + seconds);
            scorchedBy = by;
            Scorched.Value = true;
        }

        private bool TryShock(ulong by)
        {
            if (Team != Team.Enemies || Time.time < shockReadyAt || ThermalShock == null) return false;
            shockReadyAt = Time.time + ShockCooldown;
            Scorched.Value = false;
            scorchedUntil = 0f;
            Chilled.Value = false;
            chilledUntil = 0f;
            ThermalShock(this, by);
            return true;
        }

        /// <summary>
        /// A damage shield (the Cleric's Divine Aegis): absorbs hits before health, then
        /// fades when its time runs out. Synced so the bubble and the HUD bar show it.
        /// </summary>
        public readonly NetworkVariable<float> Shield = new NetworkVariable<float>();

        /// <summary>Hunter's Mark (the Ranger): takes +25% damage from everyone. Synced for the marker icon.</summary>
        public readonly NetworkVariable<bool> Marked = new NetworkVariable<bool>();
        public const float MarkedMultiplier = 1.25f;
        private float markedUntil;

        /// <summary>
        /// BLIGHT (the Plague Doctor's poison, 2026-10-20): damage over time credited to whoever
        /// laid it, and a blighted creature takes <see cref="BlightedMultiplier"/> from everyone.
        /// </summary>
        public readonly NetworkVariable<bool> Blighted = new NetworkVariable<bool>();
        public const float BlightedMultiplier = 1.1f;
        private float blightUntil, blightDps, nextBlightTick;
        private ulong blightSource = ulong.MaxValue;
        /// <summary>Server: who laid the blight (for credit, and for Contagion).</summary>
        public ulong BlightSource => blightSource;
        /// <summary>Server: raised when a blighted creature dies (the Plague Doctor's Contagion).</summary>
        public static System.Action<Health, ulong> BlightedDied;
        /// <summary>Server: is this spot inside a Black Death cloud (set by the Plague Doctor)?</summary>
        public static System.Func<Vector3, bool> InPlagueCloud;

        public void ServerBlight(float seconds, float dps, ulong source)
        {
            if (!IsServer || !IsAlive) return;
            blightUntil = Mathf.Max(blightUntil, Time.time + seconds);
            blightDps = Mathf.Max(blightDps * (Blighted.Value ? 1f : 0f), dps);
            blightSource = source;
            if (!Blighted.Value) nextBlightTick = Time.time + 0.5f;
            Blighted.Value = true;
        }
        private Transform markIcon;
        private float shieldUntil;
        private GameObject shieldBubble;

        /// <summary>Raised on every peer when the value rises (healing). Drives green numbers.</summary>
        public static event Action<Health, float> AnyHealed;
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

        /// <summary>Server: scales damage by who dealt it (gear). Applied before DEF.</summary>
        public static Func<Health, ulong, float> ServerOutgoingMultiplier;

        /// <summary>Server: a shield on this was broken by damage (the Aegis burst).</summary>
        public static event Action<Health> ServerShieldBroken;

        /// <summary>Server: scales healing this receives (Tidewoven Robe).</summary>
        public float HealMultiplier { get; set; } = 1f;

        /// <summary>The run's omen: Glass Bones scales all damage, Famine scales healing on players.</summary>
        public static float OmenDamageScale = 1f, OmenPlayerHealScale = 1f;
        /// <summary>The floor event's extra hurt for players (Blood Tithe), set by the director.</summary>
        public static float FloorPlayerDamageScale = 1f;

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

        // ---- The body, as hit tests see it: the creature's own collider shape. ----
        private float bodyRadius = 0.4f, bodyLow = 0f, bodyHigh = 1.8f;
        private bool bodyCached;
        // A box-shaped body (a wall plug, a gong, a target) is tested as the real box, not a capsule
        // (2026-10-21: shots at a secret wall's edges and corners passed straight through it).
        private bool bodyIsBox;
        private Vector3 boxCenter, boxHalf;

        private void CacheBody()
        {
            bodyCached = true;
            if (TryGetComponent<CapsuleCollider>(out var cap))
            {
                bodyRadius = cap.radius;
                bodyLow = cap.center.y - cap.height / 2f;
                bodyHigh = cap.center.y + cap.height / 2f;
            }
            else if (TryGetComponent<CharacterController>(out var cc))
            {
                bodyRadius = cc.radius;
                bodyLow = cc.center.y - cc.height / 2f;
                bodyHigh = cc.center.y + cc.height / 2f;
            }
            else if (TryGetComponent<BoxCollider>(out var box))
            {
                bodyRadius = Mathf.Max(box.size.x, box.size.z) * 0.5f;
                bodyLow = box.center.y - box.size.y / 2f;
                bodyHigh = box.center.y + box.size.y / 2f;
                bodyIsBox = true;
                boxCenter = box.center;
                boxHalf = box.size * 0.5f;
            }
            else { bodyRadius = 0.4f; bodyLow = 0f; bodyHigh = centerHeight * 2f; }
        }

        /// <summary>Re-read the body from the collider (after it is resized).</summary>
        public void InvalidateBody() => bodyCached = false;

        /// <summary>The body's horizontal radius (world).</summary>
        public float BodyRadius { get { if (!bodyCached) CacheBody(); return bodyRadius; } }

        /// <summary>Distance from a point to the body's surface (negative inside).</summary>
        public float DistanceToBody(Vector3 p)
        {
            if (!bodyCached) CacheBody();
            if (bodyIsBox)
            {
                // Signed distance to the oriented box (in the box's own space; these props are unscaled).
                var q = transform.InverseTransformPoint(p) - boxCenter;
                q = new Vector3(Mathf.Abs(q.x) - boxHalf.x, Mathf.Abs(q.y) - boxHalf.y, Mathf.Abs(q.z) - boxHalf.z);
                float outside = Vector3.Max(q, Vector3.zero).magnitude;
                float inside = Mathf.Min(Mathf.Max(q.x, Mathf.Max(q.y, q.z)), 0f);
                return outside + inside;
            }
            float lo = transform.position.y + bodyLow + bodyRadius, hi = transform.position.y + bodyHigh - bodyRadius;
            if (hi < lo) hi = lo;
            var axis = new Vector3(transform.position.x, Mathf.Clamp(p.y, lo, hi), transform.position.z);
            return Vector3.Distance(p, axis) - bodyRadius;
        }

        /// <summary>Does a straight path come within <paramref name="extra"/> of the body? Sampled finely enough for any projectile step.</summary>
        public bool SegmentTouchesBody(Vector3 from, Vector3 to, float extra)
        {
            int n = Mathf.Clamp(Mathf.CeilToInt(Vector3.Distance(from, to) / 0.15f), 1, 24);
            for (int i = 0; i <= n; i++)
                if (DistanceToBody(Vector3.Lerp(from, to, i / (float)n)) <= extra) return true;
            return false;
        }

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
            else if (current > previous && previous > 0f && Time.time - spawnedAt > 0.5f)
            {
                AnyHealed?.Invoke(this, current - previous);
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
        private static Health zoneBoostTarget;
        private static float zoneBoost = 1f;

        /// <summary>Server: while set, damage to <paramref name="target"/> is scaled (a projectile's headshot, for custom hit handlers).</summary>
        /// <summary>Owner: a hit landed on you from this point (for the HUD's direction marker).</summary>
        public event Action<Vector3> HurtFrom;

        [Rpc(SendTo.Owner)]
        private void HurtFromRpc(Vector3 from) => HurtFrom?.Invoke(from);

        public static void SetZoneBoost(Health target, float multiplier) { zoneBoostTarget = target; zoneBoost = multiplier; }

        public const float DefenseWeight = 0.5f, MinDamageFraction = 0.2f;

        /// <summary>Server: who hit this last, and from where (for diagnosing deaths).</summary>
        public ulong LastHitFrom { get; private set; } = ulong.MaxValue;

        /// <summary>Server: who dealt a hit to <paramref name="victim"/>; with no attacker, poison if they are poisoned, else a trap.</summary>
        public static string NameOfHit(ulong id, Health victim)
        {
            var nm = NetworkManager.Singleton;
            bool nobody = id == ulong.MaxValue || nm == null || !nm.SpawnManager.SpawnedObjects.ContainsKey(id);
            if (nobody && victim != null && victim.TryGetComponent<Player.PlayerDebuffs>(out var deb) && deb.Poisoned) return "Poison";
            return nobody ? "Traps and afflictions" : NameOf(id);
        }

        /// <summary>Server: a readable name for whatever has this object id (bosses and champions by title).</summary>
        public static string NameOf(ulong id)
        {
            var nm = NetworkManager.Singleton;
            if (id == ulong.MaxValue || nm == null || !nm.SpawnManager.SpawnedObjects.TryGetValue(id, out var obj) || obj == null) return "Traps and afflictions";
            if (obj.TryGetComponent<Enemies.BossBrain>(out var boss)) return boss.DisplayName;
            if (obj.TryGetComponent<Enemies.EnemyBrain>(out var brain) && brain.IsChampion) return brain.Title.Value.ToString();
            string n = obj.name.Replace("(Clone)", "").Trim();
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < n.Length; i++)
            {
                if (i > 0 && char.IsUpper(n[i]) && !char.IsUpper(n[i - 1])) sb.Append(' ');
                sb.Append(n[i]);
            }
            string pretty = sb.ToString();
            if (obj.TryGetComponent<Enemies.EnemyBrain>(out var e) && e.Elite.Value) pretty = "Elite " + pretty;
            return pretty;
        }
        public Vector3 LastHitAt { get; private set; }

        /// <summary>
        /// Names the next hit for the test bots' damage-by-source tally (<see cref="Networking.AutoPlay"/>). Changes nothing
        /// about the hit; added 2026-11-04 because the totals alone could not say what was making a Mage out-damage a Knight 7:1.
        /// </summary>
        public static string DamageTag;

        /// <summary>Test bots: fires before <see cref="ServerAnyDamaged"/>, so a proc set off by this hit cannot get in first.</summary>
        public static Action<Health, float, ulong, string> ServerHitTagged;
        public Health Tag(string source) { DamageTag = source; return this; }

        public float ServerDamage(float amount, Vector3 attackerPosition, ulong attackerId = ulong.MaxValue)
        {
            string tag = DamageTag;
            DamageTag = null;
            if (!IsServer || !IsAlive || amount <= 0f) return 0f;
            LastHitFrom = attackerId;
            if (zoneBoostTarget == this) amount *= zoneBoost;
            amount *= OmenDamageScale;
            if (Team == Team.Players) amount *= FloorPlayerDamageScale;
            LastHitAt = attackerPosition;
            if (ServerOutgoingMultiplier != null && attackerId != ulong.MaxValue) amount *= ServerOutgoingMultiplier(this, attackerId);

            // DEF turns aside half its value from each hit, never more than 80% of it: armour
            // matters, but the rank and file can always still hurt you.
            float dealt = Mathf.Max(amount - Defense * DefenseWeight, amount * MinDamageFraction);
            if (Marked.Value) dealt *= MarkedMultiplier;
            if (Blighted.Value) dealt *= BlightedMultiplier;
            if (Team == Team.Enemies && InPlagueCloud != null && InPlagueCloud(transform.position)) dealt *= 1.25f;
            foreach (var modifier in IncomingModifiers) dealt = modifier(dealt, attackerPosition);
            if (dealt <= 0f) return 0f;
            if (Team == Team.Players && (attackerPosition - transform.position).sqrMagnitude > 0.25f) HurtFromRpc(attackerPosition);

            // Shields soak what is left before health does.
            if (Shield.Value > 0f)
            {
                float absorbed = Mathf.Min(Shield.Value, dealt);
                Shield.Value -= absorbed;
                dealt -= absorbed;
                if (Shield.Value <= 0.01f)
                {
                    Shield.Value = 0f;
                    ServerShieldBroken?.Invoke(this);
                }
                if (dealt <= 0f) return 0f;
            }

            // Overkill is not damage: stats and lifesteal see only the health actually lost.
            float lost = Mathf.Min(dealt, Current.Value);
            Current.Value = Mathf.Max(0f, Current.Value - dealt);
            DamagedServer?.Invoke(lost, attackerId);
            ServerHitTagged?.Invoke(this, lost, attackerId, tag);
            ServerAnyDamaged?.Invoke(this, lost, attackerId);

            if (Current.Value <= 0f && Blighted.Value) { Blighted.Value = false; BlightedDied?.Invoke(this, blightSource); }
            if (Current.Value <= 0f) Died?.Invoke(this);
            return dealt;
        }

        /// <summary>Server: does chilling this target freeze it solid (it is standing in water)?</summary>
        public static Func<Health, bool> FreezesWhenChilled;
        public static Action<Health> FrozenFx;
        private float freezeReadyAt;

        public void ServerChill(float seconds)
        {
            if (!IsServer || !IsAlive) return;
            if (Scorched.Value && TryShock(scorchedBy)) return;
            if (Team == Team.Enemies && Time.time >= freezeReadyAt && FreezesWhenChilled != null && FreezesWhenChilled(this) &&
                TryGetComponent<ICrowdControl>(out var cc))
            {
                freezeReadyAt = Time.time + 4f;
                cc.ServerStun(1.2f);
                FrozenFx?.Invoke(this);
            }
            chilledUntil = Mathf.Max(chilledUntil, Time.time + seconds);
            Chilled.Value = true;
        }

        public void ServerMark(float seconds)
        {
            if (!IsServer || !IsAlive) return;
            markedUntil = Mathf.Max(markedUntil, Time.time + seconds);
            Marked.Value = true;
        }

        /// <returns>True if the target was chilled (and no longer is).</returns>
        public bool ServerConsumeChill()
        {
            if (!IsServer || !Chilled.Value) return false;
            Chilled.Value = false;
            chilledUntil = 0f;
            return true;
        }

        /// <summary>Server: grant a shield. Re-casting refreshes it to the larger of the two.</summary>
        public void ServerShield(float amount, float seconds)
        {
            if (!IsServer || !IsAlive) return;
            Shield.Value = Mathf.Max(Shield.Value, amount);
            shieldUntil = Time.time + seconds;
        }

        private float nextStatusMote;

        private void Update()
        {
            // Everyone: frost and fire show on the body, not only in its tag (combo set-ups read at a glance).
            if ((Chilled.Value || Scorched.Value) && Team == Team.Enemies && Time.time >= nextStatusMote)
            {
                nextStatusMote = Time.time + 0.22f;
                var cam = Art.ViewCamera.Current;
                if (cam != null && (cam.transform.position - transform.position).sqrMagnitude < 625f)
                {
                    if (Chilled.Value) Art.Vfx.Mote(transform.position, new Color(0.65f, 0.88f, 1f));
                    if (Scorched.Value) Art.Vfx.Mote(transform.position, new Color(1f, 0.55f, 0.2f));
                }
            }
            if (IsServer && Chilled.Value && (Time.time >= chilledUntil || !IsAlive)) Chilled.Value = false;
            if (IsServer && Scorched.Value && (Time.time >= scorchedUntil || !IsAlive)) Scorched.Value = false;
            if (IsServer && Shield.Value > 0f && (Time.time >= shieldUntil || !IsAlive)) Shield.Value = 0f;
            if (IsServer && Marked.Value && (Time.time >= markedUntil || !IsAlive)) Marked.Value = false;
            if (IsServer && Blighted.Value)
            {
                if (!IsAlive || Time.time >= blightUntil) { Blighted.Value = false; blightDps = 0f; }
                else if (Time.time >= nextBlightTick)
                {
                    nextBlightTick = Time.time + 0.5f;
                    var src = blightSource;
                    ServerDamage(blightDps * 0.5f, transform.position, src);
                }
            }

            bool marked = Marked.Value && IsAlive;
            if (marked && markIcon == null) markIcon = BuildMarkIcon();
            if (markIcon != null)
            {
                if (markIcon.gameObject.activeSelf != marked) markIcon.gameObject.SetActive(marked);
                if (marked) markIcon.Rotate(0f, 120f * Time.deltaTime, 0f, Space.World);
            }

            bool shielded = Shield.Value > 0f && IsAlive;
            if (shielded && shieldBubble == null) shieldBubble = BuildShieldBubble();
            if (shieldBubble != null && shieldBubble.activeSelf != shielded) shieldBubble.SetActive(shielded);

            bool show = Chilled.Value && IsAlive;
            if (show && chillAura == null) chillAura = BuildChillAura();
            if (chillAura != null && chillAura.activeSelf != show) chillAura.SetActive(show);
        }

        /// <summary>A spinning red diamond over the head: "everyone hit this one".</summary>
        private Transform BuildMarkIcon()
        {
            var icon = Art.MeshKit.Part(transform, "HuntersMark", Art.MeshKit.Lathe("markGem", new[]
            {
                new Vector2(0f, -0.16f), new Vector2(0.12f, 0f), new Vector2(0f, 0.16f),
            }, 4, true), Art.Mat.Glow, Vector3.up * (centerHeight * 2f + 0.55f));
            Art.ArtLibrary.SetGlow(icon.GetComponent<Renderer>(), new Color(1f, 0.15f, 0.1f) * 2.2f);
            icon.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return icon.transform;
        }

        /// <summary>A faint golden sphere around the body: visible to the party, not in the owner's face.</summary>
        private GameObject BuildShieldBubble()
        {
            var root = new GameObject("AegisBubble");
            root.transform.SetParent(transform, false);
            var ring = Art.MeshKit.Part(root.transform, "Halo", Art.MeshKit.Lathe("aegisRing", new[]
            {
                new Vector2(0.62f, 0f), new Vector2(0.68f, 0.02f), new Vector2(0.62f, 0.04f),
            }, 20, false), Art.Mat.Glow, Vector3.up * (centerHeight + 0.1f));
            Art.ArtLibrary.SetGlow(ring.GetComponent<Renderer>(), new Color(1f, 0.85f, 0.4f) * 1.8f);
            var foot = Art.MeshKit.Part(root.transform, "Ward", Art.MeshKit.Lathe("aegisFoot", new[]
            {
                new Vector2(0.55f, 0f), new Vector2(0.62f, 0.02f), new Vector2(0.55f, 0.04f),
            }, 20, false), Art.Mat.Glow, Vector3.up * 0.04f);
            Art.ArtLibrary.SetGlow(foot.GetComponent<Renderer>(), new Color(1f, 0.85f, 0.4f) * 1.4f);
            foreach (var r in root.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return root;
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
            float cursed = Team == Team.Players && TryGetComponent<Player.PlayerDebuffs>(out var deb) ? deb.HealMultiplier : 1f;
            Current.Value = Mathf.Min(Max.Value, Current.Value + amount * HealMultiplier * cursed * (Team == Team.Players ? OmenPlayerHealScale : 1f));
        }

        public void ServerSetFraction(float fraction)
        {
            if (!IsServer) return;
            Current.Value = Max.Value * Mathf.Clamp01(fraction);
        }
    }
}
