namespace Moneylending
{
    // Moneylending is bought once in Joe Wild's network, in the Basic tab next to his referral services. The cost
    // is in Credits (Wild's favor is spent first) and is raised by the difficulty like every network entry. The player
    // can change it in the mod's settings (Settings.UnlockCost); DefaultCost is the price until then.
    internal static class Unlock
    {
        internal const string Id = "moneylending_contacts";
        internal const int DefaultCost = 350;

        // ModHelper.AddNetworkUpgrade reads PlayerStore.Instance, which creates a store when none exists, and the
        // store's constructor writes to NewGameData.Instance, which does not exist yet while the game loads its
        // mods. The entry is therefore registered when a run loads or starts, once the run's store exists.
        // Registering again replaces the definition and keeps the run's saved state.
        //
        // That saved state includes the price: a run has its own copy of the entry (PlayerStore.networkUpgrade), the
        // copy is written into the save with its cost, and registering again does not replace a copy the run already
        // has (NetworkUpgrade.RegisterModUpgrade). The network shows and charges the copy's cost
        // (NetworkUpgrade.GetCost), so the price from the settings is also written into the copy while the entry is not
        // bought. The game corrects the cost of one of its own entries in the same way after loading
        // (NetworkUpgrade.AddMissingAfterLoad, PLANT_DNA). Also called when the price is changed in the settings
        // window (Settings.OnCostChanged).
        internal static void Register()
        {
            if (!PlayerStore.HasInstance)
                return;
            int cost = Settings.UnlockCost();
            ModHelper.AddNetworkUpgrade(MoneylendingMod.ModId, Id, cost, "Referral",
                Text.UnlockTitle, Text.UnlockDescription, OnUnlocked,
                null, "CRIMINEL_NETWORK", null, 0, null, Text.UnlockBought, false);
            NetworkUpgrade entry = ModHelper.GetNetworkUpgrade(Id);
            if (entry != null && !entry.IsUnlocked() && entry.cost != cost)
            {
                MoneylendingMod.Log("price of the network entry in this run: " + entry.cost + " -> " + cost);
                entry.cost = cost;
            }
        }

        internal static bool IsUnlocked()
        {
            return Game.InRun && ModHelper.IsNetworkUpgradeUnlocked(Id);
        }

        private static void OnUnlocked()
        {
            MoneylendingMod.Log("moneylending unlocked on day " + Game.Day);
            Sign.Give();
        }
    }
}
