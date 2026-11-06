// UnderRailWorks: the player's house in Core City (zone cc_ph, area cc_ph1), bought from the Core City Housing
// Corporation and furnished through its clerk Jyles (dialogs\characters\cc_jyles). Read-only: the game keeps the house
// in the save (global property cc_ph_houseState: one value per house element, e.g. MainFloorDebris "cleared" once the
// ground floor is renovated, UpperFloor_Bedroom_Bed "black"), and on entering the zone copies the furniture for each
// value from a hidden map layer into the zone. Mods add their own things with placements (never saved), not elements:
// the element list is fixed and anything put there is written into the save.
//   if (PlayerHouse.Renovated(HouseFloor.Main)) World.Place(new Placement { Zone = PlayerHouse.Zone, Area = PlayerHouse.Area, ... });
//   PlayerHouse.Element("UpperFloor_Bedroom_Bed")   // "black", "regular"... or null
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using TimelapseVertigo.Playfield.CustomLogic.Data;

namespace UnderRailWorks
{
    public enum HouseFloor { Main, Upper, Lower }
    // Rooms of the ground floor (area cc_ph1), measured from its walkable tiles.
    public enum HouseRoom { EntranceHall, LivingRoom, Kitchen }

    public static class PlayerHouse
    {
        public const string Zone = "cc_ph";
        public const string Area = "cc_ph1";
        public const string KeyCode = "key.playerHouse";

        // Each house element (the game's names, e.g. "MainFloor_Kitchen_Table") and its value; empty before the house.
        public static IDictionary<string, string> Elements
        {
            get
            {
                var d = State("cc_ph_houseState");
                return d == null ? new Dictionary<string, string>() : d.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value);
            }
        }

        // One element's value (null: not set, or no such element).
        public static string Element(string name)
        {
            PlayerHouseElement e; string v;
            var d = State("cc_ph_houseState");
            return d != null && Enum.TryParse(name, true, out e) && d.TryGetValue(e, out v) ? v : null;
        }

        // Sets an element as Jyles' orders do (e.g. "MainFloorDebris", "cleared"; null removes it); the house changes the
        // next time its zone loads. Only values the game itself uses: the table is in the game's save, and a value with
        // no furniture tagged for it just removes what was there. False: no game, or no such element.
        public static bool SetElement(string name, string value)
        {
            PlayerHouseElement e;
            if (!Enum.TryParse(name, true, out e) || !Enum.IsDefined(typeof(PlayerHouseElement), e)) return false;
            var game = bpo.GetService<cnk>();
            if (game == null || game.aqu() == null) return false;
            var d = d39.a(game.aqu(), null);
            if (value == null) d.Remove(e); else d[e] = value;
            if (!string.IsNullOrEmpty(value)) d39.a(game.aqu()).Add(value);
            return true;
        }

        // The names of all house elements the game knows (whether set or not).
        public static IList<string> ElementNames { get { return Enum.GetNames(typeof(PlayerHouseElement)); } }

        // The player owns the house: has its keycard, or anything in it was bought.
        public static bool Owned
        {
            get
            {
                var p = World.Player;
                if (p != null && p.Inventory.Has(KeyCode, 1, true)) return true;
                var d = State("cc_ph_houseState");
                return d != null && d.Count > 0;
            }
        }

        // A floor renovated by the Housing Corporation (its debris cleared; until then it's rubble).
        public static bool Renovated(HouseFloor floor)
        {
            var e = floor == HouseFloor.Main ? "MainFloorDebris" : floor == HouseFloor.Upper ? "UpperFloorDebris" : "LowerFloorDebris";
            return Element(e) == "cleared";
        }

        // The tiles of a floor (area cc_ph1) the renovation clears: inside the house, for placing things.
        public static Rectangle FloorTiles(HouseFloor floor)
        {
            // dxt: the debris rectangles cleared when a floor is renovated (x, y, right, bottom inclusive).
            return floor == HouseFloor.Main ? new Rectangle(11, 12, 6, 8) : floor == HouseFloor.Upper ? new Rectangle(7, 0, 7, 5) : new Rectangle(3, 0, 5, 4);
        }

        // A ground-floor room's tiles (area cc_ph1, measured in game: walls around them; doorways lead out).
        public static Rectangle RoomTiles(HouseRoom room)
        {
            switch (room)
            {
                case HouseRoom.LivingRoom: return new Rectangle(1, 12, 10, 7);     // x 1-10, y 12-18 (the rug, the furniture set)
                case HouseRoom.Kitchen: return new Rectangle(1, 7, 7, 4);          // x 1-7, y 7-10 (refrigerator, freezer)
                default: return new Rectangle(12, 12, 4, 6);                       // x 12-15, y 12-17 (from the front door)
            }
        }

        // The game's security level for the house (turrets, patrols, cameras); burglars only come below 2.
        public static int SecurityLevel { get { return Variables.Global.Get("loc_cc_ph_securityLevel", 0); } }

        // Things bought for the house (the values Jyles' orders set, e.g. "securityCameras_installed").
        public static bool HasItem(string item)
        {
            var s = Global<HashSet<string>>("cc_ph_ownedItems");
            return s != null && item != null && s.Contains(item);
        }

        // Read without creating: the game's own getter (d39.a) adds an empty table when there is none.
        static Dictionary<PlayerHouseElement, string> State(string name) { return Global<Dictionary<PlayerHouseElement, string>>(name); }

        static T Global<T>(string name) where T : class
        {
            try
            {
                var game = bpo.GetService<cnk>();
                var props = game == null || game.aqu() == null ? null : game.aqu().r();
                return props == null ? null : props.GetPropertyValue(name) as T;
            }
            catch (Exception ex) { Internal.Log.Write("house: " + ex.Message); return null; }
        }
    }
}
