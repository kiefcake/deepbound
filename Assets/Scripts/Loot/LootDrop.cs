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
        private const float PickupRadius = 1.3f, MagnetRadius = 2.6f;

        private bool Magnetic => Kind.Value == LootKind.Gold || Kind.Value == LootKind.Embers;

        private PlayerLife NearestCollector(float radius)
        {
            PlayerLife best = null;
            float bestD = radius;
            foreach (var life in PlayerLife.All)
            {
                if (life == null || life.Downed.Value) continue;
                float d = Vector3.Distance(life.transform.position, transform.position);
                if (d < bestD) { bestD = d; best = life; }
            }
            return best;
        }

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
        private float pickupAllowedAt, bornAt;
        private bool landed;
        private const float HopSeconds = 0.55f;

        /// <summary>Server: spawn drops from a table roll, scattered around a point.</summary>
        public static void ServerSpawnFromTable(NetworkObject prefab, LootTable table, Vector3 at, float goldScale)
        {
            var rolls = new List<(LootKind, int)>();
            table.Roll(rolls, goldScale);
            for (int i = 0; i < rolls.Count; i++)
            {
                float a = (i / (float)Mathf.Max(1, rolls.Count)) * Mathf.PI * 2f + Random.value;
                var pos = World.DropSpots.Settle(at, at + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (rolls.Count > 1 ? 0.9f : 0f));
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
            bornAt = Time.time;
            // A short delay so loot visibly lands before it vanishes into whoever was
            // standing on the corpse.
            pickupAllowedAt = Time.time + 0.35f;
        }

        private Transform relicColumn;

        private void Update()
        {
            // Standing in a relic's column of light tints the whole view: it steps aside when you are close.
            if (relicColumn != null)
            {
                var cam = Art.ViewCamera.Current;
                bool near = cam != null && new Vector2(cam.transform.position.x - transform.position.x, cam.transform.position.z - transform.position.z).sqrMagnitude < 2.2f * 2.2f;
                if (relicColumn.gameObject.activeSelf == near) relicColumn.gameObject.SetActive(!near);
            }
            if (visual != null)
            {
                phase += Time.deltaTime;
                visual.localPosition = new Vector3(0f, 0.35f + Mathf.Sin(phase * 2.2f) * 0.07f, 0f);
                visual.localRotation = Quaternion.Euler(0f, phase * 70f, 0f);
                // Spilled from the body: a quick spinning hop, growing to size, and a bump on landing.
                float age = Time.time - bornAt;
                if (age < HopSeconds)
                {
                    float t = age / HopSeconds;
                    visual.localPosition += Vector3.up * (4f * 0.9f * t * (1f - t)) + new Vector3(0f, -0.3f * (1f - t), 0f);
                    visual.localRotation = Quaternion.Euler(0f, phase * 70f + t * 540f, 0f);
                    visual.localScale = Vector3.one * Mathf.Lerp(0.35f, 1f, Mathf.Sqrt(t));
                }
                else if (!landed)
                {
                    landed = true;
                    visual.localScale = Vector3.one;
                    Vfx.Ring(transform.position + Vector3.up * 0.04f, 0.35f, new Color(1f, 0.85f, 0.45f, 0.6f), 0.3f);
                }
                // Coins and embers drift toward whoever is near enough to collect them.
                if (Magnetic && Time.time >= pickupAllowedAt)
                {
                    var near = NearestCollector(MagnetRadius);
                    if (near != null)
                    {
                        float k = 1f - Mathf.Clamp01(Vector3.Distance(near.transform.position, transform.position) / MagnetRadius);
                        visual.position = Vector3.Lerp(visual.position, near.transform.position + Vector3.up * 1f, 0.15f + 0.5f * k);
                    }
                }
            }

            if (!IsServer || Time.time < pickupAllowedAt) return;

            foreach (var life in PlayerLife.All)
            {
                if (life == null || life.Downed.Value) continue;
                var d = life.transform.position - transform.position;
                d.y *= 0.5f;
                if (d.magnitude > (Magnetic ? MagnetRadius : PickupRadius)) continue;

                if (TryApply(life.GetComponent<PlayerProgress>()))
                {
                    if (!crumbled) PickedUpRpc(life.NetworkObjectId);
                    NetworkObject.Despawn();
                    return;
                }
            }
        }

        private bool crumbled;

        private bool TryApply(PlayerProgress progress)
        {
            if (progress == null || progress.Stats == null) return false;
            var kind = Kind.Value;

            if (kind == LootKind.Gold)
            {
                PlayerProgress.ServerShareGold(progress, Mathf.RoundToInt(Amount.Value * progress.GoldMultiplier));
                return true;
            }
            if (kind == LootKind.Relic)
            {
                if (progress.ServerAddRelic((Relic)Amount.Value)) return true;
                // One of each per player: a relic everyone already carries used to lie there for ever with its
                // beam lit (the autoplay run stood on one for ten minutes). It breaks down into embers instead.
                foreach (var pl in PlayerLife.All)
                    if (pl != null && pl.TryGetComponent<PlayerProgress>(out var other) && !other.HasRelic((Relic)Amount.Value)) return false;
                int embers = 15 + 10 * Mathf.Max(0, World.DungeonDirector.Layout != null ? World.DungeonDirector.Layout.Depth : 0);
                PlayerProgress.ServerShareEmbers(progress, embers);
                crumbled = true;
                World.DungeonDirector.Instance?.ServerFeedAll("A second " + Relics.Name((Relic)Amount.Value) + " crumbles into " + embers + " embers (you already carry one)", LootInfo.EmberColor);
                return true;
            }
            if (kind == LootKind.PotionHP || kind == LootKind.PotionMP)
            {
                // Into the pouch while it has room; a full pouch leaves it on the floor for someone else.
                var pouch = progress.GetComponent<Player.Flasks>();
                if (pouch == null) return false;
                var k = kind == LootKind.PotionHP ? Player.Flasks.Kind.Health : Player.Flasks.Kind.Mana;
                int fit = pouch.ServerAdd(k, Mathf.Max(1, Amount.Value));
                if (fit <= 0) return false;
                if (fit < Amount.Value) { Amount.Value -= fit; return false; }
                return true;
            }
            if (kind == LootKind.Sigil)
            {
                progress.ServerAddSigil();
                return true;
            }
            if (kind == LootKind.Rune)
            {
                progress.ServerAddRune((Rune)Amount.Value);
                return true;
            }
            if (kind == LootKind.Embers)
            {
                PlayerProgress.ServerShareEmbers(progress, Mathf.RoundToInt(Amount.Value * (progress.Wears(GearId.LanternOfTheLost) ? 1.4f : 1f) * (progress.Blessed(Blessing.Fortune) ? 1.5f : 1f)));
                return true;
            }
            return false;
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void PickedUpRpc(ulong playerObjectId)
        {
            // Whoever took it sees it at once, under the crosshair.
            if (NetworkPlayer.Local != null && NetworkPlayer.Local.NetworkObjectId == playerObjectId)
            {
                var k = Kind.Value;
                if (k == LootKind.Gold) UI.GameHud.Pickup("gold", Amount.Value, " gold", LootInfo.GoldColor);
                else if (k == LootKind.Embers) UI.GameHud.Pickup("embers", Amount.Value, " embers", LootInfo.EmberColor);
                else if (k == LootKind.Sigil) UI.GameHud.Pickup("sigil", 1, " Sigil", LootInfo.SigilColor);
                else if (k == LootKind.Rune) UI.GameHud.Pickup("rune" + Amount.Value, 1, " " + Runes.Name((Rune)Amount.Value), Runes.Color((Rune)Amount.Value));
                else if (k == LootKind.Relic) UI.GameHud.Pickup("relic" + Amount.Value, 1, " " + Relics.Name((Relic)Amount.Value), Relics.GemColor((Relic)Amount.Value));
                // Potions: the pouch says it, with the count (Flasks.AddedRpc).
            }
            // Relics announce themselves; gold and embers just make their noise.
            var at = transform.position + Vector3.up * 0.4f;
            if (Kind.Value == LootKind.Gold) Sfx.Play(Sound.Coins, at, 0.6f);
            else if (Kind.Value == LootKind.Embers) Sfx.Play(Sound.Fire, at, 0.45f, 1.6f);
            else if (Kind.Value == LootKind.Sigil || Kind.Value == LootKind.Rune) Sfx.Play(Sound.Relic, at, 0.8f, 1.3f);
            else if (Kind.Value == LootKind.PotionHP || Kind.Value == LootKind.PotionMP) Sfx.Play(Sound.Potion, at, 0.7f, 1.1f);
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

            if (Kind.Value == LootKind.PotionHP || Kind.Value == LootKind.PotionMP)
            {
                // A stoppered bottle, red or blue, glowing faintly through the glass.
                var pc = Kind.Value == LootKind.PotionHP ? new Color(0.95f, 0.2f, 0.2f) : new Color(0.3f, 0.5f, 1f);
                var bottle = MeshKit.Part(visual, "Bottle", MeshKit.Lathe("potionBottle", new[]
                {
                    new Vector2(0f, -0.14f), new Vector2(0.1f, -0.12f), new Vector2(0.11f, -0.02f), new Vector2(0.05f, 0.06f), new Vector2(0.04f, 0.12f), new Vector2(0f, 0.13f),
                }, 10), Mat.Glow, Vector3.zero);
                ArtLibrary.SetGlow(bottle.GetComponent<Renderer>(), pc * 1.1f);
                MeshKit.Part(visual, "Cork", MeshKit.Cylinder(0.035f, 0.03f, 0.05f, 6), Mat.Wood, new Vector3(0f, 0.12f, 0f));
                AddLight(pc, 1.6f);
                return;
            }

            if (Kind.Value == LootKind.Rune)
            {
                // A small rounded rune-stone with a glowing band in the rune's colour.
                var rc = Runes.Color((Rune)Amount.Value);
                MeshKit.Part(visual, "Stone", MeshKit.Ball(0.12f, 8, 6), Mat.StoneWall, Vector3.zero, Vector3.zero, new Vector3(1f, 0.7f, 1f));
                var band = MeshKit.Part(visual, "Band", MeshKit.Cylinder(0.125f, 0.125f, 0.03f, 10), Mat.Glow, Vector3.zero);
                ArtLibrary.SetGlow(band.GetComponent<Renderer>(), rc * 1.8f);
                AddLight(rc, 2f);
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
            visual.localScale = Vector3.one * 1.7f;
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

            var beam = MeshKit.Part(transform, "Beam", MeshKit.Cylinder(0.07f, 0.03f, 11f, 8), Mat.Glow, Vector3.zero);
            ArtLibrary.SetGlow(beam.GetComponent<Renderer>(), color * 1.4f);
            beam.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            // A soft column of its light round the beam, a slow ring on the floor, and rising sparks.
            var column = MeshKit.Part(transform, "Column", MeshKit.Cylinder(0.45f, 0.2f, 7f, 12), Mat.Smoke, Vector3.zero);
            relicColumn = column.transform;
            ArtLibrary.Tint(column.GetComponent<Renderer>(), new Color(color.r, color.g, color.b, 0.14f));
            column.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var halo = MeshKit.Pivot(transform, "Halo", Vector3.up * 0.04f);
            for (int i = 0; i < 12; i++)
            {
                float a = i * 30f * Mathf.Deg2Rad;
                var dash = MeshKit.Part(halo, "HaloDash", MeshKit.Box(new Vector3(0.22f, 0.01f, 0.05f)), Mat.Glow, new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.95f, new Vector3(0f, -i * 30f, 0f));
                ArtLibrary.SetGlow(dash.GetComponent<Renderer>(), color * 1.3f);
                dash.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            halo.gameObject.AddComponent<Art.Spin>().DegreesPerSecond = 40f;
            gameObject.AddComponent<Art.RelicSparks>().Color = color;
            AddLight(color, 6f);
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
