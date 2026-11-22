using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using UnityEngine;

namespace GunsmithExpansion
{
    // The retired gunsmith's phone (final-strategy 33 ②, 47; T-013: 300, 340, 344, 354). On his first visit with this mod he leaves his
    // number, 4867 (G U N S on a phone keypad), and from then on it is in the phone book. Until T-013 a call listed his catalog of chips,
    // modules, cabinets, the chip box and four material packs; that catalog went to Lincoln Arms (final-strategy 300; LincolnGoods,
    // LincolnPhone), and a call now offers his three blind boxes (BlindBoxes). Orders paid before the update are still brought (354):
    // their list stays in PlayerStore.modData as before and his courier brings them with the boxes.
    //
    // Deliveries: every night each pending order comes one day closer; when one is due, his courier is queued for the next morning
    // (PlayerStore.QueueFuturClient), and brings every due order in its box (DeliveryBoxes), paid for. With no room for a box anywhere
    // the courier stays at the counter until the player has made some (HandOver). An order he could not hand over because the store
    // did not serve him queues him again the night after.
    //
    // The material packs (T-005; VVittgenstein 2026-11-08 04:30, current/T-005 quote 19) are defined here and sold by Lincoln Arms:
    // gun parts x6 (185), the repair pack (265: metal screws 2, wires 2, scrap metal 4, gun parts 2, rubbing alcohol 2), metal ingots x6
    // (355), high purity metal ingots x8 (600); the prices of the normal difficulty, on the others price x (100 + markup) / 115 rounded.
    // A pack is filled in its own labelled carton before that carton is handed over.
    internal static class Phone
    {
        internal const long Number = 4867;
        internal const string DialogFuncId = "GseRetiredGunsmith";
        internal const string CourierId = "gseRetiredGunsmithDelivery";
        internal const int DeliveryDays = 2;
        // the old catalog's factor on the value, kept by Lincoln Arms for the goods that came from it (final-strategy 350 ①)
        internal const double PriceFactor = 1.3;
        private const string GivenKey = "phone_given";
        private const string OrdersKey = "phone_orders";

        internal static readonly string[] AttachmentChips =
        {
            "printer_chip_makeshift_rds", "printer_chip_makeshift_toy_laser", "printer_chip_rds", "printer_chip_laser_sight",
            "printer_chip_silencer", "printer_chip_internal_springs_improved", "printer_chip_22_mag", "printer_chip_22_mag_ext",
            "printer_chip_22_mag_high", "printer_chip_10_mag", "printer_chip_10_mag_ext", "printer_chip_10_mag_high",
        };

        internal static readonly string[] WeaponChips =
        {
            "printer_chip_pistol", "printer_chip_pistol_double", "printer_chip_small_bottle", "printer_chip_box_cutter",
            "printer_chip_kitchen_knife", "printer_chip_hatchet", "printer_chip_kitchen_cleaver",
        };

        internal static readonly string[] PermitIIWeaponChips =
        {
            "printer_chip_gun_part", "printer_chip_handmade_pistol_frame", "printer_chip_heavy_handmade_pistol_frame",
            "printer_chip_combat_knife", "printer_chip_combat_machete",
        };

        internal static readonly string[] Modules = { "printer_module_metal", "printer_module_cnc", "printer_module_melee_batch" };

        internal const string HighPurity = "INGOT_PURITY_HIGH";

        // A pack of materials: its catalog id, the weapon permit it needs, its price on the normal difficulty and its items
        // (item id, or item id@purity, once per item).
        internal sealed class Pack
        {
            internal string Id;
            internal int Permit;
            internal int NormalPrice;
            internal string[] Items;
        }

        internal static readonly Pack[] MaterialPacks =
        {
            new Pack { Id = "gse_phone_gun_parts", Permit = 1, NormalPrice = 185, Items = Repeat("gun_part", 6) },
            new Pack { Id = "gse_phone_repair_pack", Permit = 1, NormalPrice = 265, Items = Join(Repeat("nuts_metal", 2), Repeat("wire", 2),
                Repeat("scrap_metal", 4), Repeat("gun_part", 2), Repeat("rubbing_alcohol", 2)) },
            new Pack { Id = "gse_phone_ingots", Permit = 2, NormalPrice = 355, Items = Repeat("metal_ingot", 6) },
            new Pack { Id = "gse_phone_high_ingots", Permit = 3, NormalPrice = 600, Items = Repeat("metal_ingot@" + HighPurity, 8) },
        };

