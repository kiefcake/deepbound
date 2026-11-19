# Party balance round, November 2026

Four bots (Mage, Knight, Cleric, Rogue) played ten floors in real windows, five runs in all. Raw logs are in
`Builds/balance-logs/` (not in git). Run B was lost to a wrong command line and became the solo Rogue run.

Bots never dodge and cannot go down. A "near-down" is a hit that would have downed them; they heal to 60% and carry on.

## Guardian fight, minutes

| Floor | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 |
|---|---|---|---|---|---|---|---|---|---|---|
| A, before tuning | 1.0 | never woke | 0.5 | 1.2 | 0.8 | 0.5 | 0.5 | 0.8 | 0.5 | 0.7 |
| C, first retune | 1.1 | 2.4 | 0.8 | 2.5 | 1.0 | 1.7 | 1.1 | 1.6 | 1.0 | forced |
| D, tally added | 0.9 | 2.1 | 1.5 | forced | 1.7 | 1.8 | 1.2 | 1.6 | 1.1 | 2.3 |
| E, final | 1.0 | 2.3 | 1.2 | 3.1 | 1.3 | 1.2 | 1.0 | 1.7 | 0.8 | 2.2 |

Forced means the test killed the guardian after a stall. Run E had no forced floors.

## Near-downs by floor

| Floor | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 | Total |
|---|---|---|---|---|---|---|---|---|---|---|---|
| A | 1 | 0 | 0 | 0 | 0 | 1 | 6 | 0 | 2 | 13 | 23 |
| D | 0 | 12 | 1 | 2 | 0 | 12 | 6 | 1 | 11 | 60 | 105 |
| E | 1 | 8 | 3 | 0 | 1 | 26 | 12 | 24 | 13 | 35 | 123 |

Run A's floor 10 guardian fell in 0.7 minutes, so its 13 are not comparable.
Run E by class: Mage 40, Cleric 39, Rogue 37, Knight 7.

## Damage share, all ten floors of run E

| Class | Total | Share | Floor 10 guardian |
|---|---|---|---|
| Mage (Cryomancer) | 1,231,543 | 40% | 335,118 |
| Rogue (Assassin) | 1,031,153 | 34% | 248,226 |
| Cleric (Zealot) | 488,682 | 16% | 131,781 |
| Knight (Riposte) | 317,774 | 10% | 72,951 |

Before the Thermal Shock fix, run D's floor 10 guardian figures were Mage 549k, Rogue 206k, Knight 88k, Cleric 71k.
The shock was about 29,000 a hit there and about 4,250 a hit in run E.

## Kit in run E

| Class | +10 reached | Hones | Before F10: embers / gold / sigils | Armour | Charm |
|---|---|---|---|---|---|
| Mage | F7 | 4 | 31 / 643 / 0 | Legendary d9 (44.4) | Legendary d9 (30.4) |
| Knight | F7 | 5 | 181 / 234 / 0 | Legendary d9 (39.7) | Legendary d9 (34.6) |
| Cleric | F8 | 3 | 22 / 570 / 0 | Legendary d9 (44.4) | Masterwork d9 (23.4) |
| Rogue | F6 | 4 | 5 / 694 / 0 | Masterwork d9 (30.2) | Masterwork d9 (23.4) |

All four ended with Q5, E5, three runes socketed and eight relics. All used Q, E and the ultimate on every floor, and
potions and flasks on most. The Mage, Cleric and Rogue used RMB on every floor; the Knight's RMB block is not counted.

What "best gear" means here: bots wear the highest-rated piece they hold and spend everything at the camp, but relics are
the first eight found and the weapon path is picked at random. No bot played a planned build.

## What changed

| Change | Before | After | Applies to |
|---|---|---|---|
| Enemy damage per extra player | +15% | +22% (four players ×1.66) | Parties |
| Guardian health per floor, four players | none | +18% (F10 ×2.62) | Parties |
| Embers | whole | ×0.8 | Parties |
| Thermal Shock on bosses | 1% of party-scaled health | 1% of solo health | Parties |
| Sigils and relics under shared loot | to whoever found them | to the poorest member once the finder is 3 ahead | Shared loot |
| Soulbound twin rising health | always 50% | 50, 35, 25, 17, 12, then 10% | Everyone |
| Elite and chest sigil chance | 12% and 20% | 8% and 14% | Everyone |
| Knight Swing and Bash | 29 and 18 | 35 and 24 | Everyone |
| Cleric Smite | 18 | 21 | Everyone |
| Mage Frostbolt and Meteor | 50 and 70 | 44 and 60 | Everyone |
| Feed line naming who the sleeping guardian waits for | none | added | Parties |

