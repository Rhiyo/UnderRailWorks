// UnderRailWorks: the game's expansions (Expedition "xpbl", Heavy Duty "xphw"), and switching them off for a while.
// The game's side (dk2): an expansion is installed when its marker file Expansions\<id> exists in the game folder (the
// folder holds nothing else); the answer is cached (the installed set g, a second set h, and a flag per expansion), and
// everything an expansion adds asks it: its feats and skills (their DLC), zones, store stock and loot, levels above 25
// and specializations. A save records the set it was made with (a3r.DLCs) and the game refuses to load a save whose
// expansions aren't there; an exported character records it too.
// Switching one off answers "not installed" from those caches: the game acts as if it weren't there. Co-op uses it so a
// hosted game uses only expansions every player has (its saves then record only those).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnderRailWorks.Internal;

namespace UnderRailWorks
{
    [Preview("multiplayer: built for Co-op so far")]
    public static class Expansions
    {
        const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly;
        // The caches' fields (dk2.a / dk2.b: the per-expansion flags, sharing their names with methods; g / h: the sets).
        static readonly FieldInfo FlagXpbl = typeof(dk2).GetField("a", Static) ?? typeof(dk2).GetField("m_a", Static),
            FlagXphw = typeof(dk2).GetField("b", Static) ?? typeof(dk2).GetField("m_b", Static),
            SetG = typeof(dk2).GetField("g", Static), SetH = typeof(dk2).GetField("h", Static);
        static List<string> installed;
        static readonly HashSet<string> off = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Every expansion the game knows: "xpbl" (Expedition), "xphw" (Heavy Duty).
        public static IList<string> Known { get { return new List<string> { "xpbl", "xphw" }; } }
        // Its name as the game shows it ("Expedition").
        public static string Name(string id) { var n = id == null ? "" : dk2.d(id.ToLowerInvariant()); return string.IsNullOrEmpty(n) ? id : n; }

        // Installed on this computer, whether switched off or not.
        public static IList<string> Installed
        {
            get
            {
                if (installed == null)
                {
                    Reset();
                    installed = dk2.b().OrderBy(x => x).ToList();
                    Apply();
                }
                return installed.AsReadOnly();
            }
        }
        // What the game uses now: installed and not switched off.
        public static IList<string> Active { get { return Installed.Where(x => !off.Contains(x)).ToList(); } }
        // Switched off now.
        public static IList<string> Off { get { return off.ToList(); } }

        // These switched off (ids not installed are ignored), every other installed one on. On the main menu only: a game
        // under way keeps what it started with. False: in a game, or the game's caches weren't found.
        public static bool SwitchOff(IEnumerable<string> ids)
        {
            if (Hud.Exists || SetG == null || SetH == null) return false;
            var had = Installed;
            off.Clear();
            foreach (var id in ids ?? new string[0]) if (id != null && had.Contains(id.ToLowerInvariant())) off.Add(id.ToLowerInvariant());
            Reset();
            Apply();
            Log.Write("expansions: " + (off.Count == 0 ? "all installed on (" + string.Join(", ", had.ToArray()) + ")" : "switched off " + string.Join(", ", off.ToArray()) + "; on " + string.Join(", ", Active.ToArray())));
            return true;
        }
        // Every installed expansion on again.
        public static bool AllOn() { return SwitchOff(new string[0]); }

        // The game's caches emptied, so its next question reads the marker files again.
        static void Reset()
        {
            if (SetG != null) SetG.SetValue(null, null);
            if (SetH != null) SetH.SetValue(null, null);
            if (FlagXpbl != null) FlagXpbl.SetValue(null, null);
            if (FlagXphw != null) FlagXphw.SetValue(null, null);
        }
        // The caches filled, then the switched-off ones taken out.
        static void Apply()
        {
            var g = dk2.b();     // dk2.a() fills both sets

            var h = SetH == null ? null : SetH.GetValue(null) as HashSet<string>;
            foreach (var id in off) { g.Remove(id); if (h != null) h.Remove(id); }
            if (FlagXpbl != null) FlagXpbl.SetValue(null, (bool?)g.Contains("xpbl"));
            if (FlagXphw != null) FlagXphw.SetValue(null, (bool?)g.Contains("xphw"));
        }
    }
}
