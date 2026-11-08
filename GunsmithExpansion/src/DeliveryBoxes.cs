using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace GunsmithExpansion
{
    // The packages the retired gunsmith's phone orders come in (VVittgenstein 2026-11-05 13:05, current/008 quote 3 point 7; 13:59,
    // quote 4 point 7; 16:53, quote 5 point 2; 17:46, quote 7; pictures unit U160, U150). Until 0.6.2 each item went
    // onto the front counter by itself and PlayerStore.AddDirectSellingItemToTable destroyed it when the counter had no room,
    // after the order was deleted: the courier left and the player had neither the goods nor the money.
    //   - Chips go in a bulk pack after the Amazon Basics battery box (工包; 2 x 2 cells; its id keeps the word parcel), modules and cabinets in a carton (3 x 2 cells), so a delivery
    //     takes the room of one or two small items. One that is full gives way to another one. The chip box of batch 14 is
    //     the player's own storage now (ChipBox).
    //   - A box goes on the front counter; without room there on the counter top, in the inventory or in the display case,
    //     the order of GraphUtils.EmporiumTryAdd, and the courier says where.
    //   - Without room anywhere the courier waits at the counter until the player has made some (Phone.HandOver). Nothing
    //     is destroyed and no order waits for another day. VVittgenstein turned down leaving it for the next day.
    //   - A box takes nothing from the player; when it is empty it is gone.
    // Workshop mods seen on 2026-11-05 for the same trouble (read from their pages only, none of their code is here):
    // CorpPresence's crates, one 3 x 2 item that only gives and vanishes when empty; Doctor's Module Hotline, whose
    // assistant lets the player clear room and collect; Night Container Auction and Junkyard Delivery, whose goods wait
    // in a container; GM Toolbox and Daily Supply Crates, which try a second place. Agent's decisions: the box sizes, the
    // parcel's inside (chips are 1 x 1 cells), the order of the places.
    // The carton is also what thieves and petty thieves sell their guns in (OfferBoxed; VVittgenstein 2026-11-07 22:35 to 2026-11-08
    // 00:19, current/T-006 quotes 24 to 26). Inside it is 17 x 4 cells, which takes every gun of this mod and of the 2077
    // Add-On (the largest are 17 x 4) and 5 large cabinets of 3 x 4 cells (VVittgenstein 00:19, quote 26;
    // 9 x 8 until then, the Agent's number for 6 large cabinets, which took 56 of the 134 guns).
    internal static class DeliveryBoxes
    {
        internal const string ParcelId = "gse_parcel_chip";
        internal const string CartonId = "gse_box_gun";
        private const int ParcelWidth = 6;
        private const int ParcelHeight = 4;
        internal const int CartonWidth = 17;
        internal const int CartonHeight = 4;
        private const float CheckDelay = 0.5f;

        private static GameInventory filling;
        private static readonly ConditionalWeakTable<GameItem, GameInventory> insides = new ConditionalWeakTable<GameItem, GameInventory>();

        internal static bool Registered(string id)
        {
            return ModHelper.ModFileExists(GunsmithMod.ModId, "Textures/Items/" + id + ".png");
        }

        internal static void Register(ModItemDirectory directory)
        {
            int n = 0;
            foreach (string id in new[] { ParcelId, CartonId })
            {
                string box = id;
                if (!Registered(box))
                    continue;
                directory.Add(box, () => Create(directory, box));
                n++;
            }
            GunsmithMod.Log("registered " + n + " delivery packages");
        }

        private static GameItem Create(ModItemDirectory directory, string id)
        {
            bool chips = id == ParcelId;
            var made = DirectoryUtils.CreateInventoryWindow(chips ? ParcelWidth : CartonWidth, chips ? ParcelHeight : CartonHeight, true);
            PixelWindow window = made.Item1;
            GameInventory inside = made.Item2;
            GameItem box = directory.Empty().SetContentWindow(window).SetSpriteAndShapeFromMod(GunsmithMod.ModId, id);
            box.name = chips ? Text.Pick("芯片工包", "Chip Bulk Pack") : Text.Pick("枪械盒", "Gun Carton");
            // the carton's text covers the deliveries and the thieves' guns (VVittgenstein 2026-11-08 00:19, current/T-006 quote 26); the
            // parcel keeps the delivery text
            box.shortDescription = chips
                ? Text.Pick("退休枪匠送来的货。打开后把东西取走，取空后包装会消失，不能往里放东西。",
                    "Goods from the retired gunsmith. Open it and take them out; it is gone once empty, and nothing can be put in.")
                : Text.Pick("打开可以查看里面的东西，盒子归你之后才能取出。取空后盒子会消失，不能往里放东西。",
                    "Open it to look inside; the contents can be taken out once the carton is yours. It is gone once empty, and nothing can be put in.");
            box.EnableTag("IMPORTANT_TAG", false);
            box.EnableTag("CONTAINER_TAG", false);
            ContainerHelper.AddContainerTooltipTag(box);
            if (chips)
                AudioHelper.InitCartonCard(box);
            else
                AudioHelper.InitCartonBox(box);
            window.SetTitle(box.name);
            window.Validate();
            inside.mayInventoryAddItemFunc = (item, target) => ReferenceEquals(filling, target);
            // no hoppers either (ChipBox.Create; VVittgenstein 2026-11-07 20:16, current/T-006 quote 21)
            inside.EnableTag("NO_HOPPER_INVENTORY_TAG");
            inside.onSlotRemoveItemEndFunc = (Action<GameItem, GameInventory, SlotMarker>)Delegate.Combine(inside.onSlotRemoveItemEndFunc,
                new Action<GameItem, GameInventory, SlotMarker>((item, target, slot) => CheckLater(box, target)));
            // Only with the delay: on loading its contents may not be back yet when this runs.
            box.onLoaded = (Action<GameItem>)Delegate.Combine(box.onLoaded, new Action<GameItem>(b =>
            {
                if (DialogUIManager.Instance != null && DialogUIManager.Instance.isActiveAndEnabled)
                    CheckLater(b, inside);
            }));
            window.openCallback = (Action)Delegate.Combine(window.openCallback, new Action(() => CheckLater(box, inside)));
            insides.Add(box, inside);
            return box;
        }

        // The item leaves the box during or at the end of a drag, and a failed drop brings it back, so the box looks later.
        private static void CheckLater(GameItem box, GameInventory inside)
        {
            MonoBehaviour host = DialogUIManager.Instance;
            if (host != null && host.isActiveAndEnabled)
                host.StartCoroutine(MetaHelper.WaitAndDo(CheckDelay, () => RemoveIfEmpty(box, inside)));
            else
                RemoveIfEmpty(box, inside);
        }

        private static void RemoveIfEmpty(GameItem box, GameInventory inside)
        {
            try
            {
                var element = box as GameItemElement;
                if (box == null || (element != null && element.IsDestroyed()) || inside.childItems.Count > 0 || ReferenceEquals(filling, inside))
                    return;
                if (ItemMouseDragHandler.current != null && ItemMouseDragHandler.current.IsDraggingItem)
                {
                    CheckLater(box, inside);
                    return;
                }
                box.Destroy();
            }
            catch (Exception e)
            {
                GunsmithMod.Warn("empty delivery box: " + e.Message);
            }
        }

        private static string BoxFor(string id)
        {
            return id.StartsWith("printer_chip", StringComparison.Ordinal) || id.StartsWith("gse_chip_", StringComparison.Ordinal) ? ParcelId : CartonId;
        }

        // One ordered item into its box. open keeps the boxes of this delivery. false: no room in the store, the order stays.
        // elsewhere names the place when a new box or bare item went somewhere other than the front counter.
        internal static bool Hand(GameItem item, Dictionary<string, GameItem> open, ref string elsewhere)
        {
            string id = BoxFor(item.identifier);
            if (!Registered(id))
                return HandBare(item, ref elsewhere);
            GameItem box;
            if (open.TryGetValue(id, out box) && Put(box, item))
                return true;
            box = DirectoryMaster.Item(id, true);
            if (box == null)
                return HandBare(item, ref elsewhere);
            if (!Place(box, ref elsewhere))
            {
                box.Destroy();
                item.Destroy();
                return false;
            }
            open[id] = box;
            if (Put(box, item))
                return true;
            GunsmithMod.Warn(item.identifier + " does not fit an empty " + id);
            box.Destroy();
            return HandBare(item, ref elsewhere);
        }

        // Without the box's picture, as before the art is in, the item itself goes by the same rule.
        private static bool HandBare(GameItem item, ref string elsewhere)
        {
            if (Place(item, ref elsewhere))
                return true;
            item.Destroy();
            return false;
        }

        private static bool Put(GameItem box, GameItem item)
        {
            GameInventory inside;
            if (box == null || !insides.TryGetValue(box, out inside))
                return false;
            GeneralHelper.SetItemOwned(item, true);
            filling = inside;
            try
            {
                return Accept(inside, item);
            }
            finally
            {
                filling = null;
            }
        }

        private static bool Accept(GraphNodeStorage place, GameItem item)
        {
            if (place == null || !GraphUtils.CanAccept(place, item))
                return false;
            if (GraphUtils.TryAcceptAll(place, item, -1) > 0)
                return false;
            // merged into a stack there: nothing is left of it (as GraphUtils.TryAddInto)
            if (item.parentInventory == null)
                GraphUtils.DestroyIfUnplaced(item);
            return true;
        }

        private static bool Place(GameItem item, ref string elsewhere)
        {
            EmporiumEntry store = EmporiumEntry.Instance;
            if (store == null)
                return false;
            GeneralHelper.SetItemOwned(item, true);
            if (Accept(store.frontInvinvElement, item))
                return true;
            if (Accept(store.backInvinvElement, item))
                elsewhere = Text.Pick("柜台台面", "the counter top");
            else if (Accept(store.invElement, item))
                elsewhere = Text.Pick("物品栏", "the inventory");
            else if (Accept(store.showcaseElement, item))
                elsewhere = Text.Pick("展示柜", "the display case");
            else
                return false;
            return true;
        }

        // A thief's or petty thief's gun for sale goes on the counter in a carton, one carton for each gun (VVittgenstein 2026-11-07
        // 22:35, current/T-006 quote 24 point 1; 23:57, quote 25; 2026-11-08 00:19, quote 26: this carton).
        //   - The carton and the gun belong to the thief. The player can open the carton by double click and look at the gun
        //     (its tooltip, the magnifier, the X-ray scanner). The game lets nothing out of a container the store does not own
        //     (GameItem.MayRemove: GeneralHelper.IsInsideNonOwnedContainer), and nothing in (GeneralHelper.MayPlayerInsertInto).
        //   - The carton is worth nothing by itself; the game counts the gun in its price (GameItem.GetNegociatedValue adds
        //     the contents), so buying the carton costs about what the gun alone costs.
        //   - Bought, the carton belongs to the store and so does the gun, with the price paid (CartonBoughtPatch); once the gun
        //     is out, the carton is gone (RemoveIfEmpty).
        //   - Agent's decision: while it is for sale, what the game's checks read on the item lying on the counter, the carton
        //     carries for the gun: the stolen mark at the gun's heat, and the gun's contraband level, which the distrusted
        //     factions' withholding, the report and the trading reputation read (PlayerStore.TryAddDirectSellingItemToTable,
        //     StoreClient.IsSellingContraband, StoreReputation.OnItemTraded). The gun keeps its own stolen mark, and the carton
        //     loses both marks once bought (OnBought).
        // Without the carton's picture or room in it, the gun goes out by itself as before.
        internal static bool OfferBoxed(GameItem gun, bool stolen)
        {
            PlayerStore store = PlayerStore.Instance;
            if (gun == null || store == null)
                return false;
            GameItem box = Registered(CartonId) ? DirectoryMaster.Item(CartonId, true) : null;
            if (box == null || !Put(box, gun))
            {
                if (box != null)
                    box.Destroy();
                GunsmithMod.Warn(gun.identifier + " offered without a carton");
                return Sellers.Offer(gun, stolen, 0);
            }
            GeneralHelper.SetItemOwned(gun, false);
            int heat = 0;
            if (stolen && store.isStolenItemIntroduced)
            {
                StolenHelper.InitStolenItem(gun, 0);
                heat = gun.IsTag("STOLEN_TAG") && gun.IsTag("STOLEN_VALUE_INT") ? gun.GetTagReadonly("STOLEN_VALUE_INT").GetInt() : 0;
            }
            if (ContrabandHelper.IsContraband(gun))
                ContrabandHelper.InitContrabandItem(box, ContrabandHelper.GetContrabandLevel(gun));
            return Sellers.Offer(box, heat > 0, heat);
        }

        // PlayerStore.OnItemBought sets only the bought item owned and puts the price on it alone (GeneralHelper.TrackBuyPrice);
        // what is in a bought carton becomes the store's too, with the price, one gun to a carton. The marks the carton carried
        // for the gun come off: the game has counted them in the purchase already (StoreReputation.OnItemTraded, before this
        // postfix), and from here the gun carries its own, which an inspection finds inside the carton
        // (ContrabandHelper.StartInspection) and a permit changes; left on the carton they would count the gun twice.
        internal static void OnBought(GameItem box, int cost)
        {
            GameInventory inside;
            if (box == null || !box.IsID(CartonId) || !insides.TryGetValue(box, out inside))
                return;
            foreach (GameItem item in new List<GameItem>(inside.childItems))
            {
                if (item == null || GeneralHelper.IsItemOwned(item))
                    continue;
                GeneralHelper.SetItemOwned(item, true);
                item.RemoveFeatureRelatedToEvent();
                item.RemoveFeatureTemporaryBuying();
                GeneralHelper.TrackBuyPrice(item, cost);
                GunsmithMod.Log(item.identifier + " bought in a carton for " + cost);
            }
            StolenHelper.RemoveStolenStatus(box);
            ContrabandHelper.RemoveContrabandStatus(box);
        }
    }

    [HarmonyPatch(typeof(PlayerStore), nameof(PlayerStore.OnItemBought))]
    internal static class CartonBoughtPatch
    {
        private static void Postfix(GameItem gameItem, int cost)
        {
            try
            {
                DeliveryBoxes.OnBought(gameItem, cost);
            }
            catch (Exception e)
            {
                GunsmithMod.Warn("bought carton: " + e.Message);
            }
        }
    }
}
