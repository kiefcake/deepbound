#!/usr/bin/env python3
"""The regression run: source audit, then the headless host + client smoke test, judged strictly.

    python tools/run_tests.py                       # against Builds/Deepbound
    python tools/run_tests.py --build Builds/DeepboundNext

A check that quietly did not run is a failure too: REQUIRED names every check that must appear in every run. Checks
that depend on what the floor's seed rolled are in SEED_DEPENDENT and are reported as skipped instead.
"""
import argparse, os, re, subprocess, sys, time

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ap = argparse.ArgumentParser()
ap.add_argument('--build', default=os.path.join(ROOT, 'Builds', 'Deepbound'))
ap.add_argument('--port', default='7791')
args = ap.parse_args()
build = os.path.abspath(args.build)

REQUIRED = [
    'fire logs',
    'ui scale', 'ui space', 'options lists', 'every sound recorded', 'party numbers', 'trap numbers', 'minimap layout',
    'pad Knight', 'pad Mage', 'pad Cleric', 'pad Rogue', 'pad Ranger', 'pad Flagellant', 'pad PlagueDoctor',
    'bot hands Knight', 'bot hands Mage', 'bot hands Cleric', 'bot hands Rogue', 'bot hands Ranger', 'bot hands Flagellant', 'bot hands PlagueDoctor',
    'cleared floor picks you up', 'trap beat', 'ranger snare', 'spike plate', 'tidal wave', 'party rescale', 'tavern mead', 'tavern band', 'tavern songs', 'emote wheel', 'give wheel', 'panoply armour', 'enemy sizes', 'pet fetch', 'crafting', 'crafted tools', 'legacy boons', 'heirloom',
]
SEED_DEPENDENT = ['ambush', 'ambush gates lift']
MIN_PASSES = 195

fails = []
if subprocess.call([sys.executable, os.path.join(ROOT, 'tools', 'audit.py')]) != 0:
    fails.append('source audit')

exe = os.path.join(build, 'Deepbound.exe')
host_log, client_log = os.path.join(build, 'host.log'), os.path.join(build, 'client.log')
common = ['-batchmode', '-nographics', '-port', args.port]
host = subprocess.Popen([exe] + common + ['-host', '-smoketest', '40', '-logFile', host_log])
time.sleep(4)
client = subprocess.Popen([exe] + common + ['-client', '-smoketest', '34', '-logFile', client_log])
try:
    host.wait(timeout=900)
    client.wait(timeout=60)
except subprocess.TimeoutExpired:
    fails.append('the smoke test did not finish in time')
    for p in (host, client):
        if p.poll() is None:
            p.terminate()

host_text = open(host_log, encoding='utf-8', errors='replace').read()
client_text = open(client_log, encoding='utf-8', errors='replace').read()
passed = re.findall(r'\[Smoke\] PASS ([^:]+):', host_text)
for who, text in (('host', host_text), ('client', client_text)):
    for line in re.findall(r'\[Smoke\] FAIL .*', text):
        fails.append(f'{who}: {line[13:300]}')
    n = len(re.findall(r'^\w*Exception', text, re.M))
    if n:
        fails.append(f'{who}: {n} exception(s) in the log')
    if '[Sfx] no recording' in text:
        fails.append(f'{who}: a sound had no recording')
for name in REQUIRED:
    if name not in passed and f'FAIL {name}:' not in host_text:
        fails.append(f'check never ran: {name}')
if len(passed) < MIN_PASSES:
    fails.append(f'only {len(passed)} host checks passed (expected at least {MIN_PASSES}): the run stopped early?')
skipped = [n for n in SEED_DEPENDENT if n not in passed and f'FAIL {n}:' not in host_text]

for f in fails:
    print('FAIL ' + f)
if skipped:
    print('skipped by this seed: ' + ', '.join(skipped))
client_passes = len(re.findall(r'\[Smoke\] PASS', client_text))
print(f'{len(passed)} host + {client_passes} client checks passed, {len(fails)} failure(s)')
sys.exit(1 if fails else 0)
