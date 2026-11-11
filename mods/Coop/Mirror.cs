// Co-op, step 3: the joiner is in the host's world. When a joiner's character enters (and whenever the host changes
// zone), the host saves the game ("coop-host" in its saves) and sends the save; the joiner's game loads it ("coop-join")
// and freezes it (Works' Simulation.Frozen: nothing there moves or acts by itself). From then on the host sends where
// every character is, which way it faces, its health and whether it's dead (10 times a second, only what changed), and
// the joiner's game puts them there. The joiner's clicks don't move anything locally: a walk or an attack goes to the
// host as GO / ATTACKID, and the joiner's camera follows their own character.
// Characters are matched by the game's own id (kept in the save). Co-op characters are spawned by the mod, so they
// aren't in the save: they go by "c:<name>" and the joiner's game spawns its own copy.
// Messages, host to joiner: WORLD, FILE <path> <base64>..., WORLDEND, YOU <key>,
// POS <key> <x> <y> <z> <facing> <health> <dead>, SPAWN <key> <blueprint> <hostile> <name> (before a character's first POS). Joiner to host: LOADED, ATTACKID <key> (and GO x y).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using UnderRailWorks;

public sealed partial class Coop
{
    const string HostSave = "coop-host", JoinSave = "coop-join";

    // --- Host ---------------------------------------------------------------------------------------------------
    string KeyOf(Character c)
    {
        if (c == null) return null;
        if (seats.Values.Any(s => s.Character == c) || orphans.Any(o => o.Character == c) || c == partner) return "c:" + c.Name;
        return c.EntityId;
    }

    Character Resolve(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        if (key.StartsWith("c:")) { var name = key.Substring(2); return seats.Values.Select(s => s.Character).Concat(orphans.Select(o => o.Character)).FirstOrDefault(c => c != null && c.Name == name); }
        return World.ByEntityId(key);
    }

    // Saves now and sends the save; in combat (the game doesn't save then) as soon as it ends.
    void SendWorld(Seat seat)
    {
        if (seat.Peer == null || seat.Peer.Closed) return;
        seat.InWorld = false; seat.Sent.Clear();
        if (!SaveGames.CanSave) { if (!seat.WorldPending) seat.Peer.Send("SAID", "you'll see the world when the host can save (after this fight)"); seat.WorldPending = true; return; }
        seat.WorldPending = false;
        if (!SaveGames.SaveNow(HostSave)) { seat.Peer.Send("SAID", "the host couldn't save the game to send it"); Log.Warn("couldn't save " + HostSave); return; }
        var folder = SaveGames.Folder(HostSave);
        var files = Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Select(f => new KeyValuePair<string, byte[]>(f.Substring(folder.Length).TrimStart('\\', '/').Replace('\\', '/'), File.ReadAllBytes(f))).ToList();
        Log.Info("sending the world to " + seat.Peer.Name + ": " + files.Count + " files, " + files.Sum(f => f.Value.Length) / 1024 + " KB");
        var peer = seat.Peer;
        // Sent from another thread (it can be megabytes); lines from the game's thread go in between, whole.
        System.Threading.ThreadPool.QueueUserWorkItem(_ =>
        {
            peer.Send("WORLD");
            foreach (var f in files) peer.Send("FILE", f.Key, Convert.ToBase64String(f.Value));
            peer.Send("WORLDEND");
        });
    }

