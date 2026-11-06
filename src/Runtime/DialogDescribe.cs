// UnderRailWorks: a readable outline of one of the game's conversations, for mod authors deciding where to patch one
// (Dialogs.Patch) and why an option shows or not. Every node once, in the order reached from the start; later
// references say "-> #n". Conditions and effects are shown by their data tag and fields (e.g.
// `PHESC{E=MainFloorDebris, V=cleared}` the player-house element test, `BC{P=context.npc_x_met, V=True}`).
//   works.dialog characters\cc_jyles     (writes UnderRailWorks-dialog-characters_cc_jyles.txt in the game folder)
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace UnderRailWorks
{
public static partial class Dialogs
{
    public static string Describe(string dialog)
    {
        var model = bpo.GetServiceOrThrow<eve>().y3(dialog);
        if (model == null) return null;
        aji.a(model, dialog);
        var sb = new StringBuilder();
        sb.Append("Dialog ").Append(dialog).Append(model.c() ? "" : " (Escape doesn't close it)").Append('\n');
        var ids = new Dictionary<d55, int>();
        Describe(sb, model.b(), 0, ids);
        return sb.ToString();
    }

    // Writes the outline into the game folder; returns the file's path.
    public static string WriteDescription(string dialog)
    {
        var text = Describe(dialog);
        if (text == null) return null;
        var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "UnderRailWorks-dialog-" + dialog.Replace('\\', '_').Replace('/', '_') + ".txt");
        File.WriteAllText(path, text);
        return path;
    }

    static void Describe(StringBuilder sb, d55 node, int depth, Dictionary<d55, int> ids)
    {
        if (node == null) return;
        var pad = new string(' ', depth * 2);
        int id;
        if (ids.TryGetValue(node, out id)) { sb.Append(pad).Append("-> #").Append(id).Append(' ').Append(Short(node)).Append('\n'); return; }
        ids[node] = id = ids.Count + 1;
        string kind = node is ad7 ? "NPC" : node is atr ? "PLAYER" : node is dzc ? "TEXT" : "JUNCTION";
        sb.Append(pad).Append('#').Append(id).Append(' ').Append(kind);
        if (node.CausingCondition != null) sb.Append(" [if ").Append(Value(node.CausingCondition, 0)).Append(']');
        var answer = node as atr;
        if (answer != null && answer.b() != null) sb.Append(" [greyed if ").Append(Value(answer.b(), 0)).Append(']');
        var story = node as dzc;
        if (story != null)
        {
            sb.Append(": ").Append(OneLine(story.g()));
            if (story.e() != null && story.e().Count > 0) sb.Append(" {does ").Append(string.Join("; ", story.e().Select(a => Value(a, 0)))).Append('}');
        }
        sb.Append('\n');
        var q = node as ad7;
        if (q != null) foreach (var a in q.PossibleAnswers) Describe(sb, a, depth + 1, ids);
        foreach (var next in node.PossibleNextSteps) Describe(sb, next, depth + 1, ids);
    }

    static string Short(d55 node) { var s = node as dzc; return s == null ? "" : "(" + Trim(OneLine(s.g()), 50) + ")"; }
    static string OneLine(string s) { return (s ?? "").Replace('\n', ' ').Replace('\r', ' '); }
    static string Trim(string s, int n) { return s.Length <= n ? s : s.Substring(0, n) + "..."; }

    // A condition or action: its data tag and its fields; nested conditions inside.
    static string Value(object o, int depth)
    {
        if (o == null) return "null";
        if (o is string) return (string)o;
        var t = o.GetType();
        if (t.IsPrimitive || t.IsEnum || o is Guid || o is TimeSpan) return o.ToString();
        if (depth > 4) return "...";
        if (o is IEnumerable) return "[" + string.Join(", ", ((IEnumerable)o).Cast<object>().Select(x => Value(x, depth + 1))) + "]";
        var tag = t.GetCustomAttributes(typeof(eiz), false).FirstOrDefault() as eiz;
        var name = tag != null ? tag.a() : t.Name;
        var fields = new List<string>();
        for (var ty = t; ty != null && ty != typeof(object); ty = ty.BaseType)
            foreach (var f in ty.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
            {
                if (f.IsNotSerialized || typeof(Delegate).IsAssignableFrom(f.FieldType)) continue;
                object v; try { v = f.GetValue(o); } catch { continue; }
                if (v == null) continue;
                fields.Add(f.Name.Replace("m_", "") + "=" + Value(v, depth + 1));
            }
        return name + "{" + string.Join(", ", fields) + "}";
    }
}
}
