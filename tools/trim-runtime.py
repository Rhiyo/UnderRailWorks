#!/usr/bin/env python3
# trim-runtime.py <dotnet folder>: removes WPF from the bundled Windows .NET runtime (Microsoft.WindowsDesktop.App).
# The game (UnderRail.Game.exe), Works and Harmony use WinForms (System.Windows.Forms, System.Design, System.Drawing)
# but nothing of WPF: measured Nov 2026 by following every assembly reference from them through both frameworks
# (Mono.Cecil): about 49 MB of WindowsDesktop.App is never reached, nearly all of it WPF. Only the WPF family and its
# native libraries go; smaller unreached pieces WinForms might still load by name at run time stay (resource
# extensions, designer editors, VisualBasic, crypto, directory services). The framework's .deps.json lists every file
# and the host checks them, so the removed ones are taken out of it too.
import json, os, re, sys, glob

WPF = re.compile(r'^(Presentation(Core|Framework.*|UI)|ReachFramework|System\.Printing|System\.Windows\.Controls\.Ribbon|'
                 r'System\.Windows\.Input\.Manipulations|System\.Windows\.Presentation|System\.Xaml|UIAutomation.*|WindowsBase|'
                 r'WindowsFormsIntegration|DirectWriteForwarder|wpfgfx_cor3|PenImc_cor3|PresentationNative_cor3|'
                 r'D3DCompiler_47_cor3|vcruntime140_cor3)(\.resources)?\.dll$', re.I)

def main(dotnet):
    roots = glob.glob(os.path.join(dotnet, 'shared', 'Microsoft.WindowsDesktop.App', '*'))
    if not roots: sys.exit('no Microsoft.WindowsDesktop.App in ' + dotnet)
    root = roots[0]
    removed, size = set(), 0
    for dirpath, _, files in os.walk(root):
        for f in files:
            if WPF.match(f):
                p = os.path.join(dirpath, f)
                size += os.path.getsize(p); os.remove(p)
                removed.add(os.path.relpath(p, root).replace(os.sep, '/'))
    deps_path = glob.glob(os.path.join(root, '*.deps.json'))[0]
    deps = json.load(open(deps_path, encoding='utf-8-sig'))
    for target in deps.get('targets', {}).values():
        for lib in target.values():
            for kind in ('runtime', 'native', 'resources'):
                entries = lib.get(kind)
                if entries:
                    for k in [k for k in entries if k.replace('\\', '/') in removed]:
                        del entries[k]
    json.dump(deps, open(deps_path, 'w', encoding='utf-8'), indent=2)
    print('runtime: removed %d WPF files (%.1f MB)' % (len(removed), size / 1048576.0))

main(sys.argv[1])
