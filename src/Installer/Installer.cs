// UnderRailWorks patcher: backs up underrail.exe, writes the patched game as UnderRail.Game.exe and puts the .NET 10
// launcher in its place as underrail.exe (Steam starts the game as before, no launch options).
//   UnderRailWorks-Patcher [game folder]               install (or update an existing install, or replace 1.x)
//   UnderRailWorks-Patcher [game folder] --uninstall   restore the original game
// The game folder defaults to the folder the patcher is in, then the current folder.
//
// The game folder after installing:
//   underrail.exe            the launcher (src/Launcher): runs UnderRail.Game.exe on .NET 10 from dotnet\
//   underrail.exe.backup     the original game, what every install patches from and --uninstall puts back
//   UnderRail.Game.exe       the patched game: 64-bit, on our FNA.dll, calling UnderRailWorks.dll
//   UnderRailWorks.dll, FNA.dll, x64\, NVorbis.dll, UnderrailSteamHost.exe, 0Harmony.dll, dotnet\, Mods\
//   sfmlnet-*.dll            the game's, with the 32-bit flag cleared (originals kept as .backup)
// UnderRailFixes 1.x (underrail.exe patched in place to call UnderrailFix.dll / UnderrailFix64.dll) is recognised and
// replaced; its settings file UnderrailFix.ini is read by UnderRailWorks on its first start.
//
// Patches (each fix is switched on/off at runtime by UnderRailWorks.ini, so changing it never needs a re-patch):
//   cnt (video settings) resolution default/load/save -> ResolutionStore.Default/Load/Save
//   efw.a() / efw.a(key) -> UnderrailDisplayModes.List/Get (monitor modes, nearest-size fallback)
//   egd.ToString -> UnderrailDisplayModes.Label
//   aw9 (dropdown) open/close -> UnderrailDropdownScroll.Opened/Closed (fits the list, adds a scrollbar)
//   dka.b zoom limits ud(1f) / uf(2f), uf(3f) -> UnderrailDisplayModes.MinZoom / MaxZoom
//   t1 (Options): end of t1.h -> UnderrailOptions.AddTab, end of t1.d (Accept) -> Save; end of d6f.a(ls) -> Applied
//   the Game subclass's constructor (enu, created first in Main) -> FrameRate.Apply, WindowIcon.Apply
//   Main -> FixStartup.Run(version) (DLL version check, DPI awareness on Windows, mods)
//   UI scale: tb.k0/k4 screen size, SpriteBatch.Begin, the world batch's MatrixTransform, ScissorRectangle,
//   Mouse.GetState, bso.abb cursor warp and the loading screen's size reads -> UiScaleFix (UiScale.cs)
//   insets: ci3 sizing / shaking the world view, daj.iz (in-game surface mouse bounds), a7m.b floor position, d0p full-screen dim, czo.a menu backgrounds, bg1.iv picture rectangle -> UiScaleFix.FullScreen / WorldLocation(Get) / InScreen / FloorOrigin / DimRect
//   eg0 creature outline (reverse-highlight check, layer add), a7m layer draw / shader pass -> WorldDrawHooks
//   tb.Draw save screenshot: 1080p picture of the screen's centre -> SaveScreenshot.Limit / Target / Taken
//   b5r zoom label, mv SHIFT+wheel and ci3 zoom-button steps -> UnderrailDisplayModes.ShownZoom / ZoomStep
//   the 64-bit mode (Patch64): XNA -> FNA, SFML music, Steam and GOG calls, the WinForms window
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

static class Patcher
{
    const string Exe = "underrail.exe", Backup = "underrail.exe.backup", Game = "UnderRail.Game.exe", Runtime = "UnderRailWorks.dll";
    const string LauncherExe = "UnderRailLauncher.exe";
    static readonly string[] OldRuntimes = { "UnderrailFix", "UnderrailFix64", "UnderRailWorks" };

    static int Main(string[] args)
    {
        bool uninstall = args.Any(a => a == "--uninstall" || a == "-u");
        string dir = args.FirstOrDefault(a => !a.StartsWith("-")) ?? FindGame();
        int code;
        try
        {
            if (dir == null || !File.Exists(Path.Combine(dir, Exe)))
                throw new Exception("Couldn't find " + Exe + ". Put the patcher in the UnderRail folder, or pass the folder as an argument.");
            if (args.Any(a => a == "--32bit" || a == "--32")) throw new Exception("UnderRailWorks only has the 64-bit mode; for a 32-bit install use UnderRailFixes 1.x.");
            code = uninstall ? Uninstall(dir) : Install(dir);
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error: " + (Environment.GetEnvironmentVariable("UNDERRAILWORKS_DEBUG") == "1" ? ex.ToString() : ex.Message));
            code = 1;
        }
        if (args.Length == 0 && !Console.IsInputRedirected)
        {
            Console.WriteLine("Press Enter to close.");
            Console.ReadLine();
        }
        return code;
    }

    // The patcher's own folder. The bundled Linux patcher's BaseDirectory is the current directory, not the binary's:
    // run from elsewhere it found the game's installed copies as "its" files and reinstalled them over themselves.
    static string Here()
    {
        var dir = Environment.GetEnvironmentVariable("UNDERRAILWORKS_PATCHER_DIR");     // set by the Linux launch script
        return string.IsNullOrEmpty(dir) ? AppDomain.CurrentDomain.BaseDirectory : Path.Combine(dir, "linux");
    }

    static string FindGame()
    {
        foreach (string d in new[] { Here(), Environment.CurrentDirectory })
            if (File.Exists(Path.Combine(d, Exe))) return d;
        return null;
    }

    static string Version(ModuleDefinition fix)
    {
        return (string)fix.GetType("FixVersion").Fields.Single(f => f.Name == "Value").Constant;
    }

    enum Kind { Stock, Patched, Launcher }

    // What a file named underrail.exe is: the original game, a game patched by 1.x or Works, or our launcher (a native
    // program, no .NET metadata).
    static Kind KindOf(string path)
    {
        try
        {
            using (var m = ModuleDefinition.ReadModule(path))
                return m.AssemblyReferences.Any(r => OldRuntimes.Contains(r.Name)) ? Kind.Patched : Kind.Stock;
        }
        catch (BadImageFormatException) { return Kind.Launcher; }
    }

