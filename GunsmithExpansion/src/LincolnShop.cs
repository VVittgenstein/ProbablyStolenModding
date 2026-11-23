using System;
using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GunsmithExpansion
{
    // Lincoln Arms night visit: W1 item ownership, stock, pooled shelf objects and player-owned shelf rows are retained.
    // W5 uses the native upgrade-merchant return panel and negotiation tablet. Pagination is attached above the shelf.
    // Reference: final-strategy 331, 342, 350 and 366; T-041 W5.
    internal static class LincolnShop
    {
        internal const string LocationId = "gse_lincoln_arms";
        internal const string DealerId = "gseLincolnDealer";
        internal const string MapCard = "gse_lincoln_map_card";
        internal const string SceneYuri = "gse_lincoln_scene_yuri";
        internal const string SceneAva = "gse_lincoln_scene_ava";
        private const int Columns = 14;
        private const int Rows = 21;
        private const float FirstLineDelay = 0.4f;
        private const float IdleSeconds = 22f;
        private const float CommentPause = 8f;
        private const float WithdrawnPause = 12f;
        private const float ByeDelay = 4f;
        private sealed class Slot
        {
            internal LincolnGoods.Good Good;
            internal ItemFeatureList.EquipmentCondition Condition;
            internal int X, Row, W, H;
            internal bool Turned;
            internal GameItem Item;
            internal int Base;
            internal bool Sold;
        }

        private sealed class OwnedSlot
        {
            internal GameItem Item;
            internal int X, AbsY, GlobalHeight, Orientation;
            internal bool Flipped;
        }

        private static DelegateAfterhourLocation location;
        private static bool here;
        private static bool ava;
        private static int level;
        private static readonly List<Slot> slots = new List<Slot>();
        private static readonly List<int> rowTop = new List<int>();
        private static readonly List<int> rowHeight = new List<int>();
        private static readonly Dictionary<GameItem, Slot> shown = new Dictionary<GameItem, Slot>();
        private static readonly List<OwnedSlot> ownedOnShelf = new List<OwnedSlot>();
        private static int firstRow, lastRow, shownTop;

        private static readonly List<KeyValuePair<float, string>> pending = new List<KeyValuePair<float, string>>();
        private static bool greeted;
        private static float greetAt = -1, lastLine, hideAt = -1, lastComment = -100, lastWithdrawn = -100, lastPaid = -100;
        private static bool tickWarned;
        private static readonly System.Random rng = new System.Random();

        private static string Key(string picture)
        {
            return GunsmithMod.ModId + ":" + picture;
        }

        internal static bool Here()
        {
            return here;
        }

        internal static void Register()
        {
            if (location != null)
                return;
            location = ModHelper.AddAfterhourLocation(GunsmithMod.ModId, LocationId, () => Lincoln.ShopName(), Arrive, Leave, null,
                Key(MapCard), Key(SceneYuri), null, () => Lincoln.Introduced(), () => Lincoln.Introduced());
            if (location == null)
            {
                GunsmithMod.Warn("Lincoln Arms not added to the night map");
                return;
            }
            location.showsStoreCounters = true;
            location.usesAfterhourBag = true;
            // Dr. Jackson's places of the two counters (DrJacksonLocation.GetFrontCounterPosition and GetBackCounterPosition)
            location.frontCounterPosition = (w, h) => new Vector2((w / 100) * 78.5f, -((h / 100) * 10));
            location.backCounterPosition = (w, h) => new Vector2((w / 2) - (w / 100) * 18f, -(h / 100) * 55f);
            location.onTick = Tick;
            location.leaveWarning = () => Text.Pick("林肯军火行会把你留在地上的东西送回你的店里。确定要离开吗？",
                "Lincoln Arms will bring anything you left on the ground back to your store. Are you sure you want to leave?");
            ModHook.OnAfterhourArriveEarly -= OnArriveEarly;
            ModHook.OnAfterhourArriveEarly += OnArriveEarly;
            ModHook.OnAfterhourArriveLate -= OnArriveLate;
            ModHook.OnAfterhourArriveLate += OnArriveLate;
            GunsmithMod.Log("Lincoln Arms added to the night map");
        }

        internal static void Unregister()
        {
            ModHook.OnAfterhourArriveEarly -= OnArriveEarly;
            ModHook.OnAfterhourArriveLate -= OnArriveLate;
        }

        // The native panel reference is set before MapUIManager.OpenLocationPanel evaluates PanelObject.
        private static void OnArriveEarly(string id)
        {
            if (id != LocationId || location == null)
                return;
            location.backgroundKey = Key(Lincoln.AvaOnDuty() ? SceneAva : SceneYuri);
            MapUIManager map = MapUIManager.Instance;
            if (map != null)
                location.panelObject = map.UIUpgradeMerchantPanel;
        }

        private static void OnArriveLate(string id)
        {
            if (id != LocationId || !here)
                return;
            RefreshShelfHeader();
            LincolnVisitUI.Tick();
        }

        private static void Arrive()
        {
            try
            {
                PlayerStore store = PlayerStore.Instance;
                EmporiumEntry entry = EmporiumEntry.Instance;
                MapUIManager map = MapUIManager.Instance;
                if (store == null || entry == null || map == null)
                    return;
                here = true;
                ava = Lincoln.AvaOnDuty();
                level = Lincoln.Level();
                store.playerLocation = (PlayerStore.PlayerLocation)1;
                LincolnShelfHeader.Capture(entry.frontInvWindow);
                entry.frontInvWindow.Show();
                entry.backInvWindow.Show();
                map.TransferBackInvToCounter();
                entry.ExpandFrontCounterGrid();
                LincolnShelfHeader.Attach(PageUp, PageDown);
                ownedOnShelf.Clear();
                shownTop = 0;
                Lay();
                firstRow = 0;
                store.currentClientInstance = StoreClientInstance.CreateClientInstance(CreateDealer());
                StoreClientMono.Instance.image.sprite = SpriteDict.Instance.ShowClientSprite(store.currentClientInstance.GetClientBlueprint().GetSprite());
                StoreUIManager.Instance.HideClient();
                store.isClientArrived = true;
                StoreUIManager.Instance.SetClientPositionToDrJackson();
                LincolnVisitUI.Begin();
                ShowRows();
                pending.Clear();
                greeted = false;
                greetAt = -1;
                hideAt = -1;
                lastLine = Time.time;
                lastComment = lastWithdrawn = lastPaid = -100;
                tickWarned = false;
                GunsmithMod.Log("at Lincoln Arms: " + (ava ? "Ava" : "Yuri") + " on duty, favor " + Lincoln.PointsText() + " (Lv." + level + "), "
                    + slots.Count + " pieces in " + rowTop.Count + " rows");
            }
            catch (Exception e)
            {
                GunsmithMod.Warn("arriving at Lincoln Arms: " + e);
            }
        }

        private static void Leave()
        {
            try
            {
                here = false;
                pending.Clear();
                HideLine();
                MapUIManager map = MapUIManager.Instance;
                EmporiumEntry entry = EmporiumEntry.Instance;
                if (NegociationUIManager.Instance != null)
                    NegociationUIManager.Instance.CloseUI();
                if (entry == null || map == null)
                    return;
                var owned = new List<GameItem>();
                var ownedSet = new HashSet<GameItem>();
                foreach (GameGridInventory inv in new[] { entry.frontInvinvElement, entry.backInvinvElement })
                {
                    if (inv == null)
                        continue;
                    foreach (GameItem item in new List<GameItem>(inv.childItems))
                    {
                        if (IsDestroyedItem(item) || !GeneralHelper.IsItemOwned(item))
                            continue;
                        SetItemVisible(item, true);
                        item.CloseContentWindow(true);
                        inv.Expel(item);
                        if (ownedSet.Add(item))
                            owned.Add(item);
                    }
                }
                // Rescue any player-owned items that were placed on the shelf and scrolled out of view.
                foreach (OwnedSlot o in ownedOnShelf)
                {
                    GameItem item = o != null ? o.Item : null;
                    if (IsDestroyedItem(item) || !GeneralHelper.IsItemOwned(item))
                        continue;
                    if (item.parentInventory != null && !ReferenceEquals(item.parentInventory, entry.frontInvinvElement))
                        continue;
                    SetItemVisible(item, true);
                    item.CloseContentWindow(true);
                    if (item.parentInventory != null)
                        item.parentInventory.Expel(item);
                    if (ownedSet.Add(item))
                        owned.Add(item);
                }
                ownedOnShelf.Clear();
                // Destroy all cached unbought shop items (including those parked off-screen) so none leak under tempRoot.
                foreach (Slot s in slots)
                {
                    GameItem item = s != null ? s.Item : null;
                    if (IsDestroyedItem(item) || GeneralHelper.IsItemOwned(item))
                        continue;
                    item.Destroy();
                    s.Item = null;
                }
                shown.Clear();
                slots.Clear();
                rowTop.Clear();
                rowHeight.Clear();
                shownTop = 0;
                entry.ResetFrontCounterGrid();
                entry.ClearFrontInv();
                entry.ClearBackInv(true);
                map.TransferCounterToBackInv();
                foreach (GameItem item in owned)
                    GraphUtils.EmporiumTryAdd(entry.backInvWindow, item);
                if (entry.afterhourInventory != null)
                {
                    foreach (GameItem item in new List<GameItem>(entry.afterhourInventory.childItems))
                        GraphUtils.EmporiumTryAdd(entry.backInvWindow, item);
                }
                map.ResetCreditDisplayPosition();
                map.DeactivateClientOutsideStore();
                GunsmithMod.Log("left Lincoln Arms, " + owned.Count + " things of the store taken back from the counters");
            }
            catch (Exception e)
            {
                GunsmithMod.Warn("leaving Lincoln Arms: " + e);
            }
            finally
            {
                RestoreVisitUI();
            }
        }

        private static void RestoreVisitUI()
        {
            try
            {
                LincolnShelfHeader.Restore();
            }
            catch (Exception e)
            {
                GunsmithMod.Warn("Lincoln shelf header cleanup: " + e.Message);
            }
            try
            {
                LincolnVisitUI.Restore();
            }
            catch (Exception e)
            {
                GunsmithMod.Warn("Lincoln visit UI cleanup: " + e.Message);
            }
            if (location != null)
            {
                if (location.panelObject != null)
                    location.panelObject.SetActive(false);
                location.panelObject = null;
            }
        }

        private static StoreClient CreateDealer()
        {
            var c = new StoreClient();
            c.identifier = DealerId;
            c.displayName = ava ? Lincoln.DaughterName() : Lincoln.DealerName();
            c.spriteName = Lincoln.OnDutyPortrait();
            c.clientFaction = "FACTION_LOWER_LEVEL";
            c.isBargainAvailable = false;
            c.nonShootable = true;
            // ClientIntent.SELL (2): the dealer sells to the player and never buys any player-owned item.
            // Keep isRegularSellDisable false (default) so PlayerStore.CanBuyItemFromTable and NegociationUIManager allow buying.
            c.clientIntent = StoreClient.ClientIntent.SELL;
            c.mainDialogue.SetText(c.displayName, Pick(Greetings()).Get());
            c.onItemAddedToClient = OnClientCounter;
            c.onItemAddedToWeighted = OnWeighted;
            c.onItemRemovedToWeighted = OnUnweighted;
            c.CompleteClientCreation(false);
            return c;
        }

        // Every piece of the day, rare guns first, then the regular goods in their order (LincolnGoods.All), the taller of a kind first so
        // that the rows waste less; then the rows.
        private static void Lay()
        {
            shown.Clear();
            slots.Clear();
            rowTop.Clear();
            rowHeight.Clear();
            int permit = GunSupply.PermitTop();
            var pieces = new List<Slot>();
            foreach (LincolnGoods.Good g in LincolnGoods.RollRare(level, permit))
                AddPieces(pieces, g, 1);
            var regular = LincolnGoods.Unlocked(level, permit);
            var order = new Dictionary<LincolnGoods.Good, int>();
            for (int i = 0; i < regular.Count; i++)
                order[regular[i]] = i;
            regular.Sort((a, b) =>
            {
                if (a.Kind != b.Kind || a.Tier != b.Tier)
                    return order[a].CompareTo(order[b]);
                int[] ca = LincolnGoods.Cells(a), cb = LincolnGoods.Cells(b);
                bool ta = ca != null && ca[0] > Columns, tb = cb != null && cb[0] > Columns;
                if (ta != tb)
                    return ta.CompareTo(tb);
                int ha = ca != null ? (ta ? ca[0] : ca[1]) : 0, hb = cb != null ? (tb ? cb[0] : cb[1]) : 0;
                return ha != hb ? hb.CompareTo(ha) : order[a].CompareTo(order[b]);
            });
            foreach (LincolnGoods.Good g in regular)
            {
                int left = LincolnGoods.Left(g);
                AddPieces(pieces, g, left == int.MaxValue ? 1 : left);
            }
            int x = 0, top = 0, row = -1;
            bool rowTurned = false;
            foreach (Slot s in pieces)
            {
                int[] cells = LincolnGoods.Cells(s.Good);
                if (cells == null)
                    continue;
                int w = cells[0], h = cells[1];
                if (w > Columns)
                {
                    s.Turned = true;
                    w = cells[1];
                    h = cells[0];
                }
                if (w > Columns || h > Rows)
                    continue;
                if (row < 0 || x + w > Columns || s.Turned != rowTurned)
                {
                    if (row >= 0)
                        top += rowHeight[row];
                    row++;
                    rowTop.Add(top);
                    rowHeight.Add(0);
                    x = 0;
                    rowTurned = s.Turned;
                }
                s.X = x;
                s.Row = row;
                s.W = w;
                s.H = h;
                x += w;
                rowHeight[row] = Math.Max(rowHeight[row], h);
                slots.Add(s);
            }
        }

        private static void AddPieces(List<Slot> pieces, LincolnGoods.Good g, int count)
        {
            for (int i = 0; i < count; i++)
            {
                ItemFeatureList.EquipmentCondition condition = LincolnGoods.IsGun(g) ? LincolnGoods.RollCondition(g, level) : ItemFeatureList.EquipmentCondition.Good;
                pieces.Add(new Slot { Good = g, Condition = condition });
            }
        }

        private static bool IsDestroyedItem(GameItem item)
        {
            if (item == null)
                return true;
            GameItemElement el = item as GameItemElement;
            return el != null && el.IsDestroyed();
        }

        private static void SetItemVisible(GameItem item, bool visible)
        {
            PixelElement pe = item as PixelElement;
            if (pe != null && pe.handler != null && pe.handler.gameObject != null)
                pe.handler.gameObject.SetActive(visible);
        }

        private static bool OnFront(GameItem item)
        {
            EmporiumEntry entry = EmporiumEntry.Instance;
            return item != null && entry != null && ReferenceEquals(item.parentInventory, entry.frontInvinvElement);
        }

        private static void ParkItem(GameGridInventory front, GameItem item)
        {
            if (IsDestroyedItem(item))
                return;
            item.CloseContentWindow(true);
            if (ReferenceEquals(item.parentInventory, front))
                front.Expel(item);
            SetItemVisible(item, false);
        }

        private static void MoveOnFront(GameItem item, int x, int y, bool flipped, int orientation)
        {
            GridShape cur = item.modifiedShape ?? item.shape;
            if (cur == null)
                return;
            if (cur.minX == x && cur.minY == y && cur.flipped == flipped && cur.orientation == orientation)
                return;
            item.modifiedShape = new GridShapeBuilder(cur).SetTransform(x, y, flipped, orientation).Build();
            GameItemElement el = item as GameItemElement;
            if (el != null)
                el.UpdateRectTransform();
        }

        private static bool IsAtSlot(GameItem item, Slot s, int y)
        {
            GridShape cur = item.modifiedShape ?? item.shape;
            return cur != null && cur.minX == s.X && cur.minY == y && !cur.flipped && cur.orientation == (s.Turned ? 1 : 0);
        }

        private static void SyncOwnedOnShelf(GameGridInventory front)
        {
            for (int i = ownedOnShelf.Count - 1; i >= 0; i--)
            {
                OwnedSlot o = ownedOnShelf[i];
                GameItem item = o != null ? o.Item : null;
                if (IsDestroyedItem(item) || !GeneralHelper.IsItemOwned(item)
                    || (item.parentInventory != null && !ReferenceEquals(item.parentInventory, front)))
                    ownedOnShelf.RemoveAt(i);
            }
            foreach (GameItem item in new List<GameItem>(front.childItems))
            {
                if (IsDestroyedItem(item) || !GeneralHelper.IsItemOwned(item))
                    continue;
                GridShape shape = item.modifiedShape ?? item.shape;
                if (shape == null)
                    continue;
                OwnedSlot record = null;
                for (int i = 0; i < ownedOnShelf.Count; i++)
                {
                    if (ReferenceEquals(ownedOnShelf[i].Item, item))
                    {
                        record = ownedOnShelf[i];
                        break;
                    }
                }
                if (record == null)
                {
                    record = new OwnedSlot { Item = item };
                    ownedOnShelf.Add(record);
                }
                record.X = shape.minX;
                record.AbsY = shownTop + shape.minY;
                record.GlobalHeight = Math.Max(1, shape.globalHeight);
                record.Flipped = shape.flipped;
                record.Orientation = shape.orientation;
            }
        }

        // Shows the rows from firstRow that fit the 14 x 21 counter:
        //   - Items staying in view shift in-place (updating modifiedShape + RectTransform) without expelling or churning price features.
        //   - Shop items leaving view are expelled and hidden (cached until Leave) instead of destroyed and re-instantiated.
        //   - Player-owned items placed back on the shelf scroll with their global row and are parked off-screen when out of view.
        private static void ShowRows()
        {
            EmporiumEntry entry = EmporiumEntry.Instance;
            if (entry == null || entry.frontInvinvElement == null)
                return;
            GameGridInventory front = entry.frontInvinvElement;
            int newTop = rowTop.Count > 0 ? rowTop[Math.Min(firstRow, rowTop.Count - 1)] : 0;
            lastRow = firstRow - 1;
            for (int r = firstRow; r < rowTop.Count && rowTop[r] + rowHeight[r] - newTop <= Rows; r++)
                lastRow = r;

            SyncOwnedOnShelf(front);

            // 1. Park items leaving view (and any unowned shop item displaced from its assigned slot).
            foreach (Slot s in slots)
            {
                if (IsDestroyedItem(s.Item) || GeneralHelper.IsItemOwned(s.Item) || !OnFront(s.Item))
                    continue;
                if (s.Row < firstRow || s.Row > lastRow || !IsAtSlot(s.Item, s, rowTop[s.Row] - shownTop))
                    ParkItem(front, s.Item);
            }
            foreach (OwnedSlot o in ownedOnShelf)
            {
                int newY = o.AbsY - newTop;
                if (newY < 0 || newY + o.GlobalHeight > Rows)
                    ParkItem(front, o.Item);
            }

            // 2. Shift staying items in-place to their new screen rows (no Expel / no feature rebuild).
            foreach (Slot s in slots)
            {
                if (s.Sold || s.Row < firstRow || s.Row > lastRow || IsDestroyedItem(s.Item) || GeneralHelper.IsItemOwned(s.Item) || !OnFront(s.Item))
                    continue;
                MoveOnFront(s.Item, s.X, rowTop[s.Row] - newTop, false, s.Turned ? 1 : 0);
            }
            foreach (OwnedSlot o in ownedOnShelf)
            {
                int newY = o.AbsY - newTop;
                if (OnFront(o.Item) && newY >= 0 && newY + o.GlobalHeight <= Rows)
                    MoveOnFront(o.Item, o.X, newY, o.Flipped, o.Orientation);
            }

            // 3. Re-enter parked player-owned shelf items whose global rows are now in view (before merchant items).
            foreach (OwnedSlot o in ownedOnShelf)
            {
                int newY = o.AbsY - newTop;
                if (OnFront(o.Item) || newY < 0 || newY + o.GlobalHeight > Rows)
                    continue;
                SetItemVisible(o.Item, true);
                if (!PutItem(front, o.Item, o.X, newY, o.Flipped, o.Orientation))
                    SetItemVisible(o.Item, false);
            }

            // 4. Enter newly visible shop slots (reusing cached off-screen items or creating once).
            foreach (Slot s in slots)
            {
                if (s.Sold || s.Row < firstRow || s.Row > lastRow)
                    continue;
                if (s.Item != null && IsDestroyedItem(s.Item))
                {
                    shown.Remove(s.Item);
                    s.Item = null;
                }
                if (s.Item != null && OnFront(s.Item))
                    continue;
                // A piece currently placed on the counter top stays there.
                if (s.Item != null && s.Item.parentInventory != null)
                    continue;
                if (s.Item == null)
                    s.Item = Make(s);
                else
                    SetItemVisible(s.Item, true);
                if (s.Item != null && !PutSlot(front, s, rowTop[s.Row] - newTop))
                    SetItemVisible(s.Item, false);
            }

            shownTop = newTop;
            RefreshShelfHeader();
        }

        private static GameItem Make(Slot s)
        {
            GameItem item = LincolnGoods.Make(s.Good, s.Condition);
            if (item == null)
                return null;
            LincolnGoods.AddPriceFeatures(item, s.Good, Lincoln.Discount());
            if (s.Base <= 0)
                s.Base = Math.Max(1, item.unitCount);
            shown[item] = s;
            return item;
        }

        private static bool PutItem(GameGridInventory front, GameItem item, int x, int y, bool flipped, int orientation)
        {
            GridShape own = item.modifiedShape ?? item.shape;
            if (own == null)
                return false;
            GridShape at = new GridShapeBuilder(own).SetTransform(x, y, flipped, orientation).Build();
            if (!front.CheckShapePlacement(item, at, new List<GameItem>()))
                return false;
            var marker = new SlotMarker { item = item, inventory = front, itemGridShape = at, numTransfer = item.unitCount };
            if (marker.TryAcceptOnce(-1) < item.unitCount)
                return false;
            GameItemElement el = item as GameItemElement;
            if (el != null)
                el.UpdateRectTransform();
            return true;
        }

        private static bool PutSlot(GameGridInventory front, Slot s, int y)
        {
            if (!PutItem(front, s.Item, s.X, y, false, s.Turned ? 1 : 0))
                return false;
            LincolnGoods.AddPriceFeatures(s.Item, s.Good, Lincoln.Discount());
            return true;
        }

        private static int MaxFirstRow()
        {
            int total = 0;
            for (int r = rowTop.Count - 1; r >= 0; r--)
            {
                if (total + rowHeight[r] > Rows)
                    return r + 1;
                total += rowHeight[r];
            }
            return 0;
        }

        private static void ScrollToRow(int target)
        {
            int next = Math.Max(0, Math.Min(MaxFirstRow(), target));
            if (next == firstRow)
                return;
            firstRow = next;
            ShowRows();
        }

        private static void Scroll(int step)
        {
            ScrollToRow(firstRow + step);
        }

        private static void PageDown()
        {
            if (!here || Dragging() || rowTop.Count == 0)
                return;
            int step = Math.Max(1, lastRow - firstRow + 1);
            ScrollToRow(firstRow + step);
        }

        private static void PageUp()
        {
            if (!here || Dragging() || firstRow <= 0 || rowTop.Count == 0)
                return;
            int sum = 0;
            int target = firstRow - 1;
            for (int r = firstRow - 1; r >= 0; r--)
            {
                if (sum + rowHeight[r] > Rows && r < firstRow - 1)
                    break;
                sum += rowHeight[r];
                target = r;
            }
            ScrollToRow(target);
        }

        private static bool OverRect(RectTransform rect)
        {
            if (rect == null)
                return false;
            Canvas canvas = rect.GetComponentInParent<Canvas>();
            Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            return RectTransformUtility.RectangleContainsScreenPoint(rect, Input.mousePosition, camera);
        }

        private static bool OverFront()
        {
            EmporiumEntry entry = EmporiumEntry.Instance;
            return entry != null && entry.frontInvinvElement != null && OverRect(entry.frontInvinvElement.rectTransform);
        }

        private static bool OverTablet()
        {
            NegociationUIManager nego = NegociationUIManager.Instance;
            if (nego == null || !nego.IsOpen)
                return false;
            if (CustomUIManager.Instance != null)
            {
                CustomUIWindow win = CustomUIManager.Instance.GetWindow(NegociationUIManager.WINDOW_ID);
                if (win != null && win.IsAlive && win.IsVisible && OverRect(win.Rect))
                    return true;
            }
            if (nego.uiObject != null && nego.uiObject.activeInHierarchy)
            {
                RectTransform rt = nego.uiObject.GetComponent<RectTransform>();
                if (OverRect(rt))
                    return true;
            }
            return false;
        }

        private static bool Dragging()
        {
            return ItemMouseDragHandler.current != null && ItemMouseDragHandler.current.IsDraggingItem;
        }

        private static bool TypingInInput()
        {
            EventSystem es = EventSystem.current;
            GameObject selected = es != null ? es.currentSelectedGameObject : null;
            if (selected == null)
                return false;
            TMP_InputField tmp = selected.GetComponent<TMP_InputField>();
            if (tmp != null && tmp.isFocused)
                return true;
            InputField legacy = selected.GetComponent<InputField>();
            return legacy != null && legacy.isFocused;
        }

        private static string RowsText()
        {
            int rows = rowTop.Count;
            int first = rows > 0 ? firstRow + 1 : 0;
            int last = rows > 0 ? Math.Max(first, lastRow + 1) : 0;
            return string.Format(Text.Pick("货架 ({0}-{1} / {2}排)", "Shelf ({0}-{1} / {2} rows)"), first, last, rows);
        }

        private static void RefreshShelfHeader()
        {
            if (!here)
                return;
            LincolnShelfHeader.Refresh(RowsText(), firstRow > 0, firstRow < MaxFirstRow());
        }

        // Placing an item on the front counter clears temporary client features before the client's callback runs
        // (GameItem.RemoveTemporaryClientFeature), so a shop good placed or returned there gets its shop price features back.
        private static void OnClientCounter(GameItem item)
        {
            Slot s;
            if (item != null && shown.TryGetValue(item, out s) && !GeneralHelper.IsItemOwned(item))
                LincolnGoods.AddPriceFeatures(item, s.Good, Lincoln.Discount());
        }

        // The dealer's client: a good put on the counter top gets its price features again (the game may take the client's features off
        // an item that leaves it) and a line.
        private static void OnWeighted(GameItem item)
        {
            Slot s;
            if (!here || item == null || !shown.TryGetValue(item, out s) || GeneralHelper.IsItemOwned(item))
                return;
            LincolnGoods.AddPriceFeatures(item, s.Good, Lincoln.Discount());
            // Opening the tablet is independent of the eight-second dialogue cooldown.
            NegociationUIManager nego = NegociationUIManager.Instance;
            if (nego != null)
            {
                if (nego.IsOpen)
                    nego.RefreshUI();
                else
                    nego.OpenUI();
            }
            float now = Time.time;
            if (now - lastComment < CommentPause)
                return;
            lastComment = now;
            LincolnTexts.CounterLines lines = Counter();
            Say((s.Good.Kind == LincolnGoods.Kind.RareGun ? lines.RareGun : LincolnGoods.IsRegularGun(s.Good) ? lines.Gun : lines.Part).Get());
        }

        private static void OnUnweighted(GameItem item)
        {
            EmporiumEntry entry = EmporiumEntry.Instance;
            if (!here || item == null || entry == null || !shown.ContainsKey(item) || GeneralHelper.IsItemOwned(item))
                return;
            foreach (GameItem other in entry.backInvinvElement.childItems)
            {
                if (other != null && !ReferenceEquals(other, item) && shown.ContainsKey(other) && !GeneralHelper.IsItemOwned(other))
                    return;
            }
            float now = Time.time;
            if (now - lastWithdrawn < WithdrawnPause || now - lastPaid < 1f)
                return;
            lastWithdrawn = now;
            Say(Counter().Withdrawn.Get());
        }

        // PlayerStore.OnItemBought, while the player is here.
        internal static void OnBought(GameItem item, int cost)
        {
            Slot s;
            if (!here || item == null || !shown.TryGetValue(item, out s))
                return;
            shown.Remove(item);
            s.Item = null;
            foreach (string id in new[] { LincolnGoods.MarkupFeature, LincolnGoods.CatalogFeature, LincolnGoods.DiscountFeature })
            {
                if (item.IsAlreadyContainFeatureWithId(id))
                    item.RemoveItemFeatureByID(id);
            }
            int units = Math.Max(1, (item.unitCount + s.Base - 1) / Math.Max(1, s.Base));
            LincolnGoods.Take(s.Good, units);
            Lincoln.AddSpent(cost, "Lincoln Arms: " + s.Good.Key);
            if (LincolnGoods.PerDay(s.Good) <= 0)
            {
                // without a daily limit (the material packs): another one in its place
                EmporiumEntry entry = EmporiumEntry.Instance;
                if (entry != null && entry.frontInvinvElement != null && s.Row >= firstRow && s.Row <= lastRow)
                {
                    s.Item = Make(s);
                    if (s.Item != null && !PutSlot(entry.frontInvinvElement, s, rowTop[s.Row] - shownTop))
                        SetItemVisible(s.Item, false);
                }
            }
            else
                s.Sold = true;
            float now = Time.time;
            lastPaid = now;
            LincolnTexts.CounterLines lines = Counter();
            pending.Clear();
            Say(lines.Paid.Get());
            pending.Add(new KeyValuePair<float, string>(now + ByeDelay, lines.Bye.Get()));
            RefreshShelfHeader();
            GunsmithMod.Log("bought at Lincoln Arms: " + s.Good.Key + " for " + cost);
        }

        private static void Tick()
        {
            if (!here)
                return;
            try
            {
                float now = Time.time;
                MapUIManager map = MapUIManager.Instance;
                LincolnShelfHeader.Tick();
                LincolnVisitUI.Tick();
                if (!greeted)
                {
                    if (map != null && map.IsInTransit)
                        return;
                    if (greetAt < 0)
                        greetAt = now + FirstLineDelay;
                    else if (now >= greetAt)
                    {
                        greeted = true;
                        Greet();
                    }
                }
                if (!Dragging())
                {
                    float wheel = Input.mouseScrollDelta.y;
                    if (wheel != 0 && (OverFront() || LincolnShelfHeader.Over()) && !OverTablet())
                        Scroll(wheel > 0 ? -1 : 1);
                    else if (!TypingInInput())
                    {
                        if (Input.GetKeyDown(KeyCode.PageUp))
                            PageUp();
                        else if (Input.GetKeyDown(KeyCode.PageDown))
                            PageDown();
                    }
                }
                if (pending.Count > 0 && now >= pending[0].Key)
                {
                    string text = pending[0].Value;
                    pending.RemoveAt(0);
                    Say(text);
                }
                else if (hideAt > 0 && now >= hideAt)
                    HideLine();
                if (greeted && pending.Count == 0 && now - lastLine >= IdleSeconds)
                    Say(Pick(Idles()).Get());
            }
            catch (Exception e)
            {
                if (!tickWarned)
                {
                    tickWarned = true;
                    GunsmithMod.Warn("Lincoln Arms tick: " + e);
                }
            }
        }

        private static void Greet()
        {
            int seen = Lincoln.Greeted(ava);
            var lines = new List<LincolnTexts.Line>();
            if (ava)
            {
                if (seen < 0)
                {
                    bool met = Lincoln.AvaMet();
                    lines.AddRange(level <= 1 ? (met ? LincolnTexts.AvaFirstNightMetLow : LincolnTexts.AvaFirstNightNeverMetLow)
                        : (met ? LincolnTexts.AvaFirstNightMetHigh : LincolnTexts.AvaFirstNightNeverMetHigh));
                }
                else if (seen < level && level >= 1)
                    lines.Add(LincolnTexts.AvaLevelVisit[level]);
                Lincoln.SetAvaMet();
            }
            else if (seen < level)
                lines.AddRange(LincolnTexts.YuriLevelVisit[level]);
            if (lines.Count == 0)
                lines.Add(Pick(Greetings()));
            if (seen < level)
                Lincoln.SetGreeted(ava, level);
            SayAll(lines);
        }

        private static LincolnTexts.Line[] Greetings()
        {
            return ava ? LincolnTexts.AvaGreeting[level] : LincolnTexts.YuriGreeting[level];
        }

        private static LincolnTexts.Line[] Idles()
        {
            return ava ? LincolnTexts.AvaIdle[level] : LincolnTexts.YuriIdle[level];
        }

        private static LincolnTexts.CounterLines Counter()
        {
            return ava ? LincolnTexts.AvaCounter[level] : LincolnTexts.YuriCounter[level];
        }

        private static LincolnTexts.Line Pick(LincolnTexts.Line[] lines)
        {
            return lines[rng.Next(lines.Length)];
        }

        private static float LineSeconds()
        {
            return 4.5f + (float)rng.NextDouble() * 1.5f;
        }

        // The first line now, each next one when the one before has stayed its time.
        private static void SayAll(List<LincolnTexts.Line> lines)
        {
            pending.Clear();
            float at = Time.time;
            for (int i = 0; i < lines.Count; i++)
            {
                if (i == 0)
                    at += Say(lines[i].Get());
                else
                {
                    pending.Add(new KeyValuePair<float, string>(at, lines[i].Get()));
                    at += LineSeconds();
                }
            }
        }

        private static float Say(string text)
        {
            DialogUIManager ui = DialogUIManager.Instance;
            if (ui == null || PlayerStore.Instance == null || PlayerStore.Instance.currentClientInstance == null || string.IsNullOrEmpty(text))
                return 0;
            ui.PlayClientSingleText(text);
            LincolnVisitUI.AfterSay();
            float seconds = LineSeconds();
            lastLine = Time.time;
            hideAt = lastLine + seconds;
            return seconds;
        }

        private static void HideLine()
        {
            hideAt = -1;
            DialogUIManager ui = DialogUIManager.Instance;
            if (ui == null)
                return;
            if (ui.clientBubble != null)
                ui.clientBubble.gameObject.SetActive(false);
            if (ui.clientNameBubble != null)
                ui.clientNameBubble.SetActive(false);
        }
    }

    [HarmonyPatch(typeof(PlayerStore), nameof(PlayerStore.OnItemBought))]
    internal static class LincolnShopBoughtPatch
    {
        private static void Postfix(GameItem gameItem, int cost)
        {
            try
            {
                LincolnShop.OnBought(gameItem, cost);
            }
            catch (Exception e)
            {
                GunsmithMod.Warn("bought at Lincoln Arms: " + e.Message);
            }
        }
    }

    // With ClientIntent.SELL, StoreClient.OnDealAccepted triggers vanilla post-deal / all-done leave dialogues that would overwrite
    // Lincoln Arms' own paid and farewell lines or dismiss the dealer when a page is emptied; skip it while at Lincoln Arms.
    [HarmonyPatch(typeof(StoreClient), nameof(StoreClient.OnDealAccepted))]
    internal static class LincolnShopDealAcceptedPatch
    {
        private static bool Prefix(StoreClient __instance)
        {
            return !(LincolnShop.Here() && __instance != null && __instance.identifier == LincolnShop.DealerId);
        }
    }
}
