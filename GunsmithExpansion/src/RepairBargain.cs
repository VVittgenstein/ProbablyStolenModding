using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace GunsmithExpansion
{
    // The game's bargain panel for repair customers (VVittgenstein 2026-11-06 02:18 and the message after it, current/008 quotes 21 and 22, item 70).
    // The panel offers discount and extra charge to a customer who
    // buys, and a gun order's customer does not (StoreClient.IsClientBuying: clientIntent BUY or SELLNBUY; his is GUNSMITH), so these
    // methods of the panel see him as BUY while they run. That intent is put back straight after, because the window, the counter and
    // the rest of the game go by it. BargainUIManager.OfferDiscount and OfferMarkup then run as written: their percentages and chances,
    // the customer's budget and trade multiplier, his lines, and his leaving when he refuses (his gun goes and the store's parts come back:
    // StoreClientListGun.OnOrderGreetingEnd). What they change on the store's items does not apply: the amounts of the order are
    // changed instead (RepairOrders.Bargained).
    internal static class RepairBargain
    {
        // The repair customer while the panel's code below runs.
        internal static StoreClient Panel;
    }

    [HarmonyPatch]
    internal static class RepairBargainPanelPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(BargainUIManager), "UpdateGroupVisibility");
            yield return AccessTools.Method(typeof(BargainUIManager), "UpdateRepGainText");
            yield return AccessTools.Method(typeof(BargainUIManager), nameof(BargainUIManager.CanOfferDiscount));
            yield return AccessTools.Method(typeof(BargainUIManager), nameof(BargainUIManager.CanOfferMarkup));
        }

        // UpdateGroupVisibility calls UpdateRepGainText: the inner call finds no repair customer and leaves it to the outer one.
        private static void Prefix(out StoreClient __state)
        {
            __state = RepairOrders.CurrentCustomer();
            if (__state != null)
            {
                __state.clientIntent = StoreClient.ClientIntent.BUY;
                RepairBargain.Panel = __state;
            }
        }

        private static Exception Finalizer(StoreClient __state, Exception __exception)
        {
            if (__state != null)
            {
                __state.clientIntent = StoreClient.ClientIntent.GUNSMITH;
                RepairBargain.Panel = null;
            }
            return __exception;
        }
    }

    // Discount (0) and extra charge (1) only: the game also lists blackmail and threats, which are not offered here.
    [HarmonyPatch(typeof(BargainUIManager), "BuildGroups")]
    internal static class RepairBargainGroupsPatch
    {
        private static void Prefix(List<BargainType> shown)
        {
            if (RepairBargain.Panel != null && shown != null)
                shown.RemoveAll(type => (int)type > 1);
        }
    }

    // A repair customer who stays to buy (StoreClientListGun.ConvertToBuyer: after his last gun from the game's follow-up, or when dismissed,
    // StoreClientListGun.TryStayForMeleeWeapon) is the same customer to the bargain panel, which resets its offers only for another customer
    // (BargainUIManager.Refresh). So what was offered on his repair stayed: no discount or extra charge could be offered on what he buys, the
    // repair's trade multiplier kept counting, and the panel kept the two groups of the repair. The panel now takes him as a new customer. A
    // customer with another gun does not get here between his guns (RepairNextGunPatch takes the follow-up), so the offer keeps for them (item 81).
    [HarmonyPatch(typeof(StoreClientListGun), "ConvertToBuyer")]
    internal static class RepairConvertPatch
    {
        private static void Prefix(StoreClient storeClient, out bool __state)
        {
            __state = storeClient != null && RepairOrders.CurrentCustomer() == storeClient;
        }

        private static void Postfix(StoreClient storeClient, bool __state)
        {
            if (!__state)
                return;
            storeClient.ClearTradeRepMultiplierOverrides();
            BargainUIManager bargain = BargainUIManager.Instance;
            if (bargain == null)
                return;
            Traverse panel = Traverse.Create(bargain);
            panel.Field("trackedClient").SetValue(null);
            panel.Field("builtLayoutKey").SetValue(null);
        }
    }

    // The game puts its discount and extra charge on the store's items on the counter; a repair customer buys none of them.
    [HarmonyPatch(typeof(BargainUIManager), "ApplyOwnedItemFeature")]
    internal static class RepairBargainFeaturePatch
    {
        private static bool Prefix()
        {
            return RepairOrders.CurrentCustomer() == null;
        }
    }

    // The window shows the amounts again after an offer.
    [HarmonyPatch(typeof(BargainUIManager), "OnBargainButton")]
    internal static class RepairBargainRefreshPatch
    {
        private static void Postfix()
        {
            if (RepairOrders.CurrentCustomer() != null && NegociationUIManager.Instance != null && NegociationUIManager.Instance.IsOpen)
                NegociationUIManager.Instance.RefreshUI();
        }
    }

    // The store's parts on a repair customer's gun are paid after the store's offer as well.
    [HarmonyPatch(typeof(GunOrderData), nameof(GunOrderData.GetPartsValue))]
    internal static class RepairPartsValuePatch
    {
        private static void Postfix(GameItem gun, ref int __result)
        {
            if (RepairOrders.OrderOf(gun) != null)
                __result = RepairOrders.Bargained(__result);
        }
    }

    // A customer who only wants the gun repaired: its slots take nothing (the game's slots ask GunModHelper.MayModGun; current/008 quote 18
    // point A).
    [HarmonyPatch(typeof(GunModHelper), nameof(GunModHelper.MayModGun))]
    internal static class RepairOnlyModPatch
    {
        private static void Postfix(GameItem gun, ref bool __result)
        {
            if (__result && RepairOrders.IsRepairOnly(gun))
                __result = false;
        }
    }
}