        private static string[] Repeat(string id, int count)
        {
            var items = new string[count];
            for (int i = 0; i < count; i++)
                items[i] = id;
            return items;
        }

        private static string[] Join(params string[][] parts)
        {
            var items = new List<string>();
            foreach (string[] part in parts)
                items.AddRange(part);
            return items.ToArray();
        }

        internal static Pack FindPack(string id)
        {
            foreach (Pack p in MaterialPacks)
            {
                if (p.Id == id)
                    return p;
            }
            return null;
        }

        internal static int PackPrice(Pack pack)
        {
            int markup = Difficulty.Rules != null ? Difficulty.Rules.retiredSupplierMarkup : 15;
            return (pack.NormalPrice * (100 + markup) + 57) / 115;
        }

        // "Gun Part ×6"; the repair pack lists what is in it. Names are the game's, in its language.
        internal static string PackName(Pack pack)
        {
            var counts = new List<KeyValuePair<string, int>>();
            foreach (string item in pack.Items)
            {
                int i = counts.FindIndex(c => c.Key == item);
                if (i < 0)
                    counts.Add(new KeyValuePair<string, int>(item, 1));
                else
                    counts[i] = new KeyValuePair<string, int>(item, counts[i].Value + 1);
            }
            var parts = new List<string>();
            foreach (KeyValuePair<string, int> c in counts)
                parts.Add(ItemName(c.Key) + " ×" + c.Value.ToString(CultureInfo.InvariantCulture));
            string list = string.Join(Text.Pick("、", ", "), parts.ToArray());
            return counts.Count > 1 ? string.Format(Text.Pick("修枪材料包（{0}）", "Gun repair pack ({0})"), list) : list;
        }

        internal static string PackTitle(Pack pack)
        {
            switch (pack.Id)
            {
                case "gse_phone_gun_parts": return Text.Pick("枪械零件包", "Gun parts pack");
                case "gse_phone_repair_pack": return Text.Pick("修枪材料包", "Gun repair pack");
                case "gse_phone_ingots": return Text.Pick("金属锭包", "Metal ingot pack");
                case "gse_phone_high_ingots": return Text.Pick("高纯度金属锭包", "High-purity metal ingot pack");
                default: return pack.Id;
            }
        }

        private static string ItemName(string orderId)
        {
            string id = orderId, purity = null;
            SplitOrderId(orderId, out id, out purity);
            GameItem blueprint = DirectoryMaster.Item(id, false);
            string name = blueprint != null ? blueprint.name : id;
            if (blueprint != null)
                blueprint.Destroy();
            return purity == HighPurity ? string.Format(Text.Pick("高纯度{0}", "High purity {0}"), name) : name;
        }

        private static void SplitOrderId(string orderId, out string id, out string purity)
        {
            int at = orderId.IndexOf('@');
            id = at > 0 ? orderId.Substring(0, at) : orderId;
            purity = at > 0 ? orderId.Substring(at + 1) : null;
        }

        internal sealed class Order
        {
            internal string Id;
            internal int Nights;
        }

        internal static void Register()
        {
            PhoneClientList.DialogFuncs[DialogFuncId] = BlindBoxes.Call;
            StoreClientListDict.storeClientDict[CourierId] = CreateCourier;
        }

        internal static void OnGameLoaded()
        {
            if (PlayerStore.HasInstance && Given())
                EnsureContact();
        }

        internal static bool Given()
        {
            return ModHelper.GetModDataBool(GunsmithMod.ModId, GivenKey, false);
        }

        internal static string Name()
        {
            return LocGenderHelper.Resolve(LocHelper.GetLocalizedClientName("name_retired_gunsmith"), (LocGenderHelper.LocGender)1);
        }

        // His greeting on the visit that gives the number gets one more sentence (the dialogue draft's 7-1, first row).
        internal static void AttachIntro(StoreClient client)
        {
            Dialogue d = client != null ? client.mainDialogue : null;
            if (d == null || Given())
                return;
            d.SetText(d.title, d.plainText + " " + LincolnTexts.RetiredNumber.Get());
            d.endAction = (Action)Delegate.Combine(d.endAction, new Action(Give));
        }