    // The 64-bit mode: a 64-bit process on our FNA.dll instead of XNA (Microsoft XNA is 32-bit only), on Windows and under
    // Proton. The game is pointed at FNA (ToFna) and at UnderRailWorks.dll (made by the build, src/Build/Retarget.cs --fna);
    // SFML music and the Steam calls go to managed replacements (Music.cs, SteamBridge.cs); the 32-bit flags are cleared on
    // the game and its sfmlnet-*.dll (those get .backup copies). FNA's native libraries go to x64\.
    static readonly string[] SfmlNet = { "sfmlnet-audio-2.dll", "sfmlnet-window-2.dll" };
    static readonly string[] Files = { Runtime, "NVorbis.dll", "UnderrailSteamHost.exe", "FNA.dll", "0Harmony.dll",
        Path.Combine("x64", "FNA3D.dll"), Path.Combine("x64", "FAudio.dll"), Path.Combine("x64", "SDL3.dll"),
        LauncherExe, "UnderRailLauncher.dll", "UnderRailLauncher.runtimeconfig.json", "UnderRailLauncher.deps.json",
        "System.Runtime.Serialization.Formatters.dll" };
    static readonly string[] Folders = { "dotnet", "Mods" };     // copied whole (Mods: the mods that ship with Works)

    static int Install(string dir)
    {
        string exe = Path.Combine(dir, Exe), backup = Path.Combine(dir, Backup), game = Path.Combine(dir, Game);
        string here = Here();     // on Linux the patcher binary is in linux/
        Func<string, string> bundled = name => new[] { here, Path.Combine(here, ".."), dir }.Select(d => Path.Combine(d, name)).FirstOrDefault(p => File.Exists(p) || Directory.Exists(p));

        // The source: the original game. underrail.exe is it on a first install, or after Steam put it back (an update or
        // "Verify files"): then it becomes the backup. Otherwise (our launcher, or a game patched by 1.x) the backup is.
        var kind = KindOf(exe);
        if (kind == Kind.Stock)
        {
            File.Copy(exe, backup, true);
            Console.WriteLine("Backed up the original as " + Backup + ".");
        }
        else
        {
            if (!File.Exists(backup) || KindOf(backup) != Kind.Stock) throw new Exception(Exe + " isn't the original game and there is no original " + Backup + " to patch from. Use Steam's \"Verify integrity of game files\", then run the patcher again.");
            Console.WriteLine(kind == Kind.Patched ? "Replacing the UnderRailFixes 1.x patch, from " + Backup + "." : "Updating the existing install, from " + Backup + ".");
        }

        // Our files next to the game (the patcher normally sits among them, in the game folder already).
        foreach (string name in Files)
        {
            string from = bundled(name);
            if (from == null) throw new Exception(name + " is missing: copy all of UnderRailWorks' files into the game folder.");
            string to = Path.Combine(dir, name);
            Directory.CreateDirectory(Path.GetDirectoryName(to));
            if (!Same(from, to)) File.Copy(from, to, true);
        }
        foreach (string folder in Folders)
        {
            string from = bundled(folder);
            if (from == null) { if (folder == "dotnet") throw new Exception("dotnet\\ is missing: copy all of UnderRailWorks' files into the game folder."); continue; }
            if (!Same(from, Path.Combine(dir, folder))) CopyTree(from, Path.Combine(dir, folder));
        }

        // The patched game, then the launcher in underrail.exe's place.
        byte[] patched = Patch(backup, Path.Combine(dir, Runtime));
        WriteSafely(game, patched);
        foreach (string lib in SfmlNet)
        {
            string path = Path.Combine(dir, lib), libBackup = path + ".backup";
            if (!File.Exists(libBackup)) File.Copy(path, libBackup);
            byte[] b = File.ReadAllBytes(libBackup);
            ClearClr32Bit(b);
            File.WriteAllBytes(path, b);
        }
        WriteSafely(exe, File.ReadAllBytes(Path.Combine(dir, LauncherExe)));
        if (Environment.OSVersion.Platform == PlatformID.Win32NT) LauncherIcon.CopyFrom(backup, exe);     // Explorer's icon
        foreach (string old in new[] { "UnderrailFix.dll", "UnderrailFix64.dll" })     // 1.x's runtime, unused now
            if (File.Exists(Path.Combine(dir, old))) { File.Delete(Path.Combine(dir, old)); Console.WriteLine("Removed 1.x's " + old + "."); }

        string version;
        using (var m = ModuleDefinition.ReadModule(Path.Combine(dir, Runtime))) version = Version(m);
        Console.WriteLine("Installed UnderRailWorks " + version + ": the game runs 64-bit on .NET 10 (dotnet\\) and FNA.");
        Console.WriteLine("Settings: UnderRailWorks.ini (created on the next launch); mods: the Mods folder and Options -> Mods in game.");
        Console.WriteLine("To undo: run the patcher with --uninstall, or Steam's \"Verify integrity of game files\".");
        return 0;
    }

    static int Uninstall(string dir)
    {
        string exe = Path.Combine(dir, Exe), backup = Path.Combine(dir, Backup);
        if (KindOf(exe) == Kind.Stock)
        {
            // Steam (an update or "Verify files") already put the original back.
            if (File.Exists(backup)) File.Delete(backup);
            Console.WriteLine(Exe + " is already the original.");
        }
        else
        {
            if (!File.Exists(backup) || KindOf(backup) != Kind.Stock) throw new Exception("No original " + Backup + " to restore. Use Steam's \"Verify integrity of game files\" (or reinstall) instead.");
            File.Copy(backup, exe, true);
            File.Delete(backup);
            Console.WriteLine("Restored the original " + Exe + ".");
        }
        foreach (string lib in SfmlNet)
        {
            string libBackup = Path.Combine(dir, lib + ".backup");
            if (File.Exists(libBackup)) { File.Copy(libBackup, Path.Combine(dir, lib), true); File.Delete(libBackup); }
        }
        foreach (string f in new[] { Game, LauncherExe }) if (File.Exists(Path.Combine(dir, f))) File.Delete(Path.Combine(dir, f));
        if (Directory.Exists(Path.Combine(dir, "dotnet"))) Directory.Delete(Path.Combine(dir, "dotnet"), true);
        Console.WriteLine("You can now delete UnderRailWorks' other files (see README.md). Mods\\, UnderRailWorks.ini and saves are kept.");
        return 0;
    }

    static bool Same(string a, string b) { return string.Equals(Path.GetFullPath(a).TrimEnd('/', '\\'), Path.GetFullPath(b).TrimEnd('/', '\\'), StringComparison.OrdinalIgnoreCase); }

