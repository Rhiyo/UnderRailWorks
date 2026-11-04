# Game names tools

UnderRail's types and members have scrambled names that change with each game build. Works keeps the readable layer
(`src/Runtime`) tied to one game version; these tools find what changed.

- `GameMap.cs`: recovers what it can of the original names from the game exe (the save code's `"ClassName+_Field"`
  strings, save tags, names the obfuscator kept). `mono GameMap.exe underrail.exe map.tsv` writes one line per type.
  On 1.3.1.2: 8,746 types (8,630 scrambled), 288 named from the save code with 951 of their fields, 3,501 with a save tag.
  The output is generated from the player's game and not committed.
- `Remap.cs`: after a game update, `mono Remap.exe old.exe new.exe build/xna/UnderRailWorks.dll` lists every game type
  and member Works uses with its new name, or UNMATCHED. It matches by clues that survive a rebuild: save tags (old
  saves store them, so the game must keep them), save-code names, kept names, strings, and the shape of the code
  (base types, signatures and field types in terms of types matched so far, repeated until nothing changes), then
  members by kept name, save name, signature and body (opcodes, strings, framework calls).
- `test-remap.sh [seeds]`: scrambles a copy of the game (every scrambled name replaced at random, as a new build
  would) and checks Remap's answers against the scrambler's record. Nov 2026: 324 of 324 right (after the content API), 162 of 162 before. A real
  update also changes code, which this doesn't simulate: changed members come out UNMATCHED (fix by hand), and
  `GameNames.Check` at startup catches anything else.
