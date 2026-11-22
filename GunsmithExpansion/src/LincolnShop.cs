using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace GunsmithExpansion
{
    // Lincoln Arms at night (final-strategy 331, 332, 343, 350, 355, 359, 360; current/T-013): a place of the night map, open once
    // Yuri Lincoln has made his first visit (LincolnVisits). Its card is gse_lincoln_map_card (296 x 168, PX05, accepted 2026-11-22
    // 04:14); its scene is gse_lincoln_scene_yuri, or gse_lincoln_scene_ava on Yuri's day off (960 x 540, T-036 and PX03).
    //   - The visit is the game's visit to Dr. Jackson (DrJacksonLocation.Arrive and Leave, read 2026-11-22): both counters shown, the
    //     player's things on the counter top put aside until leaving, the front counter widened to 14 x 21 cells, the goods on it not
    //     owned by the player, the one on duty as the client, bought by dragging to the counter top and paying on the game's tablet.
    //   - The front counter is the window onto a longer shelf (331, 355): every good of the day is laid out in rows 14 cells wide,
    //     one item for each piece in stock, a gun wider than the counter turned upright; the mouse wheel over the front counter shows
    //     the rows above or below, a row at a time (the Agent's arrangement 2 of 04:15). The rare guns of the visit come first.
    //   - Prices are the game's price features of the items (LincolnGoods.AddPriceFeatures), rounded up (LincolnPriceRoundingPatch);
    //     paying counts for the favor (342 ①) and takes the piece from the day's stock (350 ②).
    //   - The one on duty speaks (350 ⑥): about 0.4 seconds after the fade, the lines of a first visit at a new level or of the first
    //     night with Ava (2 or 3), otherwise one greeting; one idle line after 22 seconds without a line; a line on putting a good on
    //     the counter top (not twice in 8 seconds) and on taking all of them back (not twice in 12 seconds); on paying the line of
    //     the deal, and 4 seconds later the farewell. Each line stays 4.5 to 6 seconds in the game's client bubble.
    // Agent's arrangements (not confirmed by yzz): the game's scene of Dr. Jackson has his tablet drawn and a click area over it; these
    // scenes have none, so the place's panel has a button with the game's own words for it ("Open the Tablet"); a line under the
    // favor line says which rows are shown; what the player left on the ground comes back to the store as at Dr. Jackson's, which the
    // warning on leaving says.
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
        private const string StatusLabel = "gse_lincoln_status";
        private const string ScrollLabel = "gse_lincoln_rows";
        private const string TabletButton = "gse_lincoln_tablet";

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

        private static DelegateAfterhourLocation location;
        private static CustomUIWindow panel;
        private static bool here;
        private static bool ava;
        private static int level;
        private static readonly List<Slot> slots = new List<Slot>();
        private static readonly List<int> rowTop = new List<int>();
        private static readonly List<int> rowHeight = new List<int>();
        private static readonly Dictionary<GameItem, Slot> shown = new Dictionary<GameItem, Slot>();
        private static int firstRow, lastRow;

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
            location = ModHelper.AddAfterhourLocation(GunsmithMod.ModId, LocationId, () => Lincoln.ShopName(), Arrive, Leave, BuildPanel,
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

        // Before the game shows the scene (MapUIManager.Travel: ShowStage reads BackgroundKey before Arrive).
        private static void OnArriveEarly(string id)
        {
            if (id == LocationId && location != null)
                location.backgroundKey = Key(Lincoln.AvaOnDuty() ? SceneAva : SceneYuri);
        }

        // MapUIManager.OpenLocationPanel sets panelPosition (620, 260) on the right, over the 14 x 21 front counter and tablet;
        // move Lincoln Arms' panel to the left wall below the credit display and size its labels for longer translations.
        private static void OnArriveLate(string id)
        {
            if (id == LocationId && panel != null && panel.IsAlive)
            {
                FitPanel();
                panel.SetPosition(new Vector2(-690f, -80f));
            }
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
                entry.frontInvWindow.Show();
                entry.backInvWindow.Show();
                map.TransferBackInvToCounter();
                entry.ExpandFrontCounterGrid();
                Lay();
                firstRow = 0;
                store.currentClientInstance = StoreClientInstance.CreateClientInstance(CreateDealer());
                StoreClientMono.Instance.image.sprite = SpriteDict.Instance.ShowClientSprite(store.currentClientInstance.GetClientBlueprint().GetSprite());
                StoreUIManager.Instance.HideClient();
                store.isClientArrived = true;
                StoreUIManager.Instance.SetClientPositionToDrJackson();
                ShowRows();
                StoreUIManager.Instance.storeCreditDisplay.creditDisplayText.gameObject.SetActive(true);
                map.SetCreditDisplayPositionToDrJackson();
                pending.Clear();
                greeted = false;
                greetAt = -1;
                hideAt = -1;
                lastLine = Time.time;
                lastComment = lastWithdrawn = lastPaid = -100;
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
                foreach (GameGridInventory inv in new[] { entry.frontInvinvElement, entry.backInvinvElement })
                {
                    foreach (GameItem item in new List<GameItem>(inv.childItems))
                    {
                        if (item == null || !GeneralHelper.IsItemOwned(item))
                            continue;
                        item.CloseContentWindow(true);
                        inv.Expel(item);
                        owned.Add(item);
                    }
                }
                shown.Clear();
                slots.Clear();
                rowTop.Clear();
                rowHeight.Clear();
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
                panel = null;
                GunsmithMod.Log("left Lincoln Arms, " + owned.Count + " things of the store taken back from the counters");
            }
            catch (Exception e)
            {
                GunsmithMod.Warn("leaving Lincoln Arms: " + e);
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
            // as Dr. Jackson (StoreClientList.CreateInventor); he buys nothing of the player's (the Agent's arrangement 3 of 04:15)
            c.clientIntent = (StoreClient.ClientIntent)3;
            c.isRegularSellDisable = true;
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

        private static bool OnFront(GameItem item)
        {
            EmporiumEntry entry = EmporiumEntry.Instance;
            return item != null && entry != null && ReferenceEquals(item.parentInventory, entry.frontInvinvElement);
        }

        // The rows from firstRow that fit the counter: what is on the front counter is taken off and put again, a piece of a row now out
        // of view is destroyed, one now in view is made. A piece on the counter top stays there.
        private static void ShowRows()
        {
            EmporiumEntry entry = EmporiumEntry.Instance;
            if (entry == null)
                return;
            GameGridInventory front = entry.frontInvinvElement;
            int top = rowTop.Count > 0 ? rowTop[Math.Min(firstRow, rowTop.Count - 1)] : 0;
            lastRow = firstRow - 1;
            for (int r = firstRow; r < rowTop.Count && rowTop[r] + rowHeight[r] - top <= Rows; r++)
                lastRow = r;
            var kept = new HashSet<Slot>();
            foreach (Slot s in slots)
            {
                if (s.Item == null || !OnFront(s.Item) || GeneralHelper.IsItemOwned(s.Item))
                    continue;
                front.Expel(s.Item);
                if (s.Row >= firstRow && s.Row <= lastRow)
                    kept.Add(s);
                else
                    Drop(s);
            }
            foreach (Slot s in slots)
            {
                if (s.Sold || s.Row < firstRow || s.Row > lastRow)
                    continue;
                if (s.Item != null && !kept.Contains(s))
                    continue;
                if (s.Item == null)
                    s.Item = Make(s);
                if (s.Item != null && !Put(front, s, rowTop[s.Row] - top))
                    Drop(s);
            }
            RefreshPanel();
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

        private static void Drop(Slot s)
        {
            if (s.Item == null)
                return;
            shown.Remove(s.Item);
            s.Item.Destroy();
            s.Item = null;
        }

        private static bool Put(GameGridInventory front, Slot s, int y)
        {
            GameItem item = s.Item;
            GridShape own = item.modifiedShape ?? item.shape;
            if (own == null)
                return false;
            GridShape at = new GridShapeBuilder(own).SetTransform(s.X, y, false, s.Turned ? 1 : 0).Build();
            if (!front.CheckShapePlacement(item, at, new List<GameItem>()))
                return false;
            var marker = new SlotMarker { item = item, inventory = front, itemGridShape = at, numTransfer = item.unitCount };
            if (marker.TryAcceptOnce(-1) < item.unitCount)
                return false;
            LincolnGoods.AddPriceFeatures(item, s.Good, Lincoln.Discount());
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

        private static void Scroll(int step)
        {
            int next = Math.Max(0, Math.Min(MaxFirstRow(), firstRow + step));
            if (next == firstRow)
                return;
            firstRow = next;
            ShowRows();
        }

        private static bool OverFront()
        {
            EmporiumEntry entry = EmporiumEntry.Instance;
            RectTransform rect = entry != null && entry.frontInvinvElement != null ? entry.frontInvinvElement.rectTransform : null;
            if (rect == null)
                return false;
            Canvas canvas = rect.GetComponentInParent<Canvas>();
            Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            return RectTransformUtility.RectangleContainsScreenPoint(rect, Input.mousePosition, camera);
        }

        private static bool Dragging()
        {
            return ItemMouseDragHandler.current != null && ItemMouseDragHandler.current.IsDraggingItem;
        }

        private static void BuildPanel(CustomUIBuilder builder)
        {
            builder.AddLabel(Lincoln.StatusLine(), StatusLabel);
            builder.AddLabel(RowsText(), ScrollLabel);
            builder.AddButton(LocHelper.GetLocalizedUI("inte_tablet_hover", new object[0]), () =>
            {
                if (NegociationUIManager.Instance != null)
                    NegociationUIManager.Instance.ToggleUI();
            }, TabletButton);
            panel = builder.Handle;
        }

        private static float FitLabel(CustomUIElement el)
        {
            if (el == null || el.text == null)
                return 48f;
            el.text.enableWordWrapping = true;
            float h = Mathf.Ceil(el.text.GetPreferredValues(el.text.text ?? "", 360f, 0f).y) + 6f;
            if (h < 48f)
                h = 48f;
            el.SetPreferredSize(0f, h);
            return h;
        }

        private static void FitPanel()
        {
            if (panel == null || !panel.IsAlive)
                return;
            float h1 = FitLabel(panel.Get(StatusLabel));
            float h2 = FitLabel(panel.Get(ScrollLabel));
            float width = panel.Rect != null && panel.Rect.sizeDelta.x > 0f ? panel.Rect.sizeDelta.x : 400f;
            panel.SetSize(width, Mathf.Max(272f, 164f + h1 + h2));
        }

        private static string RowsText()
        {
            int rows = rowTop.Count;
            if (rows == 0)
                return Text.Pick("今天的货已经卖完了。", "Everything is sold out for today.");
            return string.Format(Text.Pick("在前柜台上滚动鼠标滚轮可以翻看其余货物：第 {0} 至 {1} 排，共 {2} 排。",
                "Scroll the mouse wheel over the front counter to see the other goods: rows {0} to {1} of {2}."),
                firstRow + 1, Math.Max(firstRow + 1, lastRow + 1), rows);
        }

        private static void RefreshPanel()
        {
            try
            {
                if (panel == null || !panel.IsAlive)
                    return;
                CustomUIElement status = panel.Get(StatusLabel);
                if (status != null)
                    status.SetText(Lincoln.StatusLine());
                CustomUIElement rows = panel.Get(ScrollLabel);
                if (rows != null)
                    rows.SetText(RowsText());
                FitPanel();
            }
            catch (Exception e)
            {
                GunsmithMod.Warn("Lincoln Arms panel: " + e.Message);
            }
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
            if (item == null || !shown.TryGetValue(item, out s) || GeneralHelper.IsItemOwned(item))
                return;
            LincolnGoods.AddPriceFeatures(item, s.Good, Lincoln.Discount());
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
                if (entry != null && s.Row >= firstRow && s.Row <= lastRow)
                {
                    s.Item = Make(s);
                    if (s.Item != null && !Put(entry.frontInvinvElement, s, rowTop[s.Row] - rowTop[firstRow]))
                        Drop(s);
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
            RefreshPanel();
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
                float wheel = Input.mouseScrollDelta.y;
                if (wheel != 0 && !Dragging() && OverFront())
                    Scroll(wheel > 0 ? -1 : 1);
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
}
