// UnderRailWorks: mods' own pictures and sounds, and replacing the game's.
// The game's side (measured from its code): every texture is loaded by name ("Locale\\Creatures\\...", "Icons\\armors",
// "concept\\mm\\1p78", "Portraits\\...") through one resource manager (a4q, the cne service), which caches it in a
// dictionary of entries (b5s: b the object, a/c idle timers; c == -1 never expires). Works puts a mod's texture into
// that cache under the game's name, so the game draws it wherever it would draw its own: menu art, portraits,
// character and creature sprite sheets, icon atlases, floors, objects. The entries are kept from being unloaded, and
// the cache is made case-insensitive (the game asks for the same file in different cases).
// Item and ability icons are cells of icon atlases ("armors.12": Icons\armors, cell 12, 33x33); every icon lookup goes
// through ejh.b(name), usually with the item definition's icon name (bk3.a3()): Works answers its own icon names in
// ejh.b and gives registered item codes such a name in a3 (Icons). Saves keep the game's names (see ProxyIconCopied).
// Nothing here touches item, character or save data: remove the mod and the game draws its own again.
//   Assets.Replace(this, "Portraits\\Male\\m_01", "art/portrait.png");      // or just Mods/MyMod/textures/Portraits/Male/m_01.png
//   Icons.SetItemIcon(this, "med.bandage", "art/bandage.png");
//   MainMenu.SetBackground(this, "art/menu.png");
//   var hit = Assets.Sound(this, "sounds/hit.wav"); hit.Play();
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using UnderRailWorks.Internal;

namespace UnderRailWorks
{
    public static class Assets
    {
        // A picture from the mod's folder (PNG or JPEG), loaded once; null before the game's graphics are up (use the
        // file-based Replace / Icons / MainMenu calls from Load: they load when needed) or if the file can't be read.
        public static Texture2D Texture(WorksMod mod, string file) { return AssetStore.Load(Path.Combine(mod.Info.Folder, file)); }

        // A sound from the mod's folder (16-bit PCM WAV), loaded once.
        public static SoundEffect Sound(WorksMod mod, string file) { return AssetStore.LoadSound(Path.Combine(mod.Info.Folder, file)); }

        // The game draws the mod's picture instead of its own texture gameAsset (its name under data, without the
        // extension, e.g. "Portraits\\Male\\m_01"; ContentIndex lists them). Same size as the original for sprite sheets
        // and atlases (their cells and hit-test shapes stay the game's); any size for whole pictures.
        public static void Replace(WorksMod mod, string gameAsset, string file) { AssetStore.Replace(mod.Id, gameAsset, Path.Combine(mod.Info.Folder, file)); }
        public static void Replace(WorksMod mod, string gameAsset, Texture2D texture) { AssetStore.Replace(mod.Id, gameAsset, texture); }

        // Writes one of the game's textures to a PNG (to edit for a replacement or a texture pack). False if it isn't
        // a texture or the graphics aren't up.
        public static bool Export(string gameAsset, string pngFile) { return AssetStore.Export(gameAsset, pngFile); }

        // What the game has loaded now, by name (to find the texture behind something on screen).
        public static IList<string> Loaded { get { return AssetStore.LoadedNames(); } }

        // Every texture the game has, by name (from data's .xnb files).
        public static IList<string> GameTextures { get { return AssetStore.GameNames().ToList(); } }
    }

    public static class Icons
    {
        // A new icon name for a picture in the mod's folder (drawn scaled into the icon's square): use it wherever the
        // game takes an icon name.
        public static string Register(WorksMod mod, string id, string file)
        {
            var name = "works:" + mod.Id + ":" + id;
            AssetStore.Icon(name, Path.Combine(mod.Info.Folder, file));
            return name;
        }

        // Items with this code show the picture as their icon (inventory, quick slots, trade, containers, tooltips).
        public static void SetItemIcon(WorksMod mod, string itemCode, string file) { AssetStore.ItemIcon(itemCode, Register(mod, "item." + itemCode, file)); }
    }

    public static class MainMenu
    {
        // The picture behind the main menu (and the other full-screen menus that use the same art: options, loading a
        // game). Works' BackgroundFit setting decides between cropping and letterboxing it.
        public static void SetBackground(WorksMod mod, string file)
        {
            foreach (var name in AssetStore.GameNames().Where(n => n.StartsWith("concept\\", StringComparison.OrdinalIgnoreCase) && n.Split('\\').Length == 3 && n.Split('\\')[1].EndsWith("mm", StringComparison.OrdinalIgnoreCase)))
                AssetStore.Replace(mod.Id, name, Path.Combine(mod.Info.Folder, file));
        }
    }
}

