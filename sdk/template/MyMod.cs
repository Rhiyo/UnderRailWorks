// A starting point: a setting, a key, a HUD panel, a translated line, per-save data.
using Microsoft.Xna.Framework.Input;
using UnderRailWorks;

public sealed class MyModConfig : ModConfig
{
    [Header("My Mod")]
    [Label("Greet on load")] public bool Greet = true;
}

public sealed class MyMod : WorksMod
{
    MyModConfig config;
    Keybind key;

    public override void Load()
    {
        config = GetConfig<MyModConfig>();
        key = Keybinds.Add(this, "hello", "Say hello", Keys.None);     // the player binds it in Options -> Controls
        GameEvents.GameLoaded.Add(this, () =>
        {
            int loads = SaveData.Get("loads", 0) + 1;
            SaveData.Set("loads", loads);
            if (config.Greet) Notify.Log(T("welcome", loads));
        });
        GameEvents.Update.Add(this, seconds => { if (key.Pressed) Notify.OverPlayer(T("hello")); });
    }
}
