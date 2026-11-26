"""Summarise real-run logs (RunLogs/*.jsonl) into a plain-text report with flagged problems.

Usage: python tools/run_report.py [files or folders ...] [--out report.txt]
With no paths it reads the game's own RunLogs folder.
"""
import argparse
import glob
import json
import os
from collections import Counter, defaultdict

DEFAULT_DIR = os.path.join(os.environ.get('USERPROFILE', ''), 'AppData', 'LocalLow', 'Deepbound', 'Deepbound', 'RunLogs')

SLOW_FLOOR = 900
SLOW_GUARDIAN = 360
FAST_GUARDIAN = 25
IDLE_FLAG = 3
LOW_FPS = 40
HIGH_RTT = 150
SHARE_GAP = 2.0


def find_default():
    hits = glob.glob(os.path.join(os.environ.get('USERPROFILE', ''), 'AppData', 'LocalLow', '*', 'Deepbound', 'RunLogs'))
    return hits[0] if hits else DEFAULT_DIR


def load(paths):
    files = []
    for p in paths:
        files += sorted(glob.glob(os.path.join(p, '*.jsonl'))) if os.path.isdir(p) else [p]
    runs = []
    for f in files:
        rows = []
        with open(f, encoding='utf-8') as fh:
            for line in fh:
                line = line.strip()
                if line:
                    try:
                        rows.append(json.loads(line))
                    except json.JSONDecodeError:
                        rows.append({'t': 'broken', 'x': line[:120]})
        runs.append((f, rows))
    return runs


def top(d, n=8):
    return ', '.join(f'{k} {v:.0f}' if isinstance(v, float) else f'{k} {v}' for k, v in sorted(d.items(), key=lambda kv: -kv[1])[:n])


def mins(s):
    return f'{s / 60:.1f} min' if s and s > 0 else '-'


