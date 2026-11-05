// UnderRailWorks: a mod's main class (as tModLoader's Mod). A mod DLL has one public class deriving from WorksMod;
// Works creates it, fills in Info, Log, Harmony and Settings, and calls:
//   Load()     after the mods it requires: add hook handlers, register settings and options (ModOptions.Register).
//   PostLoad() after every mod's Load (as PostSetupContent): look at other mods (ModLoader.IsLoaded).
// There is no Unload: .NET Framework / Mono can't unload a DLL, so mods are switched on or off for the next start.
// The base class can grow (new virtuals, properties) without breaking mods built against an older Works.
using System;
using HarmonyLib;
using UnderRailWorks.Internal;

namespace UnderRailWorks
{

public abstract partial class WorksMod
{
    public ModInfo Info { get; internal set; }
    public string Id { get { return Info.Id; } }
    // Lines in UnderrailFix.log, marked with the mod's id (with Log=1 in UnderrailFix.ini; errors always).
    public ModLog Log { get; internal set; }
    // A Harmony instance named after the mod, for patching the game where Works has no hook yet.
    public Harmony Harmony { get; internal set; }
    // The mod's settings.ini in its folder (defaults: SettingsDefaults, written on first start).
    // This mod's data for the current game (saved with it as plain values).
    public ModData Data { get { return data ?? (data = new ModData(Id)); } }
    ModData data;
    public ModSettings Settings { get { return ModSettings.For(Id, SettingsDefaults); } }
    protected virtual string SettingsDefaults { get { return null; } }

    // The mod's ModConfig class (ModConfig.cs), loaded from settings.ini on first use.
    public T GetConfig<T>() where T : ModConfig { return ModConfig.Get<T>(Info); }

    public virtual void Load() { }
    public virtual void PostLoad() { }
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
    public static readonly Version Version = new Version(0, 9);     // 0.x: a preview; breaking changes possible until 1.0
}
}
