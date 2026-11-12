// UnderRailWorks on modern .NET (the .NET 10 launcher, src/Launcher): what the game's BinaryFormatter data needs
// that .NET Framework had. Saves and config.dat must stay readable by the stock game (AGENTS.md):
// - System.Random isn't serializable on modern .NET: a surrogate reads and writes it in .NET Framework's format
//   (inext, inextp, SeedArray; a seeded Random on modern .NET runs the same algorithm, so the state carries over).
// - Type names are written with modern assembly names (System.Private.CoreLib, System.Collections...): FrameworkNames
//   turns them back into the .NET Framework ones (the types' TypeForwardedFrom, else mscorlib), as FnaMode does
//   for FNA -> XNA.
using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using UnderRailWorks;

namespace UnderRailWorks.Internal
{

public static class CoreCompat
{
    public static readonly bool Active = Environment.Version.Major >= 5;
    internal const string Mscorlib = "mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089";

    internal static void Startup()
    {
        if (!Active) return;
        try
        {
            // Every BinaryFormatter the game makes gets the surrogates (the game makes its own in eoq / ctc).
            var formatter = Type.GetType("System.Runtime.Serialization.Formatters.Binary.BinaryFormatter, System.Runtime.Serialization.Formatters", true);
            var harmony = new HarmonyLib.Harmony("UnderRailWorks.CoreCompat");
            var postfix = new HarmonyLib.HarmonyMethod(typeof(CoreCompat).GetMethod("Made", BindingFlags.Static | BindingFlags.NonPublic));
            foreach (var ctor in formatter.GetConstructors()) harmony.Patch(ctor, postfix: postfix);
            Log.Write("modern .NET " + Environment.Version + ": BinaryFormatter surrogates (Random) and .NET Framework type names on");
        }
        catch (Exception ex) { Log.Always("modern .NET compatibility failed: " + ex); }
    }

    static void Made(object __instance)
    { try {
        var f = (IFormatter)__instance;
        if (!(f.SurrogateSelector is Selector)) f.SurrogateSelector = new Selector(f.SurrogateSelector);
    } catch (Exception ex_) { PatchGuard.Failed("CoreCompat.Made", ex_); }
    }

    sealed class Selector : ISurrogateSelector
    {
        ISurrogateSelector next;
        public Selector(ISurrogateSelector next) { this.next = next; }
        public void ChainSelector(ISurrogateSelector selector) { next = selector; }
        public ISurrogateSelector GetNextSelector() { return next; }
        public ISerializationSurrogate GetSurrogate(Type type, StreamingContext context, out ISurrogateSelector selector)
        {
            if (type == typeof(Random)) { selector = this; return RandomSurrogate.Instance; }
            if (next != null) return next.GetSurrogate(type, context, out selector);
            selector = null;
            return null;
        }
    }

    // .NET Framework's Random: SeedArray (56 ints), inext, inextp. On modern .NET a seeded Random keeps the same in
    // _impl (CompatSeedImpl) ._prng (CompatPrng) ._seedArray/_inext/_inextp; an unseeded one (XoshiroImpl) has no such
    // state, so it is written as a fresh compatible state seeded from it.
    sealed class RandomSurrogate : ISerializationSurrogate
    {
        internal static readonly RandomSurrogate Instance = new RandomSurrogate();
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        static readonly FieldInfo Impl = typeof(Random).GetField("_impl", Any);

        static FieldInfo Prng(object impl) { return impl == null ? null : impl.GetType().GetField("_prng", Any); }

        public void GetObjectData(object obj, SerializationInfo info, StreamingContext context)
        {
            var random = (Random)obj;
            object impl = Impl.GetValue(random);
            var prngField = Prng(impl);
            if (prngField == null) { impl = Impl.GetValue(new Random(random.Next())); prngField = Prng(impl); }
            object prng = prngField.GetValue(impl);
            var t = prng.GetType();
            info.FullTypeName = "System.Random";
            info.AssemblyName = Mscorlib;
            info.AddValue("inext", (int)t.GetField("_inext", Any).GetValue(prng));
            info.AddValue("inextp", (int)t.GetField("_inextp", Any).GetValue(prng));
            info.AddValue("SeedArray", ((int[])t.GetField("_seedArray", Any).GetValue(prng)).Clone());
        }

