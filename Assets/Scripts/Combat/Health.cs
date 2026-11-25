using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.Combat
{
    public enum Team : byte { Players, Enemies, Props, Allies }

    public class Health : NetworkBehaviour
    {
        public static readonly List<Health> All = new List<Health>();

        [SerializeField] private Team team;

        public readonly NetworkVariable<float> Current = new NetworkVariable<float>(100f);
        public readonly NetworkVariable<float> Max = new NetworkVariable<float>(100f);

        public readonly NetworkVariable<bool> Chilled = new NetworkVariable<bool>();

        public readonly NetworkVariable<bool> Scorched = new NetworkVariable<bool>();
        private float scorchedUntil, shockReadyAt;
        private ulong scorchedBy = ulong.MaxValue;

        public static Action<Health, ulong> ThermalShock;
        public const float ShockCooldown = 4f;

        public void ServerScorch(float seconds, ulong by)
        {
            if (!IsServer || !IsAlive) return;
            if (Chilled.Value && TryShock(by)) return;
            scorchedUntil = Mathf.Max(scorchedUntil, Time.time + seconds);
            scorchedBy = by;
            Scorched.Value = true;
            World.OilSlick.ServerIgniteAt(transform.position, by);
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

        public readonly NetworkVariable<float> Shield = new NetworkVariable<float>();

        public readonly NetworkVariable<bool> Marked = new NetworkVariable<bool>();
        public const float MarkedMultiplier = 1.25f;
        private float markedUntil;

        public readonly NetworkVariable<bool> Blighted = new NetworkVariable<bool>();
        public const float BlightedMultiplier = 1.1f;

        public readonly NetworkVariable<byte> BlightStacks = new NetworkVariable<byte>();
        public const int MaxBlightStacks = 4;
        public const float FesterGap = 0.5f;

        public const float BlightStackStep = 0.35f;
        private float lastFester;
        private float blightUntil, blightDps, blightBase, nextBlightTick;
        private ulong blightSource = ulong.MaxValue;

        public ulong BlightSource => blightSource;

        public static System.Action<Health, ulong> BlightedDied;

        public static System.Func<Health, bool> InPlagueCloud;

        public void ServerBlight(float seconds, float dps, ulong source)
        {
            if (!IsServer || !IsAlive) return;
            blightUntil = Mathf.Max(blightUntil, Time.time + seconds);
            if (!Blighted.Value) { BlightStacks.Value = 1; lastFester = Time.time; }
            else if (BlightStacks.Value < MaxBlightStacks && Time.time - lastFester >= FesterGap) { BlightStacks.Value++; lastFester = Time.time; }

            blightBase = Mathf.Max(Blighted.Value ? blightBase : 0f, dps);
            blightDps = blightBase * (1f + BlightStackStep * (Mathf.Max(1, (int)BlightStacks.Value) - 1));
            blightSource = source;
            if (!Blighted.Value) nextBlightTick = Time.time + 0.5f;
            Blighted.Value = true;
        }

        public int BlightStackCount => Blighted.Value ? Mathf.Max(1, (int)BlightStacks.Value) : 0;

        public void ServerClearBlight()
        {
            if (!IsServer) return;
            Blighted.Value = false; blightDps = 0f; blightBase = 0f; blightUntil = 0f; BlightStacks.Value = 0;
        }

        public void ServerFesterFull()
        {
            if (!IsServer || !Blighted.Value) return;
            BlightStacks.Value = MaxBlightStacks;
            blightDps = blightBase * (1f + BlightStackStep * (MaxBlightStacks - 1));
            lastFester = Time.time;
        }
        private Transform markIcon;
        private float shieldUntil;
        private GameObject shieldBubble;

        public static event Action<Health, float> AnyHealed;
        private float chilledUntil;
        private GameObject chillAura;

        public float Defense { get; set; }

        public readonly List<Func<float, Vector3, float>> IncomingModifiers = new List<Func<float, Vector3, float>>();

        public event Action<float> Hurt;

        public static event Action<Health, float> AnyHurt;

        public event Action<Health> Died;

        public static event Action<Health, float, ulong> ServerAnyDamaged;

        public static Func<Health, ulong, float> ServerOutgoingMultiplier;

        public static event Action<Health> ServerShieldBroken;

        public static event Action<Health> AnyDied;

        public float HealMultiplier { get; set; } = 1f;

        public static float OmenDamageScale = 1f, OmenPlayerHealScale = 1f;

        public static float FloorPlayerDamageScale = 1f;

        public event Action<float, ulong> DamagedServer;

        public Team Team => team;
        public bool IsAlive => Current.Value > 0f;
        public float Fraction => Max.Value > 0f ? Current.Value / Max.Value : 0f;

        public float LastHurtTime { get; private set; } = -100f;

        public Vector3 Center => transform.position + Vector3.up * centerHeight;

        [SerializeField] private float centerHeight = 1f;
        public float CenterHeight { get => centerHeight; set => centerHeight = value; }

        private float bodyRadius = 0.4f, bodyLow = 0f, bodyHigh = 1.8f;
        private bool bodyCached;

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

        public void InvalidateBody() => bodyCached = false;

        public float BodyRadius { get { if (!bodyCached) CacheBody(); return bodyRadius; } }

        public float DistanceToBody(Vector3 p)
        {
            if (!bodyCached) CacheBody();
            if (bodyIsBox)
            {
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

        public void ServerSetMax(float max, bool refill)
        {
            if (!IsServer) return;
            Max.Value = max;
            Current.Value = refill ? max : Mathf.Min(Current.Value, max);
        }

        private static Health zoneBoostTarget;
        private static float zoneBoost = 1f;

        public event Action<Vector3> HurtFrom;

        [Rpc(SendTo.Owner)]
        private void HurtFromRpc(Vector3 from) => HurtFrom?.Invoke(from);

        public static void SetZoneBoost(Health target, float multiplier) { zoneBoostTarget = target; zoneBoost = multiplier; }

        public const float DefenseWeight = 0.5f, MinDamageFraction = 0.2f;

        public ulong LastHitFrom { get; private set; } = ulong.MaxValue;

        public static string NameOfHit(ulong id, Health victim)
        {
            var nm = NetworkManager.Singleton;
            bool nobody = id == ulong.MaxValue || nm == null || !nm.SpawnManager.SpawnedObjects.ContainsKey(id);
            if (nobody && victim != null && victim.TryGetComponent<Player.PlayerDebuffs>(out var deb) && deb.Poisoned) return "Poison";
            return nobody ? "Traps and afflictions" : NameOf(id);
        }

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

        public static string DamageTag;

        public static Action<Health, float, ulong, string> ServerHitTagged;
        public Health Tag(string source) { DamageTag = source; return this; }

        public Health Ticking() { tickNext = true; return this; }
        private static bool tickNext;
        public static bool InTick { get; private set; }

        public string LastZeroedBy { get; private set; } = "none";
        public float LastLanded { get; private set; }

        public float ServerDamage(float amount, Vector3 attackerPosition, ulong attackerId = ulong.MaxValue)
        {
            string tag = DamageTag;
            DamageTag = null;
            InTick = tickNext;
            tickNext = false;
            if (!IsServer || !IsSpawned || !IsAlive || amount <= 0f) return 0f;

            bool overwhelming = amount >= 1e5f;
            LastHitFrom = attackerId;
            if (zoneBoostTarget == this && !overwhelming) amount *= zoneBoost;
            if (!overwhelming) amount *= OmenDamageScale;
            if (Team == Team.Players) amount *= FloorPlayerDamageScale;
            LastHitAt = attackerPosition;
            if (ServerOutgoingMultiplier != null && attackerId != ulong.MaxValue && !overwhelming) amount *= ServerOutgoingMultiplier(this, attackerId);

            float dealt = overwhelming ? amount : Mathf.Max(amount - Defense * DefenseWeight, amount * MinDamageFraction);
            if (Marked.Value) dealt *= MarkedMultiplier;
            if (Blighted.Value) dealt *= BlightedMultiplier;
            if (Team == Team.Enemies && InPlagueCloud != null && InPlagueCloud(this)) dealt *= 1.25f;

            foreach (var modifier in IncomingModifiers)
            {
                float was = dealt;
                dealt = modifier(dealt, attackerPosition);
                if (!IsSpawned) return 0f;
                if (was > 0f && dealt <= 0f) LastZeroedBy = modifier.Method.DeclaringType?.Name + "." + modifier.Method.Name + " at " + Time.time.ToString("F1");
            }
            if (dealt <= 0f) return 0f;
            LastLanded = dealt;
            if (Team == Team.Players && (attackerPosition - transform.position).sqrMagnitude > 0.25f) HurtFromRpc(attackerPosition);

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

            float lost = Mathf.Min(dealt, Current.Value);
            Current.Value = Mathf.Max(0f, Current.Value - dealt);
            DamagedServer?.Invoke(lost, attackerId);
            ServerHitTagged?.Invoke(this, lost, attackerId, tag);
            ServerAnyDamaged?.Invoke(this, lost, attackerId);

            if (Current.Value <= 0f && Blighted.Value) { Blighted.Value = false; BlightedDied?.Invoke(this, blightSource); }
            if (Current.Value <= 0f) { Died?.Invoke(this); AnyDied?.Invoke(this); }
            return dealt;
        }

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

        public bool ServerConsumeChill()
        {
            if (!IsServer || !Chilled.Value) return false;
            Chilled.Value = false;
            chilledUntil = 0f;
            return true;
        }

        public void ServerShield(float amount, float seconds)
        {
            if (!IsServer || !IsAlive) return;
            Shield.Value = Mathf.Max(Shield.Value, amount);
            shieldUntil = Time.time + seconds;
        }

        private float nextStatusMote;

        private void Update()
        {
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
                if (!IsAlive || Time.time >= blightUntil) { Blighted.Value = false; blightDps = 0f; blightBase = 0f; BlightStacks.Value = 0; }
                else if (Time.time >= nextBlightTick)
                {
                    nextBlightTick = Time.time + 0.5f;
                    var src = blightSource;
                    Ticking().ServerDamage(blightDps * 0.5f, transform.position, src);
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
