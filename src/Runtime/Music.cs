// Background music without SFML, for a 64-bit process (experimental-64bit patch). The game uses SFML only for
// streamed music, all in one class (bxt): new Music(file), Loop = true, Volume (0-100), Pitch (1 = normal), Status,
// Play, Stop, Dispose. The patch changes bxt's Music field to object and sends those calls here.
// 32-bit process: the real SFML Music, exactly as before. 64-bit: the game's 32-bit CSFML/OpenAL/libsndfile can't
// load, so the .ogg is decoded with NVorbis (managed) and streamed through XNA's DynamicSoundEffectInstance (FNA).
using System;
using Microsoft.Xna.Framework.Audio;
using UnderRailWorks;

namespace UnderRailWorks.Internal
{

public static class FixMusic
{
    static bool Native => IntPtr.Size == 4;

    public static object Create(string file)
    {
        if (Native) return new SFML.Audio.Music(file);
        try { return new OggMusic(file); }
        catch (Exception ex) { Log.Write("music: " + file + ": " + ex.Message); return null; }
    }

    // SFML.Audio.SoundStatus values, which the game compares against: Stopped 0, Paused 1, Playing 2.
    public static int Status(object music)
    {
        if (music is SFML.Audio.Music sf) return (int)sf.Status;
        return music is OggMusic ogg ? ogg.Status : 0;
    }

    public static void SetLoop(object music, bool loop)
    {
        if (music is SFML.Audio.Music sf) sf.Loop = loop;
        else if (music is OggMusic ogg) ogg.Loop = loop;
    }

    public static void SetVolume(object music, float volume)
    {
        if (music is SFML.Audio.Music sf) sf.Volume = volume;
        else if (music is OggMusic ogg) ogg.Volume = volume;
    }

    public static void SetPitch(object music, float pitch)
    {
        if (music is SFML.Audio.Music sf) sf.Pitch = pitch;
        else if (music is OggMusic ogg) ogg.Pitch = pitch;
    }

    public static void Play(object music)
    {
        if (music is SFML.Audio.Music sf) sf.Play();
        else if (music is OggMusic ogg) ogg.Play();
    }

    public static void Stop(object music)
    {
        if (music is SFML.Audio.Music sf) sf.Stop();
        else if (music is OggMusic ogg) ogg.Stop();
    }

    public static void Dispose(object music)
    {
        if (music is SFML.Audio.Music sf) sf.Dispose();
        else if (music is OggMusic ogg) ogg.Dispose();
    }
}

// One streamed .ogg, kept about three ~0.1 s buffers ahead by a background thread (Stream). FNA's
// DynamicSoundEffectInstance only notices finished buffers (PendingBufferCount going down, BufferNeeded) in its
// internal Update(), which FrameworkDispatcher.Update calls; XNA's base Game.Update calls that, and UnderRail's
// Update doesn't call the base. So the thread calls the instance's Update() itself (it locks its own buffer list and
// only asks FAudio for the voice state). This class is only used in 64-bit, i.e. on FNA.
sealed class OggMusic : IDisposable
{
    const int BufferMs = 100, Queued = 3;
    readonly NVorbis.VorbisReader reader;
    readonly DynamicSoundEffectInstance output;
    readonly float[] samples;
    readonly byte[] pcm;
    readonly object gate = new object();
    bool ended, disposed;
    public bool Loop;
    readonly string name;

    static readonly System.Reflection.MethodInfo Collect = typeof(DynamicSoundEffectInstance).GetMethod("Update",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null, Type.EmptyTypes, null);
    static readonly System.Collections.Generic.List<OggMusic> playing = new System.Collections.Generic.List<OggMusic>();
    static System.Threading.Thread streamer;

    public OggMusic(string file)
    {
        reader = new NVorbis.VorbisReader(file);
        int perBuffer = reader.SampleRate * BufferMs / 1000 * reader.Channels;
        samples = new float[perBuffer];
        pcm = new byte[perBuffer * 2];
        output = new DynamicSoundEffectInstance(reader.SampleRate, reader.Channels == 1 ? AudioChannels.Mono : AudioChannels.Stereo);
        name = System.IO.Path.GetFileName(file);
        Log.Write("music: opened " + name);
        lock (playing)
        {
            playing.Add(this);
            if (streamer == null) { streamer = new System.Threading.Thread(Stream) { IsBackground = true, Name = "UnderRailFixes music" }; streamer.Start(); }
        }
    }

    static void Stream()
    {
        while (true)
        {
            OggMusic[] all;
            lock (playing) all = playing.ToArray();
            foreach (var m in all)
            {
                try
                {
                    lock (m.gate)
                    {
                        if (m.disposed) continue;
                        if (Collect != null) Collect.Invoke(m.output, null);
                        while (!m.ended && m.output.State == SoundState.Playing && m.output.PendingBufferCount < Queued) m.Fill();
                    }
                }
                catch (Exception ex) { Log.Write("music: " + ex.Message); }
            }
            System.Threading.Thread.Sleep(20);
        }
    }

    // SFML: Stopped 0, Paused 1, Playing 2.
    public int Status
    {
        get
        {
            lock (gate)
            {
                if (disposed) return 0;
                switch (output.State) { case SoundState.Playing: return 2; case SoundState.Paused: return 1; default: return 0; }
            }
        }
    }

    public float Volume { set { lock (gate) if (!disposed) output.Volume = Math.Max(0f, Math.Min(1f, value / 100f)); } }

    // SFML pitch is a speed factor (2 = an octave up); XNA's is in octaves, -1 to 1.
    public float Pitch { set { lock (gate) if (!disposed && value > 0) output.Pitch = (float)Math.Max(-1.0, Math.Min(1.0, Math.Log(value, 2))); } }

    public void Play()
    {
        lock (gate)
        {
            if (disposed || output.State == SoundState.Playing) return;
            if (output.State == SoundState.Stopped)
            {
                if (ended) { reader.DecodedPosition = 0; ended = false; }
                while (output.PendingBufferCount < Queued && !ended) Fill();
            }
            output.Play();
        }
    }

    public void Stop()
    {
        lock (gate)
        {
            if (disposed) return;
            output.Stop();
            reader.DecodedPosition = 0;
            ended = false;
        }
    }

    // Under gate.
    void Fill()
    {
        int read = reader.ReadSamples(samples, 0, samples.Length);
        if (read < samples.Length && Loop)
        {
            reader.DecodedPosition = 0;
            read += reader.ReadSamples(samples, read, samples.Length - read);
        }
        if (read <= 0) { ended = true; return; }
        for (int i = 0; i < read; i++)
        {
            int v = (int)(samples[i] * 32767f);
            v = v > 32767 ? 32767 : v < -32768 ? -32768 : v;
            pcm[i * 2] = (byte)v;
            pcm[i * 2 + 1] = (byte)(v >> 8);
        }
        output.SubmitBuffer(pcm, 0, read * 2);
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            try { output.Stop(); output.Dispose(); } catch { }
            reader.Dispose();
        }
        lock (playing) playing.Remove(this);
    }
}
}
