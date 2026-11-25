#!/usr/bin/env python3
import argparse
import os
import re
import subprocess
import sys
import time

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def read(path):
    return open(path, encoding='utf-8', errors='replace').read() if os.path.exists(path) else ''


def parse_args():
    ap = argparse.ArgumentParser(description='Play a real party of bots over localhost and fail if any member does nothing.')
    ap.add_argument('--classes', nargs='+', default=['Mage', 'Knight', 'Cleric', 'Rogue'])
    ap.add_argument('--floors', type=int, default=1)
    ap.add_argument('--build', default=os.path.join(ROOT, 'Builds', 'Deepbound'))
    ap.add_argument('--out', default=None, help='log folder (default <build>/partycheck)')
    ap.add_argument('--port', default='7797')
    ap.add_argument('--minutes', type=float, default=25.0, help='give up after this many minutes per floor')
    return ap.parse_args()


def launch(args, build, out):
    exe = os.path.join(build, 'Deepbound.exe')
    window = ['-port', args.port, '-screen-fullscreen', '0', '-screen-width', '640', '-screen-height', '360',
              '-autofloors', str(args.floors)]
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
    return procs, logs


def wait(args, procs, host_txt):
    deadline = time.time() + args.minutes * 60 * args.floors
    try:
        while time.time() < deadline:
            if 'RUN OVER' in read(host_txt):
                return True
            if procs[0].poll() is not None:
                return False
            time.sleep(10)
        return False
    finally:
        time.sleep(3)
        for p in procs:
            if p.poll() is None:
                p.terminate()


def judge(args, logs, finished):
    fails = []
    host = read(logs[0][1])
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
        text = read(txt)
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
        errors = read(player_log)
        n = len(re.findall(r'^\w*Exception', errors, re.M))
        if n:
            fails.append(f'{cls}: {n} exception(s) in {player_log}')
        if '[Sfx] no recording' in errors:
            fails.append(f'{cls}: a sound had no recording')
    return fails


def main():
    args = parse_args()
    build = os.path.abspath(args.build)
    out = os.path.abspath(args.out) if args.out else os.path.join(build, 'partycheck')
    os.makedirs(out, exist_ok=True)
    procs, logs = launch(args, build, out)
    fails = judge(args, logs, wait(args, procs, logs[0][1]))
    for f in fails:
        print('FAIL ' + f)
    print('party check: ' + ('FAILED' if fails else f'PASS ({len(args.classes)} players, {args.floors} floor(s))'))
    return 1 if fails else 0


if __name__ == '__main__':
    sys.exit(main())
