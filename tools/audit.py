#!/usr/bin/env python3
"""Source audit: rules that a play test will not notice until someone is looking at the wrong monitor.

Run from anywhere: python tools/audit.py. Exit code 0 = clean. Reads only; needs nothing but Python.
"""
import os, re, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SCRIPTS = os.path.join(ROOT, 'Assets', 'Scripts')
SFX = os.path.join(ROOT, 'Assets', 'Resources', 'Audio', 'Sfx')
AUDIO_EXT = ('.wav', '.ogg', '.mp3')
problems = []


def sources():
    for base, _, files in os.walk(SCRIPTS):
        for f in files:
            if f.endswith('.cs'):
                path = os.path.join(base, f)
                yield os.path.relpath(path, SCRIPTS).replace('\\', '/'), open(path, encoding='utf-8').read()


def code_only(text):
    """Comments out, so a note about the old way does not count as the old way."""
    return re.sub(r'//.*', '', text)


# Real pixels are only for these: everything else draws in UiScale's virtual space.
PIXELS_OK = {'Networking/SmokeTest.cs', 'UI/UiScale.cs', 'UI/PadCursor.cs', 'UI/RetroLook.cs', 'UI/Settings.cs', 'Player/FirstPersonMotor.cs'}
WORLD_OK = {'UI/UiScale.cs', 'Networking/SmokeTest.cs'}
SYNTH_OK = {'Art/TavernMusic.cs', 'Player/PlayerVoice.cs'}  # the band's songs, and incoming voice chat
NO_DRAWING = {'Networking/NetworkLauncher.cs'}              # its OnGUI draws nothing any more
STALE = ['BearTrap', 'Bear Trap', 'bear trap', 'MusicSynth', 'AmbienceSynth', 'BlinkGhost']

for name, text in sources():
    code = code_only(text)
    if name not in PIXELS_OK and re.search(r'\bScreen\.(width|height)\b', code):
        problems.append(f'{name}: Screen.width/height in UI code: use UiScale.W/H, or the UI will not scale')
    if name not in WORLD_OK and 'WorldToScreenPoint' in code:
        problems.append(f'{name}: WorldToScreenPoint: use UiScale.World, or labels drift at other resolutions')
    if 'RotateAroundPivot' in code:
        problems.append(f'{name}: RotateAroundPivot: use UiScale.Rotate, it swings around the wrong point under a scale')
    if name not in SYNTH_OK and 'AudioClip.Create' in code:
        problems.append(f'{name}: AudioClip.Create: sound effects are recordings, no coded noise')
    if 'void OnGUI' in code and 'UiSkin.Apply' not in code and name not in NO_DRAWING:
        problems.append(f'{name}: OnGUI without UiSkin.Apply: it would draw unscaled')
    for word in STALE:
        if word in code:
            problems.append(f'{name}: mentions the removed "{word}"')

# Every Sound has at least one recording.
sfx = open(os.path.join(SCRIPTS, 'Art', 'Sfx.cs'), encoding='utf-8').read()
enum = re.search(r'enum Sound\s*\{(.*?)\}', code_only(sfx), re.S).group(1)
sounds = [s.strip() for s in enum.replace('\n', ' ').split(',') if s.strip()]
for s in sounds:
    folder = os.path.join(SFX, s)
    clips = [f for f in os.listdir(folder) if f.lower().endswith(AUDIO_EXT)] if os.path.isdir(folder) else []
    if not clips:
        problems.append(f'Sound.{s}: no recording in Resources/Audio/Sfx/{s}/ (it would be silent)')

for p in problems:
    print('FAIL ' + p)
print(f'audit: {len(sounds)} sounds checked, {len(problems)} problem(s)')
sys.exit(1 if problems else 0)
