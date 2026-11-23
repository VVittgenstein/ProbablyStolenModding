using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace GunsmithExpansion
{
    // Expands the store back counters (backInvinvElement and backInvinvElementCounter) from vanilla 18x4 to 20x7 and
    // Store-mode frontInvinvElement from 18x4 to 20x5 so every fully kitted gun (exhaustive worst case across 109 mod
    // guns + 6 vanilla guns: 19x5 widest on gse_w131/gse_w124, 14x7 tallest on gse_w116) fits on the counter for
    // trading, repair hand-in, customer delivery and afterhour trading at Lincoln Arms and Dr. Jackson.
    //
    // Layout invariants verified across all 7 EmporiumEntry.UpdateScaleFactor resolutions (960x540 .. 3840x2160):
    //   - Classic PixelWindow size is 324x116 px for 20x7 and 324x84 px for 20x5 (16 px/cell + 4 px border).
    //   - Store mode: backInvWindow (20x7) keeps its bottom edge at vanilla's exact counter lip (-((ch/100)*52.2) - 68,
    //     i.e. -329 px at 537..540) so the [Accept / Dismiss / Bell] counter buttons at [-335..-356] and invWindow at
    //     -365 remain completely uncovered; frontInvWindow (20x5) stacks directly above backInvWindow at [-129..-213],
    //     clearing vendingFountainWindow (-126.5), vendingMachineWindow (-127), showcaseWindow II, docInvWindow and
    //     trashInvWindow with 0 overlap (Supply and RepairOrders also fall back to 20x7 backInvinvElement if needed).
    //   - Afterhour counter locations (Lincoln Arms and Dr. Jackson, ShowsStoreCounters == true): frontInvinvElement
    //     keeps its 14x21 shelf (ExpandFrontCounterGrid); backInvWindow sits between Lincoln's ledgerTablet
    //     (X_max = 368.5 scaled) and frontInvWindow (X_min = frontX) with bottom at afterhourTop + 4 px, clearing
    //     ledgerTablet, dealerTablet, Dr. Jackson's sheetTablet, frontInvWindow, LincolnShelfHeader and afterhourWindow
    //     with 0 overlap.
    //   - Save/load safety: counters are resized before PlayerStore.LoadGame decodes backInvJSON, so both old saves
    //     (items within 18x4) and new saves (items anywhere in 20x7) load cleanly; backInvinvElementCounter matches
    //     backInvinvElement so TransferBackInvToCounter / TransferCounterToBackInv preserve all positions.
    internal static class CounterCapacity
    {
        internal const int CounterWidth = 20;
        internal const int CounterHeight = 7;
        internal const int StoreFrontCounterHeight = 5;

        private const int VanillaBackWindowHeightPx = 68;
        private const float AfterhourBottomGapPx = 4f;
        private const float AfterhourFrontGapPx = 8f;
        private const float LincolnLedgerGapPx = 5f;
        private const float LincolnLedgerRightAt960 = 368.5f;
        private const int DefaultAfterhourWindowHeightPx = 173;

        internal static bool IsInAfterhourCounterLocation()
        {
            MapUIManager map = MapUIManager.Instance;
            return map != null && map.Current != null && map.Current.ShowsStoreCounters;
        }

        internal static bool EnsureGridSize(GameGridInventory inv, PixelWindow win, int width, int height)
        {
            if (inv == null)
                return false;
            GridShape shape = inv.inventoryShape;
            if (shape != null && shape.width == width && shape.height == height)
                return false;
            inv.SetShape(width, height);
            inv.Validate();
            if (win != null)
                win.Validate();
            return true;
        }

        internal static void EnsureStoreCounters(EmporiumEntry entry, bool includeFront)
        {
            if (entry == null)
                return;
            EnsureGridSize(entry.backInvinvElement, entry.backInvWindow, CounterWidth, CounterHeight);
            EnsureGridSize(entry.backInvinvElementCounter, null, CounterWidth, CounterHeight);
            if (includeFront && !IsInAfterhourCounterLocation())
                EnsureGridSize(entry.frontInvinvElement, entry.frontInvWindow, CounterWidth, StoreFrontCounterHeight);
        }

        internal static Vector2 ComputeStoreBackPosition(EmporiumEntry entry, int canvasWidth, int canvasHeight)
        {
            PixelWindow backWin = entry != null ? entry.backInvWindow : null;
            int backW = backWin != null && backWin.widthPixels > 0 ? backWin.widthPixels : CounterWidth * 16 + 4;
            int backH = backWin != null && backWin.heightPixels > 0 ? backWin.heightPixels : CounterHeight * 16 + 4;
            float x = (canvasWidth / 2) - (backW / 2);
            float vanillaTop = -((canvasHeight / 100) * 52.2f);
            float vanillaBottom = vanillaTop - VanillaBackWindowHeightPx;
            float y = vanillaBottom + backH;
            if (y > 0f)
                y = 0f;
            return new Vector2(x, y);
        }

        internal static Vector2 ComputeAfterhourBackPosition(
            EmporiumEntry entry,
            AfterhourLocation location,
            int canvasWidth,
            int canvasHeight)
        {
            PixelWindow backWin = entry != null ? entry.backInvWindow : null;
            int backW = backWin != null && backWin.widthPixels > 0 ? backWin.widthPixels : CounterWidth * 16 + 4;
            int backH = backWin != null && backWin.heightPixels > 0 ? backWin.heightPixels : CounterHeight * 16 + 4;

            Vector2? frontPos = location != null ? location.GetFrontCounterPosition(canvasWidth, canvasHeight) : null;
            float frontX = frontPos.HasValue ? frontPos.Value.x : (canvasWidth / 100) * 78.5f;
            float ledgerRight = (canvasWidth / 960f) * LincolnLedgerRightAt960;
            float minX = ledgerRight + LincolnLedgerGapPx;
            float maxX = frontX - AfterhourFrontGapPx - backW;
            float x = minX <= maxX ? (minX + maxX) * 0.5f : Math.Max(0f, maxX);

            int afterhourH = entry != null && entry.afterhourWindow != null && entry.afterhourWindow.heightPixels > 0
                ? entry.afterhourWindow.heightPixels
                : DefaultAfterhourWindowHeightPx;
            float afterhourTop = -(canvasHeight - afterhourH);
            float y = afterhourTop + AfterhourBottomGapPx + backH;
            if (y > 0f)
                y = 0f;
            return new Vector2(x, y);
        }

        internal static Vector2 ComputeStoreFrontPosition(EmporiumEntry entry)
        {
            PixelWindow backWin = entry != null ? entry.backInvWindow : null;
            PixelWindow frontWin = entry != null ? entry.frontInvWindow : null;
            if (backWin == null || backWin.rectTransform == null)
                return Vector2.zero;
            Vector2 backPos = backWin.rectTransform.anchoredPosition;
            int frontH = frontWin != null && frontWin.heightPixels > 0 ? frontWin.heightPixels : StoreFrontCounterHeight * 16 + 4;
            float y = backPos.y + frontH;
            if (y > 0f)
                y = 0f;
            return new Vector2(backPos.x, y);
        }

        // Old-save guard for vanilla guns (Mounts.Guns where Vanilla == true) wearing mod attachments whose top-mounted
        // views now extend to cy < 0 (modifiedYOrigin > 0). ItemShapes.OnGameLoaded already handles all managed mod
        // items (useSpriteFromMod && IsManagedMod); this pass aligns vanilla guns carrying mod attachments safely
        // without overlap or item loss.
        internal static void AlignVanillaGunsAfterLoad()
        {
            EmporiumEntry entry = EmporiumEntry.Instance;
            if (entry == null)
                return;

            List<GameItem> all = CollectAllItems(entry);
            int updated = 0;
            int repositioned = 0;
            int rolledBack = 0;

            for (int i = 0; i < all.Count; i++)
            {
                GameItem item = all[i];
                if (item == null || IsDestroyed(item) || item.useSpriteFromMod)
                    continue;
                MountPoints mp;
                if (string.IsNullOrEmpty(item.identifier)
                    || !Mounts.Guns.TryGetValue(item.identifier, out mp)
                    || !mp.Vanilla)
                    continue;
                if (!HasShapeModifyingChild(item))
                    continue;

                var computed = item.ComputeModifiedShapeState(null, null, null, null, null);
                GridShape computedShape = computed.Item1;
                int newXOrigin = computed.Item2;
                int newYOrigin = computed.Item3;
                if (computedShape == null)
                    continue;

                bool sameShape = ShapesLocalEqual(item.modifiedShape, computedShape)
                    && item.modifiedXOrigin == newXOrigin
                    && item.modifiedYOrigin == newYOrigin;
                bool validNow = IsPlacementValidInGrid(item, item.modifiedShape);
                if (sameShape && validNow)
                    continue;

                GridShape oldModShape = item.modifiedShape;
                int oldXOrigin = item.modifiedXOrigin;
                int oldYOrigin = item.modifiedYOrigin;
                int origMinX = oldModShape != null ? oldModShape.minX : 0;
                int origMinY = oldModShape != null ? oldModShape.minY : 0;
                bool origFlipped = oldModShape != null && oldModShape.flipped;
                int origOrientation = oldModShape != null ? oldModShape.orientation : 0;

                GameInventory origInv = item.parentInventory;
                var slotInv = origInv as GameSlotInventory;
                GridShape preferred = slotInv != null
                    ? new GridShapeBuilder(computedShape).SetTransform(0, 0, false, slotInv.defaultOrientation).Build()
                    : new GridShapeBuilder(computedShape).SetTransform(origMinX, origMinY, origFlipped, origOrientation).Build();

                item.modifiedShape = preferred;
                item.modifiedXOrigin = newXOrigin;
                item.modifiedYOrigin = newYOrigin;

                if (origInv == null || !IsGridInventory(origInv))
                {
                    item.ValidateShapeState();
                    updated++;
                    continue;
                }

                if (IsPlacementValidInGrid(item, preferred))
                {
                    item.ValidateShapeState();
                    updated++;
                    continue;
                }

                GridShape placed = FindNearestValidInGrid(origInv, item, preferred, origMinX, origMinY, origFlipped, origOrientation);
                if (placed != null)
                {
                    item.modifiedShape = placed;
                    item.ValidateShapeState();
                    repositioned++;
                    continue;
                }

                // Zero-loss rollback if the container has no room for the expanded attachment rows.
                item.modifiedShape = oldModShape;
                item.modifiedXOrigin = oldXOrigin;
                item.modifiedYOrigin = oldYOrigin;
                item.Validate();
                rolledBack++;
            }

            if (updated + repositioned + rolledBack > 0)
            {
                GunsmithMod.Log("CounterCapacity: aligned " + (updated + repositioned + rolledBack)
                    + " vanilla gun(s) with mod attachments ("
                    + updated + " in place, " + repositioned + " repositioned, " + rolledBack + " kept)");
            }
        }

        private static bool HasShapeModifyingChild(GameItem item)
        {
            List<GameInventory> childInvs = GraphUtils.FindChildrenType<GameInventory>(item, null, null);
            if (childInvs == null)
                return false;
            for (int i = 0; i < childInvs.Count; i++)
            {
                List<GameItem> children = childInvs[i] != null ? childInvs[i].childItems : null;
                if (children == null)
                    continue;
                for (int j = 0; j < children.Count; j++)
                {
                    if (children[j] != null && children[j].MayParentShapeModifier())
                        return true;
                }
            }
            return false;
        }

        private static bool ShapesLocalEqual(GridShape a, GridShape b)
        {
            if (ReferenceEquals(a, b))
                return true;
            if (a == null || b == null || a.width != b.width || a.height != b.height)
                return false;
            for (int y = 0; y < a.height; y++)
            {
                for (int x = 0; x < a.width; x++)
                {
                    if (a.GetLocal(x, y) != b.GetLocal(x, y))
                        return false;
                }
            }
            return true;
        }

        private static bool IsGridInventory(GameInventory inv)
        {
            return inv is GameGridInventory || inv is GameGridScrollableInventory;
        }

        private static bool IsPlacementValidInGrid(GameItem item, GridShape candidate)
        {
            if (item == null || item.parentInventory == null || !IsGridInventory(item.parentInventory))
                return true;
            if (candidate == null)
                return false;
            return item.parentInventory.CheckShapePlacement(item, candidate, new List<GameItem>(0));
        }

        private static GridShape FindNearestValidInGrid(
            GameInventory inv,
            GameItem item,
            GridShape template,
            int anchorX,
            int anchorY,
            bool origFlipped,
            int origOrientation)
        {
            int invW, invH;
            var grid = inv as GameGridInventory;
            if (grid != null && grid.inventoryShape != null)
            {
                invW = grid.inventoryShape.width;
                invH = grid.inventoryShape.height;
            }
            else
            {
                var scroll = inv as GameGridScrollableInventory;
                if (scroll == null)
                    return null;
                invW = scroll.width;
                invH = scroll.height;
            }
            if (invW <= 0 || invH <= 0)
                return null;

            int o0 = ((origOrientation % 4) + 4) % 4;
            int[] orientations = { o0, (o0 + 1) % 4, (o0 + 2) % 4, (o0 + 3) % 4 };
            bool[] flips = { origFlipped, !origFlipped };
            var probe = new GridShapeBuilder(template);

            for (int fi = 0; fi < flips.Length; fi++)
            {
                for (int oi = 0; oi < orientations.Length; oi++)
                {
                    bool flip = flips[fi];
                    int orient = orientations[oi];
                    int gw = (orient % 2 != 0) ? template.height : template.width;
                    int gh = (orient % 2 != 0) ? template.width : template.height;
                    int maxX = invW - gw;
                    int maxY = Math.Min(invH - gh, 64);
                    if (maxX < 0 || maxY < 0)
                        continue;

                    int bestX = -1;
                    int bestY = -1;
                    int bestDist = int.MaxValue;
                    for (int y = 0; y <= maxY; y++)
                    {
                        for (int x = 0; x <= maxX; x++)
                        {
                            int dist = Math.Abs(x - anchorX) + Math.Abs(y - anchorY);
                            if (dist >= bestDist)
                                continue;
                            probe.SetTransform(x, y, flip, orient);
                            if (inv.CheckShapePlacement(item, probe, new List<GameItem>(0)))
                            {
                                bestDist = dist;
                                bestX = x;
                                bestY = y;
                                if (bestDist == 0)
                                    return new GridShapeBuilder(template).SetTransform(bestX, bestY, flip, orient).Build();
                            }
                        }
                    }
                    if (bestX >= 0)
                        return new GridShapeBuilder(template).SetTransform(bestX, bestY, flip, orient).Build();
                }
            }
            return null;
        }

        private static List<GameItem> CollectAllItems(EmporiumEntry entry)
        {
            var seen = new HashSet<GameItem>();
            var list = new List<GameItem>();
            AddItems(seen, list, entry.GetAllOwnedItems(false, true));
            AddItems(seen, list, entry.GetAllItems());
            AddTree(seen, list, entry.backInvinvElementCounter);
            AddTree(seen, list, entry.hirelingInv);
            return list;
        }

        private static void AddItems(HashSet<GameItem> seen, List<GameItem> list, List<GameItem> source)
        {
            if (source == null)
                return;
            for (int i = 0; i < source.Count; i++)
            {
                GameItem item = source[i];
                if (item != null && seen.Add(item))
                    list.Add(item);
            }
        }

        private static void AddTree(HashSet<GameItem> seen, List<GameItem> list, GraphNodeStorage root)
        {
            if (root == null)
                return;
            AddItems(seen, list, GraphUtils.FindAllChildrenType<GameItem>(root, null, null));
        }

        private static bool IsDestroyed(GameItem item)
        {
            var elem = item as GameItemElement;
            return elem != null && elem.IsDestroyed();
        }
    }

    // Runs inside EmporiumEntry.Start right after frontInvinvElement, backInvinvElement and backInvinvElementCounter
    // are instantiated and BEFORE PlayerStore.LoadGame decodes backInvJSON.
    [HarmonyPatch(typeof(EmporiumEntry), "SetupAfterhourInv")]
    internal static class CounterSetupAfterhourInvPatch
    {
        private static void Postfix(EmporiumEntry __instance)
        {
            try
            {
                CounterCapacity.EnsureStoreCounters(__instance, true);
            }
            catch (Exception e)
            {
                GunsmithMod.Warn("CounterCapacity SetupAfterhourInv: " + e.Message);
            }
        }
    }

    // Ensures counter grids are 20x7 before PlayerStore.LoadGame decodes backInvJSON even on reload paths that do not
    // recreate EmporiumEntry.
    [HarmonyPatch(typeof(PlayerStore), "LoadGame")]
    internal static class CounterLoadGamePrefixPatch
    {
        private static void Prefix()
        {
            try
            {
                CounterCapacity.EnsureStoreCounters(EmporiumEntry.Instance, !CounterCapacity.IsInAfterhourCounterLocation());
            }
            catch (Exception e)
            {
                GunsmithMod.Warn("CounterCapacity LoadGame Prefix: " + e.Message);
            }
        }
    }

    // Re-verifies counter dimensions and positions at the end of EmporiumEntry.Start.
    [HarmonyPatch(typeof(EmporiumEntry), "Start")]
    internal static class CounterEmporiumStartPatch
    {
        private static void Postfix(EmporiumEntry __instance)
        {
            try
            {
                CounterCapacity.EnsureStoreCounters(__instance, true);
            }
            catch (Exception e)
            {
                GunsmithMod.Warn("CounterCapacity Start: " + e.Message);
            }
        }
    }

    // Restores Store-mode front counter to 20x5 (instead of vanilla 18x4) when leaving Lincoln Arms or Dr. Jackson.
    [HarmonyPatch(typeof(EmporiumEntry), "ResetFrontCounterGrid")]
    internal static class CounterResetFrontGridPatch
    {
        private static void Postfix(EmporiumEntry __instance)
        {
            try
            {
                if (__instance == null)
                    return;
                CounterCapacity.EnsureGridSize(
                    __instance.frontInvinvElement,
                    __instance.frontInvWindow,
                    CounterCapacity.CounterWidth,
                    CounterCapacity.StoreFrontCounterHeight);
            }
            catch (Exception e)
            {
                GunsmithMod.Warn("CounterCapacity ResetFrontCounterGrid: " + e.Message);
            }
        }
    }

    // Calibrates backInvWindow anchoredPosition for 20x7 in both Store mode and afterhour counter locations
    // (Lincoln Arms and Dr. Jackson).
    [HarmonyPatch(typeof(EmporiumEntry), "GetBackInvPosition", new[] { typeof(int), typeof(int) })]
    internal static class CounterGetBackInvPositionPatch
    {
        private static void Postfix(EmporiumEntry __instance, int newCanvasWidth, int newCanvasHeight, ref Vector2 __result)
        {
            try
            {
                if (__instance == null)
                    return;
                CounterCapacity.EnsureGridSize(
                    __instance.backInvinvElement,
                    __instance.backInvWindow,
                    CounterCapacity.CounterWidth,
                    CounterCapacity.CounterHeight);
                CounterCapacity.EnsureGridSize(
                    __instance.backInvinvElementCounter,
                    null,
                    CounterCapacity.CounterWidth,
                    CounterCapacity.CounterHeight);

                AfterhourLocation current = MapUIManager.Instance != null ? MapUIManager.Instance.Current : null;
                bool hasAfterhourBack = current != null
                    && current.GetBackCounterPosition(newCanvasWidth, newCanvasHeight).HasValue;

                __result = hasAfterhourBack
                    ? CounterCapacity.ComputeAfterhourBackPosition(__instance, current, newCanvasWidth, newCanvasHeight)
                    : CounterCapacity.ComputeStoreBackPosition(__instance, newCanvasWidth, newCanvasHeight);
            }
            catch (Exception e)
            {
                GunsmithMod.Warn("CounterCapacity GetBackInvPosition: " + e.Message);
            }
        }
    }

    // Keeps Store-mode frontInvWindow stacked cleanly above backInvWindow with clamped top edge; preserves afterhour
    // counter shelf positions untouched.
    [HarmonyPatch(typeof(EmporiumEntry), "GetFrontInvPosition", new[] { typeof(int), typeof(int) })]
    internal static class CounterGetFrontInvPositionPatch
    {
        private static void Postfix(EmporiumEntry __instance, int newCanvasWidth, int newCanvasHeight, ref Vector2 __result)
        {
            try
            {
                if (__instance == null)
                    return;
                AfterhourLocation current = MapUIManager.Instance != null ? MapUIManager.Instance.Current : null;
                if (current != null && current.GetFrontCounterPosition(newCanvasWidth, newCanvasHeight).HasValue)
                    return;
                CounterCapacity.EnsureGridSize(
                    __instance.frontInvinvElement,
                    __instance.frontInvWindow,
                    CounterCapacity.CounterWidth,
                    CounterCapacity.StoreFrontCounterHeight);
                __result = CounterCapacity.ComputeStoreFrontPosition(__instance);
            }
            catch (Exception e)
            {
                GunsmithMod.Warn("CounterCapacity GetFrontInvPosition: " + e.Message);
            }
        }
    }

    // Ensures Store-mode frontInvWindow has validated 20x5 heightPixels and non-overlapping position whenever shown.
    [HarmonyPatch(typeof(EmporiumEntry), "ShowFrontWindow")]
    internal static class CounterShowFrontWindowPatch
    {
        private static void Postfix(EmporiumEntry __instance)
        {
            try
            {
                if (__instance == null || CounterCapacity.IsInAfterhourCounterLocation())
                    return;
                CounterCapacity.EnsureGridSize(
                    __instance.frontInvinvElement,
                    __instance.frontInvWindow,
                    CounterCapacity.CounterWidth,
                    CounterCapacity.StoreFrontCounterHeight);
                if (__instance.frontInvWindow != null && __instance.frontInvWindow.rectTransform != null)
                    __instance.frontInvWindow.rectTransform.anchoredPosition = CounterCapacity.ComputeStoreFrontPosition(__instance);
            }
            catch (Exception e)
            {
                GunsmithMod.Warn("CounterCapacity ShowFrontWindow: " + e.Message);
            }
        }
    }

    // Runs after ModHook.OnGameLoadedLate (including ItemShapes.OnGameLoaded) to align vanilla guns carrying mod
    // attachments whose shape now extends above the top of the gun (cy < 0).
    [HarmonyPatch(typeof(ModHook), "FireOnGameLoadedLate")]
    internal static class CounterVanillaGunLoadAlignPatch
    {
        private static void Postfix()
        {
            try
            {
                CounterCapacity.AlignVanillaGunsAfterLoad();
            }
            catch (Exception e)
            {
                GunsmithMod.Warn("CounterCapacity AlignVanillaGunsAfterLoad: " + e.Message);
            }
        }
    }
}
