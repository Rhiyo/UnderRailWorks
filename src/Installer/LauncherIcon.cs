// The launcher (installed as underrail.exe) is a .NET apphost with .NET's default icon, and we can't ship the game's
// art. On Windows the patcher copies the icon resources from the player's own game exe into it at install (Win32
// resource APIs: the game's first RT_GROUP_ICON and the RT_ICON images it lists, under the launcher's own group
// name so Explorer and shortcuts pick it). Linux has no Explorer: nothing to do there. Any failure only costs the icon.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

static class LauncherIcon
{
    const uint LOAD_LIBRARY_AS_DATAFILE = 0x2;
    static readonly IntPtr RT_ICON = (IntPtr)3, RT_GROUP_ICON = (IntPtr)14;

    delegate bool EnumNames(IntPtr module, IntPtr type, IntPtr name, IntPtr param);
    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)] static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
    [DllImport("kernel32")] static extern bool FreeLibrary(IntPtr module);
    [DllImport("kernel32", CharSet = CharSet.Unicode)] static extern bool EnumResourceNames(IntPtr module, IntPtr type, EnumNames callback, IntPtr param);
    [DllImport("kernel32", CharSet = CharSet.Unicode)] static extern IntPtr FindResource(IntPtr module, IntPtr name, IntPtr type);
    [DllImport("kernel32")] static extern IntPtr LoadResource(IntPtr module, IntPtr info);
    [DllImport("kernel32")] static extern IntPtr LockResource(IntPtr data);
    [DllImport("kernel32")] static extern uint SizeofResource(IntPtr module, IntPtr info);
    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)] static extern IntPtr BeginUpdateResource(string path, bool deleteExisting);
    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool UpdateResource(IntPtr update, IntPtr type, IntPtr name, ushort language, byte[] data, uint size);
    [DllImport("kernel32", SetLastError = true)] static extern bool EndUpdateResource(IntPtr update, bool discard);

    // Resource names: an integer id (IS_INTRESOURCE) or a string; kept as the id or the string.
    static object Name(IntPtr name) { return ((long)name >> 16) == 0 ? (object)(int)name : Marshal.PtrToStringUni(name); }

    static List<object> Names(IntPtr module, IntPtr type)
    {
        var list = new List<object>();
        EnumResourceNames(module, type, (m, t, n, p) => { list.Add(Name(n)); return true; }, IntPtr.Zero);
        return list;
    }

    static byte[] Read(IntPtr module, IntPtr type, object name)
    {
        IntPtr info = name is int ? FindResource(module, (IntPtr)(int)name, type) : FindResourceByString(module, (string)name, type);
        if (info == IntPtr.Zero) return null;
        var size = SizeofResource(module, info);
        var bytes = new byte[size];
        Marshal.Copy(LockResource(LoadResource(module, info)), bytes, 0, (int)size);
        return bytes;
    }
    [DllImport("kernel32", CharSet = CharSet.Unicode, EntryPoint = "FindResourceW")] static extern IntPtr FindResourceByString(IntPtr module, string name, IntPtr type);

    public static void CopyFrom(string gameExe, string launcherExe)
    {
        try
        {
            IntPtr game = LoadLibraryEx(gameExe, IntPtr.Zero, LOAD_LIBRARY_AS_DATAFILE);
            if (game == IntPtr.Zero) return;
            byte[] group; var images = new Dictionary<int, byte[]>();
            try
            {
                var groups = Names(game, RT_GROUP_ICON);
                if (groups.Count == 0) return;
                group = Read(game, RT_GROUP_ICON, groups[0]);
                if (group == null || group.Length < 6) return;
                // GRPICONDIR: reserved, type, count; then 14-byte entries ending with the RT_ICON id (ushort at +12).
                int count = BitConverter.ToUInt16(group, 4);
                for (int i = 0; i < count; i++)
                {
                    int id = BitConverter.ToUInt16(group, 6 + i * 14 + 12);
                    var img = Read(game, RT_ICON, id);
                    if (img != null) images[id] = img;
                }
            }
            finally { FreeLibrary(game); }

            // The launcher's own group name (the one Explorer shows), else 32512 as the apphost uses.
            object target = 32512;
            IntPtr launcher = LoadLibraryEx(launcherExe, IntPtr.Zero, LOAD_LIBRARY_AS_DATAFILE);
            if (launcher != IntPtr.Zero) { var g = Names(launcher, RT_GROUP_ICON); if (g.Count > 0) target = g[0]; FreeLibrary(launcher); }

            IntPtr update = BeginUpdateResource(launcherExe, false);
            if (update == IntPtr.Zero) return;
            bool ok = true;
            foreach (var kv in images) ok &= UpdateResource(update, RT_ICON, (IntPtr)kv.Key, 0, kv.Value, (uint)kv.Value.Length);
            ok &= target is int ? UpdateResource(update, RT_GROUP_ICON, (IntPtr)(int)target, 0, group, (uint)group.Length)
                                : UpdateResourceByString(update, RT_GROUP_ICON, (string)target, 0, group, (uint)group.Length);
            EndUpdateResource(update, !ok);
            if (ok) Console.WriteLine("Gave underrail.exe the game's icon (" + images.Count + " sizes).");
        }
        catch (Exception ex) { Console.WriteLine("(The launcher keeps .NET's icon: " + ex.Message + ")"); }
    }
    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "UpdateResourceW")] static extern bool UpdateResourceByString(IntPtr update, IntPtr type, string name, ushort language, byte[] data, uint size);
}
