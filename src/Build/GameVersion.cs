// Release step: prints the game version an underrail.exe shows on its main menu ("Version: x.y.z.w"), read from the
// exe itself: the static System.Version field set with new Version(a, b, c, d) in a static constructor (dm9.d in
// UnderRail 1.3.1.2; the obfuscated name can change, so it is found by shape, and it must be the only one).
// Usage: mono GameVersion.exe <underrail.exe>
using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

static class GameVersion
{
    static int Main(string[] args)
    {
        var module = ModuleDefinition.ReadModule(args[0]);
        var found = module.GetTypes().Select(t => t.Methods.FirstOrDefault(m => m.IsConstructor && m.IsStatic && m.HasBody)).Where(m => m != null)
            .SelectMany(cctor => cctor.Body.Instructions.Where(i => i.OpCode == OpCodes.Newobj && i.Operand is MethodReference r
                && r.DeclaringType.FullName == "System.Version" && r.Parameters.Count == 4
                && i.Next != null && i.Next.OpCode == OpCodes.Stsfld)
                .Select(i => string.Join(".", Enumerable.Range(1, 4).Select(k => Constant(Back(i, 5 - k)))))).Distinct().ToList();
        if (found.Count != 1 || found[0].Contains("?")) { Console.Error.WriteLine("game version not found unambiguously: " + string.Join(", ", found)); return 1; }
        Console.WriteLine(found[0]);
        return 0;
    }

    static Instruction Back(Instruction i, int n) { for (; n > 0 && i != null; n--) i = i.Previous; return i; }

    static string Constant(Instruction i)
    {
        if (i == null) return "?";
        switch (i.OpCode.Code)
        {
            case Code.Ldc_I4_0: return "0"; case Code.Ldc_I4_1: return "1"; case Code.Ldc_I4_2: return "2";
            case Code.Ldc_I4_3: return "3"; case Code.Ldc_I4_4: return "4"; case Code.Ldc_I4_5: return "5";
            case Code.Ldc_I4_6: return "6"; case Code.Ldc_I4_7: return "7"; case Code.Ldc_I4_8: return "8";
            case Code.Ldc_I4_S: return ((sbyte)i.Operand).ToString();
            case Code.Ldc_I4: return ((int)i.Operand).ToString();
            default: return "?";
        }
    }
}
