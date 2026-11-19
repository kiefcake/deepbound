# Readability audit: enemy attacks (2026-11-19)

The bar (League of Legends kit clarity, `docs/PRODUCTION.md` rule 1): every attack that hurts gives a tell you can read
and react to, carried by **shape** (a floor circle, lane or wedge, a pose) and **sound**. The `!` glyph comes from
`EnemyBrain.Status` and is extra. Read from the code in `Assets/Scripts/Enemies/*Brain.cs`, `BossBrain.*.cs`,
`Combat/Telegraph.cs`, `Art/BossWindup.cs`.

**Verdicts** are for the code as it was before this pass. **OK** = a shape or pose, plus a sound, at least about 0.35 s
before a big hit, and a way to avoid it. **WEAK** = there is a tell but a channel is missing (usually sound), it is too
short, or it lies about the timing. **MISSING** = the damage lands with no tell at all. The **Fixed** column says what
changed in this pass (blank = left as it was).

**How tell lengths are written.** `windup` is the prefab value from `Editor/GameBuilder.BuildEnemyPrefab` (0.6 if not
set). Every rank-and-file wind-up is multiplied by `EnemyBrain.Pace`: 1, enraged 0.75, frenzied 0.6, Quickened Dead omen
x0.83, Patient champion x1.3. So a frenzied skeleton under the omen winds up in 0.6 x 0.6 x 0.83 = 0.3 s. `Tell(x)` on
guardians = x * `Difficulty.Tell(depth)` (1.0 on floor 1 down to 0.72 by depth 8) * 0.85 when furious.

**Shared sound facts** (they explain many rows):
- `Telegraph.Show/ShowLine/ShowCone` make no sound. `BossBrain.Gather` and `Art/BossWindup` made none either (fixed: see 1).
- `EnemyBrain.PlayActionRpc` plays a sound only for `Windup`, `WindHeavy`, `WindSweep`: bones (no voice) rattle every time
  (volume 0.4); voiced creatures (ghoul, brute, gargoyle, mimic, plaguebearer, frost wight, entling, hoarder) play
  `MonsterAttack` only 35% of the time unless elite, so about two wind-ups in three are silent. `Slam`, `Cast`, `Hurl`,
  `Charge`, `Channel`, `Summon` poses are silent.

## Rank and file: the shared move set (`EnemyBrain`, not edited in this pass)

| Attack | Enemy | Tell | Tell s | Sound? | Verdict | Fixed |
|---|---|---|---|---|---|---|
| Swing | every melee rank and file (skeleton 0.6, ghoul 0.35, gargoyle 0.55, frost wight 0.6, shieldbearer 0.75, mimic 0.4, bellringer 0.75, ink lamprey 0.6, mourner 0.3, entling 0.35) | Windup pose, `!` (Status bit 4) | windup x Pace | bones: rattle; voiced: 35% | OK for bones; WEAK for voiced (silent 2 in 3) | |
| Heavy blow (x1.8, knockback) | same | WindHeavy pose, `!!` (bit 8) | windup x 1.7 x Pace | as Swing | OK (voiced: sound caveat) | |
| Flurry (2 x 0.7) | same | first as Swing; second blow Windup pose | first: windup x Pace; second: 0.32 | as Swing | OK (small blows) | |
| Sweep (220 deg, x0.8) | same | WindSweep pose, `<!>` (bit 16) | windup x 1.25 x Pace | as Swing | OK | |
| Lunge (x1.25, 13 m/s) | same | floor lane 1.2 m wide, Windup pose, `>>` (bit 32) | 0.55 (not Pace-scaled) | as Swing | OK | |
| Leap down from a ledge (x0.9) | any that can drop | floor circle 1.8 m (orange), Cast pose | 1.05 (0.45 crouch + 0.6 flight) | none until the landing thud | WEAK (no sound) | not fixed: `EnemyBrain.cs` |
| Volatile death blast (x1.6) | Volatile elites, Marrowgrin champion | floor circle 3.2 m (orange) | 1.3 | none until the blast | WEAK (no sound) | not fixed: `EnemyBrain.cs` |
| Guardian dash (x0.9, bowls over) | every boss/Warden that moves | floor lane, Windup pose, roar | Tell(0.75) | roar | OK | |

## Rank and file: their own attacks

