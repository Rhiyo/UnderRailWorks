// UnderRailWorks: a player's screen, when the game has more than one player (co-op). What a mod shows one player (its
// popups: Ui.Ask, AskNumber, Confirm, Choose; Notify.Log) goes to the screen of the player it's for: the acting
// player (Party.Acting: the one whose action is running), or one named with Party.As(player, () => Ui.Confirm(...)).
// The answer comes back to this game and the mod's callback runs with that player as World.Player. With no multiplayer
// mod, or for the local player, everything is as before.
// A multiplayer mod provides the screens (PlayerScreens.Provider): Works asks it to show a popup or a message on
// another player's machine; it calls the answer back.
using System;
using Microsoft.Xna.Framework;
using UnderRailWorks.Internal;

namespace UnderRailWorks
{
    [Preview("multiplayer: built for Co-op so far")] public enum PopupKind { Ask, AskNumber, Choose }

    [Preview("multiplayer: built for Co-op so far")] public sealed class RemotePopup
    {
        public PopupKind Kind { get; internal set; }
        public string Title { get; internal set; }       // Choose
        public string Text { get; internal set; }        // the question
        public string Initial { get; internal set; }     // Ask: the text; AskNumber: the number
        public int Min { get; internal set; }
        public int Max { get; internal set; }
        public string[] Buttons { get; internal set; }   // Choose; Ask / AskNumber: [0] the OK button's text
        public string AlsoAllow { get; internal set; }   // Ask
    }

    [Preview("multiplayer: built for Co-op so far")] public interface IPlayerScreens
    {
        // A popup on who's screen: true if it's shown there (the answer comes later: Ask text, AskNumber the number as
        // text, Choose the button's index as text; cancelled: never). False: not a remote player, show it here.
        bool Popup(Character who, RemotePopup popup, Action<string> answer);
        // A line in who's message log. False: show it here.
        bool Message(Character who, string text, Color colour);
        // who is played on another machine (their windows can't be shown here).
        bool IsRemote(Character who);
    }

    [Preview("multiplayer: built for Co-op so far")] public static class PlayerScreens
    {
        public static IPlayerScreens Provider { get; set; }

        // Shown elsewhere (true), or here (false).
        internal static bool Route(RemotePopup popup, Action<string> answer)
        {
            var who = Party.Acting; var p = Provider;
            if (who == null || p == null) return false;
            try { return p.Popup(who, popup, text => Party.As(who, () => answer(text))); }
            catch (Exception ex) { Log.Write("player screens: " + ex.Message); return false; }
        }
        // The acting player is on another machine: a mod's live window for them isn't opened here.
        internal static bool ActingIsRemote { get { var who = Party.Acting; var p = Provider; try { return who != null && p != null && p.IsRemote(who); } catch { return false; } } }

        internal static bool RouteMessage(string text, Color colour)
        {
            var who = Party.Acting; var p = Provider;
            if (who == null || p == null) return false;
            try { return p.Message(who, text, colour); }
            catch (Exception ex) { Log.Write("player screens: " + ex.Message); return false; }
        }
    }
}
