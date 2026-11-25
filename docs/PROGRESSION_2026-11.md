# Character progression: how it fits together (2026-11-24)

The user asked: "please figure out a way to make all of the character progression and upgrades make sense and work together".

## The rule: one job per system

Every upgrade answers one question. If two systems answer the same question, one of them changes.

| System | Where | Paid with | Its one job |
|---|---|---|---|
| **Stats** (drills, gear stats) | Brann's yard, drops | gold | Your body: health, defence, speed, attack rate, regeneration, ultimate charge |
| **Weapon level** (temper +1 to +10, hones) | Hobb's anvil | embers, gold | How hard your weapon hits: ATT, DEX, range |
| **Weapon Path** (+2, mastered at +4) | Hobb's anvil | (with the level) | *How* your weapon attacks |
| **Edges** (+5, +7, +9) | Hobb's anvil | (with the level) | When you hit harder or hold longer: conditional damage or defence |
| **Ability ranks** (Q and E, 1 to 5) | Ysolde's stones | sigils | What your abilities do: a behaviour choice per rank, plus potency and cooldown |
| **Runes** (three sockets) | Ysolde's stones | embers (meld) | What your hits leave behind: on-hit effects that set up combos |
| **Relics** (eight) | Chests, altars, shrine | found, gold | Rule changers, and synergy pairs |
| **Gear and sets** | Drops, Hobb, Silas | gold | Defensive identity, and a set bonus for a matched pair |
| **Masteries** (six drills in one stat) | Brann's yard | gold | A reward for committing to one stat |
| **Bargains** | Hidden shrines | a curse | A trade-off for the run |
| **Legacy** (Renown) | Pause menu | Renown | Between runs: conveniences and one heirloom relic, never raw power |

## Problems found, and what was done

1. **Wisdom did nothing for four classes.** It fed mana regeneration (Mage, Cleric), reagents (Plague Doctor) and Cleric healing. On a Knight, Rogue, Ranger or Flagellant it was dead weight.
   *Fixed:* every point of WIS gained over the class's own start charges the ultimate 1.5% faster, for every class (`Ultimates.WisdomCharge`). The stat text and the Meditation drill now say so.
2. **Two masteries did the same thing.** MP mastery and WIS mastery were both "6% shorter cooldowns".
   *Fixed:* MP mastery is now "mana returns 25% faster" (Mage and Cleric only; the MP drill is only sold to them). WIS mastery keeps the 6% shorter cooldowns.
3. **The cooldown cap did not hold.** The Guide said cooldown reduction capped at 40%, but sets and masteries multiplied on top of the cap.
   *Fixed, then the user's call:* "remove the 40% cap". Every source now adds up (relics, the blessing, edges, the Grave Trade set, the WIS mastery), and ability ranks and bargains shorten on top. The only limit is a floor: no cooldown drops below a fifth of its length. The stat sheet shows the total, and the Guide says there is no cap.
4. **Growth was invisible.** About a dozen damage multipliers (ATT, hones, ascension, relics, sets, edges, ranks) all multiply together, and none of them showed a number.
   *Fixed:* the stat sheet (hold Tab) has a DAMAGE panel. Each attack and ability shows a plain hit now and the percentage gained since the run began (`Progression.DamageSheet`). The feed says "Firebolt now hits for 48 (was 42)" when your main attack grows.
5. **Five currencies.** Gold, embers, sigils, crafting parts and Renown each have one clear sink. Embers are spent at both the anvil and the rune meld, which is fine: both are weapon power.
   No change; the Guide's sections explain each currency as you find it.

## Still to look at (needs play data)

- **Damage curve per floor.** With the DAMAGE panel in place, party bot runs can log the main attack's growth floor by floor. That shows whether any one source (relics, hones) outgrows the rest. This needs windowed runs; it is part of the party balancing still pending.
- **Edges vs Path.** Some Edges overlap with Path masteries (both conditional damage). Review once the curve is logged.

## Edges vs weapon paths (review, 2026-11-25)

Paths change the shape of an attack (a flame stream, a fork, a fan, a riposte); Edges are flat modifiers offered three at
a time at +5/+7/+9. No Edge repeats what a path does, so neither choice makes the other pointless. Pairs that multiply
and are worth watching in party logs:

| Pair | Why it stacks | Ceiling |
|---|---|---|
| Flagellant + Desperation | Mortification already gives up to +40% when wounded; Desperation adds up to +25% on the same missing health | x1.75 near death |
| Knight Bulwark + Retribution | spikes already return blocked melee; Retribution returns unblocked melee too | every melee hit returns damage |
| Stun paths (Stormcaller mastery, Zealot hammer, Fanatic crash, Bulwark) + Opportunist | the path stuns, Opportunist gives +20% to stunned foes, Exposed adds +25% | x1.5 on stunned foes |

None of them breaks a run in the logs so far (the strongest classes in party runs were the Plague Doctor and the Rogue,
both fixed elsewhere). Leave them as build identity unless a run shows one dominating.
