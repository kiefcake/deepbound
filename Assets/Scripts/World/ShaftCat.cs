using Deepbound.Art;
using Deepbound.Player;
using Deepbound.Progression;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// The cat goes down the shaft at night, and comes back (2026-10-20, the lore's open thread 4). Now and
    /// then it is found curled asleep in a side room far below camp. Pet it (F): it purrs, mends whoever
    /// petted it by <see cref="Mend"/> and lifts their afflictions, then wakes, stretches and slips off into
    /// the dark. Once per cat.
    /// </summary>
    public class ShaftCat : Interactable
    {
        public const float Mend = 0.3f, SpawnChance = 0.08f;

        public readonly NetworkVariable<bool> Woken = new NetworkVariable<bool>();

        private Transform cat, body, head;
        private float wokeAt = -1f, nextMote;

        public override float Range => 2.2f;

        public override string Prompt(PlayerProgress who) => Woken.Value ? null : "Pet the cat  (it is a long way from home)";

        protected override void ServerUse(PlayerProgress who)
        {
            if (Woken.Value || who == null) return;
            Woken.Value = true;
            var life = who.GetComponent<PlayerLife>();
            if (life != null)
            {
                life.Health.ServerHeal(life.Health.Max.Value * Mend);
                if (who.TryGetComponent<PlayerDebuffs>(out var deb)) deb.ServerCleanse();
            }
            PettedRpc(who.name);
            Invoke(nameof(ServerGone), 3.2f);
        }

        /// <summary>Server, for tests.</summary>
        public void ServerDebugUse(PlayerProgress who) => ServerUse(who);

        private void ServerGone()
        {
            if (IsSpawned) NetworkObject.Despawn();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void PettedRpc(string who)
        {
            wokeAt = Time.time;
            Sfx.Play(Sound.Purr, transform.position + Vector3.up * 0.3f, 0.9f);
            UI.GameHud.ShowCombo(transform.position + Vector3.up * 0.8f, "PURRING", new Color(1f, 0.85f, 0.6f));
            UI.GameHud.Feed("The castle cat, a long way from home. It purrs for " + who + ", then pads off into the dark.", new Color(0.85f, 0.8f, 0.7f));
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            Build();
        }

        private void Update()
        {
            if (cat == null) return;
            if (wokeAt < 0f)
            {
                // Asleep: slow breathing, and a pale mote drifting up now and then.
                body.localScale = new Vector3(1f, 1f + Mathf.Sin(Time.time * 1.8f) * 0.04f, 1f);
                if (Time.time >= nextMote) { nextMote = Time.time + 1.4f; Vfx.Speck(transform.position + new Vector3(0.12f, 0.35f, 0.08f), new Color(0.9f, 0.85f, 0.7f)); }
                return;
            }
            float t = Time.time - wokeAt;
            // Wakes, lifts its head, then trots away and fades into the dark.
            head.localRotation = Quaternion.Slerp(head.localRotation, Quaternion.Euler(-15f, 0f, 0f), Time.deltaTime * 4f);
            if (t > 0.6f && t < 0.75f) Sfx.Play(Sound.Meow, transform.position + Vector3.up * 0.3f, 0.6f);
            if (t > 1.2f) cat.localPosition += cat.forward * Time.deltaTime * 2.2f;
            if (t > 2.4f) cat.localScale = Vector3.one * Mathf.Max(0.01f, 1.25f * (1f - (t - 2.4f) / 0.8f));
        }

        private void Build()
        {
            cat = MeshKit.Pivot(transform, "ShaftCat", new Vector3(0f, 0.14f, 0f));
            cat.localScale = Vector3.one * 1.25f;
            var fur = new Color(0.26f, 0.24f, 0.25f);
            void Fur(GameObject g) => ArtLibrary.Tint(g.GetComponent<Renderer>(), fur);
            body = MeshKit.Pivot(cat, "Body", Vector3.zero);
            Fur(MeshKit.Part(body, "Loaf", MeshKit.Ball(0.13f, 12, 9), Mat.Cloth, Vector3.zero, default, new Vector3(1.25f, 0.62f, 1.05f)));
            Fur(MeshKit.Part(body, "Haunch", MeshKit.Ball(0.09f, 10, 8), Mat.Cloth, new Vector3(-0.08f, 0.01f, -0.05f), default, new Vector3(1f, 0.75f, 1f)));
            head = MeshKit.Pivot(cat, "Head", new Vector3(0.12f, 0.02f, 0.08f), new Vector3(10f, 40f, -12f));
            Fur(MeshKit.Part(head, "Skull", MeshKit.Ball(0.07f, 10, 8), Mat.Cloth, Vector3.zero, default, new Vector3(1.1f, 0.92f, 1f)));
            Fur(MeshKit.Part(head, "Muzzle", MeshKit.Ball(0.032f, 8, 6), Mat.Cloth, new Vector3(0f, -0.02f, 0.058f)));
            var white = new Color(0.9f, 0.88f, 0.84f);
            foreach (float s in new[] { -1f, 1f })
            {
                Fur(MeshKit.Part(head, "Ear", MeshKit.Lathe("catEar", new[] { new Vector2(0.026f, 0f), new Vector2(0f, 0.06f) }, 4), Mat.Cloth, new Vector3(s * 0.038f, 0.05f, -0.005f), new Vector3(-20f, 0f, s * -22f)));
                MeshKit.Part(head, "Lid", MeshKit.Box(new Vector3(0.022f, 0.004f, 0.004f)), Mat.Dark, new Vector3(s * 0.028f, 0.012f, 0.066f), new Vector3(0f, 0f, s * 12f));
                Fur(MeshKit.Part(body, "Paw", MeshKit.Ball(0.026f, 6, 5), Mat.Cloth, new Vector3(0.14f, -0.045f, 0.06f + s * 0.025f)));
                ArtLibrary.Tint(MeshKit.Part(head, "InnerEar", MeshKit.Lathe("catEarIn", new[] { new Vector2(0.016f, 0f), new Vector2(0f, 0.04f) }, 4), Mat.Cloth, new Vector3(s * 0.038f, 0.054f, 0.006f), new Vector3(-20f, 0f, s * -22f)).GetComponent<Renderer>(), new Color(0.8f, 0.5f, 0.55f));
            }
            ArtLibrary.Tint(MeshKit.Part(head, "WhiteMuzzle", MeshKit.Ball(0.034f, 8, 6), Mat.Cloth, new Vector3(0f, -0.024f, 0.06f)).GetComponent<Renderer>(), white);
            ArtLibrary.Tint(MeshKit.Part(body, "WhiteChest", MeshKit.Ball(0.05f, 8, 6), Mat.Cloth, new Vector3(0.12f, -0.02f, 0.06f), default, new Vector3(1f, 0.8f, 1.2f)).GetComponent<Renderer>(), white);
            for (int i = 0; i < 9; i++)
            {
                float a = Mathf.Deg2Rad * (200f + i * 22f);
                Fur(MeshKit.Part(body, "Tail" + i, MeshKit.Ball(0.026f - i * 0.0012f, 6, 5), Mat.Cloth, new Vector3(Mathf.Cos(a) * 0.16f, -0.04f + i * 0.002f, Mathf.Sin(a) * 0.15f)));
            }
            // Curled on a pale scrap of sacking (a black cat on black stone would vanish).
            var rag = MeshKit.Part(transform, "Sacking", MeshKit.Cylinder(0.42f, 0.4f, 0.03f, 12), Mat.Cloth, new Vector3(0.02f, 0.015f, 0f), new Vector3(0f, 20f, 0f), new Vector3(1.15f, 1f, 0.85f));
            ArtLibrary.Tint(rag.GetComponent<Renderer>(), new Color(0.72f, 0.64f, 0.5f));
            // A little dust and a cobweb thread on it: it has come a long way down.
            var web = MeshKit.Part(body, "Cobweb", MeshKit.Box(new Vector3(0.12f, 0.003f, 0.003f)), Mat.Bone, new Vector3(0f, 0.09f, 0f), new Vector3(0f, 30f, 8f));
            ArtLibrary.Tint(web.GetComponent<Renderer>(), new Color(0.85f, 0.85f, 0.85f, 0.6f));
        }
    }
}
