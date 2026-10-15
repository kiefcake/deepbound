# Research: what the reference games do that Deepbound doesn't (2026-10-15)

The vision names four games: League of Legends (kit clarity), Dark and Darker and R.E.P.O.
(first-person co-op tension), Realm of the Mad God (systems backbone). This pass read how each
one creates its fun, then compared that against what Deepbound already has. Sources are listed
at the end. Fan wikis and forum posts were the main sources, so treat exact numbers as approximate.

## What each game does well

### Dark and Darker: dread, risk and loss
- **Soul Heart revive.** A dead teammate's heart sits in their body. Allies have to take it out
  and carry it to a fixed Altar of Sacrifice. The ritual there can be interrupted and costs the
  reviver health. A Cleric can resurrect in place instead, if the heart is still on the body.
  Death is a co-op problem you solve on foot, not a timer you wait out.
- **Extraction pressure.** Escape portals open late. A collapsing zone (since replaced with
  timed, one-use exits) pushes the party toward conflict and forces a choice: leave now or
  push deeper. Players said fighting over an exit was the best part, and complained when the
  collapse was not clearly announced.
- **Skull Keys.** Bosses drop a key to a secret room with locked, high-quality chests. It's a
  side trip with risk and reward.
- **High Roller.** Every enemy is elite and loot is better. Deepbound's Omens already cover this.
- **Rarity tiers add enchantment slots.** Two items of the same tier still differ because base
  values are rolled.

### R.E.P.O.: physical comedy and carrying things
- **Carrying is the game.** Valuables are physics objects (fragile vases, pianos). Carrying one
  slows you, takes your hands, and a knock can break it. Moving one is a team job.
- **Proximity voice is a mechanic.** You need to talk, but talking draws monsters.
- **Monsters with rules.** The Apex Predator duck is harmless until you engage it. Some enemies
  turn hostile when grabbed. Every monster is a puzzle with one rule to learn.
- **Escalating quota.** Each level asks for more extraction value, so pressure grows over a run.

### Realm of the Mad God: loot moments and chained progression
- **Bag tiers you can read from across the screen.** Each tier has a colour, and the top tiers
  are soulbound by damage share. The white bag drop is the moment players remember.
- **Events.** Killing quest monsters spawns a once-per-realm event boss. Killing that opens a
  portal to a rare dungeon.
- **Chained bosses.** Oryx 1 drops a locked portal. You need an "incantation" from elsewhere to
  open it, and three runes to reach the third form. The finale is a goal you build toward over a run.
- **Bullet patterns with names.** Bosses run named phases (Silence: a spiral you circle; Cosmic
  Might: orbs plus random superblasts). There's a rage phase at the end. Each phase teaches
  one dodge.
- **Status effects are the vocabulary.** Weak, Quiet (silence), Slowed, Dazed, Confused and Blind.

### League of Legends: clarity
- **Riot's rule:** the more dodgeable a spell is, the more it should draw the eye. The more it
  changes the fight, the flashier it should be. A projectile's direction should read even in
  a still frame.
- **Each ability has its own visual language,** so you recognise it instantly. Skins never
  reduce clarity.
- **Death recap.** It shows exactly what killed you, so every death teaches something. RotMG has a
  "killed by" line too.
- **Semantic pings** (danger, on my way, enemy missing, help) let a team talk without voice.

### Realm of the Mad God boss logic (follow-up, 2026-10-15)
- **Phase structure.** Bosses are state machines that move between named phases on HP thresholds or
  timers. Oryx 1 is the exception: it picks phases at random until a rage phase at low HP.
- **Invulnerable minion phases.** Null alternates: it is vulnerable on odd phases, then invulnerable on
  even ones while its minions attack. Killing every minion starts the next phase. Shaitan becomes
  invulnerable and summons his hands, and has short invulnerable gaps between phases.
- **Armored phases.** Oryx and the Marble Colossus have phases where they can be hit but are
  Armored (heavy damage reduction).
- **Survival phases.** The Marble Colossus goes invulnerable for 20-45 s, circling and firing spirals
  and spears. You can't damage it; you dodge until the timer ends.
- **Taunts.** Every phase change has a spoken line, so players hear the shift as well as see it.
- **Rage.** A last, fastest phase. Some bosses respawn their pillars or orbiting rocks for it.
- **Adaptive HP.** Boss HP scales with the number of players in the dungeon.

**Applied in Deepbound** (`BossBrain` phase framework):
- HP gates: damage stops at the gate, so no burst can skip a phase.
- Four stances: Transition, Invulnerable-while-warded with a timeout, timed Survival, Armored.
- A shield bubble while the boss is immune, IMMUNE on hits, and the stance and phase name on the
  boss bar with a countdown or wards left.
- Taunts shown as subtitles.

| Boss | Phases |
|---|---|
| Gravewarden | 50% Grave Rend; 20% Last Stand (throws its shield away: no frontal block, faster blows) |
| Tide Lich | 60% The Tide Turns; 35% The Drowning Deep (12 s survival spiral) |
| Hollow King | 50% Throne Ward (immune while 2 Royal Guards live, 25 s max); 25% The Last Crown |
| Pale Cardinal | 66% and 33% Choir of the Damned (immune while the Wraiths live, 22 s max); 12% Final Hymn (10 s survival) |

