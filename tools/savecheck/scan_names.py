# Modern .NET names in save files (files or folders given): none may be there for the stock game. ("FNA" in chunk0.dat
# is the library record the stock game also writes, as WineMono.FNA under Proton; its reader ignores it.)
import os, sys, zlib, collections
bad = collections.Counter(); files = 0
for arg in sys.argv[1:]:
    paths = [os.path.join(arg, f) for f in os.listdir(arg)] if os.path.isdir(arg) else [arg]
    for p in paths:
        if not p.endswith('.dat'): continue
        b = open(p, 'rb').read(); files += 1
        i = b.find(b'\x1f\x8b\x08')
        data = zlib.decompressobj(47).decompress(b[i:]) if i >= 0 else b
        for pat in (b'System.Private.CoreLib', b'StringEqualityComparer', b'NonRandomized', b'Version=10.0.0.0', b'Version=9.0.0.0', b'Version=8.0.0.0'):
            if pat in data: bad[(p, pat.decode())] += data.count(pat)
print(files, "files scanned:", "MODERN NAMES " + str(dict(bad)) if bad else "no modern names")
sys.exit(1 if bad else 0)
