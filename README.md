# UnderRailWorks

An engine upgrade and mod framework for [UnderRail](https://store.steampowered.com/app/250520/UnderRail/) (Styg).

- **Engine upgrade:** the game runs 64-bit on modern .NET (.NET 10) and FNA, on Windows and under Proton, with display
  fixes (any resolution, interface scale, zoom, frame rate).
- **Mod framework:** mods are .NET DLLs in the game's `Mods` folder. They hook the game through UnderRailWorks' API
  (events, readable names for the game's scrambled classes, settings, an in-game Mods tab) or patch it with
  [Harmony](https://github.com/pardeike/Harmony). Dependencies and load order work like
  [tModLoader](https://github.com/tModLoader/tModLoader)'s.
- **UnderRailFixes** (Occluded characters: keyhole, fills, outlines) is the first mod, in `mods/UnderRailFixes`.

No game code or game files are shipped: the patcher changes the player's own copy, and mods patch it at runtime.
Every file the game writes (saves, config.dat) stays readable by the stock game.

## Status

**In development, not released yet.** [UnderRailFixes 1.x](https://github.com/Rhiyo/UnderRailFixes) is the released,
stable version of the fixes. Works started from UnderRailFixes 1.0.8 and is a new major version (2.x): installs, files
and the API are not compatible with 1.x.

Working today (tested under Proton): the game on .NET 10, the mod loader and Mods tab, mod dependencies, the drawing
API and the UnderRailFixes mod, save compatibility with the stock game (`tools/savecheck`). See `TODO.md` for what's
left and `UnderRailWorks.md` for the design.

## For modders

A mod is a folder in `Mods\` with a `mod.json` and a DLL whose main class derives from `WorksMod`:

```json
{ "id": "MyMod", "name": "My Mod", "version": "1.0.0", "author": "me", "works": "2.0",
  "description": "What it does.", "requires": ["UnderRailFixes@2.0"] }
```

```csharp
public sealed class MyMod : WorksMod
{
    public override void Load()
    {
        WorldDraw.LayerStarting.Add(this, (view, context, layer, shaderPass) => { /* every world layer, in order */ });
        ModOptions.Register(Id, page => page.Toggle("Enabled", "Enable my mod", true));
    }
}
```

`examples/` has small mods; `mods/UnderRailFixes` is a full one. Docs and a mod template are on the TODO list.

## Building

Linux, with Mono (`mcs`, `mkbundle`), Mono.Cecil 0.11, the .NET 10 SDK, a Steam copy of UnderRail and any Proton.
See `AGENTS.md`.

## Licence

MIT (`LICENSE`). Third-party notices for what the release bundles are in `src/Release`.
