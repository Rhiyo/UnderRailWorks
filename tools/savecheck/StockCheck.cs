// Save compatibility check, the stock side: loads save files (global.dat, chunk0.dat, info.dat) and config.dat with the
// stock game's own loader (eoq + its binder dds), as the stock game does. Run under Proton's Wine Mono in a folder
// holding the stock underrail.exe, so it resolves types exactly like the stock game under Proton.
// Usage: StockCheck.exe <results file> <save folder or .dat file>...
using System;
using System.Collections.Generic;
using System.IO;

static class StockCheck
{
    static int Main(string[] args)
    {
        var lines = new List<string>();
        int bad = 0, ok = 0;
        try
        {
            // As tb.Initialize does: the game's type resolver for its data files.
            var resolver = (e6e)Activator.CreateInstance(typeof(bk3).Assembly.GetType("af9"), true);
            resolver.b(true); resolver.a(true); resolver.av1();
            AppDomain.CurrentDomain.AssemblyResolve += resolver.av0;
            bpo.RegisterService((ei2)resolver);
            resolver.d();
            typeof(dds).GetField("a").SetValue(null, resolver);
            lines.Add("runtime: " + Environment.Version + (Type.GetType("Mono.Runtime") != null ? " (Mono)" : "") + ", game: " + typeof(bk3).Assembly.Location);
            for (int i = 1; i < args.Length; i++)
            {
                var files = new List<string>();
                if (Directory.Exists(args[i])) foreach (var f in new[] { "global.dat", "chunk0.dat", "info.dat" }) { var p = Path.Combine(args[i], f); if (File.Exists(p)) files.Add(p); }
                else files.Add(args[i]);
                foreach (var path in files)
                {
                    string label = Path.GetFileName(Path.GetDirectoryName(path)) + "/" + Path.GetFileName(path);
                    try
                    {
                        string name = Path.GetFileName(path);
                        // As the game reads them: config.dat (ls) and info.dat (a3r, the save list entry, uncompressed).
                        object o = name == "config.dat" ? (object)eoq.DeserializeFromBinaryFileCompressed<ls>(path, dds.a(), true)
                            : name == "info.dat" ? (object)eoq.DeserializeFromBinaryFile<a3r>(path, dds.a(), true)
                            : name.EndsWith(".char") ? (object)ajk.a(path)     // an exported character, as the import dialog reads it
                            : eoq.DeserializeFromBinaryFileCompressed<object>(path, dds.a(), true);
                        lines.Add("OK   " + label + " -> " + (o == null ? "null" : o.GetType().FullName));
                        ok++;
                    }
                    catch (Exception ex)
                    {
                        var e = ex; while (e.InnerException != null) e = e.InnerException;
                        lines.Add("FAIL " + label + ": " + e.GetType().Name + ": " + e.Message.Replace("\n", " "));
                        bad++;
                    }
                }
            }
        }
        catch (Exception ex) { lines.Add("CHECKER FAILED: " + ex); bad++; }
        lines.Add("result: " + ok + " ok, " + bad + " failed");
        File.WriteAllLines(args[0], lines.ToArray());
        return bad;
    }
}
