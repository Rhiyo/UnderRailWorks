# MkDocs hook for the documentation site (mkdocs.yml, .github/workflows/docs.yml).
# At build time, in memory (nothing is written into docs/ or committed):
# - docs/API.md is split into one page per type (api/<Type>.md) plus api/index.md (the "which to use" table and
#   every type grouped by first letter); the API reference nav section lists them all. API.md itself isn't published.
# - UI.md (repo root) is published as UI.md.
# - Links to API.md and ../UI.md in the other pages are pointed at those pages.
import os
import re

from mkdocs.structure.files import File

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
HEADING = re.compile(r'^## (.+?)(?:  _\((\w+), ([\w.]+)\)_)?(  \*\*Preview\*\*.*)?$')

_pages = {}  # src_uri -> markdown
_types = []  # (name, kind, file, preview, summary, src_uri)


def _file_name(name):
    return re.sub(r'[^A-Za-z0-9_]+', '-', name).strip('-')


def _html(name):
    return name.replace('<', '&lt;').replace('>', '&gt;')


def _summary(text):
    text = ' '.join(text.split())
    m = re.match(r'(.+?[.:;])(\s|$)', text)
    s = m.group(1) if m else text
    return (s[:157] + '...') if len(s) > 160 else s


def _split_api():
    _pages.clear()
    _types.clear()
    lines = open(os.path.join(ROOT, 'docs', 'API.md'), encoding='utf-8').read().split('\n')
    intro, sections, cur = [], [], None
    for line in lines:
        m = HEADING.match(line)
        if m:
            cur = [m, []]
            sections.append(cur)
        elif cur is None:
            if not line.startswith('# '):
                intro.append(line)
        else:
            cur[1].append(line)
    extra = []  # sections that aren't types ("Interface: which to use") go on the index
    for m, body in sections:
        name, kind, src, preview = m.group(1), m.group(2), m.group(3), m.group(4)
        if not kind:
            extra += ['## ' + name] + body
            continue
        uri = 'api/' + _file_name(name) + '.md'
        doc = next((l for l in body if l.strip() and not l.startswith('- ') and not l.startswith('Values:')), '')
        _types.append((name, kind, src, bool(preview), _summary(doc), uri))
        head = ['# ' + _html(name), '', '_' + kind + ', `' + src + '`_' + (preview or '')]
        page = head + body + ['', '[All types](index.md)', '']
        _pages[uri] = '\n'.join(page)
    intro_text = '\n'.join(intro).strip().replace('docs/Versioning.md', '[Versioning](../Versioning.md)')
    out = ['# API reference', '', intro_text, ''] + extra + ['## All types', '']
    letters = sorted(set(n[0].upper() for n, *_ in _types))
    out.append(' '.join('[' + l + '](#' + l.lower() + ')' for l in letters))
    out.append('')
    for l in letters:
        out += ['### ' + l, '', '| Type | Kind | Source | |', '|---|---|---|---|']
        for name, kind, src, preview, summary, uri in sorted(_types, key=lambda t: t[0].lower()):
            if name[0].upper() != l:
                continue
            note = ('**Preview** ' if preview else '') + summary.replace('|', '\\|')
            out.append('| [' + _html(name) + '](' + uri[4:] + ') | ' + kind
                       + ' | `' + src + '` | ' + note + ' |')
        out.append('')
    _pages['api/index.md'] = '\n'.join(out)
    _pages['UI.md'] = open(os.path.join(ROOT, 'UI.md'), encoding='utf-8').read()


def on_config(config, **kwargs):
    _split_api()
    nav = config['nav'] or []
    for i, item in enumerate(nav):
        if isinstance(item, dict) and list(item.values()) == ['api/index.md']:
            title = list(item.keys())[0]
            children = ['api/index.md'] + [{n: uri} for n, _, _, _, _, uri in sorted(_types, key=lambda t: t[0].lower())]
            nav[i] = {title: children}
    return config


def on_files(files, config, **kwargs):
    api = files.get_file_from_path('API.md')
    if api:
        files.remove(api)
    for uri, content in _pages.items():
        files.append(File.generated(config, uri, content=content))
    return files


def on_page_markdown(markdown, page, config, files, **kwargs):
    markdown = re.sub(r'\]\(API\.md(#[^)]*)?\)', lambda m: '](api/index.md' + (m.group(1) or '') + ')', markdown)
    return markdown.replace('](../UI.md)', '](UI.md)')