def report_run(path, rows, out, flags):
    head = next((r for r in rows if r['t'] in ('run', 'client')), {})
    out.append('=' * 78)
    out.append(os.path.basename(path))
    if head.get('t') == 'client':
        report_client(rows, head, out, flags)
        return
    party = ', '.join(f"{p['name']} ({p['class']})" for p in head.get('party', []))
    out.append(f"build {head.get('build')}  difficulty {head.get('difficulty')}  omen {head.get('omen') or 'none'}  daily {head.get('daily')}  steam {head.get('steam')}")
    out.append(f'party: {party}')
    m = head.get('machine', {})
    out.append(f"host machine: {m.get('gpu')} / {m.get('cpu')} / {m.get('ram')} MB / {m.get('screen')} / quality {m.get('quality')}")
    end = next((r for r in rows if r['t'] == 'end'), None)
    out.append(f"result: {end['result'] if end else 'unfinished'}  floors {end['floors'] if end else '?'}  total {mins(end['seconds']) if end else '?'}")

    totals = defaultdict(lambda: Counter())
    sources = defaultdict(Counter)
    taken_from = Counter()
    killers = Counter()
    used = Counter()
    events = [r for r in rows if r['t'] == 'ev']
    pending = []
    for r in rows:
        if r['t'] == 'floor':
            out.append('-' * 78)
            out.append(f"floor {r['depth'] + 1} {r['name']}  [{r['result']}]  {mins(r['seconds'])}  guardian {r.get('guardian') or '-'} {mins(r['guardianSeconds'])}  event {r['event']}")
            if r['seconds'] > SLOW_FLOOR:
                flags.append(f"floor {r['depth'] + 1}: slow floor ({mins(r['seconds'])})")
            gs = r['guardianSeconds']
            if gs > SLOW_GUARDIAN:
                flags.append(f"floor {r['depth'] + 1}: long guardian fight {r.get('guardian')} ({mins(gs)})")
            if 0 < gs < FAST_GUARDIAN:
                flags.append(f"floor {r['depth'] + 1}: guardian {r.get('guardian')} died in {gs:.0f} s")
            rooms = r.get('roomSeconds', {})
            if rooms:
                out.append('  time by room: ' + top(rooms, 6))
            dealt_all = sum(p['dealt'] for p in r['players']) or 1
            for p in r['players']:
                share = p['dealt'] / dealt_all
                out.append(f"  {p['name']:<14} {p['class']:<12} +{p['weapon']} path {p['path']}  dealt {p['dealt']:.0f} ({share:.0%}, guardian {p['dealtGuardian']:.0f})  taken {p['taken']:.0f}  healed {p['healed']:.0f}")
                out.append(f"    kills {p['kills']}  downs {p['downs']} ({p['downedSeconds']:.0f} s down)  revives {p['revives']}  gold {p['gold']} (+{p['goldGained']})  embers {p['embers']} ({p['embersDelta']:+})  sigils {p['sigils']} ({p['sigilsDelta']:+})  pouch {p.get('pouch')}")
                if p['relics']:
                    out.append('    relics: ' + ', '.join(p['relics']))
                out.append('    damage by source: ' + top(p['bySource']))
                out.append('    hurt by: ' + top(p['takenFrom'], 6))
                for k in ('dealt', 'taken', 'kills', 'downs', 'revives', 'healed'):
                    totals[p['name'] + ' (' + p['class'] + ')'][k] += p[k]
                for k, v in p['bySource'].items():
                    sources[p['class']][k] += v
                for k, v in p['takenFrom'].items():
                    taken_from[k] += v
                for k, v in p.get('used', {}).items():
                    used[k] += v
            if len(r['players']) > 1:
                shares = sorted(p['dealt'] for p in r['players'])
                if shares[0] > 0 and shares[-1] / shares[0] > SHARE_GAP * len(shares) / 2:
                    flags.append(f"floor {r['depth'] + 1}: damage spread wide ({shares[-1]:.0f} vs {shares[0]:.0f})")
            spawned = r.get('spawned', {})
            if spawned:
                out.append('  enemies at start: ' + top(spawned, 10))
            out.extend(pending)
            pending.clear()
        elif r['t'] == 'leftover':
            left = r.get('enemies', {})
            if left:
                pending.append(f"  enemies left alive at floor end: {sum(left.values())} ({top(left, 6)})")
            pending.append(f"  objects left on the floor: {top(r.get('objects', {}), 12)}  loot on ground {r.get('lootOnGround')}")
        elif r['t'] == 'down':
            killers[r['by']] += 1
            pending.append(f"  DOWN {r['who']} by {r['by']} at {r['s']:.0f} s in {r['room']} ({r['standing']} standing, guardian fight {r['guardianFight']}, pouch {r.get('pouch')})")
        elif r['t'] == 'guardianDown':
            pending.append(f"  guardian {r['name']} fell after {r['fightSeconds']:.0f} s (last hit {r['by']})")

    out.extend(pending)
    idle = Counter(e.get('who') for e in events if e['k'] == 'idle')
    for who, n in idle.items():
        if n >= IDLE_FLAG:
            flags.append(f'{who} stood still 45 s+ {n} times (lost, waiting or confused?)')
    for e in events:
        if e['k'] in ('joined', 'left'):
            flags.append(f"party change on floor {e['depth'] + 1}: {e['k']} ({e['x']})")
    ticks = [r for r in rows if r['t'] == 'tick' and r.get('fps')]
    low = [t for t in ticks if t['fps'] < LOW_FPS]
    if ticks:
        out.append(f"host fps: mean {sum(t['fps'] for t in ticks) / len(ticks):.0f}, worst frame {max(t['worstMs'] for t in ticks):.0f} ms, {len(low)} of {len(ticks)} samples under {LOW_FPS}")
        if len(low) > len(ticks) * 0.2:
            flags.append(f'host under {LOW_FPS} fps in {len(low)} of {len(ticks)} samples')

    out.append('-' * 78)
    out.append('RUN TOTALS')
    for who, c in totals.items():
        out.append(f"  {who:<28} dealt {c['dealt']:.0f}  taken {c['taken']:.0f}  healed {c['healed']:.0f}  kills {c['kills']}  downs {c['downs']}  revives {c['revives']}")
    for cls, c in sources.items():
        out.append(f'  {cls} damage by source: ' + top(c, 10))
    out.append('  most damage to players from: ' + top(taken_from, 10))
    out.append('  what downed players: ' + top(killers, 10))
    acts = Counter()
    for k, v in used.items():
        acts[k.split(':')[0] + ':' + k.split(':', 1)[1].split(' ')[0] if ':' in k else k] += v
    out.append('  things used: ' + top(acts, 30))
    buys = [e for e in events if e['k'] == 'buy']
    if buys:
        out.append('  purchases: ' + '; '.join(f"{e.get('who')} {e['x']}" for e in buys[:40]))
    relics = [e for e in events if e['k'] == 'relic']
    if relics:
        out.append('  relics gained: ' + '; '.join(f"{e.get('who')} {e['x']} (floor {e['depth'] + 1})" for e in relics))
    never = Counter()
    for r in rows:
        if r['t'] == 'census':
            for k in r.get('objects', {}):
                never[k] += 1
    used_types = {e['x'] for e in events if e['k'] == 'use'}
    untouched = sorted(k for k in never if not k.startswith('Breakable') and k not in used_types)
    if untouched:
        out.append('  interactables seen but never used: ' + ', '.join(untouched))
    tips = [r['x'] for r in rows if r['t'] == 'msg' and r['k'] == 'tip']
    if tips:
        out.append(f'  tips shown to the host: {len(tips)}')


def report_client(rows, head, out, flags):
    m = head.get('machine', {})
    out.append(f"client {head.get('me')}: {m.get('gpu')} / {m.get('cpu')} / {m.get('ram')} MB / {m.get('screen')} / quality {m.get('quality')}")
    ticks = [r for r in rows if r['t'] == 'tick' and r.get('fps')]
    if not ticks:
        return
    fps = sum(t['fps'] for t in ticks) / len(ticks)
    rtt = [t['rttMs'] for t in ticks if 'rttMs' in t]
    out.append(f"  fps mean {fps:.0f}, worst frame {max(t['worstMs'] for t in ticks):.0f} ms" + (f", ping mean {sum(rtt) / len(rtt):.0f} ms max {max(rtt)} ms" if rtt else ''))
    if fps < LOW_FPS:
        flags.append(f"client {head.get('me')} averaged {fps:.0f} fps")
    if rtt and sum(rtt) / len(rtt) > HIGH_RTT:
        flags.append(f"client {head.get('me')} ping averaged {sum(rtt) / len(rtt):.0f} ms")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('paths', nargs='*')
    ap.add_argument('--out')
    a = ap.parse_args()
    runs = load(a.paths or [find_default()])
    out, flags = [], []
    for path, rows in runs:
        report_run(path, rows, out, flags)
    out.append('=' * 78)
    out.append('FLAGS (look here first)')
    out += ['  - ' + f for f in flags] or ['  none']
    text = '\n'.join(out)
    if a.out:
        open(a.out, 'w', encoding='utf-8').write(text)
    print(text)


if __name__ == '__main__':
    main()
