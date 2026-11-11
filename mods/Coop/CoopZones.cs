// Co-op: players in different zones. One game process has one live zone, so whoever is alone in a zone runs it on their
// own machine ("owns" it), and hands it back when they rejoin:
// - A joined character taking an exit with the others more than 12 tiles away goes alone (SPLIT): the host takes it out
//   of its zone and sends the zone it goes to (if the host has it stored) and the global variables and quests; the
//   joiner's game stops mirroring and travels there itself, as an ordinary game with their character as the player.
//   With everyone near, the whole group travels as before.
// - The host taking an exit while a joiner is far from them: that joiner stays (STAY): the host saves first and sends
//   that world for the joiner to own.
// - While apart, global variables and quests go both ways every 3 s (VARS), the owner's zone goes to the host every
//   60 s and when they leave it (ZONE), so the host's save has it, and their sheet as it changes (MYSHEET).
// - The joiner walking into the host's zone (REJOIN), or the host into theirs (HANDBACK / HANDED): the zone's state
//   goes to the host, the joiner is placed by the host's character and gets the world again (a mirror).
// - Two players can't run the same zone apart: walking into a zone someone else owns is refused until they're back.
// The world map shows each machine its own player's zone (the game's own: it's that game's current zone).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using UnderRailWorks;

public sealed partial class Coop
{
    const float TogetherTiles = 12;

    static float Dist(Character a, Character b) { return Vector2.Distance(new Vector2(a.Position.X, a.Position.Y), new Vector2(b.Position.X, b.Position.Y)); }

