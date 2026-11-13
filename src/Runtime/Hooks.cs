// UnderRailWorks: the one event model every area of the API uses (as tModLoader's hooks, adapted):
// - Hook<T>: every handler runs, in mod load order (requirements first), e.g. "a layer is about to be drawn".
// - ReplaceHook<T> (and "first answer" hooks, RunUntil): handlers are asked from the last in load order back to the
//   first until one returns true: that one has taken the step, and the game's own step and the rest are skipped
//   (e.g. "draw this layer yourself"; one replacement, never two drawings).
// One rule for players and mod authors: the mod lower in the load order (Options -> Mods, top to bottom) has the final
// say. With Hook<T> that's because it runs last (a value it sets stays); with ReplaceHook<T> because it's asked first.
// HookOrder moves a handler within that: Last = the final say, First = the first to run / the last to be asked.
// A handler that throws is logged with its mod's name and keeps running; one that fails 10 times is removed (a broken
// per-frame handler would otherwise fill the log). The game and the other mods carry on (tModLoader lets such an
// exception through). With no handlers, the game runs as stock.
using System;
using System.Collections.Generic;
using System.Linq;
using UnderRailWorks.Internal;

namespace UnderRailWorks
{

// Where a handler runs among a hook's handlers (tModLoader runs by load order only; BepInEx/Harmony have priorities).
public enum HookOrder { First = 0, Early = 1, Normal = 2, Late = 3, Last = 4 }

public abstract class HookBase<T> : IModOwned, HookOwner where T : class
{
    void HookOwner.SetOnFailure(Action<string> onFailure) { OnFailure = onFailure; }
    readonly string name;
    internal T[] handlers = new T[0];
    string[] owners = new string[0];
    int[] orders = new int[0];
    readonly Dictionary<T, int> failures = new Dictionary<T, int>();
    // Each handlers array's owners: a run keeps its snapshot even when a handler adds or removes one meanwhile, and
    // still knows whose each handler is (its interface, its errors).
    readonly System.Runtime.CompilerServices.ConditionalWeakTable<T[], string[]> ownersOf = new System.Runtime.CompilerServices.ConditionalWeakTable<T[], string[]>();
    void Set(T[] h, string[] o, int[] r)
    {
        // An empty array may be the runtime's shared one (Array.Empty): it has no owners to look up anyway.
        if (h.Length > 0) { ownersOf.Remove(h); ownersOf.Add(h, o); }
        owners = o; orders = r; handlers = h;
    }
    const int MaxFailures = 10;
    internal HookBase(string name) { this.name = name; lock (ModScope.Hooks) ModScope.Hooks.Add(this); }

    // Reloading a mod: its handlers go (the new code adds its own).
    void IModOwned.RemoveOwner(string mod)
    {
        var keep = Enumerable.Range(0, handlers.Length).Where(i => owners[i] != mod).ToList();
        if (keep.Count == handlers.Length) return;
        foreach (var i in Enumerable.Range(0, handlers.Length).Where(i => owners[i] == mod)) failures.Remove(handlers[i]);     // nothing keeps the old code alive
        var h = keep.Select(i => handlers[i]).ToArray(); var o = keep.Select(i => owners[i]).ToArray(); var r = keep.Select(i => orders[i]).ToArray();
        Set(h, o, r);
    }
    // One mod's handlers only (a reloaded mod catching up: GameStarted, ZoneLoaded...).
    internal void RunFor(string mod, Action<T> call)
    {
        var list = handlers; var o = owners;
        for (int i = 0; i < list.Length && i < o.Length; i++)
        {
            if (o[i] != mod) continue;
            var was = ModScope.Current; ModScope.Current = mod;     // what it adds is its own (removed at its next reload)
            try { call(list[i]); } catch (Exception ex) { Failed(list, i, ex); }
            finally { ModScope.Current = was; }
        }
    }
    // While a handler runs: its mod (what it adds to the interface is its own, removed if the mod is reloaded).
    internal string OwnerAt(T[] list, int i) { string[] o; return list != null && ownersOf.TryGetValue(list, out o) && i < o.Length ? o[i] : null; }

