// Co-op's network layer (in the mod, not Works: docs/Multiplayer.md). Plain TCP, one message per line: words separated
// by tabs (a tab or newline inside a word is replaced by a space). Sockets read on background threads; received
// messages wait in a queue the game's thread empties (Poll), so game code only ever runs on the game's thread.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

sealed class Peer
{
    static int next;
    public readonly int Id = Interlocked.Increment(ref next);
    readonly TcpClient tcp;
    readonly StreamWriter writer;
    readonly object sendLock = new object();
    public volatile bool Closed;
    public string Name = "?";
    public string Address;

    public Peer(TcpClient tcp, Action<Peer, string[]> received, Action<Peer> closed)
    {
        this.tcp = tcp;
        tcp.NoDelay = true;
        Address = tcp.Client.RemoteEndPoint == null ? "?" : tcp.Client.RemoteEndPoint.ToString();
        var stream = tcp.GetStream();
        writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
        var reader = new StreamReader(stream, new UTF8Encoding(false));
        var t = new Thread(() =>
        {
            try
            {
                string line;
                while (!Closed && (line = reader.ReadLine()) != null)
                    if (line.Length > 0) received(this, line.Split('\t'));
            }
            catch (Exception) { }
            Close();
            closed(this);
        }) { IsBackground = true, Name = "Coop peer" };
        t.Start();
    }

    public void Send(params object[] words)
    {
        if (Closed) return;
        var line = string.Join("\t", words.Select(w => (w == null ? "" : Convert.ToString(w, System.Globalization.CultureInfo.InvariantCulture)).Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ')).ToArray());
        try { lock (sendLock) writer.WriteLine(line); }
        catch (Exception) { Close(); }
    }

    public void Close()
    {
        if (Closed) return;
        Closed = true;
        try { tcp.Close(); } catch { }
    }
}

// The messages from all peers, for the game's thread.
sealed class Inbox
{
    readonly Queue<KeyValuePair<Peer, string[]>> queue = new Queue<KeyValuePair<Peer, string[]>>();
    public void Add(Peer p, string[] words) { lock (queue) queue.Enqueue(new KeyValuePair<Peer, string[]>(p, words)); }
    public List<KeyValuePair<Peer, string[]>> Take() { lock (queue) { var l = queue.ToList(); queue.Clear(); return l; } }
}

sealed class Listener
{
    readonly TcpListener listener;
    public readonly int Port;
    public volatile bool Stopped;

    public Listener(int port, Action<TcpClient> accepted)
    {
        Port = port;
        listener = new TcpListener(IPAddress.Any, port);
        listener.Start();
        var t = new Thread(() =>
        {
            while (!Stopped)
            {
                try { accepted(listener.AcceptTcpClient()); }
                catch (Exception) { if (Stopped) break; Thread.Sleep(200); }
            }
        }) { IsBackground = true, Name = "Coop listener" };
        t.Start();
    }

    public void Stop() { Stopped = true; try { listener.Stop(); } catch { } }
}
