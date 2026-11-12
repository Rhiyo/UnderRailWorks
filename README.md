# UnderRailWorks

An engine upgrade and mod framework for [UnderRail](https://store.steampowered.com/app/250520/UnderRail/) (Styg).

- **Engine upgrade:** the game runs 64-bit on modern .NET (.NET 10) and FNA, on Windows and under Proton, with display
  fixes (any resolution, interface scale, zoom, frame rate).
- **Mod framework:** mods are .NET DLLs in the game's `Mods` folder. They hook the game through UnderRailWorks' API
  (events, readable names for the game's scrambled classes, settings, an in-game Mods tab) or patch it with
  [Harmony](https://github.com/pardeike/Harmony). Dependencies and load order work like
  [tModLoader](https://github.com/tModLoader/tModLoader)'s.
- **Mods that ship with it** (in `mods/`, each can be switched off in Options -> Mods):
  - UnderRailFixes: Occluded characters (keyhole, fills, outlines).
  - Minimap: the area around you, exits, and the characters you can see in their Tab outline colours.
  - Quest Tracker: your active quests on the HUD.
  - Word Gets Around: nearby characters comment on what you've done.
  - Difficulty Dial: damage taken and dealt, hit chance and AP as settings.
  - After-Action Report: a summary after each fight, and career totals.
  - Contract Board: a contractor at South Gate Station with generated bounties and deliveries.
  - HUD Layout: press F12 and drag the game's HUD parts where you want them.
  - Vendor Directory: F6 lists the zone's merchants with a Trade button (optionally from anywhere in the zone).
  - Fair Trade: what merchants charge and pay, as settings.
  - Loadouts: three gear sets per game; save what you wear, put a set back on in one click or key.
  - Pacing: XP a level costs, XP earned, extra skill/feat/ability points per level.
  - Watchful Eyes: opening someone else's container tells you who would see you steal (or that no one is).
  - The Pit: a pit master at South Gate Station runs waves of beasts that fight only you; each wave pays out.
  - Scanner: point at a creature for its level, health, attitude, resistances per damage type and detection.
  - Field Medic: after a fight, if you're hurt, uses a bandage (or a health hypo when too hurt) from your bag.
  - Merchant Ledger: every merchant's stock and prices written down when you trade; L searches them all.
  - Movable Windows: drag the inventory, notes, crafting, map... by their title bar; click a window to bring it to the front. A window you moved stays open when another opens on its side.
  - Status Board: health, status effects by name with turns left, and AP/MP in combat, on the HUD.
  - Neural Reallocation: a neurotechnician at South Gate Station resets your skills or abilities for charons.

No game code or game files are shipped: the patcher changes the player's own copy, and mods patch it at runtime.
Every file the game writes (saves, config.dat) stays readable by the stock game.

## Status

**In development, not released yet.** [UnderRailFixes 1.x](https://github.com/Rhiyo/UnderRailFixes) is the released,
stable version of the fixes. Works is a separate product (starting at 1.0) that grew out of UnderRailFixes 1.0.8: installs, files
and the API are not compatible with 1.x.

Working today (tested under Proton): the game on .NET 10, the mod loader and Mods tab, mod dependencies, the drawing
API and the UnderRailFixes mod, save compatibility with the stock game (`tools/savecheck`). See `TODO.md` for what's
left and `UnderRailWorks.md` for the design.

## Installing (once released)

1. Close the game, extract the release zip and copy its contents into the UnderRail folder.
2. Windows: run `UnderRailWorks-Patcher.exe`. Linux / Steam Deck: run `./underrailworks-patcher`.
3. Start the game from Steam as usual. Settings: **Options -> Mods** and `UnderRailWorks.ini`; mods go in `Mods`.

The patcher backs up `underrail.exe` as `underrail.exe.backup`, writes the patched game as `UnderRail.Game.exe` and
puts a small .NET 10 launcher in `underrail.exe`'s place (the runtime is in `dotnet`, nothing to install). It replaces
an UnderRailFixes 1.x install and takes over its settings. After a game update, run the patcher again.

**Uninstall:** run the patcher with `--uninstall` (or Steam's "Verify integrity of game files"), then delete
`UnderRailWorks*`, `underrailworks-patcher`, `UnderRailLauncher*`, `FNA.dll`, `NVorbis.dll`, `0Harmony.dll`,
`System.Runtime.Serialization.Formatters.dll`, `UnderrailSteamHost.exe`, `Mono.Cecil.dll`, `README.md`, `LICENSE.txt`,
`THIRD-PARTY-NOTICES.txt`, `linux`, `x64` and, if you don't want them any more, `Mods` and `UnderRailWorks.ini`.

## For modders

Start with `docs/ModdingGuide.md` (setup, the rules, what to do when Works lacks something), then `docs/Cookbook.md` (an example per API area, compiled and run on every build). The API reference is `docs/API.md` (generated from the code's comments on every build); the guide with how each part
works in the game is UnderRailWorks.md. The quickest start: `sdk/template` (copy it, rename MyMod, `dotnet build`: the mod is built and copied into the
game's Mods folder; `dotnet build -t:PackMod` makes a release zip). It needs the .NET SDK and UnderRailWorks
installed in the game (`UnderRailPath` if it isn't Steam's default folder).

A mod is a folder in `Mods\` with a `mod.json` and a DLL whose main class derives from `WorksMod`:

```json
{ "id": "MyMod", "name": "My Mod", "version": "1.0.0", "author": "me", "works": "0.9",
  "description": "What it does.", "requires": ["UnderRailFixes@2.0"] }
```

The API is in the `UnderRailWorks` namespace and is a **preview (0.9)**: a mod declares the API version it was built
for, and until 1.0 a new minor version may change what mods use (they must be rebuilt). `UnderRailWorks.Internal` is
Works' own plumbing, not API: don't use it.

```csharp
using UnderRailWorks;

public sealed class MyConfig : ModConfig            // settings: Works builds the Mods tab rows and settings.ini
{
    [Header("Visuals")]
    [Label("Show the effect")] public bool ShowEffect = true;
    [Label("Strength"), Range(0, 100), Increment(5)] public int Strength = 75;
}

public sealed class MyMod : WorksMod
{
    public override void Load()
    {
        var config = GetConfig<MyConfig>();
        WorldDraw.LayerStarting.Add(this, (view, context, layer, shaderPass) => { /* every world layer, in order */ });
        UiEvents.HudCreated.Add(this, hud => Ui.Add(hud, Ui.Button("Hello", () => Log.Info("clicked")), 20, 200));
    }
}
```

A character, a conversation and a quest (the whole of `examples/TheStranger`, built without the game):

```csharp
Quests.Define(this, "stranger_debt", "South Gate Station", "A stranger says I owe him 10 charons.", xp: 75);
Dialogs.Define(this, "stranger/talk", d =>
{
    d.Line("start", "You owe me 10 charons.")
        .Option("Here.", "paid", Condition.Code(() => World.Player.Inventory.Money >= 10),
                Outcome.Code(() => World.Player.Inventory.TakeMoney(10)), Outcome.CompleteQuest("stranger_debt"))
        .Option("Later.", Dialog.End, Outcome.StartQuest("stranger_debt"));
    d.Line("paid", "Pleasure.").Next(Dialog.End);
});
World.Place(new Placement { Id = "stranger", Zone = "LU-SouthGateStation-N", Area = "LU-SouthGateStation-N",
    X = 14, Y = 21, Blueprint = "creatures\\generichumans\\foundryworker", Name = "Stranger", Dialog = "stranger/talk" });
```

Pictures and sounds: `Assets.Texture` / `Sound`, replacing any game texture (`Assets.Replace`, or with no code a
`textures/` folder mirroring the game's names: portraits, character sprites, icons, GUI), `Icons.SetItemIcon`,
`MainMenu.SetBackground`; `Assets.Export` writes a game texture to PNG to start from.

Saves stay loadable by the stock game: placed characters are never saved, mod quests and mod state are kept in plain
variables. `examples/` has small mods; `mods/UnderRailFixes` is a full one. Docs and a mod template are on the TODO list.

## Building

Linux, with Mono (`mcs`, `mkbundle`), Mono.Cecil 0.11, the .NET 10 SDK (`DOTNET`, default `~/.dotnet10/dotnet`), a
Steam copy of UnderRail and any Proton. `./build.sh` builds `dist/UnderRailWorks` (fetching FNA, Harmony and the Windows
.NET runtime with pinned hashes); `tools/check-release.sh` checks the patchers and `tools/savecheck/check-saves.sh
--play` save compatibility with the stock game; `./release.sh X.Y.Z` does all of it and publishes. See `AGENTS.md`.

## Licence

MIT (`LICENSE`). Third-party notices for what the release bundles are in `src/Release`.
