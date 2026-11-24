using System;
using System.Collections.Generic;
using UnityEngine;

namespace GunsmithExpansion
{
    internal static partial class CounterCapacity
    {
        internal const int CounterWidth = 20, CounterHeight = 7;
        private const int VanillaBackWindowHeightPx = 68;
        private static GameGridInventory back, reserve;
        private static int baseW, baseH;
        private static bool expanded, loading, changing, pending, mountingPreview;
        private static bool lastEnabled = true;
        private static int lastProbe = -1;
        private static GameGridInventory visitFront;
        private static int visitFrontW, visitFrontH;

        private static AfterhourLocation Location
        {
            get { return MapUIManager.Instance == null ? null : MapUIManager.Instance.Current; }
        }

        private static bool InStore()
        {
            return Location == null && PlayerStore.Instance != null
                && PlayerStore.Instance.playerLocation == PlayerStore.PlayerLocation.IN_STORE;
        }

        private static bool NightCounterActive()
        {
            AfterhourLocation loc = Location;
            if (loc == null) return false;
            if (loc.ShowsStoreCounters) return true;
            int w, h;
            return CanvasSize(out w, out h) && loc.GetBackCounterPosition(w, h).HasValue;
        }

        private static bool CounterActive() { return InStore() || NightCounterActive(); }

        internal static bool ManagePosition { get { return Enabled || expanded; } }

        private static bool Bind(EmporiumEntry entry)
        {
            if (entry == null || entry.backInvinvElement == null || entry.backInvinvElementCounter == null)
                return false;
            if (back != entry.backInvinvElement || reserve != entry.backInvinvElementCounter)
            {
                if (back != null) back.onSlotRemoveItemEndFunc -= Removed;
                if (reserve != null) reserve.onSlotRemoveItemEndFunc -= Removed;
                back = entry.backInvinvElement;
                reserve = entry.backInvinvElementCounter;
                back.onSlotRemoveItemEndFunc += Removed;
                reserve.onSlotRemoveItemEndFunc += Removed;
                expanded = loading = changing = pending = mountingPreview = false;
                visitFront = null;
                lastProbe = -1;
            }
            if (!expanded && !changing)
            {
                GridShape shape = back.inventoryShape;
                if (shape == null || shape.width <= 0 || shape.height <= 0)
                    return false;
                baseW = Math.Min(shape.width, CounterWidth);
                baseH = Math.Min(shape.height, CounterHeight);
            }
            return true;
        }

        private static bool IsBack(GameInventory grid)
        {
            EmporiumEntry e = EmporiumEntry.Instance;
            return grid != null && e != null && (grid == e.backInvinvElement || grid == e.backInvinvElementCounter);
        }

