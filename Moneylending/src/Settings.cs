using System;
using System.Collections.Generic;

namespace Moneylending
{
    // What the player can change (VVittgenstein 2026-11-10: the price of the network entry and each interest rate, through the
    // configuration and the configuration file). The values are entries of the game's mod configuration (ModHelper.GetConfig):
    // the game shows them in this mod's settings window in the Mods panel, where a change takes effect when Apply is pressed,
    // and keeps them in <persistentDataPath>\ModConfigs\Moneylending.json, which it reads once when it starts and which can be
    // edited while the game is closed. The file holds every value as text:
    //     { "key": "unlock_cost", "value": "350" }
    //     { "key": "interest_min_scavenger", "value": "30" }, { "key": "interest_max_scavenger", "value": "40" }, ...
    //
    // Agent's decisions (none of them confirmed one by one):
    //   - Which rates: the lowest and the highest interest each of the eight kinds of borrower proposes (Profile.MinRate and
    //     MaxRate are the defaults). The points added by "ask for more interest", the Loan Shark's rate and the bank's rate
    //     are not entries.
    //   - No range is given to the game, so its window shows text boxes (ModConfigWindow.BuildEntry), where a number can be
    //     typed exactly; with a range it would show a slider 224 pixels wide. The game then accepts any whole number, so this
    //     mod keeps the price within 0 to MaxCost and a rate within 0 to MaxRate, and writes a number outside back as the
    //     nearest one inside.
    //   - A lowest rate above the highest: the two are used the other way round, and the entries stay as typed.
    //   - The section titles are Latin letters only, as in GunsmithExpansion (the game shows them without the font swap).
    internal static class Settings
    {
        internal const string CostKey = "unlock_cost";
        internal const string MinRatePrefix = "interest_min_";
        internal const string MaxRatePrefix = "interest_max_";
        internal const int MaxCost = 1000000;
        internal const int MaxRate = 1000;
        private const string PriceSection = "Price";
        private const string InterestSection = "Interest";

        private static ConfigEntry<int> cost;
        private static readonly Dictionary<string, ConfigEntry<int>> minRates = new Dictionary<string, ConfigEntry<int>>();
        private static readonly Dictionary<string, ConfigEntry<int>> maxRates = new Dictionary<string, ConfigEntry<int>>();

        internal static void Define()
        {
            ModConfig config = ModHelper.GetConfig(MoneylendingMod.ModId);
            cost = config.Define(CostKey, Unlock.DefaultCost, PriceSection, Text.SettingCost, null, null, Text.SettingCostTip);
            Keep(cost, MaxCost);
            cost.OnChanged += OnCostChanged;
            foreach (Profile p in Profiles.All)
            {
                Profile profile = p;
                ConfigEntry<int> min = config.Define(MinRatePrefix + p.ConfigName, p.MinRate, InterestSection,
                    () => Text.SettingMinRate(Text.BorrowerName(profile)), null, null, () => Text.SettingRateTip(profile));
                ConfigEntry<int> max = config.Define(MaxRatePrefix + p.ConfigName, p.MaxRate, InterestSection,
                    () => Text.SettingMaxRate(Text.BorrowerName(profile)), null, null, () => Text.SettingRateTip(profile));
                Keep(min, MaxRate);
                Keep(max, MaxRate);
                min.OnChanged += value => Keep(min, MaxRate);
                max.OnChanged += value => Keep(max, MaxRate);
                minRates[p.Id] = min;
                maxRates[p.Id] = max;
            }
            // Written at every start, so the file exists and lists every entry for players who edit it by hand.
            config.Save();
            MoneylendingMod.Log("settings: " + Summary() + " (" + config.FilePath + ")");
        }

        // The price of the network entry before the game's own multipliers (NetworkUpgrade.GetCost).
        internal static int UnlockCost()
        {
            return cost != null ? Limit(cost.Value, MaxCost) : Unlock.DefaultCost;
        }

        // The range a new request draws its interest from (Lending.NewRequest). Loans already made keep the rate they were made
        // at, and so does a request already drawn: both are in the ledger.
        internal static void RateRange(Profile p, out int min, out int max)
        {
            ConfigEntry<int> low, high;
            int a = minRates.TryGetValue(p.Id, out low) ? Limit(low.Value, MaxRate) : p.MinRate;
            int b = maxRates.TryGetValue(p.Id, out high) ? Limit(high.Value, MaxRate) : p.MaxRate;
            min = Math.Min(a, b);
            max = Math.Max(a, b);
        }

        internal static int Limit(int value, int max)
        {
            return Math.Max(0, Math.Min(max, value));
        }

        // Writes a number outside the limits back as the nearest one inside, so the window and the file show the number in
        // use. ConfigEntry.Set raises OnChanged again, then with the number inside, and writes the file.
        private static void Keep(ConfigEntry<int> entry, int max)
        {
            int value = Limit(entry.Value, max);
            if (value != entry.Value)
                entry.Set(value);
        }

        // Applied at once: Unlock.Register writes the new price into the entry of the run that is loaded, if there is one.
        private static void OnCostChanged(int value)
        {
            if (Limit(value, MaxCost) != value)
            {
                Keep(cost, MaxCost);
                return;
            }
            try
            {
                Unlock.Register();
            }
            catch (Exception e)
            {
                MoneylendingMod.Warn("applying the price of the network entry failed: " + e);
            }
        }

        private static string Summary()
        {
            var parts = new List<string> { "price " + UnlockCost() };
            foreach (Profile p in Profiles.All)
            {
                int min, max;
                RateRange(p, out min, out max);
                parts.Add(p.Id + " " + min + "-" + max + "%");
            }
            return string.Join(", ", parts.ToArray());
        }
    }
}
