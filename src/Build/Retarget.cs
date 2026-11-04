// Build step: the runtime (UnderRailWorks.dll) is compiled against FNA (Wine Mono's WineMono.FNA). This points each of its XNA type
// references at the XNA 4.0 assembly the game references that type from, so the DLL binds the way underrail.exe
// does: through Wine Mono's XNA facades on Linux, or to Microsoft XNA on Windows.
// Usage: mono Retarget.exe <runtime.dll> <underrail.exe>
// mono Retarget.exe --fna <runtime.dll> <FNA.dll> <UnderRailWorks.dll> writes the runtime the game uses: assembly named
// after the output file, every XNA type reference pointed at FNA (which has them all, same names).
// mono Retarget.exe --fna-mod <mod.dll> <FNA.dll> <out.dll>: a mod likewise (its name kept; a reference to the 1.x
// runtime names UnderrailFix / UnderrailFix64 becomes UnderRailWorks).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;

static class Retarget
{
    static int Main(string[] args)
    {
        if (args[0] == "--fna") return ToFna(args[1], args[2], args[3]);
        if (args[0] == "--fna-mod") return ModToFna(args[1], args[2], args[3]);
        var game = ModuleDefinition.ReadModule(args[1]);
        var scopes = new Dictionary<string, AssemblyNameReference>();
        foreach (var t in game.GetTypeReferences())
            if (t.DeclaringType == null && t.Scope is AssemblyNameReference a && a.Name.StartsWith("Microsoft.Xna.Framework"))
                scopes[t.FullName] = a;
        var xna = game.AssemblyReferences.Where(a => a.Name.StartsWith("Microsoft.Xna.Framework")).ToDictionary(a => a.Name);

        var dll = ModuleDefinition.ReadModule(new MemoryStream(File.ReadAllBytes(args[0])));
        var missing = new List<string>();
        foreach (var t in dll.GetTypeReferences().ToList())
        {
            if (t.DeclaringType != null || !(t.Scope is AssemblyNameReference s) || !s.Name.StartsWith("WineMono.FNA")) continue;
            AssemblyNameReference target;
            if (!scopes.TryGetValue(t.FullName, out target))
            {
                // Not used by the game itself: XNA's own layout (Graphics types in .Graphics, the rest in the core assembly).
                string name = t.Namespace.StartsWith("Microsoft.Xna.Framework.Graphics") ? "Microsoft.Xna.Framework.Graphics" : "Microsoft.Xna.Framework";
                if (!xna.TryGetValue(name, out target)) { missing.Add(t.FullName); continue; }
            }
            var reference = dll.AssemblyReferences.FirstOrDefault(r => r.FullName == target.FullName);
            if (reference == null)
            {
                reference = new AssemblyNameReference(target.Name, target.Version) { PublicKeyToken = target.PublicKeyToken, Culture = target.Culture };
                dll.AssemblyReferences.Add(reference);
            }
            t.Scope = reference;
        }
        if (missing.Count > 0) { Console.Error.WriteLine("no XNA assembly for: " + string.Join(", ", missing)); return 1; }
        foreach (var r in dll.AssemblyReferences.Where(r => r.Name.StartsWith("WineMono.FNA")).ToList()) dll.AssemblyReferences.Remove(r);
        dll.Write(args[0]);
        Console.WriteLine("XNA references: " + string.Join(", ", dll.AssemblyReferences.Where(r => r.Name.StartsWith("Microsoft.Xna")).Select(r => r.Name)));
        return 0;
    }

    // A mod compiled like the runtime (against WineMono.FNA and the runtime): its XNA references pointed at our
    // FNA (mods run in the 64-bit mode only). Its own name is kept.
    static int ModToFna(string input, string fnaPath, string output)
    {
        var fna = AssemblyNameReference.Parse(ModuleDefinition.ReadModule(fnaPath).Assembly.FullName);
        var asm = AssemblyDefinition.ReadAssembly(new MemoryStream(File.ReadAllBytes(input)));
        var module = asm.MainModule;
        var reference = new AssemblyNameReference(fna.Name, fna.Version) { PublicKeyToken = fna.PublicKeyToken, Culture = fna.Culture };
        module.AssemblyReferences.Add(reference);
        foreach (var t in module.GetTypeReferences())
            if (t.Scope is AssemblyNameReference a && (a.Name.StartsWith("Microsoft.Xna.Framework") || a.Name.StartsWith("WineMono.FNA"))) t.Scope = reference;
        foreach (var r in module.AssemblyReferences.Where(r => r.Name.StartsWith("Microsoft.Xna.Framework") || r.Name.StartsWith("WineMono.FNA")).ToList()) module.AssemblyReferences.Remove(r);
        foreach (var r in module.AssemblyReferences.Where(r => r.Name == "UnderrailFix" || r.Name == "UnderrailFix64")) r.Name = "UnderRailWorks";
        asm.Write(output);
        Console.WriteLine(Path.GetFileName(output) + ": XNA references -> " + fna.FullName);
        return 0;
    }

    static int ToFna(string input, string fnaPath, string output)
    {
        var fna = AssemblyNameReference.Parse(ModuleDefinition.ReadModule(fnaPath).Assembly.FullName);
        var asm = AssemblyDefinition.ReadAssembly(new MemoryStream(File.ReadAllBytes(input)));
        asm.Name.Name = Path.GetFileNameWithoutExtension(output);
        asm.MainModule.Name = Path.GetFileName(output);
        var module = asm.MainModule;
        var reference = new AssemblyNameReference(fna.Name, fna.Version) { PublicKeyToken = fna.PublicKeyToken, Culture = fna.Culture };
        module.AssemblyReferences.Add(reference);
        foreach (var t in module.GetTypeReferences())
            if (t.Scope is AssemblyNameReference a && a.Name.StartsWith("Microsoft.Xna.Framework")) t.Scope = reference;
        foreach (var r in module.AssemblyReferences.Where(r => r.Name.StartsWith("Microsoft.Xna.Framework")).ToList()) module.AssemblyReferences.Remove(r);
        asm.Write(output);
        Console.WriteLine(Path.GetFileName(output) + ": XNA references -> " + fna.FullName);
        return 0;
    }
}
