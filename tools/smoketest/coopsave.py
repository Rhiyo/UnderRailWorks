#!/usr/bin/env python3
# coopsave.py <save folder> <character name> [character.char]: makes a save a co-op game's save for tests, as Co-op
# itself records one: a co-op character in the save's UnderRailWorks.json (mods -> Coop -> characters), with its sheet
# (the .char, base64) if given. Only ever used on test copies of saves.
import base64, json, os, sys
folder, name = sys.argv[1], sys.argv[2]
sheet = base64.b64encode(open(sys.argv[3], 'rb').read()).decode() if len(sys.argv) > 3 else None
path = os.path.join(folder, "UnderRailWorks.json")
data = json.load(open(path, encoding='utf-8-sig')) if os.path.exists(path) else {"format": 1.0, "mods": {}}
coop = data.setdefault("mods", {}).setdefault("Coop", {})
chars = [c for c in coop.get("characters", []) if c.get("name") != name]
chars.append({"name": name, "blueprint": "creatures\\generichumans\\jkkgunslinger", "zone": None, "x": 0, "y": 0, "sheet": sheet})
coop["characters"] = chars
json.dump(data, open(path, "w", encoding='utf-8'))
print("co-op save: " + folder + " with " + ", ".join(c["name"] for c in chars))
