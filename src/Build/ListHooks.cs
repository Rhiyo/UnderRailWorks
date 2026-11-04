// Release check: lists the UnderrailFix hooks a patched underrail.exe calls, one "Class.Method" per line (sorted,
// unique), so tools/check-release.sh can compare them with the expected set.
// Usage: mono ListHooks.exe <patched underrail.exe>
using System;
using System.Linq;
using Mono.Cecil;

static class ListHooks
{
    static int Main(string[] args)
    {
        var module = ModuleDefinition.ReadModule(args[0]);
        var hooks = module.GetTypes().SelectMany(t => t.Methods).Where(m => m.HasBody).SelectMany(m => m.Body.Instructions)
            .Select(i => i.Operand as MethodReference).Where(r => r != null && (r.DeclaringType.Scope.Name == "UnderrailFix" || r.DeclaringType.Scope.Name == "UnderrailFix64"))
            .Select(r => r.DeclaringType.Name + "." + r.Name).Distinct().OrderBy(n => n, StringComparer.Ordinal);
        foreach (var h in hooks) Console.WriteLine(h);
        return 0;
    }
}