    // --- Variables and quests as text (both sides) --------------------------------------------------------------
    static string PackVars(Dictionary<string, object> vars)
    {
        var sb = new StringBuilder();
        foreach (var kv in vars)
        {
            string t = kv.Value is bool ? "b" : kv.Value is int ? "i" : kv.Value is float ? "f" : "s";
            sb.Append(kv.Key).Append('\u0001').Append(t).Append('\u0001').Append(Convert.ToString(kv.Value, System.Globalization.CultureInfo.InvariantCulture)).Append('\u0002');
        }
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(sb.ToString()));
    }
    static Dictionary<string, object> UnpackVars(string packed)
    {
        var d = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var e in Encoding.UTF8.GetString(Convert.FromBase64String(packed)).Split(new[] { '\u0002' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var p = e.Split('\u0001');
            if (p.Length < 3) continue;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            object v = p[1] == "b" ? (object)(p[2] == "True") : p[1] == "i" ? (object)int.Parse(p[2], inv) : p[1] == "f" ? (object)float.Parse(p[2], inv) : p[2];
            d[p[0]] = v;
        }
        return d;
    }
    static string QuestsNow() { return string.Join(",", Quests.Active.ToArray()) + "|" + string.Join(",", Quests.Completed.ToArray()) + "|" + string.Join(",", Quests.Failed.ToArray()); }
    static void ApplyQuests(string q)
    {
        var p = q.Split('|');
        if (p.Length < 3) return;
        Func<string, string[]> ids = s => s.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var id in ids(p[1])) if (Quests.State(id) != QuestState.Completed) Quests.Complete(id, false);
        foreach (var id in ids(p[2])) if (Quests.State(id) != QuestState.Failed) Quests.Fail(id, false);
        foreach (var id in ids(p[0])) if (Quests.State(id) == QuestState.NotStarted) Quests.Start(id, false);
    }

    // What changed since base (and base brought up to date).
    static Dictionary<string, object> Changes(Dictionary<string, object> now, Dictionary<string, object> base_)
    {
        var d = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var kv in now) { object o; if (!base_.TryGetValue(kv.Key, out o) || !Equals(o, kv.Value)) d[kv.Key] = kv.Value; }
        foreach (var kv in d) base_[kv.Key] = kv.Value;
        return d;
    }

    // --- Host ---------------------------------------------------------------------------------------------------
    Dictionary<string, object> hostVars; string hostQuests; double hostVarsTimer;
    string pendingTo, pendingEntry; Seat pendingFrom;

    void LoadZones()
    {
        // A joined character taking an exit: the group goes, or it goes alone.
        GameEvents.CharacterLeavingZone.Add(this, e =>
        {
            if (listener == null || e.Who == null || e.Cancel) return;
            var seat = seats.Values.FirstOrDefault(s => s.Character == e.Who);
            if (seat == null) return;
            var owner = seats.Values.FirstOrDefault(s => s.Split != null && string.Equals(s.Split, e.To, StringComparison.OrdinalIgnoreCase));
            if (owner != null) { e.Cancel = true; seat.Peer.Send("SAID", owner.Peer.Name + " is there on their own: you can go in when they're back"); return; }
            if (!Party.Members.Any(c => c != e.Who && Dist(c, e.Who) > TogetherTiles)) return;     // everyone near: the group goes
            e.Cancel = true;
            SplitTo(seat, e.To, e.Entry);
        });
        // The host taking an exit: into a zone a joiner owns (it's handed back first), or leaving far joiners behind.
        GameEvents.ZoneLeaving.Add(this, e =>
        {
            if (listener == null || e.Cancel) return;
            var owner = seats.Values.FirstOrDefault(s => s.Split != null && string.Equals(s.Split, e.To, StringComparison.OrdinalIgnoreCase));
            if (owner != null)
            {
                e.Cancel = true;
                pendingTo = e.To; pendingEntry = e.Entry; pendingFrom = owner;
                owner.Peer.Send("HANDBACK");
                Notify.Log("Co-op: " + owner.Peer.Name + " is in " + e.To + ": waiting for them to hand it over...", Say);
                return;
            }
            var p = World.Player;
            foreach (var s in seats.Values.Where(s => s.Character != null && s.InWorld && p != null && Dist(s.Character, p) > TogetherTiles).ToList()) Stay(s);
        });
        GameEvents.Update.Add(this, dt => { if (listener != null) HostVars(dt); });
        GameEvents.ZoneLoaded.Add(this, (zone, first) =>
        {
            if (listener == null) return;
            foreach (var s in seats.Values) s.Peer.Send("HOSTZONE", World.ZoneId);
            if (pendingFrom != null) { pendingFrom = null; pendingTo = null; }
        });
    }

    // Who runs which zone: told to everyone, and outlined on the host's world map.
    void Owners()
    {
        var z = string.Join(",", seats.Values.Where(s => s.Split != null).Select(s => s.Split).ToArray());
        foreach (var s in seats.Values) s.Peer.Send("OWNERS", z);
        foreach (var s in seats.Values.Concat(orphans).Where(s => s.Record != null))
            if (s.Split != null) WorldMap.Mark(this, "player:" + s.Record.Name, s.Split, OtherPlayer); else WorldMap.Unmark(this, "player:" + s.Record.Name);
    }
    static readonly Color OtherPlayer = new Color(80, 170, 255);

    // The joiner's map, while on their own: the host's zone and the others'.
    void MarkOthers()
    {
        foreach (var k in marked) WorldMap.Unmark(this, k);
        marked.Clear();
        if (ownZone == null) return;
        if (hostZone != null) { WorldMap.Mark(this, "host", hostZone, OtherPlayer); marked.Add("host"); }
        foreach (var z in owned.Where(z => !string.Equals(z, ownZone, StringComparison.OrdinalIgnoreCase))) { WorldMap.Mark(this, "other:" + z, z, OtherPlayer); marked.Add("other:" + z); }
    }
    readonly List<string> marked = new List<string>();

    void Detach(Seat seat)
    {
        var c = seat.Character;
        if (c == null) return;
        Remember(seat);
        CharacterControl.Release(c); Party.Remove(c);
        World.Despawn(c);
        seat.Character = null; seat.InWorld = false;
    }

    void SendState(Seat seat)
    {
        seat.Peer.Send("VARSALL", PackVars(Variables.Global.Snapshot()), QuestsNow());
        if (hostVars == null) { hostVars = Variables.Global.Snapshot(); hostQuests = QuestsNow(); }
    }

    void SplitTo(Seat seat, string to, string entry)
    {
        var name = seat.Record.Name;
        Detach(seat);
        SendState(seat);
        var zone = Zones.Export(to);
        if (zone != null) seat.Peer.Send("ZONE", Convert.ToBase64String(zone));
        seat.Peer.Send("HOSTZONE", World.ZoneId);
        seat.Peer.Send("SPLIT", to, entry);
        seat.Split = to; seat.Record.Zone = to;
        Owners();
        Log.Info(name + " goes to " + to + " alone");
        Notify.Log("Co-op: " + name + " went to " + to + " on their own", Say);
    }

    // The host leaves; this joiner, far from them, stays and runs this zone.
    void Stay(Seat seat)
    {
        if (!SaveGames.CanSave) return;     // in a fight: everyone goes
        var c = seat.Character; var zone = World.ZoneId;
        var at = c.Position; int facing = c.Facing;
        Remember(seat);
        var sheet = seat.Record.Sheet;
        Detach(seat);
        SendState(seat);
        seat.Peer.Send("STAY", zone, F(at.X), F(at.Y), F(at.Z), facing, sheet ?? "");
        seat.Split = zone; seat.Record.Zone = zone;
        SendWorld(seat);
        Owners();
        Notify.Log("Co-op: " + seat.Record.Name + " stays in " + zone, Say);
    }

    void HostVars(double dt)
    {
        if ((hostVarsTimer -= dt) > 0) return;
        hostVarsTimer = 3;
        var apart = seats.Values.Where(s => s.Split != null).ToList();
        if (apart.Count == 0 || hostVars == null) return;
        var changes = Changes(Variables.Global.Snapshot(), hostVars);
        var q = QuestsNow();
        if (changes.Count == 0 && q == hostQuests) return;
        hostQuests = q;
        foreach (var s in apart) s.Peer.Send("VARS", PackVars(changes), q);
    }

    // From a joiner apart (or rejoining). False: not one of these.
    bool HostZoneMessage(Seat seat, string[] w)
    {
        switch (w[0])
        {
            case "ZONE":
            {
                if (w.Length < 2) return true;
                var bytes = Convert.FromBase64String(w[1]);
                if (!Zones.Import(bytes)) Log.Warn("couldn't take " + Zones.ZoneOf(bytes) + " from " + seat.Peer.Name);
                return true;
            }
            case "VARS":
            {
                if (w.Length < 3) return true;
                var d = UnpackVars(w[1]);
                foreach (var kv in d) { Variables.Global.SetValue(kv.Key, kv.Value); if (hostVars != null) hostVars[kv.Key] = kv.Value; }
                ApplyQuests(w[2]); hostQuests = QuestsNow();
                foreach (var s in seats.Values.Where(x => x != seat && x.Split != null)) s.Peer.Send("VARS", w[1], hostQuests);
                return true;
            }
            case "ARRIVED":
                // In the host's own zone (it didn't know where the host was): back in, as a rejoin.
                if (w.Length > 1 && string.Equals(w[1], World.ZoneId, StringComparison.OrdinalIgnoreCase)) { seat.Split = null; Owners(); Enter(seat, seat.Record, false); return true; }
                if (w.Length > 1) { seat.Split = w[1]; seat.Record.Zone = w[1]; Owners(); Notify.Log("Co-op: " + seat.Record.Name + " is in " + w[1], Say); }
                return true;
            case "REJOIN":
                seat.Split = null; Owners();
                Enter(seat, seat.Record, false);     // next to the host's character, and the world again
                return true;
            case "HANDED":
                seat.Split = null; Owners();
                if (pendingFrom == seat && pendingTo != null) { var to = pendingTo; var entry = pendingEntry; World.Travel(to, entry); }     // the joiner is placed by the host there (zone loaded)
                return true;
        }
        return false;
    }

    // --- Joiner -------------------------------------------------------------------------------------------------
    string ownZone, goingTo, hostZone;
    HashSet<string> owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    byte[] pendingZone;
    string[] stay;     // STAY's zone, x, y, z, facing, sheet: the next world is ours to run
    Dictionary<string, object> myVars; string myQuests; double myVarsTimer, myZoneTimer;

    bool JoinerZoneMessage(string[] w)
    {
        switch (w[0])
        {
            case "HOSTZONE": if (w.Length > 1) hostZone = w[1]; MarkOthers(); return true;
            case "OWNERS": owned = new HashSet<string>((w.Length > 1 ? w[1] : "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase); MarkOthers(); return true;
            case "VARSALL":
            case "VARS":
            {
                if (w.Length < 3) return true;
                foreach (var kv in UnpackVars(w[1])) { Variables.Global.SetValue(kv.Key, kv.Value); if (myVars != null) myVars[kv.Key] = kv.Value; }
                ApplyQuests(w[2]); myQuests = QuestsNow();
                return true;
            }
            case "ZONE": if (w.Length > 1) pendingZone = Convert.FromBase64String(w[1]); return true;
            case "SPLIT":
            {
                if (w.Length < 3) return true;
                // On our own from here: this game runs, it isn't a copy any more.
                inWorld = false; Simulation.Frozen = false; Simulation.IsMirror = false;
                if (pendingZone != null) { Zones.Import(pendingZone); pendingZone = null; }
                goingTo = w[1];
                Tell("you go to " + w[1] + " on your own");
                if (!World.Travel(w[1], w[2])) Tell("couldn't travel to " + w[1]);
                return true;
            }
            case "STAY": stay = w.Skip(1).ToArray(); return true;
            case "HANDBACK":
            {
                if (ownZone == null) return true;
                HandOver("HANDED");
                Tell((hostName ?? "the host") + " is coming here: you're back in their game");
                return true;
            }
        }
        return false;
    }

    // Our zone and character go to the host; then we wait for their world.
    void HandOver(string word)
    {
        var zone = Zones.Export(ownZone);
        if (zone != null) host.Send("ZONE", Convert.ToBase64String(zone));
        var sheet = SheetOf(World.Player);
        if (sheet != null) host.Send("MYSHEET", sheet);
        SendVars(true);
        host.Send(word);
        ownZone = null; goingTo = null;
        MarkOthers();
    }

    void SendVars(bool force)
    {
        if (myVars == null) return;
        var changes = Changes(Variables.Global.Snapshot(), myVars);
        var q = QuestsNow();
        if (!force && changes.Count == 0 && q == myQuests) return;
        myQuests = q;
        host.Send("VARS", PackVars(changes), q);
    }

    void LoadZonesJoiner()
    {
        GameEvents.ZoneLoaded.Add(this, (zone, first) =>
        {
            if (host == null || host.Closed || goingTo == null) return;
            ownZone = World.ZoneId; goingTo = null;
            myVars = Variables.Global.Snapshot(); myQuests = QuestsNow();
            host.Send("ARRIVED", ownZone);
            MarkOthers();
            Tell("you're in " + ownZone + " on your own (your game runs it); come back to " + (hostName ?? "the host") + " by walking into their zone");
        });
        GameEvents.ZoneLeaving.Add(this, e =>
        {
            if (host == null || host.Closed || ownZone == null || e.Cancel) return;
            if (hostZone != null && string.Equals(e.To, hostZone, StringComparison.OrdinalIgnoreCase))
            {
                e.Cancel = true;     // the host's game places us there
                Tell("back to " + (hostName ?? "the host") + "...");
                HandOver("REJOIN");
                return;
            }
            if (owned.Contains(e.To)) { e.Cancel = true; Tell("someone else is there on their own: you can go in when they're back"); return; }
            var z = Zones.Export(ownZone);
            if (z != null) host.Send("ZONE", Convert.ToBase64String(z));
            SendVars(false);
            goingTo = e.To;
        });
        GameEvents.Update.Add(this, dt =>
        {
            if (host == null || host.Closed || ownZone == null) return;
            if ((myVarsTimer -= dt) <= 0) { myVarsTimer = 3; SendVars(false); var me = World.Player; if (me != null && lastSign != null && SignOf(me) != lastSign) SendMySheet(); }
            if ((myZoneTimer -= dt) <= 0) { myZoneTimer = 60; var z = Zones.Export(ownZone); if (z != null) host.Send("ZONE", Convert.ToBase64String(z)); }
        });
    }

    // The world just loaded is ours to run (STAY): our character from its sheet where it stood, the host's taken out.
    bool ArrivedOwn()
    {
        if (stay == null) return false;
        var s = stay; stay = null;
        Simulation.Frozen = false; Simulation.IsMirror = false;
        var hostBody = World.Player;
        Character me = null;
        if (s.Length > 5 && s[5].Length > 0)
        {
            try { me = World.SpawnFromCharacterFile(WriteSheet("mirror-own", s[5]), (int)Math.Round(P(s[1])), (int)Math.Round(P(s[2]))); } catch (Exception ex) { Log.Error("own: " + ex.Message); }
        }
        if (me == null) { Tell("couldn't place your character here"); return true; }
        int facing; int.TryParse(s[4], out facing);
        me.PlaceAt(new Vector3(P(s[1]), P(s[2]), P(s[3])), facing);
        World.MakePlayer(me);
        if (hostBody != null && hostBody != me) hostBody.RemoveFromWorld();
        followed = me; lastSign = SignOf(me);
        ownZone = s[0];
        myVars = Variables.Global.Snapshot(); myQuests = QuestsNow();
        host.Send("ARRIVED", ownZone);
        MarkOthers();
        Tell((hostName ?? "the host") + " left: you're in " + ownZone + " on your own (your game runs it)");
        Build();
        return true;
    }
}