| Attack | Enemy | Tell | Tell s | Sound? | Verdict | Fixed |
|---|---|---|---|---|---|---|
| Arrow (21 m/s, leads on deeper floors) | Archer | Draw pose, `!` | 1.0 x Pace | rattle; bow twang on release | OK | Draw pose now lasts the real wind-up (`ArcherBrain.cs:102`, was the bare 1.0) |
| Pounce (x1.2) | Ghoul | Windup crouch; no lane, no `!` | 0.45 | hiss | WEAK (no shape on the floor) | lane 2.6 m wide on the floor, cancelled by stun/interrupt (`GhoulBrain.cs:76`, `PounceLaneRpc` `:122`, cancelled at `:108/:115`) |
| Ground pound (2.8 m, daze 1.6 s, knockback) | Bone Brute | floor circle, Slam pose | circle: bare 1.1; real blow: 1.1 x move (heavy 1.87 / sweep 1.38) x Pace | rattle/voice 35% + slam | WEAK: the circle lied ("full = hit" broke: enraged it struck before the circle filled, heavy it filled 0.8 s early) | circle lasts exactly until the blow (`BruteBrain.cs:28`) |
| Shield shove (knockback) | Shieldbearer | Swing tell | 0.75 x move x Pace | rattle + bash | OK | |
| Shadow bolt | Necromancer, Wraith, Hexer, Cantor, Choir Acolyte, Withering Hag, Abyss Eye, Rime Magus, Orbweaver | Windup pose, `!` | 0.6-0.8 x Pace | rattle + whisper/frost on release | OK (projectiles 14-18 m/s) | |
| Spit (poison) | Plaguebearer | Windup pose, `!` | 0.9 x Pace | voice 35% + slime on release | OK (slow visible spit) | |
| Death cloud (poison) | Plaguebearer | green circle (the zone itself) | 0 (poisoned the instant it appeared) | splash | WEAK (no lead time) | first tick 0.5 s after the cloud appears (`PlaguebearerBrain.cs:47`) |
| Wail (x1.7) | Wraith | blue circle 3 m at your feet, Cast pose | 1.3 | whisper + shade | OK | |
| Frost Nova (frostbite, root from act 2) | Frost Wight | frost circle 3.6 m, Cast pose | 1.1 | frost | OK | |
| Curse (no damage, -30% dmg / -50% heals) | Hexer | Cast pose, loud whisper; feed line once per 30 s | 1.0 | whisper | OK (break line of sight) | |
| Dive (x1.4, daze, knock) | Gargoyle | floor lane 2 m | 0.8 | whisper | OK | |
| Ambush bite (x1.2) | Mimic | the chest's lid breathes now and then (idle) | 0 | none before | WEAK by design (the surprise is the mechanic) | not changed (design) |
| Chomp | Mimic | Swing tell | 0.4 x move x Pace | voice 35% + bash | OK | |
| Strike (24, heavy 43) | Mourner | Windup pose, `!` - but it can only strike while nobody looks at it, so only sound reaches you | 0.3 x move x Pace | rattle at 0.4 volume | WEAK (too short for an audio-only tell on a big hit) | wind-up at least 0.45 s and a loud grinding sob every wind-up (`MournerBrain.cs:32-46`, `MinWindup`, `GrindRpc`) |
| Latch (then rooted up to 12 s, squeezes 0.45x each second) | Entling | Windup pose, `!` | 0.35 x Pace | voice 35% | WEAK (borderline length for a 12 s hold; usually silent) | 0.45 s and a creak of twigs every time (`EntlingBrain.cs:76-83`, `CreakRpc`) |
| Orb patterns: spiral / gapped ring / aimed 3-bolt burst | Orbweaver | flash + embers on the conjure (0.9 s), the orb itself (first shot 1.6 s after it appears) | cast 0.9, then bullets | summon; ring: fire; spiral: none; aimed burst: none | OK (slow 8.5 m/s bullets) except the aimed burst: WEAK (silent) | aimed burst plays a fire hiss (`OrbweaverBrain.cs:162`, `AimedFxRpc` `:227`) |
| Hymn (wards allies, no damage) | Choir Acolyte | gold circle 9 m, Cast pose | 2.6 | toll + whisper | OK | |
| Beam (x2.4) | Abyssal Eye | violet lane toward where you are going, Windup pose | 1.1 x Tell | frost | OK | |
| Songs (Rose, Scissors, Seedpods, March) + 3-bolt fan | Bone Piper | flash, the song's name, Cast pose; the fan: Swing tell | 0.75 x Tell (0.54-0.75); fan 0.6 x Pace | pipe note per song | OK (slow 5.5-9.8 m/s bullets) | |
| Hush (x0.5 + Silence) | Cantor | pale circle 2.8 m where you are heading, Cast pose | 1.4 | muted toll | OK | |
| Blizzard (follows you) | Rime Magus | pale circle 3.2 m, Cast pose; the storm itself is visible while it chases | 1.1 | frost | OK | |
| Ink spit (x0.6 + Blind) | Ink Lamprey | violet circle 2.3 m where you are heading, WindHeavy pose | 1.0 | slime | OK | |
| Evil eye (x0.5 + Accursed) | Withering Hag | green lane 16 x 1.7 m, Cast pose | 1.3 | whisper | OK | |
| Toll (x0.4, Silence, knock; a jumpable ring) | Bellringer | pale circle 2.4 m, Slam pose; then the travelling ring | 1.2 | chain, then toll | OK | |

