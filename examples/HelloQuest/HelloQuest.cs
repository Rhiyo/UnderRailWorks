// Example mod: a quest and a conversation with Works' content API (Quests, Dialogs, Variables, Notify). F11 talks to
// the nearest character the player can see; the conversation gives the quest, and asking again completes it.
using System;
using System.Linq;
using Microsoft.Xna.Framework.Input;
using UnderRailWorks;

[assembly: System.Reflection.AssemblyVersion("1.0.0")]

public sealed class HelloQuest : WorksMod
{
    Keybind talkKey;

    public override void Load()
    {
        talkKey = Keybinds.Add(this, "talk", "Talk to nearest", Keys.End);
        Quests.Add(this, "helloquest_favour", "South Gate Station", "Someone asked me for a small favour. Ask them about it again.", xp: 50, oddityXp: 1);
        Dialogs.Add(this, "helloquest/talk", d =>
        {
            d.Branch("entry")
                .If(Condition.Quest("helloquest_favour", QuestState.Completed), "done")
                .If(Condition.Quest("helloquest_favour", QuestState.Active), "waiting")
                .Else("start");
            d.Line("start", "Hey. You look like someone who gets things done.")
                .Option("What do you need?", "ask", Outcome.StartQuest("helloquest_favour"), Outcome.SetFlag("helloquest_asked"))
                .Option("Not now.", Dialog.End);
            d.Line("ask", "Just talk to me again in a moment. That's the favour.").Next(Dialog.End);
            d.Line("waiting", "Back already?")
                .Option("Favour done.", "thanks", Outcome.CompleteQuest("helloquest_favour"))
                .Option("Bye.", Dialog.End);
            d.Line("thanks", "Appreciated. Times visited: $(context.helloquest_visits).", Outcome.AddInt("helloquest_visits", 1)).Next(Dialog.End);
            d.Line("done", "We're square.").Next(Dialog.End);
        });
        GameEvents.Update.Add(this, seconds =>
        {
            if (!talkKey.Pressed) return;
            var player = World.Player;
            var npc = player == null ? null : World.Visible.Where(c => c.CanTalk && c.Area == player.Area)
                .OrderBy(c => (c.Position - player.Position).LengthSquared()).FirstOrDefault();
            if (npc == null) { Notify.OverPlayer("Nobody to talk to."); return; }
            Log.Info("talking to " + npc.Name + ": " + Dialogs.Start(npc, "helloquest/talk") + ", quest " + Quests.State("helloquest_favour"));
        });
    }
}
