// 64-bit FNA mode (--experimental-64bit): the patcher points underrail.exe and a copy of this DLL (UnderrailFix64.dll)
// from XNA 4.0 to our FNA.dll, which has 64-bit builds; its native libraries are in the game folder's x64\.
// Works on Windows (.NET Framework; Microsoft XNA itself is 32-bit only) and under Proton (Wine Mono).
// - Natives: FNA has no search path of its own on .NET Framework, so x64\ is added with SetDllDirectory.
// - Graphics: FNA3D's D3D11 backend (native on Windows, DXVK under Proton). Its default SDL_GPU backend has the
//   Advanced-shader flicker bug (FNA-XNA/FNA3D#274). Not overridden if FNA3D_FORCE_DRIVER is already set.
// - Saves: the game's type resolver (e6e.av3) maps " FNA" / " WineMono.FNA" names of Point, Vector2, Vector3, Color
//   and Matrix itself, so saves move between the stock game and FNA mode both ways. Names written by the stock game on
//   Windows (Microsoft.Xna.Framework) or Proton (WineMono.FNA) are rewritten to our FNA first.
// - Window: the game treats Game.Window as a WinForms Form (FnaWindow) and Window.Handle as an HWND (tb.lg).
using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Xna.Framework;

public static class FnaMode
{
    public static readonly bool Active = typeof(Game).Assembly.GetName().Name == "FNA";
    static readonly string Fna = typeof(Game).Assembly.FullName;
    static readonly Regex Xna = new Regex(@"(Microsoft\.Xna\.Framework(\.\w+)?|WineMono\.FNA), Version=[\d.]+, Culture=neutral, PublicKeyToken=\w+");

    // Start of e6e.av3(typeName, assembly, throwIfMissing), for both strings.
    public static string SaveTypeName(string name)
    {
        if (name == null) return name;
        if (CoreCompat.Active) name = FrameworkNames.Read(name);
        return Active && Xna.IsMatch(name) ? Xna.Replace(name, Fna) : name;
    }

    // End of e6e.av5(type, out typeName, out assembly, bool), which names types when saving (BinaryFormatter, through the
    // binder dds, for saves and config.dat): our FNA's name is written as
    // the XNA 4.0 assembly the stock game uses for the type (Graphics types in .Graphics, Game types in .Game, the rest
    // in the core), which the stock game resolves on Windows (XNA) and under Proton (Wine Mono maps XNA to its FNA).
    // Only " FNA" names of five types would be understood otherwise; key bindings in config.dat are Keys.
    public static void SavedName(Type type, ref string typeName, ref string assembly)
    {
        if (!(Active || CoreCompat.Active) || type == null) return;
        if (assembly == null && typeName == null)
        {
            // The binder left it unnamed (most types): BinaryFormatter would then write type.Assembly.FullName and
            // type.FullName itself. Name it here when that would contain our FNA. Nothing else: on modern .NET the
            // formatter writes .NET Framework names itself (TypeForwardedFrom), and an ISerializable type may write
            // itself as another type (HashSet<string>'s StringEqualityComparer as GenericEqualityComparer<string>),
            // which naming the original type here would break (it did: saves the stock game can't read).
            if (!InvolvesFna(type)) return;
            string n = WrittenTypeName(type), a = WrittenAssembly(type);
            if (n != type.FullName || a != type.Assembly.FullName) { typeName = n; assembly = a; }
            return;
        }
        if (assembly == Fna) assembly = XnaAssembly(type.Namespace, type.Name);
        if (CoreCompat.Active) { assembly = FrameworkNames.Fix(assembly); typeName = FrameworkNames.Fix(typeName); }
        if (assembly != null && CoreCompat.Active && assembly == type.Assembly.FullName) assembly = WrittenAssembly(type);
        if (typeName != null && Active && typeName.Contains(Fna)) typeName = FnaName.Replace(typeName, m => m.Groups[1].Value + "." + m.Groups[2].Value + ", " + XnaAssembly(m.Groups[1].Value, m.Groups[2].Value));
    }

    // be3 (the game's own formatter) names a type the binder leaves unnamed (it does that for most types) as
    // type.Assembly.FullName and type.FullName; both are patched to come here.
    // (On modern .NET also .NET Framework names for .NET's own types: FrameworkNames, CoreCompat.cs.)
    public static string WrittenAssembly(Type type)
    {
        string assembly = type.Assembly.FullName;
        if (Active && assembly == Fna) return XnaAssembly(type.Namespace, type.Name);
        return FrameworkNames.Assembly(type) ?? assembly;
    }

    public static string WrittenTypeName(Type type)
    {
        if (CoreCompat.Active) return FrameworkNames.Name(type, WrittenAssembly);
        string name = type.FullName;
        return Active && name != null && name.Contains(Fna) ? FnaName.Replace(name, m => m.Groups[1].Value + "." + m.Groups[2].Value + ", " + XnaAssembly(m.Groups[1].Value, m.Groups[2].Value)) : name;
    }

    static bool InvolvesFna(Type t)
    {
        if (!Active) return false;
        while (t.IsArray) t = t.GetElementType();
        if (t.Assembly.FullName == Fna) return true;
        return t.IsGenericType && t.GetGenericArguments().Any(InvolvesFna);
    }

