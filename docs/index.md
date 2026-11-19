# UnderRailWorks for modders

UnderRailWorks is an engine upgrade and mod framework for [UnderRail](https://store.steampowered.com/app/250520/UnderRail/)
(Styg). It runs the game 64-bit on .NET 10 and FNA, and loads mods: .NET DLLs (or content packs with no code) in the
game's `Mods` folder, switched on and off from the main menu's **Mods** screen, with their settings in
**Options -> Works**.

Mods use Works' API (namespace `UnderRailWorks`): events, readable names for the game's scrambled classes, settings,
the game's own popups and windows, drawing in the world, content (items, dialogs, quests, placements), and saves that
stay loadable by the stock game. Dependencies and load order work like tModLoader's. The API is a **preview (0.9)**:
see [Versions & API stability](Versioning.md).

## Start here

1. Install UnderRailWorks into the game (the patcher) and the .NET SDK 8 or newer.
2. Copy the mod template, [`sdk/template`](https://github.com/Rhiyo/UnderRailWorks/tree/main/sdk/template), together
   with `sdk/UnderRailWorks.Mod.targets`, and rename `MyMod`.
3. `dotnet build` builds the mod and copies it into the game's `Mods/MyMod`; `dotnet build -t:PackMod` makes a zip to
   share. Set `UnderRailPath` if the game isn't in Steam's default folder.

Then read, in this order:

- [Modding guide](ModdingGuide.md): setup, the rules every mod keeps, what to do when Works lacks something, and
  making an API mod that other mods build on.
- [Cookbook](Cookbook.md): an example per API area, compiled on every build.
- [Drawing](Drawing.md): how a frame is drawn, lighting and effects, and how mods share the world's drawing.
- [UI rules](UI.md): anything the player sees must work at every resolution and interface size.
- [Multiplayer](Multiplayer.md) and [Versions & API stability](Versioning.md).
- [API reference](api/index.md): every public type and member, generated from the code's comments.

Small example mods are in [`examples/`](https://github.com/Rhiyo/UnderRailWorks/tree/main/examples), full ones (Wall
Vision, Minimap, Quest Tracker...) in [`mods/`](https://github.com/Rhiyo/UnderRailWorks/tree/main/mods).
