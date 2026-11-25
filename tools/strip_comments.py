#!/usr/bin/env python3
"""Remove every comment from C# files, leaving strings, chars and code untouched.

    python tools/strip_comments.py Assets/Scripts/UI/Codex.cs [more.cs ...]

Handles // and /* */ comments, regular, verbatim (@"..."), interpolated ($"..." and $@"...") and raw (\"\"\"...\"\"\") strings,
and char literals. Lines left empty by a removed comment are dropped; runs of blank lines collapse to one.
"""
import sys


def strip(src):
    out = []
    i, n = 0, len(src)
    while i < n:
        c = src[i]
        nxt = src[i + 1] if i + 1 < n else ''
        if c == '/' and nxt == '/':
            while i < n and src[i] != '\n':
                i += 1
            continue
        if c == '/' and nxt == '*':
            end = src.find('*/', i + 2)
            i = n if end < 0 else end + 2
            continue
        if src.startswith('"""', i):
            end = src.find('"""', i + 3)
            end = n if end < 0 else end + 3
            out.append(src[i:end])
            i = end
            continue
        if c in '@$' or c == '"':
            j = i
            verbatim = False
            while j < n and src[j] in '@$':
                verbatim = verbatim or src[j] == '@'
                j += 1
            if j < n and src[j] == '"':
                interp = '$' in src[i:j]
                k = j + 1
                depth = 0
                while k < n:
                    ch = src[k]
                    if interp and ch == '{':
                        if k + 1 < n and src[k + 1] == '{':
                            k += 2
                            continue
                        depth += 1
                    elif interp and ch == '}' and depth > 0:
                        depth -= 1
                    elif depth == 0:
                        if verbatim:
                            if ch == '"':
                                if k + 1 < n and src[k + 1] == '"':
                                    k += 2
                                    continue
                                break
                        else:
                            if ch == '\\':
                                k += 2
                                continue
                            if ch == '"':
                                break
                    elif ch == '"' and depth > 0:
                        q = k + 1
                        while q < n and src[q] != '"':
                            q += 2 if src[q] == '\\' else 1
                        k = q
                    k += 1
                out.append(src[i:k + 1])
                i = k + 1
                continue
        if c == "'":
            k = i + 1
            while k < n and src[k] != "'":
                k += 2 if src[k] == '\\' else 1
            out.append(src[i:k + 1])
            i = k + 1
            continue
        out.append(c)
        i += 1
    text = ''.join(out)
    lines = []
    original = src.split('\n')
    stripped = text.split('\n')
    for line in stripped:
        lines.append(line.rstrip())
    result, blank = [], 0
    for line in lines:
        if line.strip() == '':
            blank += 1
            if blank > 1:
                continue
        else:
            blank = 0
        result.append(line)
    cleaned = []
    for idx, line in enumerate(result):
        if line.strip() == '' and idx + 1 < len(result) and result[idx + 1].strip() in ('}', ''):
            continue
        if line.strip() == '' and cleaned and cleaned[-1].strip().endswith('{'):
            continue
        cleaned.append(line)
    return '\n'.join(cleaned).strip('\n') + '\n'


if __name__ == '__main__':
    for path in sys.argv[1:]:
        with open(path, encoding='utf-8-sig') as f:
            src = f.read().replace('\r\n', '\n')
        with open(path, 'w', encoding='utf-8', newline='\n') as f:
            f.write(strip(src))
        print('stripped', path)
