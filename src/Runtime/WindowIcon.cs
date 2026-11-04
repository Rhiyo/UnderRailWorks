// The game window's icon, set explicitly from underrail.exe's own icon (resource 32512: 64x64 and 32x32). On Windows
// the XNA window already has it (the window class's icon); FNA's SDL window (Proton, the 64-bit mode) has no icon of
// its own, which Wine's X11 driver covers with the exe's icon but its Wayland driver doesn't: the task bar then shows
// none. WM_SETICON is the standard way, so it changes nothing visible on Windows. With UnderRailWorks the running exe is
// the launcher (no game icon: we don't ship the game's art), so the icon is read from the player's own game file,
// UnderRail.Game.exe (or underrail.exe.backup), loaded as data.
using System;
using System.Runtime.InteropServices;
using UnderRailWorks;

namespace UnderRailWorks.Internal
{

public static class WindowIcon
{
    const int IconResource = 32512, WM_SETICON = 0x80, ICON_SMALL = 0, ICON_BIG = 1, IMAGE_ICON = 1, LR_DEFAULTCOLOR = 0;
    const uint LOAD_LIBRARY_AS_DATAFILE = 0x2, LOAD_LIBRARY_AS_IMAGE_RESOURCE = 0x20;

    // End of the Game subclass's constructor (enu): the window exists by then (XNA and FNA create it in Game's).
    public static void Apply(object game)
    {
        if (!FixSettings.WindowIcon) return;
        try
        {
            IntPtr hwnd = FnaWindow.Hwnd(((Microsoft.Xna.Framework.Game)game).Window.Handle);
            IntPtr exe = IntPtr.Zero;
            foreach (string name in new[] { "UnderRail.Game.exe", "underrail.exe.backup" })
            {
                string path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name);
                if (System.IO.File.Exists(path)) exe = LoadLibraryEx(path, IntPtr.Zero, LOAD_LIBRARY_AS_DATAFILE | LOAD_LIBRARY_AS_IMAGE_RESOURCE);
                if (exe != IntPtr.Zero) break;
            }
            if (exe == IntPtr.Zero) exe = GetModuleHandle(null);
            IntPtr big = LoadImage(exe, (IntPtr)IconResource, IMAGE_ICON, 64, 64, LR_DEFAULTCOLOR);
            IntPtr small = LoadImage(exe, (IntPtr)IconResource, IMAGE_ICON, 32, 32, LR_DEFAULTCOLOR);
            if (hwnd == IntPtr.Zero || big == IntPtr.Zero) { Log.Write("window icon: not found (window " + hwnd + ", icon " + big + ")"); return; }
            SendMessage(hwnd, WM_SETICON, (IntPtr)ICON_BIG, big);
            SendMessage(hwnd, WM_SETICON, (IntPtr)ICON_SMALL, small != IntPtr.Zero ? small : big);
            Log.Write("window icon: set");
        }
        catch (Exception ex) { Log.Write("window icon: " + ex.Message); }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr LoadImage(IntPtr instance, IntPtr name, int type, int cx, int cy, int flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
}
}
