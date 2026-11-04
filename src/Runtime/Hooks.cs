// UnderRailWorks: the one event model every area of the API uses (as tModLoader's hooks, adapted):
// - Hook<T>: every handler runs, in mod load order (requirements first), e.g. "a layer is about to be drawn".
// - ReplaceHook<T>: handlers run in load order until one returns true: that one has taken the step and the game's own
//   step and later handlers are skipped (e.g. "draw this layer yourself"; one replacement, never two drawings).
// A handler that throws is logged with its mod's name and keeps running; one that fails 10 times is removed (a broken
// per-frame handler would otherwise fill the log). The game and the other mods carry on (tModLoader lets such an
// exception through). With no handlers, the game runs as stock.
using System;
using System.Collections.Generic;

public abstract class HookBase<T> where T : class
{
    readonly string name;
    T[] handlers = new T[0];
    string[] owners = new string[0];
    readonly Dictionary<T, int> failures = new Dictionary<T, int>();
    const int MaxFailures = 10;
    internal HookBase(string name) { this.name = name; }

    public bool Any { get { return handlers.Length > 0; } }

    public void Add(WorksMod mod, T handler) { Add(mod.Id, handler); }
    // mod: the mod's id, for the log if the handler fails.
    public void Add(string mod, T handler)
    {
        var h = new List<T>(handlers) { handler };
        var o = new List<string>(owners) { mod };
        handlers = h.ToArray(); owners = o.ToArray();
    }

    public void Remove(T handler)
    {
        int i = Array.IndexOf(handlers, handler);
        if (i < 0) return;
        var h = new List<T>(handlers); var o = new List<string>(owners);
        h.RemoveAt(i); o.RemoveAt(i);
        handlers = h.ToArray(); owners = o.ToArray();
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
}

// The first handler that returns true replaces the game's step.
public sealed class ReplaceHook<T> : HookBase<T> where T : class
{
    internal ReplaceHook(string name) : base(name) { }
    internal bool Run(Func<T, bool> call) { return Each(call); }
}
