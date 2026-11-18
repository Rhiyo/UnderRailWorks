# Writing an UnderRailWorks mod

This guide is the starting point for mod authors. The examples are in [Cookbook.md](Cookbook.md) (one per API area,
compiled and run on every build), every type and member is in [API.md](API.md), and how each part works inside the
game is in UnderRailWorks.md.

## 1. Set up

1. Install UnderRailWorks into the game (the patcher), and the .NET SDK 8 or newer.
2. Copy `sdk/template` (with `sdk/UnderRailWorks.Mod.targets`), rename `MyMod` (folder, `.cs`, `.csproj`, `mod.json`).
3. `dotnet build` builds the mod and copies it to the game's `Mods/MyMod`. Start the game; the main menu's Mods screen
   lists it, with any errors, and switches it on and off without a restart (its settings are in Options -> Mods).
4. `dotnet build -t:PackMod` makes `bin/MyMod-1.0.0.zip` to share.

Set `UnderRailPath` if the game isn't in Steam's default folder. No code at all? A **content pack** is a mod with only
`mod.json` and `content/*.json` (items, recipes, dialogs, quests, placements, triggers, stores, loot...): see
`mods/CampfireCooking` and the "Content packs" section of UnderRailWorks.md.

## 2. The rules

**Use Works' API, not the game's code.** Mods reference `UnderRailWorks.dll` and `FNA.dll`, not `underrail.exe`.
The game's names are scrambled and change with game updates; Works keeps one readable layer over them, checked at
startup, so a game update means fixing Works once instead of every mod breaking. Works also carries the traps you'd
otherwise hit alone (obfuscated property accessors, members that share names, state the game caches). When Works lacks
something that needs the game's code, see 3: an API mod keeps that code in one mod that others build on.

**Saves stay loadable by the stock game.** Never put a mod's own types into the game's save. Keep your data in
`SaveData` (`SaveData.Set("kills", 12)`, JSON next to the save); things you put in the world (characters,
objects, triggers, exits) are placements, re-created on every load and never saved. A player who removes your mod
must still be able to load their game. (A save made *inside* a mod's new zone is the one exception: it needs the mod.)

**Never ship game files or decompiled code.** Clone stock items with `Items.Add`, point at stock art and maps by
path; ship only your own work.

**Interface: any resolution, any interface size.** Read [UI.md](../UI.md). In short: ask the player things with the
game's own popups (`Ui.Ask`, `Ui.AskNumber`, `Ui.Confirm`, `Ui.Choose`); lay text out with `Ui.LineHeight` and
`Ui.TextWidth`; give other sizes in Small's units through `Ui.S(n)`; move things only with `Ui.Move` (or what uses it);
test at Small and at Huge.

**Handlers never throw into the game.** Works catches and reports a failing handler (log, the mod's card in the Mods
tab, one message to the player), but keep per-frame handlers cheap and null-check the player (there is none in the
menus).

## 3. When Works doesn't have what you need

In this order:

1. **Build it from Works' API.** Mods are full C#: most features are compositions of what's there (Workbench Planner,
   Stealth Lens and Survey Pins are).
2. **Ask for it in Works** (an issue, or a pull request to `src/Runtime`). A gap in the API is fixed in Works, so
   every mod gets it and it survives game updates. This is how `Ui.WorldView`, `Camera`, `Stealth.Notice` and many
   others came about.
3. **Publish it from your mod for other mods** when it's a feature others build on (a camera network, a reputation
   system): `Mods.Provide(this, api)`. Others use it without referencing your DLL: they declare a public interface of
   their own with the members they use (same names and types) and call `Mods.Api<IYourApi>("YourMod")` (as SMAPI's
   GetApi): Works makes an object implementing it that forwards to yours, or returns null and logs which member didn't
   fit. Or `Mods.Call("YourMod", "Method", args)` for one call, or events with `Mods.Event(name)` / `Raise`. Declare
   your API in your documentation, and only add to it (new members, new overloads): renaming or removing a member
   breaks the mods that use it. Ask for it in PostLoad or later; after the providing mod is hot-reloaded, ask again (`Mods.Reloaded` says which mod).
4. **Raw game access, done once: an API mod.** When the piece needs Harmony on the game's code, put it in a mod of
   its own that offers it to other mods the way Works offers its API (below), rather than patching inside your
   feature mod. Then only that one mod breaks when the game updates, and every mod that needs the piece shares one
   patch instead of each making its own, clashing ones. Ask for it in Works too (2): an API mod that several mods use
   has proved its shape, and is the best candidate to move into Works.

   Harmony on the game's types (by reflection, or by referencing the game yourself) breaks when the game updates and
   you're on your own: keep it in one class, check every lookup for null and switch the feature off with a log line
   when one is missing, and never touch saves this way. Make your own Harmony instance
   (`new HarmonyLib.Harmony("YourMod")`, with `<UseHarmony>true</UseHarmony>` in the project); the Mods screen notes that
   the mod may break with a game update.

