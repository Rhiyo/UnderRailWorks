#!/usr/bin/env python3
# docs/Cookbook.md from tools/cookbook/Cookbook.cs: each "#region Title: text" block becomes a section with its code.
import os, re, textwrap
here = os.path.dirname(os.path.abspath(__file__))
root = os.path.dirname(os.path.dirname(here))
src = open(os.path.join(here, 'Cookbook.cs'), encoding='utf-8').read()
out = ['# UnderRailWorks cookbook', '',
       'Short examples, one per API area. They are compiled on every build (tools/cookbook/Cookbook.cs), so they match',
       'the current API. Each is a method of a `WorksMod`: call it from `Load()`. The full list of types and members is',
       '[API.md](API.md); how each part works in the game is in UnderRailWorks.md.', '']
for m in re.finditer(r'#region ([^\n]*)\n(.*?)#endregion', src, re.S):
    title, body = m.group(1).strip(), m.group(2)
    head, _, text = title.partition(':')
    out.append('## ' + head.strip())
    if text.strip(): out += ['', text.strip()[0].upper() + text.strip()[1:] + '.']
    out += ['', '```csharp', textwrap.dedent(body).strip('\n'), '```', '']
open(os.path.join(root, 'docs', 'Cookbook.md'), 'w', encoding='utf-8').write('\n'.join(out))
print('docs/Cookbook.md:', len(re.findall(r'#region', src)), 'examples')
