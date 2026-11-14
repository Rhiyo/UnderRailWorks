// UnderRailWorks: music and sound effects. The game's side (measured from its code;
// ~/UnderRailWorks-research/specs/music-console-feats-ai.md):
// - The audio manager (b6d, the dd7 service) builds a song table (id -> SongInfo za, whose SourcePath is relative to
//   the data folder, or absolute) and a sound table (id -> d6c, which loads its SoundEffect once) from the audio bank,
//   in its private c(). Songs are .ogg (decoded by Works' music player), sounds were the game's .xnb (Works puts a WAV's
//   SoundEffect in their place).
// - Which song plays: c4m.ai6() every frame picks the highest of three layers (menu; the map's; the current area's
//   "Music" property) and asks dd7.ad3(id, volume, start, end) when it changes. The game has no combat music.
// - Every sound plays by id: dd7.aeb(id, volume, loop, category).
// Nothing here is saved (the game's MusicOverride_ world variables are left alone).
//   Music.Replace(this, "Junkyard", "music/junkyard.ogg");   Music.Add(this, "fight", "music/fight.ogg");
//   Music.Override(this, () => Combat.InCombat ? "fight" : null);
//   Sound.Replace(this, "Interface.ButtonClick", "sounds/click.wav");   Sound.Play("Interface.ButtonClick");
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework.Audio;
using UnderRailWorks.Internal;

namespace UnderRailWorks
{
    public static class Music
    {
        // The game's song ids (from its audio bank), and the one playing now.
        public static IList<string> Songs { get { var b = AudioHooks.Bank; return b == null ? new List<string>() : AudioHooks.SongTable(b).Keys.OrderBy(k => k).ToList(); } }
        public static string Current { get { return AudioHooks.Playing; } }

        // The song id plays the mod's .ogg instead (every place the game plays it).
        public static void Replace(WorksMod mod, string id, string oggFile) { AudioHooks.Songs[id.ToLowerInvariant()] = new KeyValuePair<string, bool>(ModFiles.Resolve(mod, oggFile, "song " + id), false); AudioHooks.Reapply(); }
        // A new song id (to play with Override, Play, or an area's "Music" property set by a mod).
        public static void Add(WorksMod mod, string id, string oggFile) { AudioHooks.Songs[id.ToLowerInvariant()] = new KeyValuePair<string, bool>(ModFiles.Resolve(mod, oggFile, "song " + id), true); AudioHooks.Reapply(); }

        // Asked every frame: a song id to play instead of the game's choice, or null for the game's. The first mod (in
        // hook order) with an answer wins.
        public static void Override(WorksMod mod, Func<string> choose) { AudioHooks.Choosers.Add(mod, choose); }
    }

    public static class Sound
    {
        // Every sound id the game has (e.g. "Interface.ButtonClick").
        public static IList<string> Ids { get { var b = AudioHooks.Bank; return b == null ? new List<string>() : AudioHooks.SoundTable(b).Keys.OrderBy(k => k).ToList(); } }
        // The sound id plays the mod's WAV (16-bit PCM) instead.
        public static void Replace(WorksMod mod, string id, string wavFile) { AudioHooks.Sounds[id] = ModFiles.Resolve(mod, wavFile, "sound " + id); AudioHooks.Reapply(); }
        // A new sound id the game's id-based paths (and Play) can use.
        public static void Add(WorksMod mod, string id, string wavFile) { Replace(mod, id, wavFile); }
        // Plays a sound by id at volume 0..1 (as an interface sound).
        public static void Play(string id, float volume = 1f) { var a = bpo.GetService<dd7>(); if (a != null) a.aeb(id, volume, false, c9z.e); }
        // Wherever the game plays oldId, newId plays instead.
        public static void Remap(WorksMod mod, string oldId, string newId) { AudioHooks.Remaps[oldId] = newId; remappedBy[oldId] = mod.Id; }
        static readonly System.Collections.Generic.Dictionary<string, string> remappedBy = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        internal static void Forget(string mod) { foreach (var k in System.Linq.Enumerable.ToList(System.Linq.Enumerable.Where(remappedBy, kv => string.Equals(kv.Value, mod, StringComparison.OrdinalIgnoreCase)))) { AudioHooks.Remaps.Remove(k.Key); remappedBy.Remove(k.Key); } }
    }
}

namespace UnderRailWorks.Internal
{
    static class AudioHooks
    {
        internal static readonly Dictionary<string, KeyValuePair<string, bool>> Songs = new Dictionary<string, KeyValuePair<string, bool>>();     // id -> (file, new)
        internal static readonly Dictionary<string, string> Sounds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        internal static readonly Dictionary<string, string> Remaps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        internal static readonly Hook<Func<string>> Choosers = new Hook<Func<string>>("Music.Override");
        internal static b6d Bank;
        internal static string Playing;

