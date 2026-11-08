# More things to do at camp and in the dungeon — the four options, explained

Written 2026-11-08, because the user asked: "please explain each option further in how you would implement it and how
it would work". The user then asked for all of them ("take your time on all of them").

**Status (2026-11-08):**
- Built: knife board, fishing at the well, bell ringing, arm wrestling (against Brann, and delver against delver since the second pass: C stakes a challenge, another player matches it, the winner takes both);
  statue room, chime room (plates in order; no co-op two-plate step, the Oath room already does that) and the light-beam
  room (standing mirrors, from act three); chandelier,
  oil + fire, cracked pillars, prisoner cages, vigil candles; trophies + remembered quests; weather (clear / mist / rain);
  Old Wick at Skull Poker (real house gold, 150 a visit, not fake); pet tricks (fetch, come, spin, chase the cat, sleep by
  the fire).
- Also built (second pass, 2026-11-08): the weight puzzle (THE SCALES room, act two on: five urns, a counterweight that
  is the sum of two or three of them), a drinker at the bar (Old Hollis, who toasts the fallen), the memorial (a board of
  the fallen on the tavern wall, a candle per lost party).
- Not built: time of day (camp stays night; dusk would mean a second lighting pass over the whole camp).

---

## 1. Camp pastimes (small games between runs, alone or with the party)

**What you'd do**
- **Knife-throwing board** by the archery range. Every class can throw. Three throws per round, scored by ring, with a
  party leaderboard for the visit. Brann bets you can't beat his score.
- **Arm wrestling** at a tavern table. Two players press their keys in rhythm with a moving marker; a hit in the sweet
  spot pushes the arms your way. It can also be played against Brann or Hobb.
- **Fishing at the well / moat edge.** Cast, wait for the float to dip, hit F in time. Most catches are junk (boots,
  bones), some are coins, and the rare ones are trinkets that Marta or Silas buy. A quiet thing to do while friends shop.
- **Bell ringing**, a co-op rhythm game on the chapel bells. Each player holds one rope, and Aldric "conducts" a short
  pattern. A clean peal gives a small blessing for the next floor.

**How it would be built**
- Each game is one networked station, like the gong or the dice table. The server owns the score and the RNG; the
  owner gets a small panel or prompts, and every peer sees the 3D result (the knife sticking in the board, the fish on
  the line).
- Shared pieces already exist and would be reused:
  - `Interactable` for F
  - `RpcTarget.Single` panels (as in Bones and blackjack)
  - stool pinning (as in Skull Poker)
  - `CampChatter.Say` for the folk to react
- Rewards stay small: gold, a cosmetic title, one-floor buffs. They must never be stronger than the dungeon.
- Each one gets a smoke check (forced outcome → score/payout right) and a tip.

**Effort:** about a day per game. Fishing and knife-throwing are the cheapest; arm wrestling needs the most tuning.

---

## 2. Dungeon puzzles (rooms you think through, not just fight through)

**What you'd do**
- **Statue rotation room.** Four statues around a sealed door; each one turns 90° with F. When they all face the
  sigil on the floor, the door opens onto a chest. Wrong turns are harmless, but enemies keep coming in waves while you
  work.
- **Pressure-plate sequence.** Runes light up on the wall in an order (shown once, with a sound per rune). Step on the
  plates in the same order. In co-op, two plates must be held at the same time.
- **Mirror and light beam** (Cathedral and Abyss acts). Rotate standing mirrors so a shaft of light reaches a crystal. The
  crystal burns away a barrier, or wakes a dormant guardian early in your favour.
- **Weight puzzle.** Carry the right cargo to a scale. This reuses `Carryable`: urns of different weights balance a
  hanging gate.

**How it would be built**
- Each puzzle is a new `RoomFeature`, the same pattern as the Oath room and the Vault levers:
  - `DungeonLayout` picks a side or combat room for it.
  - `DungeonBuilder` dresses it.
  - A small networked component holds the state on the server: statue facings, the plate sequence, mirror angles.
- Solutions are seeded from the floor seed, so every peer builds the same puzzle and only the inputs are synced.
- Rewards come from the existing pieces: chest + Sigil (`ServerOathKept`-style), a free temper, or shortening the
  Tallyman's clock.
