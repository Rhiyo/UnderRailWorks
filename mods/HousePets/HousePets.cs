// House Pets: an UnderRailWorks mod. Jyles, the Core City Housing Corporation's clerk (who already sells the house's
// furniture, turrets and renovations), gets a "pets" page in his room list: a cat or a dog, delivered to the player's
// house once its ground floor is renovated. Pets wander the living room on their own (the game's own idle AI); talking
// to one lets you pet it, name it, feed it, or take it along (it follows you between areas until you send it home;
// a pet about to be badly hurt bolts for home instead). The pets are kept in the mod's save data and put in the house as placements each
// time the player enters (never in the game's save: the save stays loadable without the mod). Built from Works' API
// only (PlayerHouse, Dialogs.Patch, Dialogs.Add, World.Place, Ui.Ask, SaveData).
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using UnderRailWorks;

[assembly: System.Reflection.AssemblyVersion("1.1.0")]

public sealed class HousePetsConfig : ModConfig
{
    [Header("House Pets")]
    [Label("Cat price"), Range(0, 2000), Increment(50)] public int CatPrice = 200;
    [Label("Dog price"), Range(0, 2000), Increment(50)] public int DogPrice = 350;
    [Label("Most pets in the house"), Range(1, 6), Increment(1)] public int MaxPets = 4;
}

public sealed class HousePets : WorksMod
{
    sealed class Pet { public int Number; public string Kind; public string Name; }

    HousePetsConfig config;
    readonly Dictionary<int, Placement> placements = new Dictionary<int, Placement>();
    // Pets out with the player (never saved: a loaded game has them at home), and which character each pet is now.
    readonly HashSet<int> withPlayer = new HashSet<int>();
    readonly Dictionary<Character, int> active = new Dictionary<Character, int>();
    static readonly Random random = new Random();

    SaveData Saved { get { return SaveData.Of(this); } }

    List<Pet> Pets
    {
        get
        {
            var list = new List<Pet>();
            int count = Saved.Get("count", 0);
            for (int i = 1; i <= count; i++)
            {
                var kind = Saved.Get<string>("kind." + i, null);
                if (kind != null) list.Add(new Pet { Number = i, Kind = kind, Name = Saved.Get<string>("name." + i, null) });
            }
            return list;
        }
    }

    public override void Load()
    {
        config = GetConfig<HousePetsConfig>();
        Dialogs.Patch(this, "characters\\cc_jyles", p =>
        {
            p.AddOption("What room do you want to start from", T("ask"), "pitch");
            p.Branch("pitch").If(Condition.Code(() => Pets.Count >= config.MaxPets), "full").Else("catalog");
            p.Line("full", T("full")).Option(T("back"), "@");
            p.Line("catalog", T("pitch"))
                .OptionGreyed(T("buyCat", config.CatPrice), "bought", Condition.Code(() => Money() >= config.CatPrice), Outcome.Code(() => Buy("cat", config.CatPrice)))
                .OptionGreyed(T("buyDog", config.DogPrice), "bought", Condition.Code(() => Money() >= config.DogPrice), Outcome.Code(() => Buy("dog", config.DogPrice)))
                .Option(T("later"), "@");
            p.Line("bought", T("bought")).Option(T("thanks"), "@");
        });
        GameEvents.ZoneLoaded.Add(this, (zone, first) =>
        {
            // Characters from the zone left behind are gone; a pet that was with the player but didn't arrive (left
            // behind, or the game reloaded) went home. (Followers arrive before ZoneLoaded.)
            foreach (var gone in active.Keys.Where(c => c == null || c.Area == null).ToList()) active.Remove(gone);
            withPlayer.RemoveWhere(n => Current(n) == null);
            if (string.Equals(zone, PlayerHouse.Zone, StringComparison.OrdinalIgnoreCase)) PlacePets();
        });
        GameEvents.GameStarted.Add(this, () => { withPlayer.Clear(); active.Clear(); });
        GameEvents.FollowerArrived.Add(this, (now, old) => { int n; if (old != null && active.TryGetValue(old, out n)) { active.Remove(old); active[now] = n; } });
        // A pet about to be badly hurt runs home instead (the blow misses it).
        Combat.ModifyIncomingDamage.Add(this, d =>
        {
            int n;
            if (d.Target == null || !active.TryGetValue(d.Target, out n)) return;
            int total = d.Parts.Sum(p => p.Amount);
            if (d.Target.Health - total > d.Target.MaxHealth / 3) return;
            d.Cancel = true;
            var pet = PetNumber(n);
            Notify.Over(d.Target, T("bolts", pet == null ? T("cat") : pet.Name ?? T(pet.Kind)));
            SendHome(n, d.Target, true);
        });
    }

    int Money() { var p = World.Player; return p == null ? 0 : p.Inventory.Money; }

    void Buy(string kind, int price)
    {
        var p = World.Player;
        if (p == null || !p.Inventory.TakeMoney(price)) return;
        int n = Saved.Get("count", 0) + 1;
        Saved.Set("count", n);
        Saved.Set("kind." + n, kind);
        Log.Info("bought a " + kind + " (pet " + n + ")");
    }

