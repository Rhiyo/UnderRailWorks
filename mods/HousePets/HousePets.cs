// House Pets: an UnderRailWorks mod. Jyles, the Core City Housing Corporation's clerk (who already sells the house's
// furniture, turrets and renovations), gets a "pets" page in his room list: a cat or a dog, delivered to the player's
// house once its ground floor is renovated. Pets wander the living room on their own (the game's own idle AI); talking
// to one lets you pet it or name it. The pets are kept in the mod's save data and put in the house as placements each
// time the player enters (never in the game's save: the save stays loadable without the mod). Built from Works' API
// only (PlayerHouse, Dialogs.Patch, Dialogs.Define, World.Place, Ui.Ask, SaveData).
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using UnderRailWorks;

[assembly: System.Reflection.AssemblyVersion("1.0.0")]

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
        GameEvents.ZoneLoaded.Add(this, (zone, first) => { if (string.Equals(zone, PlayerHouse.Zone, StringComparison.OrdinalIgnoreCase)) PlacePets(); });
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
            if (World.Placed(Id(pet)) != null) continue;
            var at = good[random.Next(good.Count)];
            good.Remove(at);
            if (good.Count == 0) good = free.ToList();
            var number = pet.Number;
            var placement = new Placement
            {
                Id = Id(pet), Zone = PlayerHouse.Zone, Area = PlayerHouse.Area, X = at.X, Y = at.Y,
                Blueprint = pet.Kind == "dog" ? "creatures\\dog" : "creatures\\cat",
                Name = pet.Name ?? T(pet.Kind), Dialog = DialogOf(number),
                When = Condition.Code(() => PetNumber(number) != null && PlayerHouse.Renovated(HouseFloor.Main)),
            };
            placements[number] = placement;
            World.Place(placement);
            Dialogs.Define(DialogOf(number), b => Talk(b, number));
        }
    }

    string Id(Pet pet) { return "housepets_" + pet.Number; }
    static string DialogOf(int number) { return "housepets_pet" + number; }
    Pet PetNumber(int number) { return Pets.FirstOrDefault(x => x.Number == number); }

    void Talk(DialogBuilder b, int number)
    {
        var pet = PetNumber(number);
        if (pet == null) { b.Line("gone", "...").Option(T("leave"), Dialog.End); return; }
        bool cat = pet.Kind != "dog";
        if (pet.Name == null)
            b.Line("hello", T(cat ? "newCatHello" : "newDogHello"))
                .Option(T("name"), Dialog.End, Outcome.Code(() => AskName(number)))
                .Option(T("petNew"), "petted")
                .Option(T("leave"), Dialog.End);
        else
            b.Line("hello", T(cat ? "catHello" : "dogHello", pet.Name))
                .Option(T("pet", pet.Name), "petted")
                .Option(T("rename"), Dialog.End, Outcome.Code(() => AskName(number)))
                .Option(T("leave"), Dialog.End);
        b.Line("petted", T(cat ? "catPetted" : "dogPetted")).Option(T("leave"), Dialog.End);
    }

    void AskName(int number)
    {
        var pet = PetNumber(number);
        if (pet == null) return;
        var names = T(pet.Kind == "dog" ? "dogNames" : "catNames").Split(',');
        var kind = T(pet.Kind).ToLowerInvariant();
        Ui.Ask(this, "name", T("askTitle", kind), T("askQuestion"), pet.Name ?? names[random.Next(names.Length)], name =>
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
