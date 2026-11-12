// UnderRailWorks: a mod's main class (as tModLoader's Mod). A mod DLL has one public class deriving from WorksMod;
// Works creates it, fills in Info, Log, Harmony and Settings, and calls:
//   Load()     after the mods it requires: add hook handlers, register settings and options (ModOptions.Register).
//   PostLoad() after every mod's Load (as PostSetupContent): look at other mods (ModLoader.IsLoaded).
//   Unload()   before the mod's code is loaded again (works.reload, HotReload=1): anything Works doesn't know about
//              (threads, files, game objects it changed directly). What it registered through Works goes by itself.
// Switching a mod on or off still applies at the next start.
// The base class can grow (new virtuals, properties) without breaking mods built against an older Works.
using System;
using System.Collections.Generic;
using HarmonyLib;
using UnderRailWorks.Internal;

namespace UnderRailWorks
{

public abstract partial class WorksMod
{ protected WorksMod() { }
    public ModInfo Info { get; internal set; }
    public string Id { get { return Info.Id; } }
    // Lines in UnderRailWorks.log, marked with the mod's id (with Log=1 in UnderRailWorks.ini; errors always).
    public ModLog Log { get; internal set; }
    // A Harmony instance named after the mod, for patching the game where Works has no hook yet.
    internal Harmony Harmony { get; set; }
    // This mod's plain values for the current game, inside the game's own save (its global variables). For anything
    // else (lists, dictionaries, more than a few values) use SaveData.Of(mod), kept next to the save.
    public ModData Data { get { return data ?? (data = new ModData(Id)); } }
    ModData data;
    // The mod's settings.ini as raw keys (defaults: SettingsDefaults, written on first start). GetConfig is the typed
    // way, with the mod's page in Options -> Mods.
    public ModSettings Settings { get { return ModSettings.For(Id, SettingsDefaults); } }
    protected virtual string SettingsDefaults { get { return null; } }

    // The mod's ModConfig class (ModConfig.cs), loaded from settings.ini on first use.
    public T GetConfig<T>() where T : ModConfig { return ModConfig.Get<T>(Info); }

    public virtual void Load() { }
    public virtual void PostLoad() { }
    public virtual void Unload() { }
}

public sealed class ModLog
{
    readonly string id;
    internal ModLog(string id) { this.id = id; }
    public void Info(string line) { UnderRailWorks.Internal.Log.Write("mods: " + id + ": " + line); }
    public void Warn(string line) { UnderRailWorks.Internal.Log.Write("mods: " + id + ": warning: " + line); }
    public void Error(string line) { UnderRailWorks.Internal.Log.Always("mods: " + id + ": error: " + line); }
}

// The Works API version mods are checked against ("works": "0.9" in mod.json). From 1.0: same major version, at least
// that minor (the minor goes up when the API gains something; the major only when something mods use changes or goes).
// While it is 0.x (a preview): the same minor exactly, since a minor may still change what mods use.
public static class WorksApi
{
    // The API's version (docs/Versioning.md): a mod's "works" in mod.json must have the same major and at most this
    // minor. Major: something mods could use was removed or changed. Minor: something was added. 0.x: a preview, each
    // minor may break. build.sh compares the API with api/UnderRailWorks.api.txt (the last release's) and fails on a
    // break this version doesn't account for. Change only these two numbers (the tools read them).
    internal const int Major = 0, Minor = 9;
    public static readonly Version Version = new Version(Major, Minor);

    // The game running (e.g. "1.3.1.2"), and the one this Works was made for. After a game update they differ until
    // Works is updated; most of Works usually still fits, and Unavailable says what doesn't.
    public static string GameVersion { get { try { return dm9.d == null ? "?" : dm9.d.ToString(); } catch (Exception) { return "?"; } } }
    public static string MadeForGameVersion { get { return FixVersion.ForGame; } }
    // Parts of Works that couldn't be set up in this game version (e.g. "party and exits"): their events never fire
    // and their calls do nothing. Empty normally. A mod that needs one can say so instead of misbehaving.
    public static IReadOnlyList<string> Unavailable { get { return UnderRailWorks.Internal.WorksHealth.Failed; } }
}
}
