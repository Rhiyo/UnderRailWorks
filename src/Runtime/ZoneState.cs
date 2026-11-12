// UnderRailWorks: a zone's stored state and the game's global variables as data, to move them between games (players
// in different zones: whoever is alone in a zone runs it, then hands it back).
//   var bytes = Zones.Export("cc_ph");        // the zone as this game has it stored (the current one: stored first)
//   Zones.Import(bytes);                      // this game's stored copy of that zone replaced (not the current zone)
//   var vars = Variables.Global.Snapshot();   // name -> value (bool, int, float, string)
// The game's side: a visited zone is kept in the in-memory bulk storage (bmg's ccc) as three compressed items,
// lmaps\<zone>_L1.uzl (doq: its layer state), .uzdm (l8), .ap (az0: pending actions, its elapsed time), written by
// bl1.yp when the player leaves or saves (d6f.arr(false) stores the current zone and keeps it live), read when the zone
// loads over its static map. The bytes are the game's own, so a save made after an import stays the stock game's format.
// Not carried: characters that travel between zones (glent\..., indexed in cyt.b()).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnderRailWorks.Internal;

namespace UnderRailWorks
{
    public static partial class Zones
    {
        static readonly string[] Parts = { "_L1.uzl", ".uzdm", ".ap" };
        static readonly MethodInfo StoreCurrent = typeof(cnk).Assembly.GetType("d6f") == null ? null : typeof(cnk).Assembly.GetType("d6f").GetMethod("arr", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(bool) }, null);
        static string Item(string zone, string part) { return (Path.Combine(d5x.x, zone) + part).ToLowerInvariant(); }

        // Null: this game has never stored that zone (never visited).
        [Preview("multiplayer: built for Co-op so far")] public static byte[] Export(string zoneId)
        {
            if (string.IsNullOrEmpty(zoneId)) return null;
            if (string.Equals(zoneId, World.ZoneId, StringComparison.OrdinalIgnoreCase) && StoreCurrent != null) StoreCurrent.Invoke(bpo.GetService<cnk>(), new object[] { false });
            var store = bmg.b();
            var found = Parts.Select(p => new { Name = Item(zoneId, p), Data = store.d(Item(zoneId, p)) }).ToList();
            if (found.Any(f => f.Data == null)) return null;
            using (var m = new MemoryStream())
            using (var w = new BinaryWriter(m))
            {
                w.Write(zoneId); w.Write(found.Count);
                foreach (var f in found)
                {
                    w.Write(f.Name);
                    w.Write(f.Data.d.HasValue ? (f.Data.d.Value ? (byte)1 : (byte)0) : (byte)2);
                    w.Write((int)f.Data.c);
                    w.Write(f.Data.a, (int)f.Data.b, (int)f.Data.c);
                }
                w.Flush();
                return m.ToArray();
            }
        }

        // Replaces this game's stored copy of the zone in data (Export's). False: it's the current zone (live: leave it
        // first), or the data isn't a zone.
        [Preview("multiplayer: built for Co-op so far")] public static bool Import(byte[] data)
        {
            if (data == null) return false;
            using (var r = new BinaryReader(new MemoryStream(data)))
            {
                var zone = r.ReadString();
                if (string.Equals(zone, World.ZoneId, StringComparison.OrdinalIgnoreCase)) return false;
                int n = r.ReadInt32();
                var store = bmg.b();
                for (int i = 0; i < n; i++)
                {
                    var name = r.ReadString(); var c = r.ReadByte(); int len = r.ReadInt32(); var bytes = r.ReadBytes(len);
                    if (!name.StartsWith(Item(zone, ""), StringComparison.Ordinal)) return false;
                    store.a(name, bytes, 0, bytes.Length, c == 2 ? (bool?)null : c == 1);
                }
                Log.Write("zones: imported " + zone + " (" + data.Length / 1024 + " KB)");
                return true;
            }
        }

        // The zone id in data (Export's).
        [Preview("multiplayer: built for Co-op so far")] public static string ZoneOf(byte[] data) { if (data == null) return null; using (var r = new BinaryReader(new MemoryStream(data))) return r.ReadString(); }
    }

    public sealed partial class Variables
    {
        // Every variable with a plain value (bool, int, float, string), by name.
        [Preview("multiplayer: built for Co-op so far")] public Dictionary<string, object> Snapshot()
        {
            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            var p = source();
            if (p == null) return result;
            foreach (var prop in p.Properties.ToList())
            {
                var v = prop.Value;
                if (v is bool || v is int || v is float || v is string) result[prop.Name] = v;
            }
            return result;
        }
        // Sets a plain value (bool, int, float, string) as Snapshot gives it.
        [Preview("multiplayer: built for Co-op so far")] public void SetValue(string name, object value)
        {
            if (value is bool) Set(name, (bool)value); else if (value is int) Set(name, (int)value); else if (value is float) Set(name, (float)value); else if (value is string) Set(name, (string)value);
        }
    }
}