    double mirrorTimer;
    void HostMirror(double dt)
    {
        foreach (var s in seats.Values.Where(s => s.WorldPending && s.Character != null).ToList()) if (SaveGames.CanSave) SendWorld(s);
        if ((mirrorTimer -= dt) > 0) return;
        mirrorTimer = 0.1;
        var watching = seats.Values.Where(s => s.InWorld && s.Peer != null && !s.Peer.Closed).ToList();
        if (watching.Count == 0) return;
        var p = World.Player;
        if (p == null) return;
        var everyone = World.CharactersNear(p.Position, 400);
        var lines = new List<KeyValuePair<string, object[]>>(); var who = new Dictionary<string, Character>();
        foreach (var c in everyone)
        {
            var key = KeyOf(c);
            if (key == null) continue;
            var at = c.Position;
            who[key] = c;
            lines.Add(new KeyValuePair<string, object[]>(key, new object[] { "POS", key, F(at.X), F(at.Y), F(at.Z), c.Facing, (int)Math.Round(c.Health), c.IsDead ? 1 : 0 }));
        }
        foreach (var s in watching)
            foreach (var l in lines)
            {
                var text = string.Join(" ", l.Value.Skip(2).Select(o => Convert.ToString(o, CultureInfo.InvariantCulture)).ToArray());
                string old;
                if (s.Sent.TryGetValue(l.Key, out old) && old == text) continue;
                if (old == null) { var c = who[l.Key]; s.Peer.Send("SPAWN", l.Key, l.Key.StartsWith("c:") ? BlueprintOf(c.Name) : c.Blueprint ?? "", c.IsHostile ? 1 : 0, c.Name); }
                s.Sent[l.Key] = text;
                s.Peer.Send(l.Value);
            }
    }

    string BlueprintOf(string name) { var r = records.FirstOrDefault(x => x.Name == name); return r == null || r.Blueprint == null ? DefaultBlueprint : r.Blueprint; }
    static string F(float v) { return v.ToString("0.###", CultureInfo.InvariantCulture); }

    void HostMirrorMessage(Seat seat, string[] w)
    {
        if (w[0] == "LOADED") { seat.InWorld = true; seat.Sent.Clear(); seat.Peer.Send("YOU", "c:" + (seat.Character == null ? "" : seat.Character.Name)); Log.Info(seat.Peer.Name + " is in the world"); }
    }

    // --- Joiner -------------------------------------------------------------------------------------------------
    bool receiving, loading, inWorld;
    string myKey;
    readonly Dictionary<string, Character> spawned = new Dictionary<string, Character>();
    readonly Dictionary<string, string[]> spawnInfo = new Dictionary<string, string[]>();
    readonly List<string[]> early = new List<string[]>();     // positions that came before the world was loaded

    void MirrorMessage(string[] w)
    {
        switch (w[0])
        {
            case "WORLD":
            {
                receiving = true; inWorld = false; Simulation.Frozen = false;
                var folder = SaveGames.Folder(JoinSave);
                try { if (Directory.Exists(folder)) Directory.Delete(folder, true); Directory.CreateDirectory(folder); } catch (Exception ex) { Log.Error("can't clear " + folder + ": " + ex.Message); }
                Tell("receiving the host's world...");
                return;
            }
            case "FILE":
            {
                if (!receiving || w.Length < 3) return;
                var rel = w[1].Replace('\\', '/');
                if (rel.Length == 0 || rel.Contains("..") || Path.IsPathRooted(rel) || rel.Contains(":")) { Log.Warn("refused file " + rel); return; }
                var path = Path.Combine(SaveGames.Folder(JoinSave), rel.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, Convert.FromBase64String(w[2]));
                return;
            }
            case "WORLDEND":
                receiving = false;
                spawned.Clear(); early.Clear();
                if (SaveGames.Load(JoinSave)) { loading = true; Tell("loading the host's world"); }
                else Tell("couldn't load the host's world (a different game version or DLC?)");
                return;
            case "YOU": myKey = w.Length > 1 ? w[1] : null; FollowMe(); return;
            case "SPAWN": if (w.Length > 2) spawnInfo[w[1]] = w; return;
            case "POS":
                if (!inWorld) { if (loading) early.Add(w); return; }
                Place(w);
                return;
        }
    }