    // In the house: each pet on a free tile of the living room (open on most sides: a tile hemmed in by
    // furniture leaves it standing still). A pet already there stays where it wandered to.
    void PlacePets()
    {
        if (!PlayerHouse.Renovated(HouseFloor.Main)) return;
        var room = PlayerHouse.RoomTiles(HouseRoom.LivingRoom);
        var player = World.Player;
        if (player == null || player.Area == null || player.Area.Name != PlayerHouse.Area) return;
        var free = World.FreeTilesNear(room.Center.X, room.Center.Y, 7).Where(t => room.Contains(t)).ToList();
        var open = new HashSet<Point>(free);
        var good = free.Where(t => new[] { new Point(t.X + 1, t.Y), new Point(t.X - 1, t.Y), new Point(t.X, t.Y + 1), new Point(t.X, t.Y - 1) }.Count(open.Contains) >= 3).ToList();
        if (good.Count == 0) good = free;
        if (good.Count == 0) { Log.Info("no free tile for pets"); return; }
        foreach (var pet in Pets)
        {
            if (withPlayer.Contains(pet.Number)) continue;
            var here = World.Placed(Id(pet));
            if (here != null) { active[here] = pet.Number; continue; }
            var at = good[random.Next(good.Count)];
            good.Remove(at);
            if (good.Count == 0) good = free.ToList();
            var number = pet.Number;
            var placement = new Placement
            {
                Id = Id(pet), Zone = PlayerHouse.Zone, Area = PlayerHouse.Area, X = at.X, Y = at.Y,
                Blueprint = pet.Kind == "dog" ? "creatures\\dog" : "creatures\\cat",
                Name = pet.Name ?? T(pet.Kind), Dialog = DialogOf(number),
                When = Condition.Code(() => PetNumber(number) != null && PlayerHouse.Renovated(HouseFloor.Main) && !withPlayer.Contains(number)),
            };
            placements[number] = placement;
            World.Place(placement);
            var placed = World.Placed(placement.Id);
            if (placed != null) active[placed] = number;
            Dialogs.Add(this, DialogOf(number), b => Talk(b, number));
        }
    }

    string Id(Pet pet) { return "housepets_" + pet.Number; }
    static string DialogOf(int number) { return "housepets_pet" + number; }
    Pet PetNumber(int number) { return Pets.FirstOrDefault(x => x.Number == number); }

    void Talk(DialogBuilder b, int number)
    {
        var pet = PetNumber(number);
        if (pet == null) { b.Line("gone", "...").Option(T("leave"), Dialog.End); return; }
        bool cat = pet.Kind != "dog", out_ = withPlayer.Contains(number);
        var line = pet.Name == null
            ? b.Line("hello", T(cat ? "newCatHello" : "newDogHello")).Option(T("name"), Dialog.End, Outcome.Code(() => AskName(number))).Option(T("petNew"), "petted")
            : b.Line("hello", T(cat ? "catHello" : "dogHello", pet.Name)).Option(T("pet", pet.Name), "petted").Option(T("rename"), Dialog.End, Outcome.Code(() => AskName(number)));
        line.Option(T("feedMeat"), "fed", Condition.Code(() => Has("food.hopperMeat")), Outcome.Code(() => Feed("food.hopperMeat")))
            .Option(T("feedSteak"), "fedSteak", Condition.Code(() => Has("food.caveHopperSteak")), Outcome.Code(() => Feed("food.caveHopperSteak")));
        if (out_) line.Option(T("goHome"), Dialog.End, Outcome.Code(() => SendHome(number, Current(number))));
        else line.Option(T("comeAlong"), Dialog.End, Outcome.Code(() => TakeAlong(number)));
        line.Option(T("leave"), Dialog.End);
        b.Line("petted", T(cat ? "catPetted" : "dogPetted")).Option(T("leave"), Dialog.End);
        b.Line("fed", T(cat ? "catFed" : "dogFed")).Option(T("leave"), Dialog.End);
        b.Line("fedSteak", T(cat ? "catFedSteak" : "dogFedSteak")).Option(T("leave"), Dialog.End);
    }

    static bool Has(string code) { var p = World.Player; return p != null && p.Inventory.Has(code); }
    static void Feed(string code) { var p = World.Player; if (p != null) p.Inventory.Take(code, 1); }
    Character Current(int number) { return active.Where(kv => kv.Value == number && kv.Key != null && kv.Key.Area != null && !kv.Key.IsDead).Select(kv => kv.Key).FirstOrDefault(); }

    // Follows the player, to other areas too, until sent home.
    void TakeAlong(int number)
    {
        var c = Current(number); var p = World.Player;
        if (c == null || p == null) return;
        withPlayer.Add(number);
        c.Follow(p, 2, true);
    }

    // Stops following. In the house it stays where it is; elsewhere it heads home (gone from here, back in the house
    // the next time the player comes in).
    void SendHome(int number, Character c, bool quiet = false)
    {
        withPlayer.Remove(number);
        if (c == null) return;
        c.StopFollowing();
        if (string.Equals(World.ZoneId, PlayerHouse.Zone, StringComparison.OrdinalIgnoreCase)) return;
        var pet = PetNumber(number);
        if (!quiet) Notify.OverPlayer(T("wentHome", pet == null ? "" : pet.Name ?? T(pet.Kind)));
        active.Remove(c);
        World.Despawn(c);
    }

    void AskName(int number)
    {
        var pet = PetNumber(number);
        if (pet == null) return;
        var names = T(pet.Kind == "dog" ? "dogNames" : "catNames").Split(',');
        var kind = T(pet.Kind).ToLowerInvariant();
        Ui.Ask(this, T("askQuestion", kind), pet.Name ?? names[random.Next(names.Length)], name =>
        {
            Saved.Set("name." + number, name);
            var c = World.Placed("housepets_" + number);
            if (c != null) c.Name = name;
            Placement placement;
            if (placements.TryGetValue(number, out placement)) placement.Name = name;
            Notify.OverPlayer(T("named", kind, name));
        });
    }
}
