// Example mod: a new character in an existing map, with a conversation and a quest, through Works' API only
// (World.Place, Dialogs, Quests, Inventory, Variables). Nothing of it goes into the save but plain variables and the
// quest's state: the stranger is placed again each time the area loads.
using System;
using UnderRailWorks;

[assembly: System.Reflection.AssemblyVersion("1.0.0")]

public sealed class TheStranger : WorksMod
{
    const string Quest = "stranger_debt", Paid = "stranger_paid", Met = "stranger_met";
    const int Price = 10;

    public override void Load()
    {
        Quests.Define(this, Quest, "South Gate Station", "A stranger at the north gate says he's owed " + Price + " charons by someone who looks just like me. Pay him, or don't.", xp: 75, oddityXp: 1);

        Dialogs.Define(this, "stranger/talk", d =>
        {
            d.Branch("entry")
                .If(Condition.Flag(Paid), "after")
                .If(Condition.Quest(Quest, QuestState.Active), "again")
                .Else("first");

            d.Line("first", "You. Yeah, you. Last week a fella with your face borrowed " + Price + " charons off me and walked through that gate.", Outcome.SetFlag(Met))
                .Option("That wasn't me.", "denial")
                .Option("Fine. Here's your money.", "pay", Condition.Code(CanPay))
                .Option("I'll think about it.", "think", Outcome.StartQuest(Quest));
            d.Line("denial", "Sure it wasn't. Same scowl, same boots. " + Price + " charons and we forget it.")
                .Option("Fine, here.", "pay", Condition.Code(CanPay))
                .Option("Later.", "think", Outcome.StartQuest(Quest));
            d.Line("think", "Don't think too long.").Next(Dialog.End);
            d.Line("again", "Got my " + Price + " charons?")
                .OptionGreyed("Here. (" + Price + " charons)", "pay", Condition.Code(CanPay))
                .Option("Not yet.", Dialog.End);
            d.Line("pay", "Pleasure doing business. Here, for your trouble: found these by the tracks.",
                    Outcome.Code(Pay), Outcome.SetFlag(Paid), Outcome.CompleteQuest(Quest))
                .Next(Dialog.End);
            d.Line("after", "We're square. Watch out for your twin.").Next(Dialog.End);
        });

        World.Place(new Placement
        {
            Id = "stranger_npc", Zone = "LU-SouthGateStation-N", Area = "LU-SouthGateStation-N", X = 14, Y = 21,
            Blueprint = "creatures\\generichumans\\foundryworker", Name = "Stranger", Dialog = "stranger/talk",
        });
    }

    static bool CanPay() { var p = World.Player; return p != null && p.Inventory.Money >= Price; }

    static void Pay()
    {
        var p = World.Player;
        if (Quests.State(Quest) == QuestState.NotStarted) Quests.Start(Quest, false);     // paid at the first meeting
        p.Inventory.TakeMoney(Price);
        p.Inventory.Add("consumables\\medicine\\bandage", 2);
    }
}
