// Quest Tracker: an UnderRailWorks mod. A panel on the HUD lists the active quests (newest first) with their journal
// text, as the journal shows it; it updates when a quest starts, completes or fails, and a line in the chat log says
// which. Settings: Options -> Mods -> Quest Tracker settings. Built from Works' API only (Quests, Ui, GameEvents).
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using UnderRailWorks;

[assembly: System.Reflection.AssemblyVersion("1.0.1")]

public sealed class QuestTrackerConfig : ModConfig
{
    [Header("Quest Tracker")]
    [Label("Show the tracker")] public bool Show = true;
    [Label("Quests shown"), Range(1, 8)] public int MaxQuests = 4;
    [Label("Lines per quest"), Range(1, 6), Tooltip("Longer journal texts are cut after this many lines.")] public int MaxLines = 3;
    [Label("Width"), Range(200, 500), Increment(10)] public int Width = 300;
    [Label("Background opacity"), Range(0, 100), Increment(5)] public int Opacity = 75;

    public override void OnChanged() { QuestTracker.Dirty = true; }
}


public sealed class QuestTracker : WorksMod
{
    internal static bool Dirty;
    QuestTrackerConfig config;
    Keybind toggleKey;
    UiElement hud;
    UiPanel panel;
    bool hidden;
    double sinceCheck;
    string shown = "";

    static readonly Color Heading = new Color(230, 160, 60), Body = new Color(210, 210, 200);

    public override void Load()
    {
        config = GetConfig<QuestTrackerConfig>();
        toggleKey = Keybinds.Register(this, "toggle", "Show/hide", Keys.F8);
        UiEvents.HudCreated.Add(this, root => { hud = root; panel = null; shown = ""; Dirty = true; });
        UiEvents.HudDestroyed.Add(this, () => hud = null);     // back to the main menu
        GameEvents.QuestChanged.Add(this, (id, state) =>
        {
            Dirty = true;
            var info = Quests.Info(id);
            if (info != null && state != QuestState.Active)
                Notify.Log("Tracker: " + (state == QuestState.Completed ? "done" : "failed") + " - " + FirstLine(Quests.JournalText(id)), state == QuestState.Completed ? new Color(120, 220, 120) : new Color(220, 120, 100));
        });
        GameEvents.GameStarted.Add(this, () => Dirty = true);
        GameEvents.Update.Add(this, seconds =>
        {
            if (hud == null) return;
            if (toggleKey.Pressed) { hidden = !hidden; Dirty = true; }
            sinceCheck += seconds;
            if (!Dirty && sinceCheck < 2) return;     // journal texts can change with variables: a slow check too
            sinceCheck = 0;
            Dirty = false;
            Refresh();
        });
    }

    void Refresh()
    {
        var active = Quests.Active.Take(config.MaxQuests).ToList();
        var key = string.Join("|", active.Select(Quests.JournalText)) + hidden + config.Show + config.Width + config.MaxLines + config.Opacity;
        if (key == shown && panel != null) return;
        shown = key;
        if (panel != null) Ui.Remove(panel);
        panel = null;
        if (!config.Show || hidden || active.Count == 0) return;

        int w = config.Width, y = 6;
        var rows = new List<KeyValuePair<UiElement, int>>();
        string lastGroup = null;
        foreach (var id in active)
        {
            var info = Quests.Info(id);
            var group = info == null ? "" : info.Group;
            if (group != lastGroup && group.Length > 0)
            {
                var h = Ui.Label(group, w - 16); Ui.SetColor(h, Heading);
                rows.Add(new KeyValuePair<UiElement, int>(h, y)); y += 20; lastGroup = group;
            }
            var lines = Ui.Wrap(Quests.JournalText(id), w - 44);     // room for " ..." on a cut line
            for (int i = 0; i < lines.Count && i < config.MaxLines; i++)
            {
                var text = (i == 0 ? "- " : "  ") + lines[i] + (i == config.MaxLines - 1 && lines.Count > config.MaxLines ? " ..." : "");
                var l = Ui.Label(text, w - 16); Ui.SetColor(l, Body);
                rows.Add(new KeyValuePair<UiElement, int>(l, y)); y += 18;
            }
            y += 4;
        }
        panel = Ui.Panel(w, y + 4);
        panel.Background = new Color(8, 8, 8) * (config.Opacity / 100f);
        foreach (var r in rows) Ui.Add(panel, r.Key, 8, r.Value);
        // Below the portrait, left (the HUD's top-left is the portrait and its health bar).
        Hud.AddPart(this, "Quest Tracker", panel, HudAnchor.TopLeft, 12, 145);     // a named HUD part: layout mods can move it
    }

    static string FirstLine(string text) { var t = (text ?? "").Split('.')[0]; return t.Length > 80 ? t.Substring(0, 77) + "..." : t; }

}
