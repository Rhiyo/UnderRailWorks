// UnderRailWorks: a mod's main class (as tModLoader's Mod). A mod DLL has one public class deriving from WorksMod;
// Works creates it, fills in Info, Log, Harmony and Settings, and calls:
//   Load()     after the mods it requires: add hook handlers, register settings and options (ModOptions.Register).
//   PostLoad() after every mod's Load (as PostSetupContent): look at other mods (ModLoader.IsLoaded).
// There is no Unload: .NET Framework / Mono can't unload a DLL, so mods are switched on or off for the next start.
// The base class can grow (new virtuals, properties) without breaking mods built against an older Works.
using System;
using HarmonyLib;

public abstract class WorksMod
{
    public ModInfo Info { get; internal set; }
    public string Id { get { return Info.Id; } }
    // Lines in UnderrailFix.log, marked with the mod's id (with Log=1 in UnderrailFix.ini; errors always).
    public ModLog Log { get; internal set; }
    // A Harmony instance named after the mod, for patching the game where Works has no hook yet.
    public Harmony Harmony { get; internal set; }
    // The mod's settings.ini in its folder (defaults: SettingsDefaults, written on first start).
    public ModSettings Settings { get { return ModSettings.For(Id, SettingsDefaults); } }
    protected virtual string SettingsDefaults { get { return null; } }

    public virtual void Load() { }
    public virtual void PostLoad() { }
}

public sealed class ModLog
{
    readonly string id;
    internal ModLog(string id) { this.id = id; }
    public void Info(string line) { global::Log.Write("mods: " + id + ": " + line); }
    public void Warn(string line) { global::Log.Write("mods: " + id + ": warning: " + line); }
    public void Error(string line) { global::Log.Always("mods: " + id + ": error: " + line); }
}

// The Works API version mods are checked against ("works": "1.0" in mod.json): same major version, at least that
// minor. The minor goes up when the API gains something; the major only when something mods use changes or goes.
public static class WorksApi
{
    public static readonly Version Version = new Version(1, 0);
}
