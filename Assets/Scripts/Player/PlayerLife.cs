using System.Collections.Generic;
using Deepbound.Art;
using Deepbound.Combat;
using Deepbound.Core;
using Deepbound.Progression;
using Unity.Netcode;
using UnityEngine;
using Deepbound.World;
using UnityEngine.InputSystem;

namespace Deepbound.Player
{
    /// <summary>
    /// Downed and revive, R.E.P.O.-style (design doc Q11, decided 2026-10-13).
    ///
    /// <para>
    /// At zero health a player is not dead — they are downed: kneeling, unable to move or
    /// fight, waiting for a teammate. A teammate holding F nearby for
    /// <see cref="ReviveSeconds"/> brings them back at partial health. If every player is
    /// downed at once, the party has wiped and the run ends (see DungeonDirector).
    /// </para>
    ///
    /// Server-authoritative: the reviver only reports "I am holding F on this person"; the
    /// server checks distance and owns the progress bar.
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class PlayerLife : NetworkBehaviour
    {
        public const float ReviveSeconds = 3f;
        public const float ReviveRange = 2.6f;
        private const float ReviveHealthFraction = 0.35f;

        /// <summary>Every player on this peer, for wipe checks, revive targeting and AI.</summary>
        public static readonly List<PlayerLife> All = new List<PlayerLife>();

        public readonly NetworkVariable<bool> Downed = new NetworkVariable<bool>();

        /// <summary>Downed and unrevived this long (while an ally still stands) and you bleed out.</summary>
        public const float BleedOutSeconds = 40f;

        /// <summary>Seconds bled so far while downed (server-written, for the HUD).</summary>
        public readonly NetworkVariable<float> Bleed = new NetworkVariable<float>();

        /// <summary>Bled out: only your Soul Skull, carried to the waystone, brings you back.</summary>
        public readonly NetworkVariable<bool> Soul = new NetworkVariable<bool>();

        /// <summary>Seconds left before a downed player bleeds out.</summary>
        public float BleedLeft => Mathf.Max(0f, BleedOutSeconds - Bleed.Value);

        /// <summary>
        /// Hidden from enemies (the Rogue's Smoke Bomb): no enemy will pick, keep, or be
        /// pulled onto this player. Server-written; teammates see a faint marker instead.
        /// </summary>
        public readonly NetworkVariable<bool> Hidden = new NetworkVariable<bool>();

        /// <summary>True if enemies may target this player right now.</summary>
        public bool Targetable => !Downed.Value && !Hidden.Value && Health.IsAlive && Time.time >= VeiledUntil;

        /// <summary>Server: inside a Rogue's smoke cloud (Smoke Screen) — enemies cannot pick you.</summary>
        public float VeiledUntil { get; set; }

        /// <summary>Whether an enemy may target this health: players must be <see cref="Targetable"/>.</summary>
        public static bool CanTarget(Health h) => h != null && h.IsAlive && (!h.TryGetComponent<PlayerLife>(out var p) || p.Targetable);
        public readonly NetworkVariable<float> ReviveProgress = new NetworkVariable<float>();

        public Health Health { get; private set; }

        private PlayerLife reviver; // server: who is currently holding F on us
        private float lastReviverPing;
        private float reviveSendAt;
        private float lastDamagedAt;
        private float regenCarry;
        private PlayerProgress progress;
        private FirstPersonMotor motor;

        // Dodge: owner-side cooldown, server-side invulnerability window.
        public const float DodgeCooldown = 1.2f, DodgeInvulnerable = 0.35f, DodgeSpeed = 15f;
        public float DodgeReadyAt { get; private set; }
        public float DodgeCooldownNow => progress != null && progress.HasRelic(Loot.Relic.GhostLantern) ? DodgeCooldown * 0.5f : DodgeCooldown;
        private float invulnerableUntil;

        private void Awake()
        {
            Health = GetComponent<Health>();
            progress = GetComponent<PlayerProgress>();
            motor = GetComponent<FirstPersonMotor>();
        }

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            if (IsServer)
            {
                Health.Died += HandleDied;
                Health.DamagedServer += (lost, from) => { lastDamagedAt = Time.time; ServerRecordHit(lost, from); };
                // Registered first, so a dodged hit is gone before blocks or relics see it.
                Health.IncomingModifiers.Insert(0, (dmg, from) => Time.time < invulnerableUntil ? 0f : dmg);
                // The cat's whisker: a one-in-a-hundred chance to slip a hit entirely.
                Health.IncomingModifiers.Insert(1, (dmg, from) =>
                {
                    if (progress == null || !progress.CatWhisker.Value || dmg <= 0f || Random.value >= PlayerProgress.WhiskerEvade) return dmg;
                    EvadedRpc();
                    return 0f;
                });
            }
            Downed.OnValueChanged += HandleDownedChanged;
        }

