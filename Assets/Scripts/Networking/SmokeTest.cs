using System.Collections;
using System.Linq;
using Deepbound.Combat;
using Deepbound.Enemies;
using Deepbound.Loot;
using Deepbound.Progression;
using Deepbound.Player;
using Deepbound.World;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.Networking
{
    /// <summary>
    /// Headless end-to-end check, run with <c>-batchmode -nographics -host -smoketest 20</c>
    /// (and optionally a second instance with <c>-client</c>).
    ///
    /// <para>
    /// There is no Play-mode test suite; this is the next best thing. It boots a real
    /// player build, lets the dungeon generate, and logs one <c>[Smoke]</c> line per check
    /// to the player log, then quits. On the host it also kills the boss server-side to
    /// prove the floor-cleared transition fires. A missing line or a FAIL means a broken
    /// build.
    /// </para>
    /// </summary>
    public class SmokeTest : MonoBehaviour
    {
        // Tests never touch the player's real wardrobe or Renown.
        private void Awake()
        {
            Progression.Cosmetics.TestMode = true;
            DungeonDirector.TestMode = true;
            UI.Journal.TestMode = true;
            Progression.Deeds.TestMode = true;
        }

        public void Run(float seconds)
        {
            StartCoroutine(Sequence(seconds));
            // Watchdog: if the sequence throws, the coroutine dies silently and the
            // process would idle forever. Fail loudly instead.
            // x8 (was x6 until 2026-10-29): the checks had grown to fill the old 240 s on a headless host.
            Invoke(nameof(Timeout), seconds * 8f);
        }

        private void Timeout()
        {
            Debug.Log("[Smoke] FAIL watchdog: sequence did not finish (see exceptions above)");
            Application.Quit(1);
        }

        /// <summary>
        /// Server: the test player has no passive regeneration any more and the dead hit harder, so a
        /// long run of checks would wear them down and wipe the party mid-test. Top them up when low
        /// (never reviving: checks that down a player on purpose still do).
        /// </summary>
        private IEnumerator Guardian()
        {
            while (true)
            {
                // Every frame: a ghoul pack can take a Knight from half to the floor between half-second checks.
                yield return null;
                var net = NetworkManager.Singleton;
                if (net == null || !net.IsServer) continue;
                var me = NetworkPlayer.Local;
                if (me == null) continue;
                var life = me.GetComponent<PlayerLife>();
                if (life != null && !life.Downed.Value && life.Health.IsAlive && life.Health.Fraction < 0.4f) life.Health.ServerSetFraction(0.8f);
            }
        }

        /// <summary>
        /// Class DPS (balance pass, players' side), run windowed: <c>-classdps</c>. Every class holds its
        /// main attack on a training dummy for six seconds through a virtual pad's right trigger (which
        /// also proves each class attacks on a pad). Logged as [ClassDps] rows, then quits.
        /// </summary>
        public void ClassDps(NetworkLauncher launcher) => StartCoroutine(ClassDpsSequence(launcher));

        private IEnumerator ClassDpsSequence(NetworkLauncher launcher)
        {
            launcher.StartHost();
            yield return new WaitForSeconds(3f);
            var me = NetworkPlayer.Local;
            if (me != null) yield return ClassDpsTable(me, me.GetComponent<PlayerProgress>());
            yield return new WaitForSeconds(0.5f);
            Application.Quit();
        }

        private IEnumerator ClassDpsTable(NetworkPlayer me, PlayerProgress progressMe)
        {
            var dummy = FindObjectsByType<TrainingDummy>(FindObjectsSortMode.None).Select(d => d.GetComponent<Health>()).OrderBy(d => d.transform.position.z).FirstOrDefault();
            if (dummy == null) { Log("class dps", "no training dummy", false); yield break; }
            var motor = me.GetComponent<FirstPersonMotor>();
            var hp = me.GetComponent<Health>();
            // Not a virtual pad: input events are dropped when the window loses focus, and classes after that read 0 dps.
            FirstPersonMotor.BotControl = true;
            float dealt = 0f;
            System.Action<Health, float, ulong> count = (h, amount, from) => { if (h == dummy && from == me.NetworkObjectId) dealt += amount; };
            Health.ServerAnyDamaged += count;
            var rows = new System.Collections.Generic.List<string>();
            bool allHit = true;
            foreach (Classes.ClassId id in System.Enum.GetValues(typeof(Classes.ClassId)))
            {
                progressMe.ClassId.Value = id;
                yield return new WaitForSeconds(0.4f);
                bool melee = id == Classes.ClassId.Knight || id == Classes.ClassId.Rogue || id == Classes.ClassId.Flagellant;
                float range = melee ? 1.7f : 7f;
                var at = dummy.transform.position + Vector3.back * (range + dummy.BodyRadius);
                at.y = me.transform.position.y;
                motor.Teleport(at, Quaternion.LookRotation(Vector3.forward));
                var eye = at + Vector3.up * 1.6f;
                motor.SetPitch(Mathf.Atan2(eye.y - dummy.Center.y, Vector3.Distance(new Vector3(eye.x, 0f, eye.z), new Vector3(dummy.Center.x, 0f, dummy.Center.z))) * Mathf.Rad2Deg);
                foreach (var kit in me.GetComponents<IClassKit>()) kit.RestoreResource(1f);
                yield return new WaitForSeconds(0.4f);
                dealt = 0f;
                // Hold and release the trigger: a long draw for the Ranger, quick presses for swings.
                float hold = id == Classes.ClassId.Ranger ? 1.0f : 0.4f;
                float t0 = Time.time;
                while (Time.time - t0 < 6f)
                {
                    UI.Binds.BotSetPrimary(true);
                    float until = Time.time + hold;
                    while (Time.time < until) { hp.ServerSetFraction(1f); yield return null; }
                    UI.Binds.BotSetPrimary(false);
                    float rel = Time.time + 0.08f;
                    while (Time.time < rel) yield return null;
                }
                yield return new WaitForSeconds(0.6f);
                float dps = dealt / 6f;
                rows.Add(id + " " + dps.ToString("F1"));
                Debug.Log("[ClassDps] " + id + " | " + (melee ? "melee" : "ranged") + " | " + dealt.ToString("F0") + " in 6 s | " + dps.ToString("F1") + " dps");
                if (dealt <= 0f) allHit = false;
            }
            Health.ServerAnyDamaged -= count;
            FirstPersonMotor.BotControl = false;
            motor.SetPitch(0f);
            Log("class dps", string.Join(", ", rows), allHit);
        }

        private IEnumerator Sequence(float seconds)
        {
            StartCoroutine(Guardian());
            // The client is downed by the host's bleed-out check: its owner gets the death recap.
            bool recapSeen = false;
            PlayerLife.DownedRecap += (killer, lines) => { if (recapSeen) return; recapSeen = true; Log("death recap", "downed by " + killer + " | " + lines.Replace("\n", "; ").Replace("\t", " "), !string.IsNullOrEmpty(killer)); };
            yield return new WaitForSeconds(4f);

            var net = NetworkManager.Singleton;
            if (net.IsServer)
            {
                // Sessions start at camp. Check it, then board the carriage.
                int dummies = FindObjectsByType<TrainingDummy>(FindObjectsSortMode.None).Length;
                Log("lobby", "in lobby " + DungeonDirector.InLobby + ", " + dummies + " dummies", DungeonDirector.InLobby && dummies == Lobby.DummySpots.Length);

                // Switch to Mage at camp, then chill a dummy and shatter it.
                var me = NetworkPlayer.Local;
                var progressMe = me.GetComponent<PlayerProgress>();
                progressMe.ClassId.Value = Classes.ClassId.Mage;
                yield return new WaitForSeconds(0.5f);
                Log("class switch", progressMe.Class.Name + ", max hp " + me.GetComponent<Health>().Max.Value + ", rig " + me.Rig.name,
                    progressMe.Class.Name == "Mage" && me.Rig.name == "MageVisual" && me.ActiveKit is MageKit);

                var dummy = FindObjectsByType<TrainingDummy>(FindObjectsSortMode.None)[0].GetComponent<Health>();
                var mage = me.GetComponent<MageKit>();
                // Fire from where the player really stands: the server clamps bolt origins
                // to the caster, so a bolt "from" anywhere else would be moved.
                me.GetComponent<FirstPersonMotor>().Teleport(dummy.transform.position + Vector3.right * 5f, Quaternion.LookRotation(Vector3.left));
                yield return new WaitForSeconds(0.2f);
                var from = me.transform.position + Vector3.up * 1.6f;
                mage.ServerBolt(from, dummy.Center - from, true);
                yield return new WaitForSeconds(0.6f);
                Log("frostbolt chills", "chilled " + dummy.Chilled.Value, dummy.Chilled.Value);
                float before = dummy.Current.Value;
                mage.ServerBolt(from, dummy.Center - from, false);
                yield return new WaitForSeconds(0.5f);
                // Relics: Thunder Idol procs on the 6th hit; Vampiric Fang heals the attacker.
                progressMe.ServerAddRelic(Relic.ThunderSigil);
                progressMe.ServerAddRelic(Relic.VampiricFang);
                var meHp = me.GetComponent<Health>();
                meHp.ServerSetFraction(0.5f);
                float hpBefore = meHp.Current.Value;
                dummy.ServerSetFraction(1f);
                float dummyBefore = dummy.Current.Value;
                for (int i = 0; i < 6; i++) dummy.ServerDamage(10f, me.transform.position, me.NetworkObjectId);
                float dealt = dummyBefore - dummy.Current.Value;
                Log("relics", "6 hits of 10 dealt " + dealt.ToString("F0") + " (lightning adds more), hp " + hpBefore.ToString("F0") + " -> " + meHp.Current.Value.ToString("F0"),
                    dealt > 60f && meHp.Current.Value > hpBefore && progressMe.RelicCount == 2);
                meHp.ServerSetFraction(1f);

                Log("firebolt shatters", "chill consumed " + !dummy.Chilled.Value + ", dealt " + (before - dummy.Current.Value).ToString("F0"),
                    !dummy.Chilled.Value && before - dummy.Current.Value > MageKit.FireboltDamage);

                // Cryomancer path: one frostbolt pierces both dummies, which stand in a line.
                var dummiesAll = FindObjectsByType<TrainingDummy>(FindObjectsSortMode.None).Select(d => d.GetComponent<Health>()).OrderBy(d => d.transform.position.z).ToArray();
                if (dummiesAll.Length >= 2)
                {
                    foreach (var d in dummiesAll) d.ServerConsumeChill();
                    progressMe.WeaponPath.Value = 2;
                    var near = dummiesAll[0].transform.position;
                    me.GetComponent<FirstPersonMotor>().Teleport(near + Vector3.back * 5f, Quaternion.LookRotation(Vector3.forward));
                    yield return new WaitForSeconds(0.2f);
                    var cfrom = me.transform.position + Vector3.up * 1.6f;
                    mage.ServerBolt(cfrom, dummiesAll[1].Center - cfrom, true);
                    yield return new WaitForSeconds(0.8f);
                    Log("cryomancer pierce", "chilled " + dummiesAll[0].Chilled.Value + " and " + dummiesAll[1].Chilled.Value, dummiesAll[0].Chilled.Value && dummiesAll[1].Chilled.Value);
                    progressMe.WeaponPath.Value = 0;
                }

                // Tidal Wave (Q, replacing Blink): rolls out across the wedge in front and strikes what it reaches once.
                me.GetComponent<FirstPersonMotor>().Teleport(dummy.transform.position + Vector3.right * 4f, Quaternion.LookRotation(Vector3.left));
                yield return new WaitForSeconds(0.2f);
                dummy.ServerSetFraction(1f);
                float waveBefore = dummy.Current.Value;
                mage.ServerWave(Vector3.left);
                yield return new WaitForSeconds(MageKit.WaveSeconds + 0.3f);
                float waveDealt = waveBefore - dummy.Current.Value;
                Log("tidal wave", "dealt " + waveDealt.ToString("F0") + " (one hit, dry: " + !mage.WaveWet + ")", waveDealt > 0f && waveDealt < MageKit.WaveDamage * 3f && !mage.WaveWet);

                // Cleric: Aegis absorbs a hit completely; Consecrate burns a dummy standing in it.
                progressMe.ClassId.Value = Classes.ClassId.Cleric;
                yield return new WaitForSeconds(0.5f);
                var cleric = me.GetComponent<ClericKit>();
                var hpCleric = me.GetComponent<Health>();
                hpCleric.ServerSetFraction(1f);
                cleric.ServerAegis();
                float shield = hpCleric.Shield.Value, beforeHit = hpCleric.Current.Value;
                hpCleric.ServerDamage(30f, me.transform.position + me.transform.forward);
                Log("cleric aegis", "shield " + shield.ToString("F0") + ", hp " + beforeHit.ToString("F0") + " -> " + hpCleric.Current.Value.ToString("F0") + ", shield left " + hpCleric.Shield.Value.ToString("F0"),
                    shield > 0f && Mathf.Approximately(hpCleric.Current.Value, beforeHit) && hpCleric.Shield.Value < shield);

                dummy.ServerSetFraction(1f);
                me.GetComponent<FirstPersonMotor>().Teleport(dummy.transform.position + Vector3.right * 1.5f, me.transform.rotation);
                yield return new WaitForSeconds(0.2f);
                float dummyFull = dummy.Current.Value;
                cleric.ServerConsecrate();
                yield return new WaitForSeconds(1.6f);
                Log("cleric consecrate", "dummy " + dummyFull.ToString("F0") + " -> " + dummy.Current.Value.ToString("F0"), dummy.Current.Value < dummyFull);
                // Rogue: from the smoke, a stab in the back is an Ambush opener (x1.5, x2).
                progressMe.ClassId.Value = Classes.ClassId.Rogue;
                yield return new WaitForSeconds(0.5f);
                var rogue = me.GetComponent<RogueKit>();
                dummy.ServerSetFraction(1f);
                var behind = dummy.transform.position - dummy.transform.forward * 1.4f;
                me.GetComponent<FirstPersonMotor>().Teleport(behind, Quaternion.LookRotation(dummy.transform.forward));
                yield return new WaitForSeconds(0.2f);
                rogue.ServerSmoke();
                bool wasHidden = me.GetComponent<PlayerLife>().Hidden.Value;
                float dBefore = dummy.Current.Value;
                rogue.ServerStab(me.transform.position + Vector3.up * 1.5f, dummy.transform.forward);
                float opener = dBefore - dummy.Current.Value;
                Log("rogue ambush", "hidden " + wasHidden + " -> " + me.GetComponent<PlayerLife>().Hidden.Value + ", opener stab dealt " + opener.ToString("F0") + " (plain ~13)",
                    wasHidden && !me.GetComponent<PlayerLife>().Hidden.Value && opener > 30f);

                // Ranger: three full draws mark the dummy; a trap set on it springs.
                progressMe.ClassId.Value = Classes.ClassId.Ranger;
                yield return new WaitForSeconds(0.5f);
                var ranger = me.GetComponent<RangerKit>();
                dummy.ServerSetFraction(1f);
                me.GetComponent<FirstPersonMotor>().Teleport(dummy.transform.position + Vector3.right * 6f, Quaternion.LookRotation(Vector3.left));
                yield return new WaitForSeconds(0.2f);
                for (int i = 0; i < 3; i++)
                {
                    var o = me.transform.position + Vector3.up * 1.6f;
                    ranger.ServerShoot(o, dummy.Center - o, 1f);
                    yield return new WaitForSeconds(0.4f);
                }
                Log("ranger mark", "marked " + dummy.Marked.Value + ", dummy " + dummy.Current.Value.ToString("F0") + " / " + dummy.Max.Value.ToString("F0"), dummy.Marked.Value);
                // Snare Shot: the net-arrow bursts on the dummy, catches it, and the next arrow hits 25% harder.
                dummy.ServerSetFraction(1f);
                {
                    var o = me.transform.position + Vector3.up * 1.6f;
                    ranger.ServerSnareShot(o, dummy.Center - o);
                    for (float t = 0f; t < 1.5f && ranger.LastSnareCaught < 0; t += 0.1f) yield return new WaitForSeconds(0.1f);
                    float snaredBy = ranger.Snared(dummy);
                    Log("ranger snare", "caught " + ranger.LastSnareCaught + ", snared x" + snaredBy.ToString("F2") + ", dummy " + dummy.Current.Value.ToString("F0") + " / " + dummy.Max.Value.ToString("F0"),
                        ranger.LastSnareCaught >= 1 && snaredBy > 1.2f && dummy.Current.Value < dummy.Max.Value);
                }

                // Flagellant: swings heal (Penance) and build Fervor, the third crashes; Scourge costs blood for Zeal; the leap spends Fervor.
                progressMe.ClassId.Value = Classes.ClassId.Flagellant;
                yield return new WaitForSeconds(0.5f);
                {
                    var flag = me.GetComponent<FlagellantKit>();
                    var fh = me.GetComponent<Health>();
                    dummy.ServerSetFraction(1f);
                    me.GetComponent<FirstPersonMotor>().Teleport(dummy.transform.position + Vector3.right * 1.6f, Quaternion.LookRotation(Vector3.left));
                    yield return new WaitForSeconds(0.2f);
                    fh.ServerSetFraction(0.5f);
                    float hp0 = fh.Current.Value, d0 = dummy.Current.Value;
                    for (int i = 0; i < 3; i++)
                    {
                        flag.ServerSwing(me.transform.position + Vector3.up * 1.5f, dummy.Center - (me.transform.position + Vector3.up * 1.5f), 1f);
                        yield return new WaitForSeconds(0.1f);
                    }
                    float fervor = flag.Fervor.Value;
                    bool healed = fh.Current.Value > hp0, hurt = dummy.Current.Value < d0;
                    fh.ServerSetFraction(1f);
                    flag.ServerScourge();
                    yield return new WaitForSeconds(0.1f);
                    bool paid = fh.Fraction < 0.95f, zeal = flag.Zealous;
                    float d1 = dummy.Current.Value;
                    flag.ServerLeapCrash(dummy.transform.position);
                    yield return new WaitForSeconds(0.1f);
                    Log("flagellant", "swings hurt " + hurt + ", penance healed " + healed + ", fervor " + fervor.ToString("F0") + ", scourge paid " + paid + " zeal " + zeal + ", leap dealt " + (d1 - dummy.Current.Value).ToString("F0") + " fervor now " + flag.Fervor.Value.ToString("F0"),
                        hurt && healed && fervor > 0f && paid && zeal && dummy.Current.Value < d1 && flag.Fervor.Value < 1f);
                    bool chained = flag.ServerChain(dummy);
                    Log("flagellant chains", "chain landed " + chained, chained);
                    fh.ServerSetFraction(1f);
                }

                // Plague Doctor: a vial blights; a tonic heals; the mask cleanses and wards.
                progressMe.ClassId.Value = Classes.ClassId.PlagueDoctor;
                yield return new WaitForSeconds(0.5f);
                {
                    var pd = me.GetComponent<PlagueDoctorKit>();
                    var ph = me.GetComponent<Health>();
                    var pdb = me.GetComponent<PlayerDebuffs>();
                    dummy.ServerSetFraction(1f);
                    float d0 = dummy.Current.Value;
                    pd.ServerSplash(dummy.transform.position);
                    bool blighted = dummy.Blighted.Value;
                    yield return new WaitForSeconds(1.2f);
                    bool rotting = dummy.Current.Value < d0;
                    ph.ServerSetFraction(0.5f);
                    float h0 = ph.Current.Value;
                    int healedN = pd.ServerTonic(me.transform.position);
                    bool tonic = ph.Current.Value > h0;
                    pdb.ServerPoison(6f, 4f);
                    bool poisoned = pdb.Poisoned;
                    int masked = pd.ServerMask();
                    pdb.ServerPoison(6f, 4f);
                    Log("plague doctor", "vial blighted " + blighted + " rotting " + rotting + ", tonic healed " + healedN + " (" + tonic + "), poisoned " + poisoned + " -> mask cleansed " + !pdb.Poisoned + ", warded " + pdb.Warded,
                        blighted && rotting && tonic && poisoned && masked > 0 && !pdb.Poisoned && pdb.Warded);
                    pd.ServerMiasma(dummy.transform.position);
                    yield return new WaitForSeconds(0.7f);
                    Log("plague miasma", "dummy blighted in the cloud " + dummy.Blighted.Value, dummy.Blighted.Value);
                    ph.ServerSetFraction(1f);
                }

                // ---- Things that slipped through once (2026-11-03, the user: "add more test cases"). tools/run_tests.py
                // requires each of these names to appear, so one that stops running is a failure too. ----

                // Every kit answers the virtual pad with no cursor lock, as a bot in an unfocused window must (three of four
                // bots once stood there for a whole floor and nothing said so): attack, Q and E all really go off.
                {
                    var botPad = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Gamepad>();
                    var botKeys = UnityEngine.InputSystem.Keyboard.current == null ? UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Keyboard>() : null;
                    botPad.MakeCurrent();
                    bool hadLock = Cursor.lockState == CursorLockMode.Locked;
                    Cursor.lockState = CursorLockMode.None;
                    FirstPersonMotor.BotControl = true;
                    var padMotor = me.GetComponent<FirstPersonMotor>();
                    var views = new System.Collections.Generic.List<AbilityView>();
                    foreach (Classes.ClassId id in System.Enum.GetValues(typeof(Classes.ClassId)))
                    {
                        progressMe.ClassId.Value = id;
                        yield return new WaitForSeconds(0.5f);
                        var kit = me.GetComponents<IClassKit>().First(k => k.Class == id);
                        kit.RestoreResource(1f);
                        me.GetComponent<Health>().ServerSetFraction(1f);
                        dummy.ServerSetFraction(1f);
                        bool melee = id == Classes.ClassId.Knight || id == Classes.ClassId.Rogue || id == Classes.ClassId.Flagellant;
                        void Stand()
                        {
                            var at = dummy.transform.position + Vector3.back * ((melee ? 1.7f : 6f) + dummy.BodyRadius);
                            at.y = me.transform.position.y;
                            padMotor.Teleport(at, Quaternion.LookRotation(Vector3.forward));
                            var eye = at + Vector3.up * 1.6f;
                            padMotor.SetPitch(Mathf.Atan2(eye.y - dummy.Center.y, Vector3.Distance(new Vector3(eye.x, 0f, eye.z), new Vector3(dummy.Center.x, 0f, dummy.Center.z))) * Mathf.Rad2Deg);
                        }
                        Stand();
                        yield return new WaitForSeconds(0.2f);
                        float padStart = dummy.Current.Value;
                        UnityEngine.InputSystem.InputSystem.QueueStateEvent(botPad, new UnityEngine.InputSystem.LowLevel.GamepadState { rightTrigger = 1f });
                        yield return new WaitForSeconds(1f);
                        UnityEngine.InputSystem.InputSystem.QueueStateEvent(botPad, new UnityEngine.InputSystem.LowLevel.GamepadState());
                        yield return new WaitForSeconds(0.9f);
                        float attacked = padStart - dummy.Current.Value;
                        var went = new System.Collections.Generic.HashSet<string>();
                        foreach (var button in new[] { UnityEngine.InputSystem.LowLevel.GamepadButton.LeftShoulder, UnityEngine.InputSystem.LowLevel.GamepadButton.RightShoulder })
                        {
                            kit.RestoreResource(1f);
                            Stand();
                            yield return new WaitForSeconds(0.25f);
                            UnityEngine.InputSystem.InputSystem.QueueStateEvent(botPad, new UnityEngine.InputSystem.LowLevel.GamepadState().WithButton(button));
                            yield return new WaitForSeconds(0.1f);
                            UnityEngine.InputSystem.InputSystem.QueueStateEvent(botPad, new UnityEngine.InputSystem.LowLevel.GamepadState());
                            yield return new WaitForSeconds(0.15f);
                            views.Clear();
                            kit.FillAbilities(views);
                            foreach (var v in views) if (v.Remaining > 0.05f && (v.Key == "Q" || v.Key == "E")) went.Add(v.Key);
                        }
                        Log("pad " + id, "attack dealt " + attacked.ToString("F0") + ", abilities gone off: " + string.Join("+", went.OrderBy(k => k)) + ", hands free " + me.GetComponent<PlayerLife>().CanAct + ", ready " + padMotor.ActionsReady,
                            attacked > 0f && went.Count == 2);
                        // Whatever it left behind (a held beam, a leap in flight, a cloud) settles before the next class.
                        yield return new WaitForSeconds(0.5f);
                    }
                    FirstPersonMotor.BotControl = false;
                    Cursor.lockState = hadLock ? CursorLockMode.Locked : CursorLockMode.None;
                    UnityEngine.InputSystem.InputSystem.RemoveDevice(botPad);
                    if (botKeys != null) UnityEngine.InputSystem.InputSystem.RemoveDevice(botKeys);
                    me.GetComponent<Health>().Shield.Value = 0f; // the Cleric's Aegis would swallow the hits later checks measure
                    me.GetComponent<Health>().ServerSetFraction(1f);
                }

                // A bot's hands bypass the input system (unfocused windows drop its events): every kit must answer them too.
                {
                    FirstPersonMotor.BotControl = true;
                    foreach (Classes.ClassId id in System.Enum.GetValues(typeof(Classes.ClassId)))
                    {
                        progressMe.ClassId.Value = id;
                        yield return new WaitForSeconds(0.5f);
                        var kit = me.GetComponents<IClassKit>().First(k => k.Class == id);
                        kit.ShaveCooldowns(120f);
                        kit.RestoreResource(1f);
                        dummy.ServerSetFraction(1f);
                        bool melee = id == Classes.ClassId.Knight || id == Classes.ClassId.Rogue || id == Classes.ClassId.Flagellant;
                        var at = dummy.transform.position + Vector3.back * ((melee ? 1.7f : 6f) + dummy.BodyRadius);
                        at.y = me.transform.position.y;
                        var bm = me.GetComponent<FirstPersonMotor>();
                        bm.Teleport(at, Quaternion.LookRotation(Vector3.forward));
                        var eye = at + Vector3.up * 1.6f;
                        bm.SetPitch(Mathf.Atan2(eye.y - dummy.Center.y, Vector3.Distance(new Vector3(eye.x, 0f, eye.z), new Vector3(dummy.Center.x, 0f, dummy.Center.z))) * Mathf.Rad2Deg);
                        yield return new WaitForSeconds(0.3f);
                        float b0 = dummy.Current.Value;
                        UI.Binds.BotSetPrimary(true);
                        yield return new WaitForSeconds(1f);
                        UI.Binds.BotSetPrimary(false);
                        yield return new WaitForSeconds(0.9f);
                        float hit = b0 - dummy.Current.Value;
                        kit.RestoreResource(1f);
                        UI.Binds.BotPress(UI.Bind.AbilityE);
                        yield return new WaitForSeconds(0.25f);
                        var botViews = new System.Collections.Generic.List<AbilityView>();
                        kit.FillAbilities(botViews);
                        bool went = botViews.Any(v => v.Key == "E" && v.Remaining > 0.05f);
                        Log("bot hands " + id, "attack dealt " + hit.ToString("F0") + ", E went off " + went, hit > 0f && went);
                        yield return new WaitForSeconds(0.4f);
                    }
                    FirstPersonMotor.BotControl = false;
                    me.GetComponent<Health>().Shield.Value = 0f;
                    me.GetComponent<Health>().ServerSetFraction(1f);
                }

                // The interface: no window size and no setting may run the menus off the screen; the layout space fills the window.
                {
                    bool fits = true, grows = true;
                    float last = 0f;
                    foreach (float sh in new[] { 600f, 720f, 768f, 900f, 1080f, 1200f, 1440f, 1600f, 2160f, 4320f })
                    {
                        float f1 = UI.UiScale.For(sh, 1f);
                        if (f1 < last) grows = false;
                        last = f1;
                        foreach (float us in new[] { 0.75f, 0.9f, 1f, 1.15f, 1.3f, 1.5f })
                            if (sh / UI.UiScale.For(sh, us) < UI.UiScale.MinVirtualHeight - 0.5f) fits = false;
                    }
                    Log("ui space", "fits " + fits + ", grows with the window " + grows + ", virtual " + UI.UiScale.W.ToString("F0") + " x " + UI.UiScale.H.ToString("F0") + " at x" + UI.UiScale.Factor.ToString("F2") + " on " + Screen.width + " x " + Screen.height,
                        fits && grows && Mathf.Abs(UI.UiScale.W * UI.UiScale.Factor - Screen.width) < 1f && Mathf.Abs(UI.UiScale.H * UI.UiScale.Factor - Screen.height) < 1f);
                    float bottom = UI.Minimap.SmallBottom;
                    Log("minimap layout", "side " + UI.Minimap.SmallSize.ToString("F0") + ", what sits under it starts at " + bottom.ToString("F0"), !UI.Settings.ShowMinimap || bottom >= 18f + UI.Minimap.SmallSize);

                    var sizes = UI.Settings.Resolutions();
                    bool sorted = true;
                    for (int i = 1; i < sizes.Count; i++) if (sizes[i].y < sizes[i - 1].y || sizes[i] == sizes[i - 1]) sorted = false;
                    var chosen = UI.Settings.ChosenResolution;
                    Log("options lists", sizes.Count + " resolutions, sorted without repeats " + sorted + ", chosen " + chosen.w + " x " + chosen.h + " listed " + sizes.Contains(new Vector2Int(chosen.w, chosen.h)) + ", window mode " + UI.Settings.WindowMode,
                        sorted && (chosen.w <= 0 || sizes.Contains(new Vector2Int(chosen.w, chosen.h))) && UI.Settings.WindowMode >= 0 && UI.Settings.WindowMode < UI.Settings.WindowModeNames.Length && UI.Settings.FrameLimit < UI.Settings.FrameLimitNames.Length && UI.Settings.UiScaleIndex < UI.Settings.UiScaleNames.Length);
                }

                // No sound may be left without a recording: there is no synthesiser to fall back on, it would just be silent.
                {
                    var silent = System.Enum.GetValues(typeof(Art.Sound)).Cast<Art.Sound>().Where(sd => Resources.LoadAll<AudioClip>("Audio/Sfx/" + sd).Length == 0).ToList();
                    Log("every sound recorded", silent.Count == 0 ? "all " + System.Enum.GetValues(typeof(Art.Sound)).Length + " have clips" : "silent: " + string.Join(", ", silent), silent.Count == 0);
                }

                // Enemy sizes (2026-11-07): a spread of runts and hulks among plain fighters, none for bosses or odd shapes.
                {
                    int runts = 0, hulks = 0, n = 4000;
                    float lo = 9f, hi = 0f;
                    for (ulong id = 1; id <= (ulong)n; id++)
                    {
                        float s = Enemies.EnemyBrain.SizeFor(id, Art.CharacterArt.SkeletonKind.Warrior, false);
                        if (s < 0.8f) runts++; else if (s > 1.3f) hulks++;
                        lo = Mathf.Min(lo, s); hi = Mathf.Max(hi, s);
                    }
                    bool odd = Enemies.EnemyBrain.SizeFor(77, Art.CharacterArt.SkeletonKind.Mimic, false) == 1f && Enemies.EnemyBrain.SizeFor(77, Art.CharacterArt.SkeletonKind.Warrior, true) == 1f;
                    Log("enemy sizes", runts + " runts, " + hulks + " hulks in " + n + ", sizes " + lo.ToString("0.00") + "-" + hi.ToString("0.00") + (odd ? "" : ", a boss or mimic was sized"),
                        odd && runts > n * 0.04f && runts < n * 0.1f && hulks > n * 0.025f && hulks < n * 0.07f && lo >= 0.67f && hi <= 1.56f);
                }

                // The party's numbers, so a slip in the formulas shows: harder per head and more of it with each player, solo untouched.
                Log("party numbers", "hit x" + Difficulty.Damage(1) + " / " + Difficulty.Damage(2) + " / " + Difficulty.Damage(4) + ", boss x" + Difficulty.BossHealth(1) + " / " + Difficulty.BossHealth(2) + " / " + Difficulty.BossHealth(4) + ", crowd x" + Difficulty.SpawnCount(1) + " / " + Difficulty.SpawnCount(4),
                    Mathf.Approximately(Difficulty.Damage(1), 1f) && Mathf.Abs(Difficulty.Damage(2) - 1.22f) < 0.001f && Mathf.Abs(Difficulty.Damage(4) - 1.66f) < 0.001f
                    && Mathf.Approximately(Difficulty.BossDepth(1, 9), 1f) && Mathf.Abs(Difficulty.BossDepth(4, 9) - 2.62f) < 0.001f && Mathf.Approximately(Difficulty.BossDepth(4, 0), 1f)
                    && Mathf.Approximately(Difficulty.BossHealth(1), Difficulty.BossToughness) && Difficulty.BossHealth(4) > Difficulty.BossHealth(2) && Difficulty.BossHealth(2) > Difficulty.BossHealth(1)
                    && Difficulty.EnemyHealth(4) > Difficulty.EnemyHealth(1) && Difficulty.SpawnCount(4) > Difficulty.SpawnCount(1));

                // Traps: nothing hidden in the first act, more of it deeper, and the difficulty setting bites.
                {
                    bool climbs = true;
                    for (int d = 1; d < DungeonDirector.FloorsPerRun; d++) if (SpikeTrap.HiddenChance(d) < SpikeTrap.HiddenChance(d - 1)) climbs = false;
                    Log("trap beat", "vents dark " + (FlameJet.Period - FlameJet.Burn).ToString("F1") + " s, warned " + FlameJet.Warn + " s, plates rearm " + SpikeTrap.Rearm + " s", FlameJet.Period - FlameJet.Burn >= 0.8f && FlameJet.Period - FlameJet.Burn <= 1.2f && SpikeTrap.Rearm + SpikeTrap.Delay > SpikeTrap.ImpaleSeconds);
                    Log("trap numbers", "hidden plates " + SpikeTrap.HiddenChance(0).ToString("F2") + " on floor 1, " + SpikeTrap.HiddenChance(DungeonDirector.FloorsPerRun - 1).ToString("F2") + " on the last; a 100 bite is " + DungeonDirector.TrapHurt(100f).ToString("F0") + " on " + Difficulty.Level.Name,
                        SpikeTrap.HiddenChance(0) <= 0f && SpikeTrap.HiddenChance(1) <= 0f && climbs && SpikeTrap.HiddenChance(DungeonDirector.FloorsPerRun - 1) <= 1f && SpikeTrap.HiddenChance(DungeonDirector.FloorsPerRun - 1) > 0.3f
                        && Mathf.Approximately(DungeonDirector.TrapHurt(100f), 100f * Difficulty.Level.Damage));
                }

                progressMe.ClassId.Value = Classes.ClassId.Mage;
                yield return new WaitForSeconds(0.3f);
                DungeonDirector.Instance.ServerStartRun();
            }

            // Wait for the dungeon itself, not a guess at how long the lobby checks take.
            float waitUntil = Time.time + 180f; // the client waits out the host's lobby checks, however many there are
            while ((DungeonDirector.Layout == null || NetworkPlayer.Local == null) && Time.time < waitUntil) yield return null;
            yield return new WaitForSeconds(2f);

            var layout = DungeonDirector.Layout;
            Log("role", net.IsHost ? "host" : net.IsClient ? "client" : "none", net.IsHost || net.IsConnectedClient);
            if (net.IsHost && DungeonDirector.Instance != null)
            {
                var rows = BalanceReport.Compute(DungeonDirector.Instance.AllEnemyPrefabs());
                Debug.Log(BalanceReport.Format(rows));
                Log("balance report", rows.Count + " rows, " + rows.Count(r => r.Flag != null) + " flagged", rows.Count > 0);
            }
            Log("dungeon", layout != null ? layout.Rooms.Count + " rooms, depth " + layout.Depth : "missing", layout != null);

            int enemies = Health.All.Count(h => h != null && h.Team == Team.Enemies);
            Log("enemies", enemies.ToString(), enemies > 0);
            Log("players", PlayerLife.All.Count.ToString(), PlayerLife.All.Count > 0);
            // The interface follows the window: 1:1 at 1080p, x2 at 4K, x0.75 at 720p, never so big the menus run off.
            Log("ui scale", "720p x" + UI.UiScale.For(720f, 1f) + ", 1080p x" + UI.UiScale.For(1080f, 1f) + ", 1440p x" + UI.UiScale.For(1440f, 1f).ToString("F2") + ", 4K x" + UI.UiScale.For(2160f, 1f) + "; 720p at 150% x" + UI.UiScale.For(720f, 1.5f),
                Mathf.Approximately(UI.UiScale.For(1080f, 1f), 1f) && Mathf.Approximately(UI.UiScale.For(2160f, 1f), 2f) && Mathf.Approximately(UI.UiScale.For(720f, 1f), 0.75f) && Mathf.Approximately(UI.UiScale.For(720f, 1.5f), 1f));

            // Voice: the codec survives a round trip, and a client's voice reaches the host.
            float snr = VoiceCodec.RoundTripSnr();
            Log("voice codec", "round trip SNR " + snr.ToString("F1") + " dB", snr > 15f);
            if (!net.IsServer && NetworkPlayer.Local != null)
            {
                var tone = new float[VoiceCodec.SampleRate * 6 / 5];
                for (int i = 0; i < tone.Length; i++) tone[i] = 0.25f * Mathf.Sin(i * 2f * Mathf.PI * 330f / VoiceCodec.SampleRate);
                NetworkPlayer.Local.GetComponent<PlayerVoice>().DebugInject(tone);
            }

            var local = NetworkPlayer.Local;
            if (local != null && layout != null)
            {
                var room = layout.RoomAt(local.transform.position);
                Log("local player", local.transform.position.ToString("F1") + " in " + (room != null ? room.Kind.ToString() : "no room"),
                    room != null && local.transform.position.y > -1f);
            }
            else
            {
                Log("local player", "missing", false);
            }

            if (local != null)
            {
                local.GetComponent<PlayerPings>().PingSpot(local.transform.position + local.transform.forward * 5f);
                yield return new WaitForSeconds(0.5f);
                Log("ping round trip", "own ping visible: " + PlayerPings.Active.ContainsKey(local.NetworkObjectId), PlayerPings.Active.ContainsKey(local.NetworkObjectId));
                local.GetComponent<PlayerPings>().PingKind(PlayerPings.Kind.Danger, local.transform.position + local.transform.forward * 6f);
                yield return new WaitForSeconds(0.5f);
                bool danger = PlayerPings.Active.TryGetValue(local.NetworkObjectId, out var dp) && dp.Kind == PlayerPings.Kind.Danger;
                local.GetComponent<PlayerPings>().PingKind(PlayerPings.Kind.Help, local.transform.position);
                yield return new WaitForSeconds(0.5f);
                bool help = PlayerPings.Active.TryGetValue(local.NetworkObjectId, out var hp2) && hp2.Kind == PlayerPings.Kind.Help && hp2.Follow == local.transform;
                Log("ping wheel", "danger " + danger + ", help pinned to self " + help, danger && help);
            }

            var boss = BossBrain.Active;
            Log("boss", boss != null ? boss.Health.Current.Value + " hp" : "missing", boss != null);
            if (boss != null)
                Log("boss intro", boss.DisplayName + ", " + boss.Epithet, !string.IsNullOrEmpty(boss.Epithet) && !string.IsNullOrEmpty(boss.Hint) && boss.Epithet != "Guardian of the deep");

            // The client stays for the host's two-player checks (rage, voice, the soul skull) and leaves once it has
            // been carried back from a soul, so the host finishes the rest alone.
            if (!net.IsServer && local != null)
            {
                local.ChatRpc("smoke chat from the client");
                var myLife = local.GetComponent<PlayerLife>();
                bool wasSoul = false;
                float leaveBy = Time.time + seconds;
                while (Time.time < leaveBy && !(wasSoul && !myLife.Soul.Value && !myLife.Downed.Value))
                {
                    wasSoul |= myLife.Soul.Value;
                    yield return null;
                }
                yield return new WaitForSeconds(1f);
                Log("final phase", DungeonDirector.Instance != null ? DungeonDirector.Instance.Phase.Value.ToString() : "no director", true);
                Debug.Log("[Smoke] done");
                Application.Quit(0);
                yield break;
            }

            // Early, while the client is still connected.
            if (net.IsServer && local != null && layout != null)
            {
                // Rage: an enemy hunts whoever angers it most, switching only when a rival passes them by a fifth.
                var others = PlayerLife.All.Where(pl => pl != null && pl.gameObject != local.gameObject).ToList();
                // A plain brain (no Update of its own): a skeleton, else a brute or an archer, whichever the seed gave us.
                var rager = FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None).Where(e => !e.IsDead && !e.IsChampion &&
                        (e.GetType() == typeof(EnemyBrain) || e is BruteBrain || e is ArcherBrain))
                    .OrderBy(e => e.GetType() == typeof(EnemyBrain) ? 0 : e is BruteBrain ? 1 : 2).FirstOrDefault();
                if (rager == null || others.Count == 0) Log("rage", "no plain enemy (" + (rager != null) + ") or no second player (" + others.Count + ")", false);
                else
                {
                    var friend = others[0];
                    var trace = new System.Text.StringBuilder();
                    System.Action<string> mark = stage => trace.Append(" [" + stage + ": hp " + friend.Health.Current.Value.ToString("0") + "/" + friend.Health.Max.Value.ToString("0") +
                        " down " + friend.Downed.Value + " soul " + friend.Soul.Value + " spawned " + friend.IsSpawned + " rage " + rager.ThreatOf(friend.NetworkObjectId).ToString("0") + "]");
                    mark("start");
                    // Next to the two of them (it only hunts within its leash).
                    rager.ServerDebugPlace(friend.transform.position + (local.transform.position - friend.transform.position) * 0.5f + Vector3.right * 2f);
                    // Elite (so its meter syncs), with a harmless affix: no splitting or bursting into the next checks.
                    if (!rager.Elite.Value) rager.ServerMakeElite(EliteAffix.Swift);
                    foreach (var pl in PlayerLife.All) if (pl != null) rager.ServerForget(pl.Health);
                    rager.ServerAddThreat(friend.NetworkObjectId, 100f);
                    mark("added");
                    // It only re-thinks between swings: give it a moment each time.
                    bool onFriend = false;
                    for (int k = 0; k < 25 && !onFriend; k++) { yield return new WaitForSeconds(0.1f); onFriend = rager.CurrentTarget == friend.Health; }
                    rager.ServerAddThreat(local.NetworkObjectId, rager.ThreatOf(friend.NetworkObjectId) - rager.ThreatOf(local.NetworkObjectId));
                    yield return new WaitForSeconds(0.6f);
                    bool held = rager.CurrentTarget == friend.Health;      // level with it: not enough to take it
                    mark("held");
                    rager.ServerAddThreat(local.NetworkObjectId, rager.ThreatOf(friend.NetworkObjectId) * 0.4f);
                    bool pulled = false;
                    for (int k = 0; k < 25 && !pulled; k++) { yield return new WaitForSeconds(0.1f); pulled = rager.CurrentTarget == local.GetComponent<Health>(); }
                    float friendAfterPull = rager.ThreatOf(friend.NetworkObjectId);
                    mark("pulled");
                    yield return new WaitForSeconds(0.3f);
                    float friendBefore = rager.ThreatOf(friend.NetworkObjectId);
                    float before = rager.ThreatOf(local.NetworkObjectId);
                    rager.GetComponent<Health>().ServerDamage(5f, local.transform.position, local.NetworkObjectId);
                    bool hitsAnger = rager.ThreatOf(local.NetworkObjectId) > before;
                    bool synced = rager.Rage.Value.Count >= 2 && rager.Rage.Value.Target == local.NetworkObjectId;
                    Log("rage", "on first " + onFriend + ", held when level " + held + ", pulled at +40% " + pulled + ", hits anger " + hitsAnger + ", meter synced " + synced + (onFriend && held && pulled && hitsAnger && synced ? "" :
                        " (rows " + rager.Rage.Value.Count + ", target " + rager.Rage.Value.Target + " vs me " + local.NetworkObjectId + ", elite " + rager.Elite.Value +
                        ", friend " + friend.NetworkObjectId + " rage " + rager.ThreatOf(friend.NetworkObjectId).ToString("0") + " / me " + rager.ThreatOf(local.NetworkObjectId).ToString("0") +
                        ", friend before hit " + friendBefore.ToString("0") + ", after pull " + friendAfterPull.ToString("0") + ", friend alive " + friend.Health.IsAlive + " targetable " + PlayerLife.CanTarget(friend.Health) + ")" + trace), onFriend && held && pulled && hitsAnger && synced);
                    rager.GetComponent<Health>().ServerDamage(1e6f, rager.transform.position); // out of the way of the next checks
                    yield return new WaitForSeconds(0.2f);
                }

                // Text chat: the client's line reaches the host.
                for (int k = 0; k < 30 && !UI.ChatBox.Heard("smoke chat from the client"); k++) yield return new WaitForSeconds(0.1f);
                Log("chat", "heard the client: " + UI.ChatBox.Heard("smoke chat from the client") + " (last: " + UI.ChatBox.Last + ")", UI.ChatBox.Heard("smoke chat from the client"));

                // Shared loot: gold one player picks up is split across the party; a pack piece can be handed over.
                var mate = PlayerLife.All.FirstOrDefault(pl => pl != null && pl.gameObject != local.gameObject);
                if (mate != null)
                {
                    var myP = local.GetComponent<PlayerProgress>();
                    var mateP = mate.GetComponent<PlayerProgress>();
                    int mineBefore = myP.Gold.Value, mateBefore = mateP.Gold.Value;
                    DungeonDirector.Instance.ServerDropLoot(local.transform.position + Vector3.up * 0.3f, LootKind.Gold, 40);
                    for (int k = 0; k < 20 && myP.Gold.Value == mineBefore; k++) yield return new WaitForSeconds(0.1f);
                    int gotMine = myP.Gold.Value - mineBefore, gotMate = mateP.Gold.Value - mateBefore;
                    Log("shared gold", "picked up 40 (x gold bonus): me +" + gotMine + ", ally +" + gotMate + " (rule " + (DungeonDirector.ShareLoot ? "shared" : "finders keepers") + ")",
                        gotMate > 0 && Mathf.Abs(gotMine - gotMate) <= 1);
                    int mateBag = mateP.BagCount;
                    myP.ServerStow(new GearItem(GearId.HuntersTooth, 0, 0));
                    myP.BagGiftRpc(myP.BagCount - 1);
                    yield return new WaitForSeconds(0.3f);
                    myP.ServerAddGold(100);
                    int beforeGive = mateP.Gold.Value;
                    myP.GiveGoldRpc(mate.NetworkObjectId, 25);
                    yield return new WaitForSeconds(0.3f);
                    Log("gifts", "ally's pack " + mateBag + " -> " + mateP.BagCount + ", gave 25 gold: ally " + beforeGive + " -> " + mateP.Gold.Value,
                        mateP.BagCount == mateBag + 1 && mateP.Gold.Value == beforeGive + 25);
                }

                // Boss phases: damage stops at the first gate (50%), the boss goes immune for the transition.
                var gateBoss = BossBrain.Active;
                if (gateBoss != null && gateBoss is GravewardenBrain)
                {
                    var bh = gateBoss.Health;
                    gateBoss.Dormant = false; // asleep it is immune; the gate test needs it awake
                    bh.ServerSetFraction(0.56f);
                    yield return null;
                    bh.ServerDamage(bh.Max.Value * 0.3f, gateBoss.transform.position + gateBoss.transform.forward * -3f);
                    float atGate = bh.Fraction;
                    for (float t = 0f; t < 1f && !gateBoss.Immune; t += 0.05f) yield return new WaitForSeconds(0.05f);
                    bool immune = gateBoss.Immune;
                    float before = bh.Current.Value;
                    bh.ServerDamage(200f, gateBoss.transform.position + gateBoss.transform.forward * -3f);
                    bool held = Mathf.Approximately(before, bh.Current.Value);
                    Log("boss phase gate", "hp stopped at " + (atGate * 100f).ToString("F1") + "%, stance " + gateBoss.CurrentStance + " (" + gateBoss.PhaseName.Value + "), immune hit held " + held,
                        Mathf.Abs(atGate - 0.5f) < 0.01f && immune && held);
                    gateBoss.ServerDebugResetPhases();
                    bh.ServerSetFraction(1f);
                    gateBoss.Dormant = true;
                }

                var remoteVoice = PlayerLife.All.Where(pl => pl != null && pl.gameObject != local.gameObject).Select(pl => pl.GetComponent<PlayerVoice>()).FirstOrDefault();
                if (remoteVoice != null)
                {
                    for (float t = 0f; t < 12f && remoteVoice.ServerFramesFrom < 10; t += 0.2f) yield return new WaitForSeconds(0.2f);
                    yield return new WaitForSeconds(0.3f);
                    Log("voice round trip", "host got " + remoteVoice.ServerFramesFrom + " frames from the client, played " + remoteVoice.FramesHeard +
                        " (distance " + Vector3.Distance(local.transform.position, remoteVoice.transform.position).ToString("F0") + " m)", remoteVoice.ServerFramesFrom >= 10);
                }

                var fm = local.GetComponent<FirstPersonMotor>();
                // Sheltered: behind a blocking Knight, blows from the front land at half strength.
                var behind = PlayerLife.All.FirstOrDefault(pl => pl != null && pl.gameObject != local.gameObject);
                if (behind != null)
                {
                    var hp0 = local.GetComponent<PlayerProgress>();
                    var classBefore = hp0.ClassId.Value;
                    hp0.ClassId.Value = Classes.ClassId.Knight;
                    var kk = local.GetComponent<KnightKit>();
                    kk.DebugHoldBlock = true;
                    yield return new WaitForSeconds(0.2f);
                    var front = local.transform.position + local.transform.forward * 5f;
                    var rear = local.transform.position - local.transform.forward * 5f;
                    var spotBehind = local.transform.position - local.transform.forward * 1.5f;
                    // Ask the question directly at the spot behind (the client owns its own position).
                    var was = behind.transform.position;
                    behind.transform.position = spotBehind;
                    bool fromFront = KnightKit.ShelterFor(behind, front), fromRear = KnightKit.ShelterFor(behind, rear);
                    behind.transform.position = was;
                    kk.DebugHoldBlock = false;
                    hp0.ClassId.Value = classBefore;
                    Log("sheltered", "blow from the front sheltered " + fromFront + ", from behind " + fromRear, fromFront && !fromRear);
                }
                // Dragging: the host takes the downed client by the collar and hauls it a few metres.
                var dragged = PlayerLife.All.FirstOrDefault(pl => pl != null && pl.gameObject != local.gameObject);
                if (dragged != null)
                {
                    var hostLife = local.GetComponent<PlayerLife>();
                    fm.Teleport(dragged.transform.position + Vector3.right * 1.2f + Vector3.up * 0.1f, local.transform.rotation);
                    yield return new WaitForSeconds(0.2f);
                    dragged.Health.ServerDamage(1e6f, dragged.transform.position + Vector3.forward);
                    yield return new WaitForSeconds(0.4f);
                    float speedBefore = local.GetComponent<PlayerProgress>().SpeedMultiplier;
                    dragged.DragRpc(local.NetworkObject);
                    yield return new WaitForSeconds(0.3f);
                    bool holding = dragged.BeingDragged && !hostLife.CanAct && local.GetComponent<PlayerProgress>().SpeedMultiplier < speedBefore;
                    var startAt = dragged.transform.position;
                    // Walk away in short steps (the drag breaks past 4 m), giving the client time to follow.
                    var away = local.transform.position - startAt;
                    away.y = 0f;
                    away = away.sqrMagnitude > 0.01f ? away.normalized : Vector3.right;
                    for (int st = 0; st < 8; st++)
                    {
                        fm.Teleport(local.transform.position + away * 0.5f, Quaternion.LookRotation(away));
                        yield return new WaitForSeconds(0.25f);
                    }
                    yield return new WaitForSeconds(0.5f);
                    float moved = Vector3.Distance(dragged.transform.position, startAt);
                    dragged.DragRpc(local.NetworkObject);
                    yield return new WaitForSeconds(0.3f);
                    Log("drag the downed", "held " + holding + ", body moved " + moved.ToString("F1") + " m, let go " + !dragged.BeingDragged, holding && moved > 2f && !dragged.BeingDragged);
                    dragged.ServerRevive(1f);
                    yield return new WaitForSeconds(0.3f);
                    // The give wheel (2026-11-07): 25 gold and a health potion from the host to the ally beside them.
                    {
                        var giver = local.GetComponent<GiveWheel>();
                        var myProg = local.GetComponent<PlayerProgress>();
                        var theirProg = dragged.GetComponent<PlayerProgress>();
                        var myPouch = local.GetComponent<Flasks>();
                        var theirPouch = dragged.GetComponent<Flasks>();
                        local.GetComponent<FirstPersonMotor>().Teleport(dragged.transform.position + Vector3.right * 1.5f + Vector3.up * 0.1f, local.transform.rotation);
                        yield return null;
                        myProg.Gold.Value += 60;
                        myPouch.ServerAdd(Flasks.Kind.Health, 1);
                        while (theirPouch.Count(Flasks.Kind.Health) >= Flasks.Max) theirPouch.ServerTakeOne(Flasks.Kind.Health);
                        int g0 = theirProg.Gold.Value, h0 = theirPouch.Count(Flasks.Kind.Health), mine0 = myPouch.Count(Flasks.Kind.Health);
                        bool gaveGold = giver.ServerGive(dragged.NetworkObjectId, GiveWheel.Gift.Gold);
                        bool gavePotion = giver.ServerGive(dragged.NetworkObjectId, GiveWheel.Gift.Health);
                        yield return new WaitForSeconds(0.2f);
                        bool ok = gaveGold && gavePotion && theirProg.Gold.Value == g0 + GiveWheel.GoldStep && theirPouch.Count(Flasks.Kind.Health) == h0 + 1 && myPouch.Count(Flasks.Kind.Health) == mine0 - 1;
                        Log("give wheel", "gold +" + (theirProg.Gold.Value - g0) + ", potion " + h0 + " -> " + theirPouch.Count(Flasks.Kind.Health) + ", giver " + mine0 + " -> " + myPouch.Count(Flasks.Kind.Health), ok);
                    }
                }
                // Teamwork: a pincer from opposite sides, an assist on the kill, then a last tw_stand and a clutch revive.
                {
                    var tmate = PlayerLife.All.FirstOrDefault(pl => pl != null && pl.gameObject != local.gameObject && !pl.Downed.Value);
                    if (tmate != null)
                    {
                        var tw_hostLife = local.GetComponent<PlayerLife>();
                        var hostProg = local.GetComponent<PlayerProgress>();
                        var mateProg = tmate.GetComponent<PlayerProgress>();
                        var mateTeam = tmate.GetComponent<Teamwork>();
                        var hostTeam = local.GetComponent<Teamwork>();
                        // Stand 6 m from the client so the enemy can sit between the two of us.
                        var tw_side = local.transform.position - tmate.transform.position;
                        tw_side.y = 0f;
                        tw_side = tw_side.sqrMagnitude > 0.01f ? tw_side.normalized : Vector3.right;
                        fm.Teleport(tmate.transform.position + tw_side * 6f + Vector3.up * 0.1f, Quaternion.LookRotation(-tw_side));
                        yield return new WaitForSeconds(0.3f);
                        var tw_mid = (local.transform.position + tmate.transform.position) / 2f;
                        var tw_foe = DungeonDirector.Instance.ServerDebugSpawn("skeleton", tw_mid, false);
                        yield return new WaitForSeconds(0.3f);
                        float tw_pinc = 0f, tw_sameSide = 0f;
                        int assistsBefore = mateTeam != null ? mateTeam.ServerAssists : 0;
                        bool tw_assisted = false;
                        if (tw_foe != null)
                        {
                            // Party size changes mid-floor: the foe was scaled for two; rescaled for one it keeps its hurt
                            // fraction and drops to the solo multiplier; back to two restores it.
                            var rsH = tw_foe.GetComponent<Health>();
                            rsH.ServerSetFraction(0.6f);
                            float max2 = rsH.Max.Value;
                            tw_foe.ServerRescaleParty(1);
                            float max1 = rsH.Max.Value, frac1 = rsH.Fraction;
                            tw_foe.ServerRescaleParty(2);
                            float want = Difficulty.EnemyHealth(1) / Difficulty.EnemyHealth(2);
                            Log("party rescale", "max " + max2.ToString("F0") + " -> " + max1.ToString("F0") + " (x" + (max1 / max2).ToString("F2") + ", want x" + want.ToString("F2") + "), fraction kept " + frac1.ToString("F2") + ", back " + rsH.Max.Value.ToString("F0"),
                                Mathf.Abs(max1 / max2 - want) < 0.02f && Mathf.Abs(frac1 - 0.6f) < 0.02f && Mathf.Abs(rsH.Max.Value - max2) < 1f);
                            rsH.ServerSetFraction(1f);
                            var tw_fh = tw_foe.GetComponent<Health>();
                            // Wherever the navmesh put it: the host steps round to its far side from the client.
                            var fpos = tw_foe.transform.position;
                            var away = fpos - tmate.transform.position;
                            away.y = 0f;
                            away = away.sqrMagnitude > 0.01f ? away.normalized : Vector3.right;
                            fm.Teleport(fpos + away * 2f + Vector3.up * 0.1f, Quaternion.LookRotation(-away));
                            yield return new WaitForSeconds(0.1f);
                            Teamwork.ServerPincer(tw_fh, mateProg);
                            tw_pinc = Teamwork.ServerPincer(tw_fh, hostProg);
                            // A second enemy beside the client: both of us hit it from the same tw_side.
                            var tw_foe2 = DungeonDirector.Instance.ServerDebugSpawn("skeleton", tmate.transform.position - tw_side * 2f, false);
                            yield return new WaitForSeconds(0.3f);
                            if (tw_foe2 != null)
                            {
                                var tw_f2 = tw_foe2.GetComponent<Health>();
                                // This time the host stands right beside the client.
                                fm.Teleport(tmate.transform.position + Vector3.Cross(Vector3.up, tw_side) * 0.8f + Vector3.up * 0.1f, local.transform.rotation);
                                yield return new WaitForSeconds(0.1f);
                                Teamwork.ServerPincer(tw_f2, mateProg);
                                tw_sameSide = Teamwork.ServerPincer(tw_f2, hostProg);
                                tw_f2.ServerDamage(1e6f, tw_f2.transform.position, local.NetworkObjectId);
                            }
                            tw_fh.ServerDamage(1e6f, tw_fh.transform.position, local.NetworkObjectId);
                            yield return new WaitForSeconds(0.3f);
                            tw_assisted = mateTeam != null && mateTeam.ServerAssists > assistsBefore;
                        }
                        Log("pincer and assist", "opposite sides x" + tw_pinc.ToString("F2") + ", same side x" + tw_sameSide.ToString("F2") + ", assist credited " + tw_assisted,
                            Mathf.Approximately(tw_pinc, Teamwork.PincerMultiplier) && Mathf.Approximately(tw_sameSide, 1f) && tw_assisted);

                        // Last tw_stand: the client goes down; the host is the last one standing, then gets them up.
                        fm.Teleport(tmate.transform.position + tw_side * 1.2f + Vector3.up * 0.1f, local.transform.rotation);
                        yield return new WaitForSeconds(0.2f);
                        int clutchesBefore = hostTeam.ServerClutches;
                        tmate.Health.ServerDamage(1e6f, tmate.transform.position + Vector3.forward);
                        yield return new WaitForSeconds(0.4f);
                        bool tw_stand = Teamwork.InLastStand(tw_hostLife);
                        float tw_taken = 100f;
                        foreach (var mod in local.GetComponent<Health>().IncomingModifiers) tw_taken = mod(tw_taken, local.transform.position + Vector3.forward * 30f);
                        for (int i = 0; i < 40 && tmate.Downed.Value; i++)
                        {
                            tmate.ReviveHoldRpc(local.NetworkObject);
                            yield return new WaitForSeconds(0.1f);
                        }
                        yield return new WaitForSeconds(0.3f);
                        Debug.Log("[Smoke] clutch: revive gap " + Vector3.Distance(local.transform.position, tmate.transform.position).ToString("F1") + " m, progress " + tmate.ReviveProgress.Value.ToString("F2") + ", downed " + tmate.Downed.Value);
                        bool tw_clutched = hostTeam.ServerClutches > clutchesBefore && !tmate.Downed.Value && local.GetComponent<Emotes>().Brothers;
                        Log("last stand and clutch", "last stand " + tw_stand + " (100 taken -> " + tw_taken.ToString("F0") + "), clutch " + tw_clutched + ", after it " + Teamwork.InLastStand(tw_hostLife),
                            tw_stand && tw_taken < 100f && tw_clutched && !Teamwork.InLastStand(tw_hostLife));
                        if (tmate.Downed.Value) tmate.ServerRevive(1f);
                        yield return new WaitForSeconds(0.3f);

                        // Shared doom (2026-10-29): split between everyone inside the ring; borne alone it all lands on one.
                        {
                            var dh = local.GetComponent<Health>();
                            var mh = tmate.Health;
                            fm.Teleport(tmate.transform.position + tw_side * 1.2f + Vector3.up * 0.1f, local.transform.rotation);
                            yield return new WaitForSeconds(0.2f);
                            dh.ServerSetFraction(1f); mh.ServerSetFraction(1f); dh.Shield.Value = 0f; mh.Shield.Value = 0f;
                            float d0 = dh.Current.Value, m0 = mh.Current.Value;
                            int sharedBy = BossBrain.ServerResolveDoom(tmate.transform.position, 60f, ulong.MaxValue);
                            float dLost = d0 - dh.Current.Value, mLost = m0 - mh.Current.Value;
                            yield return new WaitForSeconds(0.2f);
                            mh.ServerSetFraction(1f); mh.Shield.Value = 0f;
                            fm.Teleport(tmate.transform.position + tw_side * 8f + Vector3.up * 0.1f, local.transform.rotation);
                            yield return new WaitForSeconds(0.2f);
                            m0 = mh.Current.Value;
                            float d1 = dh.Current.Value;
                            int aloneBy = BossBrain.ServerResolveDoom(tmate.transform.position, 60f, ulong.MaxValue);
                            float aloneLost = m0 - mh.Current.Value;
                            bool hostSpared = Mathf.Approximately(d1, dh.Current.Value);
                            Log("shared doom", "shared by " + sharedBy + " (host -" + dLost.ToString("F0") + ", client -" + mLost.ToString("F0") + "), alone by " + aloneBy + " (-" + aloneLost.ToString("F0") + "), host spared " + hostSpared,
                                sharedBy == 2 && aloneBy == 1 && dLost > 0f && mLost > 0f && aloneLost > mLost * 1.5f && hostSpared);
                            dh.ServerSetFraction(1f); mh.ServerSetFraction(1f);
                            // Windowed runs only: a picture of soulbound twins and a doom ring on the client, from 8 m off.
                            if (!Application.isBatchMode)
                            {
                                // Along wherever the camera looks (the room's open side), not a guessed direction.
                                var look = Art.ViewCamera.Current != null ? Art.ViewCamera.Current.transform.forward : local.transform.forward;
                                look.y = 0f; look = look.sqrMagnitude > 0.01f ? look.normalized : Vector3.forward;
                                var across = Vector3.Cross(Vector3.up, look);
                                var eye = local.transform.position;
                                var t1 = DungeonDirector.Instance.ServerDebugSpawn("skeleton", eye + look * 4.5f + across * 1.6f, false);
                                var t2 = DungeonDirector.Instance.ServerDebugSpawn("skeleton", eye + look * 4.5f - across * 1.6f, false);
                                Debug.Log("[Smoke] coop shot: twins at " + (t1 != null ? Vector3.Distance(eye, t1.transform.position).ToString("F1") : "-") + " / " + (t2 != null ? Vector3.Distance(eye, t2.transform.position).ToString("F1") : "-") + " m");
                                if (t1 != null && t2 != null)
                                {
                                    t1.ServerStun(10f); t2.ServerStun(10f);
                                    EnemyBrain.ServerBind(t1, t2);
                                    // Stand the pair in front of the camera (it keeps its own heading; turning the body does not turn it).
                                    var ag1 = t1.GetComponent<UnityEngine.AI.NavMeshAgent>();
                                    var ag2 = t2.GetComponent<UnityEngine.AI.NavMeshAgent>();
                                    var spot1 = eye + look * 5f + across * 1.6f;
                                    var spot2 = eye + look * 5f - across * 1.6f;
                                    if (ag1 != null && ag1.enabled) ag1.Warp(spot1); else t1.transform.position = spot1;
                                    if (ag2 != null && ag2.enabled) ag2.Warp(spot2); else t2.transform.position = spot2;
                                    yield return new WaitForSeconds(2.5f);
                                    Art.DoomMark.Attach(t1.transform, BossBrain.DoomRadius, 3f, BossBrain.DoomColor);
                                    yield return new WaitForSeconds(1.6f);
                                    var shotCam = Art.ViewCamera.Current;
                                    if (shotCam != null)
                                    {
                                        // The camera's own heading, read now (it is not the body's, nor what it was a few seconds ago).
                                        var cf = shotCam.transform.forward; cf.y = 0f; cf = cf.sqrMagnitude > 0.01f ? cf.normalized : Vector3.forward;
                                        var cr = Vector3.Cross(Vector3.up, cf);
                                        var floor = new Vector3(shotCam.transform.position.x, local.transform.position.y, shotCam.transform.position.z);
                                        if (ag1 != null && ag1.enabled) ag1.Warp(floor + cf * 5f + cr * 1.6f); else t1.transform.position = floor + cf * 5f + cr * 1.6f;
                                        if (ag2 != null && ag2.enabled) ag2.Warp(floor + cf * 5f - cr * 1.6f); else t2.transform.position = floor + cf * 5f - cr * 1.6f;
                                        yield return new WaitForSeconds(0.3f);
                                    }
                                    if (shotCam != null)
                                        Debug.Log("[Smoke] coop shot: twin 1 alive " + t1.GetComponent<Health>().IsAlive + " at view " + shotCam.WorldToViewportPoint(t1.transform.position + Vector3.up) +
                                            ", twin 2 alive " + t2.GetComponent<Health>().IsAlive + " at view " + shotCam.WorldToViewportPoint(t2.transform.position + Vector3.up) + ", cam " + shotCam.transform.position + " host " + local.transform.position);
                                    yield return new WaitForEndOfFrame();
                                    ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(Application.dataPath, "..", "coop.png"));
                                    yield return new WaitForSeconds(0.3f);
                                    t1.GetComponent<Health>().ServerDamage(1e6f, t1.transform.position);
                                    t2.GetComponent<Health>().ServerDamage(1e6f, t2.transform.position);
                                }
                            }
                        }

                        // Vengeance: a skeleton downs the client; it is marked; the host kills it and avenges them.
                        var grudgeFoe = DungeonDirector.Instance.ServerDebugSpawn("skeleton", tmate.transform.position + Vector3.forward * 2f, false);
                        yield return new WaitForSeconds(0.3f);
                        bool avenged = false, marked = false;
                        if (grudgeFoe != null)
                        {
                            int before = Teamwork.ServerAvenged;
                            var gh = grudgeFoe.GetComponent<Health>();
                            tmate.Health.ServerDamage(1e6f, grudgeFoe.transform.position, grudgeFoe.NetworkObjectId);
                            yield return new WaitForSeconds(0.3f);
                            marked = gh.Marked.Value;
                            gh.ServerDamage(1e6f, gh.transform.position, local.NetworkObjectId);
                            yield return new WaitForSeconds(0.3f);
                            avenged = Teamwork.ServerAvenged > before;
                        }
                        Log("vengeance", "downed by a skeleton: marked " + marked + ", avenged " + avenged, marked && avenged);
                        if (tmate.Downed.Value) tmate.ServerRevive(1f);
                        yield return new WaitForSeconds(0.3f);

                        // Floor honours: a fresh floor tally, one kill by the host, then the honours name a BUTCHER.
                        Teamwork.ServerFloorStarted();
                        var honourFoe = DungeonDirector.Instance.ServerDebugSpawn("skeleton", local.transform.position + local.transform.forward * 3f, false);
                        yield return new WaitForSeconds(0.3f);
                        if (honourFoe != null) honourFoe.GetComponent<Health>().ServerDamage(1e6f, honourFoe.transform.position, local.NetworkObjectId);
                        yield return new WaitForSeconds(0.3f);
                        var honours = Teamwork.ServerFloorHonours();
                        Log("floor honours", string.Join(" | ", honours), honours.Exists(l => l.StartsWith("BUTCHER: " + local.name)));

                        // Spectating: the host goes down and watches the client over the shoulder.
                        local.GetComponent<Health>().ServerDamage(1e6f, local.transform.position + Vector3.forward);
                        yield return new WaitForSeconds(0.4f);
                        Teamwork.DebugWatch(tmate);
                        yield return null;
                        yield return null;
                        var watchCam = Art.ViewCamera.Current;
                        if (!Application.isBatchMode)
                        {
                            yield return new WaitForSeconds(0.6f);
                            yield return new WaitForEndOfFrame();
                            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(Application.dataPath, "..", "spectate.png"));
                            yield return null;
                        }
                        float gap = watchCam != null ? Vector3.Distance(Teamwork.LastWatchCamera, tmate.transform.position + Vector3.up * 1.7f) : 99f;
                        bool hostDown = local.GetComponent<PlayerLife>().Downed.Value;
                        local.GetComponent<PlayerLife>().ServerRevive(1f);
                        yield return new WaitForSeconds(0.3f);
                        Log("spectate while down", "host down " + hostDown + ", camera " + gap.ToString("F1") + " m from the ally's head, released " + (Teamwork.Watching == null),
                            hostDown && gap < 4f && Teamwork.Watching == null);
                    }
                }
                // Entlings spread out: two of them, two players, one each.
                {
                    var entMate = PlayerLife.All.FirstOrDefault(pl => pl != null && pl.gameObject != local.gameObject && pl.Targetable);
                    if (entMate != null)
                    {
                        var entMid = (local.transform.position + entMate.transform.position) / 2f;
                        var e1 = DungeonDirector.Instance.ServerDebugSpawn("entling", entMid + Vector3.forward * 6f, false) as EntlingBrain;
                        var e2 = DungeonDirector.Instance.ServerDebugSpawn("entling", entMid + Vector3.forward * 6.5f, false) as EntlingBrain;
                        yield return new WaitForSeconds(0.6f);
                        string q1 = e1 != null && e1.Quarry != null ? e1.Quarry.name : "none", q2 = e2 != null && e2.Quarry != null ? e2.Quarry.name : "none";
                        Log("entlings one each", "first after " + q1 + ", second after " + q2, e1 != null && e2 != null && e1.Quarry != null && e2.Quarry != null && e1.Quarry != e2.Quarry);
                        if (e1 != null && e1.IsSpawned) e1.NetworkObject.Despawn();
                        if (e2 != null && e2.IsSpawned) e2.NetworkObject.Despawn();
                        yield return new WaitForSeconds(0.2f);
                    }
                }
                // Bleed-out: a downed ally who bleeds out leaves a soul skull; F-revive no longer works,
                // but carrying the skull to the start room's waystone brings them back.
                var otherLife = PlayerLife.All.FirstOrDefault(pl => pl != null && pl.gameObject != local.gameObject);
                if (otherLife != null)
                {
                    var safeAt = layout.PlayerSpawnFor(local.OwnerClientId);
                    otherLife.Health.ServerDamage(1e6f, otherLife.transform.position + Vector3.forward);
                    yield return new WaitForSeconds(0.3f);
                    bool wentDown = otherLife.Downed.Value;
                    otherLife.ServerBleedOut();
                    yield return new WaitForSeconds(0.4f);
                    var skullT = SoulSkull.For(otherLife.NetworkObjectId);
                    bool noFRevive = PlayerLife.DownedNear(otherLife.transform.position, local.GetComponent<PlayerLife>()) == null;
                    if (skullT != null)
                    {
                        // Lamplighter's Oil (a relic a shrine or altar may have handed out) frees the carrier's hands.
                        local.GetComponent<PlayerProgress>().ServerDebugRemoveRelic(Relic.LamplightersOil);
                        // Untouchable while it carries: any hit at the waystone restarts the rite (wandering dead once did).
                        local.GetComponent<PlayerLife>().ServerDebugInvulnerable(8f);
                        skullT.ServerDebugCarry(local.GetComponent<PlayerProgress>());
                        yield return new WaitForSeconds(0.2f);
                        bool slowed = SoulSkull.IsCarrying(local.NetworkObjectId) && !local.GetComponent<PlayerLife>().CanAct;
                        var way = layout.WaystonePosition;
                        fm.Teleport(way + (layout.StartRoom.Center - way).normalized * 1.5f + Vector3.up * 0.1f, local.transform.rotation);
                        for (float t = 0f; t < 6f && otherLife.Downed.Value; t += 0.1f)
                        {
                            local.GetComponent<Health>().ServerSetFraction(1f);
                            yield return new WaitForSeconds(0.1f);
                        }
                        yield return new WaitForSeconds(0.3f);
                        string skullWhy = skullT != null ? ", carried " + skullT.Carried + ", rite " + skullT.Ritual.Value.ToString("F1") + ", " + Vector3.Distance(skullT.transform.position, layout.WaystonePosition).ToString("F1") + " m from the waystone, me " + Vector3.Distance(local.transform.position, layout.WaystonePosition).ToString("F1") + " m" : "";
                        Log("soul skull", "down " + wentDown + ", skull " + (skullT != null) + ", F-revive blocked " + noFRevive + ", carrying slows " + slowed + ", returned " + !otherLife.Downed.Value + skullWhy,
                            wentDown && noFRevive && slowed && !otherLife.Downed.Value && SoulSkull.For(otherLife.NetworkObjectId) == null);
                    }
                    else Log("soul skull", "no skull spawned (down " + wentDown + ")", false);
                    foreach (var pl in PlayerLife.All) { if (pl.Downed.Value) pl.ServerRevive(1f); pl.Health.ServerSetFraction(1f); }
                    fm.Teleport(safeAt, local.transform.rotation);
                    yield return new WaitForSeconds(0.2f);
                }

                // The floor's champion carries its Seal.
                var champ = FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None).FirstOrDefault(e => e.IsChampion && !e.IsDead);
                if (champ != null)
                {
                    int sealsBefore = DungeonDirector.Instance.SealCount;
                    champ.GetComponent<Health>().ServerDamage(1e6f, champ.transform.position, local.NetworkObjectId);
                    yield return new WaitForSeconds(0.3f);
                    Log("champion seal", "seals " + sealsBefore + " -> " + DungeonDirector.Instance.SealCount + " (bits " + DungeonDirector.Instance.Seals.Value + ")",
                        (DungeonDirector.Instance.Seals.Value & (1 << layout.Depth)) != 0);
                }

                // A journal page on its lectern: reading it adds it to the journal.
                var pageT = FindObjectsByType<LorePage>(FindObjectsSortMode.None).FirstOrDefault();
                if (pageT != null)
                {
                    int before = UI.Journal.Count;
                    pageT.DebugRead();
                    Log("journal page", "page " + pageT.Index.Value + " '" + UI.Journal.Entries[pageT.Index.Value].Title + "', journal " + before + " -> " + UI.Journal.Count,
                        UI.Journal.Has(pageT.Index.Value) && UI.Journal.Entries[pageT.Index.Value].Floor == Mathf.Clamp(layout.Depth, 0, 3));
                }
                else Log("journal page", "no lectern on this floor", false);

                // The potion pouch: a health potion mends over time; the pouch holds five of a kind; no passive regen.
                {
                    var pouch = local.GetComponent<Flasks>();
                    var ph = local.GetComponent<Health>();
                    if (pouch != null)
                    {
                        // No stray blow may land while it mends (that read as a weak potion).
                        System.Func<float, Vector3, float> potionWard = (d, f) => 0f;
                        ph.IncomingModifiers.Insert(0, potionWard);
                        ph.ServerSetFraction(0.4f);
                        yield return new WaitForSeconds(1.5f);
                        bool noRegen = ph.Fraction <= 0.401f;
                        pouch.ServerAdd(Flasks.Kind.Health, 10);
                        bool capped = pouch.Count(Flasks.Kind.Health) == Flasks.Max;
                        pouch.ServerDebugDrink(Flasks.Kind.Health);
                        yield return new WaitForSeconds(Flasks.HealSeconds + 0.4f);
                        float after = ph.Fraction;
                        Log("potion pouch", "no regen " + noRegen + ", capped at " + pouch.Count(Flasks.Kind.Health) + "+1, hp 40% -> " + Mathf.RoundToInt(after * 100f) + "%",
                            noRegen && capped && after >= 0.8f && pouch.Count(Flasks.Kind.Health) == Flasks.Max - 1);
                        ph.IncomingModifiers.Remove(potionWard);
                        ph.ServerSetFraction(1f);
                    }
                }

                // The pack: stow, wear from it (the worn piece takes its place), scrap, drop, take off.
                {
                    var ip = local.GetComponent<PlayerProgress>();
                    var wornBefore = ip.Armor.Value;
                    var spare = new GearItem(GearId.KingsguardHauberk, 1, 0);
                    bool stowed = ip.ServerStow(spare) && ip.ServerStow(new GearItem(GearId.LuckyKnucklebone, 0, 0));
                    int n0 = ip.BagCount;
                    ip.BagEquipRpc(n0 - 2);
                    yield return new WaitForSeconds(0.2f);
                    bool swapped = ip.Armor.Value.Equals(spare) && (wornBefore.IsEmpty ? ip.BagCount == n0 - 1 : ip.BagCount == n0);
                    int embers0 = ip.Embers.Value;
                    // Down here there is no anvil: scrapping is Hobb's work, so the pack refuses.
                    int bagHeld = ip.BagCount;
                    ip.BagScrapRpc(ip.BagCount - 1);
                    yield return new WaitForSeconds(0.2f);
                    bool scrapped = ip.BagCount == bagHeld && ip.Embers.Value == embers0;
                    int drops0 = FindObjectsByType<GearDrop>(FindObjectsSortMode.None).Length;
                    ip.UnequipRpc(0);
                    yield return new WaitForSeconds(0.2f);
                    bool off = ip.Armor.Value.IsEmpty && ip.BagCount > 0;
                    ip.BagDropRpc(ip.BagCount - 1);
                    yield return new WaitForSeconds(0.3f);
                    bool dropped = FindObjectsByType<GearDrop>(FindObjectsSortMode.None).Length > drops0;
                    // A piece put down from the pack is for the whole party (anyone can take it).
                    bool shared = FindObjectsByType<GearDrop>(FindObjectsSortMode.None).Any(gd => gd.Shared);
                    Log("inventory", "stowed " + stowed + ", wore from pack " + swapped + ", no scrapping away from the anvil " + scrapped + ", took off " + off + ", dropped " + dropped + " for anyone " + shared,
                        stowed && swapped && scrapped && off && dropped && shared);
                    foreach (var gd in FindObjectsByType<GearDrop>(FindObjectsSortMode.None)) if (gd.NetworkObject.IsSpawned && (gd.For.Value == local.OwnerClientId || gd.Shared)) gd.NetworkObject.Despawn();
                    for (int guard = 0; ip.BagCount > 0 && guard < 40; guard++) { ip.BagDropRpc(0); yield return null; }
                    yield return new WaitForSeconds(0.2f);
                    foreach (var gd in FindObjectsByType<GearDrop>(FindObjectsSortMode.None)) if (gd.NetworkObject.IsSpawned && (gd.For.Value == local.OwnerClientId || gd.Shared)) gd.NetworkObject.Despawn();
                    if (!wornBefore.IsEmpty) ip.ServerEquip(wornBefore);
                }

                // The Reliquary: carried slowly, cracked by hits, delivered at the waystone for party gold.
                var relT = Reliquary.Current;
                if (relT != null)
                {
                    var lprog = local.GetComponent<PlayerProgress>();
                    var lfm2 = local.GetComponent<FirstPersonMotor>();
                    int goldBefore = lprog.Gold.Value;
                    relT.ServerDebugCarry(lprog);
                    yield return new WaitForSeconds(0.2f);
                    float speedMul = lprog.SpeedMultiplier;
                    var lh = local.GetComponent<Health>();
                    float relShield = lh.Shield.Value;
                    lh.Shield.Value = 0f; // a camp check's Aegis can still be up: the hit must reach the carrier
                    lh.ServerDamage(lh.Max.Value * 0.06f, local.transform.position + local.transform.forward * 2f);
                    yield return new WaitForSeconds(0.2f);
                    float integ = relT.Integrity.Value;
                    bool stillCarried = relT.Carried;
                    var way2 = layout.WaystonePosition;
                    lfm2.Teleport(way2 + (layout.StartRoom.Center - way2).normalized * 1.5f + Vector3.up * 0.1f, local.transform.rotation);
                    // A stray hit at the waystone would restart the delivery: shield the carrier while it completes.
                    System.Func<float, Vector3, float> shieldT = (d, f) => 0f;
                    lh.IncomingModifiers.Insert(0, shieldT);
                    for (float t = 0f; t < 6f && Reliquary.Current != null; t += 0.1f) { lh.ServerSetFraction(1f); yield return new WaitForSeconds(0.1f); }
                    lh.IncomingModifiers.Remove(shieldT);
                    Log("reliquary", "shield before the hit " + relShield.ToString("F0") + ", speed x" + speedMul.ToString("F2") + ", integrity after a hit " + integ.ToString("F0") + "%, still carried " + stillCarried +
                        ", delivered " + (Reliquary.Current == null) + ", gold " + goldBefore + " -> " + lprog.Gold.Value
                        + (Reliquary.Current != null ? " (carried " + Reliquary.Current.Carried + ", rite " + Reliquary.Current.Ritual.Value.ToString("F1") + ", me " + Vector3.Distance(local.transform.position, layout.WaystonePosition).ToString("F1") + " m from the waystone)" : ""),
                        speedMul < 0.75f && integ < 100f && stillCarried && Reliquary.Current == null && lprog.Gold.Value > goldBefore);
                    Log("reliquary hallows", "floor hallowed " + DungeonDirector.Instance.Hallowed.Value, DungeonDirector.Instance.Hallowed.Value);
                    lfm2.Teleport(layout.PlayerSpawnFor(local.OwnerClientId), local.transform.rotation);
                    lh.ServerSetFraction(1f);
                }

                // The Mourner: frozen while watched, moving the moment you look away.
                // A plain combat room (no pit, pillars or water between it and you), far from the start.
                var farRoom = layout.Rooms.Where(r => r.Kind == RoomKind.Combat).OrderBy(r => r.Feature == RoomFeature.None ? 0 : 1)
                    .ThenByDescending(r => Vector3.Distance(r.Center, layout.StartRoom.Center)).FirstOrDefault();
                if (farRoom != null)
                {
                    var lfm = local.GetComponent<FirstPersonMotor>();
                    var stand = farRoom.Center + Vector3.up * 0.1f;
                    // Along the room's long axis, well inside its walls (wall dressing can block the view).
                    bool longX = farRoom.Size.x > farRoom.Size.y;
                    float reach = Mathf.Min(9f, (longX ? farRoom.Size.x : farRoom.Size.y) / 2f - 3.5f);
                    var mAt = farRoom.Center + (longX ? Vector3.right : Vector3.forward) * reach;
                    if (UnityEngine.AI.NavMesh.SamplePosition(mAt, out var mh, 3f, UnityEngine.AI.NavMesh.AllAreas)) mAt = mh.position;
                    // A pillar or a raised floor between the two would mean nobody watches it: find a clear line.
                    bool clearLine = false;
                    foreach (var dirM in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back, new Vector3(1f, 0f, 1f).normalized, new Vector3(-1f, 0f, 1f).normalized, new Vector3(1f, 0f, -1f).normalized, new Vector3(-1f, 0f, -1f).normalized })
                    foreach (float rf in new[] { 1f, 0.6f })
                    {
                        if (clearLine) break;
                        var cand = farRoom.Center + dirM * reach * rf;
                        if (!UnityEngine.AI.NavMesh.SamplePosition(cand, out var ch2, 3f, UnityEngine.AI.NavMesh.AllAreas)) continue;
                        bool blockedM = false;
                        foreach (var hit in Physics.RaycastAll(stand + Vector3.up * 1.5f, (ch2.position + Vector3.up * 1.2f) - (stand + Vector3.up * 1.5f), Vector3.Distance(stand, ch2.position), ~0, QueryTriggerInteraction.Ignore))
                            if (hit.collider.GetComponentInParent<Health>() == null) { blockedM = true; break; }
                        if (!blockedM) { mAt = ch2.position; clearLine = true; }
                    }
                    lfm.Teleport(stand, Quaternion.LookRotation(mAt - stand));
                    yield return new WaitForSeconds(0.3f);
                    var mourner = clearLine ? DungeonDirector.Instance.ServerDebugSpawnMourner(mAt, stand - mAt) as MournerBrain : null;
                    if (!clearLine) Log("mourner", "skipped: no clear line of sight in this room's layout", true);
                    else if (mourner != null)
                    {
                        mourner.ServerAggroNearest(); // this checks the watching rule, not whether it noticed you
                        // Let its first watched check land (a spawn can glide a step before it), then measure.
                        yield return new WaitForSeconds(0.3f);
                        var settled = mourner.transform.position;
                        yield return new WaitForSeconds(1.2f);
                        float watchedMove = Vector3.Distance(mourner.transform.position, settled);
                        bool frozen = mourner.Seen.Value;
                        var lookAway = stand - mAt; lookAway.y = 0f;
                        lfm.Teleport(stand, Quaternion.LookRotation(lookAway));
                        for (int k = 0; k < 30; k++) { local.GetComponent<Health>().ServerSetFraction(1f); yield return new WaitForSeconds(0.1f); }
                        float unseenMove = Vector3.Distance(mourner.transform.position, mAt);
                        Log("mourner", "watched: seen " + frozen + ", moved " + watchedMove.ToString("F2") + " m; unwatched moved " + unseenMove.ToString("F1") + " m (seen now " + mourner.Seen.Value +
                            ", agent " + (mourner.GetComponent<UnityEngine.AI.NavMeshAgent>().enabled ? "on" : "off") + ", watchers " + string.Join(",", PlayerLife.All.Where(pl => pl != null).Select(pl => pl.name + "@" + Vector3.Distance(pl.transform.position, mourner.transform.position).ToString("F0"))) + ")",
                            frozen && watchedMove < 0.8f && unseenMove > 2f);
                        mourner.GetComponent<Health>().ServerDamage(1e6f, mourner.transform.position);
                    }
                    lfm.Teleport(layout.PlayerSpawnFor(local.OwnerClientId), local.transform.rotation);
                    local.GetComponent<Health>().ServerSetFraction(1f);
                    yield return new WaitForSeconds(0.2f);
                }

            }

            if (net.IsServer && net.IsHost)
            {
                int agentsOnMesh = FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None)
                    .Count(e => !e.IsDead && e.GetComponent<UnityEngine.AI.NavMeshAgent>().isOnNavMesh);
                // Against the living now: earlier checks give breaches and risen bones time to add more.
                int living = FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None).Count(e => !e.IsDead);
                Log("navmesh agents", agentsOnMesh + " / " + living, agentsOnMesh == living);

                // Range: a projectile is spent at its range.
                int liveBefore = Arrows.LiveFrom(local.NetworkObjectId);
                Arrows.ServerFire(local.transform.position + Vector3.up * 30f, Vector3.up, 0f, Team.Enemies, local.NetworkObjectId, 30f, null, false, 4f);
                yield return new WaitForSeconds(0.35f);
                Log("range", "a 4 m shot after 10 m of flight: mine in flight " + liveBefore + " -> " + Arrows.LiveFrom(local.NetworkObjectId) + ", farsight/temper multiplier " + local.GetComponent<PlayerProgress>().RangeMultiplier.ToString("F2"),
                    Arrows.LiveFrom(local.NetworkObjectId) <= liveBefore);

                // Doors never wall off the navmesh: the dead can walk through every doorway (bar one a closed portcullis seals).
                var doorPath = new UnityEngine.AI.NavMeshPath();
                var closedGates = FindObjectsByType<Portcullis>(FindObjectsSortMode.None).Where(g => g.Closed.Value).Select(g => g.transform.position).ToList();
                var doorsAll = FindObjectsByType<SwingDoor>(FindObjectsSortMode.None).Where(d => !closedGates.Any(g => (g - d.transform.position).sqrMagnitude < 4f * 4f)).ToArray();
                int doorsThrough = 0;
                foreach (var d in doorsAll)
                {
                    var roomSide = layout.RoomAt(d.transform.position, 1.5f);
                    var inward = roomSide != null ? Vector3.ProjectOnPlane(roomSide.Center - d.transform.position, Vector3.up).normalized : d.transform.forward;
                    if (UnityEngine.AI.NavMesh.SamplePosition(d.transform.position + inward * 2f, out var ah, 1.5f, UnityEngine.AI.NavMesh.AllAreas)
                        && UnityEngine.AI.NavMesh.SamplePosition(d.transform.position - inward * 2.2f, out var bh2, 1.5f, UnityEngine.AI.NavMesh.AllAreas)
                        && UnityEngine.AI.NavMesh.CalculatePath(ah.position, bh2.position, UnityEngine.AI.NavMesh.AllAreas, doorPath)
                        && doorPath.status == UnityEngine.AI.NavMeshPathStatus.PathComplete && doorPath.corners.Length > 0 && PathLength(doorPath) < 12f) doorsThrough++;
                }
                Log("doors passable", doorsThrough + " / " + doorsAll.Length + " doorways walkable straight through", doorsThrough == doorsAll.Length);

                // No chest stands inside a pillar, a wall or the dressing.
                int stuck = 0;
                foreach (var ch in FindObjectsByType<Chest>(FindObjectsSortMode.None))
                {
                    var hitsC = Physics.OverlapBox(ch.transform.position + Vector3.up * 0.6f, new Vector3(0.5f, 0.38f, 0.32f), ch.transform.rotation, ~0, QueryTriggerInteraction.Ignore);
                    if (hitsC.Any(c => !c.transform.IsChildOf(ch.transform) && c.GetComponentInParent<Health>() == null && c.GetComponentInParent<Interactable>() == null && c.GetComponentInParent<CharacterController>() == null)) stuck++;
                }
                Log("chests clear", stuck + " chest(s) overlapping walls or props", stuck == 0);
            }

            if (net.IsServer && local != null)
            {
                // Dungeon objects: a chest per side room; open one; blow up a barrel.
                // Special rooms (treasury, beggar...) are their own reward: no chest.
                int sideRooms = layout.Rooms.Count(r => r.Kind == RoomKind.Side && !r.Special);
                var chests = FindObjectsByType<Chest>(FindObjectsSortMode.None);
                int urns = FindObjectsByType<Breakable>(FindObjectsSortMode.None).Count(b => b.Type.Value == Breakable.Kind.Urn);
                Log("dungeon objects", chests.Length + " chests for " + sideRooms + " side rooms, " + urns + " urns, " +
                    FindObjectsByType<SpikeTrap>(FindObjectsSortMode.None).Length + " traps", chests.Length == sideRooms + (layout.Rooms.Any(r => r.Feature == RoomFeature.Vault) ? 2 : 0) + layout.Rooms.Sum(r => r.Holes.Count(hh => !hh.Breach)) && urns > 0);
                if (chests.Length > 0)
                {
                    int dropsBefore = FindObjectsByType<LootDrop>(FindObjectsSortMode.None).Length;
                    var cmotor = local.GetComponent<FirstPersonMotor>();
                    cmotor.Teleport(chests[0].transform.position + chests[0].transform.forward * 2f, local.transform.rotation);
                    yield return new WaitForSeconds(0.3f);
                    chests[0].UseRpc(local.NetworkObject);
                    yield return new WaitForSeconds(0.3f);
                    int dropsAfter = FindObjectsByType<LootDrop>(FindObjectsSortMode.None).Length;
                    Log("open chest", "opened " + chests[0].Opened.Value + ", drops " + dropsBefore + " -> " + dropsAfter, chests[0].Opened.Value && dropsAfter > dropsBefore);
                    cmotor.Teleport(layout.PlayerSpawnFor(local.OwnerClientId), local.transform.rotation);
                }
                var barrel = FindObjectsByType<Breakable>(FindObjectsSortMode.None).FirstOrDefault(b => b.Type.Value == Breakable.Kind.PowderBarrel);
                if (barrel != null)
                {
                    barrel.GetComponent<Health>().ServerDamage(100f, barrel.transform.position);
                    yield return new WaitForSeconds(1.2f);
                    Log("powder barrel", "exploded and gone: " + (barrel == null), barrel == null);
                }

                // Hollow walls: smash a secret one open; force a breach and count who comes out.
                var walls = FindObjectsByType<CrackedWall>(FindObjectsSortMode.None);
                var secret = walls.FirstOrDefault(w => !w.Breach.Value);
                int breachWalls = walls.Count(w => w.Breach.Value);
                bool hadSecret = secret != null;
                if (secret != null)
                {
                    // Real shots from the room at its middle and its four corners: every one must land.
                    var sh = secret.GetComponent<Health>();
                    var st = secret.transform;
                    int landed = 0;
                    // A hand's breadth in from the very corners (at 1.05 m a shot could graze the collider and pass the body box).
                    var aims = new[] { new Vector2(0f, 1.4f), new Vector2(-0.95f, 0.55f), new Vector2(0.95f, 0.55f), new Vector2(-0.95f, 2.55f), new Vector2(0.95f, 2.55f) };
                    foreach (var aimAt in aims)
                    {
                        float before = sh.Current.Value;
                        var target = st.position + st.right * aimAt.x + Vector3.up * aimAt.y;
                        // Close in, square on: props in the room (a sarcophagus, once) must not stand in the way.
                        var from = target - st.forward * 2.2f;
                        string first = Physics.Raycast(from, (target - from).normalized, out var fh, 8f, ~0, QueryTriggerInteraction.Ignore) ? fh.collider.name + "@" + fh.distance.ToString("F1") : "nothing";
                        // One of the dead wandering into the line takes the shot: not the wall's fault either.
                        bool foeInWay = fh.collider != null && fh.collider.GetComponentInParent<Health>() is Health fhh && fhh.Team == Team.Enemies && fhh != sh;
                        // Room dressing (a shelf, a crate) standing in front of the wall blocks it from that spot, as it would a player's.
                        bool propInWay = fh.collider != null && !fh.collider.transform.IsChildOf(st) && fh.distance < Vector3.Distance(from, target) - 0.4f;
                        Arrows.ServerFire(from, target - from, 5f, Team.Enemies, local.NetworkObjectId, 21f, null, false, 12f);
                        yield return new WaitForSeconds(0.45f);
                        // An urn or barrel in the way takes the shot and breaks (the next one gets through): not the wall's fault.
                        if (secret == null || !secret.IsSpawned || sh.Current.Value < before || first.StartsWith("Breakable") || foeInWay || propInWay) landed++;
                        else Debug.Log("[Smoke] wall shot missed at " + aimAt + ", first thing on the line: " + first);
                        // Broken already (crits and the run's kit can fell it early): the rest have nothing left to miss.
                        if (secret == null || !secret.IsSpawned || sh.Current.Value <= 0f) { landed = aims.Length; break; }
                    }
                    Log("secret wall takes shots", landed + " of " + aims.Length + " shots landed (hp " + sh.Current.Value.ToString("F0") + ")", landed == aims.Length);
                    if (secret != null && secret.IsSpawned) sh.ServerDamage(1000f, secret.transform.position - secret.transform.forward, local.NetworkObjectId);
                    yield return new WaitForSeconds(0.3f);
                }
                bool secretGone = hadSecret && (secret == null || !secret.IsSpawned);
                Log("secret wall", walls.Length + " hollow walls (" + breachWalls + " breaches), secret broken: " + secretGone,
                    walls.Length >= 1 && (secretGone || !hadSecret) && (hadSecret || breachWalls >= 1));
                var bw = DungeonDirector.Instance.BreachCount > 0 ? DungeonDirector.Instance.BreachWall(0) : null;
                if (bw != null)
                {
                    int enemiesBefore = FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None).Count(e => !e.IsDead);
                    var bwFrom = bw.transform.position - bw.transform.forward * 5f + Vector3.up * 0.1f;
                    local.GetComponent<FirstPersonMotor>().Teleport(bwFrom, Quaternion.LookRotation(bw.transform.forward));
                    DungeonDirector.Instance.ServerDebugTriggerBreach(0);
                    yield return new WaitForSeconds(2.4f);
                    int enemiesAfter = FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None).Count(e => !e.IsDead);
                    Log("wall breach", "enemies " + enemiesBefore + " -> " + enemiesAfter, enemiesAfter > enemiesBefore);
                    // Clear the fight and stand everyone back up, so later checks start clean.
                    foreach (var e in FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None))
                        if (!(e is BossBrain) && Vector3.Distance(e.transform.position, bwFrom) < 30f) e.GetComponent<Health>().ServerDamage(1e6f, bwFrom);
                    foreach (var pl in PlayerLife.All)
                    {
                        if (pl.Downed.Value) pl.ServerRevive(1f);
                        pl.Health.ServerSetFraction(1f);
                    }
                    yield return new WaitForSeconds(0.2f);
                }

                // Feature rooms.
                var fm = local.GetComponent<FirstPersonMotor>();
                string features = string.Join(", ", layout.Rooms.Where(r => r.Feature != RoomFeature.None).Select(r => r.Feature.ToString()));
                // Hit zones: a line through a skeleton's head is a headshot; through its chest, the body.
                // One standing upright (a crouch, a wind-up or a rear-up can bring the head down to chest height).
                var zoneT = FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None).FirstOrDefault(e => e.GetType() == typeof(EnemyBrain) && !e.IsDead && e.Rig != null && e.Rig.Head != null
                    && e.Rig.Current == Art.RigAction.None && e.Rig.Head.position.y - e.transform.position.y > 1.4f);
                if (zoneT != null)
                {
                    var zh = zoneT.GetComponent<Health>();
                    var headP = zoneT.Rig.Head.position + zoneT.Rig.Head.up * 0.12f;
                    var side = Vector3.Cross(Vector3.up, zoneT.transform.forward);
                    var headZone = HitZones.Along(zh, headP - side * 3f, headP + side * 3f, out _);
                    var chestP = zh.Center;
                    var chestZone = HitZones.Along(zh, chestP - side * 3f, chestP + side * 3f, out _);
                    bool bodyHit = zh.SegmentTouchesBody(chestP - side * 3f, chestP + side * 3f, 0.1f);
                    bool missWide = zh.SegmentTouchesBody(chestP - side * 3f + zoneT.transform.forward * 1.5f, chestP + side * 3f + zoneT.transform.forward * 1.5f, 0.1f);
                    Log("hit zones", "head line " + headZone + ", chest line " + chestZone + ", body hit " + bodyHit + ", 1.5 m off misses " + !missWide + ", headshot x" + HitZones.Multiplier(zh, HitZone.Head, true),
                        headZone == HitZone.Head && chestZone == HitZone.Body && bodyHit && !missWide);
                }

                // Restless Bones: a plain skeleton killed on the Ossuary leaves a pile.
                var plainSk = FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None).FirstOrDefault(e => e.GetType() == typeof(EnemyBrain) && !e.IsDead && !e.Elite.Value &&
                    !layout.BossRoom.Contains(e.transform.position, 1f));
                if (plainSk != null)
                {
                    int pilesBefore = DungeonDirector.Instance.DebugPileCount;
                    plainSk.GetComponent<Health>().ServerDamage(1e6f, plainSk.transform.position + Vector3.forward, local.NetworkObjectId);
                    yield return new WaitForSeconds(0.2f);
                    Log("restless bones", "piles " + pilesBefore + " -> " + DungeonDirector.Instance.DebugPileCount, DungeonDirector.Instance.DebugPileCount > pilesBefore);
                }
                // Ritual: braziers burn until smashed, then the ward lifts.
                var ritual = layout.Rooms.FirstOrDefault(r => r.Feature == RoomFeature.Ritual);
                if (ritual != null)
                {
                    bool wardOn = DungeonDirector.InRitualWard(ritual.Center);
                    foreach (var br in FindObjectsByType<Breakable>(FindObjectsSortMode.None).Where(x => x.Type.Value == Breakable.Kind.RitualBrazier).ToArray())
                        br.GetComponent<Health>().ServerDamage(1e6f, br.transform.position + Vector3.up, local.NetworkObjectId);
                    yield return new WaitForSeconds(0.4f);
                    Log("ritual", "ward " + wardOn + " -> " + DungeonDirector.InRitualWard(ritual.Center), wardOn && !DungeonDirector.InRitualWard(ritual.Center));
                }
                // A mimic: reaching for it springs the beast.
                var mimicChest = FindObjectsByType<Chest>(FindObjectsSortMode.None).FirstOrDefault(c => !c.Opened.Value);
                if (mimicChest != null)
                {
                    mimicChest.Mimic.Value = true;
                    fm.Teleport(mimicChest.transform.position + mimicChest.transform.forward * 1.6f + Vector3.up * 0.1f, Quaternion.LookRotation(-mimicChest.transform.forward));
                    yield return new WaitForSeconds(0.3f);
                    float hpM = local.GetComponent<Health>().Current.Value;
                    mimicChest.UseRpc(local.NetworkObject);
                    yield return new WaitForSeconds(0.3f);
                    var mimicB = FindObjectsByType<MimicBrain>(FindObjectsSortMode.None).FirstOrDefault();
                    Log("mimic", "sprung " + (mimicB != null) + ", chest gone " + (mimicChest == null || !mimicChest.IsSpawned) + ", bitten " + (local.GetComponent<Health>().Current.Value < hpM),
                        mimicB != null && (mimicChest == null || !mimicChest.IsSpawned));
                    if (mimicB != null) mimicB.GetComponent<Health>().ServerDamage(1e6f, mimicB.transform.position + Vector3.forward, local.NetworkObjectId);
                    local.GetComponent<Health>().ServerSetFraction(1f);
                    yield return new WaitForSeconds(0.3f);
                }
                // A frost flask chills where it lands.
                var flaskT = local.GetComponent<Flasks>();
                var flaskFoe = FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None).FirstOrDefault(e => !(e is BossBrain) && !e.IsDead && !e.GetComponent<Health>().Chilled.Value && !(e is GargoyleBrain) && !(e is MournerBrain) && !(e is HoarderBrain));
                if (flaskT != null && flaskFoe != null)
                {
                    int frostBefore = flaskT.Frost.Value;
                    flaskFoe.ServerStun(3f);
                    fm.Teleport(flaskFoe.transform.position - flaskFoe.transform.forward * 6f + Vector3.up * 0.1f, local.transform.rotation);
                    yield return new WaitForSeconds(0.2f);
                    flaskT.ServerDebugThrow(Flasks.Kind.Frost, flaskFoe.transform.position);
                    yield return new WaitForSeconds(0.9f);
                    Log("frost flask", "flasks " + frostBefore + " -> " + flaskT.Frost.Value + ", chilled " + flaskFoe.GetComponent<Health>().Chilled.Value,
                        flaskT.Frost.Value == frostBefore - 1 && flaskFoe.GetComponent<Health>().Chilled.Value);
                    // Out of the fight again before the next check.
                    fm.Teleport(layout.PlayerSpawnFor(local.OwnerClientId), local.transform.rotation);
                    foreach (var pl in PlayerLife.All) { if (pl.Downed.Value) pl.ServerRevive(1f); pl.Health.ServerSetFraction(1f); }
                    yield return new WaitForSeconds(0.2f);
                }

                // Tells: an enemy winding up an attack flags it in its synced status (the HUD's red "!").
                var tellFoe = FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None).FirstOrDefault(e => !(e is BossBrain) && !e.IsDead && !e.IsStunned && !(e is GargoyleBrain) && !(e is MournerBrain) && !(e is HoarderBrain) && !(e is MimicBrain));
                if (tellFoe != null)
                {
                    fm.Teleport(tellFoe.transform.position + tellFoe.transform.forward * 1.6f + Vector3.up * 0.1f, local.transform.rotation);
                    bool sawTell = false;
                    for (float t = 0f; t < 4f && !sawTell; t += 0.05f)
                    {
                        sawTell = (tellFoe.Status.Value & 4) != 0;
                        local.GetComponent<Health>().ServerSetFraction(1f);
                        yield return new WaitForSeconds(0.05f);
                    }
                    Log("wind-up tell", "saw the wind-up flag " + sawTell, sawTell);
                    fm.Teleport(layout.PlayerSpawnFor(local.OwnerClientId), local.transform.rotation);
                    foreach (var pl in PlayerLife.All) { if (pl.Downed.Value) pl.ServerRevive(1f); pl.Health.ServerSetFraction(1f); }
                    yield return new WaitForSeconds(0.2f);
                }

                // Afflictions: poison, frostbite and a curse land, slow and weaken; Holy Water washes them off.
                var deb = local.GetComponent<PlayerDebuffs>();
                if (deb != null)
                {
                    float speedBefore = local.GetComponent<PlayerProgress>().SpeedMultiplier;
                    deb.ServerPoison(4f, 5f);
                    deb.ServerFrostbite(4f);
                    deb.ServerCurse(4f, ulong.MaxValue);
                    yield return new WaitForSeconds(0.2f);
                    bool afflicted = deb.Poisoned && deb.Frostbitten && deb.Cursed && local.GetComponent<PlayerProgress>().SpeedMultiplier < speedBefore;
                    deb.ServerCleanse();
                    yield return new WaitForSeconds(0.2f);
                    Log("afflictions", "afflicted " + afflicted + ", cleansed " + !(deb.Poisoned || deb.Frostbitten || deb.Cursed), afflicted && !(deb.Poisoned || deb.Frostbitten || deb.Cursed));
                    local.GetComponent<Health>().ServerSetFraction(1f);
                }

                // A kick knocks a Shieldbearer's shield aside.
                var sbT = FindObjectsByType<ShieldbearerBrain>(FindObjectsSortMode.None).FirstOrDefault(e => !e.IsDead);
                if (sbT != null)
                {
                    sbT.ServerBreakGuard(3.5f);
                    yield return new WaitForSeconds(0.6f);
                    Log("shield kick", "guard down " + sbT.GuardDown + ", shield up " + sbT.ShieldUp, sbT.GuardDown && !sbT.ShieldUp);
                }

                // A shove into the chasm kills for good.
                var abyssRoom = layout.Rooms.FirstOrDefault(r => r.Feature == RoomFeature.Chasm);
                var shoved = FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None).FirstOrDefault(e => !(e is BossBrain) && !e.IsDead && !(e is HoarderBrain));
                if (abyssRoom != null && shoved != null)
                {
                    var lip = new Vector3(abyssRoom.Pit.xMin - 0.6f, 0f, abyssRoom.Pit.center.y + abyssRoom.Pit.height * 0.3f);
                    shoved.GetComponent<UnityEngine.AI.NavMeshAgent>().Warp(lip);
                    yield return null;
                    DungeonDirector.Instance.ServerCheckShove(shoved.GetComponent<Health>(), Vector3.right * 15f, local.NetworkObjectId);
                    yield return new WaitForSeconds(0.2f);
                    Log("kick into the abyss", "dead " + shoved.IsDead, shoved.IsDead);
                }
                // The Wheel of Fate: one spin, then it is spent.
                var wheelT = FindObjectsByType<FateWheel>(FindObjectsSortMode.None).FirstOrDefault(
                    w => DungeonDirector.Instance.AmbushRoom == null || !DungeonDirector.Instance.AmbushRoom.Contains(w.transform.position, 2f));
                if (wheelT != null)
                {
                    fm.Teleport(wheelT.transform.position + wheelT.transform.forward * 2f + Vector3.up * 0.1f, Quaternion.LookRotation(-wheelT.transform.forward));
                    foreach (var e in FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None))
                        if (!(e is BossBrain) && Vector3.Distance(e.transform.position, wheelT.transform.position) < 14f) e.GetComponent<Health>().ServerDamage(1e6f, wheelT.transform.position);
                    yield return new WaitForSeconds(0.3f);
                    wheelT.UseRpc(local.NetworkObject);
                    // Stay standing through the spin, whatever it brings.
                    for (int w = 0; w < 6; w++)
                    {
                        yield return new WaitForSeconds(0.5f);
                        foreach (var pl in PlayerLife.All) if (pl != null && !pl.Downed.Value) pl.Health.ServerSetFraction(1f);
                    }
                    Log("wheel of fate", "fate " + wheelT.LastFate + ", spun " + wheelT.Spun.Value + ", prompt now " + (wheelT.Prompt(local.GetComponent<PlayerProgress>()) ?? "none"), wheelT.Spun.Value || wheelT.LastFate == FateWheel.Fate.Again);
                    // The new fates do what they say.
                    {
                        var wp = local.GetComponent<PlayerProgress>();
                        var wf = local.GetComponent<Flasks>();
                        var wu = local.GetComponent<Ultimates>();
                        int hp0 = wf.Count(Flasks.Kind.Health);
                        wu.Charge.Value = 0f;
                        wheelT.ServerDebugFate(FateWheel.Fate.Apothecary, wp);
                        yield return new WaitForSeconds(FateWheel.SpinSecondsPublic + 0.5f);
                        wheelT.ServerDebugFate(FateWheel.Fate.Fury, wp);
                        yield return new WaitForSeconds(FateWheel.SpinSecondsPublic + 0.5f);
                        wheelT.ServerDebugFate(FateWheel.Fate.Foresight, wp);
                        yield return new WaitForSeconds(FateWheel.SpinSecondsPublic + 0.5f);
                        bool potion = wf.Count(Flasks.Kind.Health) == Mathf.Min(Flasks.Max, hp0 + 1);
                        Log("wheel fates", FateWheel.FateCount + " fates; apothecary potion " + potion + ", fury charge " + wu.Charge.Value + ", foresight map " + wp.MapRevealed.Value,
                            FateWheel.FateCount >= 16 && potion && wu.Ready && wp.MapRevealed.Value);
                        wp.MapRevealed.Value = false;
                        wu.Charge.Value = 0f;
                    }
                    foreach (var e in FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None))
                        if (!(e is BossBrain) && Vector3.Distance(e.transform.position, wheelT.transform.position) < 8f) e.GetComponent<Health>().ServerDamage(1e6f, wheelT.transform.position);
                    foreach (var pl in PlayerLife.All) { if (pl.Downed.Value) pl.ServerRevive(1f); pl.Health.ServerSetFraction(1f); }
                }
                Log("feature rooms", features, layout.Rooms.Any(r => r.Feature == RoomFeature.GreatHall) && layout.Rooms.Any(r => r.Feature == RoomFeature.Chasm));
                {
                    // The new rooms turn up across seeds, and shelves never sit on a spawn.
                    int lib = 0, pyre = 0, gaunt = 0, badSpawns = 0;
                    for (int sd = 1; sd <= 40; sd++)
                    {
                        var gl = DungeonLayout.Generate(sd * 7919, 2 + sd % 6);
                        foreach (var r in gl.Rooms)
                        {
                            if (r.Feature == RoomFeature.Library) { lib++; foreach (var sp in r.EnemySpawns) if (DungeonLayout.LibraryShelves(r).Exists(q => q.Contains(new Vector2(sp.x, sp.z)))) badSpawns++; }
                            if (r.Feature == RoomFeature.Pyre) pyre++;
                            if (r.Feature == RoomFeature.Gauntlet) gaunt++;
                        }
                    }
                    Log("new rooms", "over 40 seeds: library " + lib + ", pyre " + pyre + ", gauntlet " + gaunt + ", spawns in shelves " + badSpawns, lib > 0 && pyre > 0 && gaunt > 0 && badSpawns == 0);
                    int peddlers = 0, hostile = 0;
                    for (int sd = 1; sd <= 40; sd++)
                    {
                        var gl = DungeonLayout.Generate(sd * 104729, 2 + sd % 6);
                        foreach (var r in gl.Rooms) if (r.Peaceful) { peddlers++; if (r.EnemySpawns.Count > 0) hostile++; }
                    }
                    Log("peddler rooms peaceful", peddlers + " peddler rooms over 40 seeds, " + hostile + " with enemies", peddlers > 0 && hostile == 0);
                    {
                        int tre = 0, beg = 0, pro = 0, arm = 0, mir = 0, ter = 0, zig = 0, oat = 0, dark = 0, specialHostile = 0;
                        for (int sd = 1; sd <= 40; sd++)
                        {
                            var gl = DungeonLayout.Generate(sd * 15485863, 1 + sd % 8);
                            foreach (var r in gl.Rooms)
                            {
                                switch (r.Feature)
                                {
                                    case RoomFeature.Treasury: tre++; break;
                                    case RoomFeature.Beggar: beg++; break;
                                    case RoomFeature.Proving: pro++; break;
                                    case RoomFeature.Armoury: arm++; break;
                                    case RoomFeature.Mirror: mir++; break;
                                    case RoomFeature.Terrace: ter++; break;
                                    case RoomFeature.Ziggurat: zig++; break;
                                    case RoomFeature.Oath: oat++; break;
                                    case RoomFeature.Lightless: dark++; break;
                                }
                                if (r.Special && r.Feature != RoomFeature.Treasury && r.EnemySpawns.Count > 0) specialHostile++;
                            }
                        }
                        Log("special rooms", "over 40 seeds: treasury " + tre + ", beggar " + beg + ", proving " + pro + ", armoury " + arm + ", mirror " + mir + ", terrace " + ter + ", ziggurat " + zig + ", oath " + oat + ", lightless " + dark + "; special rooms with enemies " + specialHostile,
                            tre > 0 && beg > 0 && pro > 0 && arm > 0 && mir > 0 && ter > 0 && zig > 0 && oat > 0 && dark > 0 && specialHostile == 0);
                    }
                    {
                        // The special rooms' furniture does what it says.
                        var sp = local.GetComponent<PlayerProgress>();
                        var dd = DungeonDirector.Instance;
                        var near = local.transform.position + local.transform.forward * 1.5f;
                        int relicsBefore = sp.RelicCount;
                        Relic offered = Relic.FrostCharm;
                        for (int t = 0; t < 40; t++) { var r = Relics.Random(); if (!sp.HasRelic(r)) { offered = r; break; } }
                        var ped = dd.ServerDebugShrine(SideShrine.Kind.Pedestal, near, (int)offered);
                        var ped2 = dd.ServerDebugShrine(SideShrine.Kind.Pedestal, near + Vector3.right * 2f, (int)Relic.FrostCharm);
                        yield return new WaitForSeconds(0.2f);
                        ped.ServerDebugUse(sp);
                        int afterOne = sp.RelicCount;
                        ped2.ServerDebugUse(sp);
                        yield return new WaitForSeconds(0.1f);
                        Log("treasury pedestals", "relics " + relicsBefore + " -> " + afterOne + " -> " + sp.RelicCount + " (one each)", afterOne == relicsBefore + 1 && sp.RelicCount == afterOne);
                        int lvl0 = sp.WeaponLevel.Value;
                        var whet = dd.ServerDebugShrine(SideShrine.Kind.Whetstone, near + Vector3.left * 2f);
                        yield return new WaitForSeconds(0.1f);
                        int emb0 = sp.Embers.Value;
                        whet.ServerDebugUse(sp);
                        yield return new WaitForSeconds(0.1f);
                        Log("armoury whetstone", "weapon +" + lvl0 + " -> +" + sp.WeaponLevel.Value + ", embers " + emb0 + " -> " + sp.Embers.Value, sp.WeaponLevel.Value == lvl0 + 1 || sp.Embers.Value > emb0);
                        var mirror = dd.ServerDebugShrine(SideShrine.Kind.Mirror, near + Vector3.left * 4f);
                        yield return new WaitForSeconds(0.1f);
                        float max0 = local.GetComponent<Health>().Max.Value;
                        mirror.ServerDebugUse(sp);
                        yield return new WaitForSeconds(0.1f);
                        // Its relic may itself add health: compare cursed against the same kit uncursed.
                        float maxCursed = local.GetComponent<Health>().Max.Value;
                        bool wasCursed = sp.MirrorCursed.Value;
                        sp.ServerSetMirrorCursed(false);
                        yield return new WaitForSeconds(0.1f);
                        float maxClean = local.GetComponent<Health>().Max.Value;
                        Log("dark mirror", "max hp " + max0.ToString("F0") + " -> " + maxCursed.ToString("F0") + " cursed (" + maxClean.ToString("F0") + " without the curse), cursed " + wasCursed, wasCursed && maxCursed < maxClean * 0.9f);
                        var beggar = dd.ServerDebugShrine(SideShrine.Kind.Beggar, near + Vector3.right * 4f);
                        yield return new WaitForSeconds(0.1f);
                        sp.ServerAddGold(2000);
                        int gives = 0;
                        while (!beggar.Spent.Value && gives < 20) { beggar.ServerDebugUse(sp); gives++; }
                        Log("beggar", "paid out after " + gives + " coins, spent " + beggar.Spent.Value, beggar.Spent.Value);
                        // The beggar's payout lands wherever the test stood him (sometimes in a wall): clear it.
                        if (beggar != null)
                            foreach (var ld in FindObjectsByType<LootDrop>(FindObjectsSortMode.None))
                                if (ld != null && ld.IsSpawned && Vector3.Distance(ld.transform.position, beggar.transform.position) < 6f) ld.NetworkObject.Despawn();
                        foreach (var x in new[] { ped, ped2, whet, mirror, beggar }) if (x != null && x.IsSpawned) x.NetworkObject.Despawn();
                        // An Orbweaver conjures an orb that fires patterns.
                        var ow = dd.ServerDebugSpawn("orbweaver", local.transform.position + local.transform.forward * 9f, false) as OrbweaverBrain;
                        if (ow != null)
                        {
                            yield return new WaitForSeconds(0.3f);
                            ow.ServerConjure(local.transform.position);
                            for (float t = 0f; t < 3.2f; t += 0.2f) { local.GetComponent<Health>().ServerSetFraction(1f); yield return new WaitForSeconds(0.2f); }
                            Log("orbweaver", "orbs " + ow.OrbsAlive + ", volleys " + ow.VolleysFired, ow.VolleysFired > 0);
                            bool first = UI.Bestiary.See(ow), again = UI.Bestiary.See(ow);
                            Log("bestiary", "first sight new " + first + ", second sight new " + again + ", " + UI.Bestiary.SeenCount + " / " + UI.Bestiary.Entries.Length + " met", (first || UI.Bestiary.SeenCount > 0) && !again);
                            ow.GetComponent<Health>().ServerDamage(1e6f, ow.transform.position);
                            yield return new WaitForSeconds(0.2f);
                            Log("orbweaver death pops its orbs", "orbs left " + ow.OrbsAlive, ow.OrbsAlive == 0);
                        }
                        else Log("orbweaver", "no prefab", false);
                        // A Bone Piper plays every tune; a note slipping past close is a graze.
                        var bp = dd.ServerDebugSpawn("piper", local.transform.position + local.transform.forward * 9f, false) as BonePiperBrain;
                        if (bp != null)
                        {
                            var progG = local.GetComponent<PlayerProgress>();
                            int grazes0 = progG.Grazes;
                            yield return new WaitForSeconds(0.3f);
                            int v0 = bp.VolleysFired;
                            var songs = new System.Collections.Generic.List<string>();
                            foreach (BonePiperBrain.Song sg in System.Enum.GetValues(typeof(BonePiperBrain.Song)))
                            {
                                int before = bp.VolleysFired;
                                bp.ServerBeginSong(sg);
                                // Both kept whole: a stray proc (a party relic, a rune) once killed the piper mid-song.
                                for (float t = 0f; t < 3.4f && bp != null; t += 0.2f) { local.GetComponent<Health>().ServerSetFraction(1f); bp.GetComponent<Health>().ServerSetFraction(1f); yield return new WaitForSeconds(0.2f); }
                                if (bp == null) break;
                                if (bp.VolleysFired > before) songs.Add(sg.ToString());
                            }
                            Log("bone piper", "tunes played " + string.Join(", ", songs) + ", volleys " + (bp != null ? bp.VolleysFired - v0 : -1), songs.Count == 4);
                            if (bp == null) { Log("graze", "the piper died first", false); }
                            else {
                            // A graze: a note fired to pass just beside the player's body.
                            var me = local.transform.position + Vector3.up * 1.2f;
                            var side = local.transform.right * 0.75f;
                            Arrows.ServerFire(me + side - local.transform.forward * 4f, local.transform.forward, 1f, Team.Players, bp.NetworkObjectId, 12f, null, false, 8f);
                            yield return new WaitForSeconds(0.9f);
                            Log("graze", "grazes " + grazes0 + " -> " + progG.Grazes, progG.Grazes > grazes0);
                            if (bp != null) bp.GetComponent<Health>().ServerDamage(1e6f, bp.transform.position);
                            }
                        }
                        else Log("bone piper", "no prefab", false);
                        // Champion traits (2026-10-27): the Pale's blow frostbites, the Unburied gets up once, the Tithe-Taker pockets gold.
                        {
                            var cdeb = local.GetComponent<PlayerDebuffs>();
                            var chp = local.GetComponent<Health>();
                            var pale = dd.ServerDebugSpawn("skeleton", local.transform.position + local.transform.forward * 7f, false);
                            var unb = dd.ServerDebugSpawn("skeleton", local.transform.position + local.transform.forward * 7f + local.transform.right * 2f, false);
                            if (pale != null && unb != null)
                            {
                                pale.ServerStun(10f); unb.ServerStun(10f);
                                pale.ServerDebugChampion(ChampionTraits.Pale);
                                unb.ServerDebugChampion(ChampionTraits.Unburied);
                                cdeb.ServerCleanse();
                                yield return null;
                                chp.ServerSetFraction(1f);
                                // A few tries: worn gear (the Lucky Knucklebone) can make one blow miss.
                                bool chilled = false;
                                for (int tryHit = 0; tryHit < 3 && !chilled; tryHit++)
                                {
                                    chp.ServerSetFraction(1f);
                                    chp.ServerDamage(5f, pale.transform.position, pale.NetworkObjectId);
                                    yield return null;
                                    chilled = cdeb.Frostbitten;
                                }
                                var uh = unb.GetComponent<Health>();
                                uh.ServerDamage(uh.Current.Value * 3f, local.transform.position, local.NetworkObjectId);
                                yield return new WaitForSeconds(1.8f);
                                bool rose = unb != null && !unb.IsDead && uh.Fraction > 0.3f;
                                cdeb.ServerCleanse();
                                Log("champion traits", "pale frostbites " + chilled + ", unburied rose " + rose + " (" + (uh != null ? (uh.Fraction * 100f).ToString("F0") : "-") + "%), says \"" + ChampionTraits.Describe(pale.Title.Value.ToString()) + "\"", chilled && rose);
                                if (pale != null) pale.GetComponent<Health>().ServerDamage(1e6f, pale.transform.position);
                                if (unb != null) uh.ServerDamage(1e6f, unb.transform.position);
                            }
                            else Log("champion traits", "no skeletons", false);
                        }
                        // A Tongueless Cantor hushes the floor under the player: Silenced, the ability keys lock; Holy Water lifts it.
                        var ct = dd.ServerDebugSpawn("cantor", local.transform.position + local.transform.forward * 9f, false) as CantorBrain;
                        if (ct != null)
                        {
                            var hdeb = local.GetComponent<PlayerDebuffs>();
                            hdeb.ServerCleanse();
                            hdeb.ServerResetGrace();
                            ct.ServerDebugHush(local.transform.position, 0.6f);
                            for (float t = 0f; t < 1.2f && ct != null && ct.Hushes == 0; t += 0.1f) { local.GetComponent<Health>().ServerSetFraction(1f); ct.GetComponent<Health>().ServerSetFraction(1f); yield return new WaitForSeconds(0.1f); }
                            yield return new WaitForSeconds(0.2f);
                            // A blow can be dodged by worn luck or stood out of: a second hush where the player stands now.
                            if (!hdeb.Silenced && ct != null)
                            {
                                hdeb.ServerResetGrace();
                                int h0 = ct.Hushes;
                                ct.ServerDebugHush(local.transform.position, 0.4f);
                                for (float t = 0f; t < 1f && ct != null && ct.Hushes == h0; t += 0.1f) yield return new WaitForSeconds(0.1f);
                                yield return null;
                            }
                            bool hushed = hdeb.Silenced, locked = UI.Binds.AbilityLock != null && UI.Binds.AbilityLock();
                            hdeb.ServerCleanse();
                            yield return new WaitForSeconds(0.2f);
                            // Dazed: a heavy blow halves your turning for a moment; Holy Water clears it.
                            hdeb.ServerCleanse();
                            yield return new WaitForSeconds(0.1f);
                            bool dazedNow = hdeb.ServerDaze(1.5f);
                            yield return null;
                            float turn = hdeb.TurnMultiplier;
                            hdeb.ServerCleanse();
                            yield return null;
                            Log("dazed", "took " + dazedNow + ", turning x" + turn.ToString("F2") + ", cleansed " + !hdeb.Dazed, dazedNow && Mathf.Abs(turn - PlayerDebuffs.DazedTurn) < 0.01f && !hdeb.Dazed);
                            Log("cantor silence", "hushes " + (ct != null ? ct.Hushes : -1) + ", silenced " + hushed + ", keys locked " + locked + ", cleansed " + !hdeb.Silenced, hushed && locked && !hdeb.Silenced);
                            if (ct != null) ct.GetComponent<Health>().ServerDamage(1e6f, ct.transform.position);
                        }
                        else Log("cantor silence", "no prefab", false);
                        // Hushing and Concussive elites (2026-10-27): their blows silence and daze.
                        {
                            var adeb = local.GetComponent<PlayerDebuffs>();
                            var ahp = local.GetComponent<Health>();
                            var hush = dd.ServerDebugSpawn("skeleton", local.transform.position + local.transform.forward * 7f, false);
                            var conc = dd.ServerDebugSpawn("skeleton", local.transform.position + local.transform.forward * 7f - local.transform.right * 2f, false);
                            if (hush != null && conc != null)
                            {
                                hush.ServerStun(10f); conc.ServerStun(10f);
                                hush.ServerMakeElite(EliteAffix.Hushing);
                                conc.ServerMakeElite(EliteAffix.Concussive);
                                adeb.ServerCleanse();
                                adeb.ServerResetGrace();
                                yield return new WaitForSeconds(0.1f);
                                ahp.ServerSetFraction(1f);
                                ahp.ServerDamage(5f, hush.transform.position, hush.NetworkObjectId);
                                ahp.ServerDamage(5f, conc.transform.position, conc.NetworkObjectId);
                                yield return null;
                                bool sil = adeb.Silenced, daz = adeb.Dazed;
                                adeb.ServerCleanse();
                                Log("hushing and concussive", "silenced " + sil + ", dazed " + daz + ", tags " + EliteAffixes.Tag(EliteAffix.Hushing) + "/" + EliteAffixes.Tag(EliteAffix.Concussive), sil && daz);
                                hush.GetComponent<Health>().ServerDamage(1e6f, hush.transform.position);
                                conc.GetComponent<Health>().ServerDamage(1e6f, conc.transform.position);
                            }
                        }
                        // Soulbound twins (2026-10-28): one struck down only falls, rises if left, and dies with its twin.
                        {
                            var ta = dd.ServerDebugSpawn("skeleton", local.transform.position + local.transform.forward * 7f + local.transform.right * 2f, false);
                            var tb = dd.ServerDebugSpawn("skeleton", local.transform.position + local.transform.forward * 7f + local.transform.right * 4f, false);
                            if (ta != null && tb != null)
                            {
                                ta.ServerStun(10f); tb.ServerStun(10f);
                                bool bound = EnemyBrain.ServerBind(ta, tb) && ta.AffixKind == EliteAffix.Soulbound && ta.TwinId.Value == tb.NetworkObjectId;
                                var ha = ta.GetComponent<Health>();
                                var hb = tb.GetComponent<Health>();
                                ha.ServerDamage(ha.Current.Value * 3f + 200f, local.transform.position, local.NetworkObjectId);
                                yield return null;
                                bool fell = ha.IsAlive && ta.ServerSevered;
                                ha.ServerDamage(ha.Max.Value * 3f, local.transform.position, local.NetworkObjectId);
                                bool immune = ha.IsAlive;
                                ta.ServerDebugEndWindow();
                                yield return new WaitForSeconds(0.2f);
                                bool rose = ha.IsAlive && !ta.ServerSevered && ha.Fraction > 0.4f;
                                ha.ServerDamage(ha.Current.Value * 3f + 200f, local.transform.position, local.NetworkObjectId);
                                yield return null;
                                bool fellAgain = ha.IsAlive && ta.ServerSevered;
                                hb.ServerDamage(hb.Current.Value * 3f + 200f, local.transform.position, local.NetworkObjectId);
                                yield return new WaitForSeconds(0.2f);
                                bool both = !ha.IsAlive && !hb.IsAlive;
                                Log("soulbound twins", "bound " + bound + ", fell " + fell + ", immune severed " + immune + ", rose " + rose + ", fell again " + fellAgain + ", died together " + both,
                                    bound && fell && immune && rose && fellAgain && both);
                                if (ha.IsAlive) ha.ServerDamage(1e6f, ta.transform.position);
                                if (hb.IsAlive) hb.ServerDamage(1e6f, tb.transform.position);
                            }
                            else Log("soulbound twins", "no spawn", false);
                        }
                        local.GetComponent<Health>().ServerSetFraction(1f);
                        // Floor events: Blood Tithe makes blows hurt more and kills mend.
                        {
                            var hl = local.GetComponent<Health>();
                            hl.ServerSetFraction(1f);
                            float before = hl.Current.Value;
                            hl.ServerDamage(40f, local.transform.position + Vector3.forward);
                            float plain = before - hl.Current.Value;
                            hl.ServerSetFraction(1f);
                            dd.ServerDebugEvent(FloorEvent.BloodTithe);
                            hl.ServerDamage(40f, local.transform.position + Vector3.forward);
                            float tithe = before - hl.Current.Value;
                            float hurt = hl.Current.Value;
                            DungeonDirector.ServerOnPlayerKill(hl);
                            bool mended = hl.Current.Value > hurt;
                            dd.ServerDebugEvent(FloorEvent.None);
                            hl.ServerSetFraction(1f);
                            // Embertide: a player's blow leaves the dead smouldering.
                            var tideFoe = Health.All.FirstOrDefault(hh => hh != null && hh.Team == Team.Enemies && hh.IsAlive && !hh.TryGetComponent<BossBrain>(out _) && !hh.Chilled.Value);
                            if (tideFoe != null)
                            {
                                dd.ServerDebugEvent(FloorEvent.Embertide);
                                tideFoe.ServerSetFraction(1f);
                                tideFoe.ServerDamage(1f, local.transform.position, local.NetworkObjectId);
                                yield return null;
                                bool smoulders = tideFoe.Scorched.Value;
                                dd.ServerDebugEvent(FloorEvent.None);
                                Log("embertide", "struck foe scorched " + smoulders + ", player damage x" + Health.FloorPlayerDamageScale.ToString("F2") + " after", smoulders && Mathf.Approximately(Health.FloorPlayerDamageScale, 1f));
                            }
                            Log("floor event", "blood tithe hit " + plain.ToString("F1") + " -> " + tithe.ToString("F1") + ", kill mends " + mended + ", back to none " + (DungeonDirector.CurrentEvent == FloorEvent.None),
                                (plain <= 0f || tithe > plain) && mended && DungeonDirector.CurrentEvent == FloorEvent.None);
                            {
                                var spProg = local.GetComponent<PlayerProgress>();
                                float sp0 = spProg.SpeedMultiplier;
                                dd.ServerDebugEvent(FloorEvent.Quicksilver);
                                yield return null;
                                float sp1 = spProg.SpeedMultiplier;
                                dd.ServerDebugEvent(FloorEvent.Muster);
                                int champs = DungeonDirector.EventChampions;
                                dd.ServerDebugEvent(FloorEvent.None);
                                Log("new floor events", "quicksilver speed x" + (sp0 > 0f ? (sp1 / sp0).ToString("F2") : "?") + ", muster champions " + champs + ", named " + DungeonDirector.EventName(FloorEvent.Muster),
                                    (sp0 <= 0f || Mathf.Abs(sp1 / sp0 - 1.15f) < 0.02f) && champs == 3 && DungeonDirector.EventName(FloorEvent.Quicksilver).Length > 0);
                            }
                        }
                        // Room clears: kill everything raised in the rooms, and each room counts as cleared.
                        {
                            int clearedBefore = dd.RoomsClearedCount;
                            int clearRoom = dd.ServerDebugClearOneRoom();
                            for (float t = 0f; t < 2f && clearRoom >= 0 && !dd.RoomIsCleared(clearRoom); t += 0.25f) yield return new WaitForSeconds(0.25f);
                            Log("room cleared", "room " + clearRoom + " cleared " + (clearRoom >= 0 && dd.RoomIsCleared(clearRoom)) + ", rooms cleared " + clearedBefore + " -> " + dd.RoomsClearedCount,
                                clearRoom < 0 ? clearedBefore > 0 : dd.RoomIsCleared(clearRoom));
                        }
                        // A patrol: an elite and its band, heading for the second room of its route.
                        {
                            // Two room centres, the farthest from the player, so the band marches undisturbed.
                            var far2 = DungeonDirector.Layout.Rooms.Where(r => r.Kind == RoomKind.Combat)
                                .OrderByDescending(r => Vector3.Distance(r.Center, local.transform.position)).Take(2).ToList();
                            var pr0 = far2.Count > 0 && UnityEngine.AI.NavMesh.SamplePosition(far2[0].Center, out var ph0, 6f, UnityEngine.AI.NavMesh.AllAreas) ? ph0.position : local.transform.position + Vector3.forward * 30f;
                            var pr1 = far2.Count > 1 && UnityEngine.AI.NavMesh.SamplePosition(far2[1].Center, out var ph1, 6f, UnityEngine.AI.NavMesh.AllAreas) ? ph1.position : pr0 + Vector3.right * 12f;
                            var band = dd.ServerSpawnPatrol(new System.Collections.Generic.List<Vector3> { pr0, pr1 }, 3);
                            yield return new WaitForSeconds(0.5f);
                            bool marching = band.Count == 3 && band.All(b => b.OnPatrol && b.Patrolling.Value) && band[0].Elite.Value;
                            bool heading = band.Count > 0 && Vector3.Distance(band[0].PatrolGoal, pr1) < 3.5f;
                            Log("patrol", band.Count + " in the band, marching " + marching + ", leader heading on " + heading, marching && heading);
                            foreach (var b in band) if (b != null && b.IsSpawned) b.NetworkObject.Despawn();
                            yield return new WaitForSeconds(0.2f);
                        }
                        // The castle cat, far below camp: petting it mends you, then it leaves.
                        {
                            var kit = dd.ServerDebugShaftCat(local.transform.position + local.transform.forward * 1.2f);
                            if (kit != null)
                            {
                                yield return new WaitForSeconds(0.2f);
                                var kh = local.GetComponent<Health>();
                                kh.ServerSetFraction(0.5f);
                                // A curse from a Hexer nearby halves healing: not the cat's fault.
                                local.GetComponent<PlayerDebuffs>().ServerCleanse();
                                kit.ServerDebugUse(sp);
                                yield return new WaitForSeconds(0.2f);
                                bool mended = kh.Fraction > 0.7f;
                                for (float t = 0f; t < 4f && kit != null && kit.IsSpawned; t += 0.2f) yield return new WaitForSeconds(0.2f);
                                if (!mended) Debug.Log("[Smoke] shaft cat why: fraction " + kh.Fraction.ToString("F2") + ", heal x" + kh.HealMultiplier.ToString("F2") + ", curse x" + local.GetComponent<PlayerDebuffs>().HealMultiplier.ToString("F2") + ", omen x" + Health.OmenPlayerHealScale.ToString("F2") + ", downed " + local.GetComponent<PlayerLife>().Downed.Value + ", woken " + kit.Woken.Value);
                                Log("shaft cat", "mended " + mended + ", gone after " + (kit == null || !kit.IsSpawned), mended && (kit == null || !kit.IsSpawned));
                                kh.ServerSetFraction(1f);
                            }
                            else Log("shaft cat", "no prefab", false);
                        }
                        // A hand torch: carrying it lights the way and empties your hands.
                        {
                            // A relic found earlier might already be Lamplighter's Oil (it lets the bearer fight): not yet.
                            sp.ServerDebugRemoveRelic(Relic.LamplightersOil);
                            var torch = dd.ServerDebugTorch(local.transform.position + local.transform.forward * 1.5f);
                            if (torch != null)
                            {
                                yield return new WaitForSeconds(0.2f);
                                torch.ServerDebugCarry(sp);
                                yield return new WaitForSeconds(0.2f);
                                // A stray blow can knock it loose (the room is not always empty): pick it up again once.
                                if (!torch.Carried) { torch.ServerDebugCarry(sp); yield return new WaitForSeconds(0.15f); }
                                bool lit = torch.Lit, hands = !local.GetComponent<PlayerLife>().CanAct;
                                torch.ServerDrop(local.transform.position);
                                yield return new WaitForSeconds(0.1f);
                                bool free = local.GetComponent<PlayerLife>().CanAct;
                                Log("hand torch", "lit " + lit + ", hands full while carried " + hands + ", free once set down " + free, lit && hands && free);
                                // Lamplighter's Oil: the torch-bearer can fight.
                                if (sp.RelicCount >= Relics.MaxCarried) sp.ServerDebugRemoveRelic(sp.RelicAt(0));
                                sp.ServerAddRelic(Relic.LamplightersOil);
                                torch.ServerDebugCarry(sp);
                                yield return new WaitForSeconds(0.2f);
                                bool oiled = local.GetComponent<PlayerLife>().CanAct;
                                torch.ServerDrop(local.transform.position);
                                Log("lamplighter's oil", "fights with the torch " + oiled, oiled);
                                // It now frees the hands for any burden: later carry checks expect full hands.
                                sp.ServerDebugRemoveRelic(Relic.LamplightersOil);
                                if (torch.IsSpawned) torch.NetworkObject.Despawn();
                            }
                            else Log("hand torch", "no prefab", false);
                        }
                        // An oath: the player stands on one plate, the Oath Stone is set on the other.
                        {
                            var pa = local.transform.position;
                            var pb = pa + local.transform.right * 4f;
                            var oath = dd.ServerDebugOath(pa, pb, pa + local.transform.forward * 3f);
                            if (oath != null)
                            {
                                yield return new WaitForSeconds(0.3f);
                                var stoneO = Carryable.All.OfType<OathStone>().LastOrDefault();
                                bool alone = oath.Pressed.Value == 1;
                                int keptBefore = dd.OathsKept;
                                if (stoneO != null) { stoneO.ServerDebugCarry(sp); yield return new WaitForSeconds(0.2f); stoneO.ServerDrop(pb); }
                                for (float t = 0f; t < 3f && !oath.Kept.Value; t += 0.2f) yield return new WaitForSeconds(0.2f);
                                Log("oath plates", "one plate alone " + alone + ", stone " + (stoneO != null) + ", kept " + oath.Kept.Value + ", coffer " + (dd.OathsKept - keptBefore), alone && oath.Kept.Value && dd.OathsKept == keptBefore + 1);
                                if (stoneO != null && stoneO.IsSpawned) stoneO.NetworkObject.Despawn();
                            }
                            else Log("oath plates", "no prefab", false);
                        }
                        // New elite affixes: Frenzied speeds up below half; Gravebound leaves bones that a stamp scatters.
                        {
                            var frAt = local.transform.position + local.transform.forward * 6f;
                            var fr = dd.ServerDebugSpawn("skeleton", frAt, false);
                            var gb = dd.ServerDebugSpawn("skeleton", frAt + Vector3.right * 3f, false);
                            if (fr != null && gb != null)
                            {
                                fr.ServerMakeElite(EliteAffix.Frenzied);
                                gb.ServerMakeElite(EliteAffix.Gravebound);
                                yield return new WaitForSeconds(0.2f);
                                bool calm = !fr.Frenzied;
                                var frh = fr.GetComponent<Health>();
                                frh.ServerSetFraction(0.4f);
                                bool frenzied = fr.Frenzied;
                                int piles0 = dd.GraveboundPileCount, made0 = dd.GraveboundPilesMade;
                                var gbAt = gb.transform.position;
                                gb.GetComponent<Health>().ServerDamage(1e6f, gbAt, local.NetworkObjectId);
                                yield return new WaitForSeconds(0.3f);
                                bool pile = dd.GraveboundPilesMade == made0 + 1;
                                local.GetComponent<FirstPersonMotor>().Teleport(gbAt + Vector3.up * 0.1f, local.transform.rotation);
                                yield return new WaitForSeconds(0.6f);
                                bool scattered = dd.GraveboundPileCount <= piles0;
                                Log("elite affixes", "frenzy only below half " + (calm && frenzied) + ", gravebound bones " + pile + ", stamped out " + scattered, calm && frenzied && pile && scattered);
                                if (fr.IsSpawned) fr.NetworkObject.Despawn();
                            }
                            else Log("elite affixes", "no skeleton", false);
                        }
                        // A Choir Acolyte's hymn wards a skeleton beside it; a hard hit mid-song breaks the next one.
                        {
                            var acAt = local.transform.position + local.transform.forward * 7f;
                            var ac = dd.ServerDebugSpawn("acolyte", acAt, false) as ChoirAcolyteBrain;
                            var mate = dd.ServerDebugSpawn("skeleton", acAt + Vector3.right * 2f, false);
                            if (ac != null && mate != null)
                            {
                                // Stand the skeleton right beside wherever the acolyte ended up (spawns snap to the navmesh).
                                // Let both settle onto the navmesh, then stand the skeleton beside the acolyte.
                                yield return new WaitForSeconds(0.3f);
                                mate.ServerStun(20f);
                                mate.ServerDebugPlace(ac.transform.position + ac.transform.right * 1.5f);
                                yield return new WaitForSeconds(0.1f);
                                ac.ServerBeginHymn();
                                for (float t = 0f; t < ChoirAcolyteBrain.HymnSeconds + 1f && ac.HymnsSung == 0; t += 0.2f) { local.GetComponent<Health>().ServerSetFraction(1f); yield return new WaitForSeconds(0.2f); }
                                bool warded = mate.GetComponent<Health>().Shield.Value > 0f;
                                string choirWhy = "dist " + Vector3.Distance(mate.transform.position, ac.transform.position).ToString("F1") + ", mate alive " + !mate.IsDead + ", shield " + mate.GetComponent<Health>().Shield.Value.ToString("F0");
                                yield return new WaitForSeconds(0.2f);
                                var achp = ac.GetComponent<Health>();
                                // Force the next hymn now and break it.
                                ac.ServerDebugResetHymn();
                                ac.ServerBeginHymn();
                                yield return new WaitForSeconds(0.3f);
                                achp.ServerDamage(achp.Max.Value * 0.3f, ac.transform.position + Vector3.forward, local.NetworkObjectId);
                                yield return new WaitForSeconds(0.3f);
                                Log("choir acolyte", "hymn sung " + ac.HymnsSung + ", skeleton warded " + warded + ", broken " + ac.HymnsBroken + " (" + choirWhy + ")", ac.HymnsSung >= 1 && warded && ac.HymnsBroken >= 1);
                                foreach (var x in new EnemyBrain[] { ac, mate }) if (x != null && x.IsSpawned) x.NetworkObject.Despawn();
                            }
                            else Log("choir acolyte", "no prefab", false);
                        }
                        // An Abyssal Eye's beam hits a player standing on its line, and misses one who stepped off.
                        {
                            // Stage it in the start room, along its long side: wherever the player happened to stand, the eye
                            // once landed 0.8 m away (the spot 7 m out was in a wall and the navmesh snap pulled it in).
                            var srE = DungeonDirector.Layout.StartRoom;
                            var axisE = srE.Size.x >= srE.Size.y ? Vector3.right : Vector3.forward;
                            var standE = srE.Center - axisE * 3f;
                            if (UnityEngine.AI.NavMesh.SamplePosition(standE, out var seH, 2f, UnityEngine.AI.NavMesh.AllAreas)) standE = seH.position;
                            local.GetComponent<FirstPersonMotor>().Teleport(standE + Vector3.up * 0.1f, Quaternion.LookRotation(axisE));
                            yield return new WaitForSeconds(0.3f);
                            // A spot 7 m out with nothing between it and the player (its beam stops at walls).
                            var eyeAt = local.transform.position + local.transform.forward * 7f;
                            for (int ed = 0; ed < 8; ed++)
                            {
                                var cand = local.transform.position + Quaternion.Euler(0f, ed * 45f, 0f) * local.transform.forward * 7f;
                                if (!UnityEngine.AI.NavMesh.SamplePosition(cand, out var eh, 1f, UnityEngine.AI.NavMesh.AllAreas)) continue;
                                bool blocked = false;
                                // The same line the beam takes: eye height to the body's centre, and a little past it.
                                var eyeFrom = eh.position + Vector3.up * 1.75f;
                                var toBody = local.GetComponent<Health>().Center - eyeFrom;
                                foreach (var hit in Physics.RaycastAll(eyeFrom, toBody, toBody.magnitude + 1f, ~0, QueryTriggerInteraction.Ignore))
                                    if (hit.collider.GetComponentInParent<Health>() == null) { blocked = true; break; }
                                if (!blocked) { eyeAt = eh.position; break; }
                            }
                            var eyeB = dd.ServerDebugSpawn("eye", eyeAt, false) as AbyssEyeBrain;
                            if (eyeB != null)
                            {
                                yield return new WaitForSeconds(0.3f);
                                var hp9 = local.GetComponent<Health>();
                                hp9.ServerSetFraction(1f);
                                // It drifts while it waits: put it back on the clear spot right before it locks on.
                                eyeB.ServerDebugPlace(eyeAt);
                                eyeB.ServerLockOn(hp9);
                                // Hold it on the clear spot while it locks on (it drifts toward the player otherwise).
                                // ...and the player on theirs (the eye ended up 0.8 m away twice: something shoved the player).
                                var standAt = local.transform.position;
                                var standRot = local.transform.rotation;
                                var standMotor = local.GetComponent<FirstPersonMotor>();
                                for (float t = 0f; t < 2.5f && eyeB.BeamsFired == 0; t += 0.1f)
                                {
                                    hp9.ServerSetFraction(1f);
                                    eyeB.ServerDebugPlace(eyeAt);
                                    if (Vector3.Distance(local.transform.position, standAt) > 0.3f) standMotor.Teleport(standAt, standRot);
                                    yield return new WaitForSeconds(0.1f);
                                }
                                float onLine = eyeB.LastBeamDealt;
                                string eyeWhy = "shield " + hp9.Shield.Value.ToString("F0") + ", dist " + Vector3.Distance(eyeB.transform.position, local.transform.position).ToString("F1") + ", dy " + (eyeB.transform.position.y - local.transform.position.y).ToString("F1") + ", targetable " + local.GetComponent<PlayerLife>().Targetable + ", beam " + eyeB.LastBeamLength.ToString("F1") + " m stopped by " + AbyssEyeBrain.LastBlocker;
                                Log("abyssal eye", "beams " + eyeB.BeamsFired + ", dealt on the line " + onLine.ToString("F0") + " (" + eyeWhy + ")", eyeB.BeamsFired >= 1 && onLine > 0f);
                                if (eyeB.IsSpawned) eyeB.NetworkObject.Despawn();
                            }
                            else Log("abyssal eye", "no prefab", false);
                        }
                        Log("tallyman clock", "arrives after " + dd.TallyArriveSeconds.ToString("F0") + " s for " + DungeonDirector.Layout.Rooms.Count + " rooms, bell at " + dd.TallyWarnSeconds.ToString("F0"),
                            // (A Tallyman's Ledger found earlier adds its three minutes.)
                            Mathf.Approximately(dd.TallyArriveSeconds, DungeonDirector.TallyBaseSeconds + DungeonDirector.TallyPerRoom * DungeonDirector.Layout.Rooms.Count)
                            || Mathf.Approximately(dd.TallyArriveSeconds, DungeonDirector.TallyBaseSeconds + DungeonDirector.TallyPerRoom * DungeonDirector.Layout.Rooms.Count + DungeonDirector.TallyLedgerDelay));
                        // The Tallyman walks to you, drains you on touch, and leaves when dismissed.
                        var tally = dd.ServerDebugTally(local.transform.position + local.transform.forward * 7f);
                        if (tally != null)
                        {
                            float startD = Vector3.Distance(tally.Pos.Value, local.transform.position);
                            for (float t = 0f; t < 5f && tally.DamageDealt <= 0f; t += 0.2f) { local.GetComponent<Health>().ServerSetFraction(1f); yield return new WaitForSeconds(0.2f); }
                            float endD = Vector3.Distance(tally.Pos.Value, local.transform.position);
                            Log("tallyman", "closed " + startD.ToString("F1") + " -> " + endD.ToString("F1") + " m, drained " + tally.DamageDealt.ToString("F0") + ", speed " + tally.Speed.ToString("F1"), endD < startD && tally.DamageDealt > 0f);
                            // Holy water holds him: he does not move while repelled.
                            tally.ServerRepel(1.5f);
                            var heldAt = tally.Pos.Value;
                            yield return new WaitForSeconds(1f);
                            Log("tallyman repelled", "repelled " + tally.Repelled + ", moved " + Vector3.Distance(heldAt, tally.Pos.Value).ToString("F2") + " m", Vector3.Distance(heldAt, tally.Pos.Value) < 0.05f);
                            dd.ServerDebugDismissTally();
                            for (float t = 0f; t < 4f && World.Tallyman.Active != null; t += 0.2f) { local.GetComponent<Health>().ServerSetFraction(1f); yield return new WaitForSeconds(0.2f); }
                            Log("tallyman leaves", "gone " + (World.Tallyman.Active == null), World.Tallyman.Active == null);
                        }
                        else Log("tallyman", "no prefab", false);
                        local.GetComponent<Health>().ServerSetFraction(1f);
                    }
                    int roomTraps = FindObjectsByType<SpikeTrap>(FindObjectsSortMode.None).Count(t => layout.RoomAt(t.transform.position, 0f) != null);
                    Log("room traps", roomTraps + " spike traps inside rooms on this floor", true);
                    // A spike plate (2026-11-01, "more dangerous"): it fires in 0.4 s, bites hard, and pins the player it catches.
                    var plate = FindObjectsByType<SpikeTrap>(FindObjectsSortMode.None).FirstOrDefault();
                    // Trap rolls are seeded: a floor with no plate at all once left this check unrun (2026-11-07).
                    if (plate == null)
                    {
                        var spot = layout.PlayerSpawnFor(local.OwnerClientId) + local.transform.forward * 3f;
                        if (UnityEngine.AI.NavMesh.SamplePosition(spot, out var onMesh, 3f, UnityEngine.AI.NavMesh.AllAreas)) spot = onMesh.position;
                        plate = World.DungeonDirector.Instance.ServerDebugSpawnPlate(spot);
                        yield return new WaitForSeconds(0.3f);
                    }
                    if (plate != null)
                    {
                        var ph = local.GetComponent<Health>();
                        var pdeb = local.GetComponent<PlayerDebuffs>();
                        pdeb.ServerResetGrace();
                        ph.Shield.Value = 0f;
                        ph.ServerSetFraction(1f);
                        yield return new WaitForSeconds(SpikeTrap.Rearm + 0.2f);
                        // A shield regrown during the wait (a relic) soaked the first bite while the pin still took: clear both now.
                        ph.Shield.Value = 0f;
                        pdeb.ServerResetGrace();
                        float before = ph.Current.Value;
                        local.GetComponent<FirstPersonMotor>().Teleport(plate.transform.position + Vector3.up * 0.1f, local.transform.rotation);
                        // Something else on the plate (a straggler, the client) can have it mid-beat: wait out one full cycle.
                        float took = 0f;
                        for (float t = 0f; t < SpikeTrap.Delay + SpikeTrap.Rearm + 0.6f && took <= 0f; t += 0.05f)
                        {
                            yield return new WaitForSeconds(0.05f);
                            took = before - ph.Current.Value;
                        }
                        yield return null;
                        bool pinned = pdeb.Rooted;
                        Log("spike plate", "took " + took.ToString("F0") + " of " + ph.Max.Value.ToString("F0") + " (hidden " + plate.Hidden.Value + "), pinned " + pinned, took >= ph.Max.Value * 0.2f && took <= ph.Max.Value * 0.4f && pinned);
                        local.GetComponent<FirstPersonMotor>().Teleport(layout.PlayerSpawnFor(local.OwnerClientId), local.transform.rotation);
                        ph.ServerSetFraction(1f);
                        yield return new WaitForSeconds(SpikeTrap.ImpaleSeconds);
                        // A pet (2026-11-07) fetches a pile of gold lying beyond the coin magnet, in sight.
                        {
                            var np = local.GetComponent<NetworkPlayer>();
                            var petProg = local.GetComponent<PlayerProgress>();
                            byte petWas = np.PetKind.Value;
                            np.PetKind.Value = (byte)PetCompanion.Kind.BoneHound;
                            var petFrom = local.transform.position;
                            // A spot 5 m off in clear view: try headings until one is.
                            var petSpot = petFrom + local.transform.forward * 5f;
                            for (int hd = 0; hd < 12; hd++)
                            {
                                var tryAt = petFrom + Quaternion.Euler(0f, hd * 30f, 0f) * local.transform.forward * 5f;
                                if (!UnityEngine.AI.NavMesh.SamplePosition(tryAt, out var petMesh, 1.5f, UnityEngine.AI.NavMesh.AllAreas)) continue;
                                if (Vector3.Distance(petFrom, petMesh.position) < 3.5f || !LootDrop.PetCanReach(petFrom, petMesh.position)) continue;
                                petSpot = petMesh.position;
                                break;
                            }
                            int petGold0 = petProg.Gold.Value, dropsBefore = LootDrop.All.Count;
                            World.DungeonDirector.Instance.ServerDropLoot(petSpot, LootKind.Gold, 40, null, settle: false, fixedSum: true);
                            float waited = 0f;
                            for (; waited < 4f && petProg.Gold.Value == petGold0; waited += 0.1f) yield return new WaitForSeconds(0.1f);
                            float far = Vector3.Distance(petFrom, petSpot);
                            np.PetKind.Value = petWas;
                            bool sizes = Cosmetics.Pets.Length == System.Enum.GetValues(typeof(PetCompanion.Kind)).Length;
                            Log("pet fetch", "gold " + petGold0 + " -> " + petProg.Gold.Value + " from " + far.ToString("F1") + " m after " + waited.ToString("F1") + " s, pets listed " + Cosmetics.Pets.Length,
                                petProg.Gold.Value > petGold0 && far > 2.6f && waited >= PetCompanion.FetchDelay - 0.2f && sizes);
                        }
                        // A flame vent strikes once a burst, for a third of your health, and goes dark long enough to cross.
                        if (layout.FlameTraps.Count > 0)
                        {
                            var vent = layout.FlameTraps[0];
                            var ventMotor = local.GetComponent<FirstPersonMotor>();
                            pdeb.ServerResetGrace();
                            ph.ServerSetFraction(1f);
                            for (int w = 0; w < 120 && FlameJet.Lit(vent.Phase, Pendulum.Clock); w++) yield return new WaitForSeconds(0.02f);
                            float beforeVent = ph.Current.Value;
                            ventMotor.Teleport(vent.At + Vector3.up * 0.1f, local.transform.rotation);
                            for (int w = 0; w < 120 && !FlameJet.Lit(vent.Phase, Pendulum.Clock); w++) yield return new WaitForSeconds(0.02f);
                            for (int w = 0; w < 120 && FlameJet.Lit(vent.Phase, Pendulum.Clock); w++) yield return new WaitForSeconds(0.02f);
                            float tookVent = beforeVent - ph.Current.Value;
                            ventMotor.Teleport(layout.PlayerSpawnFor(local.OwnerClientId), local.transform.rotation);
                            Log("flame vent", "took " + tookVent.ToString("F0") + " of " + ph.Max.Value.ToString("F0") + " in one burst", tookVent >= ph.Max.Value * 0.2f && tookVent <= ph.Max.Value * 0.4f);
                            ph.ServerSetFraction(1f);
                        }
                    }
                }
                // The blood altar: three offerings fill the chalice; a relic for who bled, Bloodlust for all.
                {
                    var altarT = FindObjectsByType<BloodAltar>(FindObjectsSortMode.None).FirstOrDefault(a => !a.Spent.Value);
                    if (altarT != null)
                    {
                        var ap2 = local.GetComponent<PlayerProgress>();
                        var ah = local.GetComponent<Health>();
                        fm.Teleport(altarT.transform.position + altarT.transform.forward * 1.6f + Vector3.up * 0.1f, Quaternion.LookRotation(-altarT.transform.forward));
                        foreach (var e in FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None))
                            if (!(e is BossBrain) && Vector3.Distance(e.transform.position, altarT.transform.position) < 16f) e.GetComponent<Health>().ServerDamage(1e6f, altarT.transform.position);
                        yield return new WaitForSeconds(0.3f);
                        int relics0 = ap2.RelicCount, emb0 = ap2.Embers.Value;
                        for (int k = 0; k < BloodAltar.Offerings; k++)
                        {
                            ah.ServerSetFraction(1f);
                            altarT.UseRpc(local.NetworkObject);
                            yield return new WaitForSeconds(0.75f);
                        }
                        bool bled = ah.Fraction < 0.95f;
                        ah.ServerSetFraction(1f);
                        Log("blood altar", "chalice " + altarT.Filled.Value + "/" + BloodAltar.Offerings + ", spent " + altarT.Spent.Value + ", bloodlust " + ap2.Bloodlust + ", relics " + relics0 + " -> " + ap2.RelicCount + ", embers " + emb0 + " -> " + ap2.Embers.Value + ", bled " + bled,
                            altarT.Spent.Value && ap2.Bloodlust && ap2.RelicCount > relics0 && ap2.Embers.Value > emb0);
                    }
                    else Log("blood altar", "none on this floor", false);
                }

                var sarc = FindObjectsByType<Sarcophagus>(FindObjectsSortMode.None).FirstOrDefault(x => x.IsSpawned && x.HasOccupant && !x.Opened.Value && !x.Scheduled);
                if (sarc != null)
                {
                    fm.Teleport(sarc.transform.position + sarc.transform.forward * 3f + Vector3.up * 0.1f, local.transform.rotation);
                    yield return new WaitForSeconds(0.5f);
                    Log("crypt", "sarcophagus opened on approach: " + sarc.Opened.Value + (sarc.Scheduled ? " (a neighbour woke first; scheduled)" : "") + " (spawned " + sarc.IsSpawned + ", occupant " + sarc.HasOccupant + ", targetable " + local.GetComponent<PlayerLife>().Targetable +
                        ", downed " + local.GetComponent<PlayerLife>().Downed.Value + (sarc.Opened.Value ? "" : ", " + sarc.DebugState()) + ", dist " + Vector3.Distance(local.transform.position, sarc.transform.position).ToString("F1") + ")", sarc.Opened.Value || sarc.Scheduled);
                    // The rest of the crypt follows, two at a time.
                    yield return new WaitForSeconds(5.2f);
                    var all = FindObjectsByType<Sarcophagus>(FindObjectsSortMode.None);
                    int open = all.Count(x => x.Opened.Value);
                    Log("crypt wakes", open + " of " + all.Length + " open 5 s later", open >= Mathf.Min(all.Length, 3));
                    foreach (var e in FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None))
                        if (!(e is BossBrain) && Vector3.Distance(e.transform.position, sarc.transform.position) < 20f) e.GetComponent<Health>().ServerDamage(1e6f, sarc.transform.position);
                    local.GetComponent<Health>().ServerSetFraction(1f);
                }

                var levers = FindObjectsByType<Lever>(FindObjectsSortMode.None);
                var gatesBefore = FindObjectsByType<Portcullis>(FindObjectsSortMode.None).Count(g => g.Closed.Value);
                foreach (var lv in levers)
                {
                    fm.Teleport(lv.transform.position + lv.transform.forward * 1.5f, Quaternion.LookRotation(-lv.transform.forward));
                    yield return new WaitForSeconds(0.2f);
                    lv.UseRpc(local.NetworkObject);
                    yield return new WaitForSeconds(0.2f);
                }
                yield return new WaitForSeconds(0.3f);
                var gatesAfter = FindObjectsByType<Portcullis>(FindObjectsSortMode.None).Count(g => g.Closed.Value);
                Log("vault", levers.Length + " levers pulled, closed gates " + gatesBefore + " -> " + gatesAfter + ", open " + DungeonDirector.Instance.VaultOpen, levers.Length == 3 && DungeonDirector.Instance.VaultOpen);

                var fountain = FindObjectsByType<Fountain>(FindObjectsSortMode.None).FirstOrDefault();
                if (fountain != null)
                {
                    var hpF = local.GetComponent<Health>();
                    hpF.ServerSetFraction(0.3f);
                    fm.Teleport(fountain.transform.position + Vector3.forward * 2f, Quaternion.LookRotation(Vector3.back));
                    yield return new WaitForSeconds(0.2f);
                    fountain.UseRpc(local.NetworkObject);
                    yield return new WaitForSeconds(0.2f);
                    Log("fountain", "hp " + hpF.Current.Value.ToString("F0") + " / " + hpF.Max.Value.ToString("F0") + ", spent " + fountain.Spent.Value, fountain.Spent.Value && hpF.Fraction > 0.95f); // from 30%: a stray hit can land in the same moment
                }

                var chasmRoom = layout.Rooms.FirstOrDefault(r => r.Feature == RoomFeature.Chasm);
                if (chasmRoom != null)
                {
                    // Drop the player into the pit (beside the bridges) and expect a rescue.
                    var pitPoint = new Vector3(chasmRoom.Pit.xMin + 1.5f, -6f, chasmRoom.Pit.yMin + 1.5f);
                    fm.Teleport(pitPoint, local.transform.rotation);
                    yield return new WaitForSeconds(1.2f);
                    Log("chasm fall", "y after fall " + local.transform.position.y.ToString("F1") + ", hp " + local.GetComponent<Health>().Current.Value.ToString("F0"),
                        local.transform.position.y > -0.5f);
                }
                fm.Teleport(layout.PlayerSpawnFor(local.OwnerClientId), local.transform.rotation);
                local.GetComponent<Health>().ServerSetFraction(1f);
                yield return new WaitForSeconds(0.3f);

                // Ambush: step into the trap room, clear both waves, find the chest.
                var dir = DungeonDirector.Instance;
                if (dir.AmbushRoom != null)
                {
                    var amb = dir.AmbushRoom;
                    var am = local.GetComponent<FirstPersonMotor>();
                    int chestsBefore = FindObjectsByType<Chest>(FindObjectsSortMode.None).Length;
                    // An earlier step can wander through the trap room first (rooms are bigger since 2026-11-01): then
                    // it has already sprung and been cleared, and the gates must stand open.
                    int wavePre = dir.AmbushWave;
                    am.Teleport(amb.Center + Vector3.up * 0.1f, local.transform.rotation);
                    // The client stays outside: the trap shuts on the host alone, sized for one.
                    yield return new WaitForSeconds(0.5f);
                    int waveSeen = dir.AmbushWave;
                    bool lonePart = PlayerLife.All.Count < 2 || dir.AmbushParty == 1;
                    bool gatesShut = FindObjectsByType<Portcullis>(FindObjectsSortMode.None).Any(g => g.Closed.Value);
                    // No one left standing inside (here: the host steps out of the room, as good as down to the trap): the
                    // gates lift for the rest of the party, and the dead fight on. Downing the host would end a solo test.
                    if (wavePre < 3 && waveSeen >= 1)
                    {
                        am.Teleport(amb.Center + new Vector3(amb.Size.x / 2f + 6f, 0.1f, 0f), local.transform.rotation);
                        yield return new WaitForSeconds(0.5f);
                        bool lifted = !FindObjectsByType<Portcullis>(FindObjectsSortMode.None).Any(g => g.Closed.Value);
                        Log("ambush gates lift", "no one standing inside: gates open " + lifted + ", wave " + dir.AmbushWave, lifted && dir.AmbushWave >= 1 && dir.AmbushWave < 3);
                        am.Teleport(amb.Center + Vector3.up * 0.1f, local.transform.rotation);
                        yield return new WaitForSeconds(0.3f);
                    }
                    for (int pass = 0; pass < 6 && dir.AmbushWave < 3; pass++)
                    {
                        foreach (var e in FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None))
                            if (!(e is BossBrain) && !e.IsDead && amb.Contains(e.transform.position, 1f)) e.GetComponent<Health>().ServerDamage(1e6f, e.transform.position, local.NetworkObjectId);
                        yield return new WaitForSeconds(0.6f);
                    }
                    int chestsAfter = FindObjectsByType<Chest>(FindObjectsSortMode.None).Length;
                    Log("ambush", "before " + wavePre + ", triggered wave " + waveSeen + " (shut in " + dir.AmbushParty + "), gates shut " + gatesShut + ", final wave " + dir.AmbushWave + ", chests " + chestsBefore + " -> " + chestsAfter,
                        wavePre >= 3 ? dir.AmbushWave == 3 && !FindObjectsByType<Portcullis>(FindObjectsSortMode.None).Any(g => g.Closed.Value)
                            // Already sprung by someone wandering through and left empty: its gates have lifted, by the new rule.
                            : waveSeen >= 1 && (gatesShut || wavePre > 0) && dir.AmbushWave == 3 && chestsAfter == chestsBefore + 1 && lonePart);
                    am.Teleport(layout.PlayerSpawnFor(local.OwnerClientId), local.transform.rotation);
                    yield return new WaitForSeconds(0.3f);
                }

                // Kill a handful of skeletons and confirm they drop loot that a player can take.
                // Only the dead standing in rooms: a breach's ambushers wait in a sealed niche with no floor to walk to yet.
                var victims = FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None).Where(e => !(e is BossBrain) && layout.RoomAt(e.transform.position, 0f) != null).Take(6).ToList();
                foreach (var v in victims) v.GetComponent<Health>().ServerDamage(1e6f, v.transform.position, local.NetworkObjectId);
                yield return new WaitForSeconds(0.3f);

                var drops = FindObjectsByType<LootDrop>(FindObjectsSortMode.None);
                Log("loot drops", drops.Length + " from " + victims.Count + " kills", drops.Length > 0);
                // Every drop landed where you can walk to it (not in a wall, on a ledge or inside a prop).
                int reachable = drops.Count(d => d != null && UnityEngine.AI.NavMesh.SamplePosition(d.transform.position, out _, 0.7f, UnityEngine.AI.NavMesh.AllAreas));
                var lost = drops.Where(d => d != null && !UnityEngine.AI.NavMesh.SamplePosition(d.transform.position, out _, 0.7f, UnityEngine.AI.NavMesh.AllAreas)).ToList();
                string lostWhat = string.Join("; ", lost.Select(d => d.Kind.Value + " at " + d.transform.position.ToString("F1") + " room " + (layout.RoomAt(d.transform.position, 0f) != null ? layout.RoomAt(d.transform.position, 0f).Kind.ToString() : "none") +
                    " nearest mesh " + (UnityEngine.AI.NavMesh.SamplePosition(d.transform.position, out var lh, 5f, UnityEngine.AI.NavMesh.AllAreas) ? Vector3.Distance(lh.position, d.transform.position).ToString("F2") : "none")));
                Log("loot reachable", reachable + " / " + drops.Count(d => d != null) + " on walkable floor" + (lost.Count > 0 ? " (off: " + lostWhat + ")" : ""), reachable == drops.Count(d => d != null));

                var progress = local.GetComponent<PlayerProgress>();
                int goldBefore = progress.Gold.Value;
                var motor = local.GetComponent<FirstPersonMotor>();
                foreach (var drop in drops)
                {
                    // Standing on one drop can collect a neighbour too, despawning it.
                    if (drop == null) continue;
                    motor.Teleport(drop.transform.position, local.transform.rotation);
                    yield return new WaitForSeconds(0.9f);
                }
                Log("pickup", "gold " + goldBefore + " -> " + progress.Gold.Value + ", embers " + progress.Embers.Value,
                    progress.Gold.Value > goldBefore || progress.Embers.Value > 0);

                // Class combos: a stunned enemy is Exposed; a heavy hit Shatters chill.
                var foe = FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None).FirstOrDefault(e => !(e is BossBrain) && !e.IsDead);
                if (foe != null)
                {
                    var fh = foe.GetComponent<Health>();
                    float plain = Classes.Combos.ServerMultiplier(fh, progress);
                    foe.ServerStun(2f);
                    float exposed = Classes.Combos.ServerMultiplier(fh, progress);
                    fh.ServerChill(3f);
                    float shatter = Classes.Combos.TryShatter(fh);
                    Log("combos", "plain x" + plain.ToString("F2") + ", stunned x" + exposed.ToString("F2") + ", shatter x" + shatter + ", chill left " + fh.Chilled.Value + ", credited " + progress.CombosTriggered.Value,
                        exposed > plain * 1.2f && shatter > 1.4f && !fh.Chilled.Value && progress.CombosTriggered.Value > 0);
                }

                // Elite affixes: a Warded elite shrugs off damage until a stun breaks the ward.
                var eliteTest = FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None).FirstOrDefault(e => !(e is BossBrain) && !e.IsDead && !e.Elite.Value && !e.IsStunned && !e.GetComponent<Health>().Chilled.Value && e != foe);
                if (eliteTest != null)
                {
                    var eh = eliteTest.GetComponent<Health>();
                    eliteTest.ServerMakeElite(EliteAffix.Warded);
                    if (eliteTest.AffixKind == EliteAffix.Warded)
                    {
                        float hpA = eh.Current.Value;
                        eh.ServerDamage(50f, local.transform.position);
                        float warded = hpA - eh.Current.Value;
                        eliteTest.ServerStun(1f);
                        yield return null;
                        float hpB = eh.Current.Value;
                        eh.ServerDamage(50f, local.transform.position);
                        float broken = hpB - eh.Current.Value;
                        Log("elite ward", "warded hit " + warded.ToString("F0") + ", after stun " + broken.ToString("F0") + ", affix now " + eliteTest.AffixKind, broken > warded * 2f && eliteTest.AffixKind == EliteAffix.None);
                    }
                    else Log("elite ward", "rolled " + eliteTest.AffixKind + " (no ward to test)", true);
                }

                motor.Teleport(layout.PlayerSpawnFor(local.OwnerClientId), local.transform.rotation);
            }

            yield return new WaitForSeconds(1f);

            if (net.IsServer && boss != null)
            {
                // Asleep (party not all inside): hits from the doorway do nothing and the gates stay open.
                bool wasDormant = boss.Dormant;
                float hpBefore = boss.Health.Current.Value;
                boss.Health.ServerDamage(60f, boss.transform.position + boss.transform.forward * 6f);
                yield return new WaitForSeconds(0.3f);
                Log("boss asleep until the party is in", "dormant " + wasDormant + ", hp " + hpBefore.ToString("F0") + " -> " + boss.Health.Current.Value.ToString("F0") + ", sealed " + DungeonDirector.Instance.BossSealed,
                    !wasDormant || (Mathf.Approximately(hpBefore, boss.Health.Current.Value) && boss.Dormant && !DungeonDirector.Instance.BossSealed));
                {
                    // The entrance: it rises to meet you and holds still under its title.
                    int played = BossBrain.EntrancesPlayed;
                    boss.ServerEntrance();
                    yield return new WaitForSeconds(0.2f);
                    Log("boss entrance", "held " + boss.InEntrance + ", played " + (BossBrain.EntrancesPlayed - played), boss.InEntrance && BossBrain.EntrancesPlayed > played);
                    yield return new WaitForSeconds(BossBrain.EntranceSeconds);
                }
                {
                    // Arena machinery: a ballista bolt and a dropped chandelier both hurt the guardian.
                    var bq = layout.BossRoom;
                    Log("arena", "shape " + bq.Arena + ", quirk " + bq.Quirk, true);
                    bool wasAsleep = boss.Dormant;
                    boss.Dormant = false;
                    boss.ServerDebugResetPhases();
                    var sp3 = local.GetComponent<PlayerProgress>();
                    // Ballista spots round the boss until a bolt lands (a pylon, pillar or the dais can eat one).
                    float b0 = boss.Health.Current.Value, bTry = b0;
                    ArenaEngine bal = null;
                    for (int dirI = 0; dirI < 8 && bTry >= b0; dirI++)
                    {
                        if (bal != null && bal.IsSpawned) bal.NetworkObject.Despawn();
                        var cand = boss.transform.position + Quaternion.Euler(0f, dirI * 45f, 0f) * boss.transform.forward * 6f;
                        bal = DungeonDirector.Instance.ServerDebugEngine(ArenaEngine.Kind.Ballista, cand, Vector3.zero);
                        yield return new WaitForSeconds(0.2f);
                        if (bal != null) bal.ServerDebugUse(sp3);
                        yield return new WaitForSeconds(0.5f);
                        bTry = boss.Health.Current.Value;
                    }
                    float b1 = boss.Health.Current.Value;
                    var winch = DungeonDirector.Instance.ServerDebugEngine(ArenaEngine.Kind.Winch, boss.transform.position + boss.transform.right * 6f, boss.transform.position);
                    yield return new WaitForSeconds(0.3f);
                    if (winch != null) winch.ServerDebugUse(sp3);
                    yield return new WaitForSeconds(1.3f);
                    float b2 = boss.Health.Current.Value;
                    Log("arena machinery", "ballista " + b0.ToString("F0") + " -> " + b1.ToString("F0") + ", chandelier -> " + b2.ToString("F0") + ", stunned " + boss.IsStunned, bal != null && winch != null && b1 < b0 && b2 < b1);
                    boss.Dormant = wasAsleep;
                }
                boss.Health.ServerDamage(1e6f, boss.transform.position);
                yield return new WaitForSeconds(1f);
                var phase = DungeonDirector.Instance.Phase.Value;
                Log("boss kill -> phase", phase.ToString(), phase == RunPhase.FloorCleared);
                // Floor 1 ends at a Warden now (no eulogy of its own); the act bosses each write one.
                var deed = boss.Deed;
                int eulogy = UI.Journal.EulogyFor((int)deed);
                Log("boss last words", boss.DisplayName + ": deed " + Progression.Deeds.Has(deed) + ", eulogy page " + (eulogy < 0 ? "none for a Warden" : UI.Journal.Has(eulogy).ToString()),
                    Progression.Deeds.Has(deed) && (eulogy < 0 || UI.Journal.Has(eulogy)));
                // No timer: the party leaves when everyone is at the portal.
                yield return new WaitForSeconds(2f);
                bool stillHere = DungeonDirector.Instance.Phase.Value == RunPhase.FloorCleared;
                // Going down after the guardian fell must not hold the lift forever.
                var clearedLife = NetworkPlayer.Local.GetComponent<PlayerLife>();
                NetworkPlayer.Local.GetComponent<Health>().ServerDamage(1e6f, NetworkPlayer.Local.transform.position);
                yield return null;
                bool clearedDown = clearedLife.Downed.Value;
                yield return new WaitForSeconds(5.5f);
                Log("cleared floor picks you up", "down " + clearedDown + ", then down " + clearedLife.Downed.Value, !clearedLife.Downed.Value);
                DungeonDirector.Instance.ServerDebugGatherAtExit();
                Log("exit at will", "waited without leaving " + stillHere, stillHere);
            }

            // After the boss the party goes home to camp, run intact; spend there, then ride down.
            // (The local player rides the grave lift up: pinned to its floor, then let go at camp.)
            float campBy = Time.time + 20f;
            bool rodeLift = false;
            while (net.IsServer && DungeonDirector.Instance.Phase.Value != RunPhase.Lobby && Time.time < campBy) { rodeLift |= Art.ExitPortal.LocalRiding; yield return null; }
            yield return new WaitForSeconds(1f);
            if (net.IsServer && local != null)
                Log("grave lift ride", "rode up " + rodeLift + ", free at camp " + !local.GetComponent<FirstPersonMotor>().Pinned, rodeLift && !local.GetComponent<FirstPersonMotor>().Pinned);
            if (net.IsServer && local != null)
            {
                var dc = DungeonDirector.Instance;
                var pc = local.GetComponent<PlayerProgress>();
                Log("camp between floors", "in camp " + DungeonDirector.InLobby + ", next floor " + (dc.NextDepth.Value + 1) + ", kills kept " + pc.Kills.Value + ", gold " + pc.Gold.Value,
                    DungeonDirector.InLobby && dc.RunInProgress && dc.NextDepth.Value == 1 && pc.Kills.Value > 0);
                // The tent's new games, the cat, and solid props.
                {
                    var slotsT = FindObjectsByType<SlotMachine>(FindObjectsSortMode.None).FirstOrDefault();
                    var bjT = FindObjectsByType<BlackjackTable>(FindObjectsSortMode.None).FirstOrDefault();
                    var catT = FindObjectsByType<CampCat>(FindObjectsSortMode.None).FirstOrDefault();
                    var gp = local.GetComponent<PlayerProgress>();
                    var gm = local.GetComponent<FirstPersonMotor>();
                    gp.ServerAddGold(200);
                    bool slotOk = false, bjOk = false;
                    if (slotsT != null)
                    {
                        gm.Teleport(slotsT.transform.position + slotsT.transform.forward * 1.4f + Vector3.up * 0.1f, local.transform.rotation);
                        yield return new WaitForSeconds(0.3f);
                        int g0 = gp.Gold.Value;
                        slotsT.UseRpc(local.NetworkObject);
                        yield return new WaitForSeconds(0.3f);
                        int delta = gp.Gold.Value - g0;
                        slotOk = delta == -SlotMachine.Stake || delta > 0;
                    }
                    if (bjT != null)
                    {
                        gm.Teleport(bjT.transform.position + bjT.transform.forward * 1.3f + Vector3.up * 0.1f, local.transform.rotation);
                        yield return new WaitForSeconds(0.3f);
                        int g0 = gp.Gold.Value;
                        bjT.UseRpc(local.NetworkObject);
                        yield return new WaitForSeconds(0.3f);
                        bool seated = BlackjackTable.LocalOpen;
                        // A chosen bet; then DEAL AGAIN after the hand is over.
                        bjT.DealRpc(local.NetworkObject, 40);
                        yield return new WaitForSeconds(0.3f);
                        bool dealt = gp.Gold.Value != g0;
                        bjT.InsuranceRpc(local.NetworkObject, false);
                        bjT.StandRpc(local.NetworkObject);
                        yield return new WaitForSeconds(0.3f);
                        int g1 = gp.Gold.Value;
                        bjT.DealRpc(local.NetworkObject, 15);
                        yield return new WaitForSeconds(0.3f);
                        bool again = gp.Gold.Value != g1;
                        bjT.InsuranceRpc(local.NetworkObject, false);
                        bjT.StandRpc(local.NetworkObject);
                        yield return new WaitForSeconds(0.3f);
                        // Split a pair of eights against a dealer 10-7: two hands, a second bet.
                        int g2 = gp.Gold.Value;
                        bjT.ServerDebugDeal(gp, 20, new byte[] { 7, 20, 9, 6 });
                        bjT.SplitRpc(local.NetworkObject);
                        yield return new WaitForSeconds(0.2f);
                        bool split = bjT.ServerHandCount(gp) == 2 && gp.Gold.Value == g2 - 40;
                        bjT.StandRpc(local.NetworkObject);
                        bjT.StandRpc(local.NetworkObject);
                        yield return new WaitForSeconds(0.2f);
                        bool splitDone = bjT.ServerPhase(gp) == BlackjackTable.Phase.Done;
                        // Double an 11 (5+6) against a dealer 9-7: one card, the bet doubled, the hand over.
                        int g3 = gp.Gold.Value;
                        bjT.ServerDebugDeal(gp, 20, new byte[] { 4, 18, 8, 6 });
                        bjT.DoubleRpc(local.NetworkObject);
                        yield return new WaitForSeconds(0.2f);
                        bool doubled = bjT.ServerPhase(gp) == BlackjackTable.Phase.Done && (gp.Gold.Value == g3 - 40 || gp.Gold.Value == g3 || gp.Gold.Value == g3 + 40);
                        Log("blackjack rules", "seated " + seated + ", dealt " + dealt + ", deal again " + again + ", split " + split + "/" + splitDone + ", double " + doubled,
                            seated && dealt && again && split && splitDone && doubled);
                        bjOk = seated && dealt && again;
                        // The house limit is 100, and a big winner is cut off until the next floor.
                        bjT.InsuranceRpc(local.NetworkObject, false);
                        bjT.StandRpc(local.NetworkObject);
                        yield return new WaitForSeconds(0.2f);
                        gp.ServerAddGold(600);
                        bjT.DealRpc(local.NetworkObject, 500);
                        yield return new WaitForSeconds(0.3f);
                        int stake = bjT.ServerStake(gp);
                        bjT.InsuranceRpc(local.NetworkObject, false);
                        bjT.StandRpc(local.NetworkObject);
                        yield return new WaitForSeconds(0.3f);
                        bjT.ServerDebugWinnings(gp, BlackjackTable.CutOff);
                        int g4 = gp.Gold.Value;
                        bjT.DealRpc(local.NetworkObject, 20);
                        yield return new WaitForSeconds(0.3f);
                        bool cut = bjT.CutOffFor(gp) && gp.Gold.Value == g4;
                        bjT.ServerDebugWinnings(gp, 0);
                        Log("blackjack limits", "a 500 bet staked " + stake + " (max " + BlackjackTable.MaxBet + "), cut off after " + BlackjackTable.CutOff + " won " + cut,
                            stake == BlackjackTable.MaxBet && cut);
                    }
                    int solid = 0;
                    foreach (var bc in FindObjectsByType<BoxCollider>(FindObjectsSortMode.None)) if (bc.GetComponent<MeshRenderer>() != null && bc.GetComponentInParent<Unity.Netcode.NetworkObject>() == null) solid++;
                    bool catOk = false;
                    if (catT != null)
                    {
                        var cprog = local.GetComponent<PlayerProgress>();
                        float max0 = local.GetComponent<Health>().Max.Value;
                        gm.Teleport(catT.transform.position + Vector3.back * 0.8f + Vector3.up * 0.1f, local.transform.rotation);
                        yield return null;
                        catT.PickUpRpc(local.NetworkObject);
                        yield return new WaitForSeconds(0.3f);
                        bool peed = cprog.CatPeed.Value && local.GetComponent<Health>().Max.Value < max0 - 0.5f;
                        yield return new WaitForSeconds(2.6f);
                        gm.Teleport(catT.transform.position + Vector3.back * 0.8f + Vector3.up * 0.1f, local.transform.rotation);
                        yield return null;
                        catT.UseRpc(local.NetworkObject);
                        yield return new WaitForSeconds(0.2f);
                        catT.PickUpRpc(local.NetworkObject);
                        yield return new WaitForSeconds(0.3f);
                        catOk = !peed && cprog.CatWhisker.Value; // 2026-11-07: no more peeing, it only wriggles free
                        Log("cat verdict", "unpetted pick-up punished " + peed + ", petted pick-up whisker " + cprog.CatWhisker.Value, catOk);
                        cprog.CatPeed.Value = false;
                        cprog.CatWhisker.Value = false;
                        // Swing at it: it is not hurt, it bolts, and it will not have you this visit.
                        yield return new WaitForSeconds(2.6f);
                        var catHp = catT.GetComponent<Health>();
                        catHp.ServerDamage(10f, local.transform.position, local.NetworkObjectId);
                        yield return new WaitForSeconds(0.3f);
                        bool fled = catT.FleeEnd.Value > NetworkManager.Singleton.ServerTime.Time;
                        gm.Teleport(catT.transform.position + Vector3.back * 0.8f + Vector3.up * 0.1f, local.transform.rotation);
                        yield return null;
                        bool whiskerBefore = cprog.CatWhisker.Value;
                        catT.PickUpRpc(local.NetworkObject);
                        yield return new WaitForSeconds(0.3f);
                        Log("cat struck", "fled " + fled + ", unhurt " + (catHp.Current.Value >= catHp.Max.Value) + ", refused " + (catT.HeldBy.Value == ulong.MaxValue && cprog.CatWhisker.Value == whiskerBefore && !cprog.CatPeed.Value),
                            fled && catHp.Current.Value >= catHp.Max.Value && catT.HeldBy.Value == ulong.MaxValue && !cprog.CatPeed.Value);
                    }
                    Log("camp folk", FindObjectsByType<UI.CampChatter>(FindObjectsSortMode.None).Length + " chatter, figures " +
                        (GameObject.Find("Smith") != null) + "/" + (GameObject.Find("Priest") != null) + "/" + (GameObject.Find("Tailor") != null) + "/" + (GameObject.Find("Driver") != null),
                        FindObjectsByType<UI.CampChatter>(FindObjectsSortMode.None).Length == 1 && GameObject.Find("Smith") != null && GameObject.Find("Driver") != null);
                    Log("tent games and the cat", "slots " + slotOk + ", blackjack " + bjOk + ", cat " + (catT != null) + ", solid props " + solid, slotOk && bjOk && catT != null && solid > 10);
                    gm.Teleport(Lobby.Origin + Vector3.up * 0.1f, local.transform.rotation);
                }
                Log("camp stations", FindObjectsByType<Anvil>(FindObjectsSortMode.None).Length + " anvil, " + FindObjectsByType<DiceTable>(FindObjectsSortMode.None).Length + " dice, " +
                    FindObjectsByType<MysteryCoffer>(FindObjectsSortMode.None).Length + " coffer, " + FindObjectsByType<Gong>(FindObjectsSortMode.None).Length + " gong",
                    FindObjectsByType<Anvil>(FindObjectsSortMode.None).Length == 1 && FindObjectsByType<DiceTable>(FindObjectsSortMode.None).Length == 1 && FindObjectsByType<Gong>(FindObjectsSortMode.None).Length == 1);

                {
                    var progress = pc;
                    var motor = local.GetComponent<FirstPersonMotor>();
                    // The anvil: hand the player embers, temper twice, check ATT rose.
                    var anvil = FindObjectsByType<Anvil>(FindObjectsSortMode.None).FirstOrDefault();
                    if (anvil != null)
                    {
                        float attBefore = progress.Value(Core.StatType.ATT);
                        progress.ServerAddEmbers(Anvil.Cost(0) + Anvil.Cost(1));
                        var ap = anvil.transform.position + anvil.transform.forward * 1.6f + Vector3.up * 0.1f;
                        motor.Teleport(ap, Quaternion.LookRotation(-anvil.transform.forward));
                        yield return new WaitForSeconds(0.2f);
                        progress.TemperRpc();
                        yield return new WaitForSeconds(0.2f);
                        progress.TemperRpc();
                        yield return new WaitForSeconds(0.3f);
                        Log("anvil", "weapon +" + progress.WeaponLevel.Value + ", ATT " + attBefore + " -> " + progress.Value(Core.StatType.ATT) + ", embers left " + progress.Embers.Value,
                            progress.WeaponLevel.Value == 2 && progress.Value(Core.StatType.ATT) > attBefore);

                        // Gear: a drop meant for this player, equip it, then scrap a second.
                    var ddef = local.GetComponent<Health>().Defense;
                    var gearDrop = GearDrop.ServerSpawn(new GearItem(GearId.OssuaryPlate, 1, 0), local.OwnerClientId, local.transform.position + local.transform.forward * 1.2f);
                    yield return new WaitForSeconds(0.2f);
                    int packBefore = progress.BagCount;
                    var armorBefore = progress.Armor.Value;
                    gearDrop.UseRpc(local.NetworkObject);
                    yield return new WaitForSeconds(0.2f);
                    // An empty armour slot takes it straight on; a filled one sends it to the pack.
                    bool intoPack = armorBefore.IsEmpty ? (progress.BagCount == packBefore && progress.Armor.Value.Id == (byte)GearId.OssuaryPlate)
                        : (progress.BagCount == packBefore + 1 && progress.Armor.Value.Equals(armorBefore));
                    Log("pickup to pack", (armorBefore.IsEmpty ? "empty slot, now wearing " + Gear.Name(progress.Armor.Value) : "pack " + packBefore + " -> " + progress.BagCount + ", worn unchanged " + progress.Armor.Value.Equals(armorBefore)), intoPack);
                    progress.BagEquipRpc(progress.BagCount - 1);
                    yield return new WaitForSeconds(0.2f);
                    Log("gear equip", "armor " + Gear.Name(progress.Armor.Value) + ", DEF " + ddef + " -> " + local.GetComponent<Health>().Defense,
                        progress.Armor.Value.Kind == GearId.OssuaryPlate && local.GetComponent<Health>().Defense > ddef);
                    // Refine it at the anvil: Fine -> Masterwork, DEF rises.
                    var anvilR = FindObjectsByType<Anvil>(FindObjectsSortMode.None).FirstOrDefault();
                    if (anvilR != null)
                    {
                        var keep = local.transform.position;
                        motor.Teleport(anvilR.transform.position + anvilR.transform.forward * 1.6f + Vector3.up * 0.1f, local.transform.rotation);
                        yield return new WaitForSeconds(0.2f);
                        float defR = local.GetComponent<Health>().Defense;
                        progress.ServerAddEmbers(Gear.RefineCost(progress.Armor.Value));
                        progress.ReforgeRpc(0, 0);
                        yield return new WaitForSeconds(0.2f);
                        Log("gear refine", Gear.Name(progress.Armor.Value) + ", DEF " + defR + " -> " + local.GetComponent<Health>().Defense,
                            progress.Armor.Value.Quality == 2 && local.GetComponent<Health>().Defense > defR);
                        motor.Teleport(keep, local.transform.rotation);
                        yield return new WaitForSeconds(0.1f);
                    }
                    var anvilJ = FindObjectsByType<Anvil>(FindObjectsSortMode.None).FirstOrDefault();
                    var keepJ = local.transform.position;
                    if (anvilJ != null) motor.Teleport(anvilJ.transform.position + anvilJ.transform.forward * 1.6f + Vector3.up * 0.1f, local.transform.rotation);
                    yield return new WaitForSeconds(0.2f);
                    int embersBefore = progress.Embers.Value;
                    var junk = GearDrop.ServerSpawn(new GearItem(GearId.Bloodstone, 0, 0), local.OwnerClientId, local.transform.position + local.transform.forward * 1.2f);
                    yield return new WaitForSeconds(0.2f);
                    junk.ScrapRpc(local.NetworkObject);
                    yield return new WaitForSeconds(0.2f);
                    Log("gear scrap", "embers " + embersBefore + " -> " + progress.Embers.Value, progress.Embers.Value > embersBefore);
                    {
                        progress.ServerStow(new GearItem(GearId.LuckyKnucklebone, 0, 0));
                        yield return new WaitForSeconds(0.1f);
                        int e0 = progress.Embers.Value, n0 = progress.BagCount;
                        progress.BagScrapRpc(n0 - 1);
                        yield return new WaitForSeconds(0.2f);
                        Log("scrap at the anvil", "pack " + n0 + " -> " + progress.BagCount + ", embers " + e0 + " -> " + progress.Embers.Value,
                            progress.BagCount == n0 - 1 && progress.Embers.Value > e0);
                    }
                    motor.Teleport(keepJ, local.transform.rotation);
                    yield return new WaitForSeconds(0.1f);

                    // Camp trade is split by craft: each vendor sells only its own share.
                    var mStock = Shop.For(local.OwnerClientId, Shop.Vendor.Merchant);
                    var sStock = Shop.For(local.OwnerClientId, Shop.Vendor.Smith);
                    var hStock = Shop.For(local.OwnerClientId, Shop.Vendor.Shrine);
                    var yStock = Shop.For(local.OwnerClientId, Shop.Vendor.Runewright);
                    bool split = !mStock.Any(it => it.Kind == Shop.ItemKind.Relic || (it.Kind == Shop.ItemKind.Gear && Gear.SlotOf(it.Gear.Kind) != GearSlot.Charm) || it.Kind == Shop.ItemKind.Sigil || it.Kind == Shop.ItemKind.Embers)
                        && sStock.Any(it => it.Kind == Shop.ItemKind.Gear) && !sStock.Any(it => it.Kind == Shop.ItemKind.Sigil) && sStock.Any(it => it.Kind == Shop.ItemKind.Embers)
                        && yStock.Any(it => it.Kind == Shop.ItemKind.Sigil)
                        && hStock.Count(it => it.Kind == Shop.ItemKind.Relic) == 2 && hStock.Any(it => it.Name == "Holy Water") && !mStock.Any(it => it.Name == "Holy Water");
                    Log("vendors", "merchant " + mStock.Length + ", smith " + sStock.Length + ", shrine " + hStock.Length + ", runewright " + yStock.Length + " items", split);
                    {
                        int pendants = mStock.Count(it => it.Kind == Shop.ItemKind.Gear && Gear.SlotOf(it.Gear.Kind) == GearSlot.Charm);
                        bool smithArmour = sStock.Where(it => it.Kind == Shop.ItemKind.Gear).All(it => Gear.SlotOf(it.Gear.Kind) == GearSlot.Armor);
                        Log("pendants", "Silas sells " + pendants + " pendants, Hobb's gear is armour " + smithArmour, pendants == 2 && smithArmour);
                    }

                    // Strike Silas: he takes no harm, holds a grudge, and charges for it until amends are paid.
                    var silas = FindObjectsByType<CampFolk>(FindObjectsSortMode.None).FirstOrDefault(f => f.Who.Value == (byte)FolkId.Silas);
                    if (silas != null)
                    {
                        int potionBefore = mStock[0].Price;
                        var silasHp = silas.GetComponent<Health>();
                        silasHp.ServerDamage(30f, local.transform.position, local.NetworkObjectId);
                        yield return new WaitForSeconds(0.3f);
                        int grudge = progress.Grudge(FolkId.Silas);
                        int potionAfter = Shop.For(local.OwnerClientId, Shop.Vendor.Merchant)[0].Price;
                        bool unhurt = silasHp.Current.Value >= silasHp.Max.Value;
                        motor.Teleport(Lobby.MerchantSpot + Vector3.right * 2.4f + Vector3.up * 0.1f, local.transform.rotation);
                        yield return new WaitForSeconds(0.2f);
                        progress.ServerAddGold(200);
                        progress.AmendsRpc(Shop.Vendor.Merchant);
                        yield return new WaitForSeconds(0.3f);
                        Log("struck folk", "grudge " + grudge + ", potion " + potionBefore + " -> " + potionAfter + ", unhurt " + unhurt + ", after amends grudge " + progress.Grudge(FolkId.Silas),
                            grudge == 1 && potionAfter > potionBefore && unhurt && progress.Grudge(FolkId.Silas) == 0);
                    }
                    else Log("struck folk", "no camp folk spawned", false);

                    // The training yard: a drill raises a stat and the next costs more.
                    motor.Teleport(Lobby.YardSpot + Vector3.right * 1.6f + Vector3.up * 0.1f, local.transform.rotation);
                    yield return new WaitForSeconds(0.2f);
                    progress.ServerAddGold(400);
                    var yard = Shop.For(local.OwnerClientId, Shop.Vendor.Yard);
                    int drillIdx = System.Array.FindIndex(yard, it => it.Kind == Shop.ItemKind.Drill && it.Stat == Core.StatType.ATT);
                    if (drillIdx >= 0)
                    {
                        float drillAtt = progress.Value(Core.StatType.ATT);
                        int priceBefore = yard[drillIdx].Price;
                        progress.BuyRpc(Shop.Vendor.Yard, drillIdx);
                        yield return new WaitForSeconds(0.3f);
                        int priceAfter = Shop.For(local.OwnerClientId, Shop.Vendor.Yard)[drillIdx].Price;
                        Log("drills", "ATT " + drillAtt + " -> " + progress.Value(Core.StatType.ATT) + ", price " + priceBefore + " -> " + priceAfter,
                            progress.Value(Core.StatType.ATT) > drillAtt && priceAfter > priceBefore);
                    }
                    else Log("drills", "no drill board at camp", false);

                    // Corwin's smelling salts: go down, come round on your own.
                    // Corwin answers from either side of his carriage.
                    bool leftReach = Shop.InReach(Shop.Vendor.Carriage, Lobby.CarriageAt + new Vector3(-2.1f, 0f, 2.2f));
                    bool rightReach = Shop.InReach(Shop.Vendor.Carriage, Lobby.CarriageAt + new Vector3(2.1f, 0f, 2.6f));
                    bool notFromSeats = !Shop.InReach(Shop.Vendor.Carriage, CarriageRide.Instance != null ? CarriageRide.Instance.SeatGround(1) : Lobby.CarriageAt);
                    Log("corwin reach", "left " + leftReach + ", right " + rightReach + ", not from the seats " + notFromSeats, leftReach && rightReach && notFromSeats);
                    motor.Teleport(Lobby.CarriageAt + new Vector3(2.2f, 0.1f, 2.2f), local.transform.rotation);
                    yield return new WaitForSeconds(0.2f);
                    progress.ServerAddGold(400);
                    var cart = Shop.For(local.OwnerClientId, Shop.Vendor.Carriage);
                    int saltIdx = System.Array.FindIndex(cart, it => it.Kind == Shop.ItemKind.Salts);
                    if (saltIdx >= 0)
                    {
                        progress.BuyRpc(Shop.Vendor.Carriage, saltIdx);
                        yield return new WaitForSeconds(0.3f);
                        bool hadSalts = progress.Salts.Value;
                        var lifeS = local.GetComponent<PlayerLife>();
                        var hS = local.GetComponent<Health>();
                        for (int k = 0; k < 3 && !lifeS.Downed.Value; k++) { hS.ServerDamage(hS.Max.Value * 5f, local.transform.position + Vector3.forward); yield return new WaitForSeconds(0.2f); }
                        bool wentDown = lifeS.Downed.Value;
                        for (float t = 0f; t < 7f && lifeS.Downed.Value; t += 0.2f) yield return new WaitForSeconds(0.2f);
                        Log("smelling salts", "bought " + hadSalts + ", went down " + wentDown + ", up again " + !lifeS.Downed.Value + " at " + Mathf.RoundToInt(hS.Fraction * 100f) + "%, used " + !progress.Salts.Value,
                            hadSalts && wentDown && !lifeS.Downed.Value && !progress.Salts.Value);
                        hS.ServerSetFraction(1f);
                    }
                    else Log("smelling salts", "Corwin sells none", false);

                    // Brother Aldric's paid prayer: bought at the shrine, it catches one killing blow.
                    motor.Teleport(Lobby.ShrineSpot + Vector3.left * 2.4f + Vector3.up * 0.1f, local.transform.rotation);
                    yield return new WaitForSeconds(0.2f);
                    progress.ServerAddGold(300);
                    var shrineStock = Shop.For(local.OwnerClientId, Shop.Vendor.Shrine);
                    int prayerIdx = System.Array.FindIndex(shrineStock, it => it.Kind == Shop.ItemKind.Prayer);
                    if (prayerIdx >= 0)
                    {
                        progress.BuyRpc(Shop.Vendor.Shrine, prayerIdx);
                        yield return new WaitForSeconds(0.3f);
                        bool prayed = progress.PrayedFor.Value;
                        var ph = local.GetComponent<Health>();
                        // Repeat: a whisker's evade or a leftover shield can soak the first blow before the prayer sees it.
                        int blows = 0;
                        for (; blows < 3 && progress.PrayedFor.Value; blows++)
                        {
                            ph.ServerSetFraction(1f);
                            ph.ServerDamage(ph.Max.Value * 5f, local.transform.position + Vector3.forward);
                            yield return new WaitForSeconds(0.2f);
                        }
                        bool caught = ph.Current.Value >= 0.5f && !local.GetComponent<PlayerLife>().Downed.Value && !progress.PrayedFor.Value;
                        Log("paid prayer", "bought " + prayed + ", killing blow left " + ph.Current.Value.ToString("F0") + " hp, spent " + !progress.PrayedFor.Value +
                            " (blows " + blows + ", shield " + ph.Shield.Value.ToString("F0") + ", whisker " + progress.CatWhisker.Value + ")", prayed && caught);
                        ph.ServerSetFraction(1f);
                    }
                    else Log("paid prayer", "not on sale at the shrine", false);

                    // The smith: this player's own gear offer lands at their feet.
                    var mpos = Lobby.AnvilSpot;
                    motor.Teleport(mpos + Vector3.left * 2.4f + Vector3.up * 0.1f, local.transform.rotation);
                    yield return new WaitForSeconds(0.2f);
                    progress.ServerAddGold(500);
                    int goldNow = progress.Gold.Value;
                    var stock = sStock;
                    int gearIndex = System.Array.FindIndex(stock, it => it.Kind == Shop.ItemKind.Gear);
                    int dropsBeforeBuy = FindObjectsByType<GearDrop>(FindObjectsSortMode.None).Length;
                    progress.BuyRpc(Shop.Vendor.Smith, gearIndex);
                    yield return new WaitForSeconds(0.3f);
                    Log("shop gear", "bought " + stock[gearIndex].Name + " for " + stock[gearIndex].Price + ", gold " + goldNow + " -> " + progress.Gold.Value + ", sold-out: " + progress.CannotBuyReason(Shop.Vendor.Smith, gearIndex),
                        progress.Gold.Value == goldNow - stock[gearIndex].Price && FindObjectsByType<GearDrop>(FindObjectsSortMode.None).Length == dropsBeforeBuy + 1 && progress.CannotBuyReason(Shop.Vendor.Smith, gearIndex) == "Sold");
                    var anvilAgain = FindObjectsByType<Anvil>(FindObjectsSortMode.None).First();
                    motor.Teleport(anvilAgain.transform.position + anvilAgain.transform.forward * 1.6f + Vector3.up * 0.1f, local.transform.rotation);
                    yield return new WaitForSeconds(0.2f);

                    // Runes and sigils are Ysolde's craft: walk to her stones.
                    motor.Teleport(World.Lobby.RunewrightSpot + Vector3.back * 1.2f + Vector3.up * 0.1f, local.transform.rotation);
                    yield return new WaitForSeconds(0.2f);
                    var stallHere = local.GetComponent<UI.GameHud>().DebugStallHere();
                    Log("ysolde reachable", "F at her stones opens " + (stallHere.HasValue ? stallHere.Value.ToString() : "nothing"), stallHere == Shop.Vendor.Runewright);

                    // The Gutted Lantern: a horn of mead makes you Tipsy (and costs its price); the band takes a request.
                    var barkeep = FindAnyObjectByType<TavernKeep>();
                    var band = FindAnyObjectByType<TavernBand>();
                    if (barkeep != null && band != null)
                    {
                        motor.Teleport(barkeep.transform.position + barkeep.transform.forward * 1.2f + Vector3.up * 0.1f, Quaternion.LookRotation(-barkeep.transform.forward));
                        yield return new WaitForSeconds(0.2f);
                        progress.ServerAddGold(100);
                        int g0 = progress.Gold.Value;
                        var deb = local.GetComponent<PlayerDebuffs>();
                        barkeep.UseRpc(local.NetworkObject);
                        yield return new WaitForSeconds(0.7f);
                        int drunk1 = deb.Drunk;
                        barkeep.UseRpc(local.NetworkObject);
                        yield return new WaitForSeconds(0.7f);
                        bool inside = Lobby.InTavern(barkeep.transform.position);
                        Log("tavern mead", "two horns: drunk " + drunk1 + " then " + deb.Drunk + " (" + PlayerDebuffs.DrunkName(deb.Drunk) + "), paid " + (g0 - progress.Gold.Value) + ", damage x" + deb.DamageMultiplier.ToString("F2") + ", bar inside the tavern " + inside,
                            drunk1 == 1 && deb.Drunk == 2 && g0 - progress.Gold.Value == 2 * TavernKeep.MeadPrice && deb.DamageMultiplier > 1.05f && inside);
                        deb.DrunkLevel.Value = 0;
                        int song0 = band.Song.Value;
                        motor.Teleport(band.transform.position + band.transform.forward * 1.4f + Vector3.up * 0.1f, Quaternion.LookRotation(-band.transform.forward));
                        yield return new WaitForSeconds(0.2f);
                        g0 = progress.Gold.Value;
                        band.UseRpc(local.NetworkObject);
                        yield return new WaitForSeconds(0.3f);
                        Log("tavern band", "song " + song0 + " -> " + band.Song.Value + " (\"" + TavernBand.SongName(band.Song.Value) + "\"), paid " + (g0 - progress.Gold.Value),
                            band.Song.Value == (song0 + 1) % Art.TavernMusic.Songs.Length && g0 - progress.Gold.Value == TavernBand.Fee);
                        // Every tune renders: long enough, never silent, never clipping.
                        string songs = ""; bool allOk = true;
                        for (int si = 0; si < Art.TavernMusic.Songs.Length; si++)
                        {
                            var data = Art.TavernMusic.RenderNow(si);
                            float peak = 0f, rms = 0f;
                            if (data != null) { foreach (var v in data) { peak = Mathf.Max(peak, Mathf.Abs(v)); rms += v * v; } rms = Mathf.Sqrt(rms / data.Length); }
                            float secs = data != null ? data.Length / (float)Art.TavernMusic.Rate : 0f;
                            songs += Art.TavernMusic.Songs[si].Name + " " + secs.ToString("F0") + " s rms " + rms.ToString("F2") + "; ";
                            allOk &= data != null && secs > 20f && peak <= 1f && rms > 0.05f;
                        }
                        Log("tavern songs", songs, allOk);
                    }
                    else Log("tavern", "no barkeep or band at camp", false);
                    // Back to Ysolde's stones for the rune checks that follow.
                    motor.Teleport(World.Lobby.RunewrightSpot + Vector3.back * 1.2f + Vector3.up * 0.1f, local.transform.rotation);
                    yield return new WaitForSeconds(0.2f);

                    // The emote wheel: a gesture chosen on it plays on your body for everyone.
                    var emo = local.GetComponent<Emotes>();
                    emo.DebugEmote(Art.Emote.Cheer);
                    yield return new WaitForSeconds(0.3f);
                    Log("emote wheel", "cheer on the rig: " + (local.Rig != null ? local.Rig.EmoteNow.ToString() : "no rig") + ", wheel slices " + Emotes.Wheel.Length,
                        local.Rig != null && local.Rig.EmoteNow == Art.Emote.Cheer && Emotes.Wheel.Length == 12);
                    // A gamepad: a virtual pad pressing A jumps (Jump held), RT attacks, and the hints turn to pad buttons.
                    {
                        var vpad = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Gamepad>();
                        UnityEngine.InputSystem.InputSystem.QueueStateEvent(vpad, new UnityEngine.InputSystem.LowLevel.GamepadState { rightTrigger = 1f }.WithButton(UnityEngine.InputSystem.LowLevel.GamepadButton.South));
                        yield return null;
                        yield return null;
                        bool jumpHeld = UI.Binds.Held(UI.Bind.Jump), attack = UI.Binds.PrimaryHeld;
                        UI.Binds.Tick();
                        string hint = UI.Binds.Show("F");
                        // The pause menu on a pad: down to GUIDE, A opens it, B backs out, B again resumes.
                        var pm = FindFirstObjectByType<UI.PauseMenu>();
                        bool padMenu = false;
                        if (pm != null)
                        {
                            UnityEngine.InputSystem.InputSystem.QueueStateEvent(vpad, new UnityEngine.InputSystem.LowLevel.GamepadState());
                            yield return null;
                            UI.PauseMenu.SetOpen(true);
                            yield return null;
                            UnityEngine.InputSystem.InputSystem.QueueStateEvent(vpad, new UnityEngine.InputSystem.LowLevel.GamepadState().WithButton(UnityEngine.InputSystem.LowLevel.GamepadButton.DpadDown));
                            yield return null; yield return null;
                            UnityEngine.InputSystem.InputSystem.QueueStateEvent(vpad, new UnityEngine.InputSystem.LowLevel.GamepadState());
                            yield return null;
                            UnityEngine.InputSystem.InputSystem.QueueStateEvent(vpad, new UnityEngine.InputSystem.LowLevel.GamepadState().WithButton(UnityEngine.InputSystem.LowLevel.GamepadButton.South));
                            yield return null; yield return null;
                            padMenu = pm.GuideShowing;
                            UnityEngine.InputSystem.InputSystem.QueueStateEvent(vpad, new UnityEngine.InputSystem.LowLevel.GamepadState());
                            yield return null;
                            UI.PauseMenu.SetOpen(false);
                        }
                        UnityEngine.InputSystem.InputSystem.RemoveDevice(vpad);
                        yield return null;
                        UI.Binds.Tick();
                        Log("gamepad", "A = jump " + jumpHeld + ", RT = attack " + attack + ", F prompt shows '" + hint + "', pause menu down+A opens the guide " + padMenu, jumpHeld && attack && hint == "X" && padMenu);
                    }
                    // The daily descent: the same seed for a day and depth, a new one for the next depth or day.
                    Log("daily seeds", "today d3 " + DungeonDirector.DailySeed(20261020, 3) + " again " + DungeonDirector.DailySeed(20261020, 3) + ", d4 " + DungeonDirector.DailySeed(20261020, 4) + ", tomorrow " + DungeonDirector.DailySeed(20261021, 3),
                        DungeonDirector.DailySeed(20261020, 3) == DungeonDirector.DailySeed(20261020, 3) && DungeonDirector.DailySeed(20261020, 3) != DungeonDirector.DailySeed(20261020, 4)
                        && DungeonDirector.DailySeed(20261020, 3) != DungeonDirector.DailySeed(20261021, 3) && DungeonDirector.DailySeed(20261020, 3) > 0);
                    // Colour-blind telegraphs: red and green move apart; blue stays (a test override, the player's setting untouched).
                    {
                        Telegraph.ForceColorBlind = true;
                        var red = Telegraph.Accessible(Telegraph.Harm); var green = Telegraph.Accessible(Telegraph.Poison); var blue = Telegraph.Accessible(Telegraph.Friendly);
                        Telegraph.ForceColorBlind = false;
                        Log("colour-blind telegraphs", "harm " + red + ", poison " + green, red.b > 0.6f && green.b < 0.3f && green.r > 0.9f && blue == Telegraph.Friendly);
                    }
                    // The climbing rope: not on the first floor, not without one.
                    {
                        var rp = local.GetComponent<PlayerProgress>();
                        bool noRope = !rp.CanUseRope;
                        Log("climbing rope", "unusable without a rope or at camp " + noRope + ", retreat allowed here " + DungeonDirector.Instance.CanRetreat, noRope);
                    }
                    // Torment stays shut until a descent is finished (tests have no wins).
                    {
                        var tdd = DungeonDirector.Instance;
                        byte before = tdd.DifficultyIndex.Value;
                        tdd.SetDifficultyRpc((byte)Difficulty.TormentIndex);
                        bool shut = tdd.DifficultyIndex.Value < Difficulty.TormentIndex;
                        tdd.SetDifficultyRpc(before);
                        Log("torment locked", "asked for Torment, got " + Difficulty.Settings[tdd.DifficultyIndex.Value].Name + " first; locked " + shut, shut || Difficulty.TormentUnlocked);
                    }
                    // Records stay out of tests: nothing is read or written.
                    Log("records", "test run beats nothing: " + (Progression.Records.SubmitRun(12345.0, 99, 999, 9999, true).Count == 0) + ", slab reads zeros " + (Progression.Records.Deepest == 0),
                        Progression.Records.SubmitRun(12345.0, 99, 999, 9999, true).Count == 0 && Progression.Records.Deepest == 0 && !Progression.Records.SubmitFloorTime(60f));
                    // Key rebinding: moving Kick to K, then onto Q, swaps Ability 1 onto K; hints follow; reset restores.
                    UI.Binds.Set(UI.Bind.Kick, UnityEngine.InputSystem.Key.K);
                    bool movedK = UI.Binds.KeyOf(UI.Bind.Kick) == UnityEngine.InputSystem.Key.K && UI.Binds.Show("V") == "K";
                    UI.Binds.Set(UI.Bind.Kick, UnityEngine.InputSystem.Key.Q);
                    bool swapped = UI.Binds.KeyOf(UI.Bind.AbilityQ) == UnityEngine.InputSystem.Key.K;
                    string rekeyed = UI.Binds.Rekey("Q  Blink  ·  V kick");
                    bool prose = UI.Binds.Rekey("KICK it (V) or bash") == "KICK it (Q) or bash";
                    UI.Binds.ResetAll();
                    Log("key rebinding", "kick K " + movedK + ", swap onto Q " + swapped + ", hint '" + rekeyed + "', reset " + UI.Binds.Label(UI.Bind.Kick) + "/" + UI.Binds.Label(UI.Bind.AbilityQ),
                        movedK && swapped && prose && rekeyed == "K  Blink  ·  Q kick" && !UI.Binds.AnyChanged);
                    // Runes: one in the bag, set it into the weapon's first socket.
                    int bagBefore = progress.RuneBagCount;
                    progress.ServerAddRune(Rune.Frost);
                    yield return new WaitForSeconds(0.1f);
                    progress.SocketRuneRpc((byte)Rune.Frost);
                    yield return new WaitForSeconds(0.2f);
                    Log("rune socket", "socket 1: " + Runes.Name(progress.Socketed(0)) + ", bag " + progress.RuneBagCount,
                        progress.Socketed(0) == Rune.Frost && progress.RuneBagCount == bagBefore);

                    // Melding: three Ember runes and the embers make one Ember II.
                    {
                        for (int k = 0; k < 3; k++) progress.ServerAddRune(Rune.Ember);
                        progress.ServerAddEmbers(Runes.MeldCost(0));
                        yield return new WaitForSeconds(0.1f);
                        int plain = progress.RuneCount(Rune.Ember), tier2Before = progress.RuneCount(Runes.Make(Rune.Ember, 1));
                        progress.MeldRunesRpc((byte)Rune.Ember);
                        yield return new WaitForSeconds(0.2f);
                        int tier2 = progress.RuneCount(Runes.Make(Rune.Ember, 1));
                        Log("rune meld", "Ember " + plain + " -> " + progress.RuneCount(Rune.Ember) + ", " + Runes.Name(Runes.Make(Rune.Ember, 1)) + " " + tier2Before + " -> " + tier2,
                            tier2 == tier2Before + 1 && progress.RuneCount(Rune.Ember) == plain - 3);
                    }

                    // Sigils: rank Q twice at the stones; the cooldown scale drops at rank 3.
                    float cdBefore = progress.CooldownFor(Classes.AbilitySlot.Q);
                    int sigilsBefore = progress.Sigils.Value;
                    progress.ServerAddSigil();
                    progress.ServerAddSigil();
                    progress.EmpowerRpc(0);
                    yield return new WaitForSeconds(0.2f);
                    progress.EmpowerRpc(0);
                    yield return new WaitForSeconds(0.2f);
                    Log("ability rank", "Q rank " + progress.RankQ.Value + ", sigils left " + progress.Sigils.Value + ", Q cooldown x" + cdBefore + " -> x" + progress.CooldownFor(Classes.AbilitySlot.Q),
                        progress.RankQ.Value == 3 && progress.Sigils.Value == sigilsBefore && progress.CooldownFor(Classes.AbilitySlot.Q) < cdBefore);

                    // Boss legendary: the Hollow Crown lifts both abilities a rank.
                    int qBefore = progress.Rank(Classes.AbilitySlot.Q), eBefore = progress.Rank(Classes.AbilitySlot.E);
                    // Each Warden has its own legendary now (a Warden used to fall through to the Hollow King's crown).
                    Log("warden legendaries", "names " + Gear.BaseName(GearId.MarrowgutsRib) + " / " + Gear.BaseName(GearId.GorrsHide) + ", slots " + Gear.SlotOf(GearId.AldousOath),
                        Gear.BaseName(GearId.ChoirmastersBaton) != "Nothing" && Gear.SlotOf(GearId.DrownedKnightsMail) == GearSlot.Armor && Gear.Effect(GearId.MarrowgutsRib).Length > 0);
                    {
                        // The Panoply (2026-11-07): its floors, and Martyr's Shell hitting harder when hurt.
                        var hpM = local.GetComponent<Health>();
                        float f0 = hpM.Fraction;
                        var keep = progress.ServerEquip(new GearItem(GearId.MartyrsShell, 3, 2));
                        hpM.ServerSetFraction(1f);
                        float full = progress.AttackMultiplier;
                        hpM.ServerSetFraction(0.2f);
                        float low = progress.AttackMultiplier;
                        hpM.ServerSetFraction(Mathf.Max(0.5f, f0));
                        if (keep.IsEmpty) progress.ServerUnequip(GearSlot.Armor); else progress.ServerEquip(keep);
                        bool floors = DungeonDirector.PanoplyFloor(1) && DungeonDirector.PanoplyFloor(4) && !DungeonDirector.PanoplyFloor(0) && !DungeonDirector.PanoplyFloor(9);
                        Log("panoply armour", "floors " + floors + ", martyr x" + (low / full).ToString("F2") + ", pieces " + Gear.PanoplyPieces.Length + ", " + Gear.BaseName(GearId.SunwardPlate),
                            floors && low > full * 1.25f && Gear.PanoplyPieces.Length == 4 && Gear.SlotOf(GearId.Rimecloak) == GearSlot.Armor);
                    }
                    {
                        // Hide of Gorr: poison cannot take its wearer.
                        var oldArmor = progress.ServerEquip(new GearItem(GearId.GorrsHide, 3, 2));
                        var gdeb = progress.GetComponent<PlayerDebuffs>();
                        gdeb.ServerCleanse();
                        gdeb.ServerPoison(3f, 5f);
                        yield return null;
                        bool immune = !gdeb.Poisoned;
                        if (!oldArmor.IsEmpty) progress.ServerEquip(oldArmor);
                        gdeb.ServerPoison(3f, 5f);
                        yield return null;
                        bool back = gdeb.Poisoned;
                        gdeb.ServerCleanse();
                        Log("hide of gorr", "immune while worn " + immune + ", poisoned once off " + back, immune && (oldArmor.IsEmpty || back));
                    }
                    var oldCharm = progress.ServerEquip(new GearItem(GearId.HollowCrown, 3, 2));
                    Log("legendary crown", "Q " + qBefore + " -> " + progress.Rank(Classes.AbilitySlot.Q) + ", E " + eBefore + " -> " + progress.Rank(Classes.AbilitySlot.E),
                        progress.Rank(Classes.AbilitySlot.E) == Mathf.Min(3, eBefore + 1));
                    if (!oldCharm.IsEmpty) progress.ServerEquip(oldCharm);

                    // +3 needs a path; choose Cleave and check the swing now reaches wider. Tempering is back at the anvil.
                    motor.Teleport(anvilAgain.transform.position + anvilAgain.transform.forward * 1.6f + Vector3.up * 0.1f, local.transform.rotation);
                    yield return new WaitForSeconds(0.2f);
                        progress.ServerAddEmbers(Anvil.Cost(2));
                        progress.TemperRpc();
                        yield return new WaitForSeconds(0.2f);
                        bool gated = progress.WeaponLevel.Value == 2;
                        progress.ChoosePathRpc(1);
                        yield return new WaitForSeconds(0.2f);
                        progress.TemperRpc();
                        yield return new WaitForSeconds(0.2f);
                        Log("weapon path", "gated before choosing: " + gated + ", path " + progress.WeaponPath.Value + ", weapon +" + progress.WeaponLevel.Value,
                            gated && progress.WeaponPath.Value == 1 && progress.WeaponLevel.Value == 3);
                    }
                    else Log("anvil", "none at camp", false);

                    // The gambling tent: a roll of the bones moves gold one way or the other (or not).
                    var dice = FindObjectsByType<DiceTable>(FindObjectsSortMode.None).FirstOrDefault();
                    if (dice != null)
                    {
                        progress.ServerAddGold(200);
                        motor.Teleport(dice.transform.position + dice.transform.forward * 1.4f + Vector3.up * 0.1f, local.transform.rotation);
                        yield return new WaitForSeconds(0.2f);
                        int g0 = progress.Gold.Value;
                        dice.UseRpc(local.NetworkObject);
                        yield return new WaitForSeconds(0.3f);
                        int stake = DiceTable.Stake(g0);
                        int delta = progress.Gold.Value - g0;
                        Log("dice", "gold " + g0 + " -> " + progress.Gold.Value + " (stake " + stake + ")", delta == stake || delta == 0 || delta == -stake);
                    }
                    // The coffer: pay and something appears.
                    var coffer = FindObjectsByType<MysteryCoffer>(FindObjectsSortMode.None).FirstOrDefault();
                    if (coffer != null)
                    {
                        progress.ServerAddGold(MysteryCoffer.Price);
                        motor.Teleport(coffer.transform.position + coffer.transform.forward * 1.6f + Vector3.up * 0.1f, local.transform.rotation);
                        yield return new WaitForSeconds(0.2f);
                        int g1 = progress.Gold.Value;
                        int things = FindObjectsByType<LootDrop>(FindObjectsSortMode.None).Length + FindObjectsByType<GearDrop>(FindObjectsSortMode.None).Length;
                        coffer.UseRpc(local.NetworkObject);
                        yield return new WaitForSeconds(0.3f);
                        int after = FindObjectsByType<LootDrop>(FindObjectsSortMode.None).Length + FindObjectsByType<GearDrop>(FindObjectsSortMode.None).Length;
                        Log("mystery coffer", "gold " + g1 + " -> " + progress.Gold.Value + ", prizes " + things + " -> " + after, progress.Gold.Value == g1 - MysteryCoffer.Price && after > things);
                    }
                    // The shrine: two blessings on offer; buy the second, and it is on you.
                    var shrineT = FindObjectsByType<Shrine>(FindObjectsSortMode.None).FirstOrDefault();
                    if (shrineT != null)
                    {
                        progress.ServerAddGold(Blessings.Price(DungeonDirector.GearDepth) * 2);
                        motor.Teleport(Lobby.ShrineSpot + shrineT.transform.forward * 1.6f + Vector3.up * 0.1f, local.transform.rotation);
                        yield return new WaitForSeconds(0.2f);
                        var bStock = Shop.For(local.OwnerClientId, Shop.Vendor.Shrine);
                        int offers = bStock.Count(it => it.Kind == Shop.ItemKind.Blessing);
                        int bIdx = System.Array.FindLastIndex(bStock, it => it.Kind == Shop.ItemKind.Blessing);
                        var want = bIdx >= 0 ? (Blessing)(int)bStock[bIdx].Amount : Blessing.None;
                        if (bIdx >= 0) progress.BuyRpc(Shop.Vendor.Shrine, bIdx);
                        yield return new WaitForSeconds(0.2f);
                        Log("shrine", offers + " blessings on offer, bought " + Blessings.Name(want) + ", wearing " + Blessings.Name((Blessing)progress.ActiveBlessing.Value),
                            offers == 2 && progress.ActiveBlessing.Value == (byte)want && want != Blessing.None);
                    }
                    // The range: a hit on a target counts for the shooter.
                    var targetT = FindObjectsByType<RangeTarget>(FindObjectsSortMode.None).FirstOrDefault();
                    if (targetT != null)
                    {
                        RangeTarget.Tally.TryGetValue(local.name, out int before);
                        targetT.GetComponent<Health>().ServerDamage(10f, local.transform.position, local.NetworkObjectId);
                        yield return new WaitForSeconds(0.1f);
                        RangeTarget.Tally.TryGetValue(local.name, out int after);
                        Log("archery range", "hits " + before + " -> " + after, after == before + 1);
                    }
                    // The Test of Might: a hit sets the record and the gong takes no damage.
                    var gongT = Gong.Instance;
                    if (gongT != null)
                    {
                        float hpG = gongT.GetComponent<Health>().Current.Value;
                        gongT.GetComponent<Health>().ServerDamage(77f, gongT.transform.position - gongT.transform.forward, local.NetworkObjectId);
                        yield return new WaitForSeconds(0.2f);
                        Log("test of might", "record " + gongT.Record.Value + " by " + gongT.RecordHolder.Value + ", hp unchanged " + (gongT.GetComponent<Health>().Current.Value == hpG),
                            gongT.Record.Value >= 77f && gongT.GetComponent<Health>().Current.Value == hpG);
                    }
                }

                // Headshots: a shot at the head each creature shows counts as a headshot, and one well above it does not.
                {
                    var heads = new System.Collections.Generic.List<string>();
                    bool allOk = true;
                    foreach (var kind in new[] { "skeleton", "brute", "wraith", "eye", "piper", "ghoul", "hexer" })
                    {
                        var e = DungeonDirector.Instance.ServerDebugSpawn(kind, local.transform.position + local.transform.forward * 7f + local.transform.right * 3f, false);
                        if (e == null) continue;
                        yield return new WaitForSeconds(0.15f);
                        e.ServerStun(3f);
                        var rig = e.GetComponentInChildren<Deepbound.Art.HumanoidRig>();
                        var eh = e.GetComponent<Health>();
                        if (rig == null || rig.Head == null) { e.NetworkObject.Despawn(); continue; }
                        // Where the head is drawn: the middle of what hangs on the head pivot.
                        var rs = rig.Head.GetComponentsInChildren<Renderer>().Where(r => r.transform.parent == rig.Head).ToArray();
                        var drawn = rs.Length > 0 ? rs.Select(r => r.bounds).Aggregate((a, b) => { a.Encapsulate(b); return a; }) : new Bounds(rig.Head.position, Vector3.one * 0.2f);
                        var eye = drawn.center - e.transform.forward * -6f + Vector3.up * 0.3f;
                        var zone = HitZones.Along(eh, eye, drawn.center + (drawn.center - eye).normalized * 0.5f, out _);
                        var over = drawn.center + Vector3.up * (drawn.extents.y + 0.45f);
                        var zoneOver = HitZones.Along(eh, eye, over + (over - eye).normalized * 0.5f, out _);
                        bool ok = zone != HitZone.Body && zoneOver == HitZone.Body;
                        allOk &= ok;
                        heads.Add(kind + (ok ? " ok" : " " + zone + "/" + zoneOver));
                        e.NetworkObject.Despawn();
                    }
                    Log("headshot hitboxes", string.Join(", ", heads), allOk && heads.Count >= 5);
                }

                // Joining by address: ports, pasted junk and host names.
                {
                    bool a1 = NetworkLauncher.ParseAddress(" 203.0.113.7:7790 ", out var ip1, out var p1, out _);
                    bool a2 = NetworkLauncher.ParseAddress("http://192.168.1.20/", out var ip2, out var p2, out _);
                    bool a3 = NetworkLauncher.ParseAddress("localhost", out var ip3, out _, out _);
                    bool a4 = !NetworkLauncher.ParseAddress("1.2.3.4:99999", out _, out _, out var why4);
                    Log("join address parsing", ip1 + ":" + p1 + ", " + ip2 + ":" + p2 + ", localhost -> " + ip3 + ", bad port says '" + why4 + "', build " + NetworkLauncher.BuildId,
                        a1 && ip1 == "203.0.113.7" && p1 == 7790 && a2 && ip2 == "192.168.1.20" && p2 == 7777 && a3 && ip3 == "127.0.0.1" && a4);
                }

                // The tailor: wearing a dye rebuilds the body with it.
                var npT = local.GetComponent<NetworkPlayer>();
                var rigBefore = npT.Rig;
                npT.CloakDye.Value = 3;
                yield return new WaitForSeconds(0.3f);
                var cloakR = npT.Rig != null ? npT.Rig.GetComponentsInChildren<Renderer>().FirstOrDefault(r => r.name == "Cloak") : null;
                Log("tailor dye", "dye " + npT.CloakDye.Value + ", rebuilt " + (npT.Rig != rigBefore) + ", cloak " + (cloakR != null) + ", renown for a clear " +
                    Progression.Cosmetics.RenownFor(4, 120, 30, true, 1), npT.Rig != rigBefore && cloakR != null && Progression.Cosmetics.RenownFor(4, 120, 30, true, 1) > 0);
                npT.CloakDye.Value = 0;
                // A pattern sews its motif on the cloak's back.
                int patBefore = npT.Pattern.Value;
                npT.Pattern.Value = 2;
                yield return new WaitForSeconds(0.3f);
                int patParts = npT.Rig != null ? npT.Rig.GetComponentsInChildren<Transform>(true).Count(t => t.name.StartsWith("Pattern")) : 0;
                var mergedOk = npT.Rig != null && npT.Rig.GetComponentsInChildren<Renderer>().Length > 0;
                Log("cloak pattern", "pattern " + Progression.Cosmetics.Patterns[npT.Pattern.Value].Name + ", motif pivots " + patParts, patParts >= 2 && mergedOk);
                npT.Pattern.Value = (byte)patBefore;
                npT.Aura.Value = 2;
                yield return new WaitForSeconds(0.3f);
                Log("tailor aura", "aura " + npT.Aura.Value + " (" + Progression.Cosmetics.Auras[npT.Aura.Value].Name + "), cardinal legendary " + Gear.Name(new GearItem(GearId.CardinalVestments, 3, 3)),
                    npT.Aura.Value == 2 && Progression.Cosmetics.AuraColor(2).HasValue);
                npT.Aura.Value = 0;

                // The ultimate: a full bar casts and empties; the Knight's banner protects.
                var ultT = local.GetComponent<Ultimates>();
                var clsT = local.GetComponent<PlayerProgress>().ClassId.Value;
                ultT.DebugCast(local.transform.position + local.transform.forward * 3f);
                yield return new WaitForSeconds(0.3f);
                Log("ultimate", Ultimates.Name(clsT) + ", charge after " + ultT.Charge.Value.ToString("F0") + ", in banner " + Ultimates.InBanner(local.transform.position),
                    ultT.Charge.Value < 1f && (clsT != Classes.ClassId.Knight || Ultimates.InBanner(local.transform.position)));

                // The Omen Stone exists at camp (the run is already under way, so it will not turn).
                Log("omen stone", FindObjectsByType<OmenStone>(FindObjectsSortMode.None).Length + " stone(s), omen " + DungeonDirector.OmenName(dc.RunOmen),
                    FindObjectsByType<OmenStone>(FindObjectsSortMode.None).Length == 1);
                // The newer omens (2026-10-27) name themselves and move their numbers.
                {
                    byte omen0 = dc.OmenValue.Value;
                    var progO = local.GetComponent<PlayerProgress>();
                    float cd0 = progO.CooldownMultiplier, price0 = progO.PriceMultiplier(Shop.Vendor.Smith);
                    dc.OmenValue.Value = (byte)Omen.Avarice;
                    bool avarice = Mathf.Abs(progO.PriceMultiplier(Shop.Vendor.Smith) / price0 - 1.5f) < 0.01f && DungeonDirector.OmenGold == 2f;
                    dc.OmenValue.Value = (byte)Omen.HushedBells;
                    bool hushed = DungeonDirector.OmenUltCharge == 1.5f;
                    dc.OmenValue.Value = (byte)Omen.QuickenedDead;
                    bool quick = DungeonDirector.OmenEmbers == 1.4f;
                    bool named = DungeonDirector.OmenText(Omen.Avarice).Length > 0 && DungeonDirector.OmenName(Omen.QuickenedDead) != "No Omen" && DungeonDirector.OmenCount == 8;
                    dc.OmenValue.Value = omen0;
                    Log("new omens", "avarice " + avarice + ", hushed bells " + hushed + ", quickened " + quick + ", named " + named + ", cd back " + (progO.CooldownMultiplier == cd0), avarice && hushed && quick && named);
                }

                // The bounty board: take a Slayer-free notice, see it ride down with the party.
                var notice = FindObjectsByType<BountyNotice>(FindObjectsSortMode.None).FirstOrDefault();
                if (notice != null)
                {
                    lobbyMotorFor(local).Teleport(notice.transform.position + notice.transform.forward * 1.4f + Vector3.up * 0.1f, Quaternion.LookRotation(-notice.transform.forward));
                    yield return new WaitForSeconds(0.3f);
                    notice.UseRpc(local.NetworkObject);
                    yield return new WaitForSeconds(0.3f);
                    Log("bounty board", FindObjectsByType<BountyNotice>(FindObjectsSortMode.None).Length + " notices, taken " + DungeonDirector.ContractName((Contract)dc.ActiveContract.Value) +
                        ", state " + (DungeonDirector.ContractStatus)dc.ContractState.Value, dc.ContractState.Value == (byte)DungeonDirector.ContractStatus.Taken);
                }

                // Board: floor two, same run.
                // Board the way players do: everyone sits on the carriage's tailgate, a countdown, the ride out.
                int seatN = 0;
                foreach (var pl in PlayerLife.All) if (pl != null) pl.Seat.Value = (sbyte)(seatN++ % CarriageRide.SeatCount);
                float boardBy = Time.time + 18f;
                bool rode = false, pinned = false;
                while (dc.Phase.Value != RunPhase.Exploring && Time.time < boardBy)
                {
                    if (CarriageRide.Instance != null && CarriageRide.Instance.Riding)
                    {
                        rode = true;
                        pinned |= Vector3.Distance(local.transform.position, CarriageRide.Instance.SeatPosition(local.GetComponent<PlayerLife>().Seat.Value >= 0 ? local.GetComponent<PlayerLife>().Seat.Value : 0)) < 0.6f;
                    }
                    yield return null;
                }
                Log("carriage ride", "seated " + seatN + ", rode out " + rode + ", rode on the seat " + pinned + ", floor started " + (dc.Phase.Value == RunPhase.Exploring), rode && dc.Phase.Value == RunPhase.Exploring);
                yield return new WaitForSeconds(1.5f);
                Log("contract live", DungeonDirector.ContractName((Contract)dc.ActiveContract.Value) + " " + (DungeonDirector.ContractStatus)dc.ContractState.Value + ", goal " + dc.ContractGoal.Value,
                    dc.ActiveContract.Value == 0 || dc.ContractState.Value == (byte)DungeonDirector.ContractStatus.Active);
                Log("next floor", "depth " + dc.Floor.Value.y + ", weapon kept +" + pc.WeaponLevel.Value, dc.Floor.Value.y == 1 && pc.WeaponLevel.Value >= 2);
                // The Drowned Vaults (act two, floor 3): their tide slows low ground while it is up.
                dc.ServerDebugStartFloor(2);
                yield return new WaitForSeconds(2f);
                Log("act two", "floor " + (dc.Floor.Value.y + 1) + " is act " + (DungeonDirector.Act(dc.Floor.Value.y) + 1) + " (" + DungeonTheme.ForDepth(dc.Floor.Value.y).Name + "), boss " +
                    (BossBrain.Active != null ? BossBrain.Active.DisplayName : "none"), DungeonDirector.Act(dc.Floor.Value.y) == 1 && BossBrain.Active is WardenBrain);
                {
                    // Hunting strikes: predicted circles walk after every standing player; a lance fan follows.
                    var hunter = BossBrain.Active;
                    var hh = local.GetComponent<Health>();
                    if (hunter != null)
                    {
                        foreach (var e in FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None)) if (!(e is BossBrain) && Vector3.Distance(e.transform.position, local.transform.position) < 20f) e.GetComponent<Health>().ServerDamage(1e6f, e.transform.position);
                        hh.ServerSetFraction(1f);
                        float h0 = hh.Current.Value;
                        int thrown0 = hunter.HuntsThrown;
                        // No shield in the way (the Wheel's Mending can leave one): this checks the strike lands.
                        hh.Shield.Value = 0f;
                        // Count the boss's blows on this player (health alone can be masked by a heal in the same second).
                        int blows = 0;
                        ulong hunterId = hunter.NetworkObjectId;
                        System.Action<Health, float, ulong> countBlows = (t, d, f) => { if (t == hh && f == hunterId) blows++; };
                        Health.ServerAnyDamaged += countBlows;
                        int hits0 = hunter.StrikeHits;
                        hunter.ServerHuntNow();
                        yield return new WaitForSeconds(1.0f);
                        Health.ServerAnyDamaged -= countBlows;
                        bool struck = blows > 0 || hh.Current.Value < h0 || hunter.StrikeHits > hits0;
                        Debug.Log("[Smoke] hunt strike hits " + (hunter.StrikeHits - hits0) + ", last dealt " + hunter.LastStrikeDealt + ", blows " + blows);
                        hh.ServerSetFraction(1f);
                        hunter.ServerHuntNow();
                        yield return new WaitForSeconds(0.2f);
                        Log("boss hunts", "hunts " + thrown0 + " -> " + hunter.HuntsThrown + ", a standing player was struck " + struck + " (strike " + Vector3.Distance(hunter.LastStrikeAt, local.transform.position).ToString("F1") + " m off, targetable " + local.GetComponent<PlayerLife>().Targetable + ", hp " + h0.ToString("F0") + " -> " + hh.Current.Value.ToString("F0") + ")" + ", tell x" + Difficulty.Tell(dc.Floor.Value.y).ToString("F2") + ", summons solo " + Difficulty.SummonCount(4, 1) + " / four " + Difficulty.SummonCount(4, 4),
                            hunter.HuntsThrown == thrown0 + 2 && struck && Difficulty.SummonCount(4, 1) < Difficulty.SummonCount(4, 4));
                        yield return new WaitForSeconds(1.4f);
                        hh.ServerSetFraction(1f);
                        // The break meter: full, the boss is held and takes half again; it recovers after BreakSeconds.
                        {
                            var bhp = hunter.GetComponent<Health>();
                            float plain = 0f, broken = 0f;
                            // Awake for the measurement (asleep, it takes nothing at all).
                            bool wasDormant = hunter.Dormant;
                            hunter.Dormant = false;
                            hunter.ServerDebugBreak();
                            yield return null;
                            bool held = hunter.InEntrance, brk = hunter.Broken.Value;
                            float mult = 1f;
                            foreach (var mod in bhp.IncomingModifiers) mult = mod(mult, Vector3.zero);
                            broken = mult;
                            yield return new WaitForSeconds(BossBrain.BreakSeconds + 0.4f);
                            mult = 1f;
                            foreach (var mod in bhp.IncomingModifiers) mult = mod(mult, Vector3.zero);
                            plain = mult;
                            hunter.Dormant = wasDormant;
                            Log("boss stagger", "broken " + brk + ", held " + held + ", damage x" + (plain > 0f ? (broken / plain).ToString("F2") : "?") + ", recovered " + !hunter.Broken.Value + " (meter " + hunter.Stagger.Value.ToString("F2") + ")",
                                brk && held && !hunter.Broken.Value && (plain <= 0f || Mathf.Abs(broken / plain - BossBrain.BreakTaken) < 0.05f));
                        }
                    }
                }
                dc.ServerDebugTide();
                yield return new WaitForSeconds(0.3f);
                var floorSpot = DungeonDirector.Layout.StartRoom.Center + Vector3.up * 0.05f;
                Log("tide", "level " + DungeonDirector.TideLevel.ToString("F2") + ", slow " + DungeonDirector.WaterSlow(floorSpot).ToString("F2"), DungeonDirector.WaterSlow(floorSpot) < 1f);
            }

            // An Entling roots the player until it dies; a Mania hit takes the legs for a moment.
            if (net.IsServer && local != null && DungeonDirector.Layout != null)
            {
                var me2 = local.GetComponent<FirstPersonMotor>();
                var deb2 = local.GetComponent<PlayerDebuffs>();
                var hp2 = local.GetComponent<Health>();
                // Alone with it: other attackers nearby would hold its turn to strike.
                foreach (var e in FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None))
                    if (!e.IsDead && !(e is BossBrain) && Vector3.Distance(e.transform.position, local.transform.position) < 30f) e.GetComponent<Health>().ServerDamage(1e6f, e.transform.position);
                yield return new WaitForSeconds(0.3f);
                var ent = DungeonDirector.Instance.ServerDebugSpawnEntling(local.transform.position + local.transform.forward * 5f);
                if (ent != null) { ent.ServerDebugPlace(local.transform.position + local.transform.forward * 1.6f); ent.ServerAggroNearest(); }
                bool held = false;
                for (float t = 0f; t < 9f && ent != null && !held; t += 0.2f) { hp2.ServerSetFraction(1f); yield return new WaitForSeconds(0.2f); held = deb2.Entangled; }
                bool cantMove = deb2.Rooted && deb2.SpeedMultiplier == 0f;
                if (ent != null) ent.GetComponent<Health>().ServerDamage(1e6f, ent.transform.position, local.NetworkObjectId);
                yield return new WaitForSeconds(0.4f);
                Log("entling", "held " + held + ", rooted " + cantMove + ", free after it died " + !deb2.Entangled +
                    (held ? "" : " (target " + (ent != null && ent.CurrentTarget != null ? ent.CurrentTarget.name : "none") + ", dist " + (ent != null ? Vector3.Distance(ent.transform.position, local.transform.position).ToString("F1") : "-") +
                    ", targetable " + PlayerLife.CanTarget(local.GetComponent<Health>()) + ")"), ent != null && held && cantMove && !deb2.Entangled);
                deb2.ServerMania(1f);
                yield return new WaitForSeconds(0.2f);
                bool manic = deb2.Manic && !local.GetComponent<PlayerLife>().CanAct;
                // The Iron Psalter (a relic the blood altar may have given) makes you immune to mania.
                bool psalter = local.GetComponent<PlayerProgress>().HasRelic(Relic.IronPsalter);
                deb2.ServerCleanse();
                Log("mania", (psalter ? "immune (Iron Psalter), " : "") + "manic " + manic + ", cleansed " + !deb2.Manic, (manic || psalter) && !deb2.Manic);
                // The Iron Psalter: madness slides off.
                var pp2 = local.GetComponent<PlayerProgress>();
                // A full tray would turn it into an empowering of something else: make room.
                if (!pp2.HasRelic(Relic.IronPsalter) && pp2.RelicCount >= Relics.MaxCarried) pp2.ServerDebugRemoveRelic(pp2.RelicAt(0));
                pp2.ServerAddRelic(Relic.IronPsalter);
                yield return new WaitForSeconds(4f); // past the mania grace
                deb2.ServerMania(1f);
                yield return new WaitForSeconds(0.2f);
                Log("iron psalter", "maddened " + deb2.Manic, !deb2.Manic);
                deb2.ServerCleanse();
                hp2.ServerSetFraction(1f);
            }

            // Ranged falloff: full bite to 45% of range, 60% at the very end.
            Log("falloff", "at 30% " + Arrows.Falloff(9f, 30f).ToString("F2") + ", at 75% " + Arrows.Falloff(22.5f, 30f).ToString("F2") + ", at the end " + Arrows.Falloff(30f, 30f).ToString("F2"),
                Arrows.Falloff(9f, 30f) >= 0.999f && Arrows.Falloff(30f, 30f) <= Arrows.FalloffFloor + 0.001f && Arrows.Falloff(22.5f, 30f) < 1f);

            // The anvil goes to +10: gold from +6, a third socket at +6, Ascended at +8.
            if (net.IsServer && local != null && DungeonDirector.InLobby == false)
            {
                var tp = local.GetComponent<PlayerProgress>();
                if (tp.WeaponPath.Value == 0) tp.WeaponPath.Value = 1;
                tp.ServerAddEmbers(3000);
                tp.ServerAddGold(3000);
                int goldBefore = tp.Gold.Value;
                float multBefore = tp.AttackMultiplier;
                int guard = 0;
                var offered = new System.Collections.Generic.List<Progression.Edge>();
                while (tp.WeaponLevel.Value < World.Anvil.MaxLevel && guard++ < 16)
                {
                    if (tp.OpenEdgeSlot >= 0)
                    {
                        var o = tp.EdgeOffer(tp.OpenEdgeSlot);
                        offered.AddRange(o);
                        tp.ServerChooseEdge(o[1]);
                    }
                    tp.ServerUpgradeWeapon();
                }
                Log("anvil to +10", "level " + tp.WeaponLevel.Value + ", sockets " + Runes.SocketsAt(tp.WeaponLevel.Value) + ", gold spent " + (goldBefore - tp.Gold.Value) + ", attack x" + multBefore.ToString("F2") + " -> x" + tp.AttackMultiplier.ToString("F2"),
                    tp.WeaponLevel.Value == World.Anvil.MaxLevel && Runes.SocketsAt(tp.WeaponLevel.Value) == 3 && goldBefore - tp.Gold.Value > 0);

                // Edges: three forged on the way (all different), a wrong pick refused; then hones past +10.
                bool edgesOk = true;
                for (int s = 0; s < Progression.Edges.Slots; s++)
                    for (int s2 = s + 1; s2 < Progression.Edges.Slots; s2++)
                        if (tp.EdgeAt(s) == Progression.Edge.None || tp.EdgeAt(s) == tp.EdgeAt(s2)) edgesOk = false;
                if (tp.EdgeAt(Progression.Edges.Slots - 1) == Progression.Edge.None) edgesOk = false;
                bool refused = !tp.ServerChooseEdge(Progression.Edge.Stalwart);
                float preHone = tp.AttackMultiplier;
                int hones = 0;
                while (tp.ServerHone()) hones++;
                Log("edges and hones", "forged " + Progression.Edges.Name(tp.EdgeAt(0)) + ", " + Progression.Edges.Name(tp.EdgeAt(1)) + ", " + Progression.Edges.Name(tp.EdgeAt(2))
                    + " (offered " + offered.Count + "), extra pick refused " + refused + ", hones " + hones + ", attack x" + preHone.ToString("F2") + " -> x" + tp.AttackMultiplier.ToString("F2"),
                    edgesOk && refused && offered.Count == 9 && hones == World.Anvil.MaxHones && tp.AttackMultiplier > preHone * 1.14f && tp.CannotTemperReason() != null);
            }

            // The climbing rope: from the waystone the party goes back to camp, and the same depth waits for them.
            if (net.IsServer && local != null && DungeonDirector.Instance.Floor.Value.y >= 1)
            {
                var rd = DungeonDirector.Instance;
                int depthNow = rd.Floor.Value.y;
                rd.ServerDebugStartFloor(depthNow);
                yield return new WaitForSeconds(2f);
                var rpr = local.GetComponent<PlayerProgress>();
                rpr.Rope.Value = true;
                var rway = DungeonDirector.Layout.WaystonePosition;
                local.GetComponent<FirstPersonMotor>().Teleport(rway + (DungeonDirector.Layout.StartRoom.Center - rway).normalized * 1.2f + Vector3.up * 0.1f, local.transform.rotation);
                yield return new WaitForSeconds(0.3f);
                bool could = rpr.CanUseRope;
                rpr.UseRopeRpc();
                for (float t = 0f; t < 5f && !DungeonDirector.InLobby; t += 0.2f) yield return new WaitForSeconds(0.2f);
                yield return new WaitForSeconds(0.5f);
                Log("climbing rope", "could " + could + ", in camp " + DungeonDirector.InLobby + ", next depth " + rd.NextDepth.Value + " (was " + depthNow + "), rope used " + !rpr.Rope.Value,
                    could && DungeonDirector.InLobby && rd.NextDepth.Value == depthNow && !rpr.Rope.Value);
                rd.ServerDebugStartFloor(depthNow);
                yield return new WaitForSeconds(2f);
            }

            // Wipe the party and check the run summary has something to show.
            if (net.IsServer && local != null)
            {
                // Repeat: a Phoenix Feather (or its grace window) can cheat the first blow.
                for (int tries = 0; tries < 4 && DungeonDirector.Instance.Phase.Value != RunPhase.Wiped; tries++)
                {
                    foreach (var p in PlayerLife.All) p.Health.ServerDamage(1e6f, p.transform.position);
                    yield return new WaitForSeconds(tries == 0 ? 0.6f : 2.5f);
                }
                var d = DungeonDirector.Instance;
                var prog = local.GetComponent<PlayerProgress>();
                Log("run summary", "phase " + d.Phase.Value + ", run " + (d.RunEndedAt.Value - d.RunStartedAt.Value).ToString("F0") + "s, kills " + prog.Kills.Value +
                    ", damage " + prog.DamageDealt.Value + ", downs " + prog.Downs.Value,
                    d.Phase.Value == RunPhase.Wiped && prog.Kills.Value >= 3 && prog.Downs.Value >= 1 && d.RunEndedAt.Value > d.RunStartedAt.Value);
            }

            // Relic synergy (last, so its effects cannot disturb other checks).
            if (net.IsServer && local != null)
            {
                var synP = local.GetComponent<PlayerProgress>();
                // An empty tray first: relics from earlier checks could fill it or already hold these.
                for (int ri = 0; ri < Relics.Count; ri++) synP.ServerDebugRemoveRelic((Relic)ri);
                synP.ServerAddRelic(Relic.ThunderSigil);
                synP.ServerAddRelic(Relic.EchoStone);
                Log("relic synergy", "Storm Caller " + Relics.Has(synP, Relics.Synergy.StormCaller), Relics.Has(synP, Relics.Synergy.StormCaller));

                // The relic tray: a relic you carry empowers it; a full tray feeds the weakest; cooldown cuts cap.
                float cdTier0 = synP.CooldownMultiplier;
                synP.ServerAddRelic(Relic.EchoStone);
                int echoTier = synP.RelicTier(Relic.EchoStone);
                for (int ri = 0; ri < 14; ri++) synP.ServerAddRelic((Relic)ri);
                int tiered = 0;
                for (int ri = 0; ri < synP.RelicCount; ri++) if (synP.RelicTierAt(ri) > 0) tiered++;
                Log("relic tray", "Echo Stone tier " + echoTier + " (cooldowns x" + cdTier0.ToString("F2") + " -> x" + synP.CooldownMultiplier.ToString("F2") + "), carried " + synP.RelicCount + "/" + Relics.MaxCarried + ", empowered " + tiered,
                    echoTier == 1 && synP.RelicCount == Relics.MaxCarried && tiered >= 2 && synP.CooldownMultiplier >= 1f - PlayerProgress.MaxCooldownCut - 0.001f && synP.CooldownMultiplier < cdTier0);

                // The new pairs wake their synergies.
                for (int ri = 0; ri < Relics.Count; ri++) synP.ServerDebugRemoveRelic((Relic)ri);
                foreach (var pr in new[] { Relic.BannerOfTheFallen, Relic.RallyingBell, Relic.FrostCharm, Relic.WhettedFang }) synP.ServerAddRelic(pr);
                Log("new synergies", "War Band " + Relics.Has(synP, Relics.Synergy.WarBand) + ", Cold Steel " + Relics.Has(synP, Relics.Synergy.ColdSteel),
                    Relics.Has(synP, Relics.Synergy.WarBand) && Relics.Has(synP, Relics.Synergy.ColdSteel));

                // Thermal Shock: fire on an enemy, then frost: the two cancel in a burst.
                var shockT = Health.All.FirstOrDefault(hh => hh != null && hh.Team == Team.Enemies && hh.IsAlive && !hh.TryGetComponent<BossBrain>(out _));
                if (shockT != null)
                {
                    shockT.ServerSetFraction(1f);
                    float shockHp = shockT.Current.Value;
                    shockT.ServerScorch(4f, local.NetworkObjectId);
                    bool scorched = shockT.Scorched.Value;
                    shockT.ServerChill(3f);
                    Log("thermal shock", "scorched " + scorched + ", after frost: scorched " + shockT.Scorched.Value + " chilled " + shockT.Chilled.Value + ", dealt " + (shockHp - shockT.Current.Value).ToString("F0"),
                        scorched && !shockT.Scorched.Value && shockT.Current.Value < shockHp); // chill may come back from another source at once
                }

                // Rank 5 is a choice: Hastened trades strength for cooldown.
                synP.RankQ.Value = 5;
                synP.RankChoice.Value = 0;
                float overP = synP.Potency(Classes.AbilitySlot.Q), overCd = synP.CooldownFor(Classes.AbilitySlot.Q);
                synP.RankChoice.Value = 1;
                float hasteP = synP.Potency(Classes.AbilitySlot.Q), hasteCd = synP.CooldownFor(Classes.AbilitySlot.Q);
                Log("rank five choice", "overcharged x" + overP.ToString("F2") + " / cd x" + overCd.ToString("F2") + ", hastened x" + hasteP.ToString("F2") + " / cd x" + hasteCd.ToString("F2"),
                    overP > hasteP && hasteCd < overCd);

                // Co-op loot: each of four players gets about a solo player's share.
                float k4 = Difficulty.KillLootShare(4), perPlayer = Difficulty.SpawnCount(4) / Difficulty.SpawnCount(1) * k4 / 4f;
                Log("coop loot share", "kill rolls x" + k4.ToString("F2") + " for 4, each player's kill income x" + perPlayer.ToString("F2") + " of solo; chests x" + Difficulty.FixedLootShare(4),
                    Mathf.Abs(perPlayer - 1f) < 0.01f && Difficulty.KillLootShare(1) == 1f && Difficulty.FixedLootShare(4) == 4f);

                // Rare draughts and tomes (pouch slot 5).
                var rp = local.GetComponent<Flasks>();
                var rlife = local.GetComponent<PlayerLife>();
                var rhp = local.GetComponent<Health>();
                if (rp != null)
                {
                    rhp.ServerSetFraction(1f);
                    rp.Rare.Value = 0; // a champion killed earlier may have filled the slot
                    bool wasDown = rlife.Downed.Value;
                    if (wasDown) rlife.ServerRevive(1f); // nobody drinks while down
                    yield return null;
                    bool gave = rp.ServerGiveRare(Progression.RareItem.Ironbark), full = !rp.ServerGiveRare(Progression.RareItem.Bloodfury);
                    rp.ServerUseRare();
                    yield return null;
                    rhp.Shield.Value = 0f;
                    float b0 = rhp.Current.Value; rhp.ServerDamage(40f, local.transform.position + Vector3.forward); float ironHit = b0 - rhp.Current.Value;
                    bool iron = rp.Ironbark;
                    rp.IronbarkUntil.Value = 0;
                    rhp.ServerSetFraction(1f);
                    b0 = rhp.Current.Value; rhp.ServerDamage(40f, local.transform.position + Vector3.forward); float plainHit = b0 - rhp.Current.Value;
                    float att0 = synP.AttackMultiplier;
                    rp.ServerGiveRare(Progression.RareItem.Bloodfury); rp.ServerUseRare();
                    yield return null;
                    float att1 = synP.AttackMultiplier;
                    rp.BloodfuryUntil.Value = 0;
                    rhp.ServerSetFraction(1f);
                    rp.ServerGiveRare(Progression.RareItem.GraveStillness); rp.ServerUseRare();
                    yield return null;
                    b0 = rhp.Current.Value; rhp.ServerDamage(80f, local.transform.position + Vector3.forward);
                    bool stone = rp.InStasis && rhp.Current.Value >= b0 - 0.01f && !rlife.CanAct && !rlife.Targetable;
                    rp.StasisUntil.Value = 0; rlife.VeiledUntil = 0f;
                    Log("rare draughts", (wasDown ? "(revived first) " : "") + "slot took one " + gave + ", second refused " + full + ", ironbark " + iron + " (hit " + ironHit.ToString("F0") + " vs " + plainHit.ToString("F0") + "), bloodfury attack x" + att0.ToString("F2") + " -> x" + att1.ToString("F2") + ", stone " + stone,
                        gave && full && iron && ironHit < plainHit * 0.7f && att1 > att0 * 1.3f && stone);

                    // The Tome of the Phoenix: it dives at the dead and scorches them; struck down, it lies as an egg and is reborn.
                    var prey = DungeonDirector.Instance.ServerDebugSpawn("skeleton", local.transform.position + local.transform.forward * 5f, false);
                    int birds0 = Phoenix.All.Count;
                    rp.Rare.Value = 0;
                    rp.ServerGiveRare(Progression.RareItem.PhoenixTome); bool read = rp.ServerUseRare();
                    Phoenix bird = Phoenix.All.LastOrDefault();
                    bool scorchedPrey = false; float preyHp = prey != null ? prey.GetComponent<Health>().Current.Value : 0f;
                    for (float t = 0f; t < 6f && prey != null && !scorchedPrey; t += 0.1f)
                    {
                        rhp.ServerSetFraction(1f);
                        var ph = prey.GetComponent<Health>();
                        scorchedPrey = ph.Scorched.Value || ph.Current.Value < preyHp;
                        yield return new WaitForSeconds(0.1f);
                    }
                    bool egg = false, reborn = false;
                    if (bird != null)
                    {
                        bird.GetComponent<Health>().ServerDamage(1e6f, bird.transform.position);
                        yield return null;
                        egg = bird.Egg.Value;
                        for (float t = 0f; t < Phoenix.EggSeconds + 1.5f && bird != null && bird.Egg.Value; t += 0.1f) yield return new WaitForSeconds(0.1f);
                        reborn = bird != null && !bird.Egg.Value && bird.Rebirths == 1;
                    }
                    Log("phoenix tome", "read " + read + ", birds " + birds0 + " -> " + Phoenix.All.Count + ", struck its prey " + scorchedPrey + ", egg when downed " + egg + ", reborn " + reborn,
                        read && bird != null && scorchedPrey && egg && reborn);
                    if (bird != null && bird.IsSpawned) bird.NetworkObject.Despawn();

                    // The Tome of the Storm strikes the same prey; the Tome of Sanctuary turns blows aside inside it.
                    bool stormHit = false;
                    if (prey != null && prey.IsSpawned)
                    {
                        var ph2 = prey.GetComponent<Health>();
                        ph2.ServerSetFraction(1f);
                        float before2 = ph2.Current.Value;
                        rp.Rare.Value = 0; rp.ServerGiveRare(Progression.RareItem.StormTome); rp.ServerUseRare();
                        for (float t = 0f; t < 3f && !stormHit; t += 0.1f) { rhp.ServerSetFraction(1f); stormHit = ph2 == null || ph2.Current.Value < before2; yield return new WaitForSeconds(0.1f); }
                    }
                    rp.Rare.Value = 0; rp.ServerGiveRare(Progression.RareItem.SanctuaryTome); rp.ServerUseRare();
                    yield return null;
                    rhp.ServerSetFraction(1f); rhp.Shield.Value = 0f;
                    float s0 = rhp.Current.Value; rhp.ServerDamage(40f, local.transform.position + Vector3.forward); float sanctHit = s0 - rhp.Current.Value;
                    Log("storm and sanctuary tomes", "storm struck " + stormHit + ", hit in sanctuary " + sanctHit.ToString("F0") + " (plain was " + plainHit.ToString("F0") + ")",
                        stormHit && sanctHit < plainHit * 0.8f);
                    if (prey != null && prey.IsSpawned) prey.NetworkObject.Despawn();

                    // A weak point bursts for its full bonus once in a while; a beast struck there goes ENRAGED.
                    var beast = DungeonDirector.Instance.ServerDebugSpawn("brute", local.transform.position + local.transform.forward * 4f, false);
                    if (beast != null)
                    {
                        var bh = beast.GetComponent<Health>();
                        float fullHit = HitZones.Multiplier(bh, HitZone.Weak, true);
                        Arrows.ReportZone(local.NetworkObjectId, bh.Center, HitZone.Weak);
                        yield return null;
                        float cooling = HitZones.Multiplier(bh, HitZone.Weak, true);
                        bool raged = beast.Enraged;
                        Log("weak point burst", "full x" + fullHit.ToString("F2") + ", then x" + cooling.ToString("F2") + " while it recovers, brute enraged " + raged,
                            fullHit > cooling && raged);
                        if (beast.IsSpawned) beast.NetworkObject.Despawn();
                    }
                    else Log("weak point burst", "no brute prefab", false);
                }
            }

            yield return new WaitForSeconds(2f);
            Log("final phase", DungeonDirector.Instance != null ? DungeonDirector.Instance.Phase.Value.ToString() : "no director", true);
            Debug.Log("[Smoke] done");
            Application.Quit(0);
        }

        /// <summary>
        /// Visual check: captures the title screen, then hosts and captures views from the
        /// start room, a combat room and the boss room into <paramref name="dir"/>. Run
        /// windowed (not -nographics), e.g. <c>-shots C:\temp\shots -screen-fullscreen 0</c>.
        /// </summary>
        public void Screenshots(string dir, NetworkLauncher launcher) => StartCoroutine(ShotSequence(dir, launcher));

        private IEnumerator ShotSequence(string dir, NetworkLauncher launcher)
        {
            System.IO.Directory.CreateDirectory(dir);
            yield return new WaitForSeconds(2.5f);
            UI.MenuBackdrop.DebugShow(0);
            yield return new WaitForSeconds(1.5f);
            yield return Shot(dir, "0_menu");
            UI.MenuBackdrop.DebugShow(1);
            yield return new WaitForSeconds(3.2f);
            yield return Shot(dir, "0_menu_battle");

            launcher.StartHost();
            yield return new WaitForSeconds(3f);

            var local = NetworkPlayer.Local;
            // The photographer never dies (bosses wake when it walks into their rooms); the run summary at the end lifts this.
            bool shotsImmortal = true;
            if (local != null) local.GetComponent<Health>().IncomingModifiers.Insert(0, (d, f) => shotsImmortal && d < 1e5f ? 0f : d);
            if (local == null)
            {
                Debug.Log("[Smoke] FAIL screenshots: no local player");
                Application.Quit(1);
                yield break;
            }

            yield return new WaitForSeconds(1.5f);
            yield return Shot(dir, "0a_character_select");
            // Every class, front and back, for judging the character art.
            var select = local.GetComponent<UI.CharacterSelect>();
            for (int ci = 0; ci < Classes.ClassDefinition.ById.Length; ci++)
            {
                foreach (var (yaw, tag) in new[] { (200f, "front"), (20f, "back") })
                {
                    select.DebugView(ci, yaw);
                    yield return null;
                    yield return null;
                    yield return Shot(dir, "0p_class_" + ((Classes.ClassId)ci) + "_" + tag);
                }
            }
            select.DebugView((int)local.GetComponent<PlayerProgress>().ClassId.Value, 200f);
            local.GetComponent<UI.CharacterSelect>().Close();
            yield return new WaitForSeconds(0.2f);

            var lobbyMotor = local.GetComponent<FirstPersonMotor>();
            lobbyMotor.enabled = false;
            var lobbyFrom = Lobby.BoardingCenter + new Vector3(-6f, 0.1f, -6f);
            lobbyMotor.Teleport(lobbyFrom, Quaternion.LookRotation(Lobby.BoardingCenter + new Vector3(4f, 0f, 0f) - lobbyFrom));
            yield return new WaitForSeconds(1f);
            yield return Shot(dir, "0b_lobby_carriage");

            var dummyFrom = Lobby.DummySpots[0] + new Vector3(4f, 0.1f, 1f);
            lobbyMotor.Teleport(dummyFrom, Quaternion.LookRotation(Lobby.DummySpots[0] - dummyFrom));
            yield return new WaitForSeconds(0.5f);
            yield return Shot(dir, "0c_lobby_dummies");
            Art.SmokeCloud.Spawn(Lobby.DummySpots[0] + new Vector3(2f, 0f, 2.5f), Player.RogueKit.SmokeCloudRadius, 4f);
            yield return new WaitForSeconds(0.9f);
            yield return Shot(dir, "0c2_smoke_cloud");

            // A tour of the castle: the road from the bonfire, the stalls, the tent, the gong, the gate.
            var tour = new (string name, Vector3 at, Vector3 look, float pitch)[]
            {
                ("0j_castle_road", Lobby.Origin + Lobby.S(0f, 0.1f, -17f), Lobby.Origin + Lobby.S(0f, 2f, 10f), -8f),
                ("0k_castle_stalls", Lobby.Origin + Lobby.S(0f, 0.1f, -10f), Lobby.Origin + Lobby.S(-5f, 1.5f, -5f), 0f),
                ("0l_castle_tent", Lobby.Origin + Lobby.S(-2f, 0.1f, 6f), Lobby.TentCenter + Vector3.up * 1.5f, 0f),
                ("0m_castle_gong", Lobby.Origin + Lobby.S(3f, 0.1f, 6f), Lobby.GongSpot + Vector3.up * 2f, 0f),
                ("0n_castle_gate", Lobby.Origin + Lobby.S(-4f, 0.1f, 8f), Lobby.BoardingCenter + new Vector3(2f, 3f, 6f), -10f),
                ("0o_castle_keep", Lobby.Origin + Lobby.S(3f, 0.1f, 2f), Lobby.Origin + Lobby.S(0f, 9f, -24f), -18f),
                ("0q_castle_shrine", Lobby.Origin + Lobby.S(10.5f, 0.1f, -4f), Lobby.ShrineSpot + Vector3.up * 1.5f, 0f),
                ("0r_castle_range", Lobby.Origin + Lobby.S(8.5f, 0.1f, 11f), Lobby.RangeCenter + Vector3.up * 1.2f, 0f),
                ("0x1_tavern_outside", Lobby.TavernCenter + new Vector3(10f, 0.1f, -4f), Lobby.TavernCenter + Vector3.up * 2f, -4f),
                ("0x2_tavern_bar", Lobby.TavernCenter + new Vector3(3.2f, 0.1f, 1.8f), Lobby.TavernKeepSpot + Vector3.up * 1.3f, 0f),
                ("0x3_tavern_band", Lobby.TavernCenter + new Vector3(0.2f, 0.1f, 2.2f), Lobby.TavernBandSpot + Vector3.up * 1.2f, 4f),
            };
            foreach (var (tname, tat, tlook, tpitch) in tour)
            {
                var flat = tlook - tat; flat.y = 0f;
                lobbyMotor.Teleport(tat, Quaternion.LookRotation(flat));
                lobbyMotor.SetPitch(tpitch);
                yield return new WaitForSeconds(0.4f);
                yield return Shot(dir, tname); if (tname == "0j_castle_road") yield return SamplePerf(tname);
            }
            lobbyMotor.SetPitch(0f);
            // The emote wheel, pushed toward DANCE.
            var shotEmotes = local.GetComponent<Emotes>();
            shotEmotes.DebugShowWheel(true, new Vector2(80f, -70f));
            yield return new WaitForSeconds(0.1f);
            yield return Shot(dir, "0x4_emote_wheel");
            shotEmotes.DebugShowWheel(false, Vector2.zero);

            // The Mage: switch class, frost the dummy, catch the bolt and the chill.
            var prog = local.GetComponent<PlayerProgress>();
            prog.ClassId.Value = Classes.ClassId.Mage;
            yield return new WaitForSeconds(0.5f);
            var mage = local.GetComponent<MageKit>();
            var dummyHp = FindObjectsByType<TrainingDummy>(FindObjectsSortMode.None)[0].GetComponent<Health>();
            lobbyMotor.Teleport(dummyHp.transform.position + new Vector3(4.5f, 0.1f, -0.5f), Quaternion.LookRotation(new Vector3(-4.5f, 0f, 0.5f)));
            yield return new WaitForSeconds(0.3f);
            var origin = local.transform.position + Vector3.up * 1.6f;
            local.ViewModelCast(true);
            mage.ServerBolt(origin + local.transform.forward * 0.6f, dummyHp.Center - origin, true);
            yield return new WaitForSeconds(0.08f);
            yield return Shot(dir, "0d_mage_frostbolt");
            yield return new WaitForSeconds(0.5f);
            yield return Shot(dir, "0e_mage_chilled_dummy");
            // Tidal Wave rolling out toward the dummy, mid-flow and at its reach.
            lobbyMotor.SetPitch(10f);
            mage.ServerWave(local.transform.forward);
            yield return new WaitForSeconds(0.22f);
            yield return Shot(dir, "0e2_tidal_wave");
            yield return new WaitForSeconds(0.2f);
            yield return Shot(dir, "0e3_tidal_wave_reach");
            lobbyMotor.SetPitch(0f);

            // Meteor: falling through the ceiling, the impact, the crater left behind.
            var meteorSpot = dummyHp.transform.position + new Vector3(-1.5f, 0f, 2f);
            var mFrom2 = meteorSpot + new Vector3(8f, 0.1f, -4f);
            lobbyMotor.Teleport(mFrom2, Quaternion.LookRotation(meteorSpot - mFrom2));
            lobbyMotor.SetPitch(-12f);
            yield return new WaitForSeconds(0.2f);
            mage.ServerMeteor(meteorSpot);
            yield return new WaitForSeconds(0.85f);
            yield return Shot(dir, "0g_meteor_falling");
            yield return new WaitForSeconds(0.5f);
            yield return Shot(dir, "0h_meteor_impact");
            lobbyMotor.SetPitch(8f);
            yield return new WaitForSeconds(1.4f);
            yield return Shot(dir, "0i_meteor_crater");
            lobbyMotor.SetPitch(0f);
            // The Ranger at full draw.
            prog.ClassId.Value = Classes.ClassId.Ranger;
            yield return new WaitForSeconds(0.5f);
            yield return Shot(dir, "0f_ranger_bow");

            prog.ClassId.Value = Classes.ClassId.Knight;
            yield return new WaitForSeconds(0.3f);

            var m = Lobby.MerchantSpot;
            var mFrom = m + Vector3.right * 3.4f + Vector3.up * 0.1f;
            lobbyMotor.Teleport(mFrom, Quaternion.LookRotation(m - mFrom));
            yield return new WaitForSeconds(0.5f);
            yield return Shot(dir, "1b_merchant");
            var mClose = m + Vector3.right * 2.4f + Vector3.up * 0.1f;
            lobbyMotor.Teleport(mClose, Quaternion.LookRotation(m - mClose));
            yield return new WaitForSeconds(0.2f);
            local.GetComponent<PlayerProgress>().ServerAddGold(400);
            local.GetComponent<UI.GameHud>().DebugOpenShop();
            yield return null;
            yield return Shot(dir, "1b_merchant_panel");
            // Silas from behind: his pack.
            local.GetComponent<UI.GameHud>().DebugCloseMenus();
            foreach (var (side, tag) in new[] { (-1f, "1b_merchant_back"), (1f, "1b_merchant_front") })
            {
                var view = Lobby.MerchantSpot + new Vector3(side * 1.7f, 0.1f, 0.6f);
                lobbyMotor.Teleport(view, Quaternion.LookRotation(Vector3.ProjectOnPlane(Lobby.MerchantSpot - view, Vector3.up)));
                lobbyMotor.SetPitch(10f);
                yield return new WaitForSeconds(0.3f);
                yield return Shot(dir, tag);
            }
            lobbyMotor.SetPitch(0f);
            // A raven on the road, up close (the lowest one: they also sit on the battlements).
            var raven = FindObjectsByType<Art.Critter>(FindObjectsSortMode.None).Where(c => c.Type == Art.Critter.Kind.Raven).OrderBy(c => c.transform.position.y).FirstOrDefault();
            if (raven != null)
            {
                local.GetComponent<UI.GameHud>().DebugCloseMenus();
                var rv = raven.transform.position;
                if (rv.y > Lobby.Origin.y + 2f) rv = Lobby.Origin + Lobby.S(0.3f, 0.02f, 2.6f);
                raven.DebugHold(rv);
                var stand = rv + new Vector3(2.2f, 0f, 0.4f);
                lobbyMotor.Teleport(stand + Vector3.up * 0.1f, Quaternion.LookRotation(Vector3.ProjectOnPlane(rv - stand, Vector3.up)));
                lobbyMotor.SetPitch(30f);
                yield return new WaitForSeconds(0.3f);
                yield return Shot(dir, "0u_raven");
                lobbyMotor.SetPitch(0f);
                raven.enabled = true;
            }
            // Brother Aldric at his prayers by the shrine (he faces away from it, toward the road).
            {
                var aldric = Lobby.ShrineSpot + Vector3.left * 1.2f;
                foreach (var (off, tag) in new[] { (new Vector3(-2.3f, 0.1f, 0.9f), "0v_aldric_praying"), (new Vector3(-0.3f, 0.1f, 2.4f), "0v_aldric_side") })
                {
                    var view = aldric + off;
                    lobbyMotor.Teleport(view, Quaternion.LookRotation(Vector3.ProjectOnPlane(aldric - view, Vector3.up)));
                    lobbyMotor.SetPitch(14f);
                    yield return new WaitForSeconds(0.4f);
                    yield return Shot(dir, tag);
                }
                lobbyMotor.SetPitch(0f);
            }
            // Strike Silas: he rocks back, complains, and a bystander chimes in.
            var silasBody = FindObjectsByType<CampFolk>(FindObjectsSortMode.None).FirstOrDefault(f => f.Who.Value == (byte)FolkId.Silas);
            if (silasBody != null)
            {
                local.GetComponent<UI.GameHud>().DebugCloseMenus();
                silasBody.GetComponent<Health>().ServerDamage(20f, local.transform.position, local.NetworkObjectId);
                yield return new WaitForSeconds(0.25f);
                yield return Shot(dir, "1b_merchant_struck");
            }
            // The other two stalls: Hobb at the anvil, the shrine's offerings.
            foreach (var (vendor, spot, tag) in new[] { (Shop.Vendor.Smith, Lobby.AnvilSpot + Vector3.left * 2.2f, "1b_smith_panel"), (Shop.Vendor.Shrine, Lobby.ShrineSpot + Vector3.left * 2.4f, "1b_shrine_panel"), (Shop.Vendor.Carriage, Lobby.CarriageAt + new Vector3(2.2f, 0f, 2.2f), "1b_corwin_panel") })
            {
                lobbyMotor.Teleport(spot + Vector3.up * 0.1f, Quaternion.LookRotation(Vector3.right));
                yield return new WaitForSeconds(0.3f);
                local.GetComponent<UI.GameHud>().DebugOpenShop(vendor);
                yield return null;
                yield return Shot(dir, tag);
            }
            local.GetComponent<PlayerProgress>().ServerResetRun();

            // The tailor: the stall, the mannequin in a dye, and the wardrobe panel.
            var tFrom = Lobby.MannequinSpot + new Vector3(3.1f, 0.1f, -0.3f);
            var tLook = Lobby.MannequinSpot + new Vector3(-1.5f, 0f, -1.5f) - tFrom;
            tLook.y = 0f;
            lobbyMotor.Teleport(tFrom, Quaternion.LookRotation(tLook));
            local.GetComponent<NetworkPlayer>().CloakDye.Value = 2;
            local.GetComponent<NetworkPlayer>().Aura.Value = 1;
            yield return new WaitForSeconds(0.6f);
            yield return Shot(dir, "1c_tailor");
            var tClose = Lobby.TailorSpot + Lobby.TailorFacing * 1.8f + Vector3.up * 0.1f;
            lobbyMotor.Teleport(tClose, Quaternion.LookRotation(Lobby.MannequinSpot - tClose));
            yield return new WaitForSeconds(0.3f);
            local.GetComponent<UI.Wardrobe>().DebugOpen(true);
            yield return null;
            yield return Shot(dir, "1c_tailor_panel");
            local.GetComponent<UI.Wardrobe>().DebugTab(Progression.Cosmetics.Kind.Pattern);
            local.GetComponent<NetworkPlayer>().Pattern.Value = 2;
            yield return new WaitForSeconds(1.2f);
            yield return Shot(dir, "1c_tailor_patterns");
            local.GetComponent<UI.Wardrobe>().DebugTab(Progression.Cosmetics.Kind.Cloak);
            local.GetComponent<UI.Wardrobe>().DebugOpen(false);
            local.GetComponent<NetworkPlayer>().Pattern.Value = 0;
            // Every pattern on a row of backs, in a spread of the brighter dyes.
            {
                var fwd = tLook.normalized;
                var right = Vector3.Cross(Vector3.up, fwd);
                var rowAt = tFrom + fwd * 2.8f - Vector3.up * 0.1f;
                lobbyMotor.Teleport(tFrom, Quaternion.LookRotation(fwd));
                var shown = new System.Collections.Generic.List<GameObject>();
                var cls = new[] { Classes.ClassId.Knight, Classes.ClassId.Mage, Classes.ClassId.Cleric, Classes.ClassId.Rogue, Classes.ClassId.Ranger };
                int[] dyes = { 2, 5, 13, 8, 11, 3, 9, 6, 14, 12 };
                for (int pass = 0; pass < 2; pass++)
                {
                    foreach (var g in shown) Destroy(g);
                    shown.Clear();
                    for (int k = 0; k < 5; k++)
                    {
                        int pat = 1 + pass * 5 + k;
                        if (pat >= Progression.Cosmetics.Patterns.Length) break;
                        var body = new GameObject("PatternDemo" + pat);
                        body.transform.SetPositionAndRotation(rowAt + right * (k - 2f) * 0.95f, Quaternion.LookRotation(fwd));
                        NetworkPlayer.BuildBody(cls[k], body.transform, new Color(0.3f, 0.5f, 0.9f), dyes[(pat - 1) % dyes.Length], pat);
                        shown.Add(body);
                    }
                    yield return new WaitForSeconds(0.4f);
                    yield return Shot(dir, "1c_patterns_" + (pass + 1));
                }
                foreach (var g in shown) Destroy(g);
            }
            local.GetComponent<NetworkPlayer>().CloakDye.Value = 0;
            local.GetComponent<NetworkPlayer>().Aura.Value = 0;

            // The bounty board and its notices.
            var bFrom = Lobby.BountySpot + Lobby.BountyFacing * 2.6f + Vector3.up * 0.1f;
            lobbyMotor.Teleport(bFrom, Quaternion.LookRotation(Lobby.BountySpot + Vector3.up * 0.2f - bFrom));
            yield return new WaitForSeconds(0.5f);
            yield return Shot(dir, "1c_bounty_board");
            // The Hall of Records slab beside it (its panel opens within reach).
            var hallFrom = Lobby.RecordsSpot + Lobby.BountyFacing * 2.2f + Vector3.up * 0.1f;
            lobbyMotor.Teleport(hallFrom, Quaternion.LookRotation(Lobby.RecordsSpot + Vector3.up * 0.6f - hallFrom));
            yield return new WaitForSeconds(0.5f);
            yield return Shot(dir, "1c_hall_of_records");

            var anvilObj = FindObjectsByType<Anvil>(FindObjectsSortMode.None).FirstOrDefault();
            if (anvilObj != null)
            {
                var af = anvilObj.transform.position + anvilObj.transform.forward * 2.2f + Vector3.up * 0.1f;
                lobbyMotor.Teleport(af, Quaternion.LookRotation(anvilObj.transform.position - af));
                lobbyMotor.SetPitch(20f);
                yield return new WaitForSeconds(0.5f);
                yield return Shot(dir, "1b_anvil");
                lobbyMotor.SetPitch(0f);
                var ap = local.GetComponent<PlayerProgress>();
                ap.ServerAddEmbers(60);
                ap.TemperRpc();
                ap.TemperRpc();
                ap.ServerAddRune(Rune.Storm);
                ap.ServerAddRune(Rune.Holy);
                ap.ServerAddRune(Rune.Ember);
                ap.ServerAddSigil();
                ap.ServerEquip(new GearItem(GearId.ThornhideJerkin, 0, 0));
                ap.ServerEquip(new GearItem(GearId.LichPhylactery, 3, 0));
                yield return new WaitForSeconds(0.3f);
                local.GetComponent<UI.GameHud>().DebugOpenForge();
                yield return null;
                yield return Shot(dir, "1b_forge_panel");
                // At +5: an edge to choose (three cards), then one forged and +7 open.
                if (ap.WeaponPath.Value == 0) ap.ChoosePathRpc(1);
                yield return new WaitForSeconds(0.2f);
                for (int i = 0; i < 4 && ap.WeaponLevel.Value < 5; i++) ap.ServerFreeTemper();
                yield return new WaitForSeconds(0.3f);
                yield return Shot(dir, "1b_forge_edges");
                if (ap.OpenEdgeSlot >= 0) ap.ServerChooseEdge(ap.EdgeOffer(ap.OpenEdgeSlot)[0]);
                for (int i = 0; i < 2; i++) ap.ServerFreeTemper();
                yield return new WaitForSeconds(0.3f);
                yield return Shot(dir, "1b_forge_edges2");
                UI.PauseMenu.SetOpen(false);

                // Ysolde's stones: the ring, then her panel with a rune set and three to meld.
                var yFrom = Lobby.RunewrightSpot + new Vector3(-3.2f, 0.1f, -3.4f);
                lobbyMotor.Teleport(yFrom, Quaternion.LookRotation(Lobby.RunewrightSpot + Vector3.up * 1.2f - yFrom));
                lobbyMotor.SetPitch(8f);
                yield return new WaitForSeconds(0.6f);
                yield return Shot(dir, "1b_runewright");
                lobbyMotor.SetPitch(0f);
                var yNear = Lobby.RunewrightSpot + new Vector3(0f, 0.1f, -1.4f);
                lobbyMotor.Teleport(yNear, Quaternion.LookRotation(Lobby.RunewrightSpot - yNear));
                yield return new WaitForSeconds(0.3f);
                ap.ServerAddRune(Rune.Frost);
                ap.ServerAddRune(Rune.Frost);
                ap.ServerAddRune(Rune.Frost);
                ap.SocketRuneRpc((byte)Rune.Storm);
                // A big bag, as a long run collects (the user's screenshot had eighteen of a kind): it must fit.
                foreach (var rk in new[] { Rune.Frost, Rune.Storm, Rune.Holy, Rune.Keen, Rune.Farsight })
                    for (int n = 0; n < 6; n++) ap.ServerAddRune(Runes.Make(rk, n % 3 == 0 ? 1 : 0));
                ap.ServerAddRune(Runes.Make(Rune.Frost, 2));
                yield return new WaitForSeconds(0.2f);
                local.GetComponent<UI.GameHud>().DebugOpenRunewright();
                yield return null;
                yield return Shot(dir, "1b_runewright_panel");
                // Rank five's choice: Overcharged or Hastened.
                ap.RankQ.Value = 4;
                for (int i = 0; i < 3; i++) ap.ServerAddSigil();
                yield return null;
                yield return Shot(dir, "1b_runewright_rank5");
                ap.ServerResetRun();
                UI.PauseMenu.SetOpen(false);

                // Close looks at the new folk faces: Hobb at his anvil, Vey at her stall.
                foreach (var (tag, at) in new[] { ("1b_face_hobb", Lobby.AnvilSpot + Vector3.back * 1.4f), ("1b_face_vey", Lobby.TailorSpot + Vector3.right * 0.6f), ("1b_face_ysolde", Lobby.RunewrightSpot) })
                {
                    var fwd = Lobby.Origin - at;
                    fwd.y = 0f;
                    var camAt = at + fwd.normalized * 1.6f + Vector3.up * 0.1f;
                    lobbyMotor.Teleport(camAt, Quaternion.LookRotation(at + Vector3.up * 1.5f - (camAt + Vector3.up * 1.6f)));
                    lobbyMotor.SetPitch(4f);
                    yield return new WaitForSeconds(0.6f);
                    yield return Shot(dir, tag);
                }
                lobbyMotor.SetPitch(0f);

                // Sergeant Brann at the yard.
                var brFrom = Lobby.YardSpot + new Vector3(2.4f, 0.1f, -2.4f);
                lobbyMotor.Teleport(brFrom, Quaternion.LookRotation(Lobby.YardSpot + Vector3.up * 1.2f - brFrom));
                yield return new WaitForSeconds(0.5f);
                yield return Shot(dir, "1b_drillmaster");

                // A relic on the ground: the beacon and its name.
                var rFrom = Lobby.Origin + Lobby.S(4f, 0.1f, -10f);
                lobbyMotor.Teleport(rFrom, Quaternion.LookRotation(Vector3.forward));
                DungeonDirector.Instance.ServerDropLoot(rFrom + new Vector3(0f, 0f, 6f), LootKind.Relic, (int)Relic.LongShadow, null, false);
                yield return new WaitForSeconds(0.9f);
                yield return Shot(dir, "1d_relic_beacon");
                foreach (var ld in FindObjectsByType<LootDrop>(FindObjectsSortMode.None)) if (ld.NetworkObject.IsSpawned && ld.Kind.Value == LootKind.Relic) ld.NetworkObject.Despawn();

                // Gear: a drop on the floor with its comparison card, then the Tab sheet.
                var gFrom = Lobby.Origin + Lobby.S(0f, 0.1f, -8f);
                lobbyMotor.Teleport(gFrom, Quaternion.LookRotation(Vector3.forward));
                ap.ServerEquip(new GearItem(GearId.OssuaryPlate, 0, 0));
                ap.ServerEquip(new GearItem(GearId.HuntersTooth, 1, 0));
                GearDrop.ServerSpawn(new GearItem(GearId.KingsguardHauberk, 2, 1), local.OwnerClientId, gFrom + Vector3.forward * 1.6f + Vector3.down * 0.1f);
                GearDrop.ServerSpawn(new GearItem(GearId.LuckyKnucklebone, 0, 0), local.OwnerClientId, gFrom + new Vector3(1.4f, -0.1f, 2.6f));
                lobbyMotor.SetPitch(25f);
                yield return new WaitForSeconds(0.8f);
                yield return Shot(dir, "1d_gear_drop");
                // A Legendary: the pillar of light, from a few steps back.
                GearDrop.ServerSpawn(new GearItem(GearId.WardensBulwark, 3, 0), local.OwnerClientId, gFrom + new Vector3(-2.2f, -0.1f, 5f));
                lobbyMotor.SetPitch(5f);
                yield return new WaitForSeconds(0.8f);
                yield return Shot(dir, "1d_gear_legendary");
                // A soul skull, close up (as an ally would see it).
                var skullAt = gFrom + new Vector3(-3.5f, -0.1f, 0.5f);
                var shotSkull = DungeonDirector.Instance.ServerDebugSkull(skullAt, ulong.MaxValue - 1);
                var campMourner = DungeonDirector.Instance.ServerDebugSpawnMourner(skullAt + new Vector3(-1.5f, 0f, 1.8f), Vector3.right);
                lobbyMotor.Teleport(gFrom, Quaternion.LookRotation(Vector3.left));
                lobbyMotor.SetPitch(22f);
                yield return new WaitForSeconds(0.8f);
                yield return Shot(dir, "1d_soul_skull");
                if (shotSkull != null && shotSkull.NetworkObject.IsSpawned) shotSkull.NetworkObject.Despawn();
                if (campMourner != null) campMourner.GetComponent<Health>().ServerDamage(1e6f, campMourner.transform.position);
                // The ping wheel, open, with Danger picked.
                var wheelPings = local.GetComponent<PlayerPings>();
                wheelPings.DebugShowWheel(true, new Vector2(0f, 80f));
                yield return new WaitForSeconds(0.3f);
                yield return Shot(dir, "1e_ping_wheel");
                wheelPings.DebugShowWheel(false, Vector2.zero);
                lobbyMotor.SetPitch(0f);
                // A long run's haul of relics (the user's screenshot had twenty): the row, sheet and pack must cope.
                for (int ri = 0; ri < 18; ri++) ap.ServerAddRelic((Relic)ri);
                yield return null;
                UI.GameHud.DebugShowStats = true;
                yield return null;
                yield return Shot(dir, "1e_stat_sheet");
                UI.GameHud.DebugShowStats = false;
                ap.ServerStow(new GearItem(GearId.KingsguardHauberk, 2, 1));
                ap.ServerStow(new GearItem(GearId.LuckyKnucklebone, 0, 0));
                ap.ServerStow(new GearItem(GearId.WardensBulwark, 3, 0));
                local.GetComponent<UI.GameHud>().DebugOpenInventory(true);
                yield return new WaitForSeconds(0.3f);
                yield return Shot(dir, "1e_inventory");
                local.GetComponent<UI.GameHud>().DebugOpenInventory(false);
                ap.ServerResetRun();
            }

            {
                var lm = local.GetComponent<FirstPersonMotor>();
                var cat = FindObjectsByType<CampCat>(FindObjectsSortMode.None).FirstOrDefault();
                if (cat != null)
                {
                    var cp = cat.transform.position + cat.transform.forward * 1.6f + cat.transform.right * 0.6f + Vector3.up * 0.1f;
                    lm.Teleport(cp, Quaternion.LookRotation(cat.transform.position - cp));
                    lm.SetPitch(30f);
                    yield return new WaitForSeconds(0.2f);
                    yield return Shot(dir, "0s_cat");
                    // Picked up unasked: it pees. Then petted and picked up: it smiles.
                    cat.PickUpRpc(local.NetworkObject);
                    yield return new WaitForSeconds(0.25f);
                    yield return Shot(dir, "0s_cat_pee");
                    yield return new WaitForSeconds(2.6f);
                    cp = cat.transform.position + cat.transform.forward * 1.2f + Vector3.up * 0.1f;
                    lm.Teleport(cp, Quaternion.LookRotation(cat.transform.position - cp));
                    yield return null;
                    cat.UseRpc(local.NetworkObject);
                    yield return new WaitForSeconds(0.2f);
                    cat.PickUpRpc(local.NetworkObject);
                    lm.SetPitch(15f);
                    yield return new WaitForSeconds(0.35f);
                    yield return Shot(dir, "0s_cat_smile");
                    local.GetComponent<PlayerProgress>().ServerResetRun();
                }
                var tentFrom = Lobby.TentCenter + new Vector3(2.6f, 0.1f, 0f);
                lm.Teleport(tentFrom, Quaternion.LookRotation(Lobby.TentCenter + new Vector3(-2f, 0f, -0.6f) - tentFrom));
                lm.SetPitch(12f);
                yield return new WaitForSeconds(0.4f);
                yield return Shot(dir, "0s_tent_games");
                lm.SetPitch(0f);
                // The carriage at the gate, the road out, then a seat on its back and the ride.
                var ride = CarriageRide.Instance;
                if (ride != null)
                {
                    var side = ride.transform.position + new Vector3(-6.5f, 0.1f, -2.5f);
                    lm.Teleport(side, Quaternion.LookRotation(ride.transform.position + Vector3.forward * 1.5f - side));
                    lm.SetPitch(4f);
                    yield return new WaitForSeconds(0.5f);
                    yield return Shot(dir, "0w_carriage");
                    var gateIn = Lobby.Origin + new Vector3(1.5f, 0.1f, Lobby.Radius - 3f);
                    lm.Teleport(gateIn, Quaternion.LookRotation(Vector3.forward));
                    lm.SetPitch(-2f);
                    yield return new WaitForSeconds(0.5f);
                    yield return Shot(dir, "0w_forest_road");
                    lm.SetPitch(0f);
                    var me = local.GetComponent<PlayerLife>();
                    lm.Teleport(ride.SeatGround(1) + Vector3.up * 0.1f, Quaternion.LookRotation(Vector3.back));
                    yield return new WaitForSeconds(0.3f);
                    me.Seat.Value = 1;
                    yield return new WaitForSeconds(0.6f);
                    yield return Shot(dir, "0t_seated");
                    DungeonDirector.Instance.ServerBoard();
                    yield return new WaitForSeconds(3.6f);
                    yield return Shot(dir, "0t_departure_gate");
                    yield return new WaitForSeconds(2.2f);
                    yield return Shot(dir, "0t_departure_road");
                    for (float t = 0f; t < 6f && DungeonDirector.Instance.Phase.Value != RunPhase.Exploring; t += 0.2f) yield return new WaitForSeconds(0.2f);
                }
            }

            if (DungeonDirector.Instance.Phase.Value != RunPhase.Exploring) DungeonDirector.Instance.ServerStartRun();
            yield return new WaitForSeconds(2.5f);

            var layout = DungeonDirector.Layout;
            if (layout == null)
            {
                Debug.Log("[Smoke] FAIL screenshots: no local player or layout");
                Application.Quit(1);
                yield break;
            }

            var motor = local.GetComponent<FirstPersonMotor>();
            motor.enabled = false; // stop input and gravity fighting the staged camera

            yield return Shot(dir, "1_start_room");
            var sr = layout.StartRoom;
            var srFrom = sr.Center + new Vector3(-sr.Size.x * 0.42f, 0.1f, -sr.Size.y * 0.42f);
            motor.Teleport(srFrom, Quaternion.LookRotation(sr.Center - srFrom));
            motor.SetPitch(12f);
            yield return new WaitForSeconds(0.4f);
            yield return Shot(dir, "1_start_room_wide"); yield return SamplePerf("1_start_room_wide");
            // Blood on the flagstones (2026-11-01): a few blows' worth staged on the floor ahead.
            {
                var bcam = Art.ViewCamera.Current;
                var bf = bcam != null ? bcam.transform.forward : local.transform.forward;
                bf.y = 0f; bf = bf.sqrMagnitude > 0.01f ? bf.normalized : Vector3.forward;
                var bside = Vector3.Cross(Vector3.up, bf);
                Art.BloodSpatter.Splash(local.transform.position + bf * 5f, 0.2f);
                Art.BloodSpatter.Splash(local.transform.position + bf * 6f + bside * 1.2f, 0.12f);
                Art.BloodSpatter.Splash(local.transform.position + bf * 6.5f - bside * 1f, 0.2f);
                yield return new WaitForSeconds(0.4f);
                yield return Shot(dir, "2c_blood");
            }
            // A door in a doorway: shut from a distance, swinging open as you come up to it.
            var door = FindObjectsByType<SwingDoor>(FindObjectsSortMode.None).OrderBy(d => (d.transform.position - local.transform.position).sqrMagnitude).FirstOrDefault();
            if (door != null)
            {
                var dp = door.transform.position;
                var toRoom = DungeonDirector.Layout.Rooms.OrderBy(r => (r.Center - dp).sqrMagnitude).First().Center - dp; toRoom.y = 0f;
                var standD = dp + toRoom.normalized * 5.5f; standD.y = dp.y + 0.1f;
                motor.Teleport(standD, Quaternion.LookRotation(Vector3.ProjectOnPlane(dp - standD, Vector3.up)));
                motor.SetPitch(5f);
                yield return new WaitForSeconds(4.5f);
                yield return Shot(dir, "1p_door_closed");
                motor.Teleport(dp + toRoom.normalized * 3f + Vector3.up * 0.1f, Quaternion.LookRotation(Vector3.ProjectOnPlane(-toRoom, Vector3.up)));
                yield return new WaitForSeconds(0.8f);
                yield return Shot(dir, "1p_door_open");
                motor.SetPitch(0f);
            }
            // A rat by the wall, before it bolts.
            var rat = FindObjectsByType<Art.Critter>(FindObjectsSortMode.None).Where(c => c.Type == Art.Critter.Kind.Rat).OrderBy(c => (c.transform.position - local.transform.position).sqrMagnitude).FirstOrDefault();
            if (rat != null)
            {
                var room0 = DungeonDirector.Layout.Rooms.OrderBy(r => (r.Center - local.transform.position).sqrMagnitude).First();
                var stand = room0.Center + new Vector3(1.5f, 0f, 1.5f);
                var rp = room0.Center + new Vector3(2.6f, 0.01f, 2.6f);
                rat.DebugHold(rp);
                motor.Teleport(stand + Vector3.up * 0.1f, Quaternion.LookRotation(Vector3.ProjectOnPlane(rp - stand, Vector3.up)));
                motor.SetPitch(45f);
                yield return new WaitForSeconds(0.3f);
                yield return Shot(dir, "1_rat");
                motor.SetPitch(0f);
                rat.enabled = true;
            }
            motor.SetPitch(0f);
            var map = local.GetComponent<UI.Minimap>();
            map.Large = true;
            yield return null;
            yield return Shot(dir, "1a_large_map");
            map.Large = false;

            // Hollow walls: a secret one up close, then smash it to see the alcove; a breach as it bursts.
            foreach (var hw in FindObjectsByType<CrackedWall>(FindObjectsSortMode.None).OrderBy(x => x.Breach.Value).ToArray())
            {
                var hf = hw.transform.position - hw.transform.forward * 3.2f + Vector3.up * 0.1f;
                motor.Teleport(hf, Quaternion.LookRotation(hw.transform.forward));
                yield return new WaitForSeconds(0.6f);
                if (!hw.Breach.Value)
                {
                    yield return Shot(dir, "1f_secret_wall");
                    hw.GetComponent<Health>().ServerDamage(1000f, hf, local.NetworkObjectId);
                    yield return new WaitForSeconds(1.2f);
                    yield return Shot(dir, "1g_secret_alcove");
                }
                else
                {
                    hw.ServerTriggerBreach(null);
                    yield return new WaitForSeconds(1.0f);
                    yield return Shot(dir, "1h_breach_rumble");
                    yield return new WaitForSeconds(0.75f);
                    yield return Shot(dir, "1i_breach_open");
                    break;
                }
            }

            // Every feature room on the floor, from a high corner looking across it.
            foreach (var room in layout.Rooms)
            {
                if (room.Feature == RoomFeature.None) continue;
                var fp = room.Center + new Vector3(-room.Size.x * 0.42f, 0.1f, -room.Size.y * 0.42f);
                if (room.Feature != RoomFeature.Crypt)
                    foreach (var e in FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None))
                        if (!(e is BossBrain) && room.Contains(e.transform.position, 1f)) e.GetComponent<Health>().ServerDamage(1e6f, fp);
                var look = room.Center - fp;
                if (room.Feature == RoomFeature.Chasm)
                {
                    // At the lip, between a bridge and the corner, peering down.
                    fp = new Vector3(room.Pit.xMin - 0.8f, 0.1f, room.Pit.center.y + room.Pit.height * 0.25f);
                    look = Vector3.right;
                }
                motor.Teleport(fp, Quaternion.LookRotation(look));
                motor.SetPitch(room.Feature == RoomFeature.Chasm ? 50f : 0f);
                yield return new WaitForSeconds(1.2f);
                yield return Shot(dir, "1c_feature_" + room.Feature);
                motor.SetPitch(0f);
            }

            foreach (var room in layout.Rooms)
            {
                if (room.Kind != RoomKind.Combat) continue;
                var p = room.Center + new Vector3(-room.Size.x * 0.35f, 0.1f, -room.Size.y * 0.35f);
                motor.Teleport(p, Quaternion.LookRotation(room.Center - p));
                yield return new WaitForSeconds(1.5f);
                yield return Shot(dir, "2_combat_room"); yield return SamplePerf("2_combat_room");

                // Status tags: chill one, mark another, stun and taunt a third.
                var all = FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None).Where(e => !(e is BossBrain) && !e.IsDead).ToArray();
                var anchor = all.FirstOrDefault();
                var inRoom = anchor == null ? new EnemyBrain[0] : all.OrderBy(e => Vector3.Distance(e.transform.position, anchor.transform.position)).Take(3).ToArray();
                if (anchor != null)
                {
                    var look = anchor.transform.position + anchor.transform.forward * 5f + Vector3.up * 0.1f;
                    motor.Teleport(look, Quaternion.LookRotation(anchor.transform.position - look));
                    yield return new WaitForSeconds(0.3f);
                }
                if (inRoom.Length > 0) inRoom[0].GetComponent<Health>().ServerChill(10f);
                if (inRoom.Length > 1) inRoom[1].GetComponent<Health>().ServerMark(10f);
                if (inRoom.Length > 2) { inRoom[2].ServerStun(10f); inRoom[2].ServerTaunt(local.GetComponent<Health>(), 10f); }
                foreach (var e in inRoom) e.ServerStun(10f);
                yield return new WaitForSeconds(0.4f);
                yield return Shot(dir, "2a_status_tags");
                // Every look an elite can wear, in a row: ward, swift, frenzy, thorns, volatile, splitting, and an enraged brute.
                {
                    var row = new System.Collections.Generic.List<EnemyBrain>();
                    var affixes = new[] { EliteAffix.Warded, EliteAffix.Swift, EliteAffix.Frenzied, EliteAffix.Thorned, EliteAffix.Volatile, EliteAffix.Splitting };
                    var fwd = local.transform.forward; var side = local.transform.right;
                    for (int i = 0; i < affixes.Length; i++)
                    {
                        var e = DungeonDirector.Instance.ServerDebugSpawn("skeleton", local.transform.position + fwd * 6f + side * ((i - 2.5f) * 1.6f), false);
                        if (e == null) continue;
                        e.ServerMakeElite(affixes[i]);
                        if (affixes[i] == EliteAffix.Frenzied) e.GetComponent<Health>().ServerSetFraction(0.4f);
                        e.ServerStun(8f);
                        row.Add(e);
                    }
                    var rager = DungeonDirector.Instance.ServerDebugSpawn("brute", local.transform.position + fwd * 8.5f, false);
                    if (rager != null) { rager.ServerEnrage(8f); rager.ServerStun(8f); row.Add(rager); }
                    yield return new WaitForSeconds(0.8f);
                    yield return Shot(dir, "2a_elite_looks");
                    foreach (var e in row) if (e != null && e.IsSpawned) e.NetworkObject.Despawn();
                }
                // The Tome of the Phoenix in flight over the room, and the belt's fifth slot.
                {
                    var sp5 = local.GetComponent<Flasks>();
                    if (sp5 != null) sp5.Rare.Value = 0;
                    if (sp5 != null && sp5.ServerGiveRare(Progression.RareItem.PhoenixTome))
                    {
                        sp5.ServerUseRare();
                        yield return new WaitForSeconds(1.6f);
                        // Look at the bird where it wheels.
                        var birdNow = Phoenix.All.LastOrDefault();
                        if (birdNow != null)
                        {
                            var lookTo = birdNow.transform.position - (local.transform.position + Vector3.up * 1.6f);
                            local.GetComponent<FirstPersonMotor>().Teleport(local.transform.position, Quaternion.LookRotation(new Vector3(lookTo.x, 0f, lookTo.z)));
                            local.GetComponent<FirstPersonMotor>().SetPitch(-Mathf.Atan2(lookTo.y, new Vector2(lookTo.x, lookTo.z).magnitude) * Mathf.Rad2Deg);
                            yield return new WaitForSeconds(0.25f);
                        }
                        yield return Shot(dir, "2a_phoenix");
                        sp5.ServerGiveRare(Progression.RareItem.Bloodfury);
                        yield return null;
                        yield return Shot(dir, "2a_rare_slot");
                        local.GetComponent<FirstPersonMotor>().SetPitch(0f);
                        sp5.ServerReset(); sp5.ServerStartingPouch();
                        foreach (var bird in Phoenix.All.ToArray()) if (bird != null && bird.IsSpawned) bird.NetworkObject.Despawn();
                    }
                }
                // A hit landing on the nearest one: sparks, chips, the flinch.
                if (anchor != null)
                {
                    var close = anchor.transform.position + anchor.transform.forward * 2.2f + Vector3.up * 0.1f;
                    motor.Teleport(close, Quaternion.LookRotation(anchor.transform.position - close));
                    yield return new WaitForSeconds(0.25f);
                    anchor.GetComponent<Health>().ServerDamage(anchor.GetComponent<Health>().Max.Value * 0.15f, close, local.NetworkObjectId);
                    yield return new WaitForSeconds(0.06f);
                    yield return Shot(dir, "2c_hit_impact");
                    // The Kenney sprite effects, staged in front of the camera (2026-10-28).
                    {
                        var camT = Art.ViewCamera.Current != null ? Art.ViewCamera.Current.transform : local.transform;
                        var eye = camT.position;
                        var fwd = Vector3.ProjectOnPlane(camT.forward, Vector3.up).normalized;
                        var at = Art.Vfx.Floor(eye + fwd * 4.5f);
                        var right = Vector3.Cross(Vector3.up, fwd);
                        Art.Vfx.Flash(at + Vector3.up * 1.4f + right * -1.5f, new Color(1f, 0.6f, 0.25f), 6f, 10f, 1.5f);
                        Art.Vfx.Fireball(at + Vector3.up * 0.8f + right * 1.6f, 1.6f, new Color(1f, 0.5f, 0.15f));
                        Art.FxSprite.Scorch(at + right * 1.6f, 1.6f);
                        Art.FxSprite.MagicCircle(at, 1.8f, 3f, new Color(0.7f, 0.4f, 1f));
                        Art.Vfx.Smoke(at + right * -1.5f, 3, 0.8f, 2.5f);
                        Art.Vfx.HitImpact(at + Vector3.up * 1.2f, eye, 1f, new Color(1f, 0.9f, 0.6f));
                        yield return new WaitForSeconds(0.12f);
                        yield return Shot(dir, "2d_fx_sprites");
                        yield return new WaitForSeconds(0.6f);
                        yield return Shot(dir, "2d_fx_sprites_later");
                    }
                    // Mid-cut: the swing trail behind the blade.
                    local.ViewModelSwing(0.12f, 0.4f, 1f);
                    yield return new WaitForSeconds(0.2f);
                    yield return Shot(dir, "2d_swing_trail");
                    // The whole cut in slow motion, frame by frame (forehand, then backhand).
                    yield return new WaitForSeconds(0.6f);
                    Time.timeScale = 0.12f;
                    // Forehand, backhand, then the chain's third: the overhead chop.
                    int cut = 0;
                    foreach (float side in new[] { 1f, -1f, 1f })
                    {
                        local.ViewModelSwing(Player.KnightKit.SwingWindup, Player.KnightKit.SwingStrike + 0.3f, side);
                        string label = cut == 2 ? "over" : side > 0 ? "fore" : "back";
                        for (int f = 0; f < 6; f++)
                        {
                            yield return new WaitForSeconds(0.05f);
                            yield return Shot(dir, "2e_swing_" + label + f);
                        }
                        cut++;
                        yield return new WaitForSeconds(0.02f);
                    }
                    Time.timeScale = 1f;
                    // Idle long enough and the Knight turns his blade to look along it (2026-10-28).
                    yield return new WaitForSeconds(8.3f);
                    yield return Shot(dir, "2e_idle_inspect");
                    // The Rogue's paired stabs, the same way.
                    var pclass = local.GetComponent<PlayerProgress>();
                    var was = pclass.ClassId.Value;
                    pclass.ClassId.Value = Classes.ClassId.Rogue;
                    yield return new WaitForSeconds(0.8f);
                    Time.timeScale = 0.12f;
                    foreach (float side in new[] { 1f, -1f })
                    {
                        local.ViewModelSwing(0.04f, 0.3f, side);
                        for (int f = 0; f < 4; f++)
                        {
                            yield return new WaitForSeconds(0.035f);
                            yield return Shot(dir, "2f_stab_" + (side > 0 ? "r" : "l") + f);
                        }
                        yield return new WaitForSeconds(0.3f);
                    }
                    Time.timeScale = 1f;
                    pclass.ClassId.Value = was;
                    yield return new WaitForSeconds(0.6f);
                }

                // Kill everything in the room so the loot it leaves is visible.
                foreach (var e in FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None))
                {
                    if (!(e is BossBrain) && room.Contains(e.transform.position, 1f)) e.GetComponent<Health>().ServerDamage(1e6f, p);
                }
                yield return new WaitForSeconds(1.8f);
                yield return Shot(dir, "2b_loot");
                break;
            }

            var boss = layout.BossRoom;
            var bp = boss.Center + (layout.BossSpawn - boss.Center).normalized * -9f + Vector3.up * 0.1f;
            motor.Teleport(bp, Quaternion.LookRotation(layout.BossSpawn - bp));
            yield return new WaitForSeconds(2.4f);
            yield return Shot(dir, "3_boss_room"); yield return SamplePerf("3_boss_room");
            {
                // Every arena device in one frame: a healing pylon, a ballista, a lit sanctum brazier, a winch and its chandelier.
                var dd = DungeonDirector.Instance;
                var side = Vector3.Cross(Vector3.up, (layout.BossSpawn - bp).normalized);
                var row = bp + (layout.BossSpawn - bp).normalized * 5f;
                dd.ServerDebugPylon(row - side * 3f);
                var balS = dd.ServerDebugEngine(ArenaEngine.Kind.Ballista, row - side * 1f, Vector3.zero);
                var brS = dd.ServerDebugEngine(ArenaEngine.Kind.Brazier, row + side * 1f, Vector3.zero);
                var wiS = dd.ServerDebugEngine(ArenaEngine.Kind.Winch, row + side * 3f, row + side * 3f + (layout.BossSpawn - bp).normalized * 4f);
                if (brS != null) brS.Lit.Value = true;
                motor.SetPitch(-6f);
                yield return new WaitForSeconds(1.2f);
                yield return Shot(dir, "3b_arena_devices");
                motor.SetPitch(0f);
            }

            // Turn round to see a corridor mouth from inside the boss room.
            motor.Teleport(bp, Quaternion.LookRotation(bp - layout.BossSpawn));
            yield return new WaitForSeconds(0.5f);
            yield return Shot(dir, "4_boss_room_back");

            // A corridor flame trap, mid-burst, and the floor's champion.
            if (layout.FlameTraps.Count > 0)
            {
                var trap = layout.FlameTraps[0];
                var along = Vector3.Cross(trap.Across, Vector3.up);
                var tp = trap.At + along * 4f + Vector3.up * 0.1f;
                motor.Teleport(tp, Quaternion.LookRotation(trap.At - tp));
                for (int w = 0; w < 120 && !FlameJet.Lit(trap.Phase, Pendulum.Clock + 0.2); w++) yield return new WaitForSeconds(0.05f);
                yield return new WaitForSeconds(0.35f);
                yield return Shot(dir, "1j_flame_trap");
            }
            // A spike plate as it lies in the open, then the same plate hidden in flagstone, then the Ranger's net beside it.
            var shotPlate = FindObjectsByType<SpikeTrap>(FindObjectsSortMode.None).FirstOrDefault();
            if (shotPlate != null)
            {
                var pp = shotPlate.transform.position;
                var away = (layout.RoomAt(pp, 0f) != null ? layout.RoomAt(pp, 0f).Center - pp : -local.transform.forward);
                away.y = 0f;
                var tp = pp + (away.sqrMagnitude > 0.1f ? away.normalized : Vector3.forward) * 3.6f + Vector3.up * 0.1f;
                motor.Teleport(tp, Quaternion.LookRotation(new Vector3(pp.x - tp.x, 0f, pp.z - tp.z)));
                motor.SetPitch(28f);
                shotPlate.Hidden.Value = false;
                yield return new WaitForSeconds(0.3f);
                yield return Shot(dir, "1j2_spike_plate");
                shotPlate.Hidden.Value = true;
                yield return new WaitForSeconds(0.3f);
                yield return Shot(dir, "1j3_spike_plate_hidden");
                shotPlate.Hidden.Value = false;
                Art.SnareNet.Spawn(pp, Player.RangerKit.SnareRadius, 3f);
                yield return new WaitForSeconds(0.45f);
                yield return Shot(dir, "1j4_snare_net");
                motor.SetPitch(0f);
                yield return new WaitForSeconds(0.2f);
            }
            var champ = FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None).FirstOrDefault(e => e.IsChampion && !e.IsDead);
            if (champ != null)
            {
                champ.ServerStun(30f);
                champ.ServerAddThreat(local.NetworkObjectId, 60f);   // its rage meter shows (and at 4 m a melee class reads OUT OF RANGE)
                var cp = champ.transform.position + champ.transform.forward * 4f + Vector3.up * 0.1f;
                motor.Teleport(cp, Quaternion.LookRotation(champ.transform.position - cp));
                yield return new WaitForSeconds(0.6f);
                yield return Shot(dir, "1k_champion");
            }

            var wheelS = FindObjectsByType<FateWheel>(FindObjectsSortMode.None).FirstOrDefault();
            if (wheelS != null)
            {
                var wp = wheelS.transform.position + wheelS.transform.forward * 4.5f + Vector3.up * 0.1f;
                motor.Teleport(wp, Quaternion.LookRotation(wheelS.transform.position + Vector3.up * 1.2f - wp - Vector3.up * 1.6f));
                yield return new WaitForSeconds(0.6f);
                yield return Shot(dir, "1l_wheel_of_fate");
            }

            // The Reliquary, resting in its side room.
            var relS = Reliquary.Current;
            if (relS != null)
            {
                var rp = relS.transform.position + new Vector3(1.8f, 0.1f, 1.8f);
                motor.Teleport(rp, Quaternion.LookRotation(relS.transform.position + Vector3.up * 0.4f - rp - Vector3.up * 1.6f));
                yield return new WaitForSeconds(0.6f);
                yield return Shot(dir, "1m_reliquary");
            }

            {
                var lectern = FindObjectsByType<LorePage>(FindObjectsSortMode.None).FirstOrDefault();
                if (lectern != null)
                {
                    var lpp = lectern.transform.position - lectern.transform.forward * 1.6f + Vector3.up * 0.1f;
                    motor.Teleport(lpp, Quaternion.LookRotation(lectern.transform.position - lpp));
                    yield return new WaitForSeconds(0.4f);
                    yield return Shot(dir, "1o_lectern");
                    lectern.DebugRead();
                    yield return null;
                    yield return Shot(dir, "1o_journal_page");
                }
                var lever = FindObjectsByType<Lever>(FindObjectsSortMode.None).FirstOrDefault();
                if (lever != null)
                {
                    var lp = lever.transform.position + lever.transform.forward * 2.2f + Vector3.up * 0.1f;
                    motor.Teleport(lp, Quaternion.LookRotation(lever.transform.position + Vector3.up * 1.3f - lp - Vector3.up * 1.6f));
                    yield return new WaitForSeconds(0.5f);
                    yield return Shot(dir, "1n_vault_lever");
                }
                var bossNow = BossBrain.Active;
                if (bossNow != null && !bossNow.IsDead)
                {
                    var bpos = bossNow.transform.position;
                    var bfrom = bpos + (layout.BossRoom.Center - bpos).normalized * 8f + Vector3.up * 0.1f;
                    if ((bfrom - bpos).sqrMagnitude < 4f) bfrom = bpos + Vector3.back * 8f + Vector3.up * 0.1f;
                    motor.Teleport(bfrom, Quaternion.LookRotation(Vector3.ProjectOnPlane(bpos - bfrom, Vector3.up)));
                    yield return new WaitForSeconds(0.3f);
                    bossNow.Health.ServerDamage(1e6f, bossNow.transform.position);
                    yield return new WaitForSeconds(0.5f);
                    yield return Shot(dir, "4b_boss_falls");
                    yield return new WaitForSeconds(0.7f);
                    var ex = DungeonDirector.ExitPosition;
                    var ep = ex + (layout.BossRoom.Center - ex).normalized * 7f + Vector3.up * 0.1f;
                    if ((ep - ex).sqrMagnitude < 1f) ep = ex + Vector3.back * 7f + Vector3.up * 0.1f;
                    motor.Teleport(ep, Quaternion.LookRotation(ex - ep));
                    yield return new WaitForSeconds(7f);
                    yield return Shot(dir, "4c_exit_portal");
                }
            }

            // One look at each later floor's theme.
            var shotFeatures = new System.Collections.Generic.HashSet<RoomFeature>(layout.Rooms.Select(r => r.Feature));
            // An act's second floor: the same place, deeper and darker.
            DungeonDirector.Instance.ServerDebugStartFloor(3);
            yield return new WaitForSeconds(2.5f);
            var deepRoom = DungeonDirector.Layout.Rooms.FirstOrDefault(r => r.Kind == RoomKind.Combat);
            if (deepRoom != null)
            {
                var df = deepRoom.Center + new Vector3(-deepRoom.Size.x * 0.4f, 0.1f, -deepRoom.Size.y * 0.4f);
                motor.Teleport(df, Quaternion.LookRotation(deepRoom.Center - df));
                yield return new WaitForSeconds(1.2f);
                yield return Shot(dir, "5_floor4_deeper");
            }

            // One floor per later act (each the act's first: a Warden waits at the end of it).
            for (int depth = 2; depth <= DungeonDirector.AbyssDepth; depth += 2)
            {
                DungeonDirector.Instance.ServerDebugStartFloor(depth);
                yield return new WaitForSeconds(2.5f);
                var l = DungeonDirector.Layout;
                // The Warden, asleep in its arena.
                var wardenS = BossBrain.Active;
                if (wardenS != null)
                {
                    var wv = wardenS.transform.position + wardenS.transform.forward * 7f + Vector3.up * 0.1f;
                    motor.Teleport(wv, Quaternion.LookRotation(wardenS.transform.position - wv));
                    motor.SetPitch(-8f);
                    yield return new WaitForSeconds(1.2f);
                    yield return Shot(dir, "3w_warden_floor" + (depth + 1));
                    motor.SetPitch(0f);
                }
                // Any room mechanic not seen yet: from a corner, and (galleries) from the balcony.
                foreach (var room in l.Rooms)
                {
                    if (room.Feature == RoomFeature.None || !shotFeatures.Add(room.Feature)) continue;
                    var fromF = room.Center + new Vector3(-room.Size.x * 0.42f, 0.1f, -room.Size.y * 0.42f);
                    foreach (var e in FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None))
                        if (!(e is BossBrain) && room.Contains(e.transform.position, 1f)) e.GetComponent<Health>().ServerDamage(1e6f, fromF);
                    motor.Teleport(fromF, Quaternion.LookRotation(room.Center - fromF));
                    yield return new WaitForSeconds(1.4f);
                    yield return Shot(dir, "1c_feature_" + room.Feature + "_floor" + (depth + 1));
                    if (room.Balconies.Count > 0)
                    {
                        var d = DungeonLayout.Dir[room.Balconies[0]];
                        bool alongX = room.Balconies[0] == 0 || room.Balconies[0] == 2;
                        var deck = room.Center + new Vector3(d.x, 0f, d.y) * ((alongX ? room.Size.y : room.Size.x) / 2f - 1.6f) + Vector3.up * (DungeonLayout.BalconyHeight + 0.1f);
                        motor.Teleport(deck, Quaternion.LookRotation(room.Center - deck - Vector3.up * 2f));
                        yield return new WaitForSeconds(1f);
                        yield return Shot(dir, "1c_feature_Gallery_balcony");
                    }
                }
                // The third floor: a staged oath in the start room (one plate held, the line half drawn),
                // then the ROOM CLEARED stamp and a floor event's badge.
                if (depth == 2)
                {
                    var oathSr = l.StartRoom;
                    var pa = oathSr.Center + Vector3.left * 3f;
                    var pb = oathSr.Center + Vector3.right * 3f;
                    var staged = DungeonDirector.Instance.ServerDebugOath(pa, pb, oathSr.Center + Vector3.forward * 4f);
                    var view = oathSr.Center + Vector3.back * 4.5f + Vector3.up * 0.1f;
                    motor.Teleport(view, Quaternion.LookRotation(oathSr.Center - view));
                    motor.SetPitch(22f);
                    yield return new WaitForSeconds(0.8f);
                    yield return Shot(dir, "1d_oath_plates");
                    motor.SetPitch(0f);
                    if (staged != null && staged.IsSpawned) staged.NetworkObject.Despawn();
                    foreach (var st in Carryable.All.OfType<OathStone>().ToList()) if (st != null && st.IsSpawned) st.NetworkObject.Despawn();
                    // The castle cat, asleep far below camp.
                    var napAt = oathSr.Center + Vector3.right * 3.6f + Vector3.forward * 3.2f;
                    var nap = DungeonDirector.Instance.ServerDebugShaftCat(napAt);
                    var napView = napAt + new Vector3(-1.4f, 0.1f, -1.4f);
                    motor.Teleport(napView, Quaternion.LookRotation(napAt - napView));
                    motor.SetPitch(38f);
                    yield return new WaitForSeconds(0.6f);
                    yield return Shot(dir, "1d_shaft_cat");
                    motor.SetPitch(0f);
                    if (nap != null && nap.IsSpawned) nap.NetworkObject.Despawn();
                    UI.GameHud.RoomCleared(true);
                    DungeonDirector.Instance.ServerDebugEvent(FloorEvent.Gilded);
                    UI.StatusEffects.DebugHover = 0;
                    yield return new WaitForSeconds(0.3f);
                    yield return Shot(dir, "1d_room_cleared_event");
                    UI.StatusEffects.DebugHover = -1;
                    DungeonDirector.Instance.ServerDebugEvent(FloorEvent.None);
                }
                // An Oath room, if this floor has one: the two plates and the stone, from the door.
                var oathRoom = l.Rooms.FirstOrDefault(r => r.Feature == RoomFeature.Oath);
                if (oathRoom != null && oathRoom.FeatureSpots.Count >= 4)
                {
                    var ofrom = oathRoom.FeatureSpots[2] + (oathRoom.FeatureSpots[2] - oathRoom.FeatureSpots[3]).normalized * 1.5f + Vector3.up * 0.1f;
                    motor.Teleport(ofrom, Quaternion.LookRotation(oathRoom.FeatureSpots[3] - ofrom));
                    motor.SetPitch(12f);
                    yield return new WaitForSeconds(0.8f);
                    yield return Shot(dir, "1d_oath_room_floor" + (depth + 1));
                    motor.SetPitch(0f);
                }
                foreach (var room in l.Rooms)
                {
                    if (room.Kind != RoomKind.Combat) continue;
                    var from = room.Center + new Vector3(-room.Size.x * 0.4f, 0.1f, -room.Size.y * 0.4f);
                    motor.Teleport(from, Quaternion.LookRotation(room.Center - from));
                    yield return new WaitForSeconds(1.2f);
                    yield return Shot(dir, "5_floor" + (depth + 1));
                    if (DungeonDirector.Act(depth) == 1)
                    {
                        // The water thickens: murk, slow ripples, and the MIRED badge (read by hovering it).
                        DungeonDirector.Instance.ServerDebugTide();
                        yield return new WaitForSeconds(1.5f);
                        UI.StatusEffects.DebugHover = 0;
                        yield return Shot(dir, "5_floor3_mire");
                        UI.StatusEffects.DebugHover = -1;
                    }
                    // This floor's own dressing, up close.
                    if (DungeonBuilder.ThemeSpots.Count > 0)
                    {
                        var (tat, tin) = DungeonBuilder.ThemeSpots[DungeonBuilder.ThemeSpots.Count / 2];
                        var tfrom = tat + tin * 3.4f + Vector3.up * 0.1f;
                        motor.Teleport(tfrom, Quaternion.LookRotation(-tin));
                        motor.SetPitch(14f);
                        yield return new WaitForSeconds(0.8f);
                        yield return Shot(dir, "5_floor" + (depth + 1) + "_dressing");
                        motor.SetPitch(0f);
                        motor.Teleport(from, Quaternion.LookRotation(room.Center - from));
                        yield return new WaitForSeconds(0.3f);
                    }

                    // Line up one of each newer enemy type for a close look.
                    foreach (var type in new[] { typeof(GhoulBrain), typeof(BruteBrain), typeof(NecromancerBrain), typeof(WraithBrain), typeof(ShieldbearerBrain), typeof(HoarderBrain), typeof(GargoyleBrain), typeof(PlaguebearerBrain), typeof(FrostWightBrain), typeof(HexerBrain), typeof(EntlingBrain), typeof(OrbweaverBrain), typeof(ChoirAcolyteBrain), typeof(AbyssEyeBrain), typeof(BonePiperBrain), typeof(CantorBrain) })
                    {
                        var e = FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None).FirstOrDefault(b => b.GetType() == type && !b.IsDead);
                        if (e == null) continue;
                        e.ServerStun(30f);
                        var ep = e.transform.position + e.transform.forward * 3.2f + Vector3.up * 0.1f;
                        motor.Teleport(ep, Quaternion.LookRotation(e.transform.position - ep));
                        yield return new WaitForSeconds(0.5f);
                        yield return Shot(dir, "7_" + type.Name + "_floor" + (depth + 1));
                    }

                    // The newer afflictions on the HUD (2026-10-27), once: SILENCED, then DAZED.
                    if (depth == 6)
                    {
                        var sdeb = local.GetComponent<PlayerDebuffs>();
                        motor.Teleport(from, Quaternion.LookRotation(room.Center - from));
                        sdeb.ServerCleanse();
                        sdeb.ServerSilence(4f);
                        yield return new WaitForSeconds(0.6f);
                        yield return Shot(dir, "2b_silenced");
                        sdeb.ServerCleanse();
                        sdeb.ServerDaze(3f);
                        yield return new WaitForSeconds(0.6f);
                        yield return Shot(dir, "2b_dazed");
                        sdeb.ServerCleanse();
                        // Each affliction's picture at the edge of the view (2026-10-28).
                        sdeb.ServerResetGrace();
                        sdeb.ServerRoot(4f);
                        yield return new WaitForSeconds(0.9f);
                        yield return Shot(dir, "2b_rooted_ice");
                        sdeb.ServerCleanse();
                        sdeb.ServerHoldRoots(local.NetworkObjectId);
                        yield return new WaitForSeconds(0.9f);
                        yield return Shot(dir, "2b_entangled");
                        sdeb.ServerReleaseRoots(local.NetworkObjectId);
                        sdeb.ServerPoison(4f, 0.1f);
                        yield return new WaitForSeconds(0.9f);
                        yield return Shot(dir, "2b_poisoned");
                        sdeb.ServerCleanse();
                        sdeb.ServerCurse(4f, ulong.MaxValue);
                        yield return new WaitForSeconds(0.9f);
                        yield return Shot(dir, "2b_cursed");
                        sdeb.ServerCleanse();
                        sdeb.ServerResetGrace();
                        sdeb.ServerMania(1.5f);
                        yield return new WaitForSeconds(0.5f);
                        yield return Shot(dir, "2b_mania");
                        sdeb.ServerCleanse();
                        // The potions' own looks (2026-10-28).
                        var pouchS = local.GetComponent<Flasks>();
                        var lifeS = local.GetComponent<Health>();
                        lifeS.ServerSetFraction(0.4f);
                        pouchS.ServerAdd(Flasks.Kind.Health, 1);
                        pouchS.ServerDebugDrink(Flasks.Kind.Health);
                        yield return new WaitForSeconds(1.2f);
                        yield return Shot(dir, "2c_potion_mend");
                        lifeS.ServerSetFraction(1f);
                        pouchS.ServerAdd(Flasks.Kind.Mana, 1);
                        pouchS.ServerDebugDrink(Flasks.Kind.Mana);
                        yield return new WaitForSeconds(0.9f);
                        yield return Shot(dir, "2c_potion_draught");
                        yield return new WaitForSeconds(3f);
                        if (pouchS.ServerGiveRare(Progression.RareItem.Ironbark) && pouchS.ServerUseRare())
                        {
                            yield return new WaitForSeconds(1f);
                            yield return Shot(dir, "2c_ironbark");
                        }
                        yield return new WaitForSeconds(8f);
                        if (pouchS.ServerGiveRare(Progression.RareItem.Bloodfury) && pouchS.ServerUseRare())
                        {
                            yield return new WaitForSeconds(1f);
                            yield return Shot(dir, "2c_bloodfury");
                        }
                        sdeb.ServerResetGrace();
                        sdeb.ServerPoison(3f, 0.1f);
                        yield return new WaitForSeconds(0.6f);
                        sdeb.ServerCleanse();
                        yield return new WaitForSeconds(0.12f);
                        yield return Shot(dir, "2c_cleansed");
                    }

                    // The Bone Piper: a close look, then each tune in full flight.
                    if (depth == 4)
                    {
                        var pFrom = room.Center + new Vector3(0f, 0.1f, -6f);
                        var piper = DungeonDirector.Instance.ServerDebugSpawn("piper", room.Center + new Vector3(0f, 0f, 3f), false) as BonePiperBrain;
                        if (piper != null)
                        {
                            piper.ServerStun(2f);
                            var close = piper.transform.position + Vector3.forward * -2.6f + Vector3.up * 0.1f;
                            motor.Teleport(close, Quaternion.LookRotation(piper.transform.position - close));
                            yield return new WaitForSeconds(0.6f);
                            yield return Shot(dir, "7_BonePiper_close");
                            yield return new WaitForSeconds(1.6f);
                            motor.Teleport(pFrom, Quaternion.LookRotation(piper.transform.position - pFrom));
                            float[] at = { 1.05f, 1.2f, 1.0f, 1.25f };
                            foreach (BonePiperBrain.Song sg in System.Enum.GetValues(typeof(BonePiperBrain.Song)))
                            {
                                piper.ServerBeginSong(sg);
                                yield return new WaitForSeconds(at[(int)sg]);
                                yield return Shot(dir, "7_BonePiper_" + sg);
                                yield return new WaitForSeconds(2.2f);
                            }
                            piper.GetComponent<Health>().ServerDamage(1e6f, piper.transform.position);
                        }
                    }

                    // The Mourner (never spawned in tests): one weeping far off, one caught reaching up close.
                    if (depth == 2)
                    {
                        var mournFrom = room.Center + Vector3.up * 0.1f;
                        var far = DungeonDirector.Instance.ServerDebugSpawnMourner(room.Center + new Vector3(-1.5f, 0f, 7f), Vector3.back);
                        var near = DungeonDirector.Instance.ServerDebugSpawnMourner(room.Center + new Vector3(1.4f, 0f, 2.6f), Vector3.back);
                        motor.Teleport(mournFrom, Quaternion.LookRotation(Vector3.forward));
                        yield return new WaitForSeconds(0.8f);
                        yield return Shot(dir, "7_MournerBrain_floor3");
                        foreach (var mn in new[] { far, near }) if (mn != null) mn.GetComponent<Health>().ServerDamage(1e6f, mn.transform.position);
                    }

                    // The Tallyman, walking in out of the dark (the screenshot player takes no damage).
                    if (depth == 2)
                    {
                        var tfromC = room.Center + Vector3.up * 0.1f;
                        motor.Teleport(tfromC, Quaternion.LookRotation(Vector3.forward));
                        var tm = DungeonDirector.Instance.ServerDebugTally(room.Center + Vector3.forward * 8f, stay: true);
                        yield return new WaitForSeconds(0.9f);
                        yield return Shot(dir, "7_Tallyman_far");
                        yield return new WaitForSeconds(0.9f);
                        yield return Shot(dir, "7_Tallyman_near");
                        DungeonDirector.Instance.ServerDebugDismissTally();
                        yield return new WaitForSeconds(3f);
                    }

                    var archer = FindObjectsByType<ArcherBrain>(FindObjectsSortMode.None).FirstOrDefault();
                    if (archer != null)
                    {
                        archer.ServerStun(30f);
                        var ap = archer.transform.position + archer.transform.forward * 2.2f + Vector3.up * 0.1f;
                        motor.Teleport(ap, Quaternion.LookRotation(archer.transform.position - ap));
                        yield return new WaitForSeconds(0.6f);
                        yield return Shot(dir, "6_archer_closeup" + (depth + 1));
                    }
                    break;
                }
            }

            // A chest, the pause menu, and the end-of-run summary.
            var someChest = FindObjectsByType<Chest>(FindObjectsSortMode.None).FirstOrDefault();
            if (someChest != null)
            {
                // Stand on whichever side of it is open (a chest's back is often to a wall), looking down at it.
                var cpos = someChest.transform.position;
                var side = someChest.transform.forward;
                foreach (var d in new[] { someChest.transform.forward, someChest.transform.right, -someChest.transform.right, -someChest.transform.forward })
                    if (!Physics.Raycast(cpos + Vector3.up, d, 2.8f)) { side = d; break; }
                var cp = cpos + side * 2.3f + Vector3.up * 0.1f;
                var flat = cpos - cp; flat.y = 0f;
                motor.Teleport(cp, Quaternion.LookRotation(flat));
                motor.SetPitch(28f);
                yield return new WaitForSeconds(0.6f);
                yield return Shot(dir, "8_chest");
                someChest.UseRpc(local.NetworkObject);
                yield return new WaitForSeconds(1f);
                yield return Shot(dir, "8b_chest_open");
                motor.SetPitch(0f);
            }
            var someUrn = FindObjectsByType<Breakable>(FindObjectsSortMode.None).FirstOrDefault();
            if (someUrn != null)
            {
                var up = someUrn.transform.position + (DungeonDirector.Layout.RoomAt(someUrn.transform.position, 2f)?.Center - someUrn.transform.position ?? Vector3.forward).normalized * 2.4f + Vector3.up * 0.1f;
                motor.Teleport(up, Quaternion.LookRotation(someUrn.transform.position - up));
                yield return new WaitForSeconds(0.6f);
                yield return Shot(dir, "8c_urns_barrels");
            }

            UI.PauseMenu.SetOpen(true);
            yield return null;
            yield return Shot(dir, "9_pause");
            UI.PauseMenu.DebugShowOptions(local.GetComponent<UI.PauseMenu>() ?? FindFirstObjectByType<UI.PauseMenu>());
            yield return null;
            yield return Shot(dir, "9c_options");
            UI.OptionsPanel.Tab = 1;
            yield return null;
            yield return Shot(dir, "9c_options_display");
            UI.OptionsPanel.Tab = 2;
            yield return null;
            yield return Shot(dir, "9c_options_audio");
            UI.OptionsPanel.Tab = 0;
            UI.PauseMenu.DebugShowControls(local.GetComponent<UI.PauseMenu>() ?? FindFirstObjectByType<UI.PauseMenu>());
            yield return null;
            yield return Shot(dir, "9c_controls");
            UI.PauseMenu.SetOpen(false);
            UI.RunSummary.DebugEpilogueAt = 12.5f;
            yield return null;
            yield return Shot(dir, "9d_epilogue");
            UI.RunSummary.DebugEpilogueAt = -1f;
            UI.PauseMenu.DebugShowGuide(local.GetComponent<UI.PauseMenu>() ?? FindFirstObjectByType<UI.PauseMenu>());
            yield return null;
            yield return Shot(dir, "9a_guide");
            UI.PauseMenu.DebugShowGuide(local.GetComponent<UI.PauseMenu>() ?? FindFirstObjectByType<UI.PauseMenu>(), 1);
            yield return null;
            yield return Shot(dir, "9a_guide_dungeon");
            UI.PauseMenu.DebugShowGuide(local.GetComponent<UI.PauseMenu>() ?? FindFirstObjectByType<UI.PauseMenu>(), 2);
            yield return null;
            yield return Shot(dir, "9a_guide_journal");
            UI.PauseMenu.SetOpen(false);

            shotsImmortal = false;
            foreach (var p in PlayerLife.All) p.Health.ServerDamage(1e6f, p.transform.position);
            yield return new WaitForSeconds(1f);
            yield return Shot(dir, "9b_run_summary");

            Debug.Log("[Smoke] screenshots done");
            Application.Quit(0);
        }

        /// <summary>
        /// Boss mechanics check: for each floor boss, drop the host into the arena, keep it
        /// alive, and let the fight run while logging every mechanic ([Boss] lines) and
        /// capturing frames. Run windowed: <c>-bosstest C:\\temp\\boss</c>.
        /// </summary>
        public void BossTest(string dir, NetworkLauncher launcher) => StartCoroutine(BossSequence(dir, launcher));

        private IEnumerator BossSequence(string dir, NetworkLauncher launcher)
        {
            System.IO.Directory.CreateDirectory(dir);
            launcher.StartHost();
            yield return new WaitForSeconds(2f);

            var local = NetworkPlayer.Local;
            var motor = local.GetComponent<FirstPersonMotor>();
            var hp = local.GetComponent<Health>();
            // The test player takes a tenth of every hit: the run must not wipe mid-test.
            hp.IncomingModifiers.Insert(0, (d, f) => d * 0.1f);

            // Boss practice (2026-10-27): from camp, the guardian alone; it falls, the party is back at camp unchanged.
            {
                var pd = DungeonDirector.Instance;
                for (float t = 0f; t < 5f && !DungeonDirector.InLobby; t += 0.2f) yield return new WaitForSeconds(0.2f);
                pd.ServerStartPractice(1);
                yield return new WaitForSeconds(2.5f);
                var pboss = BossBrain.Active;
                string pname = pboss != null ? pboss.DisplayName : "none";
                int others = FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None).Count(e => !e.IsDead && !(e is BossBrain));
                bool inPractice = DungeonDirector.InPractice;
                float scale = Health.FloorPlayerDamageScale;
                float distToArena = pboss != null ? Vector3.Distance(local.transform.position, DungeonDirector.Layout.BossRoom.Center) : -1f;
                yield return Shot(dir, "practice_arrive");
                if (pboss != null) pboss.GetComponent<Health>().ServerDamage(1e6f, pboss.transform.position, local.NetworkObjectId);
                for (float t = 0f; t < DungeonDirector.PracticeEndDelay + 6f && !DungeonDirector.InLobby; t += 0.2f) { hp.ServerSetFraction(1f); yield return new WaitForSeconds(0.2f); }
                yield return new WaitForSeconds(1f);
                Log("boss practice", "boss " + pname + ", others " + others + ", in practice " + inPractice + ", taken x" + scale.ToString("F2") + ", arrived " + distToArena.ToString("F1") + " m from the arena centre, back at camp " + DungeonDirector.InLobby + ", run kept off " + !pd.RunInProgress,
                    pname != "none" && inPractice && Mathf.Abs(scale - DungeonDirector.PracticeTaken) < 0.01f && distToArena >= 0f && distToArena < 14f && DungeonDirector.InLobby && !pd.RunInProgress && !pd.Practice.Value);
            }

            for (int depth = 0; depth <= DungeonDirector.FinalDepth; depth++)
            {
                DungeonDirector.Instance.ServerDebugStartFloor(depth);
                yield return new WaitForSeconds(2f);
                motor.enabled = false;

                var layout = DungeonDirector.Layout;
                var boss = BossBrain.Active;
                Debug.Log("[Smoke] boss test floor " + (depth + 1) + ": " + (boss != null ? boss.DisplayName : "NO BOSS"));
                if (boss == null) continue;

                // Stand across the room, in its line of sight, so it has to come — or shoot.
                var at = layout.BossRoom.Center - (layout.BossSpawn - layout.BossRoom.Center).normalized * 8f + Vector3.up * 0.1f;
                motor.Teleport(at, Quaternion.LookRotation(boss.transform.position - at));

                for (int i = 0; i < 14; i++)
                {
                    yield return new WaitForSeconds(1.2f);
                    hp.ServerSetFraction(1f);
                    var look = boss.transform.position - local.transform.position;
                    look.y = 0f;
                    if (look.sqrMagnitude > 0.1f) local.transform.rotation = Quaternion.LookRotation(look);
                    if (i % 3 == 1) yield return Shot(dir, "boss" + (depth + 1) + "_" + i);
                    // Walk toward the Lich mid-test, to provoke the blink.
                    // Wound it halfway through, to see the late-fight phases (summons, surges, enrage).
                    if (boss is TideLichBrain && i == 6) motor.Teleport(boss.transform.position + boss.transform.forward * 2f, local.transform.rotation);
                }

                // The Hollow King's decrees (2026-10-27): standing still obeys BE STILL; in plain sight, BEGONE strikes.
                if (boss is HollowKingBrain king)
                {
                    FaceBoss(local, boss);
                    king.ServerDebugDecree(0);
                    yield return new WaitForSeconds(0.6f);
                    yield return Shot(dir, "boss" + (depth + 1) + "_decree");
                    for (float t = 0f; t < HollowKingBrain.DecreeWarning + 1.5f && king.DecreesFallen == 0; t += 0.1f) { hp.ServerSetFraction(1f); yield return new WaitForSeconds(0.1f); }
                    int stillStruck = king.LastDecreeStruck;
                    king.ServerDebugDecree(1);
                    for (float t = 0f; t < HollowKingBrain.DecreeWarning + 1.5f && king.DecreesFallen < 2; t += 0.1f) { hp.ServerSetFraction(1f); yield return new WaitForSeconds(0.1f); }
                    bool seen = CombatQuery.ClearLine(king.transform.position + Vector3.up * 2.6f, local.GetComponent<Health>());
                    Log("king decrees", "fallen " + king.DecreesFallen + ", still struck " + stillStruck + ", begone struck " + king.LastDecreeStruck + " (in sight " + seen + ")",
                        king.DecreesFallen >= 2 && stillStruck == 0 && (!seen || king.LastDecreeStruck == 1));
                }

                // The Wardens' second moves (2026-10-26).
                if (boss is WardenBrain warden)
                {
                    int before = warden.SecondMoves;
                    bool began = warden.ServerDebugSecond();
                    for (int k = 0; k < 20; k++) { hp.ServerSetFraction(1f); yield return new WaitForSeconds(0.2f); }
                    FaceBoss(local, boss);
                    yield return Shot(dir, "boss" + (depth + 1) + "_second");
                    bool ok = warden.Act == 2 ? began : warden.SecondMoves > before && (warden.Act != 4 || warden.RotPools > 0);
                    Log("warden second", warden.DisplayName + ": began " + began + ", moves " + (warden.SecondMoves - before) + (warden.Act == 4 ? ", rot pools " + warden.RotPools : ""), ok);
                }

                // The newer set pieces (2026-10-26): the Cardinal's brand, the Unsung's echoes and eclipse.
                if (boss is CardinalBrain cardinal)
                {
                    bool branded = cardinal.ServerDebugBrand();
                    yield return new WaitForSeconds(1.5f);
                    FaceBoss(local, boss);
                    yield return Shot(dir, "boss" + (depth + 1) + "_brand");
                    for (float t = 0f; t < CardinalBrain.BrandSeconds + 1.5f && cardinal.BrandsBurst == 0; t += 0.2f) { hp.ServerSetFraction(1f); yield return new WaitForSeconds(0.2f); }
                    Log("cardinal brand", "branded " + branded + ", burst " + cardinal.BrandsBurst + ", allies caught " + cardinal.LastBrandCaught, branded && cardinal.BrandsBurst == 1);
                }
                if (boss is UnsungBrain unsung)
                {
                    for (int e = 0; e < 4; e++)
                    {
                        unsung.ServerDebugEcho(e);
                        yield return new WaitForSeconds(0.8f);
                        FaceBoss(local, boss);
                        yield return Shot(dir, "boss" + (depth + 1) + "_echo" + e);
                        for (int k = 0; k < 10; k++) { hp.ServerSetFraction(1f); yield return new WaitForSeconds(0.2f); }
                    }
                    Log("unsung echoes", "sung " + unsung.EchoesSung, unsung.EchoesSung >= 4);
                    // The eclipse: stand in the first pool of dawn and live; the second time, stand outside and be struck.
                    unsung.ServerDebugEclipse();
                    for (float t = 0f; t < 2f && unsung.DawnPools.Count == 0; t += 0.1f) yield return new WaitForSeconds(0.1f);
                    int fallen = unsung.EclipsesFallen;
                    if (unsung.DawnPools.Count > 0) motor.Teleport(unsung.DawnPools[0] + Vector3.up * 0.1f, local.transform.rotation);
                    yield return new WaitForSeconds(1f);
                    yield return Shot(dir, "boss" + (depth + 1) + "_dawn");
                    hp.ServerSetFraction(1f);
                    float before = hp.Current.Value;
                    for (float t = 0f; t < UnsungBrain.EclipseWarning + 1f && unsung.EclipsesFallen == fallen; t += 0.1f) yield return new WaitForSeconds(0.1f);
                    float safeLoss = before - hp.Current.Value;
                    Log("unsung eclipse", "pools " + unsung.DawnPools.Count + ", fell " + (unsung.EclipsesFallen > fallen) + ", lost in the light " + safeLoss.ToString("F0"), unsung.DawnPools.Count >= 2 && unsung.EclipsesFallen > fallen);
                    motor.Teleport(at, Quaternion.LookRotation(boss.transform.position - at));
                }

                // Walk the boss through every phase gate: wound it from behind until it changes stance,
                // then play the stance out (kill the wards, outlast the survival, wait out the transition).
                var bh = boss.GetComponent<Health>();
                motor.Teleport(at, Quaternion.LookRotation(boss.transform.position - at));
                for (int g = 0; g < boss.GateCount; g++)
                {
                    for (int k = 0; k < 60 && !boss.IsDead && boss.CurrentStance == BossBrain.Stance.Normal; k++)
                    {
                        bh.ServerDamage(bh.Max.Value * 0.05f, boss.transform.position - boss.transform.forward * 3f);
                        hp.ServerSetFraction(1f);
                        yield return new WaitForSeconds(0.12f);
                    }
                    var stance = boss.CurrentStance;
                    Log("boss phase " + boss.DisplayName + " #" + (g + 1), stance + " '" + boss.PhaseName.Value + "' at " + Mathf.RoundToInt(bh.Fraction * 100f) + "%", stance != BossBrain.Stance.Normal);
                    yield return new WaitForSeconds(0.9f);
                    FaceBoss(local, boss);
                    yield return Shot(dir, "boss" + (depth + 1) + "_phase" + (g + 1));
                    if (stance == BossBrain.Stance.Transition)
                    {
                        for (float t = 0f; t < 5f && boss.Immune; t += 0.2f) { hp.ServerSetFraction(1f); yield return new WaitForSeconds(0.2f); }
                        // A transition may lead straight into the phase it announced.
                        stance = boss.CurrentStance;
                    }
                    if (stance == BossBrain.Stance.Invulnerable)
                    {
                        int wards = boss.WardsLeft.Value;
                        yield return new WaitForSeconds(1.5f);
                        FaceBoss(local, boss);
                        yield return Shot(dir, "boss" + (depth + 1) + "_phase" + (g + 1) + "_warded");
                        foreach (var e in FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None))
                            if (!(e is BossBrain) && !e.IsDead) e.GetComponent<Health>().ServerDamage(1e6f, e.transform.position);
                        for (float t = 0f; t < 3f && boss.Immune; t += 0.2f) yield return new WaitForSeconds(0.2f);
                        Log("boss ward " + boss.DisplayName, wards + " wards killed, immune after: " + boss.Immune, wards > 0 && !boss.Immune);
                    }
                    else if (stance == BossBrain.Stance.Survival)
                    {
                        for (int k = 0; k < 20; k++) { hp.ServerSetFraction(1f); yield return new WaitForSeconds(0.2f); }
                        FaceBoss(local, boss);
                        yield return Shot(dir, "boss" + (depth + 1) + "_phase" + (g + 1) + "_survive");
                        for (float t = 0f; t < 16f && boss.Immune; t += 0.2f) { hp.ServerSetFraction(1f); yield return new WaitForSeconds(0.2f); }
                        Log("boss survival " + boss.DisplayName, "ended, immune after: " + boss.Immune, !boss.Immune);
                    }
                    for (float t = 0f; t < 4f && boss.Immune; t += 0.2f) { hp.ServerSetFraction(1f); yield return new WaitForSeconds(0.2f); }
                }
            }

            Debug.Log("[Smoke] boss test done");
            Application.Quit(0);
        }

        private static void FaceBoss(NetworkPlayer local, BossBrain boss)
        {
            var look = boss.transform.position - local.transform.position;
            look.y = 0f;
            if (look.sqrMagnitude > 0.1f) local.transform.rotation = Quaternion.LookRotation(look);
        }

        private static FirstPersonMotor lobbyMotorFor(NetworkPlayer p) => p.GetComponent<FirstPersonMotor>();

        /// <summary>A steady frame-time sample: settle, then average 90 frames (min, mean, worst).</summary>
        private static IEnumerator SamplePerf(string name)
        {
            yield return new WaitForSeconds(1f);
            float sum = 0f, worst = 0f, best = 1f;
            double cpu = 0, gpu = 0; int timed = 0, spikes = 0;
            long gcBytes = 0;
            var gcRec = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Memory, "GC Allocated In Frame");
            var timings = new FrameTiming[1];
            // Where the main thread goes (markers only report in development builds).
            string[] markerNames = { "Update.ScriptRunBehaviourUpdate", "PreLateUpdate.ScriptRunBehaviourLateUpdate", "FixedUpdate.PhysicsFixedUpdate", "PostLateUpdate.UpdateCustomRenderPipeline",
                "PostLateUpdate.FinishFrameRendering", "GUI.Repaint", "GUIUtility.ProcessEvent", "PreLateUpdate.DirectorUpdateAnimationBegin", "Update.ScriptRunDelayedDynamicFrameRate", "PostLateUpdate.UpdateAllRenderers", "Inl_RenderCameraStack", "Culling" };
            var handles = new System.Collections.Generic.List<Unity.Profiling.LowLevel.Unsafe.ProfilerRecorderHandle>();
            Unity.Profiling.LowLevel.Unsafe.ProfilerRecorderHandle.GetAvailable(handles);
            var markers = new System.Collections.Generic.List<(string, Unity.Profiling.ProfilerRecorder)>();
            foreach (var hnd in handles)
            {
                var desc = Unity.Profiling.LowLevel.Unsafe.ProfilerRecorderHandle.GetDescription(hnd);
                if (System.Array.IndexOf(markerNames, desc.Name) >= 0) markers.Add((desc.Name, Unity.Profiling.ProfilerRecorder.StartNew(desc.Category, desc.Name)));
            }
            var markerSum = new double[markers.Count];
            UI.UiTimer.Reset();
            for (int i = 0; i < 90; i++)
            {
                yield return null;
                float dt = Time.unscaledDeltaTime;
                sum += dt; worst = Mathf.Max(worst, dt); best = Mathf.Min(best, dt);
                if (dt > 0.025f) spikes++;
                FrameTimingManager.CaptureFrameTimings();
                if (FrameTimingManager.GetLatestTimings(1, timings) > 0) { cpu += timings[0].cpuMainThreadFrameTime; gpu += timings[0].gpuFrameTime; timed++; }
                if (gcRec.Valid) gcBytes += gcRec.LastValue;
                for (int m = 0; m < markers.Count; m++) if (markers[m].Item2.Valid) markerSum[m] += markers[m].Item2.LastValue;
            }
            gcRec.Dispose();
            var parts = new System.Collections.Generic.List<string>();
            for (int m = 0; m < markers.Count; m++) { if (markerSum[m] > 0) parts.Add(markers[m].Item1 + " " + (markerSum[m] / 90.0 / 1e6).ToString("F2")); markers[m].Item2.Dispose(); }
            if (parts.Count > 0) Debug.Log("[PerfMarkers] " + name + ": " + string.Join(", ", parts));
            if (UI.UiTimer.Totals.Count > 0)
                Debug.Log("[PerfHud] " + name + ": " + string.Join(", ", UI.UiTimer.Totals.OrderByDescending(kv => kv.Value).Take(14).Select(kv => kv.Key + " " + (kv.Value / 90.0).ToString("F2"))));
            Debug.Log("[PerfSample] " + name + " mean " + (sum / 90f * 1000f).ToString("F1") + " ms (" + (90f / sum).ToString("F0") + " fps), best " + (best * 1000f).ToString("F1") + ", worst " + (worst * 1000f).ToString("F1") +
                ", cpu " + (timed > 0 ? (cpu / timed).ToString("F1") : "?") + " gpu " + (timed > 0 ? (gpu / timed).ToString("F1") : "?") + " ms, spikes>25ms " + spikes + ", gc " + (gcBytes / 90 / 1024) + " KB/frame" +
                ", renderers " + FindObjectsByType<Renderer>(FindObjectsSortMode.None).Count(r => r.isVisible) + " visible, lights " + FindObjectsByType<Light>(FindObjectsSortMode.None).Count(l => l.isActiveAndEnabled) +
                ", top: " + string.Join(", ", FindObjectsByType<Renderer>(FindObjectsSortMode.None).Where(r => r.isVisible)
                    .GroupBy(r => (r.transform.parent != null ? r.transform.parent.name + "/" : "") + r.name).OrderByDescending(g => g.Count()).Take(10).Select(g => g.Key + " x" + g.Count())));
        }

        private static IEnumerator Shot(string dir, string name)
        {
            // Frame-time budget: log what each staged view costs (60 fps = 16.7 ms).
            Debug.Log("[Perf] " + name + " " + (Time.smoothDeltaTime * 1000f).ToString("F1") + " ms, " +
                FindObjectsByType<Light>(FindObjectsSortMode.None).Count(l => l.enabled) + " lights on, " +
                FindObjectsByType<Light>(FindObjectsSortMode.None).Count(l => l.enabled && l.shadows != LightShadows.None) + " shadowed");
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, name + ".png"));
            yield return null;
            yield return null;
        }

        private static float PathLength(UnityEngine.AI.NavMeshPath p)
        {
            float len = 0f;
            for (int i = 1; i < p.corners.Length; i++) len += Vector3.Distance(p.corners[i - 1], p.corners[i]);
            return len;
        }

        private static void Log(string check, string value, bool ok)
        {
            Debug.Log("[Smoke] " + (ok ? "PASS " : "FAIL ") + check + ": " + value);
        }
    }
}
