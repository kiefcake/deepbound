using System.Collections.Generic;
using Deepbound.Art;
using Deepbound.Combat;
using Deepbound.Loot;
using Deepbound.Player;
using Deepbound.Progression;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// A blood altar (reworked 2026-10-20). Its chalice wants <see cref="Offerings"/> offerings of
    /// blood; anyone in the party can give one (F, a fifth of your max health, taken straight from
    /// the pool). Each offering pays at once (embers), and the blood on the stone calls the dead
    /// within earshot. When the chalice brims, every player who bled gets a relic they lack, and the
    /// whole party gets BLOODLUST (+15% damage for <see cref="BloodlustSeconds"/>). A soloist pays it
    /// all alone; a party splits the price, and those who gave nothing share only the bloodlust.
    /// </summary>
    public class BloodAltar : Interactable
    {
        public const int Offerings = 3;
        public const float OfferFraction = 0.2f, BloodlustSeconds = 90f;

        public readonly NetworkVariable<bool> Spent = new NetworkVariable<bool>();
        public readonly NetworkVariable<byte> Filled = new NetworkVariable<byte>();
        private readonly HashSet<ulong> donors = new HashSet<ulong>();
        private float nextOffer;
        private Renderer pool;
        private readonly List<Renderer> marks = new List<Renderer>();
        private Transform bloodLevel;

        public override string Prompt(PlayerProgress who)
        {
            if (Spent.Value) return null;
            var hp = who.GetComponent<Health>();
            if (hp != null && hp.Current.Value < hp.Max.Value * (OfferFraction + 0.1f)) return "Blood altar: you are too weak to bleed for it";
            return "Offer blood (" + Mathf.RoundToInt(OfferFraction * 100f) + "% health)   chalice " + Filled.Value + " / " + Offerings + "   full: a relic each who bled, Bloodlust for all";
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            BuildVisual();
            Spent.OnValueChanged += (_, __) => UpdateGlow();
            Filled.OnValueChanged += (_, __) => UpdateGlow();
            UpdateGlow();
        }

        protected override void ServerUse(PlayerProgress who)
        {
            if (Spent.Value || Time.time < nextOffer) return;
            var hp = who.GetComponent<Health>();
            if (hp.Current.Value < hp.Max.Value * (OfferFraction + 0.1f)) return; // would nearly kill you: refuse
            nextOffer = Time.time + 0.6f;
            // Taken straight from the pool, not as a hit: blocks and wards do not apply.
            hp.ServerSetFraction((hp.Current.Value - hp.Max.Value * OfferFraction) / hp.Max.Value);
            donors.Add(who.NetworkObjectId);
            int depth = DungeonDirector.Layout != null ? DungeonDirector.Layout.Depth : 0;
            who.ServerAddEmbers(4 + Mathf.RoundToInt(2f * DungeonDirector.Tier(depth)));
            Filled.Value = (byte)Mathf.Min(Offerings, Filled.Value + 1);
            // Fresh blood on the stone: the dead nearby smell it.
            DungeonDirector.Instance?.ServerNoise(hp, transform.position, 12f);
            OfferFxRpc(who.name, Filled.Value);
            if (Filled.Value >= Offerings) ServerBrim();
        }

        private void ServerBrim()
        {
            Spent.Value = true;
            var nm = NetworkManager.Singleton;
            double until = nm.ServerTime.Time + BloodlustSeconds;
            int relics = 0;
            foreach (var p in PlayerLife.All)
            {
                if (p == null || !p.TryGetComponent<PlayerProgress>(out var prog)) continue;
                prog.BloodlustEnds.Value = until;
                if (!donors.Contains(prog.NetworkObjectId)) continue;
                for (int tries = 0; tries < 30; tries++)
                {
                    var r = Relics.Random();
                    if (prog.HasRelic(r)) continue;
                    prog.ServerAddRelic(r);
                    relics++;
                    break;
                }
            }
            BrimFxRpc(relics);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void OfferFxRpc(string who, byte filled)
        {
            Sfx.Play(Sound.Hit, transform.position + Vector3.up, 0.8f, 0.6f);
            Sfx.Play(Sound.Whisper, transform.position + Vector3.up, 0.7f, 0.6f);
            Vfx.Embers(transform.position + Vector3.up * 1f, 12, new Color(0.9f, 0.05f, 0.05f), 2.5f, 0.05f, 0.7f);
            UI.GameHud.Feed(who + " bleeds on the altar (" + filled + " / " + Offerings + ")", new Color(0.95f, 0.3f, 0.3f));
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void BrimFxRpc(int relics)
        {
            Sfx.Play(Sound.Summon, transform.position + Vector3.up, 1f, 0.6f);
            Sfx.Play(Sound.Relic, transform.position + Vector3.up, 1f, 0.8f);
            Vfx.Flash(transform.position + Vector3.up * 1.2f, new Color(1f, 0.1f, 0.08f), 8f, 8f, 0.6f);
            Vfx.Ring(transform.position + Vector3.up * 0.05f, 6f, new Color(0.9f, 0.08f, 0.06f), 0.7f);
            UI.GameHud.ShowTitleCard("THE CHALICE BRIMS", "Bloodlust: +15% damage for " + (int)BloodlustSeconds + " s" + (relics > 0 ? ", and a relic for each who bled" : ""), new Color(1f, 0.3f, 0.25f));
        }

        private void UpdateGlow()
        {
            float f = Filled.Value / (float)Offerings;
            if (pool != null) ArtLibrary.SetGlow(pool, Spent.Value ? new Color(0.25f, 0f, 0f) : new Color(1f, 0.05f, 0.05f) * (0.8f + 1.4f * f));
            for (int i = 0; i < marks.Count; i++)
                ArtLibrary.SetGlow(marks[i], i < Filled.Value ? new Color(1f, 0.1f, 0.08f) * 1.6f : new Color(0.15f, 0.02f, 0.02f));
            if (bloodLevel != null) bloodLevel.localScale = new Vector3(1f, Mathf.Max(0.05f, f), 1f);
        }

        private void BuildVisual()
        {
            MeshKit.Part(transform, "Base", MeshKit.Box(new Vector3(1.4f, 0.4f, 1.0f)), Mat.StoneWall, new Vector3(0f, 0.2f, 0f));
            MeshKit.Part(transform, "Top", MeshKit.Box(new Vector3(1.2f, 0.5f, 0.8f)), Mat.StoneWall, new Vector3(0f, 0.65f, 0f));
            // Runnels down the front where the blood runs, and a broad stone chalice on top.
            for (int i = 0; i < 3; i++)
                MeshKit.Part(transform, "Runnel" + i, MeshKit.Box(new Vector3(0.05f, 0.5f, 0.02f)), Mat.Dark, new Vector3(-0.3f + i * 0.3f, 0.62f, 0.405f));
            MeshKit.Part(transform, "Chalice", MeshKit.Lathe("altarChalice", new[]
            {
                new Vector2(0.18f, 0f), new Vector2(0.08f, 0.08f), new Vector2(0.06f, 0.22f), new Vector2(0.32f, 0.3f), new Vector2(0.36f, 0.42f), new Vector2(0.3f, 0.44f),
            }, 14), Mat.StoneWall, new Vector3(0f, 0.9f, 0f));
            bloodLevel = MeshKit.Pivot(transform, "BloodLevel", new Vector3(0f, 1.2f, 0f));
            pool = MeshKit.Part(bloodLevel, "Blood", MeshKit.Cylinder(0.3f, 0.3f, 0.12f, 14), Mat.Glow, new Vector3(0f, 0.06f, 0f)).GetComponent<Renderer>();
            // Three blood-marks on the altar's face: one lights for each offering.
            for (int i = 0; i < Offerings; i++)
            {
                var m = MeshKit.Part(transform, "Mark" + i, MeshKit.Lathe("altarMark", new[] { new Vector2(0.06f, 0f), new Vector2(0f, 0.12f) }, 3), Mat.Glow, new Vector3(-0.3f + i * 0.3f, 0.95f, 0.41f), new Vector3(180f, 0f, 0f));
                marks.Add(m.GetComponent<Renderer>());
            }
            for (int i = 0; i < 2; i++)
            {
                float x = i == 0 ? -0.7f : 0.7f;
                MeshKit.Part(transform, "Horn" + i, MeshKit.Lathe("altarHorn", new[] { new Vector2(0.08f, 0f), new Vector2(0f, 0.7f) }, 5), Mat.Bone,
                    new Vector3(x, 0.9f, 0f), new Vector3(0f, 0f, i == 0 ? 20f : -20f));
            }
            MeshKit.Part(transform, "Skull", MeshKit.Ball(0.14f, 8, 6), Mat.Bone, new Vector3(0f, 1.05f, -0.42f));
            foreach (float x in new[] { -0.55f, 0.55f })
            {
                MeshKit.Part(transform, "Candle", MeshKit.Cylinder(0.04f, 0.045f, 0.22f, 6), Mat.Bone, new Vector3(x, 1.01f, 0.28f));
                Props.AddFlame(transform, new Vector3(x, 1.16f, 0.28f), 0.5f, new Color(1f, 0.35f, 0.2f), 0.35f, 2f, false);
            }
            var light = new GameObject("Light").AddComponent<Light>();
            light.transform.SetParent(transform, false);
            light.transform.localPosition = Vector3.up * 1.4f;
            light.type = LightType.Point;
            light.color = new Color(1f, 0.15f, 0.1f);
            light.range = 5f;
            light.intensity = 2.5f;
        }
    }
}