    // POS key x y z facing health dead
    void Place(string[] w)
    {
        if (w.Length < 8) return;
        var key = w[1];
        Character c;
        // Co-op characters, and anyone who appeared in the host's game after the save was sent: spawned here.
        if (spawned.TryGetValue(key, out c) && c.Area == null) c = null;
        if (c == null && !key.StartsWith("c:")) c = World.ByEntityId(key);
        if (c == null) c = SpawnMirror(key, w);
        if (c == null) return;
        float x = P(w[2]), y = P(w[3]), z = P(w[4]);
        int facing; int.TryParse(w[5], out facing);
        var at = c.Position;
        if (Math.Abs(at.X - x) > 0.001f || Math.Abs(at.Y - y) > 0.001f || Math.Abs(at.Z - z) > 0.001f || c.Facing != facing) c.PlaceAt(new Vector3(x, y, z), facing);
        int hp; if (int.TryParse(w[6], out hp) && !c.IsDead && Math.Abs(c.Health - hp) >= 1) c.Health = hp;
        if (w[7] == "1" && !c.IsDead) c.Kill();
        if (key == myKey) FollowMe();
    }

    Character SpawnMirror(string key, string[] pos)
    {
        string[] info;
        spawnInfo.TryGetValue(key, out info);
        bool coop = key.StartsWith("c:");
        var blueprint = info != null && info[2].Length > 0 ? info[2] : coop ? DefaultBlueprint : null;
        if (blueprint == null) return null;     // not found by name: can't be shown
        bool hostile = info != null && info.Length > 3 && info[3] == "1";
        var c = World.Spawn(blueprint, (int)Math.Round(P(pos[2])), (int)Math.Round(P(pos[3])), coop ? SpawnSide.Ally : hostile ? SpawnSide.Hostile : SpawnSide.Neutral);
        if (c == null) { spawnInfo.Remove(key); return null; }     // once: not every POS
        if (info != null && info.Length > 4) c.Name = info[4]; else if (coop) c.Name = key.Substring(2);
        if (coop) c.Faction = "player";
        spawned[key] = c;
        Log.Info("mirror: " + c.Name + " placed");
        return c;
    }

    static float P(string s) { float f; return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out f) ? f : 0; }

    Character followed;
    void FollowMe()
    {
        Character c;
        if (myKey == null || !spawned.TryGetValue(myKey, out c) || c == followed) return;
        followed = c;
        Camera.Follow(c);
    }

    // The host's world is loaded here: frozen, then set from the host.
    void Arrived()
    {
        if (!loading) return;
        loading = false; inWorld = true; followed = null;
        Simulation.Frozen = true;
        foreach (var w in early) Place(w);
        early.Clear();
        host.Send("LOADED");
        Tell("you're in " + (hostName ?? "the host") + "'s world: click to walk, click an enemy to attack");
        Build();
    }

    void LeftWorld()
    {
        receiving = loading = inWorld = false;
        Simulation.Frozen = false;
        myKey = null; followed = null; spawned.Clear(); early.Clear();
    }

    void LoadMirror()
    {
        GameEvents.Update.Add(this, dt => { if (listener != null) HostMirror(dt); });
        GameEvents.ZoneLoaded.Add(this, (zone, first) => { if (loading && host != null) Arrived(); });
        // The joiner's orders go to the host: walking and attacking for now.
        GameEvents.PlayerOrdering.Add(this, o =>
        {
            if (!inWorld || host == null || host.Closed) return;
            o.Cancel = true;
            switch (o.Kind)
            {
                case PlayerOrderKind.Walk: host.Send("GO", o.X, o.Y); break;
                case PlayerOrderKind.Attack:
                {
                    var key = o.Target == null ? null : spawned.Where(kv => kv.Value == o.Target).Select(kv => kv.Key).FirstOrDefault() ?? o.Target.EntityId;
                    if (key != null) host.Send("ATTACKID", key);
                    break;
                }
                default: Tell("in co-op you can walk and attack for now"); break;
            }
        });
    }
}
