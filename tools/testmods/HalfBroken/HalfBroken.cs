// Test mod: registers a command, an update handler and a Harmony-free hook, then fails in Load. Works must remove all
// of it (the mod is shown as failed and never half-runs): "hb.alive" must not exist and "HalfBroken update" never logged.
using System;
using UnderRailWorks;

public sealed class HalfBroken : WorksMod
{
    public override void Load()
    {
        Commands.Add(this, "hb.alive", "hb.alive: exists only if a failed mod's commands were kept", a => "alive");
        GameEvents.Update.Add(this, dt => Log.Info("HalfBroken update (must never appear)"));
        throw new InvalidOperationException("HalfBroken fails on purpose after registering");
    }
}
