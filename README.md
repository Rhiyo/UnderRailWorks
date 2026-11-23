# UnderRailWorks

An engine upgrade and mod framework for [UnderRail](https://store.steampowered.com/app/250520/UnderRail/) (Styg).
Install it once; then add the mods you want, each one a folder in the game's `Mods` folder.

> **Version 0.0.1: the first release.** Tested mainly on Linux (Steam / Proton). Windows and the GOG version haven't
> been tried on real systems yet. Saves always stay loadable by the unmodded game (checked before every release), so
> you can uninstall at any time.

## What it does

### The engine

- **64-bit on modern .NET.** The game runs as a 64-bit program on .NET 10 (bundled: nothing to install) with
  [FNA](https://fna-xna.github.io/) instead of the 32-bit .NET Framework and XNA it was built for. Music, Steam and
  saving go through Works' replacements for the 32-bit parts.
- **Your own game files.** Nothing of the game is shipped: the patcher changes your own copy, keeps the original
  (`underrail.exe.backup`) and can put it back.

### Engine fixes

All switchable in game (**Options -> Works**) or in `UnderRailWorks.ini`; with a group off the game behaves as stock.

- **Any resolution:** the resolution list comes from what your monitor supports (ultrawide, 1440p, 4K, 8K, portrait),
  long lists scroll, and the resolution is kept in Works' own settings, so `config.dat` stays as the stock game wrote it.
- **Zoom that looks the same on every screen:** the world is shown at the size a 1080p screen would show it, with the
  game's own zoom numbers; wider zoom limits (further out than the game allowed) and a choice of starting zoom.
- **Interface scale:** the whole interface scaled up for big screens (crisp whole-number steps chosen automatically,
  or your own percentage).
- **Insets:** keep the HUD and menus in a centred area of a very wide screen while the world still fills it.
- **Menu backgrounds:** shown whole instead of cropped, if you like.
- **Save screenshots on big screens:** the game crops them to the top-left 2048 pixels; Works makes them show what a
  1080p screen would.
- **Frame rate:** no 60 FPS cap, or your own cap (off by default: not tested thoroughly yet).
- **High-DPI screens on Windows:** Windows' display scaling no longer stretches the game.
- **Window icon** under Proton (Wayland), where the window otherwise has none.

### The mod framework

- **Mods are folders** in the game's `Mods` folder: a `mod.json` and a .NET DLL, a JSON content pack, or both.
- **A Mods screen** on the main menu: switch mods on and off, set their order, see their errors.
- **Mod settings** in **Options -> Works**, one section per mod, kept in the mod's own `settings.ini`.
- **Dependencies and load order** as in [tModLoader](https://github.com/tModLoader/tModLoader): a mod can need or work
  with another, and Works sorts them and says what's missing.
- **One API with readable names** for the game's scrambled ones: characters, items, quests, dialogs, stores, combat,
  the interface, drawing, saves and more, all in the `UnderRailWorks` namespace. Works checks each mod against what it
  actually uses before loading it, so a mod that doesn't fit is refused by name instead of crashing the game.
- **Mods' data in saves** without changing the save: a save made with mods still loads in the unmodded game.
- **Harmony** ([pardeike/Harmony](https://github.com/pardeike/Harmony)) is bundled for mods that need something the
  API doesn't have yet.

## Mods

Works comes with **no mods installed**. Each mod is its own download on the
[Releases page](https://github.com/Rhiyo/UnderRailWorks/releases):

| Mod | What it does |
|---|---|
| **Wall Vision** | See and select characters behind walls and scenery: a keyhole, lines, dots, a fill or an outline where they're hidden, set per kind of character (you, hostiles, others). |
| **Co-op** | Play together (2-4 players): the host opens a lobby from the main menu for a new game or a saved one, the others join it with their own characters, and the game starts when everyone is there. Walking, fighting, looting, trading, conversations, travelling as a group, and everyone gets all the XP. Friends join through Steam from Join a Game or their friends list, no addresses needed (GOG Galaxy: built, not yet tested); joining by address (LAN, port forwarding or a VPN) works too. |
| **HUD Layout** | Drag the game's HUD parts and mods' panels where you want them; saved per HUD size. |
| **Movable Windows** | Drag the game's windows (inventory, notes, crafting, map...) by their title bar; a window you moved stays open when another opens beside it. |

More mods live in [`examples/`](examples) as code to learn from (a minimap, a quest tracker, difficulty and pacing
settings, a merchants' ledger, a respec NPC, an arena...). They aren't released and may not be finished.

## Install

1. Close the game. Extract `UnderRailWorks-X.Y.Z.zip` from the
   [Releases page](https://github.com/Rhiyo/UnderRailWorks/releases) and copy what's inside its folder into the
   UnderRail folder (Steam: right-click UnderRail -> Manage -> Browse local files).
2. Run the patcher: **Windows** `UnderRailWorks-Patcher.exe`; **Linux / Steam Deck** `./underrailworks-patcher`.
3. Start the game from Steam as usual. The main menu has a **Mods** button, and **Options -> Works** shows Works' version.

**Adding a mod:** extract its zip into the game's `Mods` folder (you get `Mods/<Mod name>/`), then switch it on from
the main menu's Mods button if it isn't. Updating a mod the same way keeps its settings.

**After a game update** (Steam puts the original `underrail.exe` back): run the patcher again. If the game's code moved
in a way Works doesn't know, the patcher says so and changes nothing.

**Coming from UnderRailFixes 1.x:** install Works over it; it replaces the 1.x patch and takes over its settings.

**Uninstall:** run the patcher with `--uninstall` (or Steam's "Verify integrity of game files"). Then delete
`UnderRailWorks*`, `underrailworks-patcher`, `UnderRailLauncher*`, `FNA.dll`, `NVorbis.dll`, `0Harmony.dll`,
`System.Runtime.Serialization.Formatters.dll`, `UnderrailSteamHost.exe`, `Mono.Cecil.dll`, `README.md`, `LICENSE.txt`,
`THIRD-PARTY-NOTICES.txt`, `linux`, `x64`, `dotnet` and, if you don't want them any more, `Mods` and
`UnderRailWorks.ini`. Your saves are untouched and load in the unmodded game.

<details><summary>What the patcher changes</summary>

It backs up `underrail.exe` as `underrail.exe.backup`, writes the patched game as `UnderRail.Game.exe` and puts a small
.NET 10 launcher in `underrail.exe`'s place, so Steam starts it as usual (the runtime is in `dotnet`). The two
`sfmlnet-*.dll` files get their 32-bit flag cleared (backed up too). Every place it changes is checked first; on an
unknown game version it stops before writing anything.
</details>

## Reporting a problem

1. Set `Log=1` in `UnderRailWorks.ini` (in the game folder) and play until it happens again.
2. In the game, open the console (the ` key) and type `works.health`: it lists the versions, any part of Works that
   didn't start, and mods with problems.
3. [Open an issue](https://github.com/Rhiyo/UnderRailWorks/issues) with `UnderRailWorks.log` (in the game folder) and
   what you did. The Mods screen shows which mod had errors; problems in someone else's mod go to its author.

## For modders

The guides and the API reference are in [`docs/`](docs) (also built as a website, see `mkdocs.yml`):

- [Modding guide](docs/ModdingGuide.md): setup, the rules, what to do when Works lacks something, making an API mod.
- [Cookbook](docs/Cookbook.md): an example per API area, compiled on every build.
- [Drawing](docs/Drawing.md): how a frame of the world is drawn, lighting, effects, and how drawing mods combine.
- [Multiplayer](docs/Multiplayer.md), [Versions and API stability](docs/Versioning.md), [UI rules](UI.md).
- [API reference](docs/API.md): every public type and member, generated from the code.

The quickest start is `sdk/template`: copy it, rename `MyMod`, `dotnet build` (the mod is built and copied into the
game's `Mods` folder; `dotnet build -t:PackMod` makes a release zip). It needs the .NET SDK and Works installed in the
game.

A mod is a folder in `Mods` with a `mod.json` and a DLL whose main class derives from `WorksMod`:

```json
{ "id": "MyMod", "name": "My Mod", "version": "0.1.0", "author": "me", "works": "0.9",
  "description": "What it does.", "requires": ["WallVision@0.0.1"] }
```

```csharp
using UnderRailWorks;

public sealed class MyConfig : ModConfig            // settings: Works builds its section in Options -> Works and settings.ini
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
        UiEvents.HudCreated.Add(this, hud => Ui.Add(hud, Ui.Button("Hello", () => Log.Info("clicked")), 20, 200));
        GameEvents.ZoneLoaded.Add(this, (zone, firstVisit) => { if (firstVisit) Notify.OverPlayer("Welcome to " + zone); });
    }
}
```

A character, a conversation and a quest (the whole of `examples/TheStranger`):

```csharp
Quests.Add(this, "stranger_debt", "South Gate Station", "A stranger says I owe him 10 charons.", xp: 75);
Dialogs.Add(this, "stranger/talk", d =>
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

**The API is a preview (0.9).** A mod says which API version it was built for; until 1.0 a new version may still
change what mods use, and parts marked **[Preview]** may change in any release. Works checks each mod against what it
actually uses, so a mod keeps loading as long as everything it uses is still there. `UnderRailWorks.Internal` is
Works' own plumbing, not API.

## Building and releasing

Linux, with Mono (`mcs`, `mkbundle`), Mono.Cecil 0.11, the .NET 10 SDK (`DOTNET`, default `~/.dotnet10/dotnet`), a
Steam copy of UnderRail and any Proton. See `AGENTS.md` for the details.

- `./build.sh` builds Works into `dist/UnderRailWorks`, the released mods (`mods/`) into `dist/mods/<name>`, and the
  examples and test mods into `build/Mods`. `./build.sh install` also installs Works and the released mods into the
  Steam copy.
- `./release.sh X.Y.Z -F notes.md` releases Works (tests, packages, tags `vX.Y.Z`, publishes).
- `./release-mod.sh <Mod> [X.Y.Z] -F notes.md` releases one mod from `mods/<Mod>` (its own tests, tag
  `<Mod>-vX.Y.Z`, its own release). `DRY_RUN=1` builds the zip and shows the notes without publishing.

## Licence

MIT (`LICENSE`). Third-party notices for what the release bundles are in `src/Release` and in each release's
`THIRD-PARTY-NOTICES.txt`.
