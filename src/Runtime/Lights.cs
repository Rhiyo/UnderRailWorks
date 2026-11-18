// UnderRailWorks: the game's lighting, and turning it down for mods that light the world themselves.
// How the game lights (measured, Nov 2026): every tile holds light stamps (ak1.d: one per light source reaching it, a
// colour and a kind: Absolute, Increment, Decrement); a tile's light is the area's starting light with its stamps
// applied, clamped between the area's minimal and maximal light (ak1.Lightning, aac.a). A thing's light is the
// average of its tiles'; each frame the world pass multiplies its layers' tint by it (a7m.a(b0b, bool) -> a7m.ir),
// only for things in sight (out of sight the remembered colour of the fog of war is used and left alone here).
using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Xna.Framework;
using UnderRailWorks.Internal;

namespace UnderRailWorks
{

// A light source as its light lands on the tiles (the game keeps no other position for it): where its light is
// centred, how far it reaches, its brightest colour.
public sealed class LightSource
{
    internal LightSource() { }
    public Guid Id { get; internal set; }
    public Vector2 Centre { get; internal set; }       // map position (tiles), the light-weighted middle of what it lights
    public float Radius { get; internal set; }         // tiles from Centre to the farthest tile it lights
    public Color Colour { get; internal set; }         // its brightest stamp
    public bool Darkens { get; internal set; }         // a Decrement light (shadow zones)
    public int Tiles { get; internal set; }
}

public static class Lights
{
    // How much of the game's own lighting is used for things in sight: 1 the game's, 0 none (everything at full light,
    // for a mod that lights the world itself, e.g. with LayerColour or a PostProcess effect). Every mod's request
    // multiplies (two mods at 0.5: 0.25); checked once per frame. A mod's requests go with it.
    public static void GameLighting(WorksMod mod, Func<float> strength)
    {
        if (mod == null || strength == null) throw new ArgumentNullException(mod == null ? "mod" : "strength");
        lock (requests) { requests.Add(new KeyValuePair<string, Func<float>>(mod.Id, strength)); snapshot = requests.ToArray(); }
    }
    // The strength in use this frame.
    public static float GameStrength { get { return strength; } }

    // The game's light on a tile of an area (the result of its sources, the colour things there are multiplied by);
    // null outside the area.
    public static Color? At(Area area, int x, int y)
    {
        if (area == null || x < 0 || y < 0 || x >= area.Width || y >= area.Height) return null;
        var t = Areas.Tile(area.Game, x, y);
        return t == null ? (Color?)null : t.Lightning;
    }
    // The area's light where no source reaches, and the limits the game keeps every tile's light in.
    public static Color Ambient(Area area) { return area == null ? Color.White : area.Game.StartingLightning; }
    public static Color Darkest(Area area) { return area == null ? Color.Black : area.Game.MinimalLightning; }
    public static Color Brightest(Area area) { return area == null ? Color.White : area.Game.MaximalLightning; }

    // The light sources of an area, from the stamps on its tiles (worked out again at most every half second per area;
    // a light switched off disappears from the list).
    public static IList<LightSource> Sources(Area area)
    {
        if (area == null) return new LightSource[0];
        Cached c;
        if (cache.TryGetValue(area.Game, out c) && c.At.ElapsedMilliseconds < 500) return c.List;
        var list = Collect(area);
        cache.Remove(area.Game);
        cache.Add(area.Game, new Cached { List = list, At = System.Diagnostics.Stopwatch.StartNew() });
        return list;
    }
    sealed class Cached { public IList<LightSource> List; public System.Diagnostics.Stopwatch At; }
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<aud, Cached> cache = new System.Runtime.CompilerServices.ConditionalWeakTable<aud, Cached>();

