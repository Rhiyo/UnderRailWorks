// Co-op's options when hosting, and Co-op's API for other mods.
// Host this game... opens the Host window: Co-op's own option (Shared XP, 0-100 %, default 100) and any other mod's,
// then Host. Values are remembered (the mod's settings) for next time.
// The API (Mods.Api<CoopApi>("Coop"), or Mods.Call("Coop", ...)): who's playing, hosting or joined, events when players
// come and go, options on the Host window, and a mod's own messages between the host and the joiners (so a mod can
// add to co-op without its own networking: separate fights, separate zones, a campaign's conversations...).
//   var coop = Mods.Api<CoopApi>("Coop");
//   coop.AddHostOption(this, "pace", "Turn time limit (s)", 0, 120, 10, 0);
//   coop.Handle(this, "ping", (player, words) => coop.SendTo(player, this, "pong", words));
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using UnderRailWorks;

public sealed class CoopApi
{
    readonly Coop coop;
    internal CoopApi(Coop c) { coop = c; }

    public bool IsHosting { get { return coop.IsHosting; } }
    public bool IsJoined { get { return coop.IsJoined; } }
    // Host: the joined players' names and characters (the host's own character is World.Player).
    public IDictionary<string, Character> Players { get { return coop.PlayerCharacters(); } }
    // Host: a player joined (and has a character) / left. Joiner: never raised.
    public event Action<string, Character> PlayerJoined;
    public event Action<string> PlayerLeft;
    internal void RaiseJoined(string name, Character c) { var h = PlayerJoined; if (h != null) try { h(name, c); } catch (Exception ex) { coop.Log.Error("PlayerJoined: " + ex.Message); } }
    internal void RaiseLeft(string name) { var h = PlayerLeft; if (h != null) try { h(name); } catch (Exception ex) { coop.Log.Error("PlayerLeft: " + ex.Message); } }

    // The Shared XP percentage this game is hosted with.
    public int XpShare { get { return coop.xpShare; } }

    // --- Co-op's own rules, each one switchable by a mod that does it its own way --------------------------------
    // The group travels together (every player within 10 tiles of whoever takes an exit). Off: Co-op doesn't stop
    // anyone (a mod with players in different zones decides; Works: Party.NeedTogether, GameEvents.ZoneLeaving /
    // CharacterLeavingZone, Zones.Export / Import).
    public bool TravelTogether { get { return coop.travelTogether; } set { coop.travelTogether = value; coop.ApplyTravelRule(); } }
    // Everyone listens to every conversation (a read-only window). Off: only the one talking sees it (Works:
    // GameEvents.ConversationLine for a mod's own way).
    public bool EveryoneListens { get { return coop.everyoneListens; } set { coop.everyoneListens = value; } }
    // A joiner's conversation pauses the game, as any conversation does. Off: it runs while the others play.
    public bool ConversationsPause { get { return coop.conversationsPause; } set { coop.conversationsPause = value; } }
    // Co-op's Shared XP. Off: XP is the game's (a mod's own rule: Works' Progression.ModifyExperience, XpGain.Who).
    public bool SharesXp { get { return coop.sharesXp; } set { coop.sharesXp = value; } }

    // --- For mods that change how co-op plays -------------------------------------------------------------------
    // Joiner: this game is a copy of the host's (Works refuses world changes here; Simulation.IsMirror).
    public bool IsMirror { get { return Simulation.IsMirror; } }
    // Host: the player playing a character (null: the host's own or nobody's), and a player's character.
    public string PlayerOf(Character c) { return coop.PlayerOf(c); }
    public Character CharacterOf(string player) { Character c; return Players.TryGetValue(player ?? "", out c) ? c : null; }
    // Host: send a player the host's world again (save, send, their game loads it: after a mod changed what they see).
    public void SendWorld(string player) { coop.ResendWorld(player); }

    // An option on the Host window: a number (slider) or one of a few names (dropdown). Its value: HostOption.
    public void AddHostOption(WorksMod mod, string key, string label, int min, int max, int step, int value) { coop.AddOption(new Coop.HostOption { Mod = mod.Id, Key = key, Label = label, Min = min, Max = max, Step = Math.Max(1, step), Value = value }); }
    public void AddHostChoice(WorksMod mod, string key, string label, string[] names, int value) { coop.AddOption(new Coop.HostOption { Mod = mod.Id, Key = key, Label = label, Names = names, Value = value }); }
    public int HostOption(WorksMod mod, string key) { var o = coop.Option(mod.Id, key); return o == null ? 0 : o.Value; }

    // A mod's own messages. Host: from a player (their name); joiner: from the host (name null). Words are strings.
    public void Handle(WorksMod mod, string kind, Action<string, string[]> handler) { coop.Handlers[mod.Id + "/" + kind] = handler; }
    public void SendTo(string player, WorksMod mod, string kind, params object[] words) { coop.SendMod(player, mod.Id, kind, words); }
    public void SendToAll(WorksMod mod, string kind, params object[] words) { coop.SendMod("*", mod.Id, kind, words); }
    public void SendToHost(WorksMod mod, string kind, params object[] words) { coop.SendMod(null, mod.Id, kind, words); }
}

