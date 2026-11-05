#!/usr/bin/env python3
"""A real party: one window per bot, joined over localhost, playing real floors. Fails if any member does nothing.

    python tools/test_party.py                               # Mage + Knight + Cleric + Rogue, one floor
    python tools/test_party.py --classes Ranger Flagellant --floors 2

Needs a free desktop (it opens windows; do not run it while playing). It only ever stops the processes it started.
"""
import argparse, os, re, subprocess, sys, time

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ap = argparse.ArgumentParser()
ap.add_argument('--classes', nargs='+', default=['Mage', 'Knight', 'Cleric', 'Rogue'])
ap.add_argument('--floors', type=int, default=1)
ap.add_argument('--build', default=os.path.join(ROOT, 'Builds', 'Deepbound'))
ap.add_argument('--port', default='7797')
ap.add_argument('--minutes', type=float, default=25.0, help='give up after this many minutes per floor')
args = ap.parse_args()

build = os.path.abspath(args.build)
out = os.path.join(build, 'partycheck')
os.makedirs(out, exist_ok=True)
exe = os.path.join(build, 'Deepbound.exe')
window = ['-port', args.port, '-screen-fullscreen', '0', '-screen-width', '640', '-screen-height', '360', '-autofloors', str(args.floors)]
procs, logs = [], []
for i, cls in enumerate(args.classes):
    d = os.path.join(out, f'{i}_{cls}')
    os.makedirs(d, exist_ok=True)
    txt = os.path.join(d, 'autoplay.txt')
    if os.path.exists(txt):
        os.remove(txt)
    role = ['-autoparty', str(len(args.classes))] if i == 0 else ['-autofollow', str(i)]
    player_log = os.path.join(d, 'player.log')
    procs.append(subprocess.Popen([exe, '-autoplay', d, '-autoclass', cls] + role + window + ['-logFile', player_log]))
    logs.append((cls, txt, player_log))
    time.sleep(6 if i == 0 else 3)

deadline = time.time() + args.minutes * 60 * args.floors
host_txt = logs[0][1]
finished = False
try:
    while time.time() < deadline:
        if os.path.exists(host_txt) and 'RUN OVER' in open(host_txt, encoding='utf-8', errors='replace').read():
            finished = True
            break
        if procs[0].poll() is not None:
            break
        time.sleep(10)
    time.sleep(3)
finally:
    for p in procs:
        if p.poll() is None:
            p.terminate()

fails = []
host = open(host_txt, encoding='utf-8', errors='replace').read() if os.path.exists(host_txt) else ''
if not finished:
    fails.append('the run did not finish in time')
if len(args.classes) > 1 and f'PARTY of {len(args.classes)} ' not in host:
    fails.append('not everyone joined')
floors = re.findall(r'floor done in .*', host)
if len(floors) < args.floors:
    fails.append(f'{len(floors)} of {args.floors} floors finished')
for line in re.findall(r'.*party this floor: .*', host):
    print(line.strip()[:420])
    for member, dealt in re.findall(r'(\w+ \(\w+\)): dealt (\d+)', line):
        if int(dealt) <= 0:
            fails.append(f'{member} dealt no damage on a floor')
for cls, txt, player_log in logs:
    text = open(txt, encoding='utf-8', errors='replace').read() if os.path.exists(txt) else ''
    used = re.findall(r'abilities used this floor: (.*)', text)
    print(f'{cls}: abilities per floor: {used}')
    if not used:
        fails.append(f'{cls} logged no finished floor')
    fails += [f'{cls} used no abilities on a floor' for u in used if u.strip() == 'none']
    kits = re.findall(r'KIT (.*)', text)
    if kits:
        print(f'{cls}: final kit: {kits[-1]}')
    elif args.floors > 1:
        fails.append(f'{cls} never reported a kit')
    unspent = re.findall(r'\?\? UNSPENT (.*)', text)
    if unspent:
        fails.append(f'{cls}: left camp with {len(unspent)} thing(s) undone, first: {unspent[0][:160]}')
    if args.floors > 1 and 'R Ultimate' not in ' '.join(used):
        fails.append(f'{cls} never used its ultimate')
    stuck = [x for x in re.findall(r'\?\? .*', text) if 'UNSPENT' not in x]
    if len(stuck) > 8:
        fails.append(f'{cls}: {len(stuck)} problem lines, first: {stuck[0][:160]}')
    if os.path.exists(player_log):
        errors = open(player_log, encoding='utf-8', errors='replace').read()
        n = len(re.findall(r'^\w*Exception', errors, re.M))
        if n:
            fails.append(f'{cls}: {n} exception(s) in {player_log}')
        if '[Sfx] no recording' in errors:
            fails.append(f'{cls}: a sound had no recording')

for f in fails:
    print('FAIL ' + f)
print('party check: ' + ('FAILED' if fails else f'PASS ({len(args.classes)} players, {args.floors} floor(s))'))
sys.exit(1 if fails else 0)
