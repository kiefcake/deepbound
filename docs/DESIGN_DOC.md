# Project Brief & Design Doc

**Working title:** TBD (see Q7)
**Target:** Steam, 1-4 player online co-op, first-person fantasy dungeon crawler
**Engine:** Unity 6000.3.23f1
**Authors:** kiefcake + Claude (Opus 5)
**Started:** 2026-09-13
**Status:** Q1, Q4, Q5 DECIDED. Q2, Q3, Q6 open.

### Decisions locked (2026-09-13)

| # | Decision | Choice |
|---|---|---|
| Q1 | Project base | **New Unity project, new GitHub repo.** Port the good systems across; do not carry the architecture. |
| Q4 | Netcode | **Steam Networking Sockets, Steam-exclusive.** Free, native Steam friend invites, no metered billing. |
| Q5 | Render pipeline | **URP.** |
| Q3 | Art | **Free assets only, or self-authored.** No paid packs. See section 2.1 for the revised all-free plan. |

---

## 0. How to read this document

This is the single source of truth for the project.

- **Sections 1-3 are verified fact.** I read the code and fetched the sources. Every claim is traceable to something I actually checked.
- **Sections 4-8 are proposals** that need your sign-off.
- **Section 9 is the question list** — the thing you asked for.

Anything I have not verified is labelled **UNVERIFIED**. I am not going to state something as true because it sounds right.

---

## 1. Verified current state of `game_prototype/game`

You already have a substantial Unity project here. Before proposing anything I audited it directly. Numbers below are counted, not estimated.

| Fact | Value | How I know |
|---|---|---|
| C# source | **19,446 lines** across 117 files | `wc -l` over `Assets/Scripts/**/*.cs` |
| Unity version | 6000.3.23f1, **Built-in Render Pipeline** | `ProjectSettings/ProjectVersion.txt`, `GraphicsSettings.asset` (`m_CustomRenderPipeline: {fileID: 0}`) |
| Git | Clean repo, 618 tracked files, 12 MB, ~60 commits | `git ls-files`, `git log` |
| Existing remote | `github.com/kiefcake/gametest1` | `git remote -v` |
| Uncommitted work | **~20 modified files, not committed** | `git status --short` |
| 3D models | 53 `.obj` files, hand-built low-poly, **no rigs, no skeletal animation** | `find Assets -name "*.obj"`; `Visuals/ProceduralLimbAnimator.cs` animates by rotating separate part transforms |
| Textures | 21 `.png`, mostly generated procedurally at runtime | `Visuals/IconFactory.cs`, `Visuals/PanelSpriteFactory.cs` |
| Audio | **0 files.** All SFX synthesised as raw waveforms at runtime | `Audio/SfxLibrary.cs` |
| Classes implemented | 4 (Knight, Priest, Wizard, Paladin) | `Classes/ClassDefinition.cs`, `Assets/Data/Items/` |
| Abilities implemented | 12 (4 classes x 3) with a rank/rune system | `Abilities/AbilityData.cs` (`AbilityId` enum) |
| Dungeons implemented | 5 themes + an open-world overworld | `GameBootstrap.EnterAbyssDungeon` / `EnterFrozenCrypt` / `EnterSunkenRuins` / `EnterSnakePit` / `EnterWraithboundSanctum` |
| Enemies/bosses | 21 enemy classes including 6 bosses | `Assets/Scripts/Enemies/` |

### 1.1 The single most important finding: the game is not actually multiplayer

This is the fact that drives every recommendation below.

- **68** classes derive from `MonoBehaviour`.
- **2** classes derive from `NetworkBehaviour`: `Core/DungeonSessionState.cs` (syncs one random seed) and `Core/NetworkPlayerIdentity.cs` (syncs position, a class index, and a health echo).
- Enemies, combat, damage, loot, abilities, status effects, inventory, dungeon generation and boss state are **all local-only**. None of it is networked.

The project's own code says so, in `Core/NetworkBootstrap.cs` lines 21-25:

> *"What this does NOT yet do: sync enemies, loot, dungeon state, or combat across clients. A joined player's own NetworkObject only carries position and chosen class — enough for everyone to see each other walk around the hub as their own class's colour, not yet a fully synced co-op dungeon crawler."*