        internal static void Give()
        {
            if (PlayerStore.Instance == null)
                return;
            EnsureContact();
            ModHelper.SetModDataBool(GunsmithMod.ModId, GivenKey, true);
            GunsmithMod.Log("retired gunsmith left his number " + Number);
        }

        // A save made with this mod already holds the contact; this adds it when it is missing. The phone book lists
        // a contact in the regular or cooldown state that was dialed before (StorePhoneClient.ShownInPhoneBook); the
        // number he gives counts as dialed.
        internal static void EnsureContact()
        {
            Dictionary<long, StorePhoneClient> book = PlayerStore.Instance.PhoneClientDict;
            if (book == null)
                return;
            StorePhoneClient contact;
            if (!book.TryGetValue(Number, out contact) || contact == null)
            {
                contact = new StorePhoneClient
                {
                    displayName = "Retired Gunsmith",
                    locID = "name_retired_gunsmith",
                    phoneClientType = StorePhoneClient.PhoneClientType.Supplier,
                    phoneState = StorePhoneClient.PhoneState.Regular,
                    dialogFuncId = DialogFuncId,
                    cooldownDuration = 0,
                    alias = new List<string> { "retired_gunsmith" },
                };
                book[Number] = contact;
            }
            contact.dialedBefore = true;
        }

        internal static int Week()
        {
            return Math.Max(0, StoreStation.GetDayCounter() - 1) / 7;
        }

        internal static List<Order> Orders()
        {
            var list = new List<Order>();
            string text = ModHelper.GetModData(GunsmithMod.ModId, OrdersKey, "");
            foreach (string entry in (text ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = entry.Split(',');
                int nights;
                if (parts.Length == 2 && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out nights))
                    list.Add(new Order { Id = parts[0], Nights = nights });
            }
            return RestoreLegacyPacks(list);
        }

        // Before T-020 a material pack was saved as consecutive item entries. Recover only a complete, exact pack
        // sequence with the same remaining nights. Unmatched entries stay as individual legacy items; grouping never
        // changes their quantities or price, although the old format cannot identify original partially delivered packs.
        private static List<Order> RestoreLegacyPacks(List<Order> orders)
        {
            var result = new List<Order>();
            for (int i = 0; i < orders.Count;)
            {
                Pack match = null;
                foreach (Pack pack in MaterialPacks)
                {
                    if (i + pack.Items.Length > orders.Count)
                        continue;
                    bool same = true;
                    for (int p = 0; p < pack.Items.Length; p++)
                        if (orders[i + p].Id != pack.Items[p] || orders[i + p].Nights != orders[i].Nights)
                        {
                            same = false;
                            break;
                        }
                    if (same && (match == null || pack.Items.Length > match.Items.Length))
                        match = pack;
                }
                if (match == null)
                    result.Add(orders[i++]);
                else
                {
                    result.Add(new Order { Id = match.Id, Nights = orders[i].Nights });
                    i += match.Items.Length;
                }
            }
            return result;
        }

        // The pending orders of an item taken out of the mod (SaveRecord.RemoveTakenOut): how many there were.
        internal static int RemoveOrders(string id)
        {
            List<Order> orders = Orders();
            int n = orders.RemoveAll(o => o.Id == id);
            if (n > 0)
                SaveOrders(orders);
            return n;
        }

        internal static int OrderItemCount(Order order)
        {
            Pack pack = FindPack(order.Id);
            return pack != null ? pack.Items.Length : 1;
        }

        private static void SaveOrders(List<Order> orders)
        {
            var entries = new List<string>();
            foreach (Order o in orders)
                entries.Add(o.Id + "," + o.Nights.ToString(CultureInfo.InvariantCulture));
            ModHelper.SetModData(GunsmithMod.ModId, OrdersKey, string.Join(";", entries.ToArray()));
        }