    static readonly FieldInfo Stamps = typeof(ak1).GetField("d", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
    sealed class Acc { public double X, Y, W; public float Best = -1; public Color Colour; public bool Darkens; public int Tiles; public List<Point> At = new List<Point>(); }
    static IList<LightSource> Collect(Area area)
    {
        var by = new Dictionary<Guid, Acc>();
        int w = area.Width, h = area.Height;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var t = Areas.Tile(area.Game, x, y);
                var stamps = t == null ? null : Stamps.GetValue(t) as Dictionary<Guid, c3y>;
                if (stamps == null || stamps.Count == 0) continue;
                foreach (var s in stamps.Values)
                {
                    var f = s == null ? null : s.a();
                    if (f == null) continue;
                    var light = f.aej();
                    var colour = light.b();
                    float bright = (colour.R + colour.G + colour.B) / 765f;
                    Acc a;
                    if (!by.TryGetValue(s.c(), out a)) by[s.c()] = a = new Acc { Darkens = light.a() == Ouroboros.Playfield.Lightning.LightType.Decrement };
                    double weight = Math.Max(0.01, bright);
                    a.X += (x + 0.5) * weight; a.Y += (y + 0.5) * weight; a.W += weight; a.Tiles++; a.At.Add(new Point(x, y));
                    if (bright > a.Best) { a.Best = bright; a.Colour = colour; }
                }
            }
        var list = new List<LightSource>(by.Count);
        foreach (var kv in by)
        {
            var a = kv.Value;
            var centre = new Vector2((float)(a.X / a.W), (float)(a.Y / a.W));
            float r = 0;
            foreach (var p in a.At) r = Math.Max(r, Vector2.Distance(centre, new Vector2(p.X + 0.5f, p.Y + 0.5f)));
            list.Add(new LightSource { Id = kv.Key, Centre = centre, Radius = r, Colour = a.Colour, Darkens = a.Darkens, Tiles = a.Tiles });
        }
        return list.AsReadOnly();
    }

    static readonly List<KeyValuePair<string, Func<float>>> requests = new List<KeyValuePair<string, Func<float>>>();
    static KeyValuePair<string, Func<float>>[] snapshot = new KeyValuePair<string, Func<float>>[0];
    static float strength = 1f;
    sealed class Owner : IModOwned
    {
        public void RemoveOwner(string mod) { lock (requests) { requests.RemoveAll(r => string.Equals(r.Key, mod, StringComparison.OrdinalIgnoreCase)); snapshot = requests.ToArray(); } }
    }
    static Lights() { lock (ModScope.Hooks) ModScope.Hooks.Add(new Owner()); }

    // Once per world pass: the requests multiplied.
    internal static void Frame()
    {
        var s = snapshot;
        float v = 1f;
        for (int i = 0; i < s.Length; i++)
        {
            try { v *= MathHelper.Clamp(s[i].Value(), 0f, 1f); }
            catch (Exception ex) { ModErrors.Report(s[i].Key, "Lights.GameLighting", ex); new Owner().RemoveOwner(s[i].Key); }
        }
        strength = v;
    }

    // a7m.a(b0b, bool inSight): the light a thing's layers are multiplied by.
    internal static void Light(bool inSight, ref Color light)
    {
        if (!inSight || strength >= 1f) return;
        light = Color.Lerp(Color.White, light, strength);
    }
}

static class LightsHooks
{
    internal static void Install(HarmonyLib.Harmony harmony)
    {
        var m = typeof(a7m).GetMethod("a", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null, new[] { typeof(b0b), typeof(bool) }, null);
        if (m == null || m.ReturnType != typeof(Color)) throw new MissingMethodException("a7m.a(b0b, bool) -> Color (a thing's light)");
        harmony.Patch(m, postfix: new HarmonyLib.HarmonyMethod(typeof(LightsHooks).GetMethod("Lit", BindingFlags.Static | BindingFlags.NonPublic)));
    }
    static void Lit(bool __1, ref Color __result)
    {
        try { Lights.Light(__1, ref __result); } catch (Exception ex) { PatchGuard.Failed("Lights.Light", ex); }
    }
}
}
