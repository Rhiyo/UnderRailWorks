// Test-only mod (not shipped): keys that set up game state for testing Works in game.
//   F10: give the player 20 charons (Inventory.AddMoney).
using Microsoft.Xna.Framework.Input;

[assembly: System.Reflection.AssemblyVersion("1.0.0")]

public sealed class WorksTest : WorksMod
{
    public override void Load()
    {
        GameEvents.Update.Add(this, seconds =>
        {
            var p = World.Player;
            if (p == null || !Input.Pressed(Keys.F10)) return;
            p.Inventory.AddMoney(20);
            Log.Info("gave 20 charons: now " + p.Inventory.Money + ", HP " + p.Health + "/" + p.MaxHealth + ", level " + p.Level + ", XP " + p.Experience
                + ", Strength " + p.Ability("Strength") + ", Persuasion " + p.Skill("Persuasion") + ", feats " + string.Join(",", p.Feats));
        });
        GameEvents.QuestChanged.Add(this, (id, state) => Log.Info("quest " + id + " -> " + state));
        GameEvents.Saved.Add(this, folder => Log.Info("saved " + folder));
        GameEvents.GameLoaded.Add(this, () => Log.Info("game loaded"));
    }
}
