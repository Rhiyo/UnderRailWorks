// The game never sets its timing, so it runs with XNA's defaults: a fixed timestep of 1/60 s, which caps Update and
// Draw at 60 per second whatever the monitor. Called at the end of the game's constructor (enu), before the loop.
// FrameRateFix=1: MaxFps=0 turns the fixed timestep off (with VSync the frame rate follows the monitor's refresh
// rate, without it it's uncapped); MaxFps=N keeps a fixed timestep at N per second. The engine passes elapsed time
// down to its systems (e6d / GameTime), which is what makes a variable rate possible; logic that counts frames
// instead would run faster - a MaxFps=30 test shows it as half speed.
using System;
using Microsoft.Xna.Framework;
using UnderRailWorks;

namespace UnderRailWorks.Internal
{

public static class FrameRate
{
    static Game current;

    // From the options tab: apply a changed MaxFps straight away.
    public static void Reapply() { if (current != null) Apply(current); }

    public static void Apply(object instance)
    {
        try
        {
            var game = instance as Game;
            if (game == null || !FixSettings.FrameRateFix) return;
            current = game;
            int cap = FixSettings.MaxFps;
            if (cap <= 0) game.IsFixedTimeStep = false;
            else { game.IsFixedTimeStep = true; game.TargetElapsedTime = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / cap); }
            Log.Write(cap <= 0 ? "frame rate: no fixed timestep (VSync or uncapped)" : "frame rate: fixed at " + cap + " per second");
        }
        catch (Exception ex) { Log.Write("frame rate: " + ex.Message); }
    }
}
}
