using System;
using System.Collections.Generic;
using HarmonyLib;

namespace GunsmithExpansion
{
    // The game's portable X-ray scanner (xray_scanner, ToolDirectory.CreateXRayScanner: a TOOL worth 400 that can be used
    // on items the store does not own) finds the hidden internal faults of this mod's guns (Repair). The game registers it
    // but never sells or gives it (only its item spawner lists it), and its picture, spectral_analyser in
    // Items/items_tool, is empty: every pixel has alpha 3 (current/006, 2026-11-03). Two changes (final-strategy 89 and
    // 100; VVittgenstein 2026-11-03 21:49, 22:13, 22:44):
    //   - Dr. Jackson (the game's Inventor, StoreClientList.PlaceInventorInventory) offers it the way he offers the
    //     game's Aug scanner: on 60% of his visits (VVittgenstein 2026-11-06, current/008 quote 35, item 83; 50% until
    //     then), when the store does not own one (StoreClientList.AddIfNotOwn). The price is the game's value, 400.
    //   - Its picture is this mod's Textures/Items/gse_xray.png (current/006 section 5, unit U125) once that file is in
    //     the mod.
    internal static class XRay
    {
        internal const string Id = "xray_scanner";
        internal const string Picture = "gse_xray";
        internal const int JacksonPercent = 60;
        private static readonly Random rng = new Random();

        internal static void OfferAtJackson()
        {
            if (PlayerStore.Instance == null || EmporiumEntry.Instance == null || rng.Next(100) >= JacksonPercent)
                return;
            foreach (GameItem item in EmporiumEntry.Instance.GetAllOwnedItems(false, true))
            {
                if (item != null && item.IsID(Id))
                    return;
            }
            Sellers.Offer(DirectoryMaster.Item(Id, false));
            GunsmithMod.Log("Dr. Jackson offers the X-ray scanner");
        }

        internal static void SetPicture(GameItem item)
        {
            if (item != null && ModHelper.ModFileExists(GunsmithMod.ModId, "Textures/Items/" + Picture + ".png"))
                item.SetSpriteAndShapeFromMod(GunsmithMod.ModId, Picture);
        }
    }

    [HarmonyPatch(typeof(StoreClientList), nameof(StoreClientList.PlaceInventorInventory))]
    internal static class JacksonXRayPatch
    {
        private static void Postfix()
        {
            try
            {
                XRay.OfferAtJackson();
            }
            catch (Exception e)
            {
                GunsmithMod.Warn("X-ray scanner not offered: " + e.Message);
            }
        }
    }

    [HarmonyPatch(typeof(ToolDirectory), "CreateXRayScanner")]
    internal static class XRayPicturePatch
    {
        private static void Postfix(GameItem __result)
        {
            XRay.SetPicture(__result);
        }
    }
}