- Each puzzle gets a tip, a minimap icon, and a smoke check that solves it with `ServerDebug…`.

**Effort:** about a day each. The light-beam room is the most work (beam raycasts and visuals); statues and plates
are the quickest.

---

## 3. Dungeon interactables (small things in ordinary rooms that change a fight)

**What you'd do**
- **Chandelier drops.** Shoot or hit the chain and the chandelier crashes down on whatever is under it (a telegraph
  ring shows where).
- **Oil spills + torches.** Break an oil jar to spread a slick, then set it alight with any fire (a Mage bolt, a Fire
  flask, an Ember Rune proc) for a burning floor.
- **Collapsible pillars.** A cracked pillar takes a few heavy hits, then falls in a line, crushing enemies and leaving
  rubble cover.
- **Prisoner cages.** A caged adventurer begs to be freed (lever or lockpick). Freed, they fight beside you for the
  floor (a weak ally NPC), or they turn out to be a mimic-like trap 1 time in 5.
- **Shrine candles.** Light all the candles in a room (F on each, timed) for a short blessing; enemies try to snuff them.

**How it would be built**
- These are `Breakable`/`Interactable` kinds placed by `DungeonBuilder.ThemeDressing` with a per-room chance, the
  same way great urns, powder barrels and soul lanterns are placed now.
- Damage goes through the existing pipeline (`ServerCheckShove`-style environment kills, an env-kill callout like
  SLICED / SCORCHED).
- The fire spread reuses `Health.ServerScorch` and the burning-ground pools from the Plague Doctor's miasma.
- The ally prisoner reuses `Team.Allies` (already used by the Phoenix) with a simple brain that follows the nearest
  player.
- Every one gets a telegraph and a tip (the production rules require both), plus a smoke check.

**Effort:** half a day for each small one (chandelier, oil, pillar, candles); a day and a half for the prisoner
(an ally AI).

---

## 4. Camp life touches (the castle feels lived in and reacts to you)

**What you'd do**
- **Trophies.** Every guardian you kill puts its trophy up in camp for good: the Gravewarden's shield over the
  gate, the Lich's crown in the shrine, and so on. The folk comment on them.
- **Weather and time of day.** Camp drifts between dusk, night, rain and fog between visits. Rain sends the folk
  under awnings, and the tavern fills up.
- **Folk who remember.** Quest outcomes change camp: Hobb's father's hammer hangs on its hook once delivered, Vey wears
  the deep silk, and Marta opens the saved cask the day someone reaches the bottom.
- **Other delvers.** Two or three NPC adventurers drift through: one sits at the poker table and plays you, one drinks
  at the bar, and the names of the dead go on a memorial wall after a wipe.
- **Pets do things at camp.** They chase the cat, fetch a stick you throw, and sleep by the fire when you sit.

**How it would be built**
- **Trophies and remembered quests:** host-side PlayerPrefs bits like `Deeds` and favour (never written in tests).
  `Lobby` reads them when it builds the camp and places the extra props; `DialogueLines`/`CampChatter` get lines keyed
  to them.
- **Weather:** a synced `CampWeather` byte rolled on each return to camp.
  - `Lobby`/`CampAmbience` drive the visuals: rain particles, wet ground, a fog density change.
  - `FolkRounds` gains "shelter" spots when it rains.
- **NPC delvers:** reuse `Props.FolkFigure` + `FolkIdle` + `FolkRounds`. The poker-playing one is a seat-filler bot
  in `PokerDiceTable` with simple hold/bet rules. It only plays when fewer than two players are seated, and its gold is
  fake, so it can't drain or inflate the economy.
- **Pets:** extend `PetCompanion` with camp behaviours (a stick prop, a follow-the-cat state).

**Effort:**
- trophies + remembered quests: about a day
- weather: about a day (it needs a lighting pass and a camp-tour check)
- NPC delvers: two days, the poker bot alone being one
- pet tricks: half a day

---

### My recommendation

If you want the most fun per day of work:
1. Dungeon interactables first. Chandelier, oil and pillars change every fight, and they show off in co-op.
2. Then trophies + remembered quests. They make the new quests feel like they mattered.
3. Then one pastime: fishing or knife-throwing.
4. Then the statue/plate puzzles.
