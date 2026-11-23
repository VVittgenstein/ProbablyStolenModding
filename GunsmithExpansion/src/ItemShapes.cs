using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace GunsmithExpansion
{
    // Explicit grid-shape definition generated at build time (ItemShapes.Generated.cs) so the mod never relies on
    // GameItem.SetSpriteAndShapeFromMod's single-pixel (col*16+8, row*16+7) alpha probe and performs zero runtime file I/O
    // (manifest.xml <Capabilities/> remains empty).
    internal sealed class ShapeDef
    {
        internal readonly int Cols;
        internal readonly int Rows;
        internal readonly string Mask;
        internal readonly byte[] Data;

        internal ShapeDef(int cols, int rows, string mask)
        {
            Cols = cols;
            Rows = rows;
            Mask = mask ?? "";
            Data = new byte[Math.Max(0, cols * rows)];
            int len = Math.Min(Mask.Length, Data.Length);
            for (int i = 0; i < len; i++)
                Data[i] = Mask[i] == '1' ? (byte)1 : (byte)0;
        }
    }

    // Explicit GridShape application, orientation/flip preservation during sprite switches, and safe old-save shape
    // alignment on ModHook.OnGameLoadedLate.
    //
    // Why a Harmony Prefix on GameItem.SetSpriteAndShapeFromMod (filtered to GunsmithExpansion and GunsmithExpansion2077):
    //   1. Covers every item factory and sprite-toggle site in this mod (guns incl. 2077 Add-On, gun frames, gun mods,
    //      blank chips, ammo, propellant, reloader off/on, gun cabinets all 60 states, ammo box closed/open, certificates,
    //      chip box, delivery boxes, gunsmith sign, business card, manuals/catalogs, X-ray scanner, PaperBookRegistry,
    //      and LincolnGoods.Probe) without duplicating calls across 14 source files.
    //   2. Avoids running vanilla's (8, 7) Texture2D.GetPixel loop and avoids vanilla ComputeModifiedShapeState's anchor
    //      drift when a rotated (orientation != 0) or flipped item changes width/height.
    //   3. Fast-paths same-shape sprite toggles (Cabinets.UpdatePicture, LincolnItems ammo box open/close, Reloader on/off)
    //      by updating only sprite metadata + Validate(), leaving modifiedShape's (minX, minY, flipped, orientation) intact.
    internal static partial class ItemShapes
    {
        private static readonly HashSet<string> warnedSizeMismatch = new HashSet<string>(StringComparer.Ordinal);

        internal static bool IsManagedMod(string modId)
        {
            return modId == GunsmithMod.ModId || modId == AddOn2077.ModId;
        }

        // Resolves either a concrete sprite key (e.g. "gse_w009", "gse_cabinet_small_3_2", "ammo_556")
        // or a registered item ID (e.g. "gse_frame_w009", "gse_ammo_box", "gse_reloader", "gse_cabinet_small",
        // "gse_cabinet_large", "gse_ammo_556", "gse_blank_chip", "xray_scanner") to its Table sprite key.
        internal static string ResolveSpriteKey(string idOrSprite)
        {
            if (string.IsNullOrEmpty(idOrSprite))
                return null;
            if (Table.ContainsKey(idOrSprite))
                return idOrSprite;
            if (idOrSprite.StartsWith("gse_frame_", StringComparison.Ordinal))
            {
                string gunKey = "gse_" + idOrSprite.Substring("gse_frame_".Length);
                if (Table.ContainsKey(gunKey))
                    return gunKey;
            }
            if (idOrSprite == "gse_ammo_box")
                return Table.ContainsKey("gse_ammo_box_closed") ? "gse_ammo_box_closed" : null;
            if (idOrSprite.StartsWith("gse_ammo_", StringComparison.Ordinal))
            {
                string ammoKey = "ammo_" + idOrSprite.Substring("gse_ammo_".Length);
                if (Table.ContainsKey(ammoKey))
                    return ammoKey;
            }
            if (idOrSprite == "gse_reloader")
                return Table.ContainsKey("gse_reloader_off") ? "gse_reloader_off" : null;
            if (idOrSprite == "gse_cabinet_small")
                return Table.ContainsKey("gse_cabinet_small_2_0") ? "gse_cabinet_small_2_0" : null;
            if (idOrSprite == "gse_cabinet_large")
                return Table.ContainsKey("gse_cabinet_large_2_0") ? "gse_cabinet_large_2_0" : null;
            if (idOrSprite == "gse_blank_chip")
                return Table.ContainsKey("gse_blank_chip_1") ? "gse_blank_chip_1" : null;
            if (idOrSprite == "xray_scanner")
                return Table.ContainsKey("gse_xray") ? "gse_xray" : null;
            return null;
        }

        internal static bool TryGet(string idOrSprite, out ShapeDef def)
        {
            def = null;
            string key = ResolveSpriteKey(idOrSprite);
            return key != null
                && Table.TryGetValue(key, out def)
                && def != null
                && def.Cols > 0
                && def.Rows > 0;
        }

        // Zero-instantiation cell bounding-box lookup for item IDs and sprite keys (e.g. LincolnGoods shelf layout).
        internal static bool TryGetSize(string idOrSprite, out int cols, out int rows)
        {
            ShapeDef def;
            if (TryGet(idOrSprite, out def))
            {
                cols = def.Cols;
                rows = def.Rows;
                return true;
            }
            cols = 0;
            rows = 0;
            return false;
        }

        internal static GridShape BuildBaseShape(ShapeDef def)
        {
            return new GridShapeBuilder().SetData(def.Data, def.Cols).Build();
        }

        internal static bool MatchesShape(GridShape shape, ShapeDef def)
        {
            if (shape == null || def == null || shape.width != def.Cols || shape.height != def.Rows)
                return false;
            byte[] data = def.Data;
            int cols = def.Cols;
            int rows = def.Rows;
            for (int y = 0; y < rows; y++)
            {
                int rowOff = y * cols;
                for (int x = 0; x < cols; x++)
                {
                    if (shape.GetLocal(x, y) != data[rowOff + x])
                        return false;
                }
            }
            return true;
        }

        // Public helper to apply an explicit shape by sprite key or item ID directly (preserving position, orientation, and flip).
        internal static bool Apply(GameItem item, string idOrSprite)
        {
            if (item == null || string.IsNullOrEmpty(idOrSprite))
                return false;
            string modId = IsManagedMod(item.spriteAtlasPath) ? item.spriteAtlasPath : GunsmithMod.ModId;
            return Apply(item, modId, idOrSprite);
        }

        internal static bool Apply(GameItem item, string modId, string idOrSprite)
        {
            if (item == null || !IsManagedMod(modId))
                return false;
            string spriteKey = ResolveSpriteKey(idOrSprite);
            ShapeDef def;
            if (spriteKey == null || !Table.TryGetValue(spriteKey, out def) || def == null || def.Cols <= 0 || def.Rows <= 0)
                return false;
            Sprite sprite;
            if (!TryGetModSprite(modId, spriteKey, out sprite))
                return false;
            if (!SpriteMatchesDefSize(sprite, def))
            {
                WarnSizeMismatchOnce(modId, spriteKey, sprite, def);
                return false;
            }
            bool allowRelocate = !SaveManager.IsDecoding && IsRootedInLiveStore(item);
            ApplyShapeCore(item, def, allowRelocate, out _);
            return true;
        }

        private static bool TryGetExact(string spriteKey, out ShapeDef def)
        {
            def = null;
            return !string.IsNullOrEmpty(spriteKey)
                && Table.TryGetValue(spriteKey, out def)
                && def != null
                && def.Cols > 0
                && def.Rows > 0;
        }

        // Intercepts GameItem.SetSpriteAndShapeFromMod for GunsmithExpansion and GunsmithExpansion2077.
        // Returns true when handled (caller skips original), false to fall back to vanilla behavior.
        internal static bool TryHandleSetSpriteAndShapeFromMod(GameItem item, string modId, string spriteKey)
        {
            if (item == null || !IsManagedMod(modId))
                return false;
            ShapeDef def;
            if (!TryGetExact(spriteKey, out def))
                return false;
            Sprite sprite;
            if (!TryGetModSprite(modId, spriteKey, out sprite))
                return false;
            if (!SpriteMatchesDefSize(sprite, def))
            {
                WarnSizeMismatchOnce(modId, spriteKey, sprite, def);
                return false;
            }

            // SetSprite sets spriteAtlasPath, spritePath, and protected spriteChanged = true without reflection.
            item.SetSprite(modId, spriteKey);
            item.useSpriteFromMod = true;

            // Fast path: when base shape already matches the explicit mask (e.g. gun cabinet state updates,
            // ammo box open/close, reloader off/on), never mutate shape/modifiedShape so rotated/flipped
            // items keep their exact transform without re-validating container placement.
            if (MatchesShape(item.shape, def) && item.modifiedShape != null)
            {
                item.Validate();
                return true;
            }

            bool allowRelocate = !SaveManager.IsDecoding && IsRootedInLiveStore(item);
            ApplyShapeCore(item, def, allowRelocate, out _);
            return true;
        }

        private enum AlignOutcome
        {
            Unchanged,
            UpdatedInPlace,
            RepositionedSameContainer,
            RelocatedFallback,
            RolledBack
        }

        // Subscribed to ModHook.OnGameLoadedLate (after SaveRecord, Cabinets, and Lincoln).
        // SaveManager.DecodeNodesInternal restores the GridShape serialized in old saves (e.g. 0.8.0's (8,7) sampled holes
        // or older 13x3 gun canvases). This pass aligns every managed item's shape safely, preserving orientation/flip and
        // never allowing grid overlap or item loss.
        internal static void OnGameLoaded()
        {
            try
            {
                EmporiumEntry entry = EmporiumEntry.Instance;
                if (entry == null)
                    return;

                List<GameItem> items = CollectAllStoreItems(entry);
                if (items.Count == 0)
                    return;

                // Process deepest nested items first (e.g. gun mods inside slots, guns inside cabinets/boxes)
                // before outer grid-placed items so parent guns already see final child states.
                items.Sort((a, b) => NestingDepth(b).CompareTo(NestingDepth(a)));

                int inPlace = 0;
                int repositioned = 0;
                int relocated = 0;
                int rolledBack = 0;

                for (int i = 0; i < items.Count; i++)
                {
                    GameItem item = items[i];
                    if (item == null || IsDestroyed(item))
                        continue;
                    if (!item.useSpriteFromMod || !IsManagedMod(item.spriteAtlasPath))
                        continue;

                    ShapeDef def;
                    if (!TryGetExact(item.spritePath, out def))
                        continue;

                    Sprite sprite;
                    if (!TryGetModSprite(item.spriteAtlasPath, item.spritePath, out sprite))
                        continue;
                    if (!SpriteMatchesDefSize(sprite, def))
                    {
                        WarnSizeMismatchOnce(item.spriteAtlasPath, item.spritePath, sprite, def);
                        continue;
                    }

                    bool shapeMismatch = !MatchesShape(item.shape, def);
                    bool modShapeChanged = !shapeMismatch && NeedsModifiedShapeRefresh(item);
                    bool placementInvalid = !IsPlacementValidInCurrentGrid(item);
                    if (!shapeMismatch && !modShapeChanged && !placementInvalid)
                        continue;

                    AlignOutcome outcome;
                    ApplyShapeCore(item, def, allowRelocate: true, out outcome);
                    switch (outcome)
                    {
                        case AlignOutcome.UpdatedInPlace:
                            inPlace++;
                            break;
                        case AlignOutcome.RepositionedSameContainer:
                            repositioned++;
                            break;
                        case AlignOutcome.RelocatedFallback:
                            relocated++;
                            break;
                        case AlignOutcome.RolledBack:
                            rolledBack++;
                            break;
                    }
                }

                int total = inPlace + repositioned + relocated + rolledBack;
                if (total > 0)
                {
                    GunsmithMod.Log("ItemShapes: aligned " + total + " saved item(s) ("
                        + inPlace + " in place, "
                        + repositioned + " repositioned, "
                        + relocated + " relocated, "
                        + rolledBack + " kept)");
                }
            }
            catch (Exception e)
            {
                GunsmithMod.Warn("ItemShapes.OnGameLoaded: " + e);
            }
        }

        private static void ApplyShapeCore(GameItem item, ShapeDef def, bool allowRelocate, out AlignOutcome outcome)
        {
            outcome = AlignOutcome.Unchanged;
            GameInventory origInv = item.parentInventory;
            GridShape oldShape = item.shape;
            GridShape oldModShape = item.modifiedShape;
            int oldXOrigin = item.modifiedXOrigin;
            int oldYOrigin = item.modifiedYOrigin;

            int origMinX = oldModShape != null ? oldModShape.minX : 0;
            int origMinY = oldModShape != null ? oldModShape.minY : 0;
            bool origFlipped = oldModShape != null && oldModShape.flipped;
            int origOrientation = oldModShape != null ? oldModShape.orientation : 0;

            GridShape newBase = BuildBaseShape(def);

            // Seed item.shape and item.modifiedShape with identical (Cols, Rows) before calling ComputeModifiedShapeState.
            // This avoids vanilla ComputeModifiedShapeState's anchor drift when oldWidth/oldHeight != newWidth/newHeight
            // while orientation != 0 or flipped == true, and still merges any attached gun-mod shapes cleanly.
            item.shape = newBase;
            item.modifiedShape = new GridShapeBuilder(newBase)
                .SetTransform(origMinX, origMinY, origFlipped, origOrientation)
                .Build();
            item.modifiedXOrigin = 0;
            item.modifiedYOrigin = 0;

            var computed = item.ComputeModifiedShapeState(null, null, null, null, null);
            GridShape computedShape = computed.Item1;
            int newXOrigin = computed.Item2;
            int newYOrigin = computed.Item3;

            // Anchor the overall modified bounding box at the item's saved (origMinX, origMinY, origFlipped, origOrientation).
            // For GameSlotInventory items, enforce the slot's canonical (0, 0, false, defaultOrientation) transform.
            var slotInv = origInv as GameSlotInventory;
            GridShape preferred;
            if (slotInv != null)
            {
                preferred = new GridShapeBuilder(computedShape)
                    .SetTransform(0, 0, false, slotInv.defaultOrientation)
                    .Build();
            }
            else
            {
                preferred = new GridShapeBuilder(computedShape)
                    .SetTransform(origMinX, origMinY, origFlipped, origOrientation)
                    .Build();
            }

            item.modifiedShape = preferred;
            item.modifiedXOrigin = newXOrigin;
            item.modifiedYOrigin = newYOrigin;

            // Detached items or single-slot inventories (gun cabinet slots, gun mod slots, machine slots) do not perform
            // 2D grid collision, so updating in place never causes overlaps and avoids firing slot remove/add callbacks.
            if (origInv == null || !IsGridInventory(origInv))
            {
                item.ValidateShapeState();
                outcome = AlignOutcome.UpdatedInPlace;
                return;
            }

            // Tier 1: Same container, exact original (minX, minY, flipped, orientation), zero Expel/Accept callbacks.
            if (CanPlaceInSameGrid(origInv, item, preferred))
            {
                item.modifiedShape = preferred;
                item.ValidateShapeState();
                outcome = AlignOutcome.UpdatedInPlace;
                return;
            }

            // Tier 2: Same container, nearest free (x, y) preserving original (flipped, orientation).
            GridShape sameOrient = FindPlacementInSameGrid(origInv, item, preferred, origMinX, origMinY, origFlipped, origOrientation, true);
            if (sameOrient != null)
            {
                item.modifiedShape = sameOrient;
                item.ValidateShapeState();
                GunsmithMod.Log("ItemShapes: repositioned " + DescribeItem(item)
                    + " in same container from (" + origMinX + "," + origMinY + ") to ("
                    + sameOrient.minX + "," + sameOrient.minY + ") keeping orientation="
                    + origOrientation + " flipped=" + origFlipped);
                outcome = AlignOutcome.RepositionedSameContainer;
                return;
            }

            // Tier 3: Same container, nearest free (x, y) allowing orientation/flip adjustment.
            GridShape anyOrient = FindPlacementInSameGrid(origInv, item, preferred, origMinX, origMinY, origFlipped, origOrientation, false);
            if (anyOrient != null)
            {
                item.modifiedShape = anyOrient;
                item.ValidateShapeState();
                GunsmithMod.Log("ItemShapes: repositioned " + DescribeItem(item)
                    + " in same container to (" + anyOrient.minX + "," + anyOrient.minY
                    + ") orientation=" + anyOrient.orientation + " flipped=" + anyOrient.flipped);
                outcome = AlignOutcome.RepositionedSameContainer;
                return;
            }

            // Tier 4: Original container cannot fit the updated shape; safely relocate to a fallback store inventory.
            if (allowRelocate && TryRelocateToFallbackStore(item, origInv, preferred, origFlipped, origOrientation,
                oldShape, oldModShape, oldXOrigin, oldYOrigin))
            {
                outcome = AlignOutcome.RelocatedFallback;
                return;
            }

            // Tier 5: Zero-loss rollback (never destroy or orphan an item if every store inventory is completely full).
            item.shape = oldShape;
            item.modifiedShape = oldModShape;
            item.modifiedXOrigin = oldXOrigin;
            item.modifiedYOrigin = oldYOrigin;
            item.Validate();
            GunsmithMod.Warn("ItemShapes: could not fit updated shape for " + DescribeItem(item)
                + " in original or fallback store inventories; kept previous shape to avoid overlap or item loss");
            outcome = AlignOutcome.RolledBack;
        }

        private static bool NeedsModifiedShapeRefresh(GameItem item)
        {
            if (item == null || item.modifiedShape == null)
                return true;
            // Only items with child inventories (e.g. guns with mounted attachments) can have a modifiedShape distinct from shape.
            List<GameInventory> childInvs = GraphUtils.FindChildrenType<GameInventory>(item, null, null);
            bool hasModifier = false;
            if (childInvs != null)
            {
                for (int i = 0; i < childInvs.Count && !hasModifier; i++)
                {
                    List<GameItem> children = childInvs[i] != null ? childInvs[i].childItems : null;
                    if (children == null)
                        continue;
                    for (int j = 0; j < children.Count; j++)
                    {
                        if (children[j] != null && children[j].MayParentShapeModifier())
                        {
                            hasModifier = true;
                            break;
                        }
                    }
                }
            }
            if (!hasModifier)
                return !ShapesLocalEqual(item.shape, item.modifiedShape) || item.modifiedXOrigin != 0 || item.modifiedYOrigin != 0;

            GridShape curMod = item.modifiedShape;
            var computed = item.ComputeModifiedShapeState(null, null, null, null, null);
            return !ShapesLocalEqual(curMod, computed.Item1)
                || item.modifiedXOrigin != computed.Item2
                || item.modifiedYOrigin != computed.Item3;
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

        private static bool IsPlacementValidInCurrentGrid(GameItem item)
        {
            if (item == null || item.parentInventory == null || !IsGridInventory(item.parentInventory))
                return true;
            if (item.modifiedShape == null)
                return false;
            return CanPlaceInSameGrid(item.parentInventory, item, item.modifiedShape);
        }

        private static bool IsGridInventory(GameInventory inv)
        {
            return inv is GameGridInventory || inv is GameGridScrollableInventory;
        }

        private static bool CanPlaceInSameGrid(GameInventory inv, GameItem item, GridShape candidate)
        {
            if (inv == null || item == null || candidate == null)
                return false;
            if (!inv.CheckShapePlacement(item, candidate, new List<GameItem>(0)))
                return false;
            if (item.MayParentShapeModifier())
            {
                var marker = new SlotMarker
                {
                    item = item,
                    inventory = inv,
                    itemGridShape = candidate,
                    numTransfer = item.unitCount
                };
                if (!inv.CheckChangeParentShapeStateModifier(item, marker, candidate, item.modifiedState))
                    return false;
            }
            return true;
        }

        private static bool GetGridDimensions(GameInventory inv, out int width, out int height)
        {
            var grid = inv as GameGridInventory;
            if (grid != null && grid.inventoryShape != null)
            {
                width = grid.inventoryShape.width;
                height = grid.inventoryShape.height;
                return width > 0 && height > 0;
            }
            var scroll = inv as GameGridScrollableInventory;
            if (scroll != null)
            {
                width = scroll.width;
                height = scroll.height;
                return width > 0 && height > 0;
            }
            width = 0;
            height = 0;
            return false;
        }

        private static GridShape FindPlacementInSameGrid(
            GameInventory inv,
            GameItem item,
            GridShape template,
            int anchorX,
            int anchorY,
            bool origFlipped,
            int origOrientation,
            bool sameOrientationOnly)
        {
            int invW, invH;
            if (!GetGridDimensions(inv, out invW, out invH))
                return null;

            int[] orientations = sameOrientationOnly
                ? new[] { NormalizeOrientation(origOrientation) }
                : BuildOrientationOrder(origOrientation);
            bool[] flips = sameOrientationOnly
                ? new[] { origFlipped }
                : new[] { origFlipped, !origFlipped };

            // First require the entire sprite bounding box inside [0..invW, 0..invH]; if none fits (e.g. item with
            // trailing empty cells in shape_rows), allow cell-mask-only bounds checked by CheckShapePlacement.
            for (int boundsPass = 0; boundsPass < 2; boundsPass++)
            {
                bool strictRect = boundsPass == 0;
                for (int fi = 0; fi < flips.Length; fi++)
                {
                    bool flip = flips[fi];
                    for (int oi = 0; oi < orientations.Length; oi++)
                    {
                        int orient = orientations[oi];
                        if (!sameOrientationOnly && flip == origFlipped && orient == NormalizeOrientation(origOrientation))
                            continue;

                        GridShape found = SearchClosestValidInGrid(inv, item, template, invW, invH, anchorX, anchorY, flip, orient, strictRect, isFallbackTarget: false);
                        if (found != null)
                            return found;
                    }
                }
            }
            return null;
        }

        private static GridShape SearchClosestValidInGrid(
            GameInventory inv,
            GameItem item,
            GridShape template,
            int invW,
            int invH,
            int anchorX,
            int anchorY,
            bool flip,
            int orient,
            bool strictRect,
            bool isFallbackTarget)
        {
            int gw = (orient % 2 != 0) ? template.height : template.width;
            int gh = (orient % 2 != 0) ? template.width : template.height;
            int maxX = strictRect ? invW - gw : invW - 1;
            int maxY = strictRect ? invH - gh : invH - 1;
            if (maxX < 0 || maxY < 0)
                return null;

            // Bound initial Y scan on very tall scrollable inventories (default height 400) before scanning the tail.
            int cappedMaxY = maxY;
            if (inv is GameGridScrollableInventory && maxY > 64)
            {
                int occupiedMaxY = Math.Max(0, anchorY);
                List<GameItem> existing = inv.childItems;
                if (existing != null)
                {
                    for (int i = 0; i < existing.Count; i++)
                    {
                        GameItem other = existing[i];
                        if (other != null && other.modifiedShape != null && other.modifiedShape.maxY > occupiedMaxY)
                            occupiedMaxY = other.modifiedShape.maxY;
                    }
                }
                cappedMaxY = Math.Min(maxY, occupiedMaxY + gh + 8);
            }

            GridShapeBuilder probe = new GridShapeBuilder(template);
            int bestX = -1;
            int bestY = -1;
            int bestDist = int.MaxValue;

            for (int band = 0; band < 2; band++)
            {
                int yStart = band == 0 ? 0 : cappedMaxY + 1;
                int yEnd = band == 0 ? cappedMaxY : maxY;
                if (yStart > yEnd)
                    continue;

                for (int y = yStart; y <= yEnd; y++)
                {
                    for (int x = 0; x <= maxX; x++)
                    {
                        int dist = Math.Abs(x - anchorX) + Math.Abs(y - anchorY);
                        if (dist >= bestDist)
                            continue;

                        probe.SetTransform(x, y, flip, orient);
                        bool ok;
                        if (isFallbackTarget)
                        {
                            SlotMarker marker = inv.TryInventorySlot(item, item.unitCount, probe, null);
                            ok = marker != null && marker.numTransfer >= item.unitCount && marker.targetItem == null;
                        }
                        else
                        {
                            ok = CanPlaceInSameGrid(inv, item, probe);
                        }

                        if (ok)
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

            return null;
        }

        private static bool TryRelocateToFallbackStore(
            GameItem item,
            GameInventory origInv,
            GridShape template,
            bool origFlipped,
            int origOrientation,
            GridShape oldShape,
            GridShape oldModShape,
            int oldXOrigin,
            int oldYOrigin)
        {
            EmporiumEntry entry = EmporiumEntry.Instance;
            if (entry == null)
                return false;

            // Do not relocate non-owned items (e.g. customer repair guns on the counter) into player storage.
            if (!GeneralHelper.IsItemOwned(item))
                return false;

            // Match GraphUtils.EmporiumTryAdd's accessible, persisted store inventories (never calling DestroyIfUnplaced,
            // never relocating into non-persisted backInvinvElementCounter or non-player hiddenElement, and excluding
            // contraband/stolen items from showcaseElement).
            var candidates = new List<GameInventory>();
            if (MapUIManager.Instance != null && MapUIManager.Instance.isOutside)
                AddFallback(candidates, entry.afterhourInventory, origInv);
            AddFallback(candidates, entry.invElement, origInv);
            AddFallback(candidates, entry.backInvinvElement, origInv);
            if (!IsRiskyForShowcase(item))
                AddFallback(candidates, entry.showcaseElement, origInv);
            AddFallback(candidates, entry.frontInvinvElement, origInv);

            int[] orientations = BuildOrientationOrder(origOrientation);
            bool[] flips = { origFlipped, !origFlipped };

            for (int c = 0; c < candidates.Count; c++)
            {
                GameInventory targetInv = candidates[c];
                if (!targetInv.MayHaveValidInventorySlot(item))
                    continue;
                int invW, invH;
                if (!GetGridDimensions(targetInv, out invW, out invH))
                    continue;

                GridShape targetShape = null;
                for (int boundsPass = 0; boundsPass < 2 && targetShape == null; boundsPass++)
                {
                    bool strictRect = boundsPass == 0;
                    for (int fi = 0; fi < flips.Length && targetShape == null; fi++)
                    {
                        for (int oi = 0; oi < orientations.Length && targetShape == null; oi++)
                        {
                            targetShape = SearchClosestValidInGrid(
                                targetInv, item, template, invW, invH, 0, 0,
                                flips[fi], orientations[oi], strictRect, isFallbackTarget: true);
                        }
                    }
                }

                if (targetShape == null)
                    continue;

                if (!origInv.Expel(item))
                {
                    GunsmithMod.Warn("ItemShapes: could not expel " + DescribeItem(item) + " from " + DescribeInventory(origInv));
                    return false;
                }

                item.modifiedShape = targetShape;
                if (targetInv.UncheckedAccept(item))
                {
                    origInv.Validate();
                    GunsmithMod.Log("ItemShapes: relocated " + DescribeItem(item)
                        + " from " + DescribeInventory(origInv)
                        + " to " + DescribeInventory(targetInv)
                        + " at (" + targetShape.minX + "," + targetShape.minY
                        + ") orientation=" + targetShape.orientation + " flipped=" + targetShape.flipped);
                    return true;
                }

                // Defensive recovery if UncheckedAccept ever returned false after Expel: restore previous shape first.
                item.shape = oldShape;
                item.modifiedShape = oldModShape;
                item.modifiedXOrigin = oldXOrigin;
                item.modifiedYOrigin = oldYOrigin;
                origInv.UncheckedAccept(item);
                GunsmithMod.Warn("ItemShapes: fallback inventory " + DescribeInventory(targetInv)
                    + " rejected " + DescribeItem(item) + " after expel; restored to original container");
                return false;
            }

            return false;
        }

        private static void AddFallback(List<GameInventory> list, GameInventory inv, GameInventory exclude)
        {
            if (inv == null || ReferenceEquals(inv, exclude) || !IsGridInventory(inv))
                return;
            for (int i = 0; i < list.Count; i++)
            {
                if (ReferenceEquals(list[i], inv))
                    return;
            }
            list.Add(inv);
        }

        private static bool IsRiskyForShowcase(GameItem item)
        {
            if (item == null)
                return false;
            if (item.IsTag("CONTRABAND_ITEM_TAG") || item.IsTag("STOLEN_TAG"))
                return true;
            if (Cabinets.IsCabinet(item))
            {
                List<GameItem> children = GraphUtils.FindAllChildrenType<GameItem>(item, null, null);
                if (children != null)
                {
                    for (int i = 0; i < children.Count; i++)
                    {
                        GameItem child = children[i];
                        if (child != null && (child.IsTag("CONTRABAND_ITEM_TAG") || child.IsTag("STOLEN_TAG")))
                            return true;
                    }
                }
            }
            return false;
        }

        private static List<GameItem> CollectAllStoreItems(EmporiumEntry entry)
        {
            var seen = new HashSet<GameItem>();
            var list = new List<GameItem>();

            AddItems(seen, list, entry.GetAllOwnedItems(false, true));
            AddItems(seen, list, entry.GetAllItems());
            AddInventoryTree(seen, list, entry.backInvinvElementCounter);
            AddInventoryTree(seen, list, entry.hirelingInv);
            AddInventoryTree(seen, list, entry.soldElement);
            AddInventoryTree(seen, list, entry.cassettePlayerElement);
            AddInventoryTree(seen, list, entry.trashcan);
            AddInventoryTree(seen, list, entry.dossier);
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

        private static void AddInventoryTree(HashSet<GameItem> seen, List<GameItem> list, GraphNodeStorage root)
        {
            if (root == null)
                return;
            AddItems(seen, list, GraphUtils.FindAllChildrenType<GameItem>(root, null, null));
        }

        private static int NestingDepth(GameItem item)
        {
            int depth = 0;
            GameItem cur = item;
            while (cur != null && cur.parentInventory != null && depth < 16)
            {
                cur = cur.parentInventory.GetParentItem();
                if (cur != null)
                    depth++;
            }
            return depth;
        }

        private static bool IsRootedInLiveStore(GameItem item)
        {
            EmporiumEntry entry = EmporiumEntry.Instance;
            if (entry == null || item == null || item.parentInventory == null)
                return false;
            GraphNodeStorage root = GameItem.GetRootNode(item);
            if (root == null)
                return false;
            return ReferenceEquals(root, entry.invElement)
                || ReferenceEquals(root, entry.backInvinvElement)
                || ReferenceEquals(root, entry.backInvinvElementCounter)
                || ReferenceEquals(root, entry.frontInvinvElement)
                || ReferenceEquals(root, entry.showcaseElement)
                || ReferenceEquals(root, entry.hiddenElement)
                || ReferenceEquals(root, entry.afterhourInventory)
                || ReferenceEquals(root, entry.afterhourWindow)
                || ReferenceEquals(root, entry.hirelingInv)
                || ReferenceEquals(root, entry.soldElement)
                || ReferenceEquals(root, entry.docInvElement)
                || ReferenceEquals(root, entry.trashInvElement)
                || ReferenceEquals(root, entry.vendingMachineElement)
                || ReferenceEquals(root, entry.vendingFountainElement)
                || ReferenceEquals(root, entry.cassettePlayerElement)
                || ReferenceEquals(root, entry.invWindow)
                || ReferenceEquals(root, entry.backInvWindow)
                || ReferenceEquals(root, entry.frontInvWindow)
                || ReferenceEquals(root, entry.showcaseWindow)
                || ReferenceEquals(root, entry.hiddenWindow)
                || ReferenceEquals(root, entry.hirelingWindow)
                || ReferenceEquals(root, entry.docInvWindow)
                || ReferenceEquals(root, entry.trashInvWindow)
                || ReferenceEquals(root, entry.vendingMachineWindow)
                || ReferenceEquals(root, entry.vendingFountainWindow)
                || ReferenceEquals(root, entry.cassettePlayerWindow)
                || ReferenceEquals(root, entry.trashcan)
                || ReferenceEquals(root, entry.dossier);
        }

        private static bool TryGetModSprite(string modId, string spriteKey, out Sprite sprite)
        {
            sprite = null;
            if (string.IsNullOrEmpty(modId) || string.IsNullOrEmpty(spriteKey))
                return false;
            List<LoadedMod> mods = ModLoader.Mods;
            if (mods == null)
                return false;
            for (int i = 0; i < mods.Count; i++)
            {
                LoadedMod m = mods[i];
                if (m != null && m.Manifest != null && m.Manifest.ID == modId && m.Sprites != null)
                    return m.Sprites.TryGetValue(spriteKey, out sprite) && sprite != null;
            }
            return false;
        }

        private static int GridSize()
        {
            return UISettings.current != null && UISettings.current.gridSize > 0 ? UISettings.current.gridSize : 16;
        }

        private static bool SpriteMatchesDefSize(Sprite sprite, ShapeDef def)
        {
            if (sprite == null || def == null)
                return false;
            int gs = GridSize();
            Rect r = sprite.rect;
            int cols = (int)r.width / gs;
            int rows = (int)r.height / gs;
            return cols == def.Cols && rows == def.Rows;
        }

        private static void WarnSizeMismatchOnce(string modId, string spriteKey, Sprite sprite, ShapeDef def)
        {
            string token = modId + ":" + spriteKey;
            if (!warnedSizeMismatch.Add(token))
                return;
            int gs = GridSize();
            Rect r = sprite.rect;
            GunsmithMod.Warn("ItemShapes: sprite '" + token + "' is "
                + ((int)r.width / gs) + "x" + ((int)r.height / gs)
                + " cells, but ShapeDef expects " + def.Cols + "x" + def.Rows
                + "; falling back to sprite sampling");
        }

        private static int NormalizeOrientation(int orientation)
        {
            int o = orientation % 4;
            return o < 0 ? o + 4 : o;
        }

        private static int[] BuildOrientationOrder(int origOrientation)
        {
            int o = NormalizeOrientation(origOrientation);
            return new[] { o, (o + 1) % 4, (o + 2) % 4, (o + 3) % 4 };
        }

        private static bool IsDestroyed(GameItem item)
        {
            var elem = item as GameItemElement;
            return elem != null && elem.IsDestroyed();
        }

        private static string DescribeItem(GameItem item)
        {
            if (item == null)
                return "<null>";
            return (item.identifier ?? "?") + " (sprite=" + (item.spritePath ?? "?") + ", uid=" + item.uniqueId + ")";
        }

        private static string DescribeInventory(GameInventory inv)
        {
            if (inv == null)
                return "<null>";
            EmporiumEntry entry = EmporiumEntry.Instance;
            if (entry != null)
            {
                if (ReferenceEquals(inv, entry.invElement)) return "storage(invElement)";
                if (ReferenceEquals(inv, entry.backInvinvElement)) return "backCounter(backInvinvElement)";
                if (ReferenceEquals(inv, entry.backInvinvElementCounter)) return "backCounterSecondary";
                if (ReferenceEquals(inv, entry.frontInvinvElement)) return "frontCounter(frontInvinvElement)";
                if (ReferenceEquals(inv, entry.showcaseElement)) return "showcase(showcaseElement)";
                if (ReferenceEquals(inv, entry.hiddenElement)) return "hidden(hiddenElement)";
                if (ReferenceEquals(inv, entry.afterhourInventory)) return "afterhourInventory";
                if (ReferenceEquals(inv, entry.hirelingInv)) return "hirelingInv";
                if (ReferenceEquals(inv, entry.soldElement)) return "soldElement";
            }
            GameItem owner = inv.GetParentItem();
            if (owner != null)
                return "container(" + (owner.identifier ?? "?") + ")";
            return !string.IsNullOrEmpty(inv.identifier) ? inv.identifier : inv.GetType().Name;
        }
    }

    // Intercepts GameItem.SetSpriteAndShapeFromMod for GunsmithExpansion and GunsmithExpansion2077 so every
    // created or sprite-toggled item receives its compile-time explicit GridShape with full orientation/flip safety.
    [HarmonyPatch(typeof(GameItem), nameof(GameItem.SetSpriteAndShapeFromMod), new[] { typeof(string), typeof(string) })]
    internal static class ItemShapesSetSpritePatch
    {
        private static bool Prefix(GameItem __instance, string modId, string spriteKey, ref GameItem __result)
        {
            if (!ItemShapes.IsManagedMod(modId))
                return true;
            try
            {
                if (ItemShapes.TryHandleSetSpriteAndShapeFromMod(__instance, modId, spriteKey))
                {
                    __result = __instance;
                    return false;
                }
            }
            catch (Exception e)
            {
                GunsmithMod.Warn("ItemShapes patch on " + (spriteKey ?? "?") + ": " + e.Message);
            }
            return true;
        }
    }
}
