using System.Collections.Generic;
using Deepbound.Art;
using Deepbound.Player;
using Deepbound.Progression;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.Loot
{
    /// <summary>
    /// An item lying on the dungeon floor. Walk over it to take it.
    ///
    /// <para>
    /// Pickup is server-decided — the server checks who is standing on it and applies it
    /// to that player's <see cref="PlayerProgress"/> — so two players touching it in the
    /// same frame cannot both get it. A relic you already own stays on the floor for a
    /// teammate instead of being wasted.
    /// </para>
    /// </summary>
    public class LootDrop : NetworkBehaviour
    {
        private const float PickupRadius = 1.3f;

        /// <summary>Every drop on this peer, so the minimap need not search the scene each frame.</summary>
        public static readonly List<LootDrop> All = new List<LootDrop>();

        public override void OnNetworkDespawn() => All.Remove(this);

        public override void OnDestroy()
        {
            All.Remove(this);
            base.OnDestroy();
        }

        public readonly NetworkVariable<LootKind> Kind = new NetworkVariable<LootKind>();
        public readonly NetworkVariable<int> Amount = new NetworkVariable<int>(1);

        private Transform visual;
        private float phase;
        private float pickupAllowedAt;

        /// <summary>Server: spawn drops from a table roll, scattered around a point.</summary>
        public static void ServerSpawnFromTable(NetworkObject prefab, LootTable table, Vector3 at, float goldScale)
        {
            var rolls = new List<(LootKind, int)>();
            table.Roll(rolls, goldScale);
            for (int i = 0; i < rolls.Count; i++)
            {
                float a = (i / (float)Mathf.Max(1, rolls.Count)) * Mathf.PI * 2f + Random.value;
                var pos = at + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (rolls.Count > 1 ? 0.9f : 0f) + Vector3.up * 0.05f;
                var obj = Object.Instantiate(prefab, pos, Quaternion.identity);
                var drop = obj.GetComponent<LootDrop>();
                drop.Kind.Value = rolls[i].Item1;
                drop.Amount.Value = rolls[i].Item2;
                obj.Spawn(destroyWithScene: true);
            }
        }

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            BuildVisual();
            phase = Random.value * 10f;
            // A short delay so loot visibly lands before it vanishes into whoever was
            // standing on the corpse.
            pickupAllowedAt = Time.time + 0.6f;
        }

        private void Update()
        {
            if (visual != null)
            {
                phase += Time.deltaTime;
                visual.localPosition = new Vector3(0f, 0.35f + Mathf.Sin(phase * 2.2f) * 0.07f, 0f);
                visual.localRotation = Quaternion.Euler(0f, phase * 70f, 0f);
            }

            if (!IsServer || Time.time < pickupAllowedAt) return;

            foreach (var life in PlayerLife.All)
            {
                if (life == null || life.Downed.Value) continue;
                var d = life.transform.position - transform.position;
                d.y *= 0.5f;
                if (d.magnitude > PickupRadius) continue;

                if (TryApply(life.GetComponent<PlayerProgress>()))
                {
                    PickedUpRpc(life.NetworkObjectId);
                    NetworkObject.Despawn();
                    return;
                }
            }
        }

        private bool TryApply(PlayerProgress progress)
        {
            if (progress == null || progress.Stats == null) return false;
            var kind = Kind.Value;

            if (kind == LootKind.Gold)
            {
                progress.ServerAddGold(Mathf.RoundToInt(Amount.Value * progress.GoldMultiplier));
                return true;
            }
            if (kind == LootKind.Relic) return progress.ServerAddRelic((Relic)Amount.Value);
            if (kind == LootKind.Sigil)
            {
                progress.ServerAddSigil();
                return true;
            }
            if (kind == LootKind.Embers)
            {
                progress.ServerAddEmbers(Mathf.RoundToInt(Amount.Value * (progress.Wears(GearId.LanternOfTheLost) ? 1.4f : 1f)));
                return true;
            }
            return false;
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void PickedUpRpc(ulong playerObjectId)
        {
            // Relics announce themselves; gold and embers just make their noise.
            var at = transform.position + Vector3.up * 0.4f;
            if (Kind.Value == LootKind.Gold) Sfx.Play(Sound.Coins, at, 0.6f);
            else if (Kind.Value == LootKind.Embers) Sfx.Play(Sound.Fire, at, 0.45f, 1.6f);
            else if (Kind.Value == LootKind.Sigil) Sfx.Play(Sound.Relic, at, 0.8f, 1.3f);
        }

        // ------------------------------------------------------------------

        private void BuildVisual()
        {
            visual = MeshKit.Pivot(transform, "Visual", Vector3.up * 0.35f);

            if (Kind.Value == LootKind.Relic)
            {
                BuildRelicVisual((Relic)Amount.Value);
                return;
            }

            if (Kind.Value == LootKind.Gold)
            {
                int coins = Mathf.Clamp(Amount.Value / 3, 2, 8);
                for (int i = 0; i < coins; i++)
                {
                    var off = new Vector3(Mathf.Sin(i * 2.4f) * 0.08f, -0.25f + i * 0.025f, Mathf.Cos(i * 2.4f) * 0.08f);
                    MeshKit.Part(visual, "Coin" + i, MeshKit.Cylinder(0.07f, 0.07f, 0.02f, 10), Mat.Gold, off, new Vector3(0f, i * 37f, i % 2 == 0 ? 6f : -4f));
                }
                AddLight(new Color(1f, 0.75f, 0.3f), 1.2f);
                return;
            }

            if (Kind.Value == LootKind.Sigil)
            {
                // A violet rune-stone: a flat tablet with a glowing glyph, turning slowly.
                MeshKit.Part(visual, "Tablet", MeshKit.Box(new Vector3(0.22f, 0.3f, 0.06f)), Mat.StoneWall, Vector3.zero);
                var glyph = MeshKit.Part(visual, "Glyph", MeshKit.Box(new Vector3(0.05f, 0.2f, 0.07f)), Mat.Glow, Vector3.zero);
                ArtLibrary.SetGlow(glyph.GetComponent<Renderer>(), LootInfo.SigilColor * 1.8f);
                var bar = MeshKit.Part(visual, "GlyphBar", MeshKit.Box(new Vector3(0.14f, 0.04f, 0.07f)), Mat.Glow, new Vector3(0f, 0.04f, 0f));
                ArtLibrary.SetGlow(bar.GetComponent<Renderer>(), LootInfo.SigilColor * 1.8f);
                AddLight(LootInfo.SigilColor, 2.2f);
                return;
            }

            if (Kind.Value == LootKind.Embers)
            {
                // A little cluster of glowing shards: more embers, more shards.
                int shards = Mathf.Clamp(2 + Amount.Value / 3, 3, 9);
                for (int i = 0; i < shards; i++)
                {
                    float a = i * 2.4f, r = 0.04f + (i % 3) * 0.04f;
                    var shard = MeshKit.Part(visual, "Ember" + i, MeshKit.Lathe("emberShard", new[]
                    {
                        new Vector2(0f, -0.06f), new Vector2(0.035f, 0f), new Vector2(0f, 0.08f),
                    }, 4), Mat.Glow, new Vector3(Mathf.Cos(a) * r, -0.2f + (i % 4) * 0.035f, Mathf.Sin(a) * r), new Vector3(i * 23f, i * 61f, i * 17f));
                    ArtLibrary.SetGlow(shard.GetComponent<Renderer>(), LootInfo.EmberColor * 1.8f);
                }
                AddLight(LootInfo.EmberColor, 1.6f);
                return;
            }
        }

        /// <summary>
        /// A relic: a gem in a gold cage, with a tall shaft of its colour so it can be seen
        /// from across a room. Picking one up is the run's best moment; it should look it.
        /// </summary>
        private void BuildRelicVisual(Relic relic)
        {
            var color = Relics.GemColor(relic);
            var gem = MeshKit.Part(visual, "Gem", MeshKit.Lathe("relicGem", new[]
            {
                new Vector2(0f, -0.14f), new Vector2(0.1f, 0f), new Vector2(0f, 0.16f),
            }, 6), Mat.Glow, Vector3.zero);
            ArtLibrary.SetGlow(gem.GetComponent<Renderer>(), color * 2.2f);
            for (int i = 0; i < 4; i++)
            {
                MeshKit.Part(visual, "Cage" + i, MeshKit.Box(new Vector3(0.02f, 0.34f, 0.02f)), Mat.Gold,
                    Quaternion.Euler(0f, i * 90f, 0f) * new Vector3(0f, 0f, 0.12f), new Vector3(0f, i * 90f, 0f));
            }
            MeshKit.Part(visual, "Ring", MeshKit.Cylinder(0.14f, 0.14f, 0.025f, 12), Mat.Gold, new Vector3(0f, -0.17f, 0f));

            var beam = MeshKit.Part(transform, "Beam", MeshKit.Cylinder(0.05f, 0.02f, 6f, 8), Mat.Glow, Vector3.zero);
            ArtLibrary.SetGlow(beam.GetComponent<Renderer>(), color * 1.2f);
            beam.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            AddLight(color, 4f);
        }

        private void AddLight(Color color, float intensity)
        {
            var light = new GameObject("Glow").AddComponent<Light>();
            light.transform.SetParent(visual, false);
            light.type = LightType.Point;
            light.color = color;
            light.range = 3f;
            light.intensity = intensity;
            light.shadows = LightShadows.None;
        }
    }
}
