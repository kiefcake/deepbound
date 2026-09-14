# Deepbound

A 1–4 player first-person co-op fantasy dungeon crawler. Eight classes, three levels,
three bosses that have to be *solved* rather than out-damaged. Everything you loot is
gone when the run ends; everything you unlock is cosmetic.

Built in **Unity 6000.3.23f1** (URP), networked over **Steam Networking Sockets**,
targeting **Steam**.

---

## The loop

```
LOBBY  →  carriage  →  LEVEL 1  →  BOSS  →  vendor
                          ↑                    │
                          └────────────────────┘
                       (×3, then the run ends)
```

Players spawn into a persistent lobby with things to do while friends join. When
everyone sits on the carriage, a cutscene carries the party to the next level. Fight
through it, beat a boss with a real mechanical solution, then spend the run's currency
at a vendor before boarding again. Loot and stats reset at the end of a run. Cosmetics
persist.

## Design references

| Game | What we take from it |
|---|---|
| **League of Legends** | Readable, distinct per-class kits — identity over stat sticks |
| **Dark and Darker** | First-person dungeon tension, gritty medieval fantasy |
| **R.E.P.O.** | Co-op physicality, the between-levels shop, the hub-as-vehicle |
| **Realm of the Mad God** | The 8-stat system, potions, independent-roll loot tables |
| **Skyrim** | First-person melee weight — swing arcs, not hitscan |
| **PEAK** | The pre-game lobby as a place with toys, not a menu |

## Repo layout

```
Assets/
  Editor/        Editor-only tooling (project bootstrap, build scripts)
  Settings/      URP pipeline + renderer assets
  Scenes/
  Art/           Third-party asset packs (gitignored — see docs/ASSET_SOURCES.md)
  Audio/
  Prefabs/
  Resources/
  Scripts/
    Core/        Stats, health, status effects, run state
    Networking/  Steam transport, session lifecycle, replication helpers
    Player/      Movement, camera, interaction, input
    Classes/     The 8 class definitions and their weapon behaviours
    Abilities/   Ability system
    Enemies/     Enemy AI and boss encounters
    World/       Lobby, level generation, props, hazards
    UI/
    Progression/ Cosmetics, unlocks, save data
    Loot/        Drop tables, vendor economy
docs/
  DESIGN_DOC.md      Source of truth: decisions, proposals, open questions
  ASSET_SOURCES.md   Every third-party asset, where it came from, and its licence
```

## Getting set up

1. Install Unity **6000.3.23f1** via Unity Hub.
2. Clone this repo and open the folder in Unity. First import takes a few minutes.
3. Download the art packs listed in [`docs/ASSET_SOURCES.md`](docs/ASSET_SOURCES.md)
   and extract them where that file says. They are gitignored deliberately — several
   hundred MB of third-party binaries do not belong in the repo.

### Reconfiguring the project from scratch

The URP pipeline asset, colour space and physics settings are all created by a single
idempotent editor method rather than by hand, so the configuration is reproducible:

```bash
"C:\Program Files\Unity\Hub\Editor\6000.3.23f1\Editor\Unity.exe" -projectPath . -batchmode -nographics -quit -executeMethod Deepbound.EditorTools.ProjectBootstrap.Configure -logFile bootstrap.log
```

## Verifying changes

There is no CLI test suite — Unity's compiler is the only automated check available.
To verify a change compiles:

```bash
"C:\Program Files\Unity\Hub\Editor\6000.3.23f1\Editor\Unity.exe" -projectPath . -batchmode -nographics -quit -logFile compile.log
```

Then check `compile.log` for `error CS`. A clean run means it **compiles** — it does not
mean the feature **works**. Play-mode behaviour needs a human to actually press Play.

## Licence

The game code in this repo is owned by the project. Third-party art, audio and
animation assets retain their own licences — each one is recorded in
[`docs/ASSET_SOURCES.md`](docs/ASSET_SOURCES.md) with its terms. Every asset used is
cleared for commercial release.
