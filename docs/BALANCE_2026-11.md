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
| A | 1 | 0 | 0 | 0 | 1 | 6 | 0 | 2 | 13 | - | - |
| D | 0 | 12 | 1 | 2 | 0 | 12 | 6 | 1 | 11 | 60 | 105 |
| E | 1 | 8 | 3 | 0 | 1 | 26 | 12 | 24 | 13 | 35 | 123 |

Run A's row is as logged in the first run's notes; its floor 10 was not comparable.
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

All four ended with Q5, E5, three runes socketed and eight relics. All used every ability, their ultimates, potions and
flasks on every floor.

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
- **Thunder Idol + Echo Stone (Storm Caller):** lightning every second hit, about a third of the Cleric's guardian damage.
  Left alone, because it lifts a weak damage dealer, but it is the strongest relic pair seen.
- **Late guardians are quick:** floors 7 and 9 took about a minute or less, shorter than floors 2 and 4. Bots stand still and
  keep full uptime, so people should take longer. Needs a real party.
- **Solo:** the last solo run predates the final changes and had stalls on floors 3, 7 and 9. The twins fix may explain them;
  not confirmed.
- **Untested:** Pyromancer (the Mage bot was Cryomancer in every run read), the other three classes, and real players.

## Reading the tally

An `other` entry the same size as `Relics` is not extra damage. It is the hit that set off the proc: the proc's nested hit
uses up the tag before the tally records the outer hit. So in run E, Cleric's floor 10 guardian Smite is really
37,185 + 43,361, with Thunder Idol as the 43,369 under `Relics`.