public sealed partial class Coop
{
    internal sealed class HostOption { public string Mod, Key, Label; public int Min, Max, Step, Value; public string[] Names; }
    readonly List<HostOption> options = new List<HostOption>();
    internal readonly Dictionary<string, Action<string, string[]>> Handlers = new Dictionary<string, Action<string, string[]>>();
    internal CoopApi Api;

    internal bool travelTogether = true, everyoneListens = true, conversationsPause = true, sharesXp = true;
    internal void ApplyTravelRule()
    {
        if (listener != null && travelTogether) Party.NeedTogether(this, PartyNeed.EnterZone, null, TogetherTiles);
        else Party.DontNeedTogether(this, PartyNeed.EnterZone, null);
    }
    internal string PlayerOf(Character c) { var s = seats.Values.FirstOrDefault(x => x.Character == c); return s == null ? null : s.Peer.Name; }
    internal void ResendWorld(string player) { var s = seats.Values.FirstOrDefault(x => x.Peer.Name == player && x.Character != null); if (s != null) SendWorld(s); }
    internal bool IsHosting { get { return listener != null; } }
    internal bool IsJoined { get { return host != null && !host.Closed; } }
    internal new ModLog Log { get { return base.Log; } }
    internal IDictionary<string, Character> PlayerCharacters() { return seats.Values.Where(s => s.Character != null).ToDictionary(s => s.Peer.Name, s => s.Character); }

    internal void AddOption(HostOption o)
    {
        int v; if (int.TryParse(Settings.Get("option." + o.Mod + "." + o.Key), out v)) o.Value = v;     // as last time
        options.RemoveAll(x => x.Mod == o.Mod && x.Key == o.Key);
        options.Add(o);
    }
    internal HostOption Option(string mod, string key) { return options.FirstOrDefault(x => x.Mod == mod && x.Key == key); }

    void LoadHost()
    {
        Api = new CoopApi(this);
        Mods.Provide(this, Api);
        AddOption(new HostOption { Mod = Id, Key = "xpshare", Label = "Shared XP (% of what anyone earns that the others get)", Min = 0, Max = 100, Step = 10, Value = xpShare });
    }

    UiWindow hostWindow;
    void AskAndHost()
    {
        if (hostWindow != null && hostWindow.IsOpen) return;
        int pad = Ui.S(10), w = Ui.S(520), rowH = Ui.LineHeight * 2 + Ui.S(10);
        hostWindow = Ui.Window(this, "host", "Host this game", w, Ui.S(70) + options.Count * rowH + Ui.S(50));
        if (hostWindow == null) return;
        hostWindow.Closed += () => hostWindow = null;
        w = hostWindow.Width;
        int y = hostWindow.Top + Ui.S(6);
        var apply = new List<Action>();
        foreach (var o in options)
        {
            var opt = o;
            Ui.Add(hostWindow, Ui.Label(o.Label, w - 2 * pad), pad, y);
            if (o.Names != null)
            {
                var c = Ui.Choice(w - 2 * pad, o.Names, Math.Max(0, Math.Min(o.Names.Length - 1, o.Value)));
                Ui.Add(hostWindow, c.View, pad, y + Ui.LineHeight + Ui.S(2));
                apply.Add(() => opt.Value = c.Index);
            }
            else
            {
                var s = Ui.Slider(w - 2 * pad, o.Min, o.Max, o.Value);
                s.Step = o.Step;
                Ui.Add(hostWindow, s.View, pad, y + Ui.LineHeight + Ui.S(6));
                apply.Add(() => opt.Value = (int)Math.Round(s.Value));
            }
            y += rowH;
        }
        var go = Ui.Button("Host", () =>
        {
            foreach (var a in apply) a();
            foreach (var o in options) Settings.Set("option." + o.Mod + "." + o.Key, o.Value.ToString());
            xpShare = Option(Id, "xpshare").Value;
            Settings.Set("xp.share", xpShare.ToString());
            if (hostWindow != null) hostWindow.Close();
            Commands.Print("co-op: " + Host(new string[0]));
        });
        var cancel = Ui.Button("Cancel", () => { if (hostWindow != null) hostWindow.Close(); });
        int by = hostWindow.Height - pad - go.Height;
        Ui.Add(hostWindow, go, pad, by);
        Ui.Add(hostWindow, cancel, pad + go.Width + Ui.S(6), by);
    }

    // MOD <mod> <kind> <words...>, both ways. Host: player null = to the host itself, "*" = everyone.
    internal void SendMod(string player, string mod, string kind, object[] words)
    {
        var line = new object[] { "MOD", mod, kind }.Concat(words ?? new object[0]).ToArray();
        if (listener != null)
        {
            foreach (var s in seats.Values) if (player == "*" || s.Peer.Name == player) s.Peer.Send(line);
        }
        else if (host != null && !host.Closed && player == null) host.Send(line);
    }

    bool ModMessage(string from, string[] w)
    {
        if (w[0] != "MOD" || w.Length < 3) return false;
        Action<string, string[]> h;
        if (Handlers.TryGetValue(w[1] + "/" + w[2], out h)) try { h(from, w.Skip(3).ToArray()); } catch (Exception ex) { Log.Error("mod message " + w[1] + "/" + w[2] + ": " + ex.Message); }
        return true;
    }
}
