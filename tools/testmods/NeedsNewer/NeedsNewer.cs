// Test mod: built against a newer Works than the installed one (FutureWorks.cs). The loader must not load it and must
// say what's missing (World.FutureFeature), and the game must carry on.
using UnderRailWorks;
public sealed class NeedsNewer : WorksMod
{
    public override void Load() { World.FutureFeature(1); }
}