        public override void OnNetworkDespawn()
        {
            All.Remove(this);
            if (IsServer) Health.Died -= HandleDied;
            Downed.OnValueChanged -= HandleDownedChanged;
        }

        public override void OnDestroy()
        {
            All.Remove(this);
            base.OnDestroy();
        }

        public bool CanAct => !Downed.Value && !World.Carryable.IsCarrying(NetworkObjectId);

        // ---- Death recap: the last hits before going down (League's death recap). ----
        private struct HitRecord { public string Who; public float Amount, Time; }
        private readonly List<HitRecord> recentHits = new List<HitRecord>();

        private void ServerRecordHit(float lost, ulong attackerId)
        {
            if (lost <= 0f) return;
            recentHits.Add(new HitRecord { Who = Combat.Health.NameOf(attackerId), Amount = lost, Time = Time.time });
            if (recentHits.Count > 12) recentHits.RemoveAt(0);
        }

        /// <summary>Owner: what put you down, newest last, and the killing blow's source.</summary>
        public static event System.Action<string, string> DownedRecap;

        [Rpc(SendTo.Owner)]
        private void RecapRpc(string killer, string lines) => DownedRecap?.Invoke(killer, lines);

        private void ServerSendRecap()
        {
            // Sum by source over the last 10 seconds, the biggest first.
            var totals = new Dictionary<string, (float sum, int n)>();
            foreach (var r in recentHits)
            {
                if (Time.time - r.Time > 10f) continue;
                totals.TryGetValue(r.Who, out var t);
                totals[r.Who] = (t.sum + r.Amount, t.n + 1);
            }
            var order = new List<KeyValuePair<string, (float sum, int n)>>(totals);
            order.Sort((a, b) => b.Value.sum.CompareTo(a.Value.sum));
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < order.Count && i < 5; i++)
                sb.Append(order[i].Key).Append(order[i].Value.n > 1 ? "  x" + order[i].Value.n : "").Append('\t').Append(Mathf.RoundToInt(order[i].Value.sum)).Append('\n');
            string killer = recentHits.Count > 0 ? recentHits[recentHits.Count - 1].Who : "the dark";
            recentHits.Clear();
            RecapRpc(killer, sb.ToString());
        }

        private void HandleDied(Health _)
        {
            if (progress != null && progress.ServerTryPhoenix())
            {
                Health.ServerSetFraction(progress.PhoenixFraction);
                return;
            }
            if (progress != null) progress.Downs.Value++;
            string by = Health.LastHitFrom == ulong.MaxValue ? "no attacker"
                : NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(Health.LastHitFrom, out var killer) ? killer.name : "#" + Health.LastHitFrom;
            Debug.Log("[Down] " + name + " went down at " + transform.position.ToString("F1") + ", last hit by " + by + " from " + Health.LastHitAt.ToString("F1"));
            Downed.Value = true;
            ReviveProgress.Value = 0f;
            Bleed.Value = 0f;
            bleedCarry = 0f;
            ServerSendRecap();
        }

        /// <summary>Server: the bleed-out ran its course. Leave a Soul Skull for the others to carry.</summary>
        public void ServerBleedOut()
        {
            if (!IsServer || !Downed.Value || Soul.Value) return;
            Soul.Value = true;
            Bleed.Value = BleedOutSeconds;
            ReviveProgress.Value = 0f;
            reviver = null;
            Debug.Log("[Down] " + name + " bled out");
            if (DungeonDirector.Instance != null) DungeonDirector.Instance.ServerSpawnSoulSkull(this);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void EvadedRpc()
        {
            UI.GameHud.ShowCombo(transform.position + Vector3.up * 2.1f, "EVADED (whisker)", new Color(0.85f, 0.85f, 0.7f));
            Sfx.Play(Sound.Dodge, transform.position + Vector3.up, 0.6f, 1.4f);
        }

        private void HandleDownedChanged(bool was, bool now)
        {
            if (was && !now) Sfx.Play(Sound.Revive, transform.position);
        }

        // ------------------------------------------------------------------
        // Reviving
        // ------------------------------------------------------------------

        /// <summary>
        /// Sent by the reviving player's client every ~0.2s while F is held. Progress decays
        /// if the pings stop, so letting go — or getting knocked away — loses it gradually
        /// rather than instantly.
        /// </summary>
        [Rpc(SendTo.Server)]
        public void ReviveHoldRpc(NetworkObjectReference reviverRef)
        {
            if (!Downed.Value || Soul.Value || !reviverRef.TryGet(out var reviverObject)) return;
            var r = reviverObject.GetComponent<PlayerLife>();
            if (r == null || r == this || r.Downed.Value) return;
            if (Vector3.Distance(r.transform.position, transform.position) > ReviveRange + 0.5f) return;

            reviver = r;
            lastReviverPing = Time.time;
        }

        private void Update()
        {
            if (IsOwner)
            {
                OwnerReviveInput();
                OwnerDodgeInput();
            }
            if (!IsServer) return;
            if (!Downed.Value)
            {
                ServerRegen();
                return;
            }

            if (Soul.Value) return;
            bool beingRevived = reviver != null && Time.time - lastReviverPing < 0.35f && !reviver.Downed.Value &&
                                Vector3.Distance(reviver.transform.position, transform.position) <= ReviveRange + 0.5f;

            // Bleeding out: only while someone is still standing to save you, and not while they try.
            if (!beingRevived && !DungeonDirector.TestMode && AnyAllyStanding())
            {
                bleedCarry += Time.deltaTime;
                if (Bleed.Value + bleedCarry >= BleedOutSeconds) { bleedCarry = 0f; ServerBleedOut(); return; }
                // Synced in quarter-second steps, not every frame.
                if (bleedCarry >= 0.25f) { Bleed.Value += bleedCarry; bleedCarry = 0f; }
            }

            // The Cleric's passive, Grace: revives twice as fast.
            float rate = beingRevived && reviver.progress != null && reviver.progress.ClassId.Value == Classes.ClassId.Cleric ? 2f : 1f;
            float progress = ReviveProgress.Value + (beingRevived ? Time.deltaTime * rate : -Time.deltaTime * 0.5f);
            progress = Mathf.Clamp(progress, 0f, ReviveSeconds);

            if (progress >= ReviveSeconds)
            {
                if (reviver != null && reviver.progress != null) reviver.progress.Revives.Value++;
                ServerRevive(ReviveHealthFraction);
                return;
            }

            if (Mathf.Abs(progress - ReviveProgress.Value) > 0.01f || progress == 0f) ReviveProgress.Value = progress;
        }

        /// <summary>
        /// Owner: Left Ctrl (or C) dashes in the direction you are moving — backwards if you
        /// are not — with a brief window in which nothing can hurt you. Every class has it;
        /// it is the universal answer to a telegraph you read too late.
        /// </summary>
        private void OwnerDodgeInput()
        {
            var keyboard = Keyboard.current;
            if (Downed.Value || keyboard == null || motor == null || !motor.HasControl || Time.time < DodgeReadyAt) return;
            if (!keyboard.leftCtrlKey.wasPressedThisFrame && !keyboard.cKey.wasPressedThisFrame) return;

            var input = motor.MoveInput;
            // Dodging while sprinting forward is a slide instead: low and fast, and a kick
            // during it is a slide kick. Same i-frames, same cooldown.
            if (!(input.y > 0.5f && Mathf.Abs(input.x) < 0.5f && motor.TryStartSlide()))
            {
                var dir = input.sqrMagnitude > 0.01f ? transform.right * input.x + transform.forward * input.y : -transform.forward;
                dir.y = 0f;
                motor.AddImpulse(dir.normalized * DodgeSpeed);
            }
            DodgeReadyAt = Time.time + DodgeCooldownNow;
            DodgeRpc();
        }

        [Rpc(SendTo.Server)]
        private void DodgeRpc()
        {
            invulnerableUntil = Time.time + DodgeInvulnerable;
            if (progress != null) progress.ServerOnDodge();
            DodgeFxRpc();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void DodgeFxRpc() => Sfx.Play(Sound.Dodge, transform.position + Vector3.up, 0.7f);

        /// <summary>Owner: hold F next to a downed teammate. Every class can revive.</summary>
        private void OwnerReviveInput()
        {
            var keyboard = Keyboard.current;
            if (Downed.Value || keyboard == null) return;
            var target = DownedNear(transform.position, this);

            // F is contextual: a downed teammate takes priority over a chest or altar.
            if (target == null)
            {
                if (keyboard.fKey.wasPressedThisFrame && motor != null && motor.HasControl)
                {
                    var cam = GetComponent<NetworkPlayer>();
                    var use = World.Interactable.Nearest(cam.AimOrigin, cam.AimForward, progress);
                    if (use != null) use.UseRpc(NetworkObject);
                }
                return;
            }

            if (!keyboard.fKey.isPressed || Time.time < reviveSendAt) return;
            reviveSendAt = Time.time + 0.2f;
            target.ReviveHoldRpc(NetworkObject);
        }

        /// <summary>
        /// Out-of-combat regeneration, scaled by VIT. Off for a few seconds after taking
        /// damage, so it heals you between fights, not during them.
        /// </summary>
        private void ServerRegen()
        {
            if (progress == null || progress.Stats == null || !Health.IsAlive) return;
            // No passive regeneration (play-test, 2026-10-16): healing comes from the potion pouch,
            // the fountain, Holy Water and the Cleric. Only the Thornhide Jerkin still mends you, when badly hurt.
            bool desperate = progress.DesperateRegen;
            if (!desperate || Health.Current.Value >= Health.Max.Value) return;

            regenCarry += (2f + progress.Value(StatType.VIT) * 0.12f) * 3f * Time.deltaTime;
            if (regenCarry >= 1f)
            {
                float whole = Mathf.Floor(regenCarry);
                regenCarry -= whole;
                Health.ServerHeal(whole);
            }
        }

        private float bleedCarry;

        private bool AnyAllyStanding()
        {
            foreach (var p in All) if (p != null && p != this && !p.Downed.Value) return true;
            return false;
        }

        public void ServerRevive(float healthFraction)
        {
            if (!IsServer) return;
            Soul.Value = false;
            Bleed.Value = 0f;
            var skull = World.SoulSkull.For(NetworkObjectId);
            if (skull != null && skull.NetworkObject.IsSpawned) skull.NetworkObject.Despawn();
            Health.ServerSetFraction(healthFraction);
            Downed.Value = false;
            ReviveProgress.Value = 0f;
            reviver = null;
        }

        /// <summary>The nearest downed teammate within revive range of <paramref name="from"/>, or null.</summary>
        public static PlayerLife DownedNear(Vector3 from, PlayerLife exclude)
        {
            PlayerLife best = null;
            float bestDist = ReviveRange;
            foreach (var p in All)
            {
                if (p == exclude || !p.Downed.Value || p.Soul.Value) continue;
                float d = Vector3.Distance(p.transform.position, from);
                if (d <= bestDist) { best = p; bestDist = d; }
            }
            return best;
        }
    }
}
