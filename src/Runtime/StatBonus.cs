// UnderRailWorks: stat bonuses as the game's own modifier objects (the "static effects" its feats, gear and status
// effects carry; efk.bl() rebuilds every stat from them). For mod feats and mod status effects.
//   Feats.Add(this, "brawny", "Brawny", "...", bonuses: new[] { StatBonus.Ability("Strength", 1), StatBonus.CarryCapacity(20) });
using System;
using UnderRailWorks.Internal;

namespace UnderRailWorks
{
    public sealed class StatBonus
    {
        internal readonly Func<d3e> Make;
        public string Text { get; private set; }
        StatBonus(string text, Func<d3e> make) { Text = text; Make = make; }

        // Abilities: Strength, Dexterity, Agility, Constitution, Perception, Will, Intelligence.
        public static StatBonus Ability(string ability, int amount) { return new StatBonus(ability + " " + amount, () => new ax6(ability, amount)); }
        // Skills by name (Guns, Stealth, Persuasion, Lockpicking...).
        public static StatBonus Skill(string skill, int amount) { return new StatBonus(skill + " " + amount, () => new dpw(skill, amount)); }
        public static StatBonus Evasion(int amount) { return new StatBonus("Evasion " + amount, () => new a6z(amount)); }
        public static StatBonus Dodge(int amount) { return new StatBonus("Dodge " + amount, () => new bel(amount)); }
        public static StatBonus Fortitude(int amount) { return new StatBonus("Fortitude " + amount, () => new ddl(amount)); }
        public static StatBonus Resolve(int amount) { return new StatBonus("Resolve " + amount, () => new ciw(amount)); }
        public static StatBonus Initiative(int amount) { return new StatBonus("Initiative " + amount, () => new b5x(amount)); }
        public static StatBonus Detection(int amount) { return new StatBonus("Detection " + amount, () => new ds1(amount)); }
        public static StatBonus Health(int amount) { return new StatBonus("Health " + amount, () => new dxd(amount)); }
        public static StatBonus CarryCapacity(int amount) { return new StatBonus("Carry capacity " + amount, () => new bgl(amount)); }
        public static StatBonus Psi(int amount) { return new StatBonus("Psi " + amount, () => new d72(amount)); }
        public static StatBonus PsiRegeneration(int amount) { return new StatBonus("Psi regeneration " + amount, () => new emn(amount)); }
    }
}
