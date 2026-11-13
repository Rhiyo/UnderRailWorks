#!/usr/bin/env python3
# Writes docs/API.md: every public type and member of namespace UnderRailWorks (not .Internal), with the // comment
# lines just above each, read straight from src/Runtime/*.cs (the code's comments are the documentation).
import re, os, sys, glob
root = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
types = {}
order = []
for path in sorted(glob.glob(os.path.join(root, 'src', 'Runtime', '*.cs'))):
    lines = open(path, encoding='utf-8').read().split('\n')
    ns = None; current = None; depth = 0; type_depth = None; comment = []
    head = []
    for i, l in enumerate(lines):
        # [Preview("why")] in front of a declaration: kept, labelled (not covered by the API version: docs/Versioning.md)
        preview = None
        pm = re.match(r'^(\s*)\[Preview(?:\("([^"]*)"\))?\]\s*', l)
        if pm:
            preview = pm.group(2) or 'preview'
            l = pm.group(1) + l[pm.end():]
        s = l.strip()
        m = re.match(r'namespace\s+([\w.]+)', s)
        if m: ns = m.group(1)
        if s.startswith('//'):
            comment.append(s[2:].strip()); continue
        if ns != 'UnderRailWorks':
            comment = []; continue
        tm = re.match(r'public\s+(?:static\s+|sealed\s+|abstract\s+|partial\s+)*(class|struct|enum|interface)\s+(\w+(?:<\w+>)?)', s)
        if tm and l.startswith('    public') or (tm and l.startswith('public')):
            name = tm.group(2)
            if name not in types: types[name] = {'kind': tm.group(1), 'doc': [], 'members': [], 'file': os.path.basename(path)}; order.append(name)
            if comment: types[name]['doc'] += comment
            if preview: types[name]['preview'] = preview
            current = name; comment = []
            if tm.group(1) == 'enum':
                body = l[l.find('{')+1:l.rfind('}')] if '{' in l and '}' in l else ''
                if body: types[name]['members'].append(('values', body.strip(), []))
            continue
        mm = re.match(r'public\s+(.+?)(\s*\{|\s*=>|;|\s*$)', s) if (l.startswith('        public') or l.startswith('    public ')) and current else None
        if mm and current and not s.startswith('public sealed class') and not s.startswith('public class'):
            sig = re.sub(r'\s*\{.*$', '', s); sig = re.sub(r'\s*=>.*$', '', sig).rstrip(';').strip()
            sig = sig.replace('public ', '', 1)
            types[current]['members'].append(('member', sig, comment, bool(preview)))
        comment = []
# The compiled API (build/api-now.txt, tools/apicheck/ApiSurface.cs) decides what is listed: a member found in the
# source but not public in the DLL (internal, an override of a game method, a nested private type's) is left out, and
# one the source parser missed (several members on one line) is added from the DLL, without a comment.
surface_path = os.path.join(root, 'build', 'api-now.txt')
if os.path.exists(surface_path):
    def shorten(sig):
        sig = re.sub(r'\b(?:System\.Collections\.Generic|System|Microsoft\.Xna\.Framework(?:\.\w+)?|UnderRailWorks)\.', '', sig)
        for k, v in {'Void': 'void', 'Boolean': 'bool', 'Int32': 'int', 'Int64': 'long', 'Single': 'float', 'Double': 'double', 'String': 'string', 'Object': 'object'}.items():
            sig = re.sub(r'\b' + k + r'\b', v, sig)
        prev = None
        while prev != sig: prev = sig; sig = re.sub(r'Nullable<([\w]+)>', r'\1?', sig)
        return sig
    def member_name(sig):
        sig = re.sub(r'\s*\{.*$', '', sig)
        eq = sig.find(' = ')
        if eq >= 0 and ('(' not in sig or eq < sig.find('(')): sig = sig[:eq]     # a field's initializer, not a default argument
        m = re.search(r'(\w+)\s*(?:<[^()]*>)?\s*\(', sig) or re.search(r'(\w+)\s*$', sig.strip())
        return m.group(1) if m else None
    public = {}
    for line in open(surface_path, encoding='utf-8'):
        if ' :: ' not in line: continue
        tname, rest = line.rstrip('\n').split(' :: ', 1)
        tname = tname.split('.')[-1].split('+')[-1]
        if re.match(r'(static |sealed |abstract )*(class|struct|enum|interface|delegate)\b', rest): continue
        public.setdefault(tname, []).append(rest)
    for name in order:
        t = types[name]
        if t['kind'] == 'enum' or name not in public: continue
        names = set(member_name(r) for r in public[name])
        names.add('ctor')
        t['members'] = [m for m in t['members'] if m[0] == 'values' or (len(m) > 3 and m[3]) or member_name(m[1]) in names or member_name(m[1]) == name or 'operator' in m[1]]
        have = set(member_name(m[1]) for m in t['members'] if m[0] != 'values')
        for r in public[name]:
            n = member_name(r)
            if n and n not in have and not r.startswith('ctor') and not n.startswith('op_'):
                sig = re.sub(r'^(static )?(method|property|field|readonly field|event|static readonly field|const) ', lambda m: (m.group(1) or ''), r)
                sig = re.sub(r'^(static )?readonly field ', r'\1readonly ', sig).replace('field ', '')
                t['members'].append(('member', shorten(sig), [])); have.add(n)
out = ['# UnderRailWorks API reference', '', 'Generated by `tools/apidoc/apidoc.py` from the source comments (namespace `UnderRailWorks`). The guide is UnderRailWorks.md. Parts marked **Preview** aren\'t covered by the API version yet: they may change in any release (docs/Versioning.md).', '',
       '## Interface: which to use', '',
       'Use the game\'s own windows where one fits: they are right at every resolution and interface size. Rules: UI.md.', '',
       '| You want | Use | The game\'s window |',
       '|---|---|---|',
       '| a line of text (a name) | `Ui.Ask` | the save-name popup |',
       '| a number in a range (how many) | `Ui.AskNumber` | the split-stack popup |',
       '| yes or no | `Ui.Confirm` | the message box |',
       '| one of a few answers | `Ui.Choose` | the message box |',
       '| a choice in a conversation | `Dialogs.Add` / `Dialogs.Patch` | the dialog window |',
       '| a short message over a character | `Notify.Over` / `Notify.OverPlayer` | floating text |',
       '| a panel that stays open (a list, a ledger) | `Ui.Window` + `UiRows`, `Ui.List`... | (Works\' own; fixed title bar: check other sizes) |',
       '| a line of text on the HUD | `HudText` | (Works\' own) |',
       '']
for name in sorted(order, key=str.lower):
    t = types[name]
    out.append('## ' + name + '  _(' + t['kind'] + ', ' + t['file'] + ')_' + ('  **Preview** (' + t['preview'] + '): may change in any Works release' if t.get('preview') else ''))
    if t['doc']: out.append(''); out.append(' '.join(t['doc']))
    out.append('')
    for m in t['members']:
        kind, sig, doc = m[0], m[1], m[2]
        if kind == 'values': out.append('Values: `' + sig + '`'); continue
        out.append('- `' + sig + '`' + (' **(preview)**' if len(m) > 3 and m[3] else '') + ((' — ' + ' '.join(doc)) if doc else ''))
    out.append('')
os.makedirs(os.path.join(root, 'docs'), exist_ok=True)
open(os.path.join(root, 'docs', 'API.md'), 'w', encoding='utf-8').write('\n'.join(out))
print('docs/API.md:', len(order), 'types,', sum(len(types[n]['members']) for n in order), 'members')
