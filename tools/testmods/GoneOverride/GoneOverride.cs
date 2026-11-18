// Test mod: overrides a WorksMod method this Works doesn't have (built against FutureWorks.cs). The override would
// never be called, so the loader must refuse it and name the method.
using UnderRailWorks;
public sealed class GoneOverride : WorksMod
{
    public override void Load() { }
    public override void OnFuture() { }
}