    static void CopyTree(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (string f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)), true);
        foreach (string d in Directory.GetDirectories(from)) CopyTree(d, Path.Combine(to, Path.GetFileName(d)));
    }

    // Via a temporary file, so a failed write never leaves half a file.
    static void WriteSafely(string path, byte[] data)
    {
        string temp = path + ".new";
        File.WriteAllBytes(temp, data);
        File.Copy(temp, path, true);
        File.Delete(temp);
    }

    // Every type reference into an XNA 4.0 assembly now refers to the same type in FNA (FNA has all of XNA's types,
    // with the same names, in one assembly).
    static void ToFna(ModuleDefinition module, AssemblyNameReference fna)
    {
        var reference = module.AssemblyReferences.FirstOrDefault(r => r.Name == fna.Name);
        if (reference == null) { reference = new AssemblyNameReference(fna.Name, fna.Version) { PublicKeyToken = fna.PublicKeyToken, Culture = fna.Culture }; module.AssemblyReferences.Add(reference); }
        foreach (var t in module.GetTypeReferences())
            if (t.Scope is AssemblyNameReference a && a.Name.StartsWith("Microsoft.Xna.Framework")) t.Scope = reference;
        foreach (var r in module.AssemblyReferences.Where(r => r.Name.StartsWith("Microsoft.Xna.Framework")).ToList()) module.AssemblyReferences.Remove(r);
    }

    static byte[] Patch(string exe, string runtime)
    {
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Path.GetDirectoryName(exe));
        byte[] original = File.ReadAllBytes(exe);
        var asm = AssemblyDefinition.ReadAssembly(new MemoryStream(original), new ReaderParameters { AssemblyResolver = resolver });
        var module = asm.MainModule;
        var fix = ModuleDefinition.ReadModule(runtime, new ReaderParameters { AssemblyResolver = resolver });
        Func<string, string, MethodReference> hook = (type, name) => module.ImportReference(RuntimeType(fix, type).Methods.Single(m => m.Name == name));

        // Video settings: the resolution read from / written to config.dat passes through ResolutionStore.
        var cnt = module.GetType("cnt");
        var resolutionField = cnt.Fields.Single(f => f.Name == "a" && f.FieldType.Name == "DisplayResolution");
        foreach (var ctor in cnt.Methods.Where(m => m.IsConstructor && !m.IsStatic))
            foreach (var set in ctor.Body.Instructions.Where(i => i.OpCode == OpCodes.Stfld && i.Operand == resolutionField).ToList())
            {
                bool fromConfig = set.Previous.OpCode == OpCodes.Unbox_Any;     // (DisplayResolution)info.GetValue("VS:R", ...)
                ctor.Body.GetILProcessor().InsertBefore(set, Instruction.Create(OpCodes.Call, hook("ResolutionStore", fromConfig ? "Load" : "Default")));
            }
        var save = cnt.Methods.Single(m => m.Name == "GetObjectData");
        var written = save.Body.Instructions.Single(i => i.OpCode == OpCodes.Ldfld && i.Operand == resolutionField);
        save.Body.GetILProcessor().InsertAfter(written, Instruction.Create(OpCodes.Call, hook("ResolutionStore", "Save")));

        {
            var efw = module.GetType("efw");
            var table = efw.Fields.Single(f => f.IsStatic && f.FieldType.Name.StartsWith("Dictionary"));
            var list = efw.Methods.Single(m => m.Name == "a" && m.Parameters.Count == 0 && m.ReturnType.Name.StartsWith("List"));
            var get = efw.Methods.Single(m => m.Name == "a" && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.Name == "DisplayResolution");
            Replace(list, il => { il.Emit(OpCodes.Ldsfld, table); il.Emit(OpCodes.Call, hook("UnderrailDisplayModes", "List")); il.Emit(OpCodes.Ret); });
            Replace(get, il => { il.Emit(OpCodes.Ldsfld, table); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, hook("UnderrailDisplayModes", "Get")); il.Emit(OpCodes.Ret); });

            var label = module.GetType("egd").Methods.Single(m => m.Name == "ToString");
            Replace(label, il => { il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, hook("UnderrailDisplayModes", "Label")); il.Emit(OpCodes.Ret); });

            AppendCall(module.GetType("aw9"), "a", hook("UnderrailDropdownScroll", "Opened"));
            AppendCall(module.GetType("aw9"), "b", hook("UnderrailDropdownScroll", "Closed"));
        }

        {
            // dka.b sets the zoom limits every frame: ud(1f) minimum, uf(2f) and, on wide views, uf(3f) maximum.
            int limits = 0;
            foreach (var m in module.GetType("dka").Methods.Where(m => m.HasBody))
                foreach (var i in m.Body.Instructions.Where(i => i.OpCode == OpCodes.Ldc_R4 && i.Next != null && i.Next.Operand is MethodReference r && (r.Name == "ud" || r.Name == "uf") && r.DeclaringType.Name == "c5m").ToList())
                {
                    bool min = ((MethodReference)i.Next.Operand).Name == "ud";
                    i.OpCode = OpCodes.Call; i.Operand = hook("UnderrailDisplayModes", min ? "MinZoom" : "MaxZoom"); limits++;
                }
            Expect("zoom limits", limits, 3);
        }

        // The game object's constructor (enu, the Game subclass): timing, before the loop starts.
        var gameType = module.EntryPoint.Body.Instructions.Where(i => i.OpCode == OpCodes.Newobj).Select(i => ((MethodReference)i.Operand).DeclaringType.Resolve()).First(t => t != null);
        AppendCall(gameType, ".ctor", hook("FrameRate", "Apply"));
        AppendCall(gameType, ".ctor", hook("WindowIcon", "Apply"));     // the window's icon (WindowIcon.cs)

        // FixStartup.Run(version) runs before anything else in Main: version check against the DLL, DPI awareness.
        var main = module.EntryPoint;
        var mainIl = main.Body.GetILProcessor();
        var first = main.Body.Instructions[0];
        mainIl.InsertBefore(first, Instruction.Create(OpCodes.Ldstr, Version(fix)));
        mainIl.InsertBefore(first, Instruction.Create(OpCodes.Call, hook("FixStartup", "Run")));

        // Options: an UnderrailFix tab after the game's (end of t1.h), saved with Accept (end of t1.d) and applied once
        // the game has applied its own options (end of d6f.a(ls)).
        var options = module.GetType("t1");
        AppendCall(options, "h", hook("UnderrailOptions", "AddTab"));
        AppendCall(options, "d", hook("UnderrailOptions", "Save"));
        var apply = module.GetType("d6f").Methods.Single(m => m.Name == "a" && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.Name == "ls" && !m.IsStatic);
        var applyIl = apply.Body.GetILProcessor();
        foreach (var ret in apply.Body.Instructions.Where(i => i.OpCode == OpCodes.Ret).ToList())
        {
            // As in AppendCall: the call takes the ret's place, so branches to it still run the hook.
            applyIl.InsertAfter(ret, Instruction.Create(OpCodes.Ret));
            ret.OpCode = OpCodes.Call; ret.Operand = hook("UnderrailOptions", "Applied");
        }

        // UiScale: virtual screen size, scaled drawing, clip rectangles and mouse (see UiScale.cs).
        var tb = module.GetType("tb");
        foreach (string size in new[] { "k0", "k4" })     // bef: back buffer size, requested size
            foreach (var ret in tb.Methods.Single(m => m.Name == size && m.Parameters.Count == 0).Body.Instructions.Where(i => i.OpCode == OpCodes.Ret).ToList())
                tb.Methods.Single(m => m.Name == size && m.Parameters.Count == 0).Body.GetILProcessor().InsertBefore(ret, Instruction.Create(OpCodes.Call, hook("UiScaleFix", "Virtual")));
        var draw = tb.Methods.Single(m => m.Name == "Draw");
        int loading = 0;
        foreach (var i in draw.Body.Instructions.Where(i => i.Operand is MethodReference r && (r.Name == "get_PreferredBackBufferWidth" || r.Name == "get_PreferredBackBufferHeight")).ToList())
        {
            draw.Body.GetILProcessor().InsertAfter(i, Instruction.Create(OpCodes.Call, hook("UiScaleFix", ((MethodReference)i.Operand).Name.EndsWith("Width") ? "VirtualWidth" : "VirtualHeight")));
            loading++;
        }
        Expect("loading screen size reads", loading, 2);
        // Save screenshots: the 1080p view drawn straight into a 1080p picture (Screenshot.cs), instead of cropped.
        var shotLimits = draw.Body.Instructions.Where(i => i.OpCode == OpCodes.Ldc_I4 && (int)i.Operand == 2048).ToList();
        foreach (var i in shotLimits) draw.Body.GetILProcessor().InsertAfter(i, Instruction.Create(OpCodes.Call, hook("SaveScreenshot", "Limit")));
        Expect("screenshot size limits", shotLimits.Count, 2);
        var shotTargets = draw.Body.Instructions.Where(i => i.OpCode == OpCodes.Newobj && i.Operand is MethodReference r && r.DeclaringType.Name == "RenderTarget2D" && r.Parameters.Count == 8).ToList();
        Expect("screenshot targets", shotTargets.Count, 1);
        shotTargets[0].OpCode = OpCodes.Call; shotTargets[0].Operand = hook("SaveScreenshot", "Target");
        var shotField = tb.Fields.Single(f => f.Name == "x" && f.FieldType.Name == "Texture2D");
        var shotStores = draw.Body.Instructions.Where(i => i.OpCode == OpCodes.Stfld && i.Operand == shotField).ToList();
        foreach (var i in shotStores) draw.Body.GetILProcessor().InsertBefore(i, Instruction.Create(OpCodes.Call, hook("SaveScreenshot", "Taken")));
        Expect("screenshot stores", shotStores.Count, 1);

        int begins = 0, scissors = 0, mice = 0, transforms = 0;
        foreach (var type in module.GetTypes())
            foreach (var m in type.Methods.Where(m => m.HasBody))
                foreach (var i in m.Body.Instructions.ToList())
                {
                    var r = i.Operand as MethodReference;
                    if (r == null) continue;
                    string owner = r.DeclaringType.FullName;
                    if (owner == "Microsoft.Xna.Framework.Graphics.SpriteBatch" && r.Name == "Begin")
                    {
                        if (r.Parameters.Count != 5 && r.Parameters.Count != 6) throw new Exception("unexpected SpriteBatch.Begin overload in " + type.Name + "." + m.Name);
                        i.OpCode = OpCodes.Call; i.Operand = hook("UiScaleFix", r.Parameters.Count == 5 ? "Begin5" : "Begin6"); begins++;
                    }
                    else if (owner == "Microsoft.Xna.Framework.Graphics.GraphicsDevice" && (r.Name == "set_ScissorRectangle" || r.Name == "get_ScissorRectangle"))
                    {
                        i.OpCode = OpCodes.Call; i.Operand = hook("UiScaleFix", r.Name.StartsWith("set") ? "SetScissor" : "GetScissor"); scissors++;
                    }
                    else if (owner == "Microsoft.Xna.Framework.Input.Mouse" && r.Name == "GetState" && r.Parameters.Count == 0)
                    {
                        i.OpCode = OpCodes.Call; i.Operand = hook("UiScaleFix", "GetMouse"); mice++;
                    }
                    else if (type.Name.StartsWith("UrGenericSpriteBatchBase") && r.Name == "SetValue" && owner.EndsWith("EffectParameter")
                             && i.Previous.Operand is FieldReference f && f.FieldType.Name == "Matrix" && i.Previous.OpCode == OpCodes.Ldfld)
                    {
                        // MatrixTransform (the pixel-to-clip projection); the dither matrix is a local, not a field.
                        m.Body.GetILProcessor().InsertBefore(i, Instruction.Create(OpCodes.Call, hook("UiScaleFix", "Transform"))); transforms++;
                    }
                }
        var zoomLabel = module.GetType("b5r").Methods.Where(m => m.HasBody).SelectMany(m => m.Body.Instructions.Where(i => i.Operand is MethodReference r && r.Name == "aq8").Select(i => new { m, i })).ToList();
        foreach (var x in zoomLabel) x.m.Body.GetILProcessor().InsertAfter(x.i, Instruction.Create(OpCodes.Call, hook("UnderrailDisplayModes", "ShownZoom")));
        Expect("zoom label", zoomLabel.Count, 1);
        // mv: SHIFT+wheel calls an9().uo(notches * step); scale the step to the screen.
        var wheel = module.GetType("mv").Methods.Where(m => m.HasBody).SelectMany(m => m.Body.Instructions.Where(i => i.Operand is MethodReference r && r.Name == "uo" && r.DeclaringType.Name == "c5m").Select(i => new { m, i })).ToList();
        foreach (var x in wheel) x.m.Body.GetILProcessor().InsertBefore(x.i, Instruction.Create(OpCodes.Call, hook("UnderrailDisplayModes", "ZoomStep")));
        Expect("zoom wheel step", wheel.Count, 1);
        // ci3's zoom buttons: aq9(aq8() -/+ 0.25f), the same step as the wheel (ci3 sets the view's wheel step to 0.25).
        var buttons = module.GetType("ci3").Methods.Where(m => m.HasBody).SelectMany(m => m.Body.Instructions.Where(i => i.OpCode == OpCodes.Ldc_R4 && (float)i.Operand == 0.25f
            && i.Previous != null && i.Previous.Operand is MethodReference r && r.Name == "aq8" && i.Next != null && (i.Next.OpCode == OpCodes.Add || i.Next.OpCode == OpCodes.Sub)).Select(i => new { m, i })).ToList();
        foreach (var x in buttons) x.m.Body.GetILProcessor().InsertAfter(x.i, Instruction.Create(OpCodes.Call, hook("UnderrailDisplayModes", "ZoomStep")));
        Expect("zoom button steps", buttons.Count, 2);
        // bso.abb(Point): moves the cursor (SetCursorPos), given UI coordinates.
        var warp = module.GetType("bso").Methods.Single(m => m.Name == "abb" && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.Name == "Point");
        var warpIl = warp.Body.GetILProcessor();
        var warpStart = warp.Body.Instructions[0];
        warpIl.InsertBefore(warpStart, Instruction.Create(OpCodes.Ldarg_1));
        warpIl.InsertBefore(warpStart, Instruction.Create(OpCodes.Call, hook("UiScaleFix", "ToScreen")));
        warpIl.InsertBefore(warpStart, Instruction.Create(OpCodes.Starg_S, warp.Parameters[0]));
        // Insets: ci3 (the in-game surface) must keep the world view on the whole screen and take the mouse there.
        var gametop = module.GetType("ci3");
        var sizing = gametop.Methods.Where(m => m.HasBody).SelectMany(m => m.Body.Instructions.Where(i => i.Operand is MethodReference r && r.DeclaringType.Name == "a2x" && r.Name == "c"
            && i.Previous.OpCode == OpCodes.Ldfld && ((FieldReference)i.Previous.Operand).FieldType.Name == "mv")).ToList();
        foreach (var i in sizing) { i.OpCode = OpCodes.Call; i.Operand = hook("UiScaleFix", "FullScreen"); }
        Expect("world view sizing", sizing.Count, 1);
        // The screen shake: ci3.iv (its draw) moves the world view by a random offset and back to 0,0. The Location
        // setter's name is obfuscated, so it is found through the property.
        var setLocation = module.GetType("euz").Properties.Single(p => p.Name == "Location").SetMethod;
        var shake = gametop.Methods.Single(m => m.Name == "iv" && m.HasBody).Body.Instructions
            .Where(i => i.Operand is MethodReference r && r.Name == setLocation.Name && r.DeclaringType.Name == "euz").ToList();
        Expect("world view shake moves", shake.Count, 2);
        foreach (var i in shake) { i.OpCode = OpCodes.Call; i.Operand = hook("UiScaleFix", "WorldLocation"); }
        var getLocation = module.GetType("euz").Properties.Single(p => p.Name == "Location").GetMethod;
        var shakeCheck = gametop.Methods.Single(m => m.Name == "iv" && m.HasBody).Body.Instructions
            .Where(i => i.Operand is MethodReference r && r.Name == getLocation.Name && r.DeclaringType.Name == "euz").ToList();
        Expect("world view shake check", shakeCheck.Count, 1);
        foreach (var i in shakeCheck) { i.OpCode = OpCodes.Call; i.Operand = hook("UiScaleFix", "WorldLocationGet"); }
        // d0p.a(cp9, Color, Rectangle, bool, bool): the fill popups dim the whole screen with (see DimRect).
        var dim = module.GetType("d0p").Methods.Single(m => m.Name == "a" && m.IsStatic && m.Parameters.Count == 5 && m.Parameters[2].ParameterType.Name == "Rectangle");
        var dimIl = dim.Body.GetILProcessor();
        var dimStart = dim.Body.Instructions[0];
        dimIl.InsertBefore(dimStart, Instruction.Create(OpCodes.Ldarg_2));
        dimIl.InsertBefore(dimStart, Instruction.Create(OpCodes.Call, hook("UiScaleFix", "DimRect")));
        dimIl.InsertBefore(dimStart, Instruction.Create(OpCodes.Starg_S, dim.Parameters[2]));
        // a7m.b(int, int) -> Vector2: mouse to floor position, mixing view-relative and screen coordinates (see FloorOrigin).
        var floor = module.GetType("a7m").Methods.Single(m => m.Name == "b" && m.Parameters.Count == 2 && m.Parameters.All(p => p.ParameterType.MetadataType == MetadataType.Int32) && m.ReturnType.Name == "Vector2");
        var floorAt = floor.Body.Instructions.Where(i => i.Operand is MethodReference r && r.Name == "at" && r.Parameters.Count == 0).ToList();
        Expect("floor position at() reads", floorAt.Count, 1);
        floorAt[0].OpCode = OpCodes.Call; floorAt[0].Operand = hook("UiScaleFix", "FloorOrigin");
        // czo.a(esj) -> bg1: full-screen menu backgrounds (see BackgroundSize).
        var background = module.GetType("czo").Methods.Single(m => m.Name == "a" && m.IsStatic && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.Name == "esj" && m.ReturnType.Name == "bg1");
        var bgIl = background.Body.GetILProcessor();
        var bgStart = background.Body.Instructions[0];
        bgIl.InsertBefore(bgStart, Instruction.Create(OpCodes.Ldarg_0));
        bgIl.InsertBefore(bgStart, Instruction.Create(OpCodes.Call, hook("UiScaleFix", "BackgroundSize")));
        bgIl.InsertBefore(bgStart, Instruction.Create(OpCodes.Starg_S, background.Parameters[0]));
        foreach (var ret in background.Body.Instructions.Where(i => i.OpCode == OpCodes.Ret).ToList())
        {
            // The returned bg1 is on the stack: pass it through BackgroundPlace (taking the ret's place, as in AppendCall).
            bgIl.InsertAfter(ret, Instruction.Create(OpCodes.Ret));
            ret.OpCode = OpCodes.Call; ret.Operand = hook("UiScaleFix", "BackgroundPlace");
        }
        // bg1.iv: the first Rectangle it builds is where the picture is drawn; adjust it in place (BackgroundDraw).
        var picture = module.GetType("bg1").Methods.Single(m => m.Name == "iv");
        var rectCtor = picture.Body.Instructions.First(i => i.Operand is MethodReference r && r.DeclaringType.Name == "Rectangle" && r.Name == ".ctor" && r.Parameters.Count == 4);
        var target = rectCtor;
        while (target != null && !((target.OpCode == OpCodes.Ldloca || target.OpCode == OpCodes.Ldloca_S) && ((VariableDefinition)target.Operand).VariableType.Name == "Rectangle")) target = target.Previous;
        if (target == null) throw new Exception("this game version isn't supported (background draw rectangle not found)");
        var picIl = picture.Body.GetILProcessor();
        picIl.InsertAfter(rectCtor, Instruction.Create(OpCodes.Call, hook("UiScaleFix", "BackgroundDraw")));
        picIl.InsertAfter(rectCtor, Instruction.Create(OpCodes.Ldarg_0));
        picIl.InsertAfter(rectCtor, Instruction.Create(target.OpCode, (VariableDefinition)target.Operand));

        var surface = module.GetType("daj").Methods.Single(m => m.Name == "iz");
        Replace(surface, il2 => { il2.Emit(OpCodes.Ldarg_1); il2.Emit(OpCodes.Call, hook("UiScaleFix", "InScreen")); il2.Emit(OpCodes.Ret); });

        // Gameplay, Show Covered: outlines on the covered part of characters (ShowCovered.cs).
        {
            var hotkey = module.GetType("TimelapseVertigo.Common.Settings.Hotkey") ?? module.GetTypes().Single(t => t.IsEnum && t.Name == "Hotkey");
            int reverse = Convert.ToInt32(hotkey.Fields.Single(f => f.Name == "ReverseHighlight").Constant);
            var creatures = module.GetType("eg0").Methods.Where(m => m.HasBody && m.Body.Instructions.Any(i => i.OpCode == OpCodes.Ldstr && (string)i.Operand == "Outline")).ToList();
            Expect("creature outline builders", creatures.Count, 1);
            var build = creatures[0];
            var keyChecks = build.Body.Instructions.Where(i => i.Operand is MethodReference r && r.Name == "IsKeyDown"
                && i.Previous.Operand is MethodReference k && k.DeclaringType.Name == "d1o" && Constant(i.Previous.Previous) == reverse).ToList();
            Expect("outline reverse-highlight checks", keyChecks.Count, 1);
            var buildIl = build.Body.GetILProcessor();
            // The input check (mv.w) right before the outline is built: through ShowCovered.Interactive.
            var outlineName = build.Body.Instructions.First(i => i.OpCode == OpCodes.Ldstr && (string)i.Operand == "Outline");
            var inputCheck = outlineName.Previous;
            while (inputCheck != null && !(inputCheck.OpCode == OpCodes.Ldfld && ((FieldReference)inputCheck.Operand).Name == "w" && ((FieldReference)inputCheck.Operand).DeclaringType.Name == "mv")) inputCheck = inputCheck.Previous;
            if (inputCheck == null) throw new Exception("this game version isn't supported (outline input check)");
            buildIl.InsertAfter(inputCheck, Instruction.Create(OpCodes.Call, hook("WorldDrawHooks", "Interactive")));
            buildIl.InsertAfter(keyChecks[0], Instruction.Create(OpCodes.Call, hook("WorldDrawHooks", "Reverse")));
            var add = keyChecks[0].Next;
            Func<Instruction, bool> isAdd = i => { var r = i.Operand as MethodReference; return r != null && r.DeclaringType.Name == "bym" && r.Name == "a" && r.Parameters.Count == 1 && r.Parameters[0].ParameterType.Name == "a2f"; };
            while (add != null && !isAdd(add)) add = add.Next;
            if (add == null || ((MethodReference)add.Operand).ReturnType.MetadataType != MetadataType.Void) throw new Exception("this game version isn't supported (outline layer add)");
            add.OpCode = OpCodes.Call; add.Operand = hook("WorldDrawHooks", "Add");

            var view = module.GetType("a7m");
            var drawLayer = view.Methods.Single(m => m.Name == "a" && !m.IsStatic && m.Parameters.Count == 4 && m.Parameters[0].ParameterType.Name == "cp9"
                && m.Parameters[1].ParameterType.Name == "a2f" && m.Parameters[2].ParameterType.MetadataType == MetadataType.Boolean && m.Parameters[3].ParameterType.Name == "bkx");
            var drawIl = drawLayer.Body.GetILProcessor();
            var drawStart = drawLayer.Body.Instructions[0];
            drawIl.InsertBefore(drawStart, Instruction.Create(OpCodes.Ldarg_0));
            drawIl.InsertBefore(drawStart, Instruction.Create(OpCodes.Ldarg_1));
            drawIl.InsertBefore(drawStart, Instruction.Create(OpCodes.Ldarg_2));
            drawIl.InsertBefore(drawStart, Instruction.Create(OpCodes.Ldarg_3));
            drawIl.InsertBefore(drawStart, Instruction.Create(OpCodes.Call, hook("WorldDrawHooks", "Drawn")));
            // Its a2f.f3(cp9, bkx) call: through ShowCovered.DrawLayer (same arguments), so the keyhole can draw scenery itself.
            var drawCalls = drawLayer.Body.Instructions.Where(i => i.OpCode == OpCodes.Callvirt && i.Operand is MethodReference r && r.Name == "f3"
                && r.DeclaringType.Name == "a2f").ToList();
            Expect("layer draw calls", drawCalls.Count, 1);
            drawCalls[0].OpCode = OpCodes.Call; drawCalls[0].Operand = hook("WorldDrawHooks", "DrawLayer");
            // a7m.b(cp9, ak1): what a7m draws over each tile right after its floor, not as layers (the tile overlay's
            // ob.w7 and the cgr tile renderers, e.g. the red of transition tiles): through ShowCovered.TileExtras
            // (same stack: the view, the context, the tile), so it goes wherever the keyhole sent that floor.
            var tileStep = view.Methods.Single(m => m.Name == "b" && !m.IsStatic && m.Parameters.Count == 2 && m.Parameters[0].ParameterType.Name == "cp9"
                && m.Parameters[1].ParameterType.Name == "ak1" && m.ReturnType.MetadataType == MetadataType.Void);
            var tileCalls = view.Methods.Where(m => m.HasBody).SelectMany(m => m.Body.Instructions.Where(i => (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
                && i.Operand is MethodReference r && r.DeclaringType.FullName == "a7m" && r.Name == tileStep.Name && r.Parameters.Count == 2
                && r.Parameters[0].ParameterType.Name == "cp9" && r.Parameters[1].ParameterType.Name == "ak1")).ToList();
            Expect("tile extras calls", tileCalls.Count, 1);
            tileCalls[0].OpCode = OpCodes.Call; tileCalls[0].Operand = hook("WorldDrawHooks", "TileExtras");
            // The world pass ends its loop with GraphicsDevice.SetRenderTarget(null): the keyhole's end of frame.
            var resets = view.Methods.Where(m => m.HasBody).SelectMany(m => m.Body.Instructions.Where(i => i.Operand is MethodReference r && r.Name == "SetRenderTarget"
                && i.Previous.OpCode == OpCodes.Ldnull && i.Previous.Previous.OpCode == OpCodes.Ldfld && ((FieldReference)i.Previous.Previous.Operand).Name == "c"
                && ((FieldReference)i.Previous.Previous.Operand).DeclaringType.Name == "cp9").Select(i => new { m, i })).ToList();
            Expect("world pass target resets", resets.Count, 1);
            {
                var loadContext = resets[0].i.Previous.Previous.Previous;
                var resetIl = resets[0].m.Body.GetILProcessor();
                resetIl.InsertBefore(loadContext, Instruction.Create(OpCodes.Ldarg_0));
                if (!loadContext.OpCode.Name.StartsWith("ldarg")) throw new Exception("this game version isn't supported (world pass target reset)");
                resetIl.InsertBefore(loadContext, loadContext.Operand is ParameterDefinition p ? Instruction.Create(loadContext.OpCode, p) : Instruction.Create(loadContext.OpCode));
                resetIl.InsertBefore(loadContext, Instruction.Create(OpCodes.Call, hook("WorldDrawHooks", "WorldDone")));
            }
            var pass = view.Methods.Single(m => m.Name == "b" && !m.IsStatic && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.Name == "cp9" && m.ReturnType.MetadataType == MetadataType.Void);
            var passIl = pass.Body.GetILProcessor();
            var passStart = pass.Body.Instructions[0];
            passIl.InsertBefore(passStart, Instruction.Create(OpCodes.Ldarg_0));
            passIl.InsertBefore(passStart, Instruction.Create(OpCodes.Call, hook("WorldDrawHooks", "BeforePass")));
            foreach (var ret in pass.Body.Instructions.Where(i => i.OpCode == OpCodes.Ret).ToList())
            {
                // As in AppendCall: the first inserted instruction takes the ret's place.
                passIl.InsertAfter(ret, Instruction.Create(OpCodes.Ret));
                passIl.InsertAfter(ret, Instruction.Create(OpCodes.Call, hook("WorldDrawHooks", "AfterPass")));
                passIl.InsertAfter(ret, Instruction.Create(OpCodes.Ldarg_1));
                ret.OpCode = OpCodes.Ldarg_0; ret.Operand = null;
            }
        }

        Expect("SpriteBatch.Begin calls", begins, 2);
        Expect("scissor rectangle reads/writes", scissors, 11);
        Expect("Mouse.GetState calls", mice, 7);
        Expect("world batch projections", transforms, 1);

        Patch64(module, hook, fix);

        var ms = new MemoryStream();
        asm.Write(ms);
        byte[] image = ms.ToArray();
        // Cecil drops IMAGE_FILE_LARGE_ADDRESS_AWARE and friends; keep the original COFF characteristics.
        Array.Copy(original, Coff(original), image, Coff(image), 2);
        return image;
    }

    // Each patch must find exactly what this version of the game has, or nothing is written.
    static void Expect(string what, int found, int expected)
    {
        if (found != expected) throw new Exception("this game version isn't supported (" + what + ": found " + found + ", expected " + expected + ")");
    }

    static void Patch64(ModuleDefinition module, Func<string, string, MethodReference> hook, ModuleDefinition fix)
    {
        // SFML music: every use of an SFML type becomes an object handled by FixMusic.
        var music = new Dictionary<string, string> {
            { ".ctor", "Create" }, { "get_Status", "Status" }, { "set_Loop", "SetLoop" }, { "set_Volume", "SetVolume" },
            { "set_Pitch", "SetPitch" }, { "Play", "Play" }, { "Stop", "Stop" }, { "Dispose", "Dispose" } };
        Func<TypeReference, bool> sfml = t => t != null && t.Scope != null && t.Scope.Name.StartsWith("sfmlnet");
        int calls = 0, steam = 0;
        foreach (var type in module.GetTypes())
        {
            foreach (var f in type.Fields.Where(f => sfml(f.FieldType))) f.FieldType = module.TypeSystem.Object;
            foreach (var m in type.Methods.Where(m => m.HasBody))
            {
                foreach (var v in m.Body.Variables.Where(v => sfml(v.VariableType))) v.VariableType = module.TypeSystem.Object;
                foreach (var i in m.Body.Instructions)
                {
                    var r = i.Operand as MethodReference;
                    if (r == null) continue;
                    if (sfml(r.DeclaringType))
                    {
                        string target;
                        if (!music.TryGetValue(r.Name, out target)) throw new Exception("this game version isn't supported (SFML " + r.DeclaringType.Name + "." + r.Name + ")");
                        i.OpCode = OpCodes.Call; i.Operand = hook("FixMusic", target); calls++;
                    }
                    else if (r.Name.StartsWith("SteamHelper_") && r.DeclaringType.Name == "efl")
                    {
                        i.OpCode = OpCodes.Call; i.Operand = hook("SteamBridge", r.Name.Substring("SteamHelper_".Length)); steam++;
                    }
                }
            }
        }
        Expect("SFML music calls", calls, 10);
        if (steam == 0) throw new Exception("this game version isn't supported (no Steam calls found)");

        // GOG Galaxy (enu creates the Galaxy manager axn when Galaxy.dll is there): GalaxyBridge instead, which runs Galaxy
        // in the 32-bit helper (GalaxyCSharp's natives are 32-bit only).
        var galaxy = module.GetType("enu").Methods.Where(m => m.HasBody).SelectMany(m => m.Body.Instructions
            .Where(i => i.OpCode == OpCodes.Newobj && ((MethodReference)i.Operand).DeclaringType.Name == "axn")).ToList();
        Expect("Galaxy manager", galaxy.Count, 1);
        foreach (var i in galaxy) i.Operand = module.ImportReference(RuntimeType(fix, "GalaxyBridge").Methods.Single(m => m.IsConstructor && !m.IsStatic));

        // tb uses the game window as a WinForms Form: (Form)Control.FromHandle(Window.Handle), then Form members. The
        // handle read is dropped and the GameWindow goes to FnaWindow.Form; the Form members to FnaWindow.
        var tb = module.GetType("tb");
        var formMembers = new Dictionary<string, string> {
            { "set_StartPosition", "SetStartPosition" }, { "get_FormBorderStyle", "GetBorderStyle" }, { "set_FormBorderStyle", "SetBorderStyle" },
            { "get_Location", "GetLocation" }, { "set_Location", "SetLocation" }, { "get_Width", "GetWidth" }, { "get_Height", "GetHeight" } };
        int forms = 0;
        foreach (var m in tb.Methods.Where(m => m.HasBody))
        {
            var lookups = m.Body.Instructions.Where(i => i.Operand is MethodReference r && r.Name == "FromHandle" && r.DeclaringType.FullName == "System.Windows.Forms.Control").ToList();
            if (lookups.Count == 0) continue;
            foreach (var i in lookups)
            {
                var handle = i.Previous;
                var cast = i.Next;
                if (!(handle.Operand is MethodReference h && h.Name == "get_Handle" && h.DeclaringType.Name == "GameWindow") || cast.OpCode != OpCodes.Castclass)
                    throw new Exception("this game version isn't supported (window form in tb." + m.Name + ")");
                handle.OpCode = OpCodes.Nop; handle.Operand = null;
                cast.OpCode = OpCodes.Nop; cast.Operand = null;
                i.OpCode = OpCodes.Call; i.Operand = hook("FnaWindow", "Form");
                forms++;
            }
            foreach (var v in m.Body.Variables.Where(v => v.VariableType.FullName == "System.Windows.Forms.Form")) v.VariableType = module.TypeSystem.Object;
            foreach (var i in m.Body.Instructions)
            {
                var r = i.Operand as MethodReference;
                if (r == null || r.DeclaringType.Namespace != "System.Windows.Forms" || (r.DeclaringType.Name != "Form" && r.DeclaringType.Name != "Control")) continue;
                string target;
                if (!formMembers.TryGetValue(r.Name, out target)) throw new Exception("this game version isn't supported (window Form." + r.Name + " in tb." + m.Name + ")");
                i.OpCode = OpCodes.Call; i.Operand = hook("FnaWindow", target);
            }
        }
        Expect("window form lookups", forms, 4);
        // tb.lg(): the window handle bso passes to ClientToScreen (cursor warps); FNA's is an SDL window, not an HWND.
        var lg = tb.Methods.Single(m => m.Name == "lg" && m.Parameters.Count == 0 && m.ReturnType.MetadataType == MetadataType.IntPtr);
        foreach (var ret in lg.Body.Instructions.Where(i => i.OpCode == OpCodes.Ret).ToList())
            lg.Body.GetILProcessor().InsertBefore(ret, Instruction.Create(OpCodes.Call, hook("FnaWindow", "Hwnd")));

        // Saves name XNA types with the assembly that wrote them; e6e.av3(typeName, assembly, throw) resolves them.
        var resolve = module.GetType("e6e").Methods.Single(m => m.Name == "av3" && m.Parameters.Count == 3 && m.Parameters[0].ParameterType.MetadataType == MetadataType.String);
        var resolveIl = resolve.Body.GetILProcessor();
        var resolveStart = resolve.Body.Instructions[0];
        foreach (var p in resolve.Parameters.Take(2))
        {
            resolveIl.InsertBefore(resolveStart, Instruction.Create(OpCodes.Ldarg, p));
            resolveIl.InsertBefore(resolveStart, Instruction.Create(OpCodes.Call, hook("FnaMode", "SaveTypeName")));
            resolveIl.InsertBefore(resolveStart, Instruction.Create(OpCodes.Starg, p));
        }

        // ...and e6e.av5(type, out typeName, out assembly, bool) names them when saving: written as XNA names (SavedName).
        var name = module.GetType("e6e").Methods.Single(m => m.Name == "av5" && m.Parameters.Count == 4 && m.Parameters[1].ParameterType.IsByReference && m.Parameters[2].ParameterType.IsByReference);
        var nameIl = name.Body.GetILProcessor();
        foreach (var ret in name.Body.Instructions.Where(i => i.OpCode == OpCodes.Ret).ToList())
        {
            // As in AppendCall: the first inserted instruction takes the ret's place, so branches to it run the hook.
            nameIl.InsertAfter(ret, Instruction.Create(OpCodes.Ret));
            nameIl.InsertAfter(ret, Instruction.Create(OpCodes.Call, hook("FnaMode", "SavedName")));
            nameIl.InsertAfter(ret, Instruction.Create(OpCodes.Ldarg_3));
            nameIl.InsertAfter(ret, Instruction.Create(OpCodes.Ldarg_2));
            ret.OpCode = OpCodes.Ldarg_1; ret.Operand = null;
        }

        // be3 (the game's formatter) names types the binder leaves unnamed itself: type.Assembly.FullName, type.FullName.
        var writer = module.GetType("be3").Methods.Single(m => m.HasBody && m.Body.Instructions.Any(i => i.Operand is MethodReference r && r.Name == "BindToName"));
        int assemblies = 0, typeNames = 0;
        foreach (var i in writer.Body.Instructions)
        {
            var r = i.Operand as MethodReference;
            if (r == null) continue;
            if (r.Name == "get_FullName" && r.DeclaringType.FullName == "System.Reflection.Assembly" && i.Previous.Operand is MethodReference g && g.Name == "get_Assembly")
            {
                i.Previous.OpCode = OpCodes.Nop; i.Previous.Operand = null;
                i.OpCode = OpCodes.Call; i.Operand = hook("FnaMode", "WrittenAssembly"); assemblies++;
            }
            else if (r.Name == "get_FullName" && r.DeclaringType.FullName == "System.Type")
            {
                i.OpCode = OpCodes.Call; i.Operand = hook("FnaMode", "WrittenTypeName"); typeNames++;
            }
        }
        Expect("formatter assembly names", assemblies, 1);
        Expect("formatter type names", typeNames, 1);

        module.Attributes &= ~(ModuleAttributes.Required32Bit | ModuleAttributes.Preferred32Bit);
        ToFna(module, fix.AssemblyReferences.Single(r => r.Name == "FNA"));
    }

    // COMIMAGE_FLAGS_32BITREQUIRED / 32BITPREFERRED in a .NET PE's CLI header, cleared in place (no rewrite, so a
    // strong-name signature stays byte-identical otherwise; Mono doesn't check it, .NET skips it for full trust).
    static void ClearClr32Bit(byte[] pe)
    {
        int h = BitConverter.ToInt32(pe, 0x3C);
        int dirs = h + 24 + (BitConverter.ToUInt16(pe, h + 24) == 0x10b ? 96 : 112);
        int cliRva = BitConverter.ToInt32(pe, dirs + 14 * 8);
        int sections = BitConverter.ToUInt16(pe, h + 6), table = h + 24 + BitConverter.ToUInt16(pe, h + 20);
        for (int i = 0; i < sections; i++)
        {
            int s = table + i * 40, va = BitConverter.ToInt32(pe, s + 12), size = Math.Max(BitConverter.ToInt32(pe, s + 8), 1), raw = BitConverter.ToInt32(pe, s + 20);
            if (cliRva >= va && cliRva < va + size)
            {
                int flags = cliRva - va + raw + 16;
                Array.Copy(BitConverter.GetBytes(BitConverter.ToInt32(pe, flags) & ~0x20002), 0, pe, flags, 4);
                return;
            }
        }
        throw new Exception("no CLI header");
    }

    // The int an ldc.i4* instruction loads, or null.
    static int? Constant(Instruction i)
    {
        if (i == null) return null;
        if (i.OpCode == OpCodes.Ldc_I4 || i.OpCode == OpCodes.Ldc_I4_S) return Convert.ToInt32(i.Operand);
        int n = i.OpCode.Code - Code.Ldc_I4_0;
        return n >= 0 && n <= 8 ? n : (int?)null;
    }

    static int Coff(byte[] pe) { return BitConverter.ToInt32(pe, 0x3C) + 4 + 18; }

    // Calls hook(this) just before every return of type.name(), the instance void method without parameters.
    // The runtime's types the patch calls: Works' plumbing is in UnderRailWorks.Internal (FixStartup and FixVersion
    // stay global: older patched exes call FixStartup.Run by that name).
    static TypeDefinition RuntimeType(ModuleDefinition fix, string name)
    {
        return fix.GetType(name) ?? fix.GetType("UnderRailWorks.Internal." + name) ?? fix.GetType("UnderRailWorks." + name);
    }

    static void AppendCall(TypeDefinition type, string name, MethodReference hook)
    {
        var method = type.Methods.Single(m => m.Name == name && m.Parameters.Count == 0 && m.ReturnType.MetadataType == MetadataType.Void && !m.IsStatic);
        var il = method.Body.GetILProcessor();
        foreach (var ret in method.Body.Instructions.Where(i => i.OpCode == OpCodes.Ret).ToList())
        {
            // Branches into the old ret now land on the inserted call, since it takes the ret's place.
            il.InsertAfter(ret, Instruction.Create(OpCodes.Ret));
            il.InsertAfter(ret, Instruction.Create(OpCodes.Call, hook));
            ret.OpCode = OpCodes.Ldarg_0; ret.Operand = null;
        }
    }

    static void Replace(MethodDefinition m, Action<ILProcessor> emit)
    {
        var body = m.Body;
        body.Instructions.Clear();
        body.ExceptionHandlers.Clear();
        body.Variables.Clear();
        body.InitLocals = false;
        emit(body.GetILProcessor());
    }
}
