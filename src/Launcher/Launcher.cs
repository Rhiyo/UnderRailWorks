// underrail.exe on .NET 10: loads the patched game (UnderRail.Game.exe, the 64-bit FNA build) into the default load
// context and runs its Main. The game's references resolve from the game folder: "underrail" is the game itself,
// everything else (UnderrailFix64, FNA, NVorbis, 0Harmony, GalaxyCSharp...) a DLL next to it. Anything that goes
// wrong before the game's own logging starts is written to UnderRailLauncher.log.
using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

static class Launcher
{
    static string dir;
    static void Note(string line)
    {
        try { File.AppendAllText(Path.Combine(dir, "UnderRailLauncher.log"), DateTime.Now.ToString("HH:mm:ss ") + line + Environment.NewLine); } catch { }
    }

    [STAThread]
    static int Main(string[] args)
    {
        dir = AppContext.BaseDirectory;
        try { File.WriteAllText(Path.Combine(dir, "UnderRailLauncher.log"), ""); } catch { }
        Note(".NET " + Environment.Version + " (" + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription + "), " + (Environment.Is64BitProcess ? "64" : "32") + "-bit");
        Environment.CurrentDirectory = dir;
        AppDomain.CurrentDomain.UnhandledException += (s, e) => Note("unhandled: " + e.ExceptionObject);
        Assembly game = null;
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            if (name.Name == "underrail") return game;
            foreach (string ext in new[] { ".dll", ".exe" })
            {
                string path = Path.Combine(dir, name.Name + ext);
                if (File.Exists(path)) { Note("load " + name.Name + " from " + Path.GetFileName(path)); return context.LoadFromAssemblyPath(path); }
            }
            Note("not found: " + name);
            return null;
        };
        try
        {
            game = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(dir, "UnderRail.Game.exe"));
            Note("game: " + game.FullName);
            var main = game.EntryPoint;
            object result = main.Invoke(null, main.GetParameters().Length == 0 ? null : new object[] { args });
            Note("game exited");
            return result is int code ? code : 0;
        }
        catch (Exception ex)
        {
            Note("failed: " + (ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex));
            return 1;
        }
    }
}
