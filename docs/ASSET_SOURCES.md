# Asset Sources & Licences

Every third-party asset in Deepbound is recorded here with its source and licence terms.
**Nothing ships in this game unless it appears in this file with a licence cleared for
commercial use.** If you add an asset, add a row.

Asset packs are **gitignored** (see `.gitignore`) — they are downloaded, not authored,
and several hundred MB of third-party binaries would bloat every clone. This file is how
a fresh clone knows what to fetch.

---

## Licence summary

| Licence | Commercial use | Attribution | Notes |
|---|---|---|---|
| **CC0 1.0** | Yes | Not required | Public domain dedication. No restrictions. |
| **QAL** (Quaternius Asset License) | Yes | Not required | Royalty-free, perpetual, worldwide. Two restrictions, neither of which affects shipping a game: you may not redistribute the assets *as assets* (repackaged/resold as an asset pack, stock file or template), and you may not claim you authored them. |
| **Mixamo / Adobe** | Yes | Not required | Adobe's FAQ: characters and animations are free "for unlimited commercial or non commercial use" with no credit required. |

---

## Download list

Everything below is **free**. Download the **Standard** (free) edition of each Quaternius
pack — the paid "Source" edition only adds `.blend` files and per-engine demo projects,
which we do not need.

### Characters — the 8 playable classes

| Pack | Source | Licence | Extract to |
|---|---|---|---|
| **Universal Base Characters** | [quaternius.com](https://quaternius.com/packs/universalbasecharacters.html) | QAL | `Assets/Art/Characters/Base/` |
| **Modular Character Outfits – Fantasy** | [quaternius.com](https://quaternius.com/packs/modularcharacteroutfitsfantasy.html) | CC0 | `Assets/Art/Characters/Outfits/` |

12 outfits built from 62 modular parts, 3 texture variants each, all on one Humanoid rig.
The 8 classes are *assemblies* of these parts, not 8 separate models — which is also why
cosmetics are cheap to produce here.

### Animation

| Pack | Source | Licence | Extract to |
|---|---|---|---|
| **Universal Animation Library** | [quaternius.com](https://quaternius.com/packs/universalanimationlibrary.html) | CC0 | `Assets/Art/Characters/Animations/` |
| **Universal Animation Library 2** | [quaternius.com](https://quaternius.com/packs/universalanimationlibrary2.html) | CC0 | `Assets/Art/Characters/Animations/` |
| **Mixamo** (as needed) | [mixamo.com](https://www.mixamo.com) | Adobe | `Assets/Art/Characters/Animations/Mixamo/` |

~250 humanoid animations on the same rig as the characters above, so one animation set
drives all 8 classes *and* the enemies. Mixamo fills any gaps and retargets onto the same
Humanoid avatar.

### Enemies

| Pack | Source | Licence | Extract to |
|---|---|---|---|
| **Bestiary – Dungeon Monsters Kit** | [quaternius.com](https://quaternius.com/packs/bestiarydungeonmonsterskit.html) | QAL | `Assets/Art/Characters/Enemies/` |
| **Ultimate Monsters** | [quaternius.com](https://quaternius.com/) | CC0 | `Assets/Art/Characters/Enemies/` |

### Environment

| Pack | Source | Licence | Extract to |
|---|---|---|---|
| **Modular Dungeons Pack** | [quaternius.com](https://quaternius.com/) | CC0 | `Assets/Art/Environment/Dungeon/` |
| **Medieval Village MegaKit** | [quaternius.com](https://quaternius.com/) | CC0 | `Assets/Art/Environment/Village/` |
| **Ultimate Modular Ruins Pack** | [quaternius.com](https://quaternius.com/) | CC0 | `Assets/Art/Environment/Ruins/` |

Dungeon kit for the three levels, village kit for the lobby.

### Weapons & props

| Pack | Source | Licence | Extract to |
|---|---|---|---|
| **Modular Weapons Pack** | [quaternius.com](https://quaternius.com/) | CC0 | `Assets/Art/Weapons/` |
| **Fantasy Props MegaKit** | [quaternius.com](https://quaternius.com/) | CC0 | `Assets/Art/Props/` |

### Optional supplements (style-compatible, all free)

| Pack | Source | Licence |
|---|---|---|
| KayKit Dungeon Pack, Skeletons, Fantasy Weapon Bits, Adventurers | [kaylousberg.itch.io](https://kaylousberg.itch.io) | CC0 |
| Kenney asset packs (props, UI, audio) | [kenney.nl](https://kenney.nl) | CC0 |

---

## How to download

Quaternius packs are distributed through itch.io. For each pack:

1. Open the pack page on quaternius.com and click **Download on Itch.io**.
2. Click **Download Now**.
3. Click **"No thanks, just take me to the downloads"** — do not enter a payment.
4. Download the `[Standard]` zip (the `[Source]` one costs $20 and we do not need it).
5. Extract into the path in the table above.

Automated download is not possible: itch.io's download endpoint requires a session the
browser automation cannot complete, and the in-app browser blocks file downloads.

---

## Audio

Not yet sourced. Candidates when we get there, all commercially cleared:

- **Kenney audio packs** — CC0
- **freesound.org** — filter to CC0 only; CC-BY requires attribution, and some uploads
  are NonCommercial, which would block a Steam release
- Procedural synthesis for UI/impact SFX, which is what the previous prototype did

---

## Rejected sources, and why

| Source | Why not |
|---|---|
| **Meshy.ai** and other AI 3D generators | Generation is gated behind account creation and a CAPTCHA. Output is also unrigged and unanimated, which is the expensive part — the packs above already solve it. |
| **Unity Asset Store "free" packs** | Many use the Asset Store EULA rather than CC0, which restricts redistribution in a source repo and sometimes commercial use. Not worth the per-asset legal reading. |
| **Sketchfab CC-BY models** | Usable, but attribution obligations accumulate across dozens of assets and are easy to get wrong. CC0 avoids the problem entirely. |