        // Each night (StorePhoneClient.OnEndDay, from PlayerStore.EndNight).
        internal static void OnNight()
        {
            List<Order> orders = Orders();
            if (orders.Count == 0)
                return;
            bool due = false;
            foreach (Order o in orders)
            {
                if (o.Nights > 0)
                    o.Nights--;
                if (o.Nights <= 0)
                    due = true;
            }
            SaveOrders(orders);
            // Once while he is queued: until 0.7 every night with an order due queued him again, so a second courier came the day after with nothing to hand over.
            // 0 days: this runs at the end of the day (PlayerStore.EndNight), when the game has set the MORNING state and adds a day to what it queues
            // (PlayerStore.QueueFuturClient), so he comes the next day as the phone says ("两天后送到"). Until 0.7 it was 1 and he came a day later.
            if (due && PlayerStore.Instance != null && !PlayerStore.Instance.futurStoreClientIdQueue.Contains(CourierId))
            {
                PlayerStore.Instance.QueueFuturClient(CourierId, 0);
                GunsmithMod.Log("retired gunsmith delivery queued for tomorrow");
            }
        }

        // Hands over what is due. Returns how many due orders found no room; elsewhere names the place when a box did not go
        // on the front counter.
        internal static int Deliver(out string elsewhere)
        {
            elsewhere = null;
            List<Order> orders = Orders(), left = new List<Order>();
            var boxes = new Dictionary<string, GameItem>();
            int handed = 0, noRoom = 0;
            foreach (Order o in orders)
            {
                if (o.Nights > 0 || PlayerStore.Instance == null)
                {
                    left.Add(o);
                    continue;
                }
                Pack pack = FindPack(o.Id);
                if (pack != null)
                {
                    if (DeliveryBoxes.HandPack(pack, ref elsewhere))
                        handed += pack.Items.Length;
                    else
                    {
                        left.Add(o);
                        noRoom++;
                    }
                    continue;
                }
                GameItem item = CreateOrderedItem(o.Id);
                if (item == null)
                {
                    left.Add(o);
                    noRoom++;
                    continue;
                }
                if (DeliveryBoxes.Hand(item, boxes, ref elsewhere))
                {
                    handed++;
                }
                else
                {
                    left.Add(o);
                    noRoom++;
                }
            }
            SaveOrders(left);
            GunsmithMod.Log("retired gunsmith delivered " + handed + " ordered items, " + noRoom + " without room, " + (left.Count - noRoom) + " still on order");
            return noRoom;
        }

        internal static GameItem CreateOrderedItem(string orderId)
        {
            string itemId, purity;
            SplitOrderId(orderId, out itemId, out purity);
            GameItem item = DirectoryMaster.Item(itemId, true);
            if (item != null && purity != null)
                IngotPurityHelper.SetPurity(item, purity);
            return item;
        }

        // On his line appearing, so that what he says fits what happened. Without room he asks for some and stays; the choice
        // tries again, as often as it takes. The game does not stop items from being dragged while a choice is shown. With a blind
        // box among the goods he says the lines of the dialogue draft's 7-2; with only orders of the old catalog, the lines of before.
        private static void HandOver(StoreClient courier, Dialogue line)
        {
            bool blind = BlindBoxes.AnyDue();
            string elsewhere, boxElsewhere;
            int boxes;
            int noRoom = Deliver(out elsewhere);
            noRoom += BlindBoxes.Deliver(out boxElsewhere, out boxes);
            if (elsewhere == null)
                elsewhere = boxElsewhere;
            if (line.choices != null)
                line.choices.Clear();
            if (noRoom > 0)
            {
                line.SetText(courier.displayName, blind ? LincolnTexts.BoxNoRoom.Get() : Text.Pick("货到了，可店里哪儿都放不下。你腾个地方，我等着。",
                    "Your order's here, but there's no room for it anywhere. Clear a space, I'll wait."));
                line.SetEndAction(null);
                Dialogue cleared = new Dialogue().SetText("PLAYER", LincolnTexts.Cleared.Get());
                line.AddChoice(DirectoryMaster.Item("$", true), "cleared", "", cleared, () => HandOver(courier, cleared.NextDialogue()));
                return;
            }
            if (blind)
            {
                line.SetText(courier.displayName, elsewhere != null ? LincolnTexts.BoxDeliveredElsewhere.Format(elsewhere) : LincolnTexts.BoxDelivered.Get());
                line.SetEndAction(null);
                line.NextDialogue().SetText(courier.displayName, LincolnTexts.BoxBye.Get()).SetEndAction(LeaveAfterHandOver);
                return;
            }
            line.SetText(courier.displayName, elsewhere != null
                ? string.Format(Text.Pick("你电话里订的货，我送来了。前柜台没地方，放在{0}了。钱已经付过了。",
                    "Here's what you ordered on the phone. The front counter's full, so it's in {0}. It's paid for."), elsewhere)
                : Text.Pick("你电话里订的货，我送来了。钱已经付过了。", "Here's what you ordered on the phone. It's paid for."));
            line.SetEndAction(LeaveAfterHandOver);
        }

