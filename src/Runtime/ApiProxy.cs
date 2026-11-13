// UnderRailWorks: another mod's API through an interface of your own (SMAPI's GetApi<T> does the same).
// Mods.Api<IMyView>("OtherMod") where IMyView is a public interface in *your* mod, listing the members of the other
// mod's API object you use (same names and types). Works builds an object implementing it that forwards each call, so
// neither mod references the other's DLL and the other mod can add members freely. Matching, per interface member:
// - methods by name and parameter types; properties as their get_/set_ methods; events as add_/remove_;
// - types must be the same (.NET, FNA, Works types: string, Character, Action<string>...), except enums: an enum
//   of your own is converted to the other mod's by number (as SMAPI does);
// - a member that has no match: no object at all (null), with one log line naming it, so a mismatch shows at once
//   instead of in the middle of a game. No ref/out parameters, no generic methods.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace UnderRailWorks.Internal
{
static class ApiProxy
{
    static int count;
    // (interface, the API object's type) -> how to make a proxy, or the problem.
    sealed class Recipe { internal Type Type; internal Func<object, object[], object>[] Calls; internal string Problem; }
    // Weak all the way down (interface, then the API's type; API object, then interface): neither mod's code is kept
    // alive by a cache when it is reloaded.
    static readonly ConditionalWeakTable<Type, ConditionalWeakTable<Type, Recipe>> recipes = new ConditionalWeakTable<Type, ConditionalWeakTable<Type, Recipe>>();
    static readonly ConditionalWeakTable<object, ConditionalWeakTable<Type, object>> made = new ConditionalWeakTable<object, ConditionalWeakTable<Type, object>>();
    static readonly object gate = new object();
    static readonly HashSet<string> told = new HashSet<string>();

    internal static T For<T>(object target, string modId) where T : class
    {
        lock (gate)
        {
            var cache = made.GetOrCreateValue(target);
            object p;
            if (cache.TryGetValue(typeof(T), out p)) return (T)p;
            var byTarget = recipes.GetOrCreateValue(typeof(T));
            Recipe r;
            if (!byTarget.TryGetValue(target.GetType(), out r))
            {
                try { r = Make(typeof(T), target.GetType()); }
                catch (Exception ex) { r = new Recipe { Problem = ex.GetType().Name + ": " + ex.Message }; }
                byTarget.Add(target.GetType(), r);
            }
            if (r.Problem != null)
            {
                if (told.Add(typeof(T).FullName + "|" + modId))
                    Log.Write("mods: " + modId + "'s API (" + target.GetType().Name + ") doesn't fit " + typeof(T).FullName + " (" + typeof(T).Assembly.GetName().Name + "): " + r.Problem);
                return null;
            }
            p = Activator.CreateInstance(r.Type, target, r.Calls);
            cache.Add(typeof(T), p);
            return (T)p;
        }
    }

    static IEnumerable<MethodInfo> Members(Type iface)
    {
        return new[] { iface }.Concat(iface.GetInterfaces()).SelectMany(i => i.GetMethods()).Distinct();
    }

    static Recipe Make(Type iface, Type target)
    {
        if (!iface.IsInterface) return new Recipe { Problem = "only an interface can be matched (or the API's own type)" };
        if (!iface.IsVisible) return new Recipe { Problem = "the interface must be public (nested ones in public types)" };
        if (iface.IsGenericTypeDefinition) return new Recipe { Problem = "an open generic interface" };
        var wanted = Members(iface).ToList();
        var offered = target.GetMethods(BindingFlags.Public | BindingFlags.Instance);
        var calls = new Func<object, object[], object>[wanted.Count];
        var problems = new List<string>();
        for (int i = 0; i < wanted.Count; i++)
        {
            var w = wanted[i];
            var ps = w.GetParameters();
            if (w.IsGenericMethodDefinition || ps.Any(x => x.ParameterType.IsByRef)) { problems.Add(Describe(w) + " (generic or ref/out: not supported)"); continue; }
            var found = offered.Where(o => o.Name == w.Name && !o.IsGenericMethodDefinition && Fits(o, w)).ToList();
            if (found.Count > 1) found = found.Where(o => o.GetParameters().Select(x => x.ParameterType).SequenceEqual(ps.Select(x => x.ParameterType))).ToList();
            if (found.Count != 1) { problems.Add(Describe(w) + (found.Count == 0 ? " is missing" : " matches more than one")); continue; }
            calls[i] = Invoker(found[0], w);
        }
        if (problems.Count > 0) return new Recipe { Problem = string.Join("; ", problems.ToArray()) };
        return new Recipe { Type = Emit(iface, wanted), Calls = calls };
    }

    static string Describe(MethodInfo m)
    {
        return m.ReturnType.Name + " " + m.Name + "(" + string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name).ToArray()) + ")";
    }

    // Same type, or both enums (converted by number); a result may also be anything assignable.
    static bool Same(Type theirs, Type mine) { return theirs == mine || theirs.IsEnum && mine.IsEnum; }
    static bool Fits(MethodInfo theirs, MethodInfo mine)
    {
        var a = theirs.GetParameters(); var b = mine.GetParameters();
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i].ParameterType.IsByRef || !Same(a[i].ParameterType, b[i].ParameterType)) return false;
        if (mine.ReturnType == typeof(void)) return theirs.ReturnType == typeof(void);
        return Same(theirs.ReturnType, mine.ReturnType) || mine.ReturnType.IsAssignableFrom(theirs.ReturnType) && theirs.ReturnType != typeof(void);
    }

    // value as type "to": an enum of another type by its number; anything else as it is.
    public static object Convert(object value, Type to)
    {
        if (value != null && to.IsEnum && value.GetType().IsEnum && value.GetType() != to) return Enum.ToObject(to, System.Convert.ToInt64(value));
        return value;
    }

    // (target, args) => (object)((Target)target).Method((P0)Convert(args[0], P0), ...), the result converted back.
    static Func<object, object[], object> Invoker(MethodInfo theirs, MethodInfo mine)
    {
        var t = Expression.Parameter(typeof(object), "target");
        var a = Expression.Parameter(typeof(object[]), "args");
        var conv = typeof(ApiProxy).GetMethod("Convert", BindingFlags.Public | BindingFlags.Static);
        var args = theirs.GetParameters().Select((p, i) => (Expression)Expression.Convert(
            Expression.Call(conv, Expression.ArrayIndex(a, Expression.Constant(i)), Expression.Constant(p.ParameterType, typeof(Type))), p.ParameterType)).ToArray();
        Expression call = Expression.Call(Expression.Convert(t, theirs.DeclaringType), theirs, args);
        if (theirs.ReturnType == typeof(void)) call = Expression.Block(call, Expression.Constant(null, typeof(object)));
        else call = Expression.Call(conv, Expression.Convert(call, typeof(object)), Expression.Constant(mine.ReturnType, typeof(Type)));
        return Expression.Lambda<Func<object, object[], object>>(call, t, a).Compile();
    }

    // public sealed class ApiProxyN : object, IFace { object target; Func<object, object[], object>[] calls; ... }
    static Type Emit(Type iface, List<MethodInfo> wanted)
    {
        // Collectible, one assembly per proxy: the interface may come from a hot-reloaded (collectible) mod, which a
        // non-collectible assembly may not reference.
        var name = "UnderRailWorks.ApiProxy" + (++count);
        var module = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(name), AssemblyBuilderAccess.RunAndCollect).DefineDynamicModule(name);
        var tb = module.DefineType("ApiProxy_" + iface.Name, TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class, typeof(object), new[] { iface });
        var fTarget = tb.DefineField("target", typeof(object), FieldAttributes.Private | FieldAttributes.InitOnly);
        var fCalls = tb.DefineField("calls", typeof(Func<object, object[], object>[]), FieldAttributes.Private | FieldAttributes.InitOnly);
        var ctor = tb.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, new[] { typeof(object), typeof(Func<object, object[], object>[]) });
        var il = ctor.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes));
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Stfld, fTarget);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Stfld, fCalls);
        il.Emit(OpCodes.Ret);
        var invoke = typeof(Func<object, object[], object>).GetMethod("Invoke");
        for (int i = 0; i < wanted.Count; i++)
        {
            var w = wanted[i];
            var ps = w.GetParameters().Select(p => p.ParameterType).ToArray();
            var mb = tb.DefineMethod(w.DeclaringType.Name + "." + w.Name, MethodAttributes.Private | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
                w.ReturnType, ps);
            il = mb.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, fCalls); il.Emit(OpCodes.Ldc_I4, i); il.Emit(OpCodes.Ldelem_Ref);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, fTarget);
            il.Emit(OpCodes.Ldc_I4, ps.Length); il.Emit(OpCodes.Newarr, typeof(object));
            for (int j = 0; j < ps.Length; j++)
            {
                il.Emit(OpCodes.Dup); il.Emit(OpCodes.Ldc_I4, j); il.Emit(OpCodes.Ldarg, (short)(j + 1));
                if (ps[j].IsValueType || ps[j].IsGenericParameter) il.Emit(OpCodes.Box, ps[j]);
                il.Emit(OpCodes.Stelem_Ref);
            }
            il.Emit(OpCodes.Callvirt, invoke);
            if (w.ReturnType == typeof(void)) il.Emit(OpCodes.Pop);
            else il.Emit(OpCodes.Unbox_Any, w.ReturnType);     // also a cast for reference types
            il.Emit(OpCodes.Ret);
            tb.DefineMethodOverride(mb, w);
        }
        return tb.CreateType();
    }
}
}