Load order and multiplayer, in `mod.json`: `requires` / `optional` load those mods first (`"OtherMod >= 1.2"` for a
minimum version); `sortAfter` / `sortBefore` only order. Otherwise the order is the player's: the main menu's Mods screen
lists loaded mods in load order with Up / Down (from the next start) (saved as `@order=` in `Mods/mods.ini`; "Automatic order" clears it), never
against requirements. A handler that must run before other mods' uses `HookOrder.First` (Co-op's do), not load order.
Players can save the switched-on mods and their order as profiles (the Mods screen's Profiles button; files in `Mods/profiles/`, the same lines as `mods.ini`), as RimWorld's saved mod lists.
`"multiplayer": "client"` for a mod that only changes this player's screen (a minimap, a HUD layout): co-op players
needn't have it. The default, `"both"`, means every player needs it, same version (Co-op refuses a joiner otherwise,
naming the differences).
**The final say:** when two mods change the same thing, the one lower in the load order wins: for ordinary hooks it
runs last, for replace / "first answer" hooks (`ReplaceHook`, the AI's ChooseTarget and ShouldFlee) it's asked first.

Versions: your `mod.json` says which Works API you built for (`"works": "1.0"`), and Works refuses a mod that uses
something it doesn't have, by name, instead of crashing later. Parts marked `[Preview]` may still change; the Mods screen
notes mods that use them. Details: [Versioning](Versioning.md).

### Making an API mod

As SMAPI's framework mods (SpaceCore, Content Patcher) and tModLoader's library mods. A working pair, built and tested
on every build: `examples/GameText` (the API mod: one Harmony patch on the game's text lookup, offered as a hook) and
`examples/GameTextUser` (a mod built on it without referencing its DLL).

- **Your own id and namespace**, never `UnderRailWorks`: players, the Mods screen and the version checks must be able to
  tell your API from Works'.
- **One public API class**, published in `Load` with `Mods.Provide(this, api)`. Use .NET and Works types in its
  members (`string`, `Character`, `Func<...>`, Works' `Hook<T>`), so other mods can describe it in an interface of their
  own and need no reference to your DLL. (Types of your own make them reference it: that works, but a hot reload of
  your mod then needs theirs reloaded too.)
- **Events as hooks:** `new Hook<T>(this, "YourMod.Something")` (or `ReplaceHook<T>` for "one handler takes the
  step"), exposed as a property. Other mods add handlers with `api.Something.Add(this, handler)`, as with Works' own
  hooks: they're kept in load order (`HookOrder` moves one), a handler that keeps failing is removed with its mod
  named, and a mod's handlers go when it's unloaded or reloaded. You fire it with `Something.Run(this, h => h(...))`
  (or `RunUntil` / `ReplaceHook.Run`); only the mod that made a hook can run it, and nobody can run Works' hooks.
- **Versioning:** your `mod.json` `"version"` is your API's version. Within a major version only add (members,
  overloads, hooks); renaming or removing something is a new major version. Dependents write
  `"requires": ["YourMod >= 1.2"]`: they load after you, and aren't loaded (with the reason shown) without you.
- **Game updates:** check every game lookup when you load. If one is missing, leave the feature off, say so with one
  log line, and keep the API there (with a `Working`-style property), so dependents load and can tell.
- **Hot reload:** when your mod is reloaded, your hooks go with your old code, including the handlers other mods
  added. They ask for your API again on `Mods.Reloaded` (GameTextUser shows how).

A mod built on yours:

```csharp
public interface IGameText { Hook<Func<string, string, string>> Shown { get; } bool Working { get; } }

public override void PostLoad()     // every mod's Load has run: the API is there
{
    var text = Mods.Api<IGameText>("GameText");     // null (and a log line naming the member) if it doesn't fit
    text.Shown.Add(this, (key, value) => key == "ui/MainMenu_Version" ? "Modded | " + value : value);
}
```

## 4. Testing

- Switching your mod off and on from the main menu's Mods screen (or `works.mod off|on <id>` in the console, main
  menu only) runs the same steps as a reload, halved: off runs your `Unload()` and removes what you registered through
  Works, your Harmony patches and your placements; on runs `Load` and `PostLoad`. What the game itself already holds
  (item definitions, feats, quests, status effects, zones, pictures, sounds) stays until a restart, and the Mods screen
  says so. Mods that use your API hear of you being switched on through `Mods.Reloaded`.
- `works.reload` (console, ` key) re-reads pictures, translations and settings.
- Code without restarting: `HotReload=1` in UnderRailWorks.ini, then build your mod into its folder while the game
  runs: within a couple of seconds the new DLL is loaded (the log says "reload: reloaded <id>"). Or by hand:
  `works.reload <mod id>`, `works.reload code` (every changed mod). Works removes what the old code registered through
  it (handlers, patches, commands, triggers, the interface it added, HUD parts, windows...) and runs the new Load and
  PostLoad; in a running game your GameStarted, ZoneLoaded and HudCreated handlers run once to catch up. Your save data
  and settings stay. Override `Unload()` for anything Works doesn't know about (threads, files, game objects you
  changed yourself). Not undone: item definition edits already applied (they apply to definitions loaded later) and a
  feat's type (texts and bonuses do change). Restart when in doubt; switching mods on or off still needs one.
- `works.patches [owner]`: the Harmony patches on the game by owner, or one owner's patched methods.
- `works.dialog <name>` writes an outline of any game conversation (to patch it with `Dialogs.Patch`);
  `works.content` lists item paths, blueprints, stores and dialogs; `works.spawn`, `works.give`, `works.var` help set
  up a scene.
- The log is `UnderRailWorks.log` in the game folder (`Log=1` in UnderRailWorks.ini for everything).
- Before sharing: load a save with your mod, play, save; then remove the mod and load that save in the plain game.