        public object SetObjectData(object obj, SerializationInfo info, StreamingContext context, ISurrogateSelector selector)
        {
            var random = new Random(0);     // seeded: the .NET Framework algorithm
            object impl = Impl.GetValue(random);
            var prngField = Prng(impl);
            object prng = prngField.GetValue(impl);     // a boxed copy of the struct
            var t = prng.GetType();
            t.GetField("_inext", Any).SetValue(prng, info.GetInt32("inext"));
            t.GetField("_inextp", Any).SetValue(prng, info.GetInt32("inextp"));
            t.GetField("_seedArray", Any).SetValue(prng, (int[])info.GetValue("SeedArray", typeof(int[])));
            prngField.SetValue(impl, prng);
            return random;
        }
    }
}

// .NET Framework names for types, as the stock game writes them: modern .NET's assemblies (System.Private.CoreLib,
// System.Collections...) become the .NET Framework assembly the type came from (its TypeForwardedFrom attribute, or
// mscorlib for core types), inside generic arguments too.
public static class FrameworkNames
{
    static readonly Assembly CoreLib = typeof(object).Assembly;
    static readonly Regex Modern = new Regex(@"([\w.`+]+), ((?:System|Microsoft)[\w.]*), Version=(\d+)\.[\d.]+, Culture=neutral, PublicKeyToken=(\w+)");

    // The assembly name to write for a type on modern .NET (null: not a .NET type, leave it to the caller).
    public static string Assembly(Type t)
    {
        if (!CoreCompat.Active) return null;
        while (t.IsArray) t = t.GetElementType();
        var def = t.IsGenericType ? t.GetGenericTypeDefinition() : t;
        var forwarded = def.GetCustomAttribute<TypeForwardedFromAttribute>();
        for (var outer = def.DeclaringType; forwarded == null && outer != null; outer = outer.DeclaringType)
            forwarded = (outer.IsGenericType ? outer.GetGenericTypeDefinition() : outer).GetCustomAttribute<TypeForwardedFromAttribute>();
        if (forwarded != null) return forwarded.AssemblyFullName;
        if (def.Assembly == CoreLib) return CoreCompat.Mscorlib;
        return null;
    }

    // A type's full name with each generic argument named as written (assembly through name).
    public static string Name(Type t, Func<Type, string> assembly)
    {
        if (t.IsArray) return Name(t.GetElementType(), assembly) + "[" + new string(',', t.GetArrayRank() - 1) + "]";
        if (t.IsGenericType && !t.IsGenericTypeDefinition)
            return t.GetGenericTypeDefinition().FullName + "[" + string.Join(",", t.GetGenericArguments().Select(a => "[" + Name(a, assembly) + ", " + assembly(a) + "]").ToArray()) + "]";
        return t.FullName;
    }

    // Reading: names .NET 10 builds of Works wrote by mistake (before the fix in FnaMode.SavedName), as the type the
    // object meant to be written as.
    const string StringComparerName = "System.Collections.Generic.GenericEqualityComparer`1[[System.String, " + CoreCompat.Mscorlib + "]]";
    public static string Read(string name)
    {
        if (name == "System.Collections.Generic.StringEqualityComparer" || (name.StartsWith("System.Collections.Generic.NonRandomizedStringEqualityComparer") && !name.Contains(",")))
            return StringComparerName;
        return name;
    }

    // A name string from elsewhere (the game's binder): each "Type, ModernAssembly, Version=..." pair (no brackets in
    // the type) with the .NET Framework assembly instead. Unknown names are left as they are.
    public static string Fix(string name)
    {
        if (!CoreCompat.Active || name == null) return name;
        return Modern.Replace(name, m =>
        {
            if (int.Parse(m.Groups[3].Value) < 5) return m.Value;     // already a .NET Framework name
            var type = Type.GetType(m.Groups[1].Value + ", " + m.Groups[2].Value, false);
            string a = type == null ? null : Assembly(type);
            return a == null ? m.Value : m.Groups[1].Value + ", " + a;
        });
    }
}
}
