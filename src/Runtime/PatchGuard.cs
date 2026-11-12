// Works' own patches never throw into the game: each patch method's body is guarded (an exception is caught here; a
// prefix then lets the game's own code run). Logged once per patch, so a broken one every frame doesn't flood the log.
using System;
using System.Collections.Generic;

namespace UnderRailWorks.Internal
{
static class PatchGuard
{
    static readonly HashSet<string> logged = new HashSet<string>();
    internal static int Count;
    internal static void Failed(string patch, Exception ex)
    {
        Count++;
        lock (logged) if (!logged.Add(patch)) return;
        Log.Always("UnderRailWorks: patch " + patch + " failed (the game carries on as if it weren't there; logged once): " + ex);
    }
}
}
