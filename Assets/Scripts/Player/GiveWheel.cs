using Deepbound.Art;
using Deepbound.Progression;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Deepbound.Player
{
    /// <summary>
    /// GIVE WHEEL (2026-11-07, the user: "interact with other players, for giving potions and a small set amount of gold
    /// or ember that can be repeatedly given"; they chose a hold-key wheel). Look at an ally within <see cref="Reach"/> and
    /// HOLD the give key (G by default; the old ping slot): a wheel of potions from your pouch, 25 gold, 10 embers, your
    /// rare draught, and a high five / salute / bow aimed at them. Flick toward a slice and let go to give; held on gold
    /// or embers it keeps giving every <see cref="RepeatSeconds"/>. The server checks reach, stock and room every time.
    /// </summary>
    public class GiveWheel : NetworkBehaviour
    {
        public const float Reach = 4.5f, RepeatSeconds = 1f;
        public const int GoldStep = 25, EmberStep = 10;

        public enum Gift : byte { None, Health, Draught, Frost, Holy, Gold, Embers, Rare, HighFive, Salute, Bow }

        /// <summary>Clockwise from the top.</summary>
        public static readonly Gift[] Wheel = { Gift.Health, Gift.Draught, Gift.Frost, Gift.Holy, Gift.Gold, Gift.Embers, Gift.Rare, Gift.HighFive, Gift.Salute, Gift.Bow };

        public static bool WheelOpenLocal { get; private set; }

        private NetworkPlayer target;
        private bool open;
        private Vector2 aim;
        private float onSliceSince = -1f, nextRepeat;
        private Gift lastSlice;
        private float serverNextMoney;

        private Flasks pouch;
        private PlayerProgress progress;
        private PlayerLife life;

        private void Awake()
        {
            pouch = GetComponent<Flasks>();
            progress = GetComponent<PlayerProgress>();
            life = GetComponent<PlayerLife>();
        }

        private static Flasks.Kind? PotionOf(Gift g) => g switch
        {
            Gift.Health => Flasks.Kind.Health, Gift.Draught => Flasks.Kind.Mana, Gift.Frost => Flasks.Kind.Frost, Gift.Holy => Flasks.Kind.Holy, _ => null,
        };

        /// <summary>The ally under the crosshair, standing and in reach (owner side).</summary>
        private NetworkPlayer AllyAimed()
        {
            var me = GetComponent<NetworkPlayer>();
            if (me == null || !me.AimRay(Reach, out var hit)) return null;
            var other = hit.collider.GetComponentInParent<NetworkPlayer>();
            if (other == null || other == me) return null;
            var otherLife = other.GetComponent<PlayerLife>();
            return otherLife != null && !otherLife.Downed.Value ? other : null;
        }

        private void Update()
        {
            if (!IsOwner) return;
            bool down = UI.Binds.Down(UI.Bind.Ping), held = UI.Binds.Held(UI.Bind.Ping);
            if (life == null || life.Downed.Value || UI.GameHud.MenuOpen || Emotes.WheelOpenLocal) { Close(); return; }

            if (down && !open)
            {
                target = AllyAimed();
                if (target == null) { UI.GameHud.Feed("Look at an ally up close and hold " + UI.Binds.Show("G") + " to give", new Color(0.8f, 0.78f, 0.7f)); return; }
                open = true; WheelOpenLocal = true; aim = Vector2.zero; onSliceSince = -1f; lastSlice = Gift.None;
                FirstPersonMotor.LookHeld = true;
            }
            if (!open) return;
            if (target == null || !target.IsSpawned || Vector3.Distance(target.transform.position, transform.position) > Reach + 1.5f) { Close(); return; }

            var mouse = Mouse.current;
            if (mouse != null) aim = Vector2.ClampMagnitude(aim + mouse.delta.ReadValue(), 120f);
            var pad = Gamepad.current;
            if (pad != null && pad.rightStick.ReadValue().sqrMagnitude > 0.2f) aim = pad.rightStick.ReadValue() * 120f;

            var choice = Choice();
            if (choice != lastSlice) { lastSlice = choice; onSliceSince = Time.time; nextRepeat = Time.time + 0.6f; }
            // Money repeats while you hold on its slice.
            if (held && (choice == Gift.Gold || choice == Gift.Embers) && Time.time >= nextRepeat)
            {
                nextRepeat = Time.time + RepeatSeconds;
                GiveRpc(target.NetworkObjectId, (byte)choice);
                onSliceSince = -2f; // already given: letting go does not give again
            }
            if (!held)
            {
                if (choice != Gift.None && onSliceSince != -2f) GiveRpc(target.NetworkObjectId, (byte)choice);
                Close();
            }
        }

        private void Close()
        {
            if (!open) return;
            open = false; WheelOpenLocal = false; target = null;
            FirstPersonMotor.LookHeld = false;
        }

        private Gift Choice()
        {
            if (aim.magnitude < 30f) return Gift.None;
            float ang = Mathf.Atan2(aim.x, aim.y) * Mathf.Rad2Deg;
            float step = 360f / Wheel.Length;
            return Wheel[Mathf.RoundToInt(Mathf.Repeat(ang, 360f) / step) % Wheel.Length];
        }

        /// <summary>Owner -> server: give <paramref name="what"/> to the player <paramref name="to"/>.</summary>
        [Rpc(SendTo.Server)]
        private void GiveRpc(ulong to, byte what) => ServerGive(to, (Gift)what);

        /// <summary>Server: one gift, checked. Public for the smoke test.</summary>
        public bool ServerGive(ulong to, Gift g)
        {
            if (!IsServer || life == null || life.Downed.Value) return false;
            if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(to, out var obj) || obj == NetworkObject) return false;
            var other = obj.GetComponent<NetworkPlayer>();
            var otherLife = obj.GetComponent<PlayerLife>();
            if (other == null || otherLife == null || otherLife.Downed.Value) return false;
            if (Vector3.Distance(obj.transform.position, transform.position) > Reach + 2f) return false;
            var theirPouch = obj.GetComponent<Flasks>();
            var theirs = obj.GetComponent<PlayerProgress>();
            string what = null;

            var potion = PotionOf(g);
            if (potion.HasValue)
            {
                var k = potion.Value;
                if (pouch == null || theirPouch == null || pouch.Count(k) <= 0 || theirPouch.IsFull(k)) { RefuseRpc(theirPouch != null && theirPouch.IsFull(k) ? "Their pouch is full" : "You have none"); return false; }
                if (!pouch.ServerTakeOne(k)) return false;
                theirPouch.ServerAdd(k, 1);
                what = k == Flasks.Kind.Mana ? "a draught" : "a " + Flasks.FullNameFor(k, theirs.ClassId.Value);
            }
            else if (g == Gift.Gold || g == Gift.Embers)
            {
                if (Time.time < serverNextMoney) return false;
                serverNextMoney = Time.time + RepeatSeconds * 0.8f;
                if (g == Gift.Gold)
                {
                    int n = Mathf.Min(GoldStep, progress.Gold.Value);
                    if (n <= 0) { RefuseRpc("No gold to give"); return false; }
                    progress.Gold.Value -= n; theirs.Gold.Value += n;
                    what = n + " gold";
                }
                else
                {
                    int n = Mathf.Min(EmberStep, progress.Embers.Value);
                    if (n <= 0) { RefuseRpc("No embers to give"); return false; }
                    progress.Embers.Value -= n; theirs.Embers.Value += n;
                    what = n + " embers";
                }
            }
            else if (g == Gift.Rare)
            {
                if (pouch == null || theirPouch == null || pouch.Rare.Value == 0) { RefuseRpc("No rare draught or tome"); return false; }
                if (theirPouch.Rare.Value != 0) { RefuseRpc("Their fifth slot is taken"); return false; }
                var r = (RareItem)pouch.Rare.Value;
                pouch.Rare.Value = 0;
                theirPouch.ServerGiveRare(r);
                what = RareItems.Name(r);
            }
            else if (g == Gift.HighFive)
            {
                var mine = GetComponent<Emotes>();
                var them = obj.GetComponent<Emotes>();
                if (mine != null && them != null) mine.ServerHighFiveWith(them);
                return true;
            }
            else if (g == Gift.Salute || g == Gift.Bow)
            {
                var mine = GetComponent<Emotes>();
                if (mine != null) mine.ServerPlay(g == Gift.Salute ? Emote.Salute : Emote.Bow);
                GestureRpc(name, other.name, g == Gift.Salute ? "salutes" : "bows to");
                return true;
            }
            if (what == null) return false;
            GaveRpc(name, other.name, what, obj.transform.position + Vector3.up * 1.9f);
            return true;
        }

        [Rpc(SendTo.Owner)]
        private void RefuseRpc(string why)
        {
            UI.GameHud.Feed(why, new Color(0.85f, 0.6f, 0.5f));
            Sfx.Play2D(Sound.UiError, 0.5f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void GaveRpc(string from, string to, string what, Vector3 at)
        {
            UI.GameHud.Feed(from + " gave " + to + " " + what, new Color(1f, 0.85f, 0.45f));
            Sfx.Play(what.EndsWith("gold") ? Sound.Coins : Sound.Potion, at, 0.6f, 1.2f);
            Vfx.Embers(at, 6, new Color(1f, 0.85f, 0.45f), 1.5f, 0.03f, 0.5f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void GestureRpc(string from, string to, string verb) => UI.GameHud.Feed(from + " " + verb + " " + to, new Color(0.9f, 0.88f, 0.78f));

        private string Label(Gift g)
        {
            var k = PotionOf(g);
            if (k.HasValue) return (k.Value == Flasks.Kind.Mana ? Flasks.DraughtShort(progress.ClassId.Value) : Flasks.NameOf(k.Value)) + "  " + (pouch != null ? pouch.Count(k.Value) : 0);
            return g switch
            {
                Gift.Gold => GoldStep + " GOLD", Gift.Embers => EmberStep + " EMBERS",
                Gift.Rare => pouch != null && pouch.Rare.Value != 0 ? RareItems.Name((RareItem)pouch.Rare.Value) : "RARE (none)",
                Gift.HighFive => "HIGH FIVE", Gift.Salute => "SALUTE", Gift.Bow => "BOW", _ => "",
            };
        }

        private bool Possible(Gift g)
        {
            var k = PotionOf(g);
            if (k.HasValue) return pouch != null && pouch.Count(k.Value) > 0;
            if (g == Gift.Gold) return progress.Gold.Value > 0;
            if (g == Gift.Embers) return progress.Embers.Value > 0;
            if (g == Gift.Rare) return pouch != null && pouch.Rare.Value != 0;
            return true;
        }

        private void OnGUI()
        {
            useGUILayout = false;
            if (!IsOwner || !open || target == null) return;
            float w = UI.UiScale.W, h = UI.UiScale.H;
            var c = new Vector2(w / 2f, h / 2f);
            var choice = Choice();
            UI.UiSkin.Apply();
            GUI.depth = -15;
            UI.UiSkin.Rect(new Rect(0, 0, w, h), new Color(0f, 0f, 0f, 0.3f));
            var gold = UI.UiSkin.Gold;
            for (int i = 0; i < Wheel.Length; i++)
            {
                var g = Wheel[i];
                float a = i * (360f / Wheel.Length) * Mathf.Deg2Rad;
                var pos = c + new Vector2(Mathf.Sin(a) * 250f, -Mathf.Cos(a) * 180f);
                bool on = g == choice, can = Possible(g);
                UI.UiSkin.Rect(new Rect(pos.x - 80, pos.y - 18, 160, 36), new Color(0f, 0f, 0f, on ? 0.9f : 0.72f));
                if (on) UI.UiSkin.Rect(new Rect(pos.x - 80, pos.y + 16, 160, 3), gold);
                var col = !can ? new Color(0.55f, 0.52f, 0.48f, 0.7f) : on ? gold : new Color(0.88f, 0.85f, 0.78f, 0.9f);
                GUI.Label(new Rect(pos.x - 80, pos.y - 18, 160, 36), Label(g),
                    new GUIStyle(UI.UiSkin.Body) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, wordWrap = false, clipping = TextClipping.Clip, normal = { textColor = col } });
            }
            GUI.Label(new Rect(c.x - 160, c.y - 24, 320, 24), "GIVE TO " + target.name.ToUpperInvariant(), new GUIStyle(UI.UiSkin.Small) { alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(1f, 1f, 1f, 0.75f) } });
            GUI.Label(new Rect(c.x - 160, c.y + 2, 320, 22), "hold on gold or embers to keep giving", new GUIStyle(UI.UiSkin.Small) { alignment = TextAnchor.MiddleCenter, fontSize = 12, normal = { textColor = new Color(1f, 1f, 1f, 0.45f) } });
            var dot = c + new Vector2(aim.x, -aim.y) * 0.8f;
            UI.UiSkin.Rect(new Rect(dot.x - 4, dot.y - 4, 8, 8), Color.white);
        }
    }
}
