// Build step: a reference copy of the game's exe for compiling Works and mods against. underrail.exe references
// Microsoft XNA 4.0; the game runs on our FNA (the patcher points it there), so this copy's XNA references are pointed
// at FNA.dll too. Works and mods then compile against it and FNA.dll directly and need no rewriting afterwards.
// Usage: mono Retarget.exe <underrail.exe> <FNA.dll> <out.exe>
using System;
using System.IO;
using System.Linq;
using Mono.Cecil;

static class Retarget
{
    static int Main(string[] args)
    {
        var fna = AssemblyNameReference.Parse(ModuleDefinition.ReadModule(args[1]).Assembly.FullName);
        var asm = AssemblyDefinition.ReadAssembly(new MemoryStream(File.ReadAllBytes(args[0])));
        var module = asm.MainModule;
        var reference = new AssemblyNameReference(fna.Name, fna.Version) { PublicKeyToken = fna.PublicKeyToken, Culture = fna.Culture };
        module.AssemblyReferences.Add(reference);
        foreach (var t in module.GetTypeReferences())
            if (t.Scope is AssemblyNameReference a && a.Name.StartsWith("Microsoft.Xna.Framework")) t.Scope = reference;
        foreach (var r in module.AssemblyReferences.Where(r => r.Name.StartsWith("Microsoft.Xna.Framework")).ToList()) module.AssemblyReferences.Remove(r);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[2])));
        asm.Write(args[2]);
        Console.WriteLine(Path.GetFileName(args[2]) + ": XNA references -> " + fna.FullName);
        return 0;
    }
}
