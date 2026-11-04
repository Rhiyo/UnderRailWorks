// The mod's version. The patcher writes the version of the UnderrailFix.dll it installs into underrail.exe, and
// FixStartup.Run compares the two at launch, so a DLL copied in without re-running the patcher is caught.
public static class FixVersion
{
    public const string Value = "1.0.8";
}
