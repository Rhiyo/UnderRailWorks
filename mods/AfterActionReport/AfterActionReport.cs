// After-Action Report: an UnderRailWorks mod. When a fight ends, a panel sums it up: rounds, damage you dealt and
// took, hits, misses and criticals, kills; a second column keeps career totals for this save (in plain game
// variables: saved with the game, ignored by the stock game). Press the key (default F11) to show the last report
// again. Built from Works' API only (Combat, GameEvents, Ui, Variables).
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using UnderRailWorks;

[assembly: System.Reflection.AssemblyVersion("1.0.1")]

public sealed class AfterActionReportConfig : ModConfig
{
    [Header("After-Action Report")]
    [Label("Show a report after each fight")] public bool Show = true;
    [Label("Seconds it stays"), Range(3, 60), Increment(1)] public int Seconds = 15;
}


public sealed class AfterActionReport : WorksMod
{
    sealed class Fight { public int Rounds = 1, Dealt, Taken, Hits, Misses, Crits, Kills, Dodged; }

    AfterActionReportConfig config;
    Keybind showKey;
    Fight current, last;
    UiElement hud;
    UiWindow panel;
    double shownFor;

    public override void Load()
    {
        config = GetConfig<AfterActionReportConfig>();
        showKey = Keybinds.Add(this, "show", "Last report", Keys.F11);
        UiEvents.HudCreated.Add(this, root => { hud = root; panel = null; });
        UiEvents.HudDestroyed.Add(this, () => hud = null);     // back to the main menu
        GameEvents.CombatStarted.Add(this, () => current = new Fight());
        // Another game: its fights are its own (a fight in progress at a quickload doesn't carry over).
        GameEvents.GameStarted.Add(this, () => { current = null; last = null; });
        Combat.RoundEnded.Add(this, r => { if (current != null) current.Rounds = r + 1; });
        Combat.AttackResolved.Add(this, o =>
        {
            if (current == null) return;
            if (o.ByPlayer)
            {
                if (o.Hit) { current.Hits++; current.Dealt += o.HealthLost; if (o.Critical) current.Crits++; if (o.Killed) current.Kills++; }
                else current.Misses++;
            }
            else if (o.Target != null && o.Target.IsPlayer)
            {
                if (o.Hit) current.Taken += o.HealthLost; else current.Dodged++;
            }
        });
        GameEvents.CombatEnded.Add(this, () =>
        {
            if (current == null) return;
            last = current; current = null;
            Add("aar_fights", 1); Add("aar_dealt", last.Dealt); Add("aar_taken", last.Taken); Add("aar_kills", last.Kills);
            Add("aar_hits", last.Hits); Add("aar_misses", last.Misses); Add("aar_crits", last.Crits);
            if (config.Show) ShowReport();
        });
        GameEvents.Update.Add(this, seconds =>
        {
            if (showKey.Pressed && last != null) ShowReport();
            if (panel == null) return;
            shownFor += seconds;
            if (shownFor > config.Seconds) panel.Close();
        });
    }

    static void Add(string name, int amount) { if (Variables.Global.Available) Variables.Global.Set(name, Variables.Global.Get(name, 0) + amount); }
    static int Total(string name) { return Variables.Global.Get(name, 0); }

    void ShowReport()
    {
        if (hud == null || last == null) return;
        if (panel != null) panel.Close();
        var rows = new List<string[]>
        {
            new[] { "", "This fight", "Career" },
            new[] { "Rounds", last.Rounds.ToString(), Total("aar_fights") + " fights" },
            new[] { "Damage dealt", last.Dealt.ToString(), Total("aar_dealt").ToString() },
            new[] { "Damage taken", last.Taken.ToString(), Total("aar_taken").ToString() },
            new[] { "Hits / misses", last.Hits + " / " + last.Misses, Total("aar_hits") + " / " + Total("aar_misses") },
            new[] { "Critical hits", last.Crits.ToString(), Total("aar_crits").ToString() },
            new[] { "Kills", last.Kills.ToString(), Total("aar_kills").ToString() },
            new[] { "Attacks dodged", last.Dodged.ToString(), "" },
        };
        int line = Ui.LineHeight + 2, w = Ui.S(340), h = 32 + rows.Count * line;
        panel = Ui.Window(this, "report", "After-Action Report", w, h);
        if (panel == null) return;
        panel.Closed += () => panel = null;
        for (int i = 0; i < rows.Count; i++)
        {
            var colour = i == 0 ? new Color(160, 160, 150) : new Color(215, 215, 205);
            int[] x = { 10, Ui.S(150), Ui.S(240) };
            for (int c = 0; c < 3; c++) { var l = Ui.Label(rows[i][c], c == 0 ? Ui.S(140) : Ui.S(95)); Ui.SetColor(l, colour); Ui.Add(panel, l, x[c], panel.Top + 2 + i * line); }
        }
        shownFor = 0;
    }

}
