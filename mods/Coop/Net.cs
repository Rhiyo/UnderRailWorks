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

// A connection to another player: plain TCP (TcpPeer), or through the store's lobby (OnlinePeer: Works' Online).
abstract class Peer
{
    static int next;
    public readonly int Id = Interlocked.Increment(ref next);
    public volatile bool Closed;
    public string Name = "?";
    public string Address;

    public void Send(params object[] words)
    {
        if (Closed) return;
        SendLine(string.Join("\t", words.Select(w => (w == null ? "" : Convert.ToString(w, System.Globalization.CultureInfo.InvariantCulture)).Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ')).ToArray()));
    }
    protected abstract void SendLine(string line);
    public abstract void Close();
}

sealed class TcpPeer : Peer
{
    readonly TcpClient tcp;
    readonly StreamWriter writer;
    readonly object sendLock = new object();

    public TcpPeer(TcpClient tcp, Action<Peer, string[]> received, Action<Peer> closed)
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

    protected override void SendLine(string line)
    {
        try { lock (sendLock) writer.WriteLine(line); }
        catch (Exception) { Close(); }
    }

    public override void Close()
    {
        if (Closed) return;
        Closed = true;
        try { tcp.Close(); } catch { }
    }
}

// A player in our store lobby (or the lobby's host). A frame's messages go together as one packet (lines joined by
// newlines), sent with Works' Online.Send (relayed by Steam or GOG) when Flush is called (Coop, every frame); they
// arrive through Online.Received on the game's thread (Coop.LoadOnline).
sealed class OnlinePeer : Peer
{
    public readonly string Remote;     // their store id
    readonly StringBuilder pending = new StringBuilder();
    public OnlinePeer(string remote, string service) { Remote = remote; Address = service; }
    protected override void SendLine(string line) { lock (pending) { if (pending.Length > 0) pending.Append('\n'); pending.Append(line); } }
    public void Flush()
    {
        string text;
        lock (pending) { if (pending.Length == 0 || Closed) return; text = pending.ToString(); pending.Length = 0; }
        if (!UnderRailWorks.Online.Send(Remote, Encoding.UTF8.GetBytes(text))) Close();
    }
    public override void Close() { Flush(); Closed = true; }
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