    public bool Any { get { return handlers.Length > 0; } }

    public void Add(WorksMod mod, T handler) { Add(mod.Id, handler, HookOrder.Normal); }
    // order: where among the handlers this one runs (First before the rest...); the same order keeps load order.
    public void Add(WorksMod mod, T handler, HookOrder order) { Add(mod.Id, handler, order); }
    // mod: the mod's id, for the log if the handler fails.
    internal void Add(string mod, T handler) { Add(mod, handler, HookOrder.Normal); }
    internal void Add(string mod, T handler, HookOrder order)
    {
        // After every handler of an earlier order, and of the same order from a mod earlier in the load order (so a
        // reloaded mod's handlers go back to where they were, not to the end).
        int at = 0, rank = Rank(mod);
        while (at < orders.Length && (orders[at] < (int)order || (orders[at] == (int)order && Rank(owners[at]) <= rank))) at++;
        var h = new List<T>(handlers); h.Insert(at, handler);
        var o = new List<string>(owners); o.Insert(at, mod);
        var r = new List<int>(orders); r.Insert(at, (int)order);
        Set(h.ToArray(), o.ToArray(), r.ToArray());
    }

    static int Rank(string mod) { var m = mod == null ? null : ModLoader.Find(mod); return m == null ? -1 : m.LoadIndex; }

    public void Remove(T handler)
    {
        int i = Array.IndexOf(handlers, handler);
        if (i < 0) return;
        var h = new List<T>(handlers); var o = new List<string>(owners); var r = new List<int>(orders);
        h.RemoveAt(i); o.RemoveAt(i); r.RemoveAt(i);
        Set(h.ToArray(), o.ToArray(), r.ToArray());
    }

    // Each handler in turn until call returns true; a throwing handler is removed.
    internal bool Each(Func<T, bool> call)
    {
        var list = handlers;     // a snapshot: handlers may be removed while running
        for (int i = list.Length - 1; i >= 0; i--)     // from the last: the lower mod in the load order has the final say
        {
            long started = Profiler.On ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
            var was = ModScope.Current; ModScope.Current = OwnerAt(list, i);
            try { if (call(list[i])) return true; }
            catch (Exception ex) { Failed(list, i, ex); }
            finally { ModScope.Current = was; if (started != 0) Profiled(i, started); }
        }
        return false;
    }

    // For hot call sites (every world layer) that loop over the handlers themselves, without a closure per call:
    //   var list = hook.Snapshot; for (...) { long t = hook.Starting(); try { list[i](a, b); } catch (Exception ex) { hook.Failed(list, i, ex); } finally { hook.Done(i, t); } }
    internal T[] Snapshot { get { return handlers; } }
    internal long Starting() { return Profiler.On ? System.Diagnostics.Stopwatch.GetTimestamp() : 0; }
    internal void Done(int i, long started) { if (started != 0) Profiled(i, started); }
    void Profiled(int i, long started) { var o = owners; Profiler.Add(i < o.Length ? o[i] : "?", name, System.Diagnostics.Stopwatch.GetTimestamp() - started); }

    // Set for hooks where a failed handler can't be left running (drawing: the game's drawing state may be half
    // changed): called with the mod's id instead of counting to MaxFailures.
    internal Action<string> OnFailure;