        private static void LeaveAfterHandOver()
        {
            if (DialogUIManager.Instance != null)
                DialogUIManager.Instance.CurrentClientLeave(1f);
        }

        // Built like the game's couriers that hand over goods without a sale (StoreClientListSupplier.CreateCourrierChemFree). From
        // T-013 his courier: the lines of 7-2 speak of the retired gunsmith as the old man (the dialogue draft names the speaker 送货员).
        internal static StoreClient CreateCourier()
        {
            var c = new StoreClient();
            c.identifier = CourierId;
            c.displayName = Text.Pick("送货员", "Courier");
            c.spriteName = SpriteDict.GetRandomLowerLevelSpriteName();
            c.clientFaction = "FACTION_LOWER_LEVEL";
            c.isBargainAvailable = false;
            c.clientIntent = (StoreClient.ClientIntent)2;
            c.isRegularSellDisable = true;
            c.mainDialogue.SetText(c.displayName, LincolnTexts.BoxDelivered.Get())
                .SetStartAction(() => HandOver(c, c.mainDialogue))
                .SetEndAction(LeaveAfterHandOver);
            c.allDoneDialogue = new Dialogue().SetText(c.displayName, LincolnTexts.BoxBye.Get())
                .SetEndAction(LeaveAfterHandOver);
            c.CompleteClientCreation(false);
            return c;
        }

        // The game's phone dialogue has 3 choice bubbles; for the paged menus (PhoneMenus) more are copied from the last one when the
        // call starts. A bubble selects the choice of its own index (DialogChoiceBubble.index), and Unity points the
        // copy's click listener at the copy. PhoneChoices measures the available game area,
        // wraps labels and paginates for the selected native bubble scale. Other phone calls
        // regain the game's original three positions and heights.
        internal const int ChoiceBubbles = 8;

        internal static int EnsureChoiceBubbles()
        {
            return PhoneChoices.Ensure();
        }

        // Also retained as the entry point used by the existing phone layout harnesses.
        internal static void ArrangeChoiceBubbles(DialoguePhoneUIManager ui)
        {
            PhoneChoices.Arrange(ui);
        }

        internal static bool IsMenuPage(Dialogue page)
        {
            return PhoneMenus.IsPage(page);
        }

        internal static void PrepareChoicePage(Dialogue page)
        {
            PhoneMenus.Prepare(page);
        }
    }

    [HarmonyPatch(typeof(DialoguePhoneUIManager), nameof(DialoguePhoneUIManager.DisplayChoice))]
    internal static class PhoneChoiceLayoutPatch
    {
        private static void Prefix(DialoguePhoneUIManager __instance)
        {
            if (Phone.IsMenuPage(__instance.currentDialog))
            {
                Phone.EnsureChoiceBubbles();
                Phone.PrepareChoicePage(__instance.currentDialog);
                __instance.HideChoiceBubble();
            }
        }
        private static void Postfix(DialoguePhoneUIManager __instance)
        {
            Phone.ArrangeChoiceBubbles(__instance);
        }
    }

    // The option "Dialogue bubble size" applied while choices are shown (game 1.0.0.12): the game moves its own 3 bubbles from
    // where the first one stands, so the shown ones are stacked again.
    [HarmonyPatch(typeof(EmporiumOption), "ApplyDialogBubbleScale")]
    internal static class PhoneBubbleScalePatch
    {
        private static void Postfix()
        {
            if (DialoguePhoneUIManager.Instance != null)
                PhoneChoices.Refresh(DialoguePhoneUIManager.Instance);
        }
    }

    [HarmonyPatch(typeof(StorePhoneClient), nameof(StorePhoneClient.OnEndDay))]
    internal static class PhoneNightPatch
    {
        private static void Postfix()
        {
            Phone.OnNight();
        }
    }
}
