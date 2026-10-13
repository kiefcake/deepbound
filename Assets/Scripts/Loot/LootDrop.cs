using System.Collections.Generic;
using Deepbound.Art;
using Deepbound.Core;
using Deepbound.Player;
using Deepbound.Progression;
using Deepbound.UI;
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
    /// same frame cannot both get it. A stat potion you have maxed stays on the floor for
    /// a teammate instead of being wasted, which is also the game's quiet nudge toward
    /// sharing loot.
    /// </para>
    /// </summary>
    public class LootDrop : NetworkBehaviour
    {
        private const float PickupRadius = 1.3f;

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
            if (kind == LootKind.PotionAll) return progress.ServerDrinkAllPotion() > 0;
            if (LootInfo.IsStatPotion(kind)) return progress.ServerDrinkPotion((StatType)kind);
            return false;
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void PickedUpRpc(ulong playerObjectId)
        {
            bool gold = Kind.Value == LootKind.Gold;
            Sfx.Play(gold ? Sound.Coins : Sound.Potion, transform.position + Vector3.up * 0.4f, gold ? 0.6f : 0.8f);

            if (gold || Kind.Value == LootKind.Relic) return; // relics announce themselves
            string who = NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(playerObjectId, out var obj) ? obj.name : "Someone";
            GameHud.Feed(who + " drank a " + LootInfo.Name(Kind.Value), PotionColor());
        }

        private Color PotionColor() => Kind.Value == LootKind.PotionAll ? new Color(1f, 0.85f, 0.4f) :
            LootInfo.IsStatPotion(Kind.Value) ? LootInfo.PotionColor((StatType)Kind.Value) : new Color(1f, 0.8f, 0.3f);

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

            var color = PotionColor();
            bool big = Kind.Value == LootKind.PotionAll;
            float s = big ? 1.4f : 1f;

            // Round-bottomed flask: glowing liquid body, steel collar, leather cork.
            var body = MeshKit.Part(visual, "Flask", MeshKit.Lathe("flask", new[]
            {
                new Vector2(0f, 0f), new Vector2(0.08f, 0.02f), new Vector2(0.11f, 0.08f), new Vector2(0.1f, 0.15f),
                new Vector2(0.04f, 0.2f), new Vector2(0.035f, 0.28f), new Vector2(0f, 0.28f),
            }, 10), Mat.Glow, new Vector3(0f, -0.15f, 0f), Vector3.zero, Vector3.one * s);
            ArtLibrary.SetGlow(body.GetComponent<Renderer>(), color * 1.8f);
            MeshKit.Part(visual, "Collar", MeshKit.Cylinder(0.045f, 0.045f, 0.03f, 8), Mat.Steel, new Vector3(0f, -0.15f + 0.24f * s, 0f), Vector3.zero, Vector3.one * s);
            MeshKit.Part(visual, "Cork", MeshKit.Cylinder(0.03f, 0.035f, 0.06f, 6), Mat.Leather, new Vector3(0f, -0.15f + 0.28f * s, 0f), Vector3.zero, Vector3.one * s);

            AddLight(color, big ? 3f : 2f);
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
