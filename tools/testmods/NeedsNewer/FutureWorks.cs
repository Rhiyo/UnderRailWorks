// Not a mod: a stand-in for a future UnderRailWorks.dll (the same assembly name), with one member the real one doesn't
// have. NeedsNewer is compiled against it, so the loader's check (ModCheck) must refuse NeedsNewer by name.
namespace UnderRailWorks
{
    public abstract class WorksMod { public abstract void Load(); public virtual void PostLoad() { } }
    public static class World { public static void FutureFeature(int x) { } }
}
