// Not a mod: a stand-in for a future UnderRailWorks.dll (the same assembly name), with one member the real one doesn't
// have. NeedsNewer is compiled against it, so the loader's check (ModCheck) must refuse NeedsNewer by name; so must it
// GoneOverride, which overrides a WorksMod method only this stand-in has. OldButFine uses nothing new: it loads.
namespace UnderRailWorks
{
    public abstract class WorksMod { public abstract void Load(); public virtual void PostLoad() { } public virtual void OnFuture() { } }
    public static class World { public static void FutureFeature(int x) { } }
}