So: the Unity Netcode, Relay and Authentication packages are installed and a host/join lobby screen exists, but **~97% of the gameplay code has no multiplayer implementation at all.**

"Make it 1-4 players" is therefore not a feature to bolt on. It is the largest single piece of work in the project, and it changes how nearly every gameplay class must be written.

### 1.2 Three architectural patterns that will fight the new spec

These are not style complaints. Each one has a concrete, mechanical reason it blocks something you asked for.

**(a) Everything is built at runtime via `AddComponent` — there are no prefabs.**

This is the project's defining pattern, documented in `CLAUDE.md` as deliberate. It worked well for a solo prototype with procedural art. It breaks two things you now need:

- Unity Netcode spawns networked objects **from a registered prefab list**. An enemy assembled in code at runtime has no `NetworkObject` identity to replicate.
- Real art assets (rigged FBX characters with animator controllers) **are prefabs by nature**. There is nowhere to put them in the current architecture.

**(b) The ability system is a central `switch` over an `AbilityId` enum.**

`Abilities/AbilityCaster.cs` (458 lines) contains `switch (ability.id)` blocks in `Cast`, `ApplyRuneExtras` and elsewhere — one `case` per ability behaviour. At 12 abilities this is fine. At **8 classes x 3 abilities = 24 abilities**, each with 2 rune variants, that becomes roughly 72 cases in a single file. That is exactly the "roundabout code" you told me to avoid.

**(c) Class identity is expressed as per-class boolean flags.**

`ClassDefinition.cs` has `canBlock` (Knight only), `staggerChance` (Paladin only), `piercingShots` (Priest only), `rangedShotCount` (Wizard only). Every new class with a distinct feel adds another flag that is `false` for all the others. Eight classes with League-grade distinctiveness needs composable weapon/behaviour modules, not twenty booleans.

### 1.3 What is genuinely good and worth keeping

I am not going to pretend this project is worthless in order to justify a rewrite. There is real value here:

- **`World/DungeonLayout.cs` (2,060 lines)** — a randomised room-graph dungeon generator with themes, hazards, verticality and prop placement. Real, hard-won design work.
- **The RotMG systems backbone** — `Core/StatBlock.cs`, `Core/StatType.cs`, `Core/StatusEffect.cs`, `Loot/LootTable.cs`. The 8-stat model and independent-roll loot tables are solid.
- **Combat feel work** — `Classes/EnemyTargeting.cs` is already **crosshair-aimed** (a `SphereCast` with a 0.18 m aim-forgiveness radius), not tab-target. Its comments record that auto-target was deliberately removed *because* it made combat feel aim-optional. That is a tuned, first-person-correct decision worth porting verbatim.
- **`CLAUDE.md`'s "known sharp edges" list** — five specific Unity traps already hit and fixed once (the `Mask` alpha-test bug, the `CharacterController` teleport bug, the enum serialisation trap, `RectTransform.pivot`, `Image.fillAmount`). This is worth more than most of the code.
- **`World/HubLayout.cs` (901 lines)** — a medieval castle village hub with vendors, a claw machine and a gambling table. This is already most of a PEAK-style lobby.
- **A carriage ride already exists** — `GameBootstrap.BeginCarriageRide()` / `CarriageRideRoutine()`. Your carriage-cutscene idea is already prototyped.

---

## 2. Verified research: production decisions

### 2.1 Art — the all-free plan (revised after Q3)

You ruled out paid packs, so I went back and found a fully free stack. It is genuinely **better** for this project than the paid option I first proposed, for one specific reason explained below.

