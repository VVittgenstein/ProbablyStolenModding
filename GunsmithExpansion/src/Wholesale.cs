using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace GunsmithExpansion
{
    // The game's wholesale for the sellers of guns who do not have it (VVittgenstein 2026-11-06 04:01 and 05:09, current/008 quotes 24 and 27, item 15).
    // A button in the window buys all the seller still has, for the total of the prices less the percent (NegociationUIManager.OnWholesaleClick, PlayerStore.GetWholesalePrice). With one thing left it is
    // that thing for less. Agent's numbers, from the game's sellers of the same sort, which VVittgenstein accepted: thieves as petty thieves; those who come to sell guns as the security surplus. The sellers
    // who have it in the game are left as they are; the guns this mod puts on their tables count in it.
    internal static class Wholesale
    {
        internal const int ThiefPercent = 10;
        internal const int GunSellerPercent = 15;
    }

    [HarmonyPatch(typeof(StoreClientList), nameof(StoreClientList.CreateThief))]
    internal static class ThiefWholesalePatch
    {
        private static void Postfix(StoreClient __result)
        {
            if (__result != null)
                __result.SetWholesale(-Wholesale.ThiefPercent);
        }
    }

    [HarmonyPatch]
    internal static class GunSellerWholesalePatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(StoreClientListGun), nameof(StoreClientListGun.CreateLLGunSeller));
            yield return AccessTools.Method(typeof(StoreClientListGun), nameof(StoreClientListGun.CreateMercGunSeller));
            yield return AccessTools.Method(typeof(StoreClientListGun), nameof(StoreClientListGun.CreateULGunSeller));
        }

        private static void Postfix(StoreClient __result)
        {
            if (__result != null)
                __result.SetWholesale(-Wholesale.GunSellerPercent);
        }
    }
}
