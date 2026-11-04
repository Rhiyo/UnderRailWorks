// Fair Trade: an UnderRailWorks mod. Merchants' prices as settings: what they charge you (their markup, % of the
// game's) and what they pay you for your goods (% of the game's). Applied when a barter window opens; nothing is
// saved. Built from Works' API only (Prices hooks).
using System;

[assembly: System.Reflection.AssemblyVersion("1.0.0")]

public sealed class FairTradeConfig : ModConfig
{
    [Header("Fair Trade")]
    [Label("What merchants charge (%)"), Range(25, 200), Increment(5), Tooltip("Of the game's price (which already falls with Mercantile).")] public int Buy = 100;
    [Label("What merchants pay you (%)"), Range(25, 300), Increment(5), Tooltip("Of the game's price for your goods.")] public int Sell = 100;
}

public sealed class FairTrade : WorksMod
{
    public override void Load()
    {
        var config = GetConfig<FairTradeConfig>();
        Prices.BuyMarkup.Add(this, (merchant, markup) => markup * config.Buy / 100f);
        Prices.SellFactor.Add(this, factor => factor * config.Sell / 100f);
    }
}