## Guardians: shared (`BossBrain`)

| Attack | Enemy | Tell | Tell s | Sound? | Verdict | Fixed |
|---|---|---|---|---|---|---|
| Swing (cleave/sweep) | every boss and Warden | wedge on the floor locked to the swing (`ConeTelegraphRpc`), Gather (crouch, core light), Windup pose | windup x Pace (Gravewarden 0.9/0.72, Hollow King 0.85 x scale, Wardens 0.9/0.75/0.6, others 0.6) | rattle (voice for voiced kinds 35%) | OK | |
| Shockwave (jump it) | Gravewarden, Tide Lich (<60%), Hollow King, Cardinal, Unsung, Wardens, Vharoth | circle 2.5 m round it, Slam pose; then a ring rolling out at 9 m/s | 1.1, then the ring's travel | none until the launch slam | WEAK (silent wind-up) | Gather now sighs (see fix 1) |
| Hunting strikes (3-4 predicted circles per player, 0.42 s apart) | every guardian from floor 2 | red circles 2.2 m, Hurl pose, Gather | Tell(0.85) each | none | WEAK (silent) | fix 1 |
| Lance fan (3-5 lanes per player) | every guardian from floor 2; Sir Aldous's Royal Lance | red lanes 24 x 1.7 m, Slam pose | Tell(1.0) | none | WEAK (silent) | fix 1 |
| Quick shots (fleeing party, floor 3+) | most guardians | flare at the hand | 0.4 | bow + whisper per shot | OK | |
| Bullet patterns (spirals, rings) | many | the emitter is drawn (`PatternEmitter`); slow bolts (7.5-15 m/s) | none: the bullets are the tell | aimed shots whisper | OK (bullet-hell reads by the bullets) | |
| Shared Doom (split among those inside) | every guardian in a party of 2+ | violet ring under the doomed player | Tell(3.2) | summon | OK | |
| Blink nova (Tide Lich dash) | Tide Lich | circle 3.4 m at the arrival point | Tell(0.65) + 0.55 | whisper + frost | OK | |
| Leap (Cardinal dash) | Cardinal | circle 3.2 m at the landing | Tell(0.9) + 0.75 | roar | OK | |
| Repulse (hugging it) | guardians with it | circle 4.8 m, Cast pose | Tell(1.1) | whisper | OK | |

## Guardians: their own attacks

