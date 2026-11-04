// Example mod: settings through a ModConfig class (no UI code) and a panel on the in-game HUD built with the UI toolkit.
// A small panel shows how long this session has been played, with a Hide button; a key (set in its settings) toggles it.
using System;
using Microsoft.Xna.Framework.Input;
using UnderRailWorks;

[assembly: System.Reflection.AssemblyVersion("1.0.0")]

public sealed class HelloUIConfig : ModConfig
{
    [Header("Session clock")]
    [Label("Show the clock panel"), Tooltip("1 = show the panel when a game is loaded.")]
    public bool ShowClock = true;
}


public sealed class HelloUI : WorksMod
{
    HelloUIConfig config;
    Keybind toggleKey;
    UiPanel panel;
    UiElement label;
    double played;

    public override void Load()
    {
        config = GetConfig<HelloUIConfig>();
        toggleKey = Keybinds.Register(this, "toggle", "Show/hide", Keys.Home);     // rebindable in Options -> Controls
        // The in-game screen is built anew after each load or relayout: add the panel each time.
        UiEvents.HudCreated.Add(this, root =>
        {
            panel = Ui.Panel(230, 74);
            label = Ui.Label("", 210);
            Ui.Add(panel, label, 10, 8);
            Ui.Add(panel, Ui.Button("Hide", () => Ui.Show(panel, false)), 10, 36);
            Ui.Add(root, panel, 20, 180);
            Ui.Show(panel, config.ShowClock);
            Log.Info("panel added to the HUD");
        });
        GameEvents.Update.Add(this, seconds =>
        {
            if (panel == null) return;
            if (toggleKey.Pressed) Ui.Show(panel, !panel.Visible);
            played += seconds;
            Ui.SetText(label, "This session: " + TimeSpan.FromSeconds(played).ToString(@"hh\:mm\:ss"));
        });
    }

    public override void PostLoad() { Log.Info("UnderRailFixes is " + (ModLoader.IsLoaded("UnderRailFixes") ? "here too" : "not loaded")); }

}