namespace UnderRailWorks.Internal
{
    static class AssetStore
    {
        sealed class Pending { public string Mod, File; public Texture2D Texture; }
        static readonly Dictionary<string, Pending> replaced = new Dictionary<string, Pending>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, Texture2D> files = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, SoundEffect> sounds = new Dictionary<string, SoundEffect>(StringComparer.OrdinalIgnoreCase);
        static readonly HashSet<Texture2D> ours = new HashSet<Texture2D>();
        static readonly Dictionary<string, string> iconFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, object> icons = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);     // name -> c3i
        static readonly Dictionary<string, string> itemIcons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static bool dirty = true;

        // --- The game's resource manager (a4q) ----------------------------------------------------------------------
        static object manager;
        static IDictionary cache;
        static Type entryType = typeof(c3i).Assembly.GetType("b5s");
        static FieldInfo entryObject = entryType.GetField("b"), entryIdle = entryType.GetField("a"), entryLimit = entryType.GetField("c");
        static ContentManager content;

        static bool Ready()
        {
            if (cache != null) return true;
            var m = dr3.a();
            if (m == null || m.GetType().Name != "a4q") return false;
            var t = m.GetType();
            var dictField = t.GetField("m_a", BindingFlags.Instance | BindingFlags.NonPublic) ?? t.GetField("a", BindingFlags.Instance | BindingFlags.NonPublic);
            content = t.GetFields(BindingFlags.Instance | BindingFlags.NonPublic).Select(f => f.GetValue(m)).OfType<ContentManager>().FirstOrDefault();
            var old = dictField.GetValue(m) as IDictionary;
            if (old == null || content == null) return false;
            // Case-insensitive: the game asks for "Gui\\..." and "gui\\..." alike, and mods write names as they like.
            var dictType = typeof(Dictionary<,>).MakeGenericType(typeof(string), entryType);
            var fresh = (IDictionary)Activator.CreateInstance(dictType, StringComparer.OrdinalIgnoreCase);
            foreach (DictionaryEntry e in old) if (!fresh.Contains(e.Key)) fresh[e.Key] = e.Value;
            dictField.SetValue(m, fresh);
            manager = m; cache = fresh;
            Log.Write("assets: resource cache ready (" + fresh.Count + " entries)");
            return true;
        }

        internal static IList<string> LoadedNames()
        {
            if (!Ready()) return new List<string>();
            return cache.Keys.Cast<string>().Where(k => cache[k] != null && entryObject.GetValue(cache[k]) != null).OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
        }

        internal static GraphicsDevice Device
        {
            get
            {
                if (content == null && !Ready()) return null;
                var s = content.ServiceProvider.GetService(typeof(IGraphicsDeviceService)) as IGraphicsDeviceService;
                return s == null ? null : s.GraphicsDevice;
            }
        }

        // Names of the game's textures (and sounds...), from data's .xnb files: "Portraits\\Male\\m_01".
        static List<string> gameNames;
        internal static IEnumerable<string> GameNames()
        {
            if (gameNames != null) return gameNames;
            var root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
            gameNames = Directory.Exists(root) ? Directory.GetFiles(root, "*.xnb", SearchOption.AllDirectories)
                .Select(f => f.Substring(root.Length + 1, f.Length - root.Length - 5).Replace('/', '\\')).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList() : new List<string>();
            return gameNames;
        }

        // --- Loading a mod's files ---------------------------------------------------------------------------------
        internal static Texture2D Load(string path)
        {
            Texture2D t;
            if (files.TryGetValue(path, out t) && t != null && !t.IsDisposed) return t;
            var g = Device;
            if (g == null) return null;
            try
            {
                using (var s = File.OpenRead(path)) t = Texture2D.FromStream(g, s);
                // The game's textures are premultiplied (its .xnb content); a PNG's aren't: premultiply so edges blend right.
                var px = new Color[t.Width * t.Height];
                t.GetData(px);
                for (int i = 0; i < px.Length; i++) { var c = px[i]; if (c.A < 255) px[i] = new Color(c.R * c.A / 255, c.G * c.A / 255, c.B * c.A / 255, c.A); }
                t.SetData(px);
                t.Name = path;
                files[path] = t; ours.Add(t);
                return t;
            }
            catch (Exception ex) { Log.Write("assets: " + path + ": " + ex.Message); files[path] = null; return null; }
        }

