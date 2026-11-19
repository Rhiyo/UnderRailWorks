# Drawing in the world

How UnderRail draws a frame of the world, where Works lets a mod in, and which parts combine with other mods. The
reference for each member is [API.md](API.md); runnable code is in [Cookbook.md](Cookbook.md) ("Drawing in the world")
and the two drawing examples, `examples/Lantern` (lighting) and `examples/Hologram` (a layer drawn by a mod).

**See it for yourself:** type `works.drawtrace` in the game's console. The next frame is written step by step into
`drawtrace.txt` in the game folder: every layer in the order it was drawn, what it is, where it stands on the map,
its tint, and which mod coloured, routed or drew it. `works.drawstats` counts the same since the last call.

## One frame

```
1. world pass starts ............ into Works' picture while any mod has a PostProcess effect
2. tiles, back of the view to the front
     each thing on the tile, each of its layers:
        LayerColour  ->  LayerStarting  ->  RouteLayer  ->  DrawLayer (its look)  ->  drawn
     after a tile's floor: its extras (the grid, transition tiles, TileMarks)
3. WorldDone .................... Wall Vision puts its keyhole picture back here
   PostProcess effects .......... in load order, each on the one before's result
4. outline pass ................. Tab outlines (ShaderPassStarting / ShaderPassDone), not processed
5. interface .................... Hud, Ui windows, Ui.Overlay
```

A **layer** is one sprite: a floor tile, a wall piece, a prop, a character's body, its shadow, its selection marker.
`Layer.Kind` says which (Floor, Scenery, Body, Shadow, Marker, Shield, Outline).

Three things about the order surprise everyone, and the API answers each so you don't have to rely on them:

- **Draw order isn't depth.** A floor tile next to a character is often drawn *after* it, and a walking character is
  drawn after the wall piece on the square it's leaving. Don't decide "in front" by "drawn later": use
  `Layer.IsInFrontOf(character)` and `Layer.Covers(character)`. Rectangles aren't sprites either: a railing along the
  wall behind a character overlaps its rectangle, not its body. `Layer.SolidOverlap(other)` counts where both sprites
  actually show (Wall Vision opens a keyhole only when a piece drawn after a character overlaps its body that way).
- **A creature's layers are new every frame.** Don't keep a `Layer` or `LayerSet` between frames; key what you
  remember by the character (`layer.Entity.Character`).
- **Lighting is a tint.** The game multiplies each layer's colour by the light on its tiles just before drawing it.
  `LayerColour` sees that tint, and `Lights` reads or replaces the lighting.

## Two levels

**Composing (stable): use these first.** Every mod's changes show together, and none can break another's drawing or
Wall Vision's keyhole.

| You want | Use |
|---|---|
| Tiles marked on the floor (a range, an area, a path) | `TileMarks` |
| A character or scenery tinted, faded, darkened | `WorldDraw.LayerColour` (every mod's tints multiply) |
| Text or icons over the world at a map position | `Ui.Overlay` with `World.ToScreen` / `World.TileBounds` / `Character.ScreenBounds` |
| An effect over the finished world (lighting, colour grade, fog, blur) | `WorldDraw.PostProcess` |
| The game's lighting read, turned down or off | `Lights` (`GameLighting`, `At`, `Ambient`, `Sources`) |

**The world pass itself ([Preview]): built for Wall Vision.** `LayerStarting`, `RouteLayer`, `DrawLayer`,
`DrawTileExtras`, `WorldDone`, the shader-pass hooks, `Outlines` and the batch handles. They may change in any
release. Their rules:

- **A look (`DrawLayer`)** draws the layer itself with `context.Batch` (already begun) into whatever is set. Wall
  Vision may be drawing that very layer into its keyhole picture, so never begin or end batches or switch render
  targets in a look: Works stops a look that does. Only one mod draws a given layer.
- **A router (`RouteLayer`, `DrawTileExtras`)** decides where a layer goes (another render target, several, none) and
  draws it with `WorldDraw.LayerStep` / `TileExtrasStep`, so other mods' looks and tile marks still show. Only one mod
  routes a given layer: Wall Vision's keyhole routes everything after its split, so a second router doesn't combine
  with it.
- A handler that throws switches off all of that mod's drawing until the game restarts; the game's drawing goes on.

## Lighting and post-processing

`examples/Lantern` is a whole lighting mod in ~170 lines:

1. `Lights.GameLighting(this, () => on ? 0f : 1f)`: the game's lighting off (things drawn at full light). Requests
   from several mods multiply.
2. It works out the light on each tile near the player a few times a second: from `Lights.Sources(area)` (the game's
   light sources, found from the light they put on tiles) and a lantern on the player, stopped by walls
   (`Area.KindAt`) for shadows, plus `Lights.Ambient(area)` darkened.
3. `LayerColour` multiplies every layer by its tile's light: lit the way the game lights, so tall sprites are lit by
   where they stand, and Wall Vision's keyhole shows them lit too.
4. A `PostProcess` effect adds a soft glow around each light over the finished world.

A `PostProcess` effect gets a `WorldPicture`: `Input` (the world so far) and `Output` (set as the render target). Draw
the whole next picture (`CopyInput()` first if you only add to it) and return true; return false to pass the picture
on unchanged. Your own render targets: `picture.Scratch(...)`. Positions: `picture.ToPixels(mapPosition)`. While any
effect is added, the world pass draws into Works' picture (one screen-sized copy per effect per frame); Tab outlines
and the interface come after and aren't processed.

## Tested

`tools/smoketest/drawing.sh` runs Wall Vision's keyhole with a test mod's tints, tile marks and looks, then with
Lantern and Hologram, and checks from `works.drawstats` and a `works.drawtrace` that Wall Vision routed layers through
the other mods' looks, that their tints and marks reached its picture, and that Lantern's effect ran after it.
