// Contract Board: an UnderRailWorks mod. A contractor at the north gate of South Gate Station offers generated jobs:
// bounties (put down a number of hostiles) and deliveries (bring supplies). Each taken job is a real quest under
// "Contracts" in the journal, with its progress in the text (the Quest Tracker shows it live), paid in charons and XP
// when turned in. Built from Works' API only: World.Place, Dialogs (options built from the current state), Quests,
// Text, Variables, Combat and inventory events.
// The game can't restart a quest once it's done, so contracts come from a pool of quest ids, one per job; all state is
// in plain variables (saved, ignored by the stock game).
using System;
using System.Collections.Generic;
using System.Linq;
using UnderRailWorks;

[assembly: System.Reflection.AssemblyVersion("1.0.0")]

public sealed class ContractBoard : WorksMod
{
    const int Pool = 60;
    const string Next = "cb_next";     // the number of the next contract

    sealed class Job
    {
        public int Number; public string Kind, Target; public int Count, Progress, Reward;
        public string Describe()
        {
            return Kind == "bounty" ? "Bounty: put down " + Count + " hostile" + (Count > 1 ? "s" : "")
                : "Delivery: bring " + Count + " " + (Items.Name(Target) ?? Target);
        }
        public string Encode() { return Kind + "|" + Target + "|" + Count + "|" + Progress + "|" + Reward; }
        public static Job Decode(int n, string s)
        {
            var p = (s ?? "").Split('|');
            if (p.Length < 5) return null;
            return new Job { Number = n, Kind = p[0], Target = p[1], Count = int.Parse(p[2]), Progress = int.Parse(p[3]), Reward = int.Parse(p[4]) };
        }
    }

    static readonly string[] Supplies = { "consumables\\food\\cavehoppermeat", "consumables\\food\\cannedmeat", "consumables\\medicine\\bandage" };

    static ContractBoard me;     // the static helpers' owner for Text.Set
    public override void Load()
    {
        me = this;
        for (int i = 1; i <= Pool; i++) Quests.Add(this, QuestId(i), "Contracts", "A contract from the gate.", xp: 40, oddityXp: 1);

        Dialogs.Add(this, "contracts/board", d =>
        {
            var line = d.Line("start", "Got work, if you can handle it. Pays in charons.");
            foreach (var job in ActiveJobs())
            {
                var j = job;
                if (Ready(j)) line.Option("Turn in - " + j.Describe() + " (" + j.Reward + " charons)", "paid", Outcome.Code(() => TurnIn(j)));
                else line.OptionGreyed("Turn in - " + j.Describe() + " (" + Progress(j) + "/" + j.Count + ")", "paid", Condition.Code(() => false));
            }
            if (ActiveJobs().Count < 3)
                foreach (var offer in Offers())
                {
                    var o = offer;
                    line.Option("Take - " + o.Describe() + " (" + o.Reward + " charons)", "taken", Outcome.Code(() => Take(o)));
                }
            line.Option("Nothing right now.", Dialog.End);
            d.Line("taken", "Good. Come back when it's done.").Next(Dialog.End);
            d.Line("paid", "Clean work. Here's your pay.").Next(Dialog.End);
        });

        World.Place(new Placement
        {
            Id = "contracts_board", Zone = "LU-SouthGateStation-N", Area = "LU-SouthGateStation-N", X = 11, Y = 20,
            Blueprint = "creatures\\generichumans\\sgsoldier1", Name = "Contractor", Dialog = "contracts/board",
        });

        Combat.AttackResolved.Add(this, o =>
        {
            if (!o.Killed || !o.ByPlayer || o.Target == null || !o.Target.IsHostile) return;     // bounties are for hostiles, not bystanders
            foreach (var j in ActiveJobs().Where(x => x.Kind == "bounty" && x.Progress < x.Count))
            {
                j.Progress++;
                Save(j);
                if (j.Progress == j.Count) Notify.OverPlayer("Bounty done: see the contractor", null);
            }
        });
        GameEvents.ItemAdded.Add(this, item => RefreshTexts());
        GameEvents.GameLoaded.Add(this, RefreshTexts);
        GameEvents.ZoneLoaded.Add(this, (zone, first) => RefreshTexts());
    }

    static string QuestId(int n) { return "contracts_" + n; }
    static string Var(int n) { return "cb_c" + n; }

    static List<Job> ActiveJobs()
    {
        var list = new List<Job>();
        if (!Variables.Global.Available) return list;
        foreach (var id in Quests.Active.Where(q => q.StartsWith("contracts_")))
        {
            int n;
            if (!int.TryParse(id.Substring("contracts_".Length), out n)) continue;
            var j = Job.Decode(n, Variables.Global.Get<string>(Var(n), null));
            if (j != null) list.Add(j);
        }
        return list;
    }

    // Three offers, the same until one is taken (seeded by the next contract's number).
    static List<Job> Offers()
    {
        int next = Variables.Global.Get(Next, 1);
        if (next > Pool) return new List<Job>();
        var r = new Random(next * 7919);
        var offers = new List<Job>();
        for (int i = 0; i < 3; i++)
        {
            if (r.Next(2) == 0) { int c = r.Next(2, 6); offers.Add(new Job { Kind = "bounty", Target = "", Count = c, Reward = 15 * c + r.Next(10) }); }
            else
            {
                var path = Supplies[r.Next(Supplies.Length)];
                int c = r.Next(1, 4);
                offers.Add(new Job { Kind = "delivery", Target = path, Count = c, Reward = 12 * c + r.Next(8) });
            }
        }
        return offers;
    }

    static int Progress(Job j)
    {
        if (j.Kind == "bounty") return j.Progress;
        var p = World.Player;
        return p == null ? 0 : Math.Min(j.Count, p.Inventory.Count(Items.Code(j.Target) ?? "", false));
    }
    static bool Ready(Job j) { return Progress(j) >= j.Count; }

    static void Take(Job offer)
    {
        int n = Variables.Global.Get(Next, 1);
        if (n > Pool) return;
        offer.Number = n;
        Variables.Global.Set(Next, n + 1);
        Save(offer);
        Quests.Start(QuestId(n));
    }

    static void TurnIn(Job j)
    {
        var p = World.Player;
        if (p == null || !Ready(j)) return;
        if (j.Kind == "delivery") p.Inventory.Take(Items.Code(j.Target), j.Count);
        p.Inventory.AddMoney(j.Reward);
        Quests.Complete(QuestId(j.Number));
    }

    static void Save(Job j) { Variables.Global.Set(Var(j.Number), j.Encode()); SetText(j); }

    static void SetText(Job j)
    {
        Text.Set(me, "quests", QuestId(j.Number), "The contractor at the north gate: " + j.Describe() + ". Progress: " + Progress(j) + "/" + j.Count + ". Pays " + j.Reward + " charons.");
    }

    static void RefreshTexts() { foreach (var j in ActiveJobs()) SetText(j); }
}
