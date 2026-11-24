using HarmonyLib;
using UnityEngine;

namespace GunsmithExpansion
{
    [HarmonyPatch(typeof(PlayerStore), "LoadGame")]
    internal static class CounterLoadPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Prefix() { CounterCapacity.BeginLoad(); }
        private static void Finalizer() { CounterCapacity.EndIncompleteLoad(); }
    }

    [HarmonyPatch(typeof(EmporiumEntry), "Start")]
    internal static class CounterStartPatch
    {
        private static void Postfix() { CounterCapacity.Synchronize(false); }
    }

    [HarmonyPatch(typeof(EmporiumEntry), "Update")]
    internal static class CounterUpdatePatch
    {
        private static void Postfix() { CounterCapacity.Tick(); }
    }

    [HarmonyPatch(typeof(EmporiumEntry), "GetFrontInvPosition", new[] { typeof(int), typeof(int) })]
    internal static class CounterFrontPositionPatch
    {
        private static void Postfix(EmporiumEntry __instance, int newCanvasWidth, int newCanvasHeight, ref Vector2 __result)
        {
            if (CounterCapacity.ManagePosition)
                __result = CounterCapacity.FrontPosition(__instance, newCanvasWidth, newCanvasHeight);
        }
    }

    [HarmonyPatch(typeof(EmporiumEntry), "ShowFrontWindow")]
    internal static class CounterShowFrontPatch
    {
        private static void Postfix(EmporiumEntry __instance)
        {
            int w, h;
            PixelWindow front = __instance.frontInvWindow;
            if (!CounterCapacity.ManagePosition || front == null || front.rectTransform == null || !CounterCapacity.CanvasSize(out w, out h)) return;
            front.rectTransform.anchoredPosition = CounterCapacity.FrontPosition(__instance, w, h);
        }
    }

    [HarmonyPatch(typeof(EmporiumEntry), "GetBackInvPosition", new[] { typeof(int), typeof(int) })]
    internal static class CounterBackPositionPatch
    {
        private static void Postfix(EmporiumEntry __instance, int newCanvasWidth, int newCanvasHeight, ref Vector2 __result)
        {
            if (CounterCapacity.ManagePosition)
                __result = CounterCapacity.BackPosition(__instance, newCanvasWidth, newCanvasHeight);
        }
    }

    [HarmonyPatch(typeof(EmporiumEntry), "ExpandFrontCounterGrid")]
    internal static class CounterVisitFrontPatch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(EmporiumEntry __instance) { CounterCapacity.RememberFront(__instance); }
    }

    [HarmonyPatch(typeof(EmporiumEntry), "ResetFrontCounterGrid")]
    internal static class CounterReturnFrontPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(EmporiumEntry __instance) { CounterCapacity.RestoreVisitFront(__instance); }
    }

    [HarmonyPatch(typeof(MapUIManager), "TransferBackInvToCounter")]
    internal static class CounterOutboundPatch
    {
        private static void Prefix() { CounterCapacity.PrepareTransfer(); }
        private static void Postfix() { CounterCapacity.SynchronizeAndRefresh(); }
    }

    [HarmonyPatch(typeof(MapUIManager), "TransferCounterToBackInv")]
    internal static class CounterInboundPatch
    {
        private static void Prefix() { CounterCapacity.PrepareTransfer(); }
        private static void Postfix() { CounterCapacity.SynchronizeAndRefresh(); }
    }

    [HarmonyPatch(typeof(ModHook), "FireOnAfterhourArriveLate")]
    internal static class CounterArriveNightPatch
    {
        private static void Postfix() { CounterCapacity.SynchronizeAndRefresh(); }
    }

    [HarmonyPatch(typeof(EmporiumEntry), "OnArriveStore")]
    internal static class CounterArriveStorePatch
    {
        private static void Postfix() { CounterCapacity.SynchronizeAndRefresh(); }
    }

    [HarmonyPatch(typeof(ItemMouseDragHandler), "StartDrag")]
    internal static class CounterDragStartPatch
    {
        private static void Postfix() { CounterCapacity.Synchronize(false); }
    }

    [HarmonyPatch(typeof(ItemMouseDragHandler), "EndDrag")]
    internal static class CounterDragEndPatch
    {
        private static void Postfix() { CounterCapacity.Synchronize(false); }
    }

    [HarmonyPatch(typeof(ItemMouseDragHandler), "AbortDrag")]
    internal static class CounterDragAbortPatch
    {
        private static void Postfix() { CounterCapacity.Synchronize(false); }
    }

    [HarmonyPatch(typeof(ItemSelectHandler), "SelectItem")]
    internal static class CounterSelectionPatch
    {
        private static void Postfix() { CounterCapacity.Synchronize(false); }
    }

    [HarmonyPatch(typeof(ItemSelectHandler), "OnEventReset")]
    internal static class CounterSelectionResetPatch
    {
        private static void Postfix() { CounterCapacity.Synchronize(false); }
    }

    [HarmonyPatch(typeof(ItemSelectHandler), "EndSelect")]
    internal static class CounterSelectionEndPatch
    {
        private static void Postfix() { CounterCapacity.Synchronize(false); }
    }

    [HarmonyPatch(typeof(ItemMultiSelectHandler), "StartGroupDrag")]
    internal static class CounterGroupStartPatch
    {
        private static void Postfix() { CounterCapacity.Synchronize(false); }
    }

    [HarmonyPatch(typeof(ItemMultiSelectHandler), "EndGroupDrag")]
    internal static class CounterGroupEndPatch
    {
        private static void Postfix() { CounterCapacity.Synchronize(false); }
    }

    [HarmonyPatch(typeof(ItemMultiSelectHandler), "OnEventReset")]
    internal static class CounterGroupResetPatch
    {
        private static void Postfix() { CounterCapacity.Synchronize(false); }
    }

    [HarmonyPatch(typeof(GameGridInventory), "MayHaveValidInventorySlot")]
    internal static class CounterMayFitPatch
    {
        private static void Prefix(GameGridInventory __instance, GameItem item)
        {
            GridShape candidate = null;
            CounterCapacity.Preflight(__instance, item, ref candidate, false);
        }
    }

    [HarmonyPatch(typeof(GameGridInventory), "UncheckedAccept")]
    internal static class CounterDirectAcceptPatch
    {
        private static void Prefix(GameGridInventory __instance, GameItem item)
        {
            GridShape candidate = null;
            CounterCapacity.Preflight(__instance, item, ref candidate, false);
        }
    }

    [HarmonyPatch(typeof(GameGridInventory), "TryFindOneValidInventorySlot", new[] { typeof(GameItem), typeof(bool) })]
    internal static class CounterFindSlotPatch
    {
        private static void Prefix(GameGridInventory __instance, GameItem item)
        {
            GridShape candidate = null;
            CounterCapacity.Preflight(__instance, item, ref candidate, false);
        }
        private static void Postfix(GameGridInventory __instance, GameItem item, bool keepOrientation, ref SlotMarker __result)
        {
            CounterCapacity.PreferBottom(__instance, item, false, keepOrientation, ref __result);
        }
    }

    [HarmonyPatch(typeof(GameGridInventory), "TryFindOneValidInventorySlot_TopMiddle", new[] { typeof(GameItem), typeof(bool) })]
    internal static class CounterFindMiddlePatch
    {
        private static void Prefix(GameGridInventory __instance, GameItem item)
        {
            GridShape candidate = null;
            CounterCapacity.Preflight(__instance, item, ref candidate, false);
        }
        private static void Postfix(GameGridInventory __instance, GameItem item, bool keepOrientation, ref SlotMarker __result)
        {
            CounterCapacity.PreferBottom(__instance, item, true, keepOrientation, ref __result);
        }
    }

    [HarmonyPatch(typeof(GameGridInventory), "TryInventorySlot", new[] { typeof(GameItem), typeof(int), typeof(Vector2), typeof(GridShape), typeof(TagSystem) })]
    internal static class CounterTrySlotPatch
    {
        private static void Prefix(GameGridInventory __instance, GameItem item, Vector2 itemGraphicsCenterPosition, ref GridShape tempShape)
        {
            // Non-finite x selects the explicit grid-coordinate overload. Finite x is converted from screen space
            // by GetGridPosition after the window has moved.
            CounterCapacity.Preflight(__instance, item, ref tempShape, !float.IsFinite(itemGraphicsCenterPosition.x));
        }
    }

    [HarmonyPatch(typeof(GameGridInventory), "CheckShapePlacement")]
    internal static class CounterShapePatch
    {
        private static void Prefix(GameGridInventory __instance, GameItem item, ref GridShape shape)
        {
            CounterCapacity.Preflight(__instance, item, ref shape, true);
        }
    }

    [HarmonyPatch(typeof(GameGridInventory), "CheckInventorySlotStillValid")]
    internal static class CounterStillValidPatch
    {
        private static void Prefix(GameGridInventory __instance, GameItem item, ref GridShape tempShape)
        {
            CounterCapacity.Preflight(__instance, item, ref tempShape, true);
        }
    }

    [HarmonyPatch(typeof(GameItem), "ValidateShapeState")]
    internal static class CounterItemChangedPatch
    {
        private static void Postfix(GameItem __instance) { CounterCapacity.Schedule(__instance.parentInventory); }
    }

    [HarmonyPatch(typeof(PlayerStore), "SellItem", new[] { typeof(GameItem) })]
    internal static class CounterSalePatch
    {
        private static void Postfix() { CounterCapacity.Synchronize(false); }
    }

    [HarmonyPatch(typeof(PlayerStore), "SellAllItemFromTable")]
    internal static class CounterSaleAllPatch
    {
        private static void Postfix() { CounterCapacity.Synchronize(false); }
    }

    [HarmonyPatch(typeof(PlayerStore), "BuyItem", new[] { typeof(GameItem) })]
    internal static class CounterPurchasePatch
    {
        private static void Postfix() { CounterCapacity.Synchronize(false); }
    }

    [HarmonyPatch(typeof(PlayerStore), "BuyAllItemFromTable")]
    internal static class CounterPurchaseAllPatch
    {
        private static void Postfix() { CounterCapacity.Synchronize(false); }
    }

    [HarmonyPatch(typeof(PlayerStore), "DismissCurrentClient")]
    internal static class CounterDismissPatch
    {
        private static void Postfix() { CounterCapacity.Synchronize(false); }
    }
}
