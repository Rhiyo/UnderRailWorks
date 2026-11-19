// Watchful Eyes: an UnderRailWorks mod. Open a container that isn't yours and it tells you before you take anything:
// who would see the theft (and turn hostile), or that nobody is looking. The game gives no warning: the first item
// taken in view of a guard starts a fight. Optionally the same for picking and hacking guarded locks. Built from Works'
// API only (ContainerOpened, Container.Owned / Witnesses, Locks.ModifyAttempt).
using System;
using System.Linq;
using UnderRailWorks;

[assembly: System.Reflection.AssemblyVersion("1.0.0")]

public sealed class WatchfulEyesConfig : ModConfig
{
    [Header("Watchful Eyes")]
    [Label("Warn when opening others' containers")] public bool Containers = true;
    [Label("Refuse to pick a lock while watched"), Tooltip("A pick or hack attempt in view of a guard fails instead of starting a fight. Off: the game's rules.")] public bool GuardLocks = false;
}

public sealed class WatchfulEyes : WorksMod
{
    WatchfulEyesConfig config;

    public override void Load()
    {
        config = GetConfig<WatchfulEyesConfig>();
        GameEvents.ContainerOpened.Add(this, (c, who, first) =>
        {
            if (!config.Containers || who == null || !who.IsPlayer || !c.Owned || c.IsOwner(who) || c.Items.Count == 0) return;
            var eyes = c.Witnesses(who);
            if (eyes.Count == 0) { Notify.OverPlayer("Not yours, but no one is looking."); return; }
            var names = eyes.Select(e => e.Name).Distinct().ToList();
            var text = "Not yours: " + string.Join(", ", names.Take(3)) + (names.Count > 3 ? " and others" : "") + (names.Count == 1 ? " is" : " are") + " watching.";
            Notify.OverPlayer(text);
            Notify.Log(text + " Taking anything will provoke them.");
        });
        Locks.ModifyAttempt.Add(this, a =>
        {
            if (!config.GuardLocks || a.Character == null || !a.Character.IsPlayer) return;
            if (a.Lock.Witnesses(a.Character).Count == 0) return;     // doors and containers alike
            a.Force = false;
            Notify.OverPlayer("Someone is watching. Not now.");
        });
    }
}
