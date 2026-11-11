// UnderRailWorks: what exists in the game's data, and what the player has studied.
//   Items.Paths                       every item path (data\Rules\Items), e.g. "oddities\abominationbrain"
//   Progression.StudiedOddities       oddity item code -> times studied (the Oddities window's numbers)
//   World.Blueprints                  every entity blueprint path (for World.Spawn / placements)
//   someone.Blueprint                 a character's blueprint, best guess by name (null: none matches)
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnderRailWorks.Internal;

namespace UnderRailWorks
{
    static class DataFiles
    {
        // Paths under data\<dir> without the extension, backslashes, sorted.
        internal static List<string> Under(string dir, string ext)
        {
            var root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", dir);
            if (!Directory.Exists(root)) return new List<string>();
            return Directory.GetFiles(root, "*" + ext, SearchOption.AllDirectories)
                .Select(f => f.Substring(root.Length + 1, f.Length - root.Length - 1 - ext.Length).Replace('/', '\\'))
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    public static partial class Items
    {
        static List<string> paths;
        // Every item in the game's data (paths as Inventory.Add takes them).
        public static IList<string> Paths { get { return paths ?? (paths = DataFiles.Under(Path.Combine("rules", "items"), ".item")); } }
    }

    public static partial class Progression
    {
        static readonly FieldInfo StudiedField = typeof(e3q).GetField("h", BindingFlags.Instance | BindingFlags.NonPublic) ?? typeof(e3q).GetField("m_h", BindingFlags.Instance | BindingFlags.NonPublic);
        // The oddities the player has studied (item code -> times), as the Oddities window counts them. Empty without a game.
        public static IDictionary<string, int> StudiedOddities
        {
            get
            {
                var p = World.Player == null ? null : World.Player.PlayerData;
                var d = p == null || StudiedField == null ? null : StudiedField.GetValue(p) as Dictionary<string, int>;
                return d == null ? new Dictionary<string, int>() : new Dictionary<string, int>(d, StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    public static partial class World
    {
        static List<string> blueprints;
        // Every entity blueprint (creatures, objects...) as World.Spawn and placements take them.
        public static IList<string> Blueprints { get { return blueprints ?? (blueprints = DataFiles.Under(Path.Combine("meta", "playfield", "blueprints", "locale"), ".upeb")); } }

        // A creature blueprint whose file name is the character's name (letters and digits, any case: "Ironhead
        // Gunner" -> ...\ironheadgunner). Entities don't keep their blueprint, so this is a guess; null if none matches.
        internal static string BlueprintFor(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var key = new string(name.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
            return Blueprints.Where(b => b.StartsWith("creatures", StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault(b => string.Equals(Path.GetFileName(b), key, StringComparison.OrdinalIgnoreCase));
        }
    }

    public sealed partial class Character
    {
        // Its blueprint path, matched by name among the creature blueprints (null: no match). See World.BlueprintFor.
        // A character Works spawned or placed: the blueprint it was made from.
        public string Blueprint { get { var p = World.PlacementOf(Entity); if (p != null && p.Blueprint != null && p.Blueprint.Contains("\\")) return p.Blueprint; return World.BlueprintFor(Name); } }
    }
}
