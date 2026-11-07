using System.Collections.Generic;
using Deepbound.Art;
using Deepbound.Combat;
using Deepbound.Progression;
using Deepbound.UI;
using UnityEngine;

namespace Deepbound.Player
{
    /// <summary>
    /// Crafted tools in use (2026-11-07, <see cref="Crafting"/>): the owner throws the readied one with <see cref="Bind.Tool"/>
    /// (Z) at the floor under the crosshair; the server keeps this player's caltrop patches and cuts what walks them; every
    /// peer draws the spikes and the rattle. A runtime component, added by <see cref="NetworkPlayer"/>.
    /// </summary>
    public class ToolBelt : MonoBehaviour
    {
        private NetworkPlayer player;
        private PlayerProgress progress;
        private PlayerLife life;
        private FirstPersonMotor motor;
        private float nextUse;

        private class Patch { public Vector3 At; public float Until, NextTick; public GameObject Visual; }
        private readonly List<Patch> patches = new List<Patch>();

        public void Init(NetworkPlayer who)
        {
            player = who;
            progress = who.GetComponent<PlayerProgress>();
            life = who.GetComponent<PlayerLife>();
            motor = who.GetComponent<FirstPersonMotor>();
        }

        private void Update()
        {
            if (player == null) return;
            if (player.IsOwner && Binds.Down(Bind.Tool) && Time.time >= nextUse && motor != null && motor.ActionsReady && life != null && life.CanAct)
            {
                var tool = progress.ReadiedTool;
                if (tool == null || progress.ToolCount(tool.Value) <= 0) GameHud.Pickup("notool", 0, "No tool readied: craft caltrops or a bone rattle (I)", new Color(0.8f, 0.7f, 0.6f));
                else if (player.TryAimFloor(Crafting.ToolRange, out var spot))
                {
                    nextUse = Time.time + 0.6f;
                    progress.UseToolRpc(spot);
                }
            }
            if (player.IsServer) ServerTick();
        }

        /// <summary>Server: a new caltrop patch here.</summary>
        public void ServerCaltrops(Vector3 at) => patches.Add(new Patch { At = at, Until = Time.time + Crafting.CaltropSeconds, NextTick = Time.time });

        private void ServerTick()
        {
            for (int i = patches.Count - 1; i >= 0; i--)
            {
                var p = patches[i];
                if (Time.time > p.Until) { patches.RemoveAt(i); continue; }
                if (Time.time < p.NextTick) continue;
                p.NextTick = Time.time + Crafting.CaltropTick;
                float dmg = Crafting.CaltropDamage * progress.AttackMultiplier;
                foreach (var h in Health.All)
                {
                    if (h == null || !h.IsAlive || h.Team != Team.Enemies) continue;
                    if (h.GetComponent<Enemies.BossBrain>() != null) continue;
                    var d = h.transform.position - p.At;
                    if (Mathf.Abs(d.y) > 1.2f || new Vector2(d.x, d.z).magnitude > Crafting.CaltropRadius) continue;
                    h.ServerChill(Crafting.CaltropTick + 0.6f);
                    h.Tag("Caltrops").ServerDamage(dmg, p.At, player.NetworkObjectId);
                }
            }
        }

        /// <summary>Every peer: the spikes for their life, or the rattle's clatter and ring.</summary>
        public void ShowTool(CraftKind kind, Vector3 at)
        {
            var floor = Vfx.Floor(at);
            if (kind == CraftKind.Caltrops)
            {
                var root = new GameObject("Caltrops");
                root.transform.position = floor;
                var rng = new System.Random((int)(floor.x * 97f + floor.z * 13f));
                for (int i = 0; i < 22; i++)
                {
                    float a = (float)rng.NextDouble() * Mathf.PI * 2f, r = Mathf.Sqrt((float)rng.NextDouble()) * Crafting.CaltropRadius * 0.9f;
                    var spike = MeshKit.Part(root.transform, "Spike", MeshKit.Cylinder(0.035f, 0f, 0.12f, 4), Mat.Steel, new Vector3(Mathf.Cos(a) * r, 0.02f, Mathf.Sin(a) * r),
                        new Vector3((float)rng.NextDouble() * 40f - 20f, (float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 40f - 20f));
                    spike.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
                Vfx.Ring(floor + Vector3.up * 0.03f, Crafting.CaltropRadius, new Color(0.75f, 0.8f, 0.85f, 0.5f), 0.5f);
                Sfx.Play(Sound.Chain, floor + Vector3.up * 0.3f, 0.7f, 1.4f);
                Destroy(root, Crafting.CaltropSeconds);
            }
            else
            {
                Vfx.Ring(floor + Vector3.up * 0.05f, 1.2f, new Color(0.95f, 0.9f, 0.75f, 0.6f), 0.6f);
                Vfx.Ring(floor + Vector3.up * 0.05f, Crafting.RattleRadius, new Color(0.95f, 0.9f, 0.75f, 0.25f), 1f);
                Sfx.Play(Sound.Rattle, floor + Vector3.up * 0.3f, 1f, 1.2f);
                Sfx.Play(Sound.Rattle, floor + Vector3.up * 0.3f, 0.8f, 0.9f);
                GameHud.ShowCombo(floor + Vector3.up * 0.4f, "RATTLE", new Color(0.95f, 0.9f, 0.75f));
            }
        }
    }
}