        internal static bool RequiresExpandedCounter(GridShape shape)
        {
            return shape != null && !((shape.width <= baseW && shape.height <= baseH)
                || (shape.height <= baseW && shape.width <= baseH));
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

        internal static bool CanvasSize(out int w, out int h)
        {
            w = h = 0;
            RenderHandler render = RenderHandler.current;
            if (render == null || render.windowUICanvasObject == null)
                return false;
            RectTransform canvas = render.windowUICanvasObject.GetComponent<RectTransform>();
            if (canvas == null)
                return false;
            Vector2 size = canvas.sizeDelta;
            w = (int)size.x;
            h = (int)size.y;
            return w > 0 && h > 0;
        }

        internal static Vector2 ComputeStoreFrontPosition(EmporiumEntry e, int w, int h)
        {
            PixelWindow front = e.frontInvWindow;
            return new Vector2(w / 2 - front.widthPixels / 2, Math.Min(0f, -(h / 100 * 52.2f) + front.heightPixels));
        }

        internal static Vector2 FrontPosition(EmporiumEntry e, int w, int h)
        {
            AfterhourLocation loc = Location;
            GameGridInventory grid = e.frontInvinvElement;
            PixelWindow window = e.frontInvWindow;
            if (grid != null && (window.widthPixels != grid.widthPixels + 4 || window.heightPixels != grid.heightPixels + 4))
                window.Validate();
            return (loc == null ? null : loc.GetFrontCounterPosition(w, h)) ?? ComputeStoreFrontPosition(e, w, h);
        }

        internal static Vector2 ComputeStoreBackPosition(EmporiumEntry e, int w, int h)
        {
            PixelWindow win = e.backInvWindow;
            // One extra column cannot be divided into two whole cells. Leave the odd column on the right.
            int residual = expanded ? ((CounterWidth - baseW) % 2) * 8 : 0;
            float vanillaBottom = -(h / 100 * 52.2f) - VanillaBackWindowHeightPx;
            return new Vector2(w / 2 - win.widthPixels / 2 + residual, Math.Min(0f, vanillaBottom + win.heightPixels));
        }

        private static Vector2 ComputeVanillaStoreBackPosition(EmporiumEntry e, int w, int h)
        {
            return new Vector2(w / 2 - e.backInvWindow.widthPixels / 2, -(h / 100) * 52.2f);
        }

        private static Vector2 ExpandedNightPosition(EmporiumEntry e, AfterhourLocation loc, Vector2? basePos, int w, int h)
        {
            Vector2? front = loc.GetFrontCounterPosition(w, h);
            float frontX = front.HasValue ? front.Value.x : w / 100 * 78.5f;
            int width = e.backInvWindow.widthPixels;
            float maxX = frontX - 8f - width;
            int panelHeight = e.afterhourWindow != null && e.afterhourWindow.heightPixels > 0 ? e.afterhourWindow.heightPixels : 173;
            float y = Math.Min(0f, panelHeight - h + 4f + e.backInvWindow.heightPixels);
            if (LincolnShop.Here())
            {
                float minX = w / 960f * 368.5f + 5f;
                return new Vector2(minX <= maxX ? (minX + maxX) * 0.5f : Math.Max(0f, maxX), y);
            }

            float baseX = basePos.HasValue ? basePos.Value.x : w / 2 - (w / 100) * 18f;
            int baseWidth = baseW * 16 + 4;
            int residual = ((CounterWidth - baseW) % 2) * 8;
            float x = baseX - (width - baseWidth) / 2 + residual;
            return new Vector2(Mathf.Clamp(x, 0f, Math.Max(0f, maxX)), y);
        }

        internal static Vector2 BackPosition(EmporiumEntry e, int w, int h)
        {
            AfterhourLocation loc = Location;
            bool ready = Bind(e);
            if (ready && (e.backInvWindow.widthPixels != back.widthPixels + 4 || e.backInvWindow.heightPixels != back.heightPixels + 4))
                e.backInvWindow.Validate();
            if (loc != null)
            {
                Vector2? basePos = loc.GetBackCounterPosition(w, h);
                if (expanded && (loc.ShowsStoreCounters || basePos.HasValue))
                    return ExpandedNightPosition(e, loc, basePos, w, h);
                return basePos ?? ComputeVanillaStoreBackPosition(e, w, h);
            }
            return ComputeStoreBackPosition(e, w, h);
        }

        private static void RefreshBack(EmporiumEntry e)
        {
            PixelWindow win = e.backInvWindow;
            if (win == null) return;
            win.Validate();
            int w, h;
            if (win.rectTransform != null && CanvasSize(out w, out h))
                win.rectTransform.anchoredPosition = e.GetBackInvPosition(w, h);
        }

        internal static void RememberFront(EmporiumEntry e)
        {
            GameGridInventory front = e.frontInvinvElement;
            if (front == null || front.inventoryShape == null || visitFront == front) return;
            visitFront = front;
            visitFrontW = front.inventoryShape.width;
            visitFrontH = front.inventoryShape.height;
        }

        internal static void RestoreVisitFront(EmporiumEntry e)
        {
            GameGridInventory front = e.frontInvinvElement;
            if (visitFront != null && visitFront == front && (visitFrontW != 18 || visitFrontH != 4)
                && front.inventoryShape.width == 18 && front.inventoryShape.height == 4)
                front.SetShape(visitFrontW, visitFrontH);
            visitFront = null;
            if (e.frontInvWindow != null) e.frontInvWindow.Validate();
        }

        // Both target layouts are checked before changing either inventory. No graph edges or slot callbacks change.
        private sealed class GridChange
        {
            internal readonly GameGridInventory Grid;
            internal readonly int OldWidth, OldHeight;
            internal readonly List<GameItem> Items;
            internal readonly GridShape[] Old, Next;
            private readonly int[] xOrigin, yOrigin;
            private readonly TagSystem[] states;

            internal GridChange(GameGridInventory grid, int w, int h, bool grow, bool recover)
            {
                Grid = grid;
                OldWidth = grid.inventoryShape.width;
                OldHeight = grid.inventoryShape.height;
                Items = grid.childItems;
                Old = new GridShape[Items.Count];
                xOrigin = new int[Items.Count];
                yOrigin = new int[Items.Count];
                states = new TagSystem[Items.Count];
                for (int i = 0; i < Items.Count; i++)
                {
                    GameItem item = Items[i];
                    if (item == null || item.modifiedShape == null || (!grow && RequiresExpandedCounter(item.modifiedShape)))
                        return;
                    Old[i] = item.modifiedShape;
                    xOrigin[i] = item.modifiedXOrigin;
                    yOrigin[i] = item.modifiedYOrigin;
                    states[i] = item.modifiedState;
                }
                int dx = (w - OldWidth) / 2, dy = h - OldHeight;
                if (recover)
                    Next = loading ? CounterPacking.Recover(Old, w, h, -dx, -dy) : CounterPacking.Repack(Old, w, h, -dx, -dy);
                else
                    Next = CounterPacking.Translate(Old, w, h, dx, dy);
                // UncheckedAccept can keep a large item's saved coordinates in a base-sized inventory.
                if (grow && Next == null) Next = CounterPacking.Recover(Old, w, h, -dx, -dy);
            }

            internal void Size(int w, int h)
            {
                if (Grid.inventoryShape.width != w || Grid.inventoryShape.height != h)
                    Grid.SetShape(w, h);
            }

            internal void Place()
            {
                for (int i = 0; i < Items.Count; i++)
                    Items[i].modifiedShape = Next[i];
                for (int i = 0; i < Items.Count; i++)
                {
                    if (ReferenceEquals(Next[i], Old[i])) continue;
                    Items[i].ValidateShapeState();
                    GridShape s = Items[i].modifiedShape, plan = Next[i];
                    if (!ShapesLocalEqual(s, plan) || s.minX != plan.minX || s.minY != plan.minY
                        || s.orientation != plan.orientation || s.flipped != plan.flipped
                        || Items[i].modifiedXOrigin != xOrigin[i] || Items[i].modifiedYOrigin != yOrigin[i])
                        throw new InvalidOperationException("Item shape changed during counter resize: " + Items[i].identifier);
                }
            }

            internal void Restore()
            {
                for (int i = 0; i < Items.Count; i++)
                {
                    Items[i].modifiedShape = Old[i];
                    Items[i].modifiedXOrigin = xOrigin[i];
                    Items[i].modifiedYOrigin = yOrigin[i];
                    Items[i].modifiedState = states[i];
                }
                Size(OldWidth, OldHeight);
                Grid.Validate();
            }
        }

        private static bool Resize(bool grow, bool recover)
        {
            if (changing) return false;
            int w = grow ? CounterWidth : baseW, h = grow ? CounterHeight : baseH;
            if (back.inventoryShape.width == w && back.inventoryShape.height == h
                && reserve.inventoryShape.width == w && reserve.inventoryShape.height == h)
            {
                expanded = grow && (w != baseW || h != baseH);
                return true;
            }
            var live = new GridChange(back, w, h, grow, recover);
            if (live.Next == null) return false;
            var saved = new GridChange(reserve, w, h, grow, recover);
            if (saved.Next == null) return false;
            bool wasExpanded = expanded;
            changing = true;
            try
            {
                if (grow) { live.Size(w, h); saved.Size(w, h); }
                live.Place();
                saved.Place();
                if (!grow) { live.Size(w, h); saved.Size(w, h); }
                expanded = grow && (baseW != CounterWidth || baseH != CounterHeight);
                RefreshBack(EmporiumEntry.Instance);
                return true;
            }
            catch (Exception e)
            {
                expanded = wasExpanded;
                live.Restore();
                saved.Restore();
                RefreshBack(EmporiumEntry.Instance);
                GunsmithMod.Warn("Counter resize rolled back: " + e.Message);
                return false;
            }
            finally { changing = false; }
        }

        internal static void BeginLoad()
        {
            if (!Bind(EmporiumEntry.Instance)) return;
            loading = true;
            mountingPreview = false;
            Resize(true, false);
        }

        internal static void FinishLoad()
        {
            try { Synchronize(loading); }
            finally { loading = false; }
        }

        internal static void EndIncompleteLoad()
        {
            if (loading) FinishLoad();
        }

        internal static void PrepareTransfer()
        {
            if (loading || changing || !Bind(EmporiumEntry.Instance)) return;
            if (!Enabled)
            {
                if (expanded) Resize(false, true);
                return;
            }
            if (ContainsLarge(back) || ContainsLarge(reserve))
            {
                if (!expanded) Resize(true, false);
                return;
            }
            if (expanded && !(pending && RecentProbe()) && !HeldLarge() && !(mountingPreview && HoldingItem()))
                Resize(false, true);
        }

        internal static void Preflight(GameGridInventory grid, GameItem item, ref GridShape candidate, bool gridCoordinates)
        {
            if (!Enabled || changing || loading || !IsBack(grid) || !Bind(EmporiumEntry.Instance)) return;
            if (!RequiresExpandedCounter(candidate) && !RequiresExpandedCounter(item == null ? null : item.modifiedShape)) return;
            if (!CounterActive()) return;
            pending = true;
            lastProbe = Time.frameCount;
            if (candidate != null && item != null && item.parentInventory == grid && RequiresExpandedCounter(candidate))
                mountingPreview = HoldingItem();
            if (expanded) return;
            GridShape before = gridCoordinates && candidate != null && item != null && item.parentInventory == grid ? item.modifiedShape : null;
            if (Resize(true, false) && before != null)
                candidate = CounterPacking.Move(candidate, item.modifiedShape.minX - before.minX, item.modifiedShape.minY - before.minY);
        }

        private static bool HeldLarge()
        {
            ItemMouseDragHandler mouse = ItemMouseDragHandler.current;
            if (mouse != null && mouse.IsDraggingItem && LargeItem(mouse.GetCurrentItem())) return true;
            ItemSelectHandler select = ItemSelectHandler.current;
            if (select != null && LargeItem(select.IsEquipped ? select.GetEquippedItem() : ModReflect.Get<GameItem>(select, "currentItem")))
                return true;
            ItemMultiSelectHandler multi = ItemMultiSelectHandler.current;
            if (multi == null || !multi.IsGroupDragging) return false;
            var group = ModReflect.Get<List<GameItemElement>>(multi, "selectedItems");
            if (group != null)
                for (int i = 0; i < group.Count; i++)
                    if (LargeItem(group[i])) return true;
            return false;
        }

        private static bool LargeItem(GameItem item)
        {
            return item != null && RequiresExpandedCounter(item.modifiedShape);
        }

        private static bool ContainsLarge(GameGridInventory grid)
        {
            foreach (GameItem item in grid.childItems)
                if (LargeItem(item)) return true;
            return false;
        }

        private static bool HoldingItem()
        {
            ItemMouseDragHandler mouse = ItemMouseDragHandler.current;
            ItemMultiSelectHandler group = ItemMultiSelectHandler.current;
            ItemSelectHandler select = ItemSelectHandler.current;
            return (mouse != null && mouse.IsDraggingItem) || (group != null && group.IsGroupDragging)
                || (select != null && (select.IsEquipped || ModReflect.Get<GameItem>(select, "currentItem") != null));
        }

        internal static void Synchronize(bool recover)
        {
            if ((loading && !recover) || changing || !Bind(EmporiumEntry.Instance)) return;
            pending = false;
            if (recover || !Enabled)
            {
                mountingPreview = false;
                if (expanded) Resize(false, true);
                return;
            }
            if (!CounterActive()) return;
            mountingPreview = mountingPreview && HoldingItem();
            if (mountingPreview || HeldLarge() || ContainsLarge(back) || ContainsLarge(reserve))
            {
                if (!expanded) Resize(true, false);
            }
            else if (expanded)
                // Keep W9's live store positions. Night trade can put ordinary goods in extension cells.
                Resize(false, Location != null);
        }

        internal static void SynchronizeAndRefresh()
        {
            EmporiumEntry e = EmporiumEntry.Instance;
            if (loading || changing || !Bind(e)) return;
            bool wasExpanded = expanded;
            Synchronize(false);
            if (expanded == wasExpanded) RefreshBack(e);
        }

        // Expel invokes the remove callback before the destination accepts the SlotMarker.
        // Defer shrinking until the synchronous transfer and its callbacks have returned.
        private static void Removed(GameItem item, GameInventory inv, SlotMarker slot) { Schedule(inv); }

        internal static void Schedule(GameInventory inv)
        {
            if (!changing && expanded && IsBack(inv)) pending = true;
        }

        private static bool RecentProbe()
        {
            int frame = Time.frameCount;
            return lastProbe >= 0 && (lastProbe == frame || lastProbe == frame - 1);
        }

        internal static void Tick()
        {
            bool enabled = Enabled;
            if (lastEnabled != enabled)
            {
                lastEnabled = enabled;
                SynchronizeAndRefresh();
                return;
            }
            if (pending && !RecentProbe()) Synchronize(false);
        }

        internal static void PreferBottom(GameGridInventory grid, GameItem item, bool middle, bool keepOrientation, ref SlotMarker slot)
        {
            // The original method has already tried nested inventories and stack merges. Keep those results.
            if (!Enabled || !expanded || loading || changing || !IsBack(grid) || item == null || LargeItem(item)
                || (slot != null && (slot.targetItem != null || slot.inventory != grid)) || !CounterActive()) return;
            GridShape s = item.modifiedShape;
            if (s == null) return;
            int available = item.MaxNumRemove();
            if (available < 1) return;
            var probe = new GridShapeBuilder(s);
            int dx = (CounterWidth - baseW) / 2, dy = CounterHeight - baseH;
            // Prefer the entire stone surface, including its side margins, before using any extension cell.
            for (int pass = 0; pass < 2; pass++)
            {
                int left = pass == 0 ? dx : 0, top = pass == 0 ? dy : 0;
                int width = pass == 0 ? baseW : CounterWidth, height = pass == 0 ? baseH : CounterHeight;
                for (int bottom = top + height - 1; bottom >= top; bottom--)
                    for (int flip = 0; flip < (keepOrientation ? 1 : 2); flip++)
                        for (int rot = 0; rot < (keepOrientation ? 1 : 4); rot++)
                        {
                            probe.SetTransform(0, 0, keepOrientation ? s.flipped : flip != 0,
                                keepOrientation ? s.orientation : (4 - rot) % 4);
                            int y = bottom - probe.globalHeight + 1, last = width - probe.globalWidth;
                            if (y < top || last < 0) continue;
                            for (int step = 0; step <= last; step++)
                            {
                                int x = middle ? last / 2 + (step % 2 == 0 ? -step / 2 : (step + 1) / 2) : step;
                                probe.SetPosition(left + x, y);
                                SlotMarker candidate = grid.TryInventorySlot(item, available, probe, null);
                                if (candidate == null || candidate.numTransfer <= 0) continue;
                                slot = candidate;
                                return;
                            }
                        }
            }
        }
    }
}
