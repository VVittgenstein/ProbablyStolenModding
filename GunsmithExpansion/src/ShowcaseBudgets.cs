using System;
using HarmonyLib;

namespace GunsmithExpansion
{
    // Item 14, the budgets of those the display case draws (ShowcaseHelper.CreateClientByStockType; VVittgenstein 2026-11-06 08:45, current/008 quote 40). The game's budgets were
    // set for the game's guns; this mod's guns are worth more than they can pay. The scavenger stays as in the game. Otherwise:
    //   - self defense enthusiast (the game: 145 to 195 + day / 4): with a level 1 gun of this mod in the display case 290 to 390, from day 14 on 360 to 490;
    //   - private security contractor (the game: 500 to 700 + day): with a level 2 gun in the display case 900 to 1260, with a level 3 gun 1690 to 2290.
    // A gun in a cabinet of the display case counts as in the display case (Cabinets.ShowcaseGuns). In all other cases the game's budget stays. Level 2 also takes the game's guns at this
    // mod's level 2 (GameGunLevels: SMG, shotgun; VVittgenstein 2026-11-06 09:19, current/008 quote 41); with both levels shown the higher one counts.
    // The game names the budget in a line right after setting it, so it is replaced on its way into StoreClient.SetBudget, which also applies the game's budget modifiers.
    internal static class ShowcaseBudgets
    {
        private const int LaterFromDay = 14;
        private static readonly Random rng = new Random();

        // The budget for the one SetBudget call of the creator now running; 0 leaves the game's.
        internal static int Pending;

        private static bool Shows(string tier, bool modGunsOnly)
        {
            foreach (GameItem gun in Cabinets.ShowcaseGuns())
            {
                GunDef d = RepairOrders.Find(gun.identifier);
                if (d != null)
                {
                    if (d.Tier == tier)
                        return true;
                    continue;
                }
                GameGunLevels.GameGun g = modGunsOnly ? null : GameGunLevels.Find(gun.identifier);
                if (g != null && RepairOrders.TierName(g.Level) == tier)
                    return true;
            }
            return false;
        }

        internal static int SelfDefense()
        {
            if (!Shows("一档", true))
                return 0;
            return StoreStation.GetDayCounter() < LaterFromDay ? rng.Next(290, 391) : rng.Next(360, 491);
        }

        internal static int Contractor()
        {
            if (Shows("三档", false))
                return rng.Next(1690, 2291);
            return Shows("二档", false) ? rng.Next(900, 1261) : 0;
        }
    }

    [HarmonyPatch(typeof(StoreClientListTierWeapon), nameof(StoreClientListTierWeapon.CreateSelfDefenseEnthusiast))]
    internal static class SelfDefenseBudgetPatch
    {
        private static void Prefix()
        {
            ShowcaseBudgets.Pending = ShowcaseBudgets.SelfDefense();
        }

        private static Exception Finalizer(Exception __exception)
        {
            ShowcaseBudgets.Pending = 0;
            return __exception;
        }
    }

    [HarmonyPatch(typeof(StoreClientListTierWeapon), nameof(StoreClientListTierWeapon.CreatePrivateSecurityContractor))]
    internal static class ContractorBudgetPatch
    {
        private static void Prefix()
        {
            ShowcaseBudgets.Pending = ShowcaseBudgets.Contractor();
        }

        private static Exception Finalizer(Exception __exception)
        {
            ShowcaseBudgets.Pending = 0;
            return __exception;
        }
    }

    [HarmonyPatch(typeof(StoreClient), nameof(StoreClient.SetBudget), new[] { typeof(int) })]
    internal static class PendingBudgetPatch
    {
        private static void Prefix(ref int budget)
        {
            if (ShowcaseBudgets.Pending <= 0)
                return;
            budget = ShowcaseBudgets.Pending;
            ShowcaseBudgets.Pending = 0;
        }
    }
}
