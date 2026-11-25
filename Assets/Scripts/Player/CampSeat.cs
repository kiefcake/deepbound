using Deepbound.World;
using UnityEngine;

namespace Deepbound.Player
{
    public class CampSeat : MonoBehaviour
    {
        public const float Reach = 1.6f;

        public static string Prompt { get; private set; }

        public static bool SeatedLocal { get; private set; }

        private NetworkPlayer player;
        private FirstPersonMotor motor;
        private PlayerLife life;
        private Emotes emotes;
        private (Vector3 at, Vector3 face)[] seats;
        private int seat = -1;
        private float satAt;

        public void Init(NetworkPlayer p)
        {
            player = p;
            motor = GetComponent<FirstPersonMotor>();
            life = GetComponent<PlayerLife>();
            emotes = GetComponent<Emotes>();
            seats = Lobby.LogSeats();
        }

        public int NearestFree(Vector3 from)
        {
            int best = -1;
            float bestD = Reach;
            for (int i = 0; i < seats.Length; i++)
            {
                var flat = seats[i].at - from; flat.y = 0f;
                float d = flat.magnitude;
                if (d >= bestD || Mathf.Abs(seats[i].at.y - from.y) > 1.2f || Taken(i)) continue;
                best = i; bestD = d;
            }
            return best;
        }

        private bool Taken(int i)
        {
            foreach (var other in PlayerLife.All)
            {
                if (other == null || other == life) continue;
                var d = other.transform.position - seats[i].at; d.y = 0f;
                if (d.sqrMagnitude < 0.3f * 0.3f) return true;
            }
            return false;
        }

        private void Update()
        {
            if (player == null || !player.IsOwner) return;
            bool camp = DungeonDirector.InLobby && !life.Downed.Value;
            if (seat >= 0)
            {
                var off = transform.position - seats[seat].at; off.y = 0f;
                if (!motor.Pinned || off.magnitude > 0.3f) { Stand(moved: true); return; }
                bool wantsUp = !camp || UI.Binds.Down(UI.Bind.Jump) || motor.RawMove.sqrMagnitude > 0.2f
                               || (UI.Binds.Down(UI.Bind.Use) && Time.time - satAt > 0.3f && !UI.GameHud.MenuOpen);
                if (wantsUp) { Stand(); return; }
                motor.PinTo(seats[seat].at);
                Prompt = "Sitting by the fire   -   F, jump or move to stand";
                return;
            }
            Prompt = null;
            if (!camp || motor.Pinned || UI.GameHud.MenuOpen) return;
            int free = NearestFree(transform.position);
            if (free < 0) return;
            Prompt = "F  Sit on the log by the fire";
            if (!UI.Binds.Down(UI.Bind.Use) || life.UseHasWorldTarget()) return;
            seat = free;
            satAt = Time.time;
            SeatedLocal = true;
            motor.Teleport(seats[seat].at, Quaternion.LookRotation(seats[seat].face));
            motor.PinTo(seats[seat].at);
            emotes.SitDown();
        }

        private void Stand(bool moved = false)
        {
            var s = seats[seat];
            seat = -1;
            SeatedLocal = false;
            Prompt = null;
            if (motor.Pinned) motor.Unpin(moved ? transform.position : s.at - s.face * 0.55f + Vector3.up * 0.05f);
            emotes.StandUp();
        }

        private void OnDisable()
        {
            if (player != null && player.IsOwner) { Prompt = null; SeatedLocal = false; }
        }
    }
}
