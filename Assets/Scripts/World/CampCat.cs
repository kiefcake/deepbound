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
            Lobby.S(0.6f, 0f, -8f), Lobby.S(-2.2f, 0f, -2.5f), Lobby.S(-4.6f, 0f, 5.6f), Lobby.S(-1.5f, 0f, 9.5f),
            Lobby.S(2.4f, 0f, 13f), Lobby.S(3.2f, 0f, 4f), Lobby.S(3.6f, 0f, -3.2f),
        };
        private const float WalkSpeed = 0.9f, SitSeconds = 7f;

        private Transform body, head, tail, legs;
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
            bool news = petted ? !who.CatWhisker.Value : !who.CatPeed.Value;
            who.ServerCatVerdict(petted);
            HeldBy.Value = who.NetworkObjectId;
            HeldUntil.Value = NetworkManager.ServerTime.Time + 2.5;
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
                Sfx.Play(Sound.Splash, at, 0.6f, 1.5f);
                for (int i = 0; i < 3; i++) Vfx.Embers(at + Vector3.down * 0.45f, 6, new Color(0.95f, 0.85f, 0.2f), 1.2f, 0.025f, 0.7f);
                UI.GameHud.ShowCombo(at + Vector3.up * 0.4f, "THE CAT PEES ON YOU", new Color(0.95f, 0.85f, 0.2f));
                UI.GameHud.Feed(news ? "The cat peed on " + who + ": -5% max health this run." : "The cat peed on " + who + " again.", new Color(0.95f, 0.85f, 0.2f));
            }
        }

        protected override void ServerUse(PlayerProgress who)
        {
            if (scorned.Contains(who.NetworkObjectId)) { SnubRpc(who.name); return; }
            PettedUntil.Value = NetworkManager.ServerTime.Time + 3.5;
            int coin = 0;
            // Once a visit to camp per player, it may have something for you.
            if (pettedThisVisit.Add(who.NetworkObjectId) && Random.value < 0.35f)
            {
                coin = Random.Range(5, 21);
                who.ServerAddGold(coin);
            }
            PetFxRpc(who.name, coin);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void PetFxRpc(string who, int coin)
        {
            Sfx.Play(Sound.Purr, transform.position + Vector3.up * 0.3f, 0.9f);
            UI.GameHud.ShowCombo(transform.position + Vector3.up * 0.9f, coin > 0 ? "PURR  (+" + coin + " gold)" : "PURR", new Color(1f, 0.8f, 0.55f));
            if (coin > 0) UI.GameHud.Feed("The cat drops a coin at " + who + "'s feet: +" + coin + " gold.", new Color(1f, 0.85f, 0.4f));
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
            bool petted = now < PettedUntil.Value;
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

        private void Animate(bool walking, bool petted, bool fleeing = false)
        {
            float t = Time.time;
            // Bolting: a low, fast, bounding run with the tail straight up.
            if (fleeing)
            {
                if (body != null) { body.localPosition = new Vector3(0f, 0.17f + Mathf.Abs(Mathf.Sin(t * 22f)) * 0.05f, 0f); body.localRotation = Quaternion.Euler(Mathf.Sin(t * 22f) * 8f, 0f, 0f); }
                if (legs != null) legs.localScale = new Vector3(1f, 0.8f + Mathf.Abs(Mathf.Sin(t * 22f)) * 0.3f, 1f);
                if (tail != null) tail.localRotation = Quaternion.Euler(-10f, 0f, 0f);
                if (head != null) head.localRotation = Quaternion.Euler(10f, 0f, 0f);
                return;
            }
            if (body != null) body.localPosition = new Vector3(0f, walking ? 0.2f + Mathf.Abs(Mathf.Sin(t * 9f)) * 0.015f : 0.15f, 0f);
            if (body != null) body.localRotation = Quaternion.Euler(walking ? 0f : -18f, 0f, 0f);
            if (legs != null) legs.localScale = new Vector3(1f, walking ? 1f : 0.6f, 1f);
            if (tail != null) tail.localRotation = Quaternion.Euler(-60f + Mathf.Sin(t * (petted ? 6f : 2f)) * 12f, Mathf.Sin(t * 1.3f) * 30f, 0f);
            if (head != null) head.localRotation = Quaternion.Euler(petted ? -15f : 0f, petted ? Mathf.Sin(t * 3f) * 10f : Mathf.Sin(t * 0.7f) * 20f, petted ? 8f : 0f);
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
                Fur(MeshKit.Part(head, "Ear", MeshKit.Lathe("catEar", new[] { new Vector2(0.03f, 0f), new Vector2(0f, 0.07f) }, 4), Mat.Cloth, new Vector3(s * 0.045f, 0.06f, 0f), new Vector3(-10f, 0f, s * -18f)));
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
            foreach (float x in new[] { -0.06f, 0.06f })
                foreach (float z in new[] { -0.15f, 0.15f })
                    Fur(MeshKit.Part(legs, "Leg", MeshKit.Cylinder(0.022f, 0.02f, 0.2f, 6), Mat.Cloth, new Vector3(x, -0.2f, z)));
            tail = MeshKit.Pivot(body, "Tail", new Vector3(0f, 0.04f, -0.24f));
            Fur(MeshKit.Part(tail, "Tail", MeshKit.Cylinder(0.02f, 0.012f, 0.32f, 6), Mat.Cloth, Vector3.zero));
            foreach (var r in GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }
    }
}
