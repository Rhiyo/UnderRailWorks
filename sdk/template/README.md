# Mod template

1. Install UnderRailWorks into the game. Install the .NET SDK (8 or newer).
2. Copy this folder (and `../UnderRailWorks.Mod.targets`, or fix the `Import` path), rename `MyMod`.
3. `dotnet build` builds the mod and copies it to the game's `Mods/MyMod`. Start the game.
4. `dotnet build -t:PackMod` makes `bin/MyMod-1.0.0.zip` to share.

Set `UnderRailPath` if the game isn't in Steam's default folder. The API is in the `UnderRailWorks` namespace;
start with docs/ModdingGuide.md (the rules, and what to do when Works lacks something), then docs/Cookbook.md;
the examples are in `examples/` and `mods/`.
