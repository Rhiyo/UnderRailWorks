// Test mod: declares an older Works API in mod.json ("works": "0.5") but uses only what this Works still has. The loader
// decides by what a mod uses, not by the number: it must load (with a note in the log and the Mods screen).
using UnderRailWorks;
public sealed class OldButFine : WorksMod
{
    public override void Load() { }
}