Extra RotMG sources: [Null](https://realmeye.com/wiki/null), [Marble Colossus](https://realmeye.com/wiki/marble-colossus), [Shaitan](https://www.realmeye.com/wiki/shaitan-the-advisor/78), [Oryx](https://www.realmeye.com/wiki/oryx-the-mad-god/8).

## What Deepbound already has
| Idea | Deepbound |
|---|---|
| High Roller | Omens (Blood Moon, Glass Bones...) |
| Rarity tiers | Worn / Fine / Masterwork / Legendary gear, runes, relics |
| Hit locations | Headshots + glowing weak points |
| Telegraphs | Wind-ups with a red "!", shockwave rings, ground circles |
| Monster rules | Mimic (breathes), Gargoyle (stone until close), Hoarder (runs) |
| R.E.P.O. downed | Downed + F revive; wipe when everyone is down |
| Chained finale | 4 floors, a boss each, back to camp between floors |
| Events | Champions, Wheel of Fate, breaches, ambushes |

## What to add (ranked by fun per effort)
1. **Death recap** (League/RotMG). When you go down, list the last hits (who, what, how much).
   It's cheap, and it makes every death teach something. *Building now.*
2. **Bleed-out and Soul Skull** (Dark and Darker + R.E.P.O.). A downed player bleeds out after
   ~30 s and drops their **Soul Skull**. An ally carries it (slower, no attacking) to a **Shrine
   of Return** on that floor (the start room's shrine, or the Fountain). A short ritual there,
   which an attack interrupts, brings them back. Being downed becomes a race and dying becomes a
   rescue trip. *Building now.*
3. **A legendary loot moment** (RotMG white bag). A tall pillar of light, a unique chime and a
   party-wide feed line when a Legendary drops. *Building now.*
4. **Reliquary escort** (R.E.P.O.). One heavy relic per floor sits in a side room. The carrier
   is slowed and can't attack, and a heavy hit makes them drop it. Bringing it to the carriage or
   boss door pays big. It's a co-op job where one player carries and the rest guard.
5. **The Mourner** (R.E.P.O. monster-with-a-rule). A weeping statue that only moves while nobody
   looks at it. In co-op, someone has to keep watch.
6. **Seals and a hidden fifth floor** (RotMG Oryx chain + Skull Keys). Champions drop Seals.
   With all three, the Pale Cardinal's death opens a portal to the Abyss, a secret final boss.
   This gives every run a long-term goal.
7. **Named bullet-pattern phases** (RotMG). Slow projectile rings you strafe or jump, spirals you
   circle. They fit the Cardinal's finale and a Gravewarden last stand.
8. **Ping wheel** (League). Hold the ping key for Danger / Help / Going here / Loot.
9. **Silence and Daze** debuffs (RotMG). Abilities locked, or a view sway. New afflicter enemies.
10. **Proximity voice** (R.E.P.O.). Needs Vivox or Steam voice: ask the user before adding any
    online service.

## Sources
- Dark and Darker: [extraction guide](https://metabot.gg/en/darkanddarker/guides/how-extraction-and-escape-portals-work), [TheGamer extraction](https://www.thegamer.com/dark-and-darker-extraction-guide/), [Steam discussion on circles](https://steamcommunity.com/app/2016590/discussions/0/3757726797848131685), [reviving party members](https://www.thegamer.com/dark-and-darker-how-to-revive-party-members/), [Soul Heart](https://dotesports.com/indies/news/what-is-a-soul-heart-in-dark-and-darker-and-how-do-you-use-it), [Skull Key](https://dotesports.com/indies/news/how-to-get-and-use-a-skull-key-in-dark-and-darker), [High Roller](https://www.ginx.tv/en/high-roller-mode-explained), [Gear Score wiki](https://darkanddarker.wiki.spellsandguns.com/Gear_Score)
- R.E.P.O.: [Outsider Gaming](https://outsidergaming.com/repo-co-op-horror-game-like-lethal-company/), [GFinity](https://www.gfinityesports.com/article/5-reasons-to-play-repo-if-you-love-lethal-company), [monster guide](https://www.exitlag.com/blog/r-e-p-o-monsters/), [upgrades](https://selphie1999gaming.com/game-guides/repo/all-upgrades-in-repo-and-how-they-work/)
- Realm of the Mad God: [Wikipedia](https://en.wikipedia.org/wiki/Realm_of_the_Mad_God), [RealmEye loot containers](https://www.realmeye.com/wiki/loot-containers), [Oryx's Chamber guide](https://www.realmeye.com/wiki/oryx-s-chamber-guide), [Oryx 2](https://www.realmeye.com/wiki/oryx-the-mad-god-2)
- League of Legends: [Clarity in League](https://www.leagueoflegends.com/en-gb/news/dev/clarity-in-league/), [missile VFX anatomy](https://realtimevfx.com/t/anatomy-of-a-league-of-legends-missile-part-2-what-are-we-communicating-with-our-vfx/13062)
