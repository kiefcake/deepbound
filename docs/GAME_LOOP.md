# The game loop, solo to four players (2026-10-23)

One run: camp, then 10 floors in 5 acts (a Warden on the first floor of each act, the act's boss on the second, the Unsung on floor 10), back to camp between every floor. Only cosmetics, journal pages, deeds and records last past a run.

## A floor

1. **Arrive** in the start room (waystone, peddler, anvil). Floor event and objectives announced.
2. **Clear rooms**. Combat rooms, side rooms with chests, special rooms (treasury, armoury, mirror, proving bell, wheel of fate, blood altar), a champion (the act's Seal), sometimes a reliquary to carry back, secret walls, a vault behind three levers.
3. **The pressure clock**. The Tallyman comes for parties that linger.
4. **The guardian**. A Warden or boss in its arena. It sleeps until the whole standing party is inside.
5. **The lift**. Everyone in the cage, then back to camp.

## Camp between floors

| Stall | Spend | On |
|---|---|---|
| Hobb (anvil) | embers (+gold from +6) | temper +1..+10, a path at +2, edges at +5/+7/+9, hones past +10; refine/attune gear, scrap the pack |
| Ysolde (stones) | sigils | Q/E ranks 1-5 (rank 5: Overcharged or Hastened), runes, melding |
| Shrine | gold | two relics picked for you (reroll for gold), two blessings to choose from, Aldric's prayer, holy water |
| Silas | gold | potions (dearer each floor), Swift Boots, two pendants picked for you |
| Brann (yard) | gold | drills: ATT, DEF, DEX, HP (MP for casters), each dearer than the last |
| Corwin | gold | scout's map, smelling salts, climbing rope; then the carriage down |

## How the party size changes things

`Enemies/Difficulty.cs`. *Extra* = players - 1.

| | Solo | 2 | 3 | 4 |
|---|---|---|---|---|
| Enemies per room (`SpawnCount`) | x0.8 | x1.1 | x1.4 | x1.7 |
| Each enemy's health (`EnemyHealth`, +45%/extra) | x1 | x1.45 | x1.9 | x2.35 |
| **Enemy health to chew through, per player** | 0.8 | 0.8 | 0.89 | 1.0 |
| Enemy damage (+8%/extra) | x1 | x1.08 | x1.16 | x1.24 |
| Boss / Warden health (+85%/extra) | x1 | x1.85 | x2.7 | x3.55 |
| Elite chance | base | +2% | +4% | +6% |
| Reviving | salts, prayer, Phoenix only | F-revive, soul skulls, the lot | | |

What that adds up to:

- **Solo is the hardest way to play.** There's no one to revive you, and every enemy is after you. It gets the lightest crowds (x0.8) to make up for that. Smelling Salts, Aldric's Prayer and the Phoenix Feather are what save a solo run, and a solo run starts with one set of Salts already in the pack (Corwin's gift).
- **Two players is the easiest per head.** Each player faces the same health as a solo player, with half the aggro on them and revives on top.
- **Four players do about 25% more work per head than solo.** Combos (Mark, Exposed, Shatter, Thermal Shock, Blessed) more than make up for it. Bosses scale a little under per-head (x3.55 for four), because their attacks split across the party.

## Loot per player (fixed this pass)

Before, gold and embers were split evenly (shared loot), but drops barely grew with the party. In a four-player run each player got about 40% of a solo player's income, so the anvil stalled around +4 by floor 10. Relics, sigils and runes were thinner still: one champion sigil for the whole party, and one relic roll per elite.

Now:

- **Enemies roll their loot table `KillLootShare` times** (1 / 1.45 / 1.71 / 1.88). With x1.7 enemies at four players, kill income per player is exactly solo's.
- **Fixed sources** (chests, urns, bosses, Wardens, champions, mimics, the hoarder) **roll once per player.**
- **Gold and embers from all those rolls land as one pile each,** then split across the party as before. Finders-keepers mode (L at camp) still works.
- **Champions drop a Sigil for each player.** Bosses already did.
- **Gear was already rolled per player.** Only its owner sees it.
- **Relic etiquette:** a relic you already carry would only empower yours, so it stays on the floor while a teammate lacks it.
- **Party-only relics** (Shared Vow, Rallying Bell) never roll in a solo run. The Banner of the Fallen works solo too, since its bearer gets the bonus.

So the economy targets are the same at every party size: about one temper a floor, +10 around floor 9, a sigil or two a floor, and 6-8 relics by the end with a few empowered.
