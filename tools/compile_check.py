#!/usr/bin/env python3
import argparse
import glob
import os
import re
import subprocess
import sys
import tempfile

MAIN = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
EDITOR = r'C:\Program Files\Unity\Hub\Editor\6000.3.23f1\Editor\Data'
REFERENCE = re.compile(r'^-r(eference)?:.*[\\/]Assembly-CSharp(\.ref)?\.dll"?$')
SKIPPED_FLAGS = re.compile(r'^[-/](out|refout|doc|additionalfile):')


def main_project():
    if os.path.isdir(os.path.join(MAIN, 'Library', 'Bee')):
        return MAIN
    out = subprocess.run(['git', '-C', MAIN, 'worktree', 'list', '--porcelain'], capture_output=True, text=True).stdout
    for line in out.splitlines():
        if line.startswith('worktree ') and os.path.isdir(os.path.join(line[9:], 'Library', 'Bee')):
            return line[9:]
    sys.exit('compile_check: no checkout with Library/Bee found; run one Unity build first')


def sources(root, editor):
    found = []
    for path in glob.glob(os.path.join(root, 'Assets', '**', '*.cs'), recursive=True):
        rel = os.path.relpath(path, root).replace(os.sep, '/')
        if ('/Editor/' in rel) == editor:
            found.append(path)
    return sorted(found)


def response_file(project, tmp, root, name, editor):
    rsps = glob.glob(os.path.join(project, 'Library', 'Bee', 'artifacts', '*', name + '.rsp'))
    if not rsps:
        sys.exit('compile_check: %s.rsp not found; run one Unity build first' % name)
    lines = []
    with open(max(rsps, key=os.path.getmtime), encoding='utf-8-sig') as f:
        for line in f:
            line = line.rstrip('\r\n')
            if not line:
                continue
            if line.strip('"').endswith('.cs') and not line.startswith(('-', '/')):
                continue
            if SKIPPED_FLAGS.match(line):
                continue
            if editor and REFERENCE.match(line):
                line = '-r:"%s"' % os.path.join(tmp, 'Assembly-CSharp.dll')
            lines.append(line)
    lines.append('-out:"%s"' % os.path.join(tmp, name + '.dll'))
    lines += ['"%s"' % p for p in sources(root, editor)]
    path = os.path.join(tmp, name + '.check.rsp')
    with open(path, 'w', encoding='utf-8') as f:
        f.write('\n'.join(lines) + '\n')
    return path


def compile_assembly(root, project, tmp, name, editor):
    rsp = response_file(project, tmp, root, name, editor)
    dotnet = os.path.join(EDITOR, 'NetCoreRuntime', 'dotnet.exe')
    csc = os.path.join(EDITOR, 'DotNetSdkRoslyn', 'csc.dll')
    result = subprocess.run([dotnet, 'exec', csc, '/nostdlib', '/noconfig', '@' + rsp], cwd=project,
                            capture_output=True, text=True, encoding='utf-8', errors='replace')
    out = (result.stdout + result.stderr).splitlines()
    errors = sorted(set(l for l in out if ' error ' in l))
    warnings = sorted(set(l for l in out if ' warning CS' in l and 'Assets' in l))
    return errors, warnings, result.returncode


def main():
    ap = argparse.ArgumentParser(description='Compile the game scripts without opening Unity (reuses the last build\'s compiler arguments).')
    ap.add_argument('--root', default=None, help='checkout to compile (default: the one holding this script)')
    ap.add_argument('--warnings', action='store_true', help='print every warning')
    args = ap.parse_args()
    root = os.path.abspath(args.root or MAIN)
    project = main_project()
    failed = False
    with tempfile.TemporaryDirectory(prefix='deepbound_csc_') as tmp:
        for name, editor in (('Assembly-CSharp', False), ('Assembly-CSharp-Editor', True)):
            errors, warnings, code = compile_assembly(root, project, tmp, name, editor)
            for line in errors + (warnings if args.warnings else []):
                print(line.replace(root + os.sep, ''))
            print('compile_check: %s: %d error(s), %d warning(s)' % (name, len(errors), len(warnings)))
            failed = failed or bool(errors) or code != 0
            if failed:
                break
    print('compile_check: %s in %s' % ('FAILED' if failed else 'OK', root))
    return 1 if failed else 0


if __name__ == '__main__':
    sys.exit(main())
