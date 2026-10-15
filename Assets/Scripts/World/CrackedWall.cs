using System;
using System.Collections;
using Deepbound.Art;
using Deepbound.Combat;
using Deepbound.Player;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// The plug in a hollow wall (<see cref="WallHole"/>). Two kinds:
    /// <list type="bullet">
    /// <item><b>Secret</b>: cracked, darker masonry with a faint draught whistling through.
    /// Hit it enough and it crumbles, revealing a cache.</item>
    /// <item><b>Breach</b>: looks like any other wall. While the party fights in the room it
    /// rumbles, then bursts — and the dead pour out behind you. It cannot be broken early.</item>
    /// </list>
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class CrackedWall : NetworkBehaviour
    {
        public readonly NetworkVariable<bool> Breach = new NetworkVariable<bool>();
        public readonly NetworkVariable<Color> StoneTint = new NetworkVariable<Color>(Color.white);

        private Health health;
        private bool broken, triggered;
        private Transform visual;
        private float rumbleUntil, nextDraught;

        public bool Triggered => triggered;

        private void Awake() => health = GetComponent<Health>();

        public override void OnNetworkSpawn()
        {
            BuildVisual();
            nextDraught = Time.time + UnityEngine.Random.Range(2f, 6f);
            if (!IsServer) return;
            health.ServerSetMax(Breach.Value ? 1e6f : 60f, refill: true);
            if (Breach.Value) health.IncomingModifiers.Add((d, from) => 0f);
            health.Died += _ => ServerCrumble();
        }

        /// <summary>Server: the breach goes. Rumbles, then bursts; <paramref name="onBurst"/> spawns the ambush.</summary>
        public void ServerTriggerBreach(Action onBurst)
        {
            if (triggered || broken) return;
            triggered = true;
            StartCoroutine(BreachRoutine(onBurst));
        }

        private IEnumerator BreachRoutine(Action onBurst)
        {
            RumbleRpc();
            yield return new WaitForSeconds(1.6f);
            broken = true;
            CrumbleFxRpc(true);
            onBurst?.Invoke();
            NetworkObject.Despawn();
        }

        private void ServerCrumble()
        {
            if (broken || Breach.Value) return;
            broken = true;
            DungeonDirector.Instance?.ContractOnSecret();
            CrumbleFxRpc(false);
            NetworkObject.Despawn();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void RumbleRpc()
        {
            rumbleUntil = Time.time + 1.6f;
            Sfx.Play(Sound.Slam, transform.position + Vector3.up * 1.4f, 0.8f, 0.45f);
            Sfx.Play(Sound.Rattle, transform.position + Vector3.up * 1.4f, 1f, 0.6f);
            var local = NetworkPlayer.Local;
            if (local != null && Vector3.Distance(local.transform.position, transform.position) < 18f) local.AddShake(0.25f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void CrumbleFxRpc(bool breach)
        {
            var mid = transform.position + Vector3.up * 1.3f;
            Sfx.Play(Sound.Slam, mid, 1f, breach ? 0.6f : 0.9f);
            Sfx.Play(Sound.BoneBreak, mid, 1f, 0.5f);
            for (int i = 0; i < 4; i++)
                BoneBurst.Spawn(mid + transform.right * (i - 1.5f) * 0.6f + Vector3.up * ((i % 2) - 0.5f) * 0.8f, 0.9f, false, Mat.Rubble);
            var local = NetworkPlayer.Local;
            if (local != null && Vector3.Distance(local.transform.position, transform.position) < 15f) local.AddShake(breach ? 0.7f : 0.35f);
            if (!breach) UI.GameHud.Feed("A hidden alcove!", new Color(1f, 0.85f, 0.45f));
        }

        private void Update()
        {
            if (visual == null) return;
            // Rumbling: the stones shudder and shed dust.
            if (Time.time < rumbleUntil)
            {
                visual.localPosition = new Vector3(UnityEngine.Random.Range(-0.04f, 0.04f), UnityEngine.Random.Range(-0.02f, 0.02f), UnityEngine.Random.Range(-0.05f, 0.05f));
                if (UnityEngine.Random.value < 0.15f)
                    BoneBurst.Spawn(transform.position + Vector3.up * UnityEngine.Random.Range(0.5f, 2.6f) + transform.right * UnityEngine.Random.Range(-1f, 1f), 0.3f, false, Mat.Rubble);
            }
            else visual.localPosition = Vector3.zero;

            // A secret wall breathes: a draught whistles through it when you are close.
            if (!Breach.Value && Time.time >= nextDraught)
            {
                nextDraught = Time.time + UnityEngine.Random.Range(5f, 9f);
                var local = NetworkPlayer.Local;
                if (local != null && Vector3.Distance(local.transform.position, transform.position) < 7f)
                    Sfx.Play(Sound.Whisper, transform.position + Vector3.up * 1.4f, 0.35f, 1.6f);
            }
        }

        private void BuildVisual()
        {
            visual = MeshKit.Pivot(transform, "Visual", Vector3.zero);
            float w = WallHole.Width, h = WallHole.Height;
            // The plug sits in the wall's thickness, flush with the room face.
            var slab = MeshKit.Part(visual, "Slab", MeshKit.Box(new Vector3(w, h, 0.8f)), Mat.StoneWall, new Vector3(0f, h / 2f, 0.4f));
            var r = slab.GetComponent<Renderer>();
            if (Breach.Value)
            {
                ArtLibrary.Tint(r, StoneTint.Value);
                return;
            }

            // Secret: a shade darker, with cracks, a few loose blocks, and moss at the foot.
            ArtLibrary.Tint(r, StoneTint.Value * 0.78f);
            var rng = new System.Random((int)(transform.position.x * 31 + transform.position.z * 17));
            float x = -0.6f, y = 0.4f;
            for (int i = 0; i < 7; i++)
            {
                float nx = x + (float)rng.NextDouble() * 0.5f - 0.1f, ny = y + 0.3f + (float)rng.NextDouble() * 0.2f;
                var a = new Vector3(x, y, -0.02f); var b2 = new Vector3(nx, ny, -0.02f);
                var seg = MeshKit.Part(visual, "Crack" + i, MeshKit.Box(new Vector3(0.035f, Vector3.Distance(a, b2), 0.02f)), Mat.Dark, (a + b2) / 2f);
                seg.transform.localRotation = Quaternion.FromToRotation(Vector3.up, b2 - a);
                x = nx; y = ny;
            }
            for (int i = 0; i < 3; i++)
            {
                var p = new Vector3(((float)rng.NextDouble() - 0.5f) * (w - 0.6f), 0.4f + (float)rng.NextDouble() * (h - 0.8f), -0.06f);
                MeshKit.Part(visual, "Loose" + i, MeshKit.Box(new Vector3(0.45f, 0.22f, 0.14f)), Mat.StoneWall, p, new Vector3(0f, 0f, (float)rng.NextDouble() * 10f - 5f));
            }
            MeshKit.Part(visual, "Rubble", MeshKit.Box(new Vector3(w * 0.8f, 0.12f, 0.3f)), Mat.Rubble, new Vector3(0f, 0.06f, -0.15f));
        }
    }
}
