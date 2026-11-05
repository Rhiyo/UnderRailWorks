// UnderRailWorks: the one event model every area of the API uses (as tModLoader's hooks, adapted):
// - Hook<T>: every handler runs, in mod load order (requirements first), e.g. "a layer is about to be drawn".
// - ReplaceHook<T>: handlers run in load order until one returns true: that one has taken the step and the game's own
//   step and later handlers are skipped (e.g. "draw this layer yourself"; one replacement, never two drawings).
// A handler that throws is logged with its mod's name and keeps running; one that fails 10 times is removed (a broken
// per-frame handler would otherwise fill the log). The game and the other mods carry on (tModLoader lets such an
// exception through). With no handlers, the game runs as stock.
using System;
using System.Collections.Generic;
using UnderRailWorks.Internal;

namespace UnderRailWorks
{

// Where a handler runs among a hook's handlers (tModLoader runs by load order only; BepInEx/Harmony have priorities).
public enum HookOrder { First = 0, Early = 1, Normal = 2, Late = 3, Last = 4 }

public abstract class HookBase<T> where T : class
{
    readonly string name;
    T[] handlers = new T[0];
    string[] owners = new string[0];
    int[] orders = new int[0];
    readonly Dictionary<T, int> failures = new Dictionary<T, int>();
    const int MaxFailures = 10;
    internal HookBase(string name) { this.name = name; }

    public bool Any { get { return handlers.Length > 0; } }

    public void Add(WorksMod mod, T handler) { Add(mod.Id, handler, HookOrder.Normal); }
    // order: where among the handlers this one runs (First before the rest...); the same order keeps load order.
    public void Add(WorksMod mod, T handler, HookOrder order) { Add(mod.Id, handler, order); }
    // mod: the mod's id, for the log if the handler fails.
    public void Add(string mod, T handler) { Add(mod, handler, HookOrder.Normal); }
    public void Add(string mod, T handler, HookOrder order)
    {
        int at = 0;
        while (at < orders.Length && orders[at] <= (int)order) at++;     // after every handler of the same or an earlier order
        var h = new List<T>(handlers); h.Insert(at, handler);
        var o = new List<string>(owners); o.Insert(at, mod);
        var r = new List<int>(orders); r.Insert(at, (int)order);
        handlers = h.ToArray(); owners = o.ToArray(); orders = r.ToArray();
    }

    public void Remove(T handler)
    {
        int i = Array.IndexOf(handlers, handler);
        if (i < 0) return;
        var h = new List<T>(handlers); var o = new List<string>(owners); var r = new List<int>(orders);
        h.RemoveAt(i); o.RemoveAt(i); r.RemoveAt(i);
        handlers = h.ToArray(); owners = o.ToArray(); orders = r.ToArray();
    }

    // Each handler in turn until call returns true; a throwing handler is removed.
    protected bool Each(Func<T, bool> call)
    {
        var list = handlers;     // a snapshot: handlers may be removed while running
        for (int i = 0; i < list.Length; i++)
        {
            try { if (call(list[i])) return true; }
            catch (Exception ex)
            {
                string mod = i < owners.Length ? owners[i] : "?";
                ModErrors.Report(mod, name, ex);
                int n; failures.TryGetValue(list[i], out n); failures[list[i]] = ++n;
                if (n >= MaxFailures) { Remove(list[i]); failures.Remove(list[i]); Log.Write("mods: " + mod + ": its " + name + " handler failed " + n + " times and was removed: " + ex.Message); }
                else if (n <= 3) Log.Write("mods: " + mod + ": its " + name + " handler failed (" + n + "): " + ex);
            }
        }
        return false;
    }
}

// Every handler runs.
public sealed class Hook<T> : HookBase<T> where T : class
{
    internal Hook(string name) : base(name) { }
    internal void Run(Action<T> call) { Each(h => { call(h); return false; }); }
    // Handlers in turn until one returns true (for "first answer wins" hooks).
    internal bool RunUntil(Func<T, bool> call) { return Each(call); }
}

// The first handler that returns true replaces the game's step.
public sealed class ReplaceHook<T> : HookBase<T> where T : class
{
    internal ReplaceHook(string name) : base(name) { }
    internal bool Run(Func<T, bool> call) { return Each(call); }
}
}