    internal void Failed(T[] list, int i, Exception ex)
    {
        string mod = OwnerAt(list, i) ?? OwnerOf(list[i]);
        ModErrors.Report(mod, name, ex);
        if (OnFailure != null) { Log.Write("mods: " + mod + ": its " + name + " handler failed: " + ex); OnFailure(mod); return; }
        int n; failures.TryGetValue(list[i], out n); failures[list[i]] = ++n;
        if (n >= MaxFailures) { Remove(list[i]); failures.Remove(list[i]); Log.Write("mods: " + mod + ": its " + name + " handler failed " + n + " times and was removed: " + ex.Message); }
        else if (n <= 3) Log.Write("mods: " + mod + ": its " + name + " handler failed (" + n + "): " + ex);
    }
    string OwnerOf(T handler) { var h = handlers; for (int k = 0; k < h.Length; k++) if (h[k] == handler) return owners[k]; return "?"; }
}

// Every handler runs.
public sealed class Hook<T> : HookBase<T> where T : class
{
    internal Hook(string name) : base(name) { }
    internal void Run(Action<T> call)
    {
        var list = handlers;
        for (int i = 0; i < list.Length; i++)
        {
            long started = Starting();
            var was = ModScope.Current; ModScope.Current = OwnerAt(list, i);
            try { call(list[i]); }
            catch (Exception ex) { Failed(list, i, ex); }
            finally { ModScope.Current = was; Done(i, started); }
        }
    }
    // Handlers until one returns true, from the last in load order (for "the final say wins" hooks).
    internal bool RunUntil(Func<T, bool> call) { return Each(call); }
}

// The first handler that returns true replaces the game's step.
public sealed class ReplaceHook<T> : HookBase<T> where T : class
{
    internal ReplaceHook(string name) : base(name) { }
    internal bool Run(Func<T, bool> call) { return Each(call); }
}
}

namespace UnderRailWorks.Internal
{
    interface HookOwner { void SetOnFailure(Action<string> onFailure); }

    // works.perf: time spent in each mod's handlers per hook, while switched on.
    static class Profiler
    {
        internal static bool On;
        static double until, elapsed;
        static readonly Dictionary<string, long[]> totals = new Dictionary<string, long[]>();     // "mod|hook" -> ticks, calls
        internal static void Add(string mod, string hook, long ticks)
        {
            long[] t;
            var key = mod + "|" + hook;
            if (!totals.TryGetValue(key, out t)) totals[key] = t = new long[2];
            t[0] += ticks; t[1]++;
        }
        static long allocStart; static int gc0Start, gc1Start, gc2Start;
        // .NET's GC.GetTotalAllocatedBytes (not in the Mono reference assemblies the build compiles against).
        static readonly System.Reflection.MethodInfo TotalAllocated = typeof(GC).GetMethod("GetTotalAllocatedBytes", new[] { typeof(bool) });
        static long Allocated() { return TotalAllocated == null ? 0 : (long)TotalAllocated.Invoke(null, new object[] { false }); }
        internal static void Start(double seconds)
        {
            totals.Clear(); elapsed = 0; until = seconds; On = true;
            allocStart = Allocated(); gc0Start = GC.CollectionCount(0); gc1Start = GC.CollectionCount(1); gc2Start = GC.CollectionCount(2);
        }
        internal static void Tick(double seconds)
        {
            if (!On) return;
            elapsed += seconds;
            if (elapsed < until) return;
            On = false;
            var freq = (double)System.Diagnostics.Stopwatch.Frequency;
            var rows = totals.OrderByDescending(kv => kv.Value[0]).Take(15).Select(kv =>
                kv.Key.Replace("|", ": ") + "  " + (kv.Value[0] * 1000.0 / freq / Math.Max(0.001, elapsed)).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " ms/s, "
                + kv.Value[1] + " calls").ToList();
            var byMod = totals.GroupBy(kv => kv.Key.Split('|')[0]).Select(g => new { Mod = g.Key, Ms = g.Sum(kv => kv.Value[0]) * 1000.0 / freq / Math.Max(0.001, elapsed) })
                .OrderByDescending(x => x.Ms).Select(x => x.Mod + " " + x.Ms.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " ms/s").ToList();
            Log.Always("perf: " + elapsed.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " s of game; by mod: " + string.Join(", ", byMod));
            Log.Always("perf: allocated " + ((Allocated() - allocStart) / 1048576.0 / Math.Max(0.001, elapsed)).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)
                + " MB/s (whole process), collections gen0 " + (GC.CollectionCount(0) - gc0Start) + ", gen1 " + (GC.CollectionCount(1) - gc1Start) + ", gen2 " + (GC.CollectionCount(2) - gc2Start));
            foreach (var r in rows) Log.Always("perf:   " + r);
            Commands.Print("works.perf: " + string.Join("; ", byMod));
        }
    }
}