    // "Namespace.Name, FNA, Version=..." inside generic type names: group 1 namespace, 2 name.
    static readonly Regex FnaName = new Regex(@"(Microsoft\.Xna\.Framework(?:\.\w+)*)\.(\w+), " + Regex.Escape(Fna));

    static string XnaAssembly(string ns, string name)
    {
        string assembly = ns != null && ns.StartsWith("Microsoft.Xna.Framework.Graphics") ? "Microsoft.Xna.Framework.Graphics"
            : ns == "Microsoft.Xna.Framework" && (name.StartsWith("Game") || name == "GraphicsDeviceManager" || name == "DrawableGameComponent") ? "Microsoft.Xna.Framework.Game"
            : "Microsoft.Xna.Framework";
        return assembly + ", Version=4.0.0.0, Culture=neutral, PublicKeyToken=842cf8be1de50553";
    }

    public static void Startup()
    {
        if (!Active) return;
        SetDllDirectory(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "x64"));
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FNA3D_FORCE_DRIVER"))) Environment.SetEnvironmentVariable("FNA3D_FORCE_DRIVER", "D3D11");
        Log.Write("64-bit FNA mode: " + Fna + ", FNA3D driver " + Environment.GetEnvironmentVariable("FNA3D_FORCE_DRIVER"));
        DiagnosticsSettings();
    }

    // The game logs through Trace.WriteLine, whose first call loads the diagnostics settings from the app's config
    // files. In the 64-bit process inside Steam's Linux runtime container (Proton launched by Steam), Wine Mono's
    // config system fails there ("Couldn't impersonate token") and the game dies in tb.Initialize. The game has no
    // config file, so the settings are the defaults anyway: if loading them fails, mark them loaded (initState =
    // Initialized, no section) so Trace uses the defaults. Where loading works nothing changes.
    static void DiagnosticsSettings()
    {
        try { System.Configuration.ConfigurationManager.GetSection("system.diagnostics"); return; }
        catch (Exception ex) { Log.Write("diagnostics settings can't be loaded (" + ex.GetBaseException().Message + "); using the defaults"); }
        try
        {
            var config = typeof(System.Diagnostics.Trace).Assembly.GetType("System.Diagnostics.DiagnosticsConfiguration");
            var state = config.GetField("initState", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            state.SetValue(null, Enum.ToObject(state.FieldType, 2));     // InitState.Initialized
        }
        catch (Exception ex) { Log.Write("diagnostics settings: " + ex.Message); }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool SetDllDirectory(string path);
}

// tb's (Form)Control.FromHandle(Window.Handle): the constructor (centred, borderless), k8 (display mode: border style,
// borderless moves the window to its screen's corner) and la (window size). An FNA window is no Form, so the patch
// passes the GameWindow here and sends the Form members it uses to these methods.
public static class FnaWindow
{
    sealed class Window { public GameWindow W; }

    public static object Form(GameWindow window)
    {
        var form = Control.FromHandle(window.Handle) as Form;
        return form != null ? (object)form : new Window { W = window };
    }

    public static void SetStartPosition(object o, FormStartPosition value)
    {
        if (o is Form f) f.StartPosition = value;     // an FNA window starts centred
    }

    public static FormBorderStyle GetBorderStyle(object o)
    {
        if (o is Form f) return f.FormBorderStyle;
        return ((Window)o).W.IsBorderlessEXT ? FormBorderStyle.None : FormBorderStyle.Sizable;
    }

    public static void SetBorderStyle(object o, FormBorderStyle value)
    {
        if (o is Form f) f.FormBorderStyle = value;
        else ((Window)o).W.IsBorderlessEXT = value == FormBorderStyle.None;
    }

    public static System.Drawing.Point GetLocation(object o)
    {
        if (o is Form f) return f.Location;
        var b = ((Window)o).W.ClientBounds;
        return new System.Drawing.Point(b.X, b.Y);
    }

    public static void SetLocation(object o, System.Drawing.Point value)
    {
        if (o is Form f) f.Location = value;
        else SDL_SetWindowPosition(((Window)o).W.Handle, value.X, value.Y);
    }

    public static int GetWidth(object o) { return o is Control c ? c.Width : ((Window)o).W.ClientBounds.Width; }
    public static int GetHeight(object o) { return o is Control c ? c.Height : ((Window)o).W.ClientBounds.Height; }

    // End of tb.lg(): the window handle bso gives to ClientToScreen for cursor warps. FNA's is an SDL_Window*.
    public static IntPtr Hwnd(IntPtr handle)
    {
        if (!FnaMode.Active || handle == IntPtr.Zero) return handle;
        try
        {
            IntPtr hwnd = SDL_GetPointerProperty(SDL_GetWindowProperties(handle), "SDL.window.win32.hwnd", IntPtr.Zero);
            return hwnd != IntPtr.Zero ? hwnd : handle;
        }
        catch (Exception ex) { Log.Write("window handle: " + ex.Message); return handle; }
    }

    [DllImport("SDL3", CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] static extern bool SDL_SetWindowPosition(IntPtr window, int x, int y);
    [DllImport("SDL3", CallingConvention = CallingConvention.Cdecl)] static extern uint SDL_GetWindowProperties(IntPtr window);
    [DllImport("SDL3", CallingConvention = CallingConvention.Cdecl)] static extern IntPtr SDL_GetPointerProperty(uint props, string name, IntPtr fallback);
}