        const BindingFlags Priv = BindingFlags.Instance | BindingFlags.NonPublic;
        static readonly FieldInfo SongsField = typeof(b6d).GetField("m_e", Priv) ?? typeof(b6d).GetField("e", Priv);
        static readonly FieldInfo SongListField = typeof(b6d).GetField("m_f", Priv) ?? typeof(b6d).GetField("f", Priv);
        static readonly FieldInfo SoundsField = typeof(b6d).GetField("g", Priv);
        static readonly FieldInfo EffectField = typeof(d6c).GetField("m_b", Priv) ?? typeof(d6c).GetField("b", Priv);
        static readonly FieldInfo CurrentChoice = typeof(c4m).GetField("a", Priv);
        internal static Dictionary<string, abr> SongTable(b6d b) { return (Dictionary<string, abr>)SongsField.GetValue(b); }
        internal static Dictionary<string, d6c> SoundTable(b6d b) { return (Dictionary<string, d6c>)SoundsField.GetValue(b); }

        internal static void Reapply() { if (Bank != null) try { Apply(Bank); } catch (Exception ex) { Log.Write("audio: " + ex.Message); } }

        static void Built(b6d __instance) { try { Bank = __instance; Reapply(); } catch (Exception ex_) { PatchGuard.Failed("Audio.Built", ex_); }
    }

        static void Apply(b6d b)
        {
            var songs = SongTable(b); var list = (List<abr>)SongListField.GetValue(b);
            foreach (var kv in Songs)
            {
                abr song;
                if (songs.TryGetValue(kv.Key, out song)) song.d().d(kv.Value.Key);     // za.SourcePath: an absolute path is used as is
                else if (kv.Value.Value)
                {
                    var info = new za();
                    info.b(kv.Key); info.c(kv.Key); info.d(kv.Value.Key); info.a(1f);
                    info.a(b1g.b); info.a(bbs.b);     // fade in, fade out
                    song = new abr(info);
                    songs[kv.Key] = song; list.Add(song);
                }
                else { Log.Write("audio: no song " + kv.Key + " to replace"); continue; }
            }
            var sounds = SoundTable(b);
            foreach (var kv in Sounds)
            {
                var effect = AssetStore.LoadSound(kv.Value);
                if (effect == null) continue;
                d6c entry;
                if (!sounds.TryGetValue(kv.Key, out entry))
                {
                    var info = new dv5(); info.b(kv.Key); info.c(kv.Key); info.d(kv.Value);
                    sounds[kv.Key] = entry = new d6c(info);
                }
                EffectField.SetValue(entry, effect);     // d6c only loads its sound when it has none
            }
            Log.Write("audio: " + Songs.Count + " songs and " + Sounds.Count + " sounds from mods");
        }

        // c4m.ai6(): the per-frame song choice.
        static bool chooseFailed;
        static bool Choose(c4m __instance)
        {
            if (chooseFailed) return true;
            try { return ChooseSong(__instance); }
            catch (Exception ex) { chooseFailed = true; Log.Write("audio: song choice failed, the game chooses from now on: " + ex); return true; }
        }

        static bool ChooseSong(c4m __instance)
        {
            string want = null;
            if (Choosers.Any) Choosers.RunUntil(f => (want = f()) != null);
            var current = CurrentChoice.GetValue(__instance) as bn3;
            if (want == null)
            {
                // Ours (marked "works:"; Ordinal: a culture comparison ignores control characters, which matched every id):
                // let the game choose again.
                if (current != null && current.a() != null && current.a().StartsWith("works:", StringComparison.Ordinal)) CurrentChoice.SetValue(__instance, null);
                Playing = current == null ? null : current.a();
                return true;
            }
            if (current == null || !string.Equals(current.a(), "works:" + want, StringComparison.Ordinal))
            {
                var a = bpo.GetService<dd7>();
                if (a != null) a.ad3(want.ToLowerInvariant(), 1f, b1g.b, current == null ? (bbs?)null : bbs.b);
                CurrentChoice.SetValue(__instance, new bn3("works:" + want));
            }
            Playing = want;
            return false;
        }

        static void Remap(ref string __0) { try { string to; if (__0 != null && Remaps.Count > 0 && Remaps.TryGetValue(__0, out to)) __0 = to; } catch (Exception ex_) { PatchGuard.Failed("Audio.Remap", ex_); }
    }

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            Func<string, HarmonyLib.HarmonyMethod> m = n => new HarmonyLib.HarmonyMethod(typeof(AudioHooks).GetMethod(n, BindingFlags.Static | BindingFlags.NonPublic));
            harmony.Patch(typeof(b6d).GetMethod("c", Priv, null, Type.EmptyTypes, null), postfix: m("Built"));
            harmony.Patch(typeof(c4m).GetMethod("ai6", BindingFlags.Instance | BindingFlags.Public), prefix: m("Choose"));
            harmony.Patch(typeof(b6d).GetMethod("aeb"), prefix: m("Remap"));
            var a = bpo.GetService<dd7>() as b6d;     // already built (Works installs after the game started)
            if (a != null) Built(a);
        }
    }
}