| Attack | Enemy | Tell | Tell s | Sound? | Verdict | Fixed |
|---|---|---|---|---|---|---|
| Shield charge (40) | Gravewarden | lane 2.6 m to the wall, Charge pose, Gather | Tell(1.3) | none | WEAK (silent) | fix 1 |
| Grave Rend (x1.1, bone lines) | Gravewarden (<50%) | lanes 15 x 1.7 m to every player, Slam pose, Gather | 1.1 | none | WEAK (silent) | fix 1 |
| Last Stand pinwheels | Gravewarden (<20%) | taunt the first time; slow 8 m/s bullets | none (bullets) | none | OK | |
| Volley (5 bolts, 21 m/s) | Tide Lich | Hurl pose; no `!`, no floor shape | 1.0 | none until the bow twang | WEAK (silent draw) | Gather + sigh during the draw (`TideLichBrain.cs:265`) |
| Drowning pools (34) | Tide Lich | circle 2.6 m under each player, Summon pose, Gather | Tell(1.8) | none until the splash | WEAK (silent) | fix 1 |
| Maelstrom (pull; 9 per 0.3 s in the eye) | Tide Lich (<60%) | the eye's circle 2.2 m, Channel pose, a feed line | 0 (the pull starts at once; damage only in the eye) | splash | OK (the pull is the mechanic; the eye is marked) | |
| Whirl (2-arm spiral, 13.5 m/s) | Tide Lich (<60%) | Channel pose only; first bolts at once | 0 | none | WEAK (no lead time, silent) | 0.6 s gather + sigh before the first bolt, same spinning time (`TideLichBrain.cs:158-164`) |
| Slam (48, 6.5 m, knockback; pillars block it) | Hollow King | circle round him, Slam pose, Gather | 1.5 x scale (Tell, x0.7 enraged) | none until impact | WEAK (silent) | fix 1 |
| Crown's Judgement (130 shared) | Hollow King | "JUDGED", summon chime, then a circle at the marked one | 1.2 mark + 1.7 circle | summon (+ war cry for the judged) | OK | |
| Decrees (x2: BE STILL / BEGONE) | Hollow King (<25%) | title card, ring 9 m, Gather | Tell(2.6) | toll | OK | |
| Crown of Blades | Hollow King (<25%) | its name, swing whoosh; bullets | none (bullets) | swing | OK | |
| Judgement pillars (38) | Pale Cardinal | gold circles 2.4 m under/around players, Cast pose, Gather | Tell(1.5) | none until the eruption | WEAK (silent) | fix 1 |
| Excommunication (brand) | Pale Cardinal | gold ring following the branded, words | Tell(4.5) | toll | OK | |
| Final Hymn (rose window + shockwave + pillars) | Pale Cardinal (<12%) | survival phase card; ring travels | ring travel | slam | OK | |
| Dirge (ring of 18-24 bolts, a 3-bolt gap) | The Unsung | none before the release; slow ring (9-11 m/s), the gap is the answer | 0 | whisper on release | OK (bullets) | |
| Grasping Dark (x1.3) | The Unsung | violet circles 2.6 m under each player, Summon pose | Tell(1.5) | none | WEAK (silent) | a shade's moan under each grasp (`UnsungBrain.cs:437`, `GraspWarnRpc`) |
| Echoes (bone lines, frost pools, crown slam, rose) | The Unsung | lanes/circles in each dead boss's colour, Channel pose, "ECHO:" | Tell(1.4) (crown x1.3) | shade | OK | |
| Eclipse (x2.2 outside the pools) | The Unsung (<15%) | gold pools of dawn | 4.5 | toll | OK | |
| Slam / Black Tide / poison circle (x1.5) | Wardens 0, 1, 4 | circle 5.5 m, Slam/Cast pose, Gather | Tell(1.3) (+0.4 Undertow) | none until impact | WEAK (silent) | fix 1 |
| King's Shield charge (x1.4) and AGAIN | Sir Aldous (Warden 2) | lane 2.6 m, Charge pose, Gather | Tell(1.1) / fury Tell(0.8) / again Tell(0.75) | again: war cry; else none | WEAK (silent) | fix 1 |
| Hex-bolt fan + curse / spit fan (x0.6 / x0.5 + poison) | Choirmaster (Warden 3), Gorr (Warden 4) | Hurl pose at the moment of release; the curse landed on its target the same instant, with no line-of-sight check | 0 | none | MISSING | Tell(0.6) wind-up (Hurl pose, Gather, sigh), the fan and the curse go on the release; a stun or break cancels it (`WardenBrain.cs:341/346` call `BeginVolley` `:674`, released by `ReleaseVolley` `:685` from `Update` `:379`) |
| Hymn of Madness (x0.6 + mania) | Choirmaster | violet circles 2.6 m where players are heading, Gather | 1.4 | none until the burst | WEAK (silent) | fix 1 |
| Bone Toss (x1.2) | Marrowgut (Warden 0, <50%) | bone circle 2.4 m, a lobbed bone | Tell(1.3) | swing whoosh | OK | |
| Rot pools (poison) | Gorr (<50%) | pool laid where the slam just landed | 0 (follows the slam's own circle) | slime | OK | |
| Striding fan (3 bolts, 13 m/s) | Vharoth | Hurl pose at release | 0 | none | WEAK (silent) | a fire hiss on release (`VharothBrain.cs:187`, `HurlFxRpc` `:283`) |
| Kindling ring (14 bolts, 8 m/s) | Vharoth | the ring itself | 0 | fire | OK (slow bullets) | |
| Seekers (x0.9, 2.2 m burst) | Vharoth | glowing mine drifts at 3.1 m/s; lit, it flickers white | fuse 0.55 | none until the burst | WEAK (no shape, silent) | a burst circle on the floor and a hiss when the fuse lights (`VharothBrain.cs:214`, `SeekerLitRpc` `:272`) |
| Lash (x1.1 lanes) | Mother Marrow | lanes 1.2 m to every player, WindSweep pose, Gather | Tell(0.9) | rattle | OK | |
| Bone rain (x0.9) | Mother Marrow | bone circles 1.6 m, Cast pose, Gather | Tell(1.3) | none until the fall | WEAK (silent) | fix 1 |
| Lure bite (x1.4) | Lantern Fisher | the lure light drifting at you ("Never walk to the light") | 0: bit the instant you were within 1.9 m | none before | MISSING | the jaws rise first: red circle 1.9 m under the lure + a splash, 0.45 s, then they close there (`LanternFisherBrain.cs:204`, `BiteWarnRpc` `:134`, resolved just above it) |
| Surfacing (x1.2, 2.4 m) | Lantern Fisher | none: struck where the lure was when the stage ended | 0 | splash at the hit | MISSING | the lure stops 1 s before; a red circle where it will break the water + a deep splash (`LanternFisherBrain.cs:169-173`, `SurfaceWarnRpc` `:141`) |
| Reel and bite (x1.6) | Lantern Fisher | the line to the hooked player, Channel pose, taunt | 4 | whisper (taunt) | OK | |
| Ink spray (5 bolts, 12 m/s) | Lantern Fisher | Hurl pose at release | 0 | none | WEAK (silent) | a slime spit on release (`LanternFisherBrain.cs:126`, `SprayFxRpc` `:131`) |
| Twin Charge (x1.3) | Twice-Crowned | two lanes 1.6 m, Charge pose, Gather | Tell(1.2) | none | WEAK (silent) | fix 1 |
| Holy fan (3 bolts, 11 m/s; every copy too) | Mother Veyle (Mirror-Nun) | Cast pose at release | 0 | none | WEAK (silent) | a whisper on release (`MirrorNunBrain.cs:155`, `CastFxRpc` `:161`) |
| Copy backlash (x0.8, 3.2 m) | Mother Veyle | none: striking a copy (they cast no shadow) bursts it | 0 | shatter on the burst | WEAK by design (the punishment for a wrong guess) | not changed (design) |
| Penance spiral / Confession ring | Mother Veyle | survival card; the ring + toll, scalds only outside | - | toll | OK | |

**Fix 1** (`BossBrain.cs:899-905`, the presentation helper `Gather`): every guardian wind-up that gathers with weight >= 0.6
for 0.35 s or more (circle, lane and tinted-circle telegraphs, hunting strikes, Cardinal pillars, Grave Rend) now plays
a deep breath-in (`Sound.Whisper`, pitch 0.45) from the guardian, at most once per 0.5 s (fans call it many times a
frame). The plain swing wedges (weight 0.5) keep their rattle and are not doubled.

## Counts

Rows above: 79. Before this pass: **OK 47, WEAK 29, MISSING 3** (two OK rows had a weak part: the voiced creatures' mostly
silent swing, and the Orbweaver's silent aimed burst).

- MISSING, all fixed: the Choirmaster/Gorr bolt and spit fans (with the curse that rode them), the Lantern Fisher's lure
  bite, and its surfacing.
- WEAK fixed: 25 (13 of them by fix 1 alone). WEAK left: 4 - leap-down and Volatile have no sound (both in
  `EnemyBrain.cs`); the Mimic's ambush and Mother Veyle's copy backlash are surprise punishments by design.
- Also changed: the Orbweaver's aimed burst sound, and the Archer's draw pose now matches its real wind-up.
- No damage number was changed. Timings that changed: Mourner wind-up 0.3 -> at least 0.45 s, Entling 0.35 -> 0.45 s,
  Warden fans 0 -> Tell(0.6), Tide Lich whirl starts 0.6 s later (spins as long), Lantern Fisher bite 0 -> 0.45 s,
  Plaguebearer cloud's first tick 0 -> 0.5 s; the lure holds still for the last 1 s before it surfaces.

## Left for `EnemyBrain.cs` (the lead's file)

1. Voiced creatures voice only 35% of their wind-ups (`PlayActionRpc`, `RigAction.Windup` case): about two swings in three
   from ghouls, brutes, gargoyles, mimics, plaguebearers, frost wights and entlings are silent. Suggest: always play
   something (a growl at low volume when the `MonsterAttack` roll fails), as the bones always rattle.
2. `LeapTellRpc` and `VolatileWarnRpc` have no sound (a scrape/crouch for the leap; a hiss or `Sound.Fire` at low pitch
   for the Volatile fuse).
3. The `!` glyph (Status bit 4) is lit only by the shared swing and the lunge. Specials (pounce, wail, nova, beam, dives,
   casts) have floor shapes but no glyph. A protected `SpecialTell` flag OR'd into bit 4 in `ServerSyncStatus` would light
   it for all of them with one line per special.
4. `Pace` compounds: frenzied (0.6) under Quickened Dead (0.83) halves a wind-up, so a ghoul swing is 0.17 s and a
   skeleton 0.3 s. Consider a floor (e.g. never under 0.3 s) where the multiplier is applied in `BeginAttack`.
