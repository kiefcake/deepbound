using System.Collections.Generic;
using Deepbound.Art;
using Deepbound.Progression;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// The castle cat: a small black cat with amber eyes who patrols the camp, stops to sit and
    /// wash, and lets you pet it (F). Petted, it sits, purrs, and now and then brings you a coin
    /// it found. Its route is a function of server time, so every peer sees it in the same spot
    /// without a network transform.
    /// </summary>
    public class CampCat : Interactable
    {
        private static readonly Vector3[] Route =
        {
            Lobby.S(2.4f, 0f, -9.4f), Lobby.S(-2.2f, 0f, -2.5f), Lobby.S(-4.6f, 0f, 5.6f), Lobby.S(-1.5f, 0f, 9.5f),
            Lobby.S(2.4f, 0f, 13f), Lobby.S(3.2f, 0f, 4f), Lobby.S(3.6f, 0f, -3.2f),
        };
        private const float WalkSpeed = 0.9f, SitSeconds = 7f;

        private Transform body, head, tail, legs, tailTip, earL, earR;
        private readonly List<Transform> legPivots = new List<Transform>();
        private readonly HashSet<ulong> pettedThisVisit = new HashSet<ulong>();
        public readonly NetworkVariable<double> PettedUntil = new NetworkVariable<double>();
        private float nextMeow;

        public override float Range => 1.8f;

        public override string Prompt(PlayerProgress who) => HeldBy.Value != ulong.MaxValue ? null : "Pet the cat      Shift+F  pick it up";

        /// <summary>Who is holding the cat (it sits in their arms for a moment), or none.</summary>
        public readonly NetworkVariable<ulong> HeldBy = new NetworkVariable<ulong>(ulong.MaxValue);
        public readonly NetworkVariable<double> HeldUntil = new NetworkVariable<double>();
        private Transform smile;

        // Swung at: it hisses, sprints ahead along its route (a shared time offset, so every peer agrees),
        // and will not let that player pet it or pick it up again this visit.
        public readonly NetworkVariable<double> RouteOffset = new NetworkVariable<double>();
        public readonly NetworkVariable<double> FleeStart = new NetworkVariable<double>(-100.0);
        public readonly NetworkVariable<double> FleeEnd = new NetworkVariable<double>(-100.0);
        private const float FleeSpeedUp = 4f;
        private readonly HashSet<ulong> scorned = new HashSet<ulong>();
        private readonly Dictionary<ulong, float> lastScare = new Dictionary<ulong, float>();

        private double RouteTime(double now) => now + RouteOffset.Value + System.Math.Max(0.0, System.Math.Min(now, FleeEnd.Value) - FleeStart.Value) * FleeSpeedUp;
        private bool Fleeing(double now) => now >= FleeStart.Value && now < FleeEnd.Value;

        private void ServerScared(ulong attackerId)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.SpawnManager.SpawnedObjects.TryGetValue(attackerId, out var obj) || obj == null) return;
            if (!obj.TryGetComponent<PlayerProgress>(out var who)) return;
            if (lastScare.TryGetValue(attackerId, out var at) && Time.time - at < 2f) return;
            lastScare[attackerId] = Time.time;
            double now = NetworkManager.ServerTime.Time;
            // Fold any earlier flight into the offset, then bolt for a few seconds.
            RouteOffset.Value += System.Math.Max(0.0, System.Math.Min(now, FleeEnd.Value) - FleeStart.Value) * FleeSpeedUp;
            FleeStart.Value = now;
            FleeEnd.Value = now + 2.6;
            PettedUntil.Value = 0;
            if (HeldBy.Value != ulong.MaxValue) HeldBy.Value = ulong.MaxValue;
            scorned.Add(who.NetworkObjectId);
            HissRpc(who.name);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void HissRpc(string who)
        {
            var at = transform.position + Vector3.up * 0.4f;
            Sfx.Play(Sound.Meow, at, 1f, 0.55f);
            Sfx.Play(Sound.Whisper, at, 0.8f, 1.6f);
            UI.GameHud.ShowCombo(at + Vector3.up * 0.4f, "HSSSS!", new Color(1f, 0.6f, 0.3f));
            UI.CampChatter.CatScared(who, transform.position);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SnubRpc(string who)
        {
            Sfx.Play(Sound.Meow, transform.position + Vector3.up * 0.4f, 0.8f, 0.65f);
            UI.GameHud.ShowCombo(transform.position + Vector3.up * 0.9f, "THE CAT WILL NOT HAVE YOU", new Color(1f, 0.6f, 0.3f));
            UI.GameHud.Feed("The cat remembers what " + who + " did. Not this visit.", new Color(1f, 0.65f, 0.4f));
        }

        /// <summary>
        /// Owner → server: pick the cat up. If you have not petted it this visit it pees on you
        /// (-5% max health for the run); if you have, it smiles and gives you a whisker (1% evasion, one per run).
        /// </summary>
        [Rpc(SendTo.Server)]
        public void PickUpRpc(NetworkObjectReference user)
        {
            if (!user.TryGet(out var obj) || !obj.TryGetComponent<PlayerProgress>(out var who)) return;
            if (HeldBy.Value != ulong.MaxValue || Vector3.Distance(obj.transform.position, transform.position) > Range + 1f) return;
            if (scorned.Contains(who.NetworkObjectId)) { SnubRpc(who.name); return; }
            bool petted = pettedThisVisit.Contains(who.NetworkObjectId);
            bool news = petted && !who.CatWhisker.Value;
            who.ServerCatVerdict(petted);
            HeldBy.Value = who.NetworkObjectId;
            // Unpetted it wriggles straight out of your arms.
            HeldUntil.Value = NetworkManager.ServerTime.Time + (petted ? 2.5 : 0.7);
            VerdictRpc(who.name, petted, news);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void VerdictRpc(string who, bool petted, bool news)
        {
            var at = transform.position + Vector3.up * 0.8f;
            if (petted)
            {
                Sfx.Play(Sound.Purr, at, 1f);
                Sfx.Play(Sound.Meow, at, 0.6f, 1.2f);
                if (smile != null) smile.gameObject.SetActive(true);
                Vfx.Embers(at, 10, new Color(1f, 0.85f, 0.5f), 1.2f, 0.04f, 1f);
                UI.GameHud.ShowCombo(at + Vector3.up * 0.4f, news ? "THE CAT SMILES  (+ whisker)" : "THE CAT SMILES", new Color(1f, 0.85f, 0.55f));
                UI.GameHud.Feed(news ? "The cat gives " + who + " a whisker: 1% evasion this run." : "The cat smiles at " + who + ".", new Color(1f, 0.85f, 0.55f));
            }
            else
            {
                Sfx.Play(Sound.Meow, at, 1f, 0.8f);
                UI.GameHud.ShowCombo(at + Vector3.up * 0.4f, "MRRRP! (it wriggles free)", new Color(1f, 0.75f, 0.5f));
                UI.GameHud.Feed("The cat swats " + who + " and wriggles free. Pet it first.", new Color(1f, 0.75f, 0.5f));
            }
        }

        protected override void ServerUse(PlayerProgress who)
        {
            if (scorned.Contains(who.NetworkObjectId)) { SnubRpc(who.name); return; }
            PettedUntil.Value = NetworkManager.ServerTime.Time + 3.5;
            // Then it trails after you for a while (and winds round your legs when you stop).
            FollowId.Value = who.NetworkObjectId;
            FollowUntil.Value = NetworkManager.ServerTime.Time + 3.5 + FollowSeconds;
            int coin = 0;
            // Once a visit to camp per player, it may have something for you.
            if (pettedThisVisit.Add(who.NetworkObjectId) && Random.value < 0.35f)
            {
                coin = Random.Range(5, 21);
                who.ServerAddGold(coin);
            }
            bool mouse = coin == 0 && Random.value < 0.2f;
            PetFxRpc(who.name, coin, mouse);
        }

        public const float FollowSeconds = 22f;
        /// <summary>Who the cat is trailing after being petted (none = its rounds).</summary>
        public readonly NetworkVariable<ulong> FollowId = new NetworkVariable<ulong>(ulong.MaxValue);
        public readonly NetworkVariable<double> FollowUntil = new NetworkVariable<double>();
        private Vector3 followPos;
        private bool followInit;

        [Rpc(SendTo.ClientsAndHost)]
        private void PetFxRpc(string who, int coin, bool mouse)
        {
            Sfx.Play(Sound.Purr, transform.position + Vector3.up * 0.3f, 0.9f);
            UI.GameHud.ShowCombo(transform.position + Vector3.up * 0.9f, coin > 0 ? "PURR  (+" + coin + " gold)" : "PURR", new Color(1f, 0.8f, 0.55f));
            if (coin > 0) UI.GameHud.Feed("The cat drops a coin at " + who + "'s feet: +" + coin + " gold.", new Color(1f, 0.85f, 0.4f));
            else if (mouse) UI.GameHud.Feed("The cat lays a very dead mouse at " + who + "'s feet. It is a great honour.", new Color(1f, 0.85f, 0.55f));
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            BuildVisual();
            if (IsServer && TryGetComponent<Combat.Health>(out var hp))
            {
                hp.ServerSetMax(100f, refill: true);
                hp.IncomingModifiers.Add((dealt, from) => { ServerScared(hp.LastHitFrom); return 0f; });
            }
            nextMeow = Time.time + Random.Range(8f, 16f);
        }

        // ---- The route: walk a leg, sit a while, walk the next ----
        private static float LegLength(int i) => Vector3.Distance(Route[i], Route[(i + 1) % Route.Length]);

        private static float Cycle()
        {
            float t = 0f;
            for (int i = 0; i < Route.Length; i++) t += LegLength(i) / WalkSpeed + SitSeconds;
            return t;
        }

        /// <summary>Where the cat is at server time <paramref name="time"/>: position, facing, and whether it is walking.</summary>
        private static void Pose(double time, out Vector3 pos, out Vector3 facing, out bool walking)
        {
            float t = (float)(time % Cycle());
            for (int i = 0; i < Route.Length; i++)
            {
                var a = Route[i];
                var b = Route[(i + 1) % Route.Length];
                float walk = LegLength(i) / WalkSpeed;
                facing = (b - a).normalized;
                if (t < walk) { pos = Vector3.Lerp(a, b, t / walk); walking = true; return; }
                t -= walk;
                if (t < SitSeconds) { pos = b; walking = false; return; }
                t -= SitSeconds;
            }
            pos = Route[0];
            facing = Vector3.forward;
            walking = false;
        }

        private void Update()
        {
            if (NetworkManager == null) return;
            double now = NetworkManager.ServerTime.Time;
            if (IsServer && HeldBy.Value != ulong.MaxValue && now >= HeldUntil.Value) HeldBy.Value = ulong.MaxValue;
            // Held: in the holder's arms, facing them.
            if (HeldBy.Value != ulong.MaxValue && NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(HeldBy.Value, out var holder))
            {
                // Held low against the chest, so it sits at the bottom of the holder's view rather than filling it.
                transform.position = holder.transform.position + Vector3.up * 0.75f + holder.transform.forward * 0.75f;
                transform.rotation = Quaternion.LookRotation(-holder.transform.forward);
                Animate(false, true);
                return;
            }
            if (smile != null && smile.gameObject.activeSelf) smile.gameObject.SetActive(false);
            if (IsServer && FollowId.Value != ulong.MaxValue && now >= FollowUntil.Value) FollowId.Value = ulong.MaxValue;
            // Someone has taken a seat on the carriage: the cat hops up onto the roof to see them off.
            if (CarriagePerch(out var perch))
            {
                transform.position = Vector3.Lerp(transform.position, perch, Time.deltaTime * 3f);
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(new Vector3(Lobby.Origin.x - perch.x, 0f, Lobby.Origin.z - perch.z) + Vector3.forward * 0.01f), Time.deltaTime * 3f);
                Animate(false, false);
                followInit = false;
                return;
            }
            bool petted = now < PettedUntil.Value;
            if (!petted && FollowId.Value != ulong.MaxValue && NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(FollowId.Value, out var lead))
            {
                if (!followInit) { followPos = transform.position; followInit = true; }
                var ahead = lead.transform.position;
                var toLead = ahead - followPos; toLead.y = 0f;
                float d = toLead.magnitude;
                bool walking = d > 1.4f;
                if (walking) followPos += toLead.normalized * Mathf.Min(d - 1.2f, (d > 4f ? 5.5f : 2.2f) * Time.deltaTime);
                else
                {
                    // Winding round the legs: a slow circle at the feet.
                    float a = Time.time * 1.6f;
                    var circle = ahead + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.75f;
                    var step = circle - followPos; step.y = 0f;
                    followPos += Vector3.ClampMagnitude(step, 1.4f * Time.deltaTime);
                    walking = step.magnitude > 0.05f;
                    toLead = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a));
                }
                var at2 = followPos;
                if (Physics.Raycast(at2 + Vector3.up * 2f, Vector3.down, out var fhit, 4f, ~0, QueryTriggerInteraction.Ignore)) at2.y = fhit.point.y;
                followPos.y = at2.y;
                transform.position = at2;
                if (toLead.sqrMagnitude > 0.001f) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(toLead.normalized), Time.deltaTime * 7f);
                Animate(walking, false, false, d > 4f);
                return;
            }
            if (followInit)
            {
                // Back to its rounds: glide to where its route has got to.
                followInit = false;
            }
            bool fleeing = Fleeing(now);
            // While being petted it holds still (the route resumes from where time has carried it after).
            if (!petted)
            {
                Pose(RouteTime(now), out var p, out var f, out bool walking);
                var at = Lobby.Origin + p;
                if (Physics.Raycast(at + Vector3.up * 2f, Vector3.down, out var hit, 4f, ~0, QueryTriggerInteraction.Ignore)) at.y = hit.point.y;
                transform.position = at;
                if (walking) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(f), Time.deltaTime * (fleeing ? 14f : 6f));
                Animate(walking, false, fleeing);
            }
            else Animate(false, true);

            if (Time.time >= nextMeow)
            {
                nextMeow = Time.time + Random.Range(12f, 25f);
                var cam = ViewCamera.Current;
                if (cam != null && Vector3.Distance(cam.transform.position, transform.position) < 14f) Sfx.Play(Sound.Meow, transform.position + Vector3.up * 0.3f, 0.7f);
            }
        }

        /// <summary>On the carriage roof while anyone sits in it (every peer agrees: seats are synced).</summary>
        private bool CarriagePerch(out Vector3 at)
        {
            at = default;
            if (!DungeonDirector.InLobby || HeldBy.Value != ulong.MaxValue) return false;
            bool anyone = false;
            foreach (var p in Player.PlayerLife.All) if (p != null && p.Seat.Value >= 0) { anyone = true; break; }
            if (!anyone) return false;
            var ride = FindAnyObjectByType<CarriageRide>();
            if (ride == null || ride.Roof == null) return false;
            at = ride.Roof.position;
            return true;
        }

        // Idle business while it sits: wash, stretch or loaf, chosen from the clock so every peer agrees.
        private int Idle(float t) => Mathf.FloorToInt(t / 6f + (float)(RouteOffset.Value % 1000.0)) % 3;

        private void Animate(bool walking, bool petted, bool fleeing = false, bool trotting = false)
        {
            float t = Time.time;
            // Ears flick now and then.
            if (earL != null)
            {
                float flick = Mathf.Repeat(t + 0.37f * GetInstanceID(), 3.7f) < 0.12f ? 25f : 0f;
                earL.localRotation = Quaternion.Euler(-10f - flick, 0f, 18f);
                earR.localRotation = Quaternion.Euler(-10f, 0f, -18f - (Mathf.Repeat(t, 5.3f) < 0.1f ? 25f : 0f));
            }
            // Legs: a real gait (diagonal pairs), tucked when sitting.
            float gait = t * (fleeing ? 22f : trotting ? 15f : 9f);
            for (int i = 0; i < legPivots.Count; i++)
            {
                bool pairA = i == 0 || i == 3;
                float swing = walking || fleeing || trotting ? Mathf.Sin(gait + (pairA ? 0f : Mathf.PI)) * (fleeing ? 45f : 30f) : 0f;
                legPivots[i].localRotation = Quaternion.Euler(swing, 0f, 0f);
            }
            // The tail: two segments, an S that sways (up and hooked when happy, low and lashing when bolting).
            if (tailTip != null) tailTip.localRotation = Quaternion.Euler(petted ? 35f : 20f + Mathf.Sin(t * 1.7f) * 15f, Mathf.Sin(t * 2.3f) * 25f, 0f);
            // Bolting: a low, fast, bounding run with the tail straight up.
            if (fleeing)
            {
                if (body != null) { body.localPosition = new Vector3(0f, 0.17f + Mathf.Abs(Mathf.Sin(t * 22f)) * 0.05f, 0f); body.localRotation = Quaternion.Euler(Mathf.Sin(t * 22f) * 8f, 0f, 0f); }
                if (legs != null) legs.localScale = new Vector3(1f, 0.8f + Mathf.Abs(Mathf.Sin(t * 22f)) * 0.3f, 1f);
                if (tail != null) tail.localRotation = Quaternion.Euler(-10f, 0f, 0f);
                if (head != null) head.localRotation = Quaternion.Euler(10f, 0f, 0f);
                return;
            }
            bool moving = walking || trotting;
            int idle = moving || petted ? -1 : Idle(t);
            if (body != null)
            {
                float bob = moving ? Mathf.Abs(Mathf.Sin(gait)) * 0.015f : 0f;
                // Sitting upright; a stretch (front low, rear high); a loaf (flat, paws tucked).
                body.localPosition = new Vector3(0f, moving ? 0.2f + bob : idle == 2 ? 0.1f : 0.15f, 0f);
                float pitch = moving ? 0f : idle == 1 ? 14f * Mathf.Clamp01(Mathf.Sin(Mathf.Repeat(t, 6f) / 6f * Mathf.PI) * 2f) : idle == 2 ? 0f : -18f;
                body.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            }
            if (legs != null) legs.localScale = new Vector3(1f, moving ? 1f : idle == 2 ? 0.35f : 0.6f, 1f);
            if (tail != null) tail.localRotation = Quaternion.Euler(-60f + Mathf.Sin(t * (petted ? 6f : 2f)) * 12f, Mathf.Sin(t * 1.3f) * 30f, 0f);
            if (head != null)
            {
                // Washing: head dipped to one side in little licks.
                if (idle == 0) head.localRotation = Quaternion.Euler(28f + Mathf.Abs(Mathf.Sin(t * 7f)) * 10f, 35f, 12f);
                else head.localRotation = Quaternion.Euler(petted ? -15f : 0f, petted ? Mathf.Sin(t * 3f) * 10f : Mathf.Sin(t * 0.7f) * 20f, petted ? 8f : 0f);
            }
        }

        private void BuildVisual()
        {
            var fur = new Color(0.08f, 0.07f, 0.08f);
            Renderer Fur(GameObject g) { var r = g.GetComponent<Renderer>(); ArtLibrary.Tint(r, fur); return r; }
            var scaleRoot = MeshKit.Pivot(transform, "CatScale", Vector3.zero);
            scaleRoot.localScale = Vector3.one * 1.35f;
            body = MeshKit.Pivot(scaleRoot, "Body", Vector3.up * 0.2f);
            Fur(MeshKit.Part(body, "Torso", MeshKit.Ball(0.12f, 10, 8), Mat.Cloth, Vector3.zero, default, new Vector3(1f, 0.9f, 2.1f)));
            head = MeshKit.Pivot(body, "Head", new Vector3(0f, 0.1f, 0.24f));
            Fur(MeshKit.Part(head, "Skull", MeshKit.Ball(0.085f, 10, 8), Mat.Cloth, Vector3.zero, default, new Vector3(1.1f, 0.95f, 1f)));
            Fur(MeshKit.Part(head, "Muzzle", MeshKit.Ball(0.04f, 8, 6), Mat.Cloth, new Vector3(0f, -0.02f, 0.07f)));
            foreach (float s in new[] { -1f, 1f })
            {
                var ear = MeshKit.Pivot(head, s < 0 ? "EarL" : "EarR", new Vector3(s * 0.045f, 0.06f, 0f));
                ear.localRotation = Quaternion.Euler(-10f, 0f, s * -18f);
                Fur(MeshKit.Part(ear, "Ear", MeshKit.Lathe("catEar", new[] { new Vector2(0.03f, 0f), new Vector2(0f, 0.07f) }, 4), Mat.Cloth, Vector3.zero));
                var inner = MeshKit.Part(ear, "EarInner", MeshKit.Lathe("catEarIn", new[] { new Vector2(0.018f, 0f), new Vector2(0f, 0.045f) }, 4), Mat.Cloth, new Vector3(0f, 0.004f, 0.006f));
                ArtLibrary.Tint(inner.GetComponent<Renderer>(), new Color(0.35f, 0.18f, 0.2f));
                if (s < 0) earL = ear; else earR = ear;
                var eye = MeshKit.Part(head, "Eye", MeshKit.Ball(0.016f, 6, 4), Mat.Glow, new Vector3(s * 0.035f, 0.015f, 0.075f));
                ArtLibrary.SetGlow(eye.GetComponent<Renderer>(), new Color(1f, 0.7f, 0.15f) * 1.6f);
            }
            // The smile: a little curve of light under the eyes, only when it is pleased with you.
            smile = MeshKit.Pivot(head, "Smile", new Vector3(0f, -0.035f, 0.08f));
            for (int i = -2; i <= 2; i++)
            {
                var seg = MeshKit.Part(smile, "Arc", MeshKit.Box(new Vector3(0.012f, 0.005f, 0.005f)), Mat.Glow, new Vector3(i * 0.011f, Mathf.Abs(i) * 0.004f, 0f));
                ArtLibrary.SetGlow(seg.GetComponent<Renderer>(), new Color(1f, 0.75f, 0.4f) * 1.5f);
            }
            smile.gameObject.SetActive(false);
            legs = MeshKit.Pivot(body, "Legs", Vector3.zero);
            // Four legs on their own hip pivots (front left, front right, back left, back right), with paws.
            legPivots.Clear();
            foreach (float z in new[] { 0.15f, -0.15f })
                foreach (float x in new[] { -0.06f, 0.06f })
                {
                    var hip = MeshKit.Pivot(legs, "Hip", new Vector3(x, 0f, z));
                    Fur(MeshKit.Part(hip, "Leg", MeshKit.Cylinder(0.022f, 0.02f, 0.2f, 6), Mat.Cloth, new Vector3(0f, -0.2f, 0f)));
                    Fur(MeshKit.Part(hip, "Paw", MeshKit.Ball(0.026f, 6, 4), Mat.Cloth, new Vector3(0f, -0.2f, 0.01f), default, new Vector3(1f, 0.6f, 1.3f)));
                    legPivots.Add(hip);
                }
            tail = MeshKit.Pivot(body, "Tail", new Vector3(0f, 0.04f, -0.24f));
            Fur(MeshKit.Part(tail, "Tail", MeshKit.Cylinder(0.02f, 0.016f, 0.18f, 6), Mat.Cloth, Vector3.zero));
            tailTip = MeshKit.Pivot(tail, "TailTip", new Vector3(0f, 0.18f, 0f));
            Fur(MeshKit.Part(tailTip, "Tip", MeshKit.Cylinder(0.016f, 0.008f, 0.17f, 6), Mat.Cloth, Vector3.zero));
            // A white bib and whiskers: it read as a black blob from a few metres.
            var bib = MeshKit.Part(body, "Bib", MeshKit.Ball(0.05f, 8, 6), Mat.Cloth, new Vector3(0f, 0.02f, 0.2f), default, new Vector3(1f, 1.2f, 0.6f));
            ArtLibrary.Tint(bib.GetComponent<Renderer>(), new Color(0.78f, 0.75f, 0.7f));
            foreach (float s in new[] { -1f, 1f })
                for (int w = 0; w < 2; w++)
                {
                    var wh = MeshKit.Part(head, "Whisker", MeshKit.Box(new Vector3(0.09f, 0.002f, 0.002f)), Mat.Bone, new Vector3(s * 0.07f, -0.02f - w * 0.008f, 0.08f), new Vector3(0f, s * 12f, s * (w == 0 ? 6f : -6f)));
                    ArtLibrary.Tint(wh.GetComponent<Renderer>(), new Color(0.9f, 0.9f, 0.85f));
                }
            foreach (var r in GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }
    }
}
