// Input helper for run-underrail: runs inside the game's Wine prefix (proton run) on gamescope's X display, so the
// Win32 calls below reach the game like real input. Coordinates are Wine screen pixels = game pixels in gamescope.
using System; using System.Runtime.InteropServices; using System.Threading;
class Input {
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out long p);
    [DllImport("user32.dll")] static extern void mouse_event(uint flags, int dx, int dy, int data, IntPtr extra);
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, IntPtr extra);
    [DllImport("user32.dll")] static extern uint MapVirtualKey(uint code, uint type);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string cls, string title);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr w);
    [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr w);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr w, int cmd);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr w, out uint pid);
    [DllImport("user32.dll")] static extern bool AttachThreadInput(uint from, uint to, bool attach);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr w, System.Text.StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr w, System.Text.StringBuilder s, int n);
    static string Describe(IntPtr w) {
        var t = new System.Text.StringBuilder(256); var c = new System.Text.StringBuilder(256); uint pid;
        GetWindowText(w, t, 256); GetClassName(w, c, 256); GetWindowThreadProcessId(w, out pid);
        return w + " '" + t + "' class " + c + " pid " + pid;
    }
    const uint LeftDown = 2, LeftUp = 4, RightDown = 8, RightUp = 16, Wheel = 0x800, KeyUp = 2;
    // The game's top-level window (SDL's title is the game's name).
    static IntPtr GameWindow() { IntPtr w = FindWindow(null, "Underrail"); return w != IntPtr.Zero ? w : FindWindow(null, "UnderRail"); }
    static byte Vk(string s) { return (byte)(s.StartsWith("0x") ? Convert.ToInt32(s, 16) : int.Parse(s)); }
    static void Down(byte vk) { keybd_event(vk, (byte)MapVirtualKey(vk, 0), 0, IntPtr.Zero); }
    static void Up(byte vk) { keybd_event(vk, (byte)MapVirtualKey(vk, 0), KeyUp, IntPtr.Zero); }
    static void Press(byte vk) { Down(vk); Thread.Sleep(60); Up(vk); Thread.Sleep(60); }
    // Proton swallows stdout: also written to C:\\input.log in the run's prefix (pfx/drive_c/input.log).
    static void Log(string s) { Console.WriteLine(s); try { System.IO.File.AppendAllText("C:\\input.log", s + "\n"); } catch { } }
    static void Main(string[] a) {
        for (int i = 0; i < a.Length; i++) {
            switch (a[i]) {
                case "move": SetCursorPos(int.Parse(a[++i]), int.Parse(a[++i])); Thread.Sleep(80); break;
                case "click": case "rclick": {
                    bool right = a[i] == "rclick";
                    SetCursorPos(int.Parse(a[++i]), int.Parse(a[++i])); Thread.Sleep(150);     // the game sees the hover first
                    mouse_event(right ? RightDown : LeftDown, 0, 0, 0, IntPtr.Zero); Thread.Sleep(80);
                    mouse_event(right ? RightUp : LeftUp, 0, 0, 0, IntPtr.Zero); Thread.Sleep(150); break; }
                case "ldown": SetCursorPos(int.Parse(a[++i]), int.Parse(a[++i])); Thread.Sleep(150); mouse_event(LeftDown, 0, 0, 0, IntPtr.Zero); Thread.Sleep(100); break;     // a drag: ldown, move..., lup
                case "lup": SetCursorPos(int.Parse(a[++i]), int.Parse(a[++i])); Thread.Sleep(150); mouse_event(LeftUp, 0, 0, 0, IntPtr.Zero); Thread.Sleep(150); break;
                case "wheel": mouse_event(Wheel, 0, 0, 120 * int.Parse(a[++i]), IntPtr.Zero); Thread.Sleep(100); break;
                case "key": Press(Vk(a[++i])); break;
                case "down": Down(Vk(a[++i])); break;
                case "up": Up(Vk(a[++i])); break;
                case "hold": { byte vk = Vk(a[++i]); Down(vk); Thread.Sleep(int.Parse(a[++i])); Up(vk); break; }
                case "type": foreach (char c in a[++i].ToUpperInvariant()) if (c == ' ') Press(0x20); else Press((byte)c); break;
                case "sleep": Thread.Sleep(int.Parse(a[++i])); break;
                case "focus": {     // make the game the active window: headless gamescope leaves another one active,
                                    // and then the game gets no keys and its fades/timers that need focus don't run
                    IntPtr w = GameWindow(), fg = GetForegroundWindow();
                    Log("game " + Describe(w) + "; foreground before " + Describe(fg));
                    if (w != IntPtr.Zero) {
                        uint pid, me = GetCurrentThreadId(), fgThread = GetWindowThreadProcessId(fg, out pid);
                        AttachThreadInput(me, fgThread, true);       // the foreground lock lets the foreground thread's
                        keybd_event(0x12, 0, 0, IntPtr.Zero); keybd_event(0x12, 0, KeyUp, IntPtr.Zero);   // input (and an Alt tap) through
                        ShowWindow(w, 5); BringWindowToTop(w); SetForegroundWindow(w);
                        AttachThreadInput(me, fgThread, false); Thread.Sleep(200);
                    }
                    Log("foreground after " + Describe(GetForegroundWindow())); break; }
                default: Console.WriteLine("unknown command: " + a[i]); return;
            }
        }
        long p; GetCursorPos(out p);
        Console.WriteLine("cursor " + (p & 0xffffffff) + "," + (p >> 32));
    }
}
