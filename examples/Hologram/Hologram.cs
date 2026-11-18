// Hologram: an example of the world pass's low-level part (WorldDraw.DrawLayer, [Preview]; docs/Drawing.md). The
// player's character is drawn as a see-through blue hologram. A DrawLayer handler is a "look": it draws the layer
// itself, with the batch that's already begun (context.Batch), into whatever render target is set. It never begins or
// ends batches or switches targets, because Wall Vision may be drawing this very layer into its keyhole picture at that
// moment (Works stops a look that changes the target). Only the player's own sprite (LayerKind.Body) is changed: its
// shadow and selection marker stay as they are.
using Microsoft.Xna.Framework;
using UnderRailWorks;

[assembly: System.Reflection.AssemblyVersion("1.0.0")]

public sealed class HologramConfig : ModConfig
{
    [Header("Hologram")]
    [Label("Draw the player as a hologram")] public bool On = true;
}

public sealed class Hologram : WorksMod
{
    HologramConfig config;
    static readonly Color Tint = new Color(90, 210, 255) * 0.6f;     // premultiplied: 60% see-through

    public override void Load()
    {
        config = GetConfig<HologramConfig>();
        WorldDraw.DrawLayer.Add(this, (layer, context, view) =>
        {
            if (!config.On || layer.Kind != LayerKind.Body) return false;     // false: the game (or another mod) draws it
            var c = layer.Entity.Character;
            if (c == null || !c.IsPlayer) return false;
            layer.Draw(context, Tint);     // its sprite, in our colour, with the current batch
            return true;
        });
    }
}
