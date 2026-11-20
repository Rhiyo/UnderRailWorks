// Co-op: a joiner whose mods differ from the host's isn't just refused. With the refusal the host sends its mods that
// must match (not "multiplayer": "client"), each with its name and homepage. The Co-op window then lists what differs
// and offers "Match host": Works switches the joiner's installed mods to the host's set (Mods.Match: "client" mods stay
// as they are; the joiner's own set is kept as the profile "Before co-op" first), and if nothing is missing Co-op joins
// again at once. Mods missing here (not installed, or another version) are listed with where to get them; mods are
// never sent between players (that would run another player's code). "Restore my mods" (Co-op window, main menu) goes
// back to the player's own set.
// Console: coop.match, coop.restore.
using System;
using System.Collections.Generic;
using System.Linq;
using UnderRailWorks;

public sealed partial class Coop
{
    const string BackupProfile = "Before co-op";
    sealed class HostMod { public string Id, Version, Name, Homepage; }
    List<HostMod> hostMods;     // the last host's mods that must match, sent with a refusal for different mods
    string[] lastJoin;          // coop.join's arguments, to join again after matching

    void HostModsArrived(string[] w)
    {
        hostMods = w.Skip(1).Select(x =>
        {
            var p = x.Split('|'); var idv = p[0]; int at = idv.LastIndexOf('@');
            return new HostMod { Id = at < 0 ? idv : idv.Substring(0, at), Version = at < 0 ? "" : idv.Substring(at + 1), Name = p.Length > 1 && p[1].Length > 0 ? p[1] : idv, Homepage = p.Length > 2 ? p[2] : "" };
        }).ToList();
        Log.Info("the host's mods: " + string.Join(", ", hostMods.Select(m => m.Id + " " + m.Version).ToArray()));
        Changed();
    }

    // What differs, as lines for the window.
    List<string> MismatchLines()
    {
        var lines = new List<string>();
        if (hostMods == null) return lines;
        lines.AddRange(Mods.Differences(hostMods.Select(m => m.Id + "@" + m.Version), Mods.Running(true), "the host has", "you have"));
        foreach (var m in hostMods)
        {
            var here = ModLoader.Find(m.Id);
            if (here != null && (m.Version.Length == 0 || here.VersionText == m.Version)) continue;
            lines.Add(m.Name + " " + m.Version + (here == null ? ": not installed" : ": you have " + here.VersionText) + (string.IsNullOrEmpty(m.Homepage) ? "" : "  -  " + m.Homepage));
        }
        return lines;
    }

    string MatchHost()
    {
        if (hostMods == null) return "no host's mods to match (join a game first)";
        if (World.Player != null) return "mods are switched from the main menu (Escape -> main menu)";
        var r = Mods.Match(hostMods.Select(m => m.Id + "@" + m.Version), BackupProfile);
        var said = new List<string>();
        if (r.SwitchedOn.Count > 0) said.Add("switched on " + string.Join(", ", r.SwitchedOn.ToArray()));
        if (r.SwitchedOff.Count > 0) said.Add("switched off " + string.Join(", ", r.SwitchedOff.ToArray()));
        if (r.Failed.Count > 0) said.Add("couldn't switch " + string.Join("; ", r.Failed.ToArray()));
        if (r.Missing.Count > 0)
            said.Add("still missing: " + string.Join(", ", r.Missing.Select(x =>
            {
                int at = x.LastIndexOf('@'); var m = hostMods.FirstOrDefault(h => h.Id.Equals(at < 0 ? x : x.Substring(0, at), StringComparison.OrdinalIgnoreCase));
                return m == null ? x : m.Name + " " + m.Version + (string.IsNullOrEmpty(m.Homepage) ? "" : " (" + m.Homepage + ")");
            }).ToArray()) + ": install them into the Mods folder, then Match host again");
        var text = said.Count == 0 ? "your mods already match the host's" : string.Join("; ", said.ToArray());
        Tell(text + (r.Matched ? ". Your own mods are kept as the profile \"" + BackupProfile + "\" (Restore my mods)." : ""));
        if (r.Matched && lastJoin != null)
        {
            hostMods = null;
            Tell(Join(lastJoin));
        }
        Changed();
        return r.Matched ? "matched" : "not matched yet";
    }

    string RestoreMine()
    {
        if (!Mods.HasProfile(BackupProfile)) return "nothing to restore (your mods weren't matched to a host's)";
        if (World.Player != null) return "mods are switched from the main menu (Escape -> main menu)";
        var ok = Mods.RestoreProfile(BackupProfile);
        var r = ok ? "your own mods are back" : "some of your mods couldn't switch back (see the Mods screen)";
        Tell(r); Changed();
        return r;
    }

    void LoadMatch()
    {
        Commands.Add(this, "coop.match", "coop.match: switch your mods to the last host's (after a refusal for different mods), then join again", a => MatchHost());
        Commands.Add(this, "coop.restore", "coop.restore: switch back to your own mods after coop.match", a => RestoreMine());
    }
}
