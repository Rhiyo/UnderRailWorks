// Test mod: throws while loading, to check that one broken mod doesn't stop the game or the others.
using System;
using HarmonyLib;

public static class BrokenMod
{
    public static void OnLoad(Harmony harmony)
    {
        throw new InvalidOperationException("BrokenMod fails on purpose");
    }
}