        internal static SoundEffect LoadSound(string path)
        {
            SoundEffect s;
            if (sounds.TryGetValue(path, out s)) return s;
            try { using (var f = File.OpenRead(path)) s = SoundEffect.FromStream(f); }
            catch (Exception ex) { Log.Write("assets: " + path + ": " + ex.Message); s = null; }
            sounds[path] = s;
            return s;
        }

        internal static bool Export(string name, string png)
        {
            if (Device == null) return false;
            try
            {
                var t = content.Load<Texture2D>(Norm(name));
                // Content textures are premultiplied; a PNG for editing shouldn't be.
                var px = new Color[t.Width * t.Height];
                t.GetData(px);
                for (int i = 0; i < px.Length; i++) { var c = px[i]; if (c.A > 0 && c.A < 255) px[i] = new Color(Math.Min(255, c.R * 255 / c.A), Math.Min(255, c.G * 255 / c.A), Math.Min(255, c.B * 255 / c.A), c.A); }
                var copy = new Texture2D(Device, t.Width, t.Height);
                copy.SetData(px);
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(png)));
                using (var f = File.Create(png)) copy.SaveAsPng(f, t.Width, t.Height);
                copy.Dispose();
                return true;
            }
            catch (Exception ex) { Log.Write("assets: export " + name + ": " + ex.Message); return false; }
        }

        // --- Replacing the game's textures --------------------------------------------------------------------------
        internal static void Replace(string mod, string gameAsset, string file)
        {
            replaced[Norm(gameAsset)] = new Pending { Mod = mod, File = file };
            dirty = true;
        }

        internal static void Replace(string mod, string gameAsset, Texture2D texture)
        {
            if (texture != null) ours.Add(texture);
            replaced[Norm(gameAsset)] = new Pending { Mod = mod, Texture = texture };
            dirty = true;
        }

        static string Norm(string name) { return (name ?? "").Replace('/', '\\').Trim('\\'); }

        // Texture packs without code: Mods/<mod>/textures/<game asset>.png.
        internal static void ScanTexturePack(ModInfo mod)
        {
            var dir = Path.Combine(mod.Folder, "textures");
            if (!Directory.Exists(dir)) return;
            int n = 0;
            foreach (var f in Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories).Where(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)))
            {
                var name = f.Substring(dir.Length + 1, f.Length - dir.Length - 1 - Path.GetExtension(f).Length);
                Replace(mod.Id, name, f); n++;
            }
            Log.Write("assets: " + mod.Id + ": " + n + " textures in its texture pack");
        }

        // Puts the replacements into the game's cache (each frame until all are in, then whenever one is added).
        internal static void Apply()
        {
            if (!dirty || !Ready() || Device == null) return;
            dirty = false;
            foreach (var kv in replaced)
            {
                var p = kv.Value;
                if (p.Texture == null && p.File != null) p.Texture = Load(p.File);
                if (p.Texture == null) continue;
                object entry = cache.Contains(kv.Key) ? cache[kv.Key] : null;
                if (entry == null) { entry = Activator.CreateInstance(entryType, true); cache[kv.Key] = entry; }
                if (!ReferenceEquals(entryObject.GetValue(entry), p.Texture))
                {
                    entryObject.SetValue(entry, p.Texture);
                    entryLimit.SetValue(entry, -1.0); entryIdle.SetValue(entry, double.MaxValue);     // never unloaded
                    Log.Write("assets: " + p.Mod + " replaces " + kv.Key + " (" + p.Texture.Width + "x" + p.Texture.Height + ")");
                }
            }
        }

        // a4q's private static a(b5s): unloads (disposes) an entry. Not ours.
        static bool Unloading(object __0) { var t = __0 == null ? null : entryObject.GetValue(__0) as Texture2D; return t == null || !ours.Contains(t); }

        // --- Icons ---------------------------------------------------------------------------------------------------
        internal static void Icon(string name, string file) { iconFiles[name] = file; icons.Remove(name); }
        // A game icon name (e.g. "Feat.<key>") answered with a mod's picture.
        internal static void IconAlias(string name, string file) { iconFiles[name] = file; icons.Remove(name); aliases.Add(name); }
        static readonly HashSet<string> aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        internal static void ItemIcon(string code, string icon) { itemIcons[code] = icon; }

        static readonly FieldInfo IconSize = typeof(c3i).GetField("a");
        static readonly ConstructorInfo PieceCtor = typeof(abu).GetConstructors().First(c => c.GetParameters().Length == 3);
        static object IconFor(string name)
        {
            object made;
            if (icons.TryGetValue(name, out made)) return made;
            string file;
            if (!iconFiles.TryGetValue(name, out file)) return null;
            var t = Load(file);
            if (t == null) return null;     // graphics not up yet: try again next time
            var icon = new c3i();
            IconSize.SetValue(icon, new esj(33, 33));     // c3i.a: the game's icon cell (bj8.a); a field/method name clash
            icon.b.Add((abu)PieceCtor.Invoke(new object[] { t, new Rectangle(0, 0, t.Width, t.Height), 1f }));     // abu(Texture2D, Rectangle, float)
            icons[name] = icon;
            return icon;
        }

        // ejh.b(name): any icon by name.
        static bool IconByName(string __0, ref c3i __result)
        {
            if (__0 == null || (!__0.StartsWith("works:", StringComparison.Ordinal) && !aliases.Contains(__0))) return true;
            __result = IconFor(__0) as c3i;
            return false;
        }

        // ejh.a(item): an item's icon.
        static bool ItemIconOf(coh __0, ref c3i __result)
        {
            string name;
            if (__0 == null || __0.Definition == null || itemIcons.Count == 0 || !itemIcons.TryGetValue(__0.Definition.a5() ?? "", out name)) return true;
            var icon = IconFor(name) as c3i;
            if (icon == null) return true;
            __result = icon;
            return false;
        }

        // bk3.a3(): an item definition's icon name, which most of the game passes to ejh.b. The mod's icon for
        // registered item codes. The quick-slot proxy saves the name it copies in ejh.a(av) (CP:I): there the game's own
        // name is put back, so saves only ever hold stock icon names.
        [ThreadStatic] static bool stockName;
        static bool IconNameOf(bk3 __instance, ref string __result)
        {
            string name;
            if (stockName || itemIcons.Count == 0 || !itemIcons.TryGetValue(__instance.a5() ?? "", out name) || IconFor(name) == null) return true;
            __result = name;
            return false;
        }

        static void ProxyIconCopied(av __0)
        {
            var item = __0 == null ? null : __0.b9() as coh;
            if (item == null || item.Definition == null || __0.i == null || !__0.i.StartsWith("works:", StringComparison.Ordinal)) return;
            stockName = true;
            try { __0.i = item.Definition.a3(); } finally { stockName = false; }
        }

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            const BindingFlags any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            Func<string, HarmonyLib.HarmonyMethod> m = n => new HarmonyLib.HarmonyMethod(typeof(AssetStore).GetMethod(n, any));
            var unload = typeof(a4q).GetMethods(any).First(x => x.IsStatic && x.Name == "a" && x.GetParameters().Length == 1 && x.GetParameters()[0].ParameterType == entryType);
            harmony.Patch(unload, prefix: m("Unloading"));
            harmony.Patch(typeof(ejh).GetMethod("b", BindingFlags.Static | BindingFlags.Public, null, new[] { typeof(string), typeof(cne) }, null), prefix: m("IconByName"));
            harmony.Patch(typeof(ejh).GetMethod("a", BindingFlags.Static | BindingFlags.Public, null, new[] { typeof(coh), typeof(cne) }, null), prefix: m("ItemIconOf"));
            harmony.Patch(typeof(bk3).GetMethod("a3", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null), prefix: m("IconNameOf"));
            harmony.Patch(typeof(ejh).GetMethods(BindingFlags.Static | BindingFlags.Public).First(x => x.Name == "a" && x.GetParameters().Length == 3 && x.GetParameters()[0].ParameterType == typeof(av)), postfix: m("ProxyIconCopied"));
            // Before a menu background is made (the main menu can be built before the first update).
            harmony.Patch(typeof(czo).GetMethods(any).First(x => x.IsStatic && x.Name == "a" && x.ReturnType == typeof(bg1) && x.GetParameters().Length == 1), prefix: m("BeforeBackground"));
        }

        static void BeforeBackground() { try { Apply(); } catch (Exception ex) { Log.Write("assets: " + ex.Message); } }
    }
}
