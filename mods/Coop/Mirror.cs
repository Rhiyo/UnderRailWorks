// Co-op, step 3: the joiner is in the host's world. When a joiner's character enters (and whenever the host changes
// zone), the host saves the game ("coop-host" in its saves) and sends the save; the joiner's game loads it ("coop-join")
// and freezes it (Works' Simulation.Frozen: nothing there moves or acts by itself). From then on the host sends where
// every character is, which way it faces, its health and whether it's dead (10 times a second, only what changed), and
// the joiner's game puts them there. The joiner's clicks don't move anything locally: a walk or an attack goes to the
// host as GO / ATTACKID, and the joiner's camera follows their own character.
// Characters are matched by the game's own id (kept in the save). Co-op characters are spawned by the mod, so they
// aren't in the save: they go by "c:<name>" and the joiner's game spawns its own copy.
// Messages, host to joiner: WORLD, FILE <path> <base64>..., WORLDEND, YOU <key>,
// POS <key> <x> <y> <z> <facing> <health> <dead>, WALK <key> <x> <y> (walking there: the joiner's copy walks too),
// DOOR <id> <0|1>, DEAD <key>, GONE <key>, SHEET <key> <.char base64> (a co-op character's sheet: its body is built from
// it, and the joiner's own becomes the player there), SPAWN <key> <blueprint> <hostile> <name> <store> (before a character's first POS).
// Joiner to host: LOADED, ATTACKID <key>, USE <id>, EXIT <x> <y> (and GO x y).
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

    double mirrorTimer, sheetTimer;

    // Each joined character's sheet, sent again when it changed (XP, items, skills...), checked every 5 s out of combat.
    void HostSheets(double dt)
    {
        if ((sheetTimer -= dt) > 0 || Combat.InCombat) return;
        sheetTimer = 5;
        foreach (var seat in seats.Values.Concat(orphans).Where(x => x.Character != null && x.Record != null && x.Record.Sheet != null).ToList())
        {
            // What a change worth a new sheet looks like (the sheet's bytes change all the time: its fog of war, clocks).
            var c = seat.Character;
            var sign = SignOf(c);
            if (seat.LastSign == null) { seat.LastSign = sign; continue; }
            if (sign == seat.LastSign) continue;
            seat.LastSign = sign;
            var sheet = SheetOf(c);
            if (sheet == null) continue;
            seat.Record.Sheet = sheet;
            foreach (var s in seats.Values.Where(x => x.InWorld && x.Peer != null)) s.Peer.Send("SHEET", "c:" + seat.Record.Name, sheet);
        }
    }
    readonly Dictionary<string, Character> known = new Dictionary<string, Character>();     // what each key was, to tell dead from gone
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
        var walks = new Dictionary<string, string>();
        foreach (var c in everyone) { var key = KeyOf(c); var t = c.WalkTarget; if (key != null) walks[key] = t == null ? "" : t.Value.X + " " + t.Value.Y; }
        var doors = World.Doors.ToDictionary(d => d.EntityId, d => d.IsOpen ? "1" : "0");
        foreach (var s in watching)
        {
            foreach (var kv in walks)
            {
                string old;
                if (s.Walks.TryGetValue(kv.Key, out old) && old == kv.Value) continue;
                s.Walks[kv.Key] = kv.Value;
                if (kv.Value.Length > 0 && s.Sent.ContainsKey(kv.Key)) { var xy = kv.Value.Split(' '); s.Peer.Send("WALK", kv.Key, xy[0], xy[1]); }
            }
            foreach (var kv in doors)
            {
                string old;
                if (s.Doors.TryGetValue(kv.Key, out old) && old == kv.Value) continue;
                s.Doors[kv.Key] = kv.Value;
                s.Peer.Send("DOOR", kv.Key, kv.Value);
            }
        }
        // Characters no longer listed: dead (World's lists skip the dead), or gone from the area.
        foreach (var s in watching)
            foreach (var key in s.Sent.Keys.Where(k => !who.ContainsKey(k)).ToList())
            {
                Character was; known.TryGetValue(key, out was);
                s.Peer.Send(was != null && was.IsDead ? "DEAD" : "GONE", key);
                s.Sent.Remove(key); s.Walks.Remove(key);
            }
        foreach (var kv in who) known[kv.Key] = kv.Value;
        foreach (var s in watching)
            foreach (var l in lines)
            {
                var text = string.Join(" ", l.Value.Skip(2).Select(o => Convert.ToString(o, CultureInfo.InvariantCulture)).ToArray());
                string old;
                if (s.Sent.TryGetValue(l.Key, out old) && old == text) continue;
                if (old == null && l.Key.StartsWith("c:")) { var r = records.FirstOrDefault(x => x.Name == l.Key.Substring(2)); if (r != null && r.Sheet != null) s.Peer.Send("SHEET", l.Key, r.Sheet); }
                if (old == null) { var c = who[l.Key]; s.Peer.Send("SPAWN", l.Key, l.Key.StartsWith("c:") ? BlueprintOf(c.Name) : c.Blueprint ?? "", c.IsHostile ? 1 : 0, c.Name, c.StoreName ?? ""); }
                s.Sent[l.Key] = text;
                s.Peer.Send(l.Value);
            }
    }

    string BlueprintOf(string name) { var r = records.FirstOrDefault(x => x.Name == name); return r == null || r.Blueprint == null ? DefaultBlueprint : r.Blueprint; }
    static string F(float v) { return v.ToString("0.###", CultureInfo.InvariantCulture); }

    void HostMirrorMessage(Seat seat, string[] w)
    {
        if (w[0] == "LOADED") { seat.InWorld = true; seat.Sent.Clear(); seat.Walks.Clear(); seat.Doors.Clear(); seat.Peer.Send("YOU", "c:" + (seat.Character == null ? "" : seat.Character.Name)); Log.Info(seat.Peer.Name + " is in the world"); }
    }

    // --- Joiner -------------------------------------------------------------------------------------------------
    bool receiving, loading, inWorld, creating;
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
            case "WALK":
            {
                if (!inWorld || w.Length < 4) return;
                Character c; int x, y;
                if (!spawned.TryGetValue(w[1], out c)) c = w[1].StartsWith("c:") ? null : World.ByEntityId(w[1]);
                if (c != null && int.TryParse(w[2], out x) && int.TryParse(w[3], out y)) c.WalkTo(x, y);
                return;
            }
            case "DOOR":
            {
                if (!inWorld || w.Length < 3) return;
                var d = World.Doors.FirstOrDefault(x => x.EntityId == w[1]);
                if (d != null) d.IsOpen = w[2] == "1";
                return;
            }
            case "DEAD":
            case "GONE":
            {
                if (!inWorld || w.Length < 2) return;
                Character c;
                bool ours = spawned.TryGetValue(w[1], out c);
                if (!ours && !w[1].StartsWith("c:")) c = World.ByEntityId(w[1]);
                if (c == null) return;
                if (w[0] == "DEAD") { if (!c.IsDead) c.Kill(); }
                else if (ours) { World.Despawn(c); spawned.Remove(w[1]); }
                return;
            }
            case "YOU": myKey = w.Length > 1 ? w[1] : null; FollowMe(); return;
            case "SPAWN": if (w.Length > 2) spawnInfo[w[1]] = w; return;
            case "SHEET":
            {
                if (w.Length < 3) return;
                sheets[w[1]] = w[2];
                Character old;
                if (!inWorld || !spawned.TryGetValue(w[1], out old) || old.Area == null) return;
                // A new sheet (XP, items...): a new body from it where the old one stands.
                var at = old.Position;
                var c = SpawnSheet(w[1], (int)Math.Round(at.X), (int)Math.Round(at.Y));
                if (c == null) return;
                c.PlaceAt(at, old.Facing);
                if (w[1] == myKey) { World.MakePlayer(c); followed = c; lastSign = SignOf(c); Build(); }
                World.Despawn(old);
                return;
            }
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
        // Walking here (the host's WALK): its own walk carries it; set only when it's far off (a different path).
        float off = Math.Abs(at.X - x) + Math.Abs(at.Y - y);
        if (c.IsWalking ? off > 3f : (off > 0.001f || Math.Abs(at.Z - z) > 0.001f || c.Facing != facing)) c.PlaceAt(new Vector3(x, y, z), facing);
        int hp; if (int.TryParse(w[6], out hp) && !c.IsDead && Math.Abs(c.Health - hp) >= 1) c.Health = hp;
        if (w[7] == "1" && !c.IsDead) c.Kill();
        if (key == myKey) FollowMe();
    }

    Character SpawnMirror(string key, string[] pos)
    {
        string[] info;
        spawnInfo.TryGetValue(key, out info);
        bool coop = key.StartsWith("c:");
        if (coop && sheets.ContainsKey(key)) return SpawnSheet(key, (int)Math.Round(P(pos[2])), (int)Math.Round(P(pos[3])));
        var blueprint = info != null && info[2].Length > 0 ? info[2] : coop ? DefaultBlueprint : null;
        if (blueprint == null) return null;     // not found by name: can't be shown
        bool hostile = info != null && info.Length > 3 && info[3] == "1";
        var c = World.Spawn(blueprint, (int)Math.Round(P(pos[2])), (int)Math.Round(P(pos[3])), coop ? SpawnSide.Ally : hostile ? SpawnSide.Hostile : SpawnSide.Neutral);
        if (c == null) { spawnInfo.Remove(key); return null; }     // once: not every POS
        if (info != null && info.Length > 4) c.Name = info[4]; else if (coop) c.Name = key.Substring(2);
        if (coop) c.Faction = "player";
        if (info != null && info.Length > 5 && info[5].Length > 0) c.MakeMerchant(info[5]);     // a mod's merchant: its stock comes with a trade
        spawned[key] = c;
        Log.Info("mirror: " + c.Name + " placed");
        return c;
    }

    readonly Dictionary<string, string> sheets = new Dictionary<string, string>();
    Character SpawnSheet(string key, int x, int y)
    {
        string sheet;
        if (!sheets.TryGetValue(key, out sheet)) return null;
        Character c = null;
        try { c = World.SpawnFromCharacterFile(WriteSheet("mirror-" + key.Substring(2), sheet), x, y); }
        catch (Exception ex) { Log.Error("can't build " + key + ": " + ex); }
        if (c == null) return null;
        spawned[key] = c;
        Log.Info("mirror: " + c.Name + " placed (own sheet)");
        return c;
    }

    static float P(string s) { float f; return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out f) ? f : 0; }

    Character followed;
    void FollowMe()
    {
        Character c;
        if (myKey == null || !spawned.TryGetValue(myKey, out c) || c == followed) return;
        followed = c;
        // A character with its own sheet becomes this machine's player (HUD, inventory, camera); else the camera follows.
        if (c.HasPlayerSheet && World.MakePlayer(c)) { lastSign = SignOf(c); Build(); }
        else Camera.Follow(c);
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
        myKey = null; followed = null; spawned.Clear(); early.Clear(); sheets.Clear();
    }

    void LoadMirror()
    {
        GameEvents.Update.Add(this, dt => { if (listener != null) { HostMirror(dt); HostSheets(dt); } });
        // A character made in the game's creation for co-op: when its new game's first zone is there, it goes to the host.
        GameEvents.ZoneLoaded.Add(this, (zone, first) =>
        {
            if (!creating || host == null || host.Closed || World.Player == null) return;
            creating = false;
            var sheet = SheetOf(World.Player);
            if (sheet == null) { Tell("couldn't export your character"); return; }
            host.Send("NEW", World.Player.Name, sheet);
            Tell("sending " + World.Player.Name + " to the host...");
        });
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
                case PlayerOrderKind.Use: if (o.TargetEntityId != null) host.Send("USE", o.TargetEntityId); break;
                case PlayerOrderKind.Travel: host.Send("EXIT", o.X, o.Y); break;
                case PlayerOrderKind.Talk:
                {
                    var key = o.Target == null ? o.TargetEntityId : spawned.Where(kv => kv.Value == o.Target).Select(kv => kv.Key).FirstOrDefault() ?? o.Target.EntityId;
                    if (key != null) host.Send("TALK", key);
                    break;
                }
                default: Tell("abilities aren't relayed in co-op yet"); break;
            }
        });
    }
}
