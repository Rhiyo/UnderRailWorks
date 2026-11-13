// UnderRailWorks: the interface's scale and mapping, and Works' own settings, for mods.
using Microsoft.Xna.Framework;
using UnderRailWorks.Internal;

namespace UnderRailWorks
{
    public static class Interface
    {
        // Screen pixels per interface unit (Works' interface scale; 1 without it). Not Ui.Scale: that is the game's size tier
        // (Small 1, Large 1.5, Huge 1.75), which the layout already includes.
        public static float PixelsPerUnit { get { return UiScaleFix.Scale; } }
        // An area in interface units (where UI elements and the world's layers are laid out) in screen pixels.
        public static Rectangle ToPixels(Rectangle r) { return UiScaleFix.ToPixels(r); }
        // The game is drawing a save's screenshot right now (into a picture, not the screen).
        public static bool CapturingSaveScreenshot(Microsoft.Xna.Framework.Graphics.GraphicsDevice g) { return SaveScreenshot.Capturing(g); }
    }

    public static class WorksSettings
    {
        // A value from UnderRailWorks.ini (or, for keys that moved, the old UnderrailFix.ini); null if unset.
        public static string Get(string key) { return FixSettings.Raw(key); }
    }
}
