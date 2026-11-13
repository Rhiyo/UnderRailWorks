// Bounty Hunter: an UnderRailWorks mod. An ability marks a hostile (a mod status with stat penalties); killing a
// marked hostile pays charons, more with the Headhunter feat (requirements and stat bonuses). A ledger window lists the
// bounties of this game (per-save data). Built from Works' API only: Abilities, ModStatuses, StatBonus, Feats,
// CharacterDying, Inventory money, Ui.Window / Ui.List, translations, a decimal setting.
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework.Input;
using UnderRailWorks;

[assembly: System.Reflection.AssemblyVersion("1.0.1")]

public sealed class BountyHunterConfig : ModConfig
{
    [Header("Bounty Hunter")]
    [Label("Charons per level of the target"), Range(1, 50), Increment(1)] public int PerLevel = 12;
    [Label("Bounty multiplier"), Range(0.5f, 3f), Increment(0.1f)] public double Multiplier = 1.0;
    [Label("Mark cost (AP)"), Range(0, 40), Increment(5)] public int ApCost = 10;
}

public sealed class BountyHunter : WorksMod
{
    BountyHunterConfig config;
    ModStatus marked;
    ModFeat headhunter;
    ModAbility mark;
    Keybind ledgerKey;

    public override void Load()
    {
        config = GetConfig<BountyHunterConfig>();
        marked = StatusEffects.Add(this, "marked", T("status.name"), T("status.desc"), "Status.PsiBoost", StatBonus.Dodge(-10), StatBonus.Evasion(-10));
        headhunter = Feats.Add(this, "headhunter", T("feat.name"), T("feat.desc"), new FeatRequirements { Abilities = { ["Perception"] = 6 }, Level = 4 },
            null, new[] { StatBonus.Detection(5) });
        mark = Abilities.Add(this, "mark", T("ability.name"), T("ability.desc"), "art/mark.png", apCost: config.ApCost, cooldown: 3,
            target: AbilityTarget.Character, key: Keys.F4);
        mark.Range = 15;
        mark.Used += (user, target) =>
        {
            if (!target.IsHostile) { Notify.OverPlayer(T("notHostile")); return; }
            marked.Apply(target, 6);
            Notify.Over(target, T("status.name"), new Microsoft.Xna.Framework.Color(230, 160, 60));
        };
        GameEvents.CharacterDying.Add(this, (who, killer) => Died(who));     // statuses are still on while it dies
        ledgerKey = Keybinds.Add(this, "ledger", T("ledger"), Keys.F10);
        GameEvents.Update.Add(this, s => { if (ledgerKey.Pressed) Ledger(); });
        config.Changed += () => mark.ApCost = config.ApCost;
    }

    void Died(Character who)
    {
        if (who == null || !marked.Has(who) || World.Player == null) return;
        int pay = (int)Math.Round(Math.Max(1, who.Level) * config.PerLevel * config.Multiplier * (World.Player.HasFeat(headhunter.Key) ? 1.5 : 1.0));
        World.Player.Inventory.AddMoney(pay);
        Notify.Log(T("paid", pay, who.Name));
        var list = SaveData.Get<List<object>>("bounties", null) ?? new List<object>();
        list.Add(who.Name + "|" + pay);
        SaveData.Set("bounties", list);
        Log.Info("bounty " + pay + " for " + who.Name);
    }

    void Ledger()
    {
        var entries = (SaveData.Get<List<object>>("bounties", null) ?? new List<object>()).Select(o => Convert.ToString(o).Split('|')).Where(a => a.Length == 2).ToList();
        int total = entries.Sum(a => { int n; return int.TryParse(a[1], out n) ? n : 0; });
        var win = Ui.Window(this, "ledger", T("ledger"), Ui.S(340), Ui.S(300));
        Ui.Add(win, Ui.Label(T("total", total, entries.Count), win.Width - 20), 10, win.Top + 4);
        int listTop = win.Top + Ui.LineHeight + 12;
        var list = Ui.List(win.Width - 20, win.Height - listTop - 10);
        list.Items = entries.Select(a => a[0] + "  —  " + a[1] + " ¢").Reverse().ToList();
        Ui.Add(win, list.View, 10, listTop);
    }
}
