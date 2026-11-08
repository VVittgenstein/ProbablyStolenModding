namespace Moneylending
{
    // Moneylending is bought once in Joe Wild's network, in the Basic tab next to his referral services. The cost
    // is in Credits (Wild's favor is spent first) and is raised by the difficulty like every network entry.
    internal static class Unlock
    {
        internal const string Id = "moneylending_contacts";
        internal const int Cost = 350;

        // ModHelper.AddNetworkUpgrade reads PlayerStore.Instance, which creates a store when none exists, and the
        // store's constructor writes to NewGameData.Instance, which does not exist yet while the game loads its
        // mods. The entry is therefore registered when a run loads or starts, once the run's store exists.
        // Registering again replaces the definition and keeps the run's saved state.
        internal static void Register()
        {
            if (!PlayerStore.HasInstance)
                return;
            ModHelper.AddNetworkUpgrade(MoneylendingMod.ModId, Id, Cost, "Referral",
                Text.UnlockTitle, Text.UnlockDescription, OnUnlocked,
                null, "CRIMINEL_NETWORK", null, 0, null, Text.UnlockBought, false);
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
