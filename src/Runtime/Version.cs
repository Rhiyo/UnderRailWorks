
using UnderRailWorks;
using UnderRailWorks.Internal;
// UnderRailWorks' version. The patcher writes the version of the UnderRailWorks.dll it installs into the game, and
// FixStartup.Run compares the two at launch, so a DLL copied in without re-running the patcher is caught.
public static class FixVersion
{
    public const string Value = "1.0.0";
}