**Primary source: [Quaternius](https://quaternius.com/)** — free low-poly game assets under the **Quaternius Asset License (QAL)**. I read the actual licence text rather than trusting a summary:

> Grants *"a worldwide, royalty-free, non-exclusive, perpetual license, for personal, educational, or **commercial** purposes"* and *"**No attribution is required**"*. You may *"publish, distribute, sell, sublicense, and otherwise commercially exploit"* the resulting product.
>
> The only two restrictions: you may not redistribute the assets **as assets** (repackaged, resold, as a stock file or template), and you may not claim you authored them. Neither affects shipping them inside a game on Steam.

Older Quaternius packs are plain **CC0**. Both are clear for our use.

**The stack:**

| Need | Pack | Notes |
|---|---|---|
| Player characters | **Universal Base Characters** | 6 base bodies, 3 proportions, 20 hairstyles, **Humanoid rig** |
| The 8 class looks | **Modular Character Outfits – Fantasy** | **12 outfits from 62 modular parts**, 3 texture variants each, same Humanoid rig |
| Animation | **Universal Animation Library 1 + 2** | **~250 humanoid animations** on that same rig |
| Enemies | **Bestiary – Dungeon Monsters Kit** | 7 enemies + weapons, 3 colour variants, Humanoid rig |
| More enemies | **Ultimate Monsters**, **Easy Enemy Pack** | |
| Dungeon levels | **Modular Dungeons Pack** | Modular kit — pairs with a procedural generator |
| The lobby | **Medieval Village MegaKit** | |
| Weapons | **Modular Weapons Pack** | Medieval / fantasy |
| Props | **Fantasy Props MegaKit** | Furniture, weapons, clutter |

**Why this beats the paid option:** every character pack, every enemy, and the entire animation library share **one Humanoid rig**. That means a single animation set drives all 8 classes *and* the enemies, and any animation retargets onto any model. With KayKit I had flagged "do these import as Unity Humanoid avatars?" as an unverified risk that could have forced per-class animation work. Quaternius removes that risk by design — it is the pack family's whole selling point.

**Free supplements, all cleanly licensed:**

- **KayKit free tier** (CC0) — Dungeon Pack, Skeletons, Fantasy Weapon Bits, and 5 Adventurer characters. Style-compatible, useful for variety.
- **Kenney** (CC0) — props, UI, audio.
- **Mixamo** — live as of September 2026, free with an Adobe account, *"unlimited commercial use, no credit required"* per Adobe's own FAQ. Humanoid auto-rigger plus a large animation library; retargets onto the same Humanoid rig.

**Self-authored assets:** Blender is free and scriptable headlessly (`blender --background --python`), so anything the packs do not cover — custom boss silhouettes, bespoke props, recolours, modular part mixes — I can generate or modify directly. Blender is not currently installed; `winget` can install it.

**UNVERIFIED:** Quaternius free ("Standard") downloads reportedly contain 60–70% of each pack, with the remainder in a $20 "Source" edition. For *Modular Character Outfits – Fantasy* that would be roughly 8 of 12 outfits — enough for 8 classes, but I will confirm by actually downloading before relying on it.

**This supersedes the parked Meshy plan.** AI 3D generation was blocked on a login wall and would produce worse, unrigged, unanimated results at a cost. These packs are free, already rigged, already animated, share one rig, and carry no licensing risk on Steam.

### 2.2 Netcode — Steam's network is free, Unity's is metered

| | Unity Relay (installed today) | Steam Networking Sockets |
|---|---|---|
| Cost | **50 average CCU free, then $0.16/CCU plus bandwidth metering** | No published per-CCU or bandwidth fee — included with Steam partnership |
| Invites | Custom join codes you build yourself | **Native Steam friend invites and overlay** |
| NAT traversal | Unity Relay servers | Steam Datagram Relay (Valve's backbone) |
| Requires | UGS account with billing details on file | A Steam App ID (appid 480 works during development) |
| Works off-Steam | Yes | No — Steam-exclusive |

For a game that ships on Steam and whose players join via Steam friends, **Steam Sockets is the correct transport**: it is free, it gives you the invite flow players already expect, and it hides player IP addresses. Maintained NGO transport adapters for it exist (Facepunch.Steamworks-based).

The good news: because almost nothing is networked yet (see 1.1), **switching transport now costs nearly nothing.** Later, it would be expensive.

#### 2.2.1 Implementation plan (verified 2026-09-14)

**The transport package:** `com.community.netcode.transport.facepunch`, from Unity's own
[multiplayer-community-contributions](https://github.com/Unity-Technologies/multiplayer-community-contributions) repo.

I inspected its source rather than trusting its docs, and found one thing better and one
thing worse than expected:

- **Better:** it *vendors the Facepunch.Steamworks DLLs itself* — Win32, Win64, Linux and
  MacOS builds plus Steam's `redistributable_bin`. There is no separate NuGet step or
  DLL hunt. Adding the package gets the whole Steam stack.
- **Worse:** its `CHANGELOG.md` says it "targets Netcode for GameObjects **1.0.0**" and
  its README says "uses Facepunch.Steamworks **2.3.2**" (a 2020 release). We are on NGO
  **2.13.2**.

That second point looks alarming but is probably stale documentation rather than real
drift. I checked the actual overridden members against NGO 2.x's `NetworkTransport`
abstract surface and they match exactly: `OnEarlyUpdate`, `ServerClientId`,
`DisconnectLocalClient`, `DisconnectRemoteClient`, `GetCurrentRtt`,
`Initialize(NetworkManager)`, `Shutdown`, `Send`, `PollEvent`, `StartClient`,
`StartServer`. Nothing NGO 2.x removed is used. Upstream Facepunch.Steamworks itself is
healthy — 2.5.2 released April 2026, repo active September 2026, 3.7k stars.

**Decision: develop against `UnityTransport`, ship on `FacepunchTransport`.**

NGO makes the transport a swappable component
(`NetworkManager.NetworkConfig.NetworkTransport`), so this costs nothing and buys a lot:

1. **Steam transport cannot be meaningfully tested during development anyway.** It needs
   two Steam accounts on two machines. `UnityTransport` over localhost lets two clients
   run on this machine right now, which is how the networked gameplay actually gets built
   and debugged.
2. **It de-risks the App ID dependency.** Nothing blocks on a Steamworks account existing.
3. **If the Facepunch transport does turn out to be broken against NGO 2.13**, the
   fallback is `com.community.netcode.transport.steamnetworkingsockets` (Steamworks.NET
   based) from the same repo, and swapping is a one-component change rather than a rewrite.

The rule that matters: **all gameplay code must be transport-agnostic.** No gameplay
class may reference `Steamworks` types directly. Steam-specific concerns — lobbies,
friend invites, the overlay, rich presence — go behind an interface in
`Scripts/Networking/` with a null implementation for non-Steam dev runs.

---

## 3. The core tension in your spec, and how I would resolve it

You named five reference games. Two of them pull in opposite directions, and it is worth being explicit about that now rather than discovering it in playtesting.

**Tension 1 — RotMG progression vs. cosmetics-only persistence.**

Realm of the Mad God is a *stat-accumulation* game. Its entire progression loop is permanent stat potions, and the existing codebase is built on that assumption. But your new spec says loot does not carry over and only cosmetics persist.

These are compatible *only if* stat potions become a **within-run** resource: you gain stats during a run, and they reset when the run ends. That is the R.E.P.O. / Risk of Rain model, and it works well — but it means RotMG's stats become **run-scoped power**, not meta-progression. I think this is correct and I would recommend it, but it is your call (Q9), and it changes what "spend resources at the vendor" actually means.

**Tension 2 — Skyrim/Dark-and-Darker melee vs. the current combat model.**

Skyrim and Dark and Darker melee is about *swing arcs, wind-up, timing and stamina*. The current combat is an **instant aimed hitscan** — you point and it hits. It is a good aimed system, but it is not physical in the way you described. Making melee feel like Skyrim means adding swing arcs, wind-up frames, hit reactions and a stamina economy (Q8).

---

## 4. PROPOSAL: the recommended path

> **Recommendation: start a new Unity project in a new GitHub repo, and deliberately port the good parts across.**

This is a recommendation, not a done deal. It is your call (Q1).

**Why new rather than evolving the existing project:**

1. **Networking has to be designed in from the first line.** Converting 68 local `MonoBehaviour`s into server-authoritative networked behaviours is *more* work than writing them networked in the first place, and it leaves a long tail of desync bugs.
2. **The no-prefab pattern must be abandoned** in order to use rigged art and `NetworkObject` spawning. That pattern is load-bearing in nearly every existing file.
3. **URP instead of Built-in RP.** For a Steam-shippable look you want URP's lighting, post-processing and shader graph. Normally that migration is painful; here it is unusually cheap, because materials are created in code at only **33 `Shader.Find` call sites** — but it is still cheapest at line zero.
4. **The ability and class architectures need replacing anyway** (1.2b, 1.2c) to reach 8 classes without the code collapsing into switch statements.

**What gets ported rather than rewritten:**

`StatBlock` / `StatType` / `StatusEffect` - `LootTable` - the `DungeonLayout` room-graph *algorithm* - `EnemyTargeting`'s aim-forgiveness constants - the hub layout *design* - the boss encounter designs - **all of `CLAUDE.md`'s sharp-edge list**.

**The honest counter-argument:** if you would rather keep momentum and see something playable sooner, we can keep the existing project and do a staged conversion instead. That gets a running build faster; it will cost more total time and carry more bugs. Say the word and I will do it that way — I just will not pretend it is the cheaper path.

---

## 5. PROPOSAL: the 8 classes

Chosen so that every class (a) has a League-legible identity, (b) the four roles are each covered twice so a 1-4 player group always has a workable comp, and (c) all eight are buildable from **one** modular outfit pack on **one** shared rig.

| # | Class | Role | Fantasy | Status |
|---|---|---|---|---|
| 1 | **Knight** | Tank | Sword and board, block, taunt | exists |
| 2 | **Barbarian** | Bruiser | Two-hand, rage, self-damage risk | new |
| 3 | **Rogue** | Assassin | Dash, backstab, stealth window | new |
| 4 | **Ranger** | Marksman | Bow, traps, kiting | new |
| 5 | **Mage** | Burst / CC | Fire and ice, freeze-to-shatter combo | exists (Wizard) |
| 6 | **Druid** | Healer | Heal-over-time, roots, nature zones | new |
| 7 | **Engineer** | Zone control | Deployable turret, barricades | new |
| 8 | **Cleric** | Healer / Buff | Burst heals, cleanse, damage buffs | exists (Priest/Paladin) |

**Models:** all eight come from **Quaternius Universal Base Characters + Modular Character Outfits – Fantasy** (12 outfits, 62 modular parts, 3 texture variants each). Because the parts are modular and share one Humanoid rig, a class silhouette is an *assembly* — helmet + torso + legs + weapon — not a fixed model. That means eight visually distinct classes without eight separate character budgets, and it makes **cosmetics nearly free to produce**: a cosmetic is a swapped part or a texture variant.

Note this **retires the Paladin**, folding its damage-buff identity into the Cleric. If you want Paladin kept as a ninth class, or kept instead of one of these, that is Q10.

---

## 6. PROPOSAL: the game loop

```
  +---------------------------------------------------------+
  | LOBBY  (persistent, drop-in)                            |
  |   - Toys and activities while waiting (PEAK-style)      |
  |   - Cosmetic wardrobe                                   |
  |   - Class select                                        |
  |   - Ready up: sit on the carriage                       |
  +---------------------------+-----------------------------+
                              |  all seated -> CARRIAGE CUTSCENE
                              v
  +---------------------------------------------------------+
  | LEVEL N   fight through enemies -> BOSS (unique mechanic)|
  +---------------------------+-----------------------------+
                              |  boss dies -> board carriage
                              v
  +---------------------------------------------------------+
  | CAMP / VENDOR   spend the run's currency                |
  |   weapons - potions - upgrades   (NOT kept after run)   |
  +---------------------------+-----------------------------+
                              |  ready -> carriage -> LEVEL N+1
                              v
                       ... -> FINAL BOSS -> run ends
                              |
                              v
          Run summary -> COSMETIC unlocks (the only thing kept)
```

Everything inside a run is disposable. Everything kept is cosmetic. This is the Deep Rock Galactic / Lethal Company / R.E.P.O. model, and it is proven.

---

## 7. PROPOSAL: build order

Nothing here starts until Q1-Q6 are answered.

| Milestone | Contains | Definition of done |
|---|---|---|
| **M0 — Foundation** | New repo, Unity URP project, Steam transport, batch-mode compile check | Two players can connect and see each other move |
| **M1 — Vertical slice** | 1 class, 1 level, 1 boss, carriage, lobby, vendor | Four players can complete one full loop together |
| **M2 — Combat feel** | Melee arcs, stamina, hit reactions, animation, SFX | Combat feels good enough to want to repeat |
| **M3 — The 8 classes** | All 8 kits, 24 abilities, first balance pass | Every class is distinct and viable solo |
| **M4 — Content** | N levels, N bosses, enemy roster, vendor economy | A full run is completable end to end |
| **M5 — Meta** | Cosmetics, unlocks, run summary, save system | A reason to play a second run |
| **M6 — Ship** | Steam page, build pipeline, playtest, polish | Uploadable to Steam |

---

## 8. Repo and tooling notes

- **No `gh` CLI installed.** `winget` is available (v1.29.290), so I can install it — but `gh auth login` needs you to complete a browser sign-in. The alternative is that you create the empty repo on github.com and hand me the URL. (Q2)
- **Git push auth already works.** Windows Credential Manager holds valid GitHub credentials — verified with a live `git ls-remote` against the existing repo. Once a repo exists, I can push to it.
- **Unity Editor:** `C:\Program Files\Unity\Hub\Editor\6000.3.23f1\Editor\Unity.exe`. Version 2020.3.30f1 is also installed and should be considered dead.
- **Verification loop:** there is no CLI test suite. Unity's compiler in batch mode is the only automated check available. Play-mode behaviour needs you to test it — I will ask you rather than script fake automated Play-mode diagnostics.

---

## 9. QUESTIONS FOR YOU

### BLOCKING — still open

**Q2. GitHub repo — name, and how do you want it created?**
What should it be called? And do you want me to install `gh` (you would do one browser login), or will you create the empty repo and give me the URL?

**Q3a. Confirm the all-free Quaternius art stack?** (section 2.1)
Free, commercial-use-clear, rigged, animated, one shared rig across all characters and enemies. This locks the visual identity, so I want it explicit rather than assumed.

**Q6. How many levels in v1?**
Three? Five? Eight? This sets the content budget for everything else. I would suggest starting at three and extending, since each level needs a bespoke boss.

### DECIDED

- **Q1 — New Unity project, new GitHub repo.** Port systems, not architecture.
- **Q4 — Steam Networking Sockets, Steam-exclusive.**
- **Q5 — URP.**
- **Q3 — Free assets only.** Resolved into the Quaternius stack in section 2.1, pending Q3a confirmation.

### DESIGN — needed soon, not blocking M0

**Q7.** What is the game actually called?

**Q8. Melee combat model.** Skyrim / Dark-and-Darker swing arcs with wind-up and stamina, or keep the current instant aimed hitscan? This affects how every melee class feels.

**Q9. Do stat potions reset between runs?** Per section 3, I recommend yes — stats become run-scoped power, and cosmetics are the only meta-progression. Confirm?

**Q10. The class roster in section 5** — approve as-is? Keep Paladin? Any class fantasy you specifically want that I have missed?

**Q11. Death and wipes.** When one player dies mid-level: spectate until the boss dies? Revived by a teammate (R.E.P.O. style)? And does a full party wipe end the run entirely?

**Q12. Target run length.** The old scope doc said 2.5-3 hours. That is very long for a co-op session game. 45-90 minutes feels more right for this loop. What are you aiming at?

**Q13. Lobby activities** — which specific toys? (Physics objects to throw, a training dummy, the existing claw machine and gambling table, emotes, a parkour course, a shooting range, a co-op puzzle?)

**Q14. Friendly fire — on, off, or reduced?** R.E.P.O. and Dark and Darker both use it for comedy and tension; it also makes pub groups miserable.

**Q15. Proximity voice chat?** It is a signature R.E.P.O. feature and a large part of that game's feel. It is also real engineering work.

**Q16. Difficulty scaling for 1 vs 4 players.** Should solo be a genuinely balanced experience, or is this primarily a group game that solo players can attempt?

**Q17. How are cosmetics earned?** Boss first-kills? Run completions? Challenges? Random drops? This is your entire retention loop.

### LOGISTICS

**Q18.** Do you already have a Steamworks partner account or App ID? It is a one-time $100 per-app fee, recoupable after $1,000 of sales. Not needed yet — appid 480 works for development — but it gates shipping.

**Q19.** Do you want a Steam **Playtest** or **Early Access** phase? For a 4-player co-op game, getting real groups testing early matters more than usual.

**Q20.** How much time per week are you putting into this, and is there a deadline? This changes how I scope everything above.

---

## 10. Changelog

| Date | Change |
|---|---|
| 2026-09-13 | Created. Audited the existing project, researched art / netcode / Steam sourcing, drafted proposals and the question list. |
