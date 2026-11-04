// RoundTrip <game folder> <output folder> <save folder | config.dat>...
// Each save folder is copied to <output>/<name>/ with global.dat, chunk0.dat and info.dat loaded and written again on
// .NET 10 by the game's own code; config.dat likewise. Prints OK / FAIL per file.
using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

static class RoundTrip
{
    static int Main(string[] args)
    {
        string game = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(output);
        var results = new StreamWriter(Path.Combine(output, "results.txt")) { AutoFlush = true };     // Wine doesn't pass the console through
        Console.SetOut(results);
        Assembly gameAssembly = null;
        AssemblyLoadContext.Default.Resolving += (ctx, name) =>
        {
            if (name.Name == "underrail") return gameAssembly ??= ctx.LoadFromAssemblyPath(Path.Combine(game, "UnderRail.Game.exe"));
            foreach (var ext in new[] { ".dll", ".exe" })
            {
                var p = Path.Combine(game, name.Name + ext);
                if (File.Exists(p)) return ctx.LoadFromAssemblyPath(p);
            }
            return null;
        };
        try { return Run(args, output); }
        catch (Exception ex) { Console.WriteLine("ROUND TRIP FAILED: " + ex); return 1; }
    }

    static int Run(string[] args, string output)
    {
        // The Works runtime's .NET 10 compatibility (Random surrogate), as FixStartup does in the game.
        typeof(CoreCompat).GetMethod("Startup", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
        var resolver = (e6e)Activator.CreateInstance(typeof(bk3).Assembly.GetType("af9"), true);
        resolver.b(true); resolver.a(true); resolver.av1();
        AppDomain.CurrentDomain.AssemblyResolve += resolver.av0;
        bpo.RegisterService((ei2)resolver);
        resolver.d();
        typeof(dds).GetField("a").SetValue(null, resolver);
        long version = (long)typeof(eh9).GetField("c").GetValue(null);
        Console.WriteLine("runtime " + Environment.Version + ", data model version " + version);
        int bad = 0;
        for (int i = 2; i < args.Length; i++)
        {
            string src = Path.GetFullPath(args[i]);
            if (File.Exists(src))
            {
                Directory.CreateDirectory(output);
                bad += One(src, Path.Combine(output, Path.GetFileName(src)), version);
                continue;
            }
            string dst = Path.Combine(output, Path.GetFileName(src));
            Directory.CreateDirectory(dst);
            foreach (var f in Directory.GetFiles(src)) File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
            foreach (var f in new[] { "global.dat", "chunk0.dat", "info.dat" })
                if (File.Exists(Path.Combine(src, f))) bad += One(Path.Combine(src, f), Path.Combine(dst, f), version);
        }
        Console.WriteLine("result: " + bad + " failed");
        return bad;
    }

    static int One(string src, string dst, long version)
    {
        string name = Path.GetFileName(src), label = Path.GetFileName(Path.GetDirectoryName(src)) + "/" + name;
        try
        {
            if (name == "info.dat")
            {
                var o = eoq.DeserializeFromBinaryFile<a3r>(src, dds.a(), true);
                eoq.SerializeToBinaryFile(dst, o, dds.a(), version, true);
            }
            else if (name == "config.dat")
            {
                var o = eoq.DeserializeFromBinaryFileCompressed<ls>(src, dds.a(), true);
                eoq.SerializeToBinaryFileCompressed(dst, o, dds.a(), version, true);
            }
            else
            {
                var o = eoq.DeserializeFromBinaryFileCompressed<object>(src, dds.a(), true);
                eoq.SerializeToBinaryFileCompressed(dst, o, dds.a(), version, true);
            }
            Console.WriteLine("OK   " + label);
            return 0;
        }
        catch (Exception ex)
        {
            var e = ex; while (e.InnerException != null) e = e.InnerException;
            Console.WriteLine("FAIL " + label + ": " + e.GetType().Name + ": " + e.Message);
            return 1;
        }
    }
}
