using Deepbound.Art;
using Deepbound.Player;
using Deepbound.Progression;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.World
{
    public class TavernKeep : Interactable
    {
        public const int MeadPrice = 8;

        public const int MilkPrice = 4;
        public static TavernKeep Instance { get; private set; }

        public Vector3 MartaAt => transform.position - transform.forward * 0.9f;
        private float busyUntil;

        public override float Range => 2.6f;

        public override string Prompt(PlayerProgress who)
        {
            var d = who.GetComponent<PlayerDebuffs>();
            int level = d != null ? d.Drunk : 0;
            if (who.Gold.Value < MeadPrice) return "Marta: \"Mead's " + MeadPrice + " gold, love. Come back with coin.\"";
            return level >= PlayerDebuffs.DrunkMax ? "Another mead (" + MeadPrice + " gold)  -  you are already Sloshed: it only keeps you there"
                : "Buy a horn of mead  (" + MeadPrice + " gold)  -  " + PlayerDebuffs.DrunkName(level + 1) + ": +" + Mathf.RoundToInt(PlayerDebuffs.DrunkCourage * 100f * (level + 1)) + "% damage, and a wobble";
        }

        protected override void ServerUse(PlayerProgress who)
        {
            if (Time.time < busyUntil || !who.ServerSpendGold(MeadPrice)) return;
            busyUntil = Time.time + 0.6f;
            var d = who.GetComponent<PlayerDebuffs>();
            if (d == null) return;
            d.ServerDrink();
            DrinkFxRpc(who.NetworkObject, d.Drunk);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void MilkRpc(NetworkObjectReference whoRef)
        {
            if (!whoRef.TryGet(out var obj) || !obj.TryGetComponent<PlayerProgress>(out var who)) return;
            if (Vector3.Distance(who.transform.position, MartaAt) > 6f) return;
            var d = who.GetComponent<PlayerDebuffs>();
            if (d == null || d.Drunk == 0 || Time.time < busyUntil || !who.ServerSpendGold(MilkPrice)) return;
            busyUntil = Time.time + 0.6f;
            d.ServerSober();
            MilkFxRpc(who.NetworkObject);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void MilkFxRpc(NetworkObjectReference whoRef)
        {
            if (!whoRef.TryGet(out var who)) return;
            var at = who.transform.position + Vector3.up * 1.6f;
            Sfx.Play(Sound.Drink, at, 0.8f, 1.15f);
            Sfx.Play(Sound.Coins, transform.position + Vector3.up, 0.4f, 1.2f);
            UI.GameHud.ShowCombo(at + Vector3.up * 0.5f, "SOBER", new Color(0.95f, 0.95f, 0.9f));
            UI.GameHud.Feed(who.name + " drinks a glass of milk and sobers up", new Color(0.95f, 0.95f, 0.9f));
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void DrinkFxRpc(NetworkObjectReference whoRef, int level)
        {
            if (!whoRef.TryGet(out var who)) return;
            var at = who.transform.position + Vector3.up * 1.6f;
            Sfx.Play(Sound.Drink, at, 0.9f, 0.85f);
            Sfx.Play(Sound.Coins, transform.position + Vector3.up, 0.5f, 1.1f);
            string state = PlayerDebuffs.DrunkName(level);
            UI.GameHud.ShowCombo(at + Vector3.up * 0.5f, state.ToUpperInvariant(), new Color(0.95f, 0.72f, 0.3f));
            UI.GameHud.Feed(who.name + " downs a horn of mead (" + state + ")", new Color(0.95f, 0.72f, 0.3f));
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            Instance = this;

            Props.Townsfolk(transform, transform.position - transform.forward * 0.9f, transform.position + transform.forward * 3f, Props.Folk.Barkeep);
            MeshKit.Part(transform, "Horn", MeshKit.Lathe("meadHorn", new[] { new Vector2(0.045f, 0f), new Vector2(0.06f, 0.12f), new Vector2(0.07f, 0.2f) }, 10, false), Mat.Bone,
                new Vector3(0.25f, 1.12f, -0.2f), new Vector3(0f, 0f, 10f));
            var mug = MeshKit.Part(transform, "Tankard", MeshKit.Cylinder(0.06f, 0.06f, 0.14f, 10), Mat.Wood, new Vector3(-0.3f, 1.12f, -0.15f));
            ArtLibrary.Tint(mug.GetComponent<Renderer>(), new Color(0.45f, 0.3f, 0.18f));
        }
    }
}
