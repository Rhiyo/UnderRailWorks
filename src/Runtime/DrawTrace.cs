// UnderRailWorks: works.drawtrace [frames]: the next frames of the world, step by step, into drawtrace.txt in the game
// folder: the pass's start, every layer in drawing order (its kind, what it belongs to, map position, place on screen,
// tint, and which mod coloured, routed or drew it), tile extras and marks, WorldDone, the post-processing effects and
// the outline pass. The way to see the real order in your own area with your own mods (docs/Drawing.md).
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Xna.Framework;

namespace UnderRailWorks.Internal
{
static class DrawTrace
{
    internal static bool On;
    static int wanted, frame, layer;
    static StringBuilder text;
    static string path;

    internal static string Start(int frames)
    {
        wanted = Math.Max(1, Math.Min(5, frames));
        frame = 0;
        text = new StringBuilder();
        path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "drawtrace.txt");
        text.AppendLine("# works.drawtrace: " + wanted + " frame(s), " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + ". docs/Drawing.md explains the steps.");
        pending = true;
        return "tracing the next " + wanted + " frame(s) into " + path;
    }
    static bool pending;

    // WorldStarting: a traced frame begins.
    internal static void FrameStarting()
    {
        if (!pending && !On) return;
        if (pending) { pending = false; On = true; }
        frame++; layer = 0;
        text.AppendLine();
        text.AppendLine("## frame " + frame);
    }
    internal static void Line(string s) { if (On) text.AppendLine(s); }
    // Lines from inside a router (a look it ran), written after the router's own line.
    static readonly List<string> held = new List<string>();
    internal static void Hold(string s) { if (On) held.Add(s); }
    internal static void Release() { if (!On) { held.Clear(); return; } foreach (var h in held) text.AppendLine(h); held.Clear(); }

    internal static void Layer(a2f l, bool shaderPass, string colouredBy)
    {
        if (!On) return;
        var h = new UnderRailWorks.Layer(l);
        var e = h.Entity;
        var c = e.IsNull ? null : e.Character;
        var at = e.IsNull ? Vector3.Zero : e.MapPosition;
        text.Append(shaderPass ? "    outline " : "  ").Append(++layer).Append(' ').Append(h.Kind)
            .Append(c != null ? " of " + c.Name : e.IsNull || Entities.Name(e.Game) == "" ? "" : " of " + Entities.Name(e.Game))
            .Append(e.IsNull ? "" : " at " + at.X.ToString("0.##") + "," + at.Y.ToString("0.##") + (at.Z != 0 ? "," + at.Z.ToString("0.##") : ""))
            .Append(" screen ").Append(h.Bounds.X).Append(',').Append(h.Bounds.Y).Append(' ').Append(h.Bounds.Width).Append('x').Append(h.Bounds.Height);
        var tint = h.Tint;
        if (tint.HasValue) text.Append(" tint ").Append(tint.Value.R).Append('/').Append(tint.Value.G).Append('/').Append(tint.Value.B).Append('/').Append(tint.Value.A);
        if (colouredBy != null) text.Append(" coloured by ").Append(colouredBy);
        if (!h.Drawable) text.Append(" (not drawn: hidden)");
        text.AppendLine();
    }

    // AfterPass (the outline pass's end, the last world step): a traced frame ends.
    internal static void FrameDone()
    {
        if (!On) return;
        text.AppendLine("outline pass done; the interface is drawn next");
        if (frame < wanted) return;
        On = false;
        try { File.WriteAllText(path, text.ToString()); Log.Always("drawtrace: " + frame + " frame(s) written to " + path); }
        catch (Exception ex) { Log.Always("drawtrace: couldn't write " + path + ": " + ex.Message); }
        text = null;
    }
}
}
