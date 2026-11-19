"""docs/CREDITS.md -> Assets/Resources/Credits.txt: the title screen's rolling credits (section headings, then
'Work - Author' lines). Run again after editing CREDITS.md."""
import os, re
root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
src = open(os.path.join(root, 'docs', 'CREDITS.md'), encoding='utf-8').read().splitlines()
out = ['#DEEPBOUND', 'A prototype, built with Claude', '', '#Art', 'Every model and animation is made by the game\'s own code (fine detail textures: below)', '']
for line in src:
    if line.startswith('## '):
        title = re.sub(r'\s*\(.*?\)\s*$', '', line[3:]).replace('`', '').strip()
        out += ['', '#' + title]
    elif line.startswith('|') and not line.startswith('|---') and not re.match(r'\|\s*(Pack|Source|Asset|Work|Texture|Track|What)\s*\|', line):
        cells = [c.strip() for c in line.strip('|').split('|')]
        if len(cells) >= 2 and cells[0]:
            out.append(re.sub(r'[`*]', '', cells[0]) + '  -  ' + re.sub(r'[`*]', '', cells[1]))
out += ['', '#Thank you', 'Every sound and texture above is CC0: given away so games like this can exist.', '', 'Thank you for playing.']
os.makedirs(os.path.join(root, 'Assets', 'Resources'), exist_ok=True)
open(os.path.join(root, 'Assets', 'Resources', 'Credits.txt'), 'w', encoding='utf-8').write('\n'.join(out) + '\n')
print(len(out), 'lines')
