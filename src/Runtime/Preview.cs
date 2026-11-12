// [Preview]: a part of the API that isn't stable yet. It isn't covered by the API version (it may change or go in any
// Works release), it's left out of api/UnderRailWorks.api.txt, and a mod that uses it gets a note in the Mods tab.
// Parts built for one mod so far (co-op's plumbing, UnderRailFixes' drawing) stay here until a second user shows what
// shape they should keep. docs/Versioning.md.
using System;

namespace UnderRailWorks
{
    [AttributeUsage(AttributeTargets.All, Inherited = false)]
    public sealed class PreviewAttribute : Attribute
    {
        public PreviewAttribute() { }
        public PreviewAttribute(string why) { Why = why; }
        public string Why { get; private set; }
    }
}
