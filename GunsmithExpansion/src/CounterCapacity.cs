using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace GunsmithExpansion
{
    internal static partial class CounterCapacity
    {
        private const string SettingsSection = "Counter";
        private const string SettingKey = "auto_expand_counter";
        private static ConfigEntry<bool> expandEntry;

        internal static bool Enabled { get { return expandEntry == null || expandEntry.Value; } }

        internal static void DefineSettings()
        {
            ModConfig config = ModHelper.GetConfig(GunsmithMod.ModId);
            if (config == null) return;
            expandEntry = config.Define(SettingKey, true, SettingsSection,
                () => Text.Pick("自动扩展后柜台", "Automatically expand the back counter"),
                () => Text.Pick("在店内和夜访时，后柜台会为超大物品临时扩大。移走大件后，剩余物品能放回原区域时，柜台会缩回。",
                    "In the shop and during night visits, the back counter expands for oversized items. Once they are removed, it shrinks if the remaining items fit in the original area."));
            config.Save();
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

    [HarmonyPatch(typeof(ModHook), "FireOnGameLoadedLate")]
    internal static class CounterVanillaGunLoadAlignPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix()
        {
            try
            {
                CounterCapacity.AlignVanillaGunsAfterLoad();
            }
            catch (Exception e)
            {
                GunsmithMod.Warn("Counter load alignment: " + e.Message);
            }
            finally { CounterCapacity.FinishLoad(); }
        }
    }
}
