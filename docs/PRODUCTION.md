# Deepbound — production rules we follow

General game-production practice, written down so every new feature is held to it. Each rule
names how Deepbound applies it today.

## 1. Readability before difficulty
Every threat must be readable before it hurts you.
- Every big attack is telegraphed (`Telegraph.Show` circles and lines, wind-up animations whose
  length is the gameplay wind-up). New attacks must use one.
- Enemies with a special rule wear a floating tag (SHIELDED, ETHEREAL, RITUAL WARD, affixes) or a
  glowing mark (weak points). New rules get a tag or a mark.
- Afflictions show on the HUD and as coloured wisps on the afflicted player, so allies can see them.

## 2. Teach by play, not by manual
- First-time contextual tips (`UI/Tips.cs`): each new mechanic is explained once, the first time
  it is met, then never again (Options can reset or disable them). Since 2026-11-14 only `Tips.Essential` show by
  default (controls, statuses done to you, co-op rescue, rules that kill without a tell); every other mechanic is left
  for the player to discover (Options "Explain every mechanic" turns them back on). Do not add tips for new mechanics
  unless they fall in those groups.
- The floor banner states the floor's rule; the pause-menu Guide is the reference, not the tutorial.

## 3. Feedback for every action ("juice")
- Hits: sound, damage number, camera kick; heavier for crits, headshots and weak points.
- Every kill-worthy moment has a callout (multi-kills, SLICED, INTO THE ABYSS, SHIELD BROKEN).
- If an action has no visible or audible response, it is not finished.

## 4. Counterplay for every punishment
Nothing should only be suffered. Each threat has an answer the player can learn:
- Shields → kick / bash / flank. Ethereal → chill, stun, mark. Curse → kill the Hexer.
- Poison, frostbite, curse → Holy Water. Shockwaves → jump. Tide → high ground.

## 5. Accessibility and comfort
- Options: mouse sensitivity, volume, FOV, **screen shake** strength (0 = off), **damage numbers**
  toggle, **reduce flashing** (dims every `Vfx.Flash`), **tips** toggle, **frame-rate readout**.
- Colour is never the only signal: tags carry words, telegraphs have shapes and sounds.

## 6. Performance budget
- Target 60 fps (16.7 ms). The screenshot run logs `[Perf]` frame time and light counts per view.
- `Art/LightBudget`: lights beyond 42 m are switched off; only the 4 nearest shadow-casters keep
  shadows. New light sources must be children of the level root to fall under it.
- Visual-only effects (motes, shaft dust, pendulums, flames) run only near the camera.

## 7. Stability: test before shipping
- Every feature lands with a smoke-test check (`-smoketest`), and the build ships only with zero
  FAIL lines. Tests must never touch the player's real save (`TestMode` flags).
- Commit after each verified chunk; the playable build is replaced only when the game is closed.

## 8. Scope and iteration
- Small, complete increments over big unfinished ones. Balance numbers are first guesses until
  the play-test pass; they live in one place per system so they are easy to tune.
