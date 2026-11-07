using System.Collections.Generic;
using Deepbound.Combat;
using Deepbound.Enemies;
using Deepbound.Loot;
using Deepbound.Player;
using Deepbound.Progression;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// THE PANOPLY (2026-11-07, the user: "rare item room with special effect armor", guarded, every few floors). On
    /// floors 2, 5 and 8 one side room is an armoury crypt: an armour stand at the back and its last owner, a named
    /// armour-wraith (a heavy elite), standing guard. Kill it and the stand gives every player that floor's
    /// Panoply piece (<see cref="Gear.PanoplyPieces"/>). This file also ticks the pieces' auras.
    /// </summary>
    public partial class DungeonDirector
    {
        public const float PanoplyGuardHealth = 2.6f, PanoplyGuardDamage = 1.25f;
        private const float AuraTick = 0.5f, RimeRadius = 4f, RimeEvery = 1.5f, SunRadius = 6f, SunPerSecond = 0.01f;

        private static readonly string[] WraithNames = { "Ser Corvane", "Dame Ilsabet", "Ser Haldric", "Brother Oswin", "Ser Maudlin", "Dame Edra" };
        private float nextAuraTick, nextRime;

        /// <summary>Is this depth one with a Panoply (floors 2, 5, 8)?</summary>
        public static bool PanoplyFloor(int depth) => depth % 3 == 1 && depth < 9;

        /// <summary>Which piece the Panoply on a floor holds (from the floor's seed, so it is the same for everyone).</summary>
        public static GearId PanoplyPiece(int seed) => Gear.PanoplyPieces[Mathf.Abs(seed) % Gear.PanoplyPieces.Length];

        /// <summary>Server: guard and stand for a Panoply room.</summary>
        private void SetupPanoply(DungeonLayout.Room room, DungeonLayout layout)
        {
            if (room.FeatureSpots.Count < 2) return;
            var piece = PanoplyPiece(Floor.Value.x);
            var stand = room.FeatureSpots[0];
            var guard = ServerDebugSpawn(layout.Depth >= 4 ? "brute" : "skeleton", room.FeatureSpots[1], true);
            if (guard == null) return;
            var hp = guard.GetComponent<Health>();
            hp.ServerSetMax(hp.Max.Value * PanoplyGuardHealth, refill: true);
            hp.Defense += 3f;
            guard.ServerScaleDamage(PanoplyGuardDamage);
            string who = WraithNames[Mathf.Abs(Floor.Value.x / 7) % WraithNames.Length];
            guard.Title.Value = who + ", who wore the " + Gear.BaseName(piece);
            hp.Died += _ => ServerPanoplyWon(stand, piece, who);
        }

        private void ServerPanoplyWon(Vector3 stand, GearId piece, string who)
        {
            int depth = Mathf.Max(0, Floor.Value.y);
            foreach (ulong client in NetworkManager.ConnectedClientsIds)
                GearDrop.ServerSpawn(new GearItem(piece, 3, depth), client, stand + Vector3.up * 0.3f);
            PanoplyWonRpc(who, Gear.BaseName(piece), stand);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void PanoplyWonRpc(string who, string piece, Vector3 at)
        {
            UI.GameHud.ShowTitleCard(who.ToUpperInvariant() + " IS AT REST", "The " + piece + " is yours to take", new Color(0.85f, 0.75f, 1f));
            Art.Vfx.Flash(at + Vector3.up, new Color(0.8f, 0.7f, 1f), 6f, 5f, 0.4f);
            Art.Sfx.Play(Art.Sound.Relic, at + Vector3.up, 1f, 0.8f);
        }

        /// <summary>Server, every frame: the Rimecloak's frost and the Sunward Plate's halo.</summary>
        private void TickArmourAuras()
        {
            if (Time.time < nextAuraTick) return;
            nextAuraTick = Time.time + AuraTick;
            bool rime = Time.time >= nextRime;
            if (rime) nextRime = Time.time + RimeEvery;
            foreach (var life in PlayerLife.All)
            {
                if (life == null || life.Downed.Value || !life.TryGetComponent<PlayerProgress>(out var p)) continue;
                if (rime && p.Wears(GearId.Rimecloak))
                    foreach (var foe in CombatQuery.Circle(life.transform.position + Vector3.up, RimeRadius, Team.Enemies))
                        foe.ServerChill(1.6f);
                if (p.Wears(GearId.SunwardPlate))
                    foreach (var ally in PlayerLife.All)
                    {
                        if (ally == null || ally.Downed.Value) continue;
                        if (Vector3.Distance(ally.transform.position, life.transform.position) > SunRadius) continue;
                        var h = ally.Health;
                        h.ServerHeal(h.Max.Value * SunPerSecond * AuraTick * (ally == life ? 0.5f : 1f));
                    }
            }
        }
    }
}
