using System.Collections.Generic;
using Deepbound.Art;
using Deepbound.Classes;
using Deepbound.Combat;
using Deepbound.Progression;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Deepbound.Player
{
    /// <summary>
    /// The Ranger's kit: range, control, and making everything else hit harder.
    ///
    /// <list type="bullet">
    /// <item><b>Passive — Hunter's Mark.</b> Three hits on the same enemy mark it for six
    ///   seconds: it takes +25% damage from the whole party.</item>
    /// <item><b>LMB — Longbow.</b> Hold to draw, release to loose. Damage grows with the draw;
    ///   a full draw pierces through every enemy in line.</item>
    /// <item><b>RMB — Disengage.</b> Leap backwards and up, out of reach.</item>
    /// <item><b>Q — Snare Shot.</b> (2026-11-01, replacing the Bear Trap, which waited for one enemy to walk onto a
    ///   plate.) Loose a weighted net-arrow: where it strikes, a net bursts open and everything under it is held, hurt,
    ///   and SNARED: your arrows and Volley hit it harder while it struggles.</item>
    /// <item><b>E — Volley.</b> A blue circle at your crosshair, then arrows rain on it for two
    ///   seconds.</item>
    /// </list>
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class RangerKit : NetworkBehaviour, IClassKit
    {
        public const float DrawMin = 0.15f, DrawFull = 0.85f, ArrowMinDamage = 12f, ArrowMaxDamage = 42f, ArrowSpeed = 34f;
        /// <summary>A snap shot carries 20 m; a full draw 34 m (x the anvil / Farsight range multiplier).</summary>
        public const float ArrowMinRange = 20f, ArrowMaxRange = 34f;
        /// <summary>Arrows drop: a snap shot falls fast, a full draw flies flat.</summary>
        public static float ArrowGravity(float charge) => Mathf.Lerp(7f, 2.5f, Mathf.Clamp01(charge));
        public static float ArrowRange(float charge, float mul) => Mathf.Lerp(ArrowMinRange, ArrowMaxRange, Mathf.Clamp01(charge)) * mul;
        public float AttackRange => ArrowRange(Mathf.Max(Charge, 1f), progress != null ? progress.RangeMultiplier : 1f);
        private float RangeMul => progress != null ? progress.RangeMultiplier : 1f;
        public const float DisengageCooldown = 6f, DisengageFocus = 35f;
        public const float SnareCooldown = 9f, SnareFocus = 20f, SnareRadius = 3f, SnareHold = 2f, SnareDamage = 18f;
        public const float SnareSpeed = 30f, SnareRange = 26f, SnareGravity = 4f;
        /// <summary>Snared: the Ranger's arrows and Volley deal this much more for <see cref="SnareSeconds"/>.</summary>
        public const float SnareExposed = 1.25f, SnareSeconds = 4f;
        public const float VolleyCooldown = 16f, VolleyFocus = 55f, VolleyRadius = 4f, VolleyDelay = 0.8f, VolleyDamage = 11f;
        public const int VolleyWaves = 6;
        public const float FocusMax = 100f, FocusRegen = 11f;
        /// <summary>
        /// FOCUS reworked (2026-11-22, the user: "figure out what to do with the second resource bar for classes that ...
        /// can't ever use enough"; the Ranger spent ~11/s at most against 11/s regen, so the bar never mattered). Abilities
        /// are cooldowns only now. Focus BUILDS: <see cref="FocusPerHit"/> for every arrow that lands (+<see cref="FocusFullDraw"/>
        /// for a full draw), and fades after <see cref="FocusHold"/> s without a hit. Full, the next full draw is a
        /// DEADEYE: x<see cref="DeadeyeDamage"/> damage, and it spends the bar.
        /// </summary>
        public const float FocusPerHit = 9f, FocusFullDraw = 7f, FocusHold = 5f, FocusFade = 8f, DeadeyeDamage = 2.2f;
        private float lastFocusGain = -99f, nextDeadeyeGlint;
        private bool deadeyeWasReady;
        public bool DeadeyeReady => Focus >= FocusMax - 0.5f;
        public string ResourceHint => DeadeyeReady ? "Deadeye ready: release a full draw" : "Builds from landed arrows  ·  Full: Deadeye";

        /// <summary>Server -> owner: an arrow landed; build Focus.</summary>
        [Rpc(SendTo.Owner)]
        private void FocusGainRpc(float amount) { Focus = Mathf.Min(FocusMax, Focus + amount); lastFocusGain = Time.time; }
        public const int MarkHits = 3;
        public const float MarkSeconds = 6f;

        private Health health;
        private PlayerLife life;
        private FirstPersonMotor motor;
        private NetworkPlayer player;
        private PlayerProgress progress;

        public float Focus { get; private set; }
        private float drawStart = -1f;
        private float disengageReadyAt, snareReadyAt, volleyReadyAt;

        // Server.
        private ulong markTarget;
        private int markCount;
        private readonly Dictionary<ulong, float> snaredUntil = new Dictionary<ulong, float>();
        /// <summary>Tests: how many the last snare caught.</summary>
        public int LastSnareCaught { get; private set; } = -1;
        private struct Volley { public Vector3 At, Away; public float Next; public int Left; public Dictionary<Health, int> Hits; }
        private readonly List<Volley> volleys = new List<Volley>();
        private float lastServerShot;

        private void Awake()
        {
            health = GetComponent<Health>();
            life = GetComponent<PlayerLife>();
            motor = GetComponent<FirstPersonMotor>();
            player = GetComponent<NetworkPlayer>();
            progress = GetComponent<PlayerProgress>();
        }

        private bool Active => progress.ClassId.Value == ClassId.Ranger;

        // ---- IClassKit ----

        public ClassId Class => ClassId.Ranger;
        public string ResourceName => "Focus";
        public Color ResourceColor => new Color(0.45f, 0.85f, 0.4f);
        public float Resource => Focus;
        public float ResourceMax => FocusMax;
        public void RestoreResource(float fraction) => Focus = Mathf.Min(ResourceMax, Focus + ResourceMax * fraction);
        public void ShaveCooldowns(float seconds) { snareReadyAt -= seconds; volleyReadyAt -= seconds; }
        public float Charge => drawStart < 0f ? 0f : Mathf.Clamp01((Time.time - drawStart) / DrawFull);

        public void FillAbilities(List<AbilityView> into)
        {
            float cd = progress.CooldownMultiplier;
            into.Add(new AbilityView { Key = "LMB", Name = "Longbow", Active = drawStart >= 0f });
            into.Add(new AbilityView { Key = "RMB", Name = "Disengage", Remaining = disengageReadyAt - Time.time, Cooldown = DisengageCooldown * cd, });
            into.Add(new AbilityView { Key = "Q", Name = "Snare Shot", Remaining = snareReadyAt - Time.time, Cooldown = SnareCooldownNow });
            into.Add(new AbilityView { Key = "E", Name = "Volley", Remaining = volleyReadyAt - Time.time, Cooldown = VolleyCooldown * progress.CooldownFor(AbilitySlot.E), Active = DeadeyeReady });
        }

        // ==================================================================
        // Owner
        // ==================================================================

        private void Update()
        {
            if (IsServer) ServerVolleyTick();
            if (!IsOwner || !Active)
            {
                drawStart = -1f;
                return;
            }

            if (Time.time - lastFocusGain > FocusHold && Focus > 0f) Focus = Mathf.Max(0f, Focus - FocusFade * Time.deltaTime);
            // Show, don't tell: a ready Deadeye glints gold at the bow hand (and chimes once when it fills).
            if (DeadeyeReady)
            {
                if (!deadeyeWasReady) { Sfx.Play2D(Sound.Relic, 0.5f); deadeyeWasReady = true; }
                if (Time.time >= nextDeadeyeGlint)
                {
                    nextDeadeyeGlint = Time.time + 0.5f;
                    ClassFx.Glint(player.FxHand, new Color(1f, 0.9f, 0.5f), 0.35f, 0.25f, Random.Range(0f, 90f));
                }
            }
            else deadeyeWasReady = false;
            // Drawing a bow slows you; that is the price of the big shot.
            motor.SpeedMultiplier = progress.SpeedMultiplier * (drawStart >= 0f ? 0.6f : 1f);
            motor.CanSprint = drawStart < 0f;

            bool canAct = life.CanAct && motor.ActionsReady;
            var mouse = Mouse.current;
            var keyboard = Keyboard.current;
            if (!canAct)
            {
                drawStart = -1f;
                return;
            }

            // --- LMB: draw while held, loose on release ---
            if (UI.Binds.PrimaryDown) drawStart = Time.time;
            if (drawStart >= 0f && !UI.Binds.PrimaryHeld)
            {
                float held = Time.time - drawStart;
                drawStart = -1f;
                if (held >= DrawMin)
                {
                    float charge = Mathf.Clamp01(held / (DrawFull * progress.AttackTimeMultiplier));
                    player.ViewModelCast(charge >= 1f);
                    bool deadeye = charge >= 1f && DeadeyeReady;
                    if (deadeye) { Focus = 0f; UI.GameHud.ShowCombo(player.AimOrigin + player.AimForward * 3f, "DEADEYE", new Color(0.6f, 1f, 0.55f)); }
                    ShootRpc(player.AimOrigin + player.AimForward * 0.6f, player.AimForward, charge, deadeye);
                }
            }

            // --- RMB: Disengage ---
            if (UI.Binds.SecondaryDown && Time.time >= disengageReadyAt)
            {
                disengageReadyAt = Time.time + DisengageCooldown * progress.CooldownMultiplier;
                var back = -transform.forward;
                back.y = 0f;
                motor.AddImpulse(back.normalized * 17f + Vector3.up * 6f);
                DisengageFxRpc();
            }

            // --- Q: Snare Shot along the crosshair ---
            bool freeNet = Time.time < freeNetUntil;
            if (UI.Binds.Down(UI.Bind.AbilityQ) && (Time.time >= snareReadyAt || freeNet))
            {
                if (freeNet) freeNetUntil = 0f;
                else
                {
                    snareReadyAt = Time.time + SnareCooldownNow;
                    // Second Net (rank 3 B): another, free, within 3 s.
                    if (progress.B(AbilitySlot.Q, 2)) freeNetUntil = Time.time + FreeNetWindow;
                }
                player.ViewModelCast(true);
                SnareRpc(player.AimOrigin + player.AimForward * 0.6f, player.AimForward);
            }

            // --- E: Volley at the crosshair ---
            if (UI.Binds.Down(UI.Bind.AbilityE) && Time.time >= volleyReadyAt && TryAimFloor(30f, out var volleyAt))
            {
                volleyReadyAt = Time.time + VolleyCooldown * progress.CooldownFor(AbilitySlot.E);
                player.ViewModelCast(true);
                VolleyRpc(volleyAt);
            }
        }

        private bool TryAimFloor(float range, out Vector3 spot) => player.TryAimFloor(range, out spot);

        // ==================================================================
        // Server
        // ==================================================================

        [Rpc(SendTo.Server)]
        private void ShootRpc(Vector3 origin, Vector3 direction, float charge, bool deadeye) => ServerShoot(origin, direction, charge, deadeye);

        /// <summary>Server: loose an arrow at a given draw (0-1). Public for the smoke test.</summary>
        public void ServerShoot(Vector3 origin, Vector3 direction, float charge, bool deadeye = false)
        {
            if (!life.CanAct || Time.time - lastServerShot < DrawMin * 0.8f) return;
            lastServerShot = Time.time;
            if (Vector3.Distance(origin, transform.position + Vector3.up * 1.6f) > 2.5f) origin = transform.position + Vector3.up * 1.6f;

            charge = Mathf.Clamp01(charge);
            float dmg = Mathf.Lerp(ArrowMinDamage, ArrowMaxDamage, charge) * progress.AttackMultiplier * (deadeye ? DeadeyeDamage : 1f);
            float focusGain = FocusPerHit + (charge >= 1f ? FocusFullDraw : 0f);
            bool gainedThisShot = false;
            var kit = this;
            bool pierce = charge >= 1f;
            ulong me = NetworkObjectId;
            var from = transform.position;
            // Splitshot: a full draw looses three arrows in a fan (the middle one marks, mastered).
            bool split = pierce && progress.OnPath(1);
            // Blasthead: arrows burst where they strike.
            bool blast = progress.OnPath(2);
            for (int a = split ? -1 : 0; a <= (split ? 1 : 0); a++)
            {
                var dir = Quaternion.AngleAxis(a * SplitAngle, Vector3.up) * direction;
                bool instantMark = split && a == 0 && progress.Mastered(1);
                Arrows.ServerFire(origin, dir, 0f, Team.Enemies, me, ArrowSpeed * (0.7f + 0.3f * charge), h =>
                {
                    h.Tag("RangerKit").ServerDamage(dmg * (pierce && h.Team == Team.Enemies ? Combos.TryShatter(h, progress) : 1f) * (kit != null ? kit.Snared(h) : 1f), from, me);
                    if (this == null) return;
                    if (!deadeye && !gainedThisShot && h.Team == Team.Enemies) { gainedThisShot = true; FocusGainRpc(focusGain); }
                    if (instantMark && h.Team == Team.Enemies)
                    {
                        h.ServerMark(MarkSeconds);
                        MarkFxRpc(h.Center);
                    }
                    else CountMark(h);
                }, pierce && !blast, ArrowRange(charge, RangeMul), ArrowGravity(charge), blast ? at => { if (this != null) ServerBlasthead(at, charge, dmg); } : (System.Action<Vector3>)null);
                if (a != 0) ShotFxRpc(origin, dir, charge);
            }
            ShotFxRpc(origin, direction, charge, deadeye);
        }

        /// <summary>Hunter's Mark: three hits on the same enemy in a row marks it.</summary>
        private void CountMark(Health h)
        {
            if (h.Team != Team.Enemies) return;
            if (h.NetworkObjectId != markTarget)
            {
                markTarget = h.NetworkObjectId;
                markCount = 0;
            }
            if (++markCount >= MarkHits)
            {
                markCount = 0;
                h.ServerMark(MarkSeconds);
                MarkFxRpc(h.Center);
            }
        }

        // ---- Q: Snare Shot ----

        /// <summary>The Trapper path: snares come back 40% sooner and burst 40% wider.</summary>
        private float SnareCooldownNow => SnareCooldown * progress.CooldownFor(AbilitySlot.Q);
        public float SnareRadiusNow => SnareRadius;

        // ---- Hobb's paths (2026-11-20) ----
        public const float SplitAngle = 6f, BlastRadius = 1.6f, BlastShare = 0.5f;

        /// <summary>Server: a Blasthead arrow bursts where it struck.</summary>
        private void ServerBlasthead(Vector3 at, float charge, float dmg)
        {
            bool big = charge >= 1f && progress.Mastered(2);
            float r = BlastRadius * (big ? 2f : 1f) * (0.7f + 0.3f * charge);
            AbilityKit.Blast(at, r, dmg * BlastShare, NetworkObjectId, "Blasthead", h => { h.ServerScorch(3f, NetworkObjectId); if (big) AbilityKit.Stun(h, 0.4f); });
            progress.AbilityBurstRpc(at, r, PlayerProgress.FxKind.Fire);
        }

        /// <summary>Server: the extra the Ranger's own hits deal to something still struggling in a net.</summary>
        public float Snared(Health h) => h != null && snaredUntil.TryGetValue(h.NetworkObjectId, out var until) && Time.time < until ? SnareExposed : 1f;

        [Rpc(SendTo.Server)]
        private void SnareRpc(Vector3 origin, Vector3 direction) => ServerSnareShot(origin, direction);

        /// <summary>Server: loose the net-arrow; it bursts where it strikes (an enemy, a wall, the floor). Public for the smoke test.</summary>
        public void ServerSnareShot(Vector3 origin, Vector3 direction)
        {
            if (!life.CanAct) return;
            if (Vector3.Distance(origin, transform.position + Vector3.up * 1.6f) > 2.5f) origin = transform.position + Vector3.up * 1.6f;
            // No damage of its own in flight: the net does the work where it lands.
            Arrows.ServerFire(origin, direction, 0f, Team.Enemies, NetworkObjectId, SnareSpeed, h => { }, false, SnareRange * RangeMul, SnareGravity,
                at => { if (this != null) ServerSnareBurst(at); });
            SnareShotFxRpc(origin, direction);
        }

        // ---- Ysolde's options for Snare Shot and Volley (2026-11-20) ----
        public const float FreeNetWindow = 3f, LarderRefund = 4f, LarderFocus = 15f, BlastDelay = 0.6f, ChainNetRange = 10f;
        private float freeNetUntil;

        public override void OnNetworkSpawn()
        {
            if (IsServer) Health.AnyDied += OnSomethingDied;
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer) Health.AnyDied -= OnSomethingDied;
            base.OnNetworkDespawn();
        }

        /// <summary>Server: Hunter's Larder: a snared kill readies the next net sooner.</summary>
        private void OnSomethingDied(Health dead)
        {
            if (!Active || dead == null || dead.LastHitFrom != NetworkObjectId || !progress.B(AbilitySlot.Q, 3) || Snared(dead) <= 1f) return;
            snaredUntil.Remove(dead.NetworkObjectId);
            LarderRpc();
            progress.AbilityTextRpc(dead.Center + Vector3.up, "LARDER", PlayerProgress.FxKind.Earth);
        }

        [Rpc(SendTo.Owner)]
        private void LarderRpc() { snareReadyAt -= LarderRefund; Focus = Mathf.Min(FocusMax, Focus + LarderFocus); }

        /// <summary>Server: the net opens at <paramref name="at"/>. Public for the smoke test.</summary>
        public void ServerSnareBurst(Vector3 at) => ServerSnareBurst(at, true);

        private void ServerSnareBurst(Vector3 at, bool first)
        {
            // Lie on the floor under where it struck (an arrow in a wall drops its net at the wall's foot).
            var ground = Physics.Raycast(at + Vector3.up * 0.5f, Vector3.down, out var hit, 6f, ~0, QueryTriggerInteraction.Ignore) && hit.collider.GetComponentInParent<Health>() == null ? hit.point : at;
            float r = SnareRadiusNow;
            int caught = 0;
            var net = new List<Health>();
            foreach (var h in CombatQuery.Circle(ground, r, Team.Enemies).ToArray())
            {
                if (Mathf.Abs(h.transform.position.y - ground.y) > 2.5f) continue;
                caught++;
                net.Add(h);
                h.Tag("RangerKit").ServerDamage(SnareDamage * progress.AttackMultiplier * progress.Potency(AbilitySlot.Q), ground + Vector3.up, NetworkObjectId);
                if (!h.IsAlive) continue;
                // Bosses shrug a stun off sooner (their own ServerStun), but are snared for the full time.
                if (h.TryGetComponent<ICrowdControl>(out var cc)) cc.ServerStun(SnareHold);
                if (h.TryGetComponent<Enemies.BossBrain>(out var boss)) boss.ServerAddStagger(Enemies.BossBrain.StaggerStun);
                snaredUntil[h.NetworkObjectId] = Time.time + SnareSeconds;
                // Marking Net (rank 2 A) / Barbed Net (rank 2 B).
                if (progress.A(AbilitySlot.Q, 1)) { h.ServerMark(MarkSeconds); MarkFxRpc(h.Center); }
                if (progress.B(AbilitySlot.Q, 1)) AbilityKit.Bleed(this, h, SnareDamage * progress.AttackMultiplier * 0.6f, 4f, NetworkObjectId, "Barbed Net");
                // Tether (rank 3 A): hauled into the middle.
                if (progress.A(AbilitySlot.Q, 2) && !AbilityKit.IsBoss(h)) AbilityKit.PullTo(h, ground, 0.9f);
            }
            LastSnareCaught = caught;
            ulong me = NetworkObjectId;
            float att = progress.AttackMultiplier;
            // Shock Net (rank 4 A): it shocks as it lets go.
            if (progress.A(AbilitySlot.Q, 3) && net.Count > 0)
                AbilityKit.Later(this, SnareSeconds - 0.2f, () =>
                {
                    foreach (var h in net)
                    {
                        if (h == null || !h.IsAlive || Vector3.Distance(h.transform.position, ground) > r + 2f) continue;
                        h.Tag("Shock Net").ServerDamage(SnareDamage * att * 0.8f, ground, me);
                        AbilityKit.Stun(h, 1f);
                        progress.AbilityLinkRpc(ground + Vector3.up * 0.3f, h.Center, PlayerProgress.FxKind.Storm);
                    }
                });
            // Weighted Blast (rank 5 A): the stones go off.
            if (progress.A(AbilitySlot.Q, 4))
            {
                progress.AbilityCircleRpc(ground, r, BlastDelay, PlayerProgress.FxKind.Fire);
                AbilityKit.Later(this, BlastDelay, () =>
                {
                    AbilityKit.Blast(ground, r, SnareDamage * att * 1.6f, me, "Weighted Blast");
                    progress.AbilityBurstRpc(ground, r, PlayerProgress.FxKind.Fire);
                });
            }
            // Chain Net (rank 5 B): a second net onto the nearest foe it missed.
            if (first && progress.B(AbilitySlot.Q, 4))
            {
                var next = AbilityKit.Nearest(ground, ChainNetRange, net);
                if (next != null)
                {
                    var to = next.transform.position;
                    progress.AbilityLinkRpc(ground + Vector3.up * 0.5f, to + Vector3.up * 0.5f, PlayerProgress.FxKind.Earth);
                    AbilityKit.Later(this, 0.35f, () => ServerSnareBurst(to, false));
                }
            }
            if (snaredUntil.Count > 64)
            {
                var stale = new List<ulong>();
                foreach (var kv in snaredUntil) if (kv.Value < Time.time) stale.Add(kv.Key);
                foreach (var k in stale) snaredUntil.Remove(k);
            }
            SnareBurstFxRpc(ground, r, caught);
        }

        [Rpc(SendTo.Server)]
        private void VolleyRpc(Vector3 at)
        {
            if (!life.CanAct) return;
            int waves = VolleyWaves;
            var away = at - transform.position; away.y = 0f;
            volleys.Add(new Volley { At = at, Away = away.sqrMagnitude > 0.01f ? away.normalized : transform.forward, Next = Time.time + VolleyDelay, Left = waves, Hits = new Dictionary<Health, int>() });
            // Sheltering Rain (rank 4 B): allies under the arrows take a fifth less.
            if (progress.B(AbilitySlot.E, 3)) AbilityKit.ServerDamageZone(at, VolleyRadius + 1f, VolleyDelay + waves * 0.33f, 0.8f, null, "Sheltering Rain");
            VolleyWarnRpc(at);
        }

        private void ServerVolleyTick()
        {
            for (int i = volleys.Count - 1; i >= 0; i--)
            {
                var v = volleys[i];
                if (Time.time < v.Next) continue;
                // Seeker Volley (rank 5 A): the rain follows the nearest snared or marked foe.
                if (progress.A(AbilitySlot.E, 4))
                {
                    Health seek = null; float best = 9f;
                    foreach (var h in CombatQuery.Circle(v.At, 9f, Team.Enemies))
                    {
                        if (!h.IsAlive || (!h.Marked.Value && Snared(h) <= 1f)) continue;
                        float d = Vector3.Distance(h.transform.position, v.At);
                        if (d < best) { best = d; seek = h; }
                    }
                    if (seek != null) v.At = Vector3.MoveTowards(v.At, seek.transform.position, 3f);
                }
                bool last = v.Left == 1 && progress.A(AbilitySlot.E, 3);
                float radius = VolleyRadius;
                foreach (var h in CombatQuery.Circle(v.At, radius, Team.Enemies).ToArray())
                {
                    float dmg = VolleyDamage * progress.AttackMultiplier * progress.Potency(AbilitySlot.E) * Snared(h) * (last ? 3f : 1f);
                    h.Tag(last ? "The Last Arrow" : "RangerKit").ServerDamage(dmg, v.At + Vector3.up * 6f, NetworkObjectId);
                    if (progress.A(AbilitySlot.E, 1)) h.ServerChill(2f);
                    if (progress.B(AbilitySlot.E, 1)) h.ServerScorch(3f, NetworkObjectId);
                    if (progress.B(AbilitySlot.E, 2) && !AbilityKit.IsBoss(h)) AbilityKit.Stun(h, 0.3f);
                    if (last) AbilityKit.Push(h, (h.transform.position - v.At).normalized * 10f);
                    // Rain of Marks (rank 5 B).
                    if (progress.B(AbilitySlot.E, 4) && v.Hits != null)
                    {
                        v.Hits.TryGetValue(h, out int n);
                        v.Hits[h] = ++n;
                        if (n == 3) { h.ServerMark(MarkSeconds); MarkFxRpc(h.Center); }
                    }
                    CountMark(h);
                }
                if (last) { progress.AbilityBurstRpc(v.At, radius, PlayerProgress.FxKind.Steel); progress.AbilityTextRpc(v.At + Vector3.up * 2f, "THE LAST ARROW", PlayerProgress.FxKind.Steel); }
                VolleyWaveRpc(v.At);
                // Creeping Barrage (rank 3 A): it walks away from you.
                if (progress.A(AbilitySlot.E, 2)) v.At = Vfx.Floor(v.At + v.Away * 1f + Vector3.up * 0.5f);
                v.Left--;
                v.Next = Time.time + 0.33f;
                if (v.Left <= 0) volleys.RemoveAt(i); else volleys[i] = v;
            }
        }

        // ==================================================================
        // Presentation
        // ==================================================================

        [Rpc(SendTo.ClientsAndHost)]
        private void ShotFxRpc(Vector3 origin, Vector3 direction, float charge, bool deadeye = false)
        {
            if (deadeye)
            {
                ClassFx.Deadeye(origin, direction, Mathf.Min(20f, ArrowRange(charge, RangeMul)));
                Sfx.Play(Sound.Shatter, origin, 0.6f, 1.6f);
            }
            Arrows.SpawnVisual(origin, direction, ProjectileKind.Arrow, ArrowSpeed * (0.7f + 0.3f * charge), ArrowRange(charge, RangeMul), ArrowGravity(charge));
            Sfx.Play(Sound.Bow, origin, 0.6f + 0.4f * charge, 1.2f - 0.3f * charge);
            // A full draw pierces: it leaves a green streak through everything in line (2026-11-22).
            if (!deadeye && charge > 0.9f) ClassFx.PiercingShot(origin, direction, Mathf.Min(16f, ArrowRange(charge, RangeMul)));
            if (!IsOwner) player.Rig?.Play(RigAction.Draw, 0.2f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SnareShotFxRpc(Vector3 origin, Vector3 direction)
        {
            Arrows.SpawnVisual(origin, direction, ProjectileKind.Arrow, SnareSpeed, SnareRange * RangeMul, SnareGravity);
            Sfx.Play(Sound.Bow, origin, 0.85f, 0.75f);
            if (!IsOwner) player.Rig?.Play(RigAction.Draw, 0.2f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SnareBurstFxRpc(Vector3 at, float radius, int caught)
        {
            SnareNet.Spawn(at, radius, SnareHold + 0.4f);
            Sfx.Play(Sound.Chain, at + Vector3.up * 0.4f, 0.7f, 1.25f);
            if (caught > 0) Sfx.Play(Sound.Block, at + Vector3.up * 0.4f, 0.6f, 0.8f);
            Vfx.Ring(at + Vector3.up * 0.04f, radius, new Color(0.62f, 0.5f, 0.32f), 0.35f);
            // The net slaps down: a low shove of air and grit (the net itself is the shape: SnareNet).
            ClassFx.Shock(at, radius, new Color(0.85f, 0.7f, 0.45f), 0.4f, 0.3f);
            ClassFx.Rubble(Vfx.Floor(at), 4, 2.5f);
            Vfx.Smoke(at, 2, radius * 0.4f, 1.2f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void DisengageFxRpc()
        {
            Sfx.Play(Sound.Dodge, transform.position + Vector3.up, 0.8f, 0.9f);
            SmokePuff.Spawn(transform.position, 0.5f);
            // 2026-11-22: leaves kicked up where the Ranger springs off, a green trail along the leap.
            ClassFx.Disengage(transform);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void MarkFxRpc(Vector3 at)
        {
            Sfx.Play(Sound.Relic, at, 0.6f, 1.6f);
            var red = new Color(1f, 0.3f, 0.25f);
            Vfx.Ring(Vfx.Ground(at), 1.2f, red, 0.35f);
            Vfx.Embers(at, 8, red, 2.5f, 0.05f, 0.5f);
            // A hunter's mark: a reticle snaps onto the quarry.
            ClassFx.HuntersMark(null, at + Vector3.up * 0.2f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void VolleyWarnRpc(Vector3 at)
        {
            Telegraph.Show(at, VolleyRadius, VolleyDelay, null, Telegraph.Friendly);
            if (!IsOwner) player.Rig?.Play(RigAction.Cast, 0.5f);
            // 2026-11-22: the Ranger looses a streak skyward; arrowheads ring the ground where it all comes down.
            ClassFx.FallingStreak(transform.position + Vector3.up * 1.6f, transform.position + Vector3.up * 12f);
            ClassFx.VolleyRing(at, VolleyRadius, VolleyDelay + 2.2f);
        }

        /// <summary>Each wave: a handful of arrows dropping out of the dark onto the circle.</summary>
        [Rpc(SendTo.ClientsAndHost)]
        private void VolleyWaveRpc(Vector3 at)
        {
            for (int i = 0; i < 5; i++)
            {
                var land = at + new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f)) * VolleyRadius * 0.8f;
                var from = land + new Vector3(Random.Range(-1.5f, 1.5f), 7f, Random.Range(-1.5f, 1.5f));
                Arrows.SpawnVisual(from, land - from, ProjectileKind.Arrow, 26f);
                ClassFx.FallingStreak(from, land);
                if (i < 3) Vfx.Smoke(land, 1, 0.25f, 0.8f);
            }
            Sfx.Play(Sound.Hit, at + Vector3.up, 0.5f, 1.4f);
        }
    }
}