## Still open

- **Knight damage:** 10% of the party's total. Relic luck is part of it: the Knight out-damaged the Cleric in runs A and C,
  and the Cleric only passed it in run E with Thunder Idol. The Knight also took the least damage on floor 10 (2,129 against
  the Rogue's 7,942) despite having the most threat and health.
- **Knight damage (2026-11-08):** every Knight now counters (a swing within 1.6 s of a block hits x1.4; the Riposte path x2.2). Not yet measured in a bot run.
- **Thunder Idol + Echo Stone (Storm Caller):** lightning every second hit, about a third of the Cleric's guardian damage.
  Left alone, because it lifts a weak damage dealer, but it is the strongest relic pair seen. 2026-11-08: capped at every 3rd hit (tiers had made it every 2nd).
- **Late guardians are quick:** floors 7 and 9 took about a minute or less, shorter than floors 2 and 4. Bots stand still and
  keep full uptime, so people should take longer. Needs a real party.
- **Solo:** the last solo run predates the final changes and had stalls on floors 3, 7 and 9. The twins fix may explain them;
  not confirmed.
- **Untested:** Pyromancer (the Mage bot was Cryomancer in every run read), the other three classes, and real players.

## Main attack by path (2026-11-16, `-classdps`)

Solo (so the Knight's lone-swing x1.25 applies), main attack only, held on the camp dummy for 6 s from the front, each
path at mastery. Abilities, crits from behind and multi-target effects are not in it.

| Class | Base | Path 1 | Path 2 | Path 3 |
|---|---|---|---|---|
| Knight | 72.2 | Cleave 72.2 | Riposte 72.2 | Bulwark 72.2 |
| Mage | 50.8 | Pyromancer 50.8 | Cryomancer 50.8 | Wardweaver 50.8 |
| Cleric | 42.6 | Zealot 57.5 | Shepherd 42.6 | Anchorite 42.6 |
| Rogue | 55.2 | Assassin 55.2 (front) | Duelist 92.9 | Shadowskin 55.2 |
| Ranger | 41.2 | Sharpshooter 69.6 | Trapper 48.0 | Pathwarden 41.2 |
| Flagellant | 46.5 | Fanatic 52.8 | Penitent 46.6 | Scarred 46.6 |
| Plague Doctor | 39.1 | Pestilence 55.8 | Physician 44.3 | Mithridate 44.3 |

- The solo Knight's swing is no longer the weakest (it was 26 in the first table). Counters and Riposte are not in it (no
  blocks here); Cleave only widens the arc.
- Pyromancer and Cryomancer equal the base on one target: their gain is splash and pierce, which a lone dummy cannot show.
- The Duelist's x1.25 front and a flourish every 4th stab make it the top single-target main attack; the Assassin from
  behind (x2) would be about 110, so front play still pays less than flanking.
- Harness fix: the dummy refills only after 2.5 s untouched and the rows ran back to back, so the 5,000 ran out during the
  Duelist and it read 53 (dead for a third of the row). Each row now starts on a full dummy.

## Reading the tally

An `other` entry the same size as `Relics` is not extra damage. It is the hit that set off the proc: the proc's nested hit
uses up the tag before the tally records the outer hit. So in run E, Cleric's floor 10 guardian Smite is really
37,185 + 43,361, with Thunder Idol as the 43,369 under `Relics`.

## Economy (2026-11-07)

The user's target: on **Normal with no omens or events**, a solo player or one of four should reach about **80%** of the
upgrades they care about by the final guardian: **tempering + hones, ability ranks, yard drills**. Lucky drops and
dungeon extras can push past that; Hard and up (and omens like Blood Moon) pay for the rest. Prices stay as they were.

What "everything" costs per player: tempering 1,125 embers + 700 gold, five hones 1,250 embers + 2,000 gold, every drill
~3,100 gold (Breathing +500 for mana classes), both abilities to rank 5 = 14 Sigils (`AbilityRanks.MaxRank` is 5; the
notes saying 8 were stale). Total ~2,375 embers, ~5,800 gold, 14 Sigils.

Measured before the change (full ten-floor bot runs, Normal):

| Player | Gold earned | Embers (drops + scrap) | Sigils |
|---|---|---|---|
| Solo Cleric | 9,171 | 2,402 + 573 | 33 |
| Party Knight | 11,830 | 3,250 + 914 | 28 |
| Party Rogue | 9,512 | 2,530 + 809 | 23 |

Every bot ended maxed on tempering and hones with 1,800-5,200 gold and 450-1,400 embers unspent; guaranteed Sigils alone
(a champion's and a guardian's every floor) were 20 against 14 needed.

Changes: `Difficulty.EconomyGold`/`EconomyEmbers` 0.62 on every dungeon source (`LootDrop.ServerSpawnFromTable`,
`DungeonDirector.ServerDropLoot` unless `fixedSum`, contracts, reliquary, side shrines, blood altar, `Gear.ScrapValue`);
gambles paid back (`SideShrine` beggar) and a Tithe-Taker's stolen gold are `fixedSum`. The setting's gold (`Level.Gold`)
now applies to every source (it was only on kills), and settings gained `Embers` (Hard 1.3, Nightmare 1.6, Torment 2;
embers never scaled with difficulty before). Sigils: guardians no longer drop one each; champions drop one per player
75% of the time (`ChampionSigilChance`); elites 8% -> 3.5%, chests 14% -> 6%.

Expected on Normal: ~5,700 gold and ~1,850-2,500 embers per player, ~11-12 Sigils (more for whoever scraps a lot or finds
the niches and shrines). To be confirmed by a solo and a party run on the new numbers.

Solo by class (old economy, same build): the Cleric heals itself and had 0 near-downs on floors 1-9; the Rogue had 4-18
a floor and 45 on floor 9; the Mage 1-14. Solo difficulty depends heavily on the class. The Mage bot stalled for nine
minutes on floors 3 and 4 with enemies left (a bot problem; no "stuck" diagnosis printed).

## Players' side: ultimates, flasks, contracts, omens (2026-11-19)

All of this is arithmetic on the code and the main-attack table above. None of it was played. "x ATT" means times the
player's `AttackMultiplier`, which is 0.5 + ATT/50 (`Progression/PlayerProgress.cs:883`). File paths are under `Assets/Scripts/`
unless they say otherwise. Line numbers are as of this pass.

### Ultimate charge: the numbers

| Name | Value | Where | In play |
|---|---|---|---|
| `ChargeMax` | 100 | Player/Ultimates.cs:38 | One full bar. |
| `PerDamageDealt` | 0.02 a point, divided by `DepthHealth` | Ultimates.cs:41, :131 | On floor 1, 50 damage is 1 charge. The divisor is the same one that scales enemy health, so a given share of an enemy's health always pays the same. |
| `PerDamageTaken` | 0.06 a point, divided by `DepthDamage` | :41, :125 | A Knight losing all 240 health on floor 1 gets 14.4 (`Classes/ClassDefinition.cs:47`). |
| `PerSecond` | 0.15 | :41, :168 | 667 s to fill on this alone. Only in the dungeon while up. |
| `LockAfterCast` | 30 s | :41, :233 | Nothing charges after a cast, not even the trickle. |
| `RogueChargeScale` / `RogueLockAfterCast` | x1.6 / 18 s | :43, :145, :233 | The user's call (2026-11-12). |
| Warlord's Horn | x(1 + 0.5 x power); power is 1 / 1.35 / 1.7 by tier | :140, Loot/Relics.cs:60-62 | x1.5 to x1.85 |
| Echo Stone | x(1 + 0.2 x power) | :141 | x1.2 to x1.34, and it also cuts cooldowns by 20% |
| Rosary of Ash (charm) | x1.35 | :142 | |
| Hushed Bells (omen) | x1.5 | :143, World/DungeonDirector.Omens.cs:53 | |
| `AssistCharge` | 3 -> **1.1** | Player/Teamwork.cs:33 | Flat, per kill, to every other player who hit the enemy in the last 6 s. |
| `ClutchCharge` | 15 | Teamwork.cs:34 | You get an ally up while you are the last one standing. |
| `AvengeCharge` | 10 | Teamwork.cs:35 | You kill what downed an ally within 20 s. |

The multipliers stack. A top-tier Horn and Echo Stone with the Rosary give x3.35 (1.85 x 1.34 x 1.35). Under Hushed Bells
that is x5.0, and a Rogue gets x8.0. With all of that, the bar fills in seconds and the lock sets the pace (30 s, or 18 s
for the Rogue).

### How long the ultimate takes, from the main attack alone

Fill time = 100 / ((0.02 x DPS + 0.15) x class scale) on floor 1, where `DepthHealth` is 1.0. The main attack is held
without a break, with no abilities, no hits taken, no assists and no relics. A cycle is the fill time plus the lock. DPS
comes from the 2026-11-16 table.

| Class / path | DPS | Fill, s | Cycle, s |
|---|---|---|---|
| Knight (every path) | 72.2 | 62.7 | 92.7 |
| Mage (every path) | 50.8 | 85.8 | 115.8 |
| Cleric base, Shepherd, Anchorite | 42.6 | 99.8 | 129.8 |
| Cleric Zealot | 57.5 | 76.9 | 106.9 |
| Rogue base, Assassin (front), Shadowskin | 55.2 | 49.8 | 67.8 |
| Rogue Duelist | 92.9 | 31.1 | 49.1 |
| Ranger base, Pathwarden | 41.2 | 102.7 | 132.7 |
| Ranger Trapper | 48.0 | 90.1 | 120.1 |
| Ranger Sharpshooter | 69.6 | 64.9 | 94.9 |
| Flagellant base | 46.5 | 92.6 | 122.6 |
| Flagellant Penitent, Scarred | 46.6 | 92.4 | 122.4 |
| Flagellant Fanatic | 52.8 | 82.9 | 112.9 |
| Plague Doctor base | 39.1 | 107.3 | 137.3 |
| Plague Doctor Physician, Mithridate | 44.3 | 96.5 | 126.5 |
| Plague Doctor Pestilence | 55.8 | 79.0 | 109.0 |

**Deeper floors.** Charge from damage dealt is divided by `DepthHealth`: 1.0, 1.23, 1.45, 1.68, 1.9, 2.13, 2.39, 2.74,
3.16 and 3.67 on floors 1-10 (`Enemies/Difficulty.cs:134`). Enemy health is scaled by the same number, so the charge per
fight holds roughly steady and the charge per minute falls. Example: a Knight on floor 10 with +10 tempering and nothing
else fills in 112 s. That assumes the table was measured at the base ATT of 20, so tempering takes it to 60 and the
multiplier from 0.9 to 1.7. The harness sets the weapon level without adding the tempering ATT, which is why the Knight's
base and path rows match.

**What the table shows:**
- **Spread without the Rogue.** Cycles run from the Knight's 92.7 s to the Plague Doctor's 137.3 s, a 1.48x spread. Charge
  follows damage, so this follows the DPS table. The slow-charging classes (Cleric, Ranger, Plague Doctor) are the ones
  whose damage comes from heals, traps and rot, which the table leaves out. Not changed.
- **The Rogue is the outlier.** Its base cycle of 67.8 s comes round 2.0x as often as the Plague Doctor's 137.3 s, and the
  Duelist's 49.1 s comes round 2.8x as often. The x1.6 and the 18 s lock are the user's own choices. They compound with the
  Duelist's top main attack, so a Duelist casts 1.89x as often as a Knight (92.7 / 49.1). Left for the user.
- **Hits taken.** On floor 10 of run E the Rogue took 7,942 damage and the Knight 2,129. At 0.06 / `DepthDamage` 3.135 that
  is 152 charge for the Rogue (243 with the x1.6) and 41 for the Knight. Charge from being hit goes to whoever the dead
  reach, and in that run it was not the tank.

**What each ultimate does:**

| Ultimate | Payload | Where |
|---|---|---|
| Cataclysm (Mage) | Five meteors of 60 x 0.85 = 51 x ATT in 3.6 m. The outer four land 2.5-4.5 m from the aim point, so one target takes 51-255. | Ultimates.cs:259-275, Player/MageKit.cs:45 |
| Death From Shadow (Rogue) | 150 x ATT and a 1.8 s stun, one target | Ultimates.cs:304-305 |
| Rain of Arrows (Ranger) | 8 ticks of 16 = 128 x ATT per target in 5 m, plus chill | :311-319 |
| Rallying Banner (Knight) | 8 s within 7 m: allies take x0.65 and deal x1.2 | :59, :117, :163, :249 |
| Hymn of Return (Cleric) | Raises the downed within 18 m at 70%, brings the party to at least 70%, and shields 50 x ATT for 6 s | :293-296 |
| Martyrdom, Black Death | 8 s each | :245-246 |

Single-target damage per minute, using the cycles above: Rogue 133 x ATT (Duelist 183), Mage 26-132 and Ranger 58.

### Assists: fixed

The damage charge for a whole kill is base health x `RankHealth` 1.55 x `EnemyHealth(players)` x 0.02. `DepthHealth`
cancels out. In a party of four (x2.05, `Difficulty.cs:76`) that comes to:
- a Ghoul (55 health, `Assets/Editor/GameBuilder.cs:450`): 3.5
- a Necromancer (75, :516): 4.8
- a Brute (280, :511): 17.8

That charge is split between everyone who hit the enemy. Each assist paid a flat 3 to every other player who hit it, so 9 a
kill when all four hit. That is 2.6x the Ghoul's whole damage charge and 1.9x the Necromancer's. Split evenly, with a
quarter of the kills yours, a Necromancer kill gave each player 1.19 from damage and 2.25 from assists. So in a four the
ultimate filled about 2.9x as fast per kill as your own damage alone would fill it, and a solo player gets no assists at all.
`AssistCharge` dates from 2026-10-28. The 2026-11-01 play-test cut the other sources about 2.7x and missed it.

**Fix:** 3 -> 1.1 (3 / 2.7). A Necromancer kill now gives each player 1.19 + 0.83, which is 1.7x their own damage, and
assists are a bonus again rather than the main source. Clutch (15) and Avenge (10) are rare and dramatic, so they are left
alone; see the list for the user.

### Flasks

| Name | Value | Where | In play |
|---|---|---|---|
| Pouch | 5 of each | Player/Flasks.cs:32 | |
| Starting pouch | 2 health, 1 of each other kind | :321 | |
| Health Potion | 45% of max health over 3 s (12 ticks) | :33, :382-385 | x1.5 with Second Wind (Progression/Edges.cs:44), x1.25 for the Plague Doctor (PlayerProgress.Builds.cs:192) |
| Class draught | Refills 70%, then holds the resource full for 3 s | :33 | |
| Drink / throw cooldown | 1.2 s / 0.8 s | :34 | |
| Throw range / flight | 16 m / 0.55 s | :34 | |
| Frost Flask | 3.6 m: 10 -> **20** x ATT, chill 4 s | :471-475 | |
| Holy Water | 4 m: 24 x ATT and a 1.8 s stun. Allies in 4 m are cleansed and healed 20% of max health. It also repels the Tallyman. | :479-490 | |
| Prices at camp before floor 1 | Health 18, draught 15, Frost 25, Holy 30 | Progression/Shop.cs:280-288 | |
| Price growth | x(1 + 0.25 x depth) | Shop.cs:198 | Floor 10: Health 58, draught 49, Frost 81, Holy 98 |
| Peddler | x1.35 | Shop.cs:274 | |

Throwables never drop. The loot tables hold only health potions and draughts (`Loot/LootTable.cs:57-118`), so thrown flasks
come from shops, crafting (`Progression/Crafting.cs:37-38`) and the starting pouch.

- **Holy Water against the Frost Flask: fixed.** Holy hit 2.4x as hard (24 against 10), over 1.23x the area (radius 4 m
  against 3.6 m), and stunned where Frost only slows. It also cleansed, healed and drove off the Tallyman, for 30 gold
  against 25. Frost's only advantages were a longer hold (4 s against 1.8 s), setting up Shatter and freezing water.
  **Fix:** Frost 10 -> 20 x ATT, which matches Holy Water's damage per gold (24 / 30 x 25). Holy Water is still the
  stronger flask, but its higher price now buys the support effects rather than extra damage.
- **Health Potion against Holy Water as healing.** On yourself the potion heals more per gold: 45% for 18 against 20% for 30. Holy
  Water only comes out ahead with three or more players in the splash (four x 20% = 80%). Neither is dominant.
- **Holy Water and the "no potions" clause.** Holy Water heals without breaking the clause, because only drinking calls
  `QuestOnPotion` (Flasks.cs:373). For the user.

Rare slot:

| Item | Value | Where |
|---|---|---|
| Ironbark | 8 s: x0.5 damage taken, a shield of 25% of max health, immune to crowd control | Progression/RareItems.cs:17 |
| Bloodfury | 8 s: x1.4 damage, x0.8 attack time (x1.75 damage per second: 6 s of extra main attack) | :18 |
| Grave Stillness | 3 s as stone, then 1 s of grace | :19 |
| Tome of the Phoenix | 30 s. Dives for 22 x ATT every 1.4 s plus travel time; bursts for 30 x ATT. | :20, Player/Phoenix.cs:27-28 |
| Tome of the Storm | 10 s: a bolt every 0.6 s within 14 m, 24 x ATT and a 0.5 s stun | :21 |
| Tome of Sanctuary | 10 s within 6 m: x0.65 damage taken, 3% of max health mended a second | :22 |
| Silas's price | 150 + 40 x depth | :87 |
| Drop chance | elite 4%, chest 7% | Loot/LootTable.cs:70, :84, :106 |

The Storm Tome lands up to 17 bolts, 408 x ATT on one target, which is 2.7x a Death From Shadow. The Phoenix manages at most
about 21 dives, 462 x ATT, over 30 s. The two totals are close: the Storm does its damage up front and keeps a lone target
stunned (0.5 s of every 0.6 s). These are one-use items, so being worth a few ultimates is fair. Not changed.

### Contracts (quests)

| Name | Value | Where |
|---|---|---|
| Gold per quest | (50 + 40 n) x k, then x0.62 on Normal; n = `RoundToInt(Tier)` | World/DungeonDirector.Bounties.cs:336, :344; Enemies/Difficulty.cs:42 |
| Embers per quest | (8 + 3 n) x k x omen x event, then x0.62 | Bounties.cs:345 |
| k | x1.5 for a special quest, x1.3 with a clause | :130, :337 |
| Sigil | the hard kinds (Untouchable, Swift Blade, Champion Hunt, Elite Hunter, Mark, Cargo), or any special quest | :169, :338, :346 |
| Quests at once | 3 | :55 |
| Slayer goal | (45 + 14 t) x 1.35, so 61 kills on floor 1 | :180 |
| Favour discount | 3% a level, up to 5 levels | :59-60 |

`RoundToInt` rounds halves to even, so n on floors 1-10 is 0, 0, 1, 2, 2, 2, 3, 4, 4, 4. On Normal, one quest pays:
- **gold:** 31, 31, 56, 81, 81, 81, 105, 130, 130, 130 on floors 1-10. That is 856 a slot over a run, or 1,112 with a
  clause. Three slots make 2,568, or 3,336 with clauses.
- **embers:** 5, 5, 7, 9, 9, 9, 11, 12, 12, 12. That is 91 a slot and 273 for three, before the party's x0.8.

The economy section expects about 5,700 gold a run, so three quests a floor add about 45% more gold.

- **Sigils overshoot.** This is in a World file, so it is for the lead and the user. The economy pass (2026-11-07) set
  Sigils to about 11-12 a run against 14 needed, and quests came the next day.
  - Every giver's pool has a Mark, which pays a Sigil, and five givers also have Cargo, which does too.
  - Corwin's third job is Swift Blade and Brann's is Untouchable, and both pay a Sigil.
  - That puts the chance that a giver's offer pays a Sigil at 2/3 for Silas, Hobb, Vey, Brann and Marta, 3/3 for Corwin,
    and 1/3 for Aldric and Ysolde: about 5.0 of the 8 offers each camp visit.

  A party can take three Sigil quests every floor, which is up to 30 Sigils each over ten floors. Even one a floor makes about
  21-22, against 14 needed.
- **Pay does not follow effort.** Treasure Hunt (smash one secret wall, no clause) pays 77% of what Slayer pays (1 / 1.3).
  On floor 1, Slayer means 61 kills under a clause that fails the job the moment it is broken.

### Omens

| Omen | Cost | Reward | Where (World/DungeonDirector.Omens.cs unless noted) |
|---|---|---|---|
| Blood Moon | elite chance x1.8 (capped at 0.6) | kill gold x1.5 | :45, :54; DungeonDirector.cs:1048 |
| Famine | player healing x0.5 | embers x1.5 | :56, :69 |
| Restless Dead | x1.3 enemies | kill gold x1.5 | :45, :55; DungeonDirector.cs:1057 |
| Glass Bones | all damage x1.4, yours and theirs | none stated | :68 |
| Quickened Dead | enemy pace x0.83, enemy speed x1.2 | embers x1.4 | :49-50, :56 |
| Hushed Bells | cooldowns x1.4 | ultimate charge x1.5 | :52-53 |
| Avarice | camp prices x1.5 | kill gold x2 | :45, :47 |
| Renown | x1.35 for every omen | | :41 |

- **The gold omens only touch kill loot.** `OmenGold` is read in one place (`Enemies/EnemyBrain.cs:1685`), so chests,
  contracts and shrines are untouched. The ember omens cover every table and single drop (`Loot/LootDrop.cs:135`,
  `World/DungeonDirector.cs:454`). The stone's "+50% gold" means +50% of kill gold.
- **Avarice** doubles kill gold, raises prices x1.5 and adds no danger. Let s be the share of a run's gold that comes from
  kills. Buying power is then (1 + s) / 1.5, which breaks even only at s = 0.5 and is a loss below that. Restless Dead pays
  x1.95 kill gold (1.3 x 1.5) and x1.3 kill embers, with no price penalty. So the omen that adds danger out-pays the one
  that adds none, and Avarice is at best renown for nothing. None of the logs read for this pass measure s. It needs
  measuring, and the call is the user's.
- **Every omen pays the same renown.** Avarice adds no danger, yet earns the same x1.35 as Glass Bones or Famine.
- **Hushed Bells.** The x1.5 does not touch the 30 s lock, so a Knight's cycle goes from 92.7 s to 71.8 s: 29% more
  ultimates, against 29% fewer Q/E uses (1 / 1.4). "Charges 50% faster" is true of the bar, not of the casts.
- **Glass Bones has a hidden reward.** `OmenDamageScale` is applied before the charge hook (`Combat/Health.cs:360`), so
  ultimates charge x1.4 from damage both dealt and taken.
- **Famine and Quickened Dead pay in embers.** The economy section expects 1,850-2,500 embers against 2,375 of things to
  spend them on. x1.5 makes that 2,775-3,750, so at most about 525 of the bonus has anything to buy. Their reward is mostly
  renown.

### Changed 2026-11-19

| Change | Before | After | File |
|---|---|---|---|
| Ultimate charge per assist | 3 | 1.1 | Assets/Scripts/Player/Teamwork.cs:33 |
| Frost Flask damage | 10 x ATT | 20 x ATT | Assets/Scripts/Player/Flasks.cs:474 |

### For the user to decide

1. **Rogue ultimate.** The x1.6 charge and 18 s lock compound with the Duelist's main attack: 1.89x a Knight's casts and
   2.8x a base Plague Doctor's. One option is to keep the x1.6 and use the 30 s lock.
2. **Contract Sigils.** Up to three a floor, against an economy built for 14 a run. Options: a Sigil only from special
   quests, or at most one Sigil quest a floor. (`World/DungeonDirector.Bounties.cs`, not touched here.)
3. **Contract pay is flat.** One secret wall pays 77% of what 61 kills under a clause pay.
4. **Avarice** loses money unless kills bring in at least half the gold. Options: prices x1.25, or its x2 on every gold
   source (as the ember omens already work).
5. **Renown is x1.35 for every omen,** from Avarice to Glass Bones.
6. **Gold omen text.** "+50% gold" is only kill gold. **Hushed Bells text.** "Charges 50% faster" ignores the lock.
7. **Holy Water and the "no potions" clause.** Holy Water heals 20% without breaking it.
8. **Clutch (15) and Avenge (10) charges** also predate the 2.7x cut. At 2.7x they would be about 5.6 and 3.7. They are rare
   moments, so this pass left them alone.
