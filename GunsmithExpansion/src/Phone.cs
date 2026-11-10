using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using UnityEngine;

namespace GunsmithExpansion
{
    // Phone orders from the retired gunsmith (final-strategy 33 ②, 47). On his first visit with this mod he leaves his
    // number, 4867 (G U N S on a phone keypad), and from then on it is in the phone book. A call lists the catalog for
    // the weapon permit held; an order is paid in full at once, at the item's value × 1.3 and then the game's retired
    // supplier markup of the difficulty (DifficultyRules.retiredSupplierMarkup: 0, 15, 30 or 45%), and he brings it
    // 2 days later. At most 3 items are ordered a week (days 1 to 7, 8 to 14 and so on). The catalog grows with the
    // permit (final-strategy 45): the game's retired gunsmith's printer chips that he stocks regardless of the permit
    // and this mod's tier one printer chips at first; with weapon permit II also the printing modules, the chips he
    // stocks more often with a higher permit and this mod's tier two chips; with permit III this mod's tier three
    // chips (the division of the game's items is the Agent's). The contact is a StorePhoneClient in
    // PlayerStore.PhoneClientDict and goes into the save with the game's own contacts; pending orders and the week's
    // count are kept in PlayerStore.modData.
    //
    // Deliveries: every night each pending order comes one day closer; when one is due, his visit is queued for the
    // next morning (PlayerStore.QueueFuturClient), and he brings every due order in its box (DeliveryBoxes), paid for.
    // With no room for a box anywhere he stays at the counter until the player has made some (HandOver). An order he
    // could not hand over because the store did not serve him queues him again the night after.
    //
    // Materials (T-005; VVittgenstein 2026-11-08 04:30, current/T-005 quote 19): the catalog
    // also has four packs, which are not counted in the 3 a week. Gun parts x6 (185) and the repair pack (265: metal screws 2, wires 2,
    // scrap metal 4, gun parts 2, rubbing alcohol 2) from the start, metal ingots x6 (355) with weapon permit II, high purity metal ingots x8
    // (600) with permit III. The prices are those of the normal difficulty; Agent's decision: on the others they take the game's retired
    // supplier markup as the retired gunsmith's other goods do, price x (100 + markup) / 115 rounded. A pack remains one saved order
    // and is filled in its own labelled carton before that carton is handed over. An order without room stays with the courier.
    internal static class Phone
    {
        internal const long Number = 4867;
        internal const string DialogFuncId = "GseRetiredGunsmith";
        internal const string CourierId = "gseRetiredGunsmithDelivery";
        internal const int DeliveryDays = 2;
        internal const int WeeklyLimit = 3;
        internal const double PriceFactor = 1.3;
        private const string GivenKey = "phone_given";
        private const string OrdersKey = "phone_orders";
        private const string WeekKey = "phone_week";
        private const string WeekCountKey = "phone_week_count";

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

        internal sealed class Menu
        {
            internal string Label;
            internal readonly List<Menu> Children = new List<Menu>();
            internal readonly List<string> Items = new List<string>();
        }

        internal sealed class Order
        {
            internal string Id;
            internal int Nights;
        }

        internal static void Register()
        {
            PhoneClientList.DialogFuncs[DialogFuncId] = Call;
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
            return LocHelper.GetLocalizedClientName("name_retired_gunsmith");
        }

        // His greeting on the visit that gives the number gets one more sentence.
        internal static void AttachIntro(StoreClient client)
        {
            Dialogue d = client != null ? client.mainDialogue : null;
            if (d == null || Given())
                return;
            d.SetText(d.title, d.plainText + " " + string.Format(Text.Pick(
                "以后缺芯片、模组或材料，打 {0} 找我。先付钱，两天后送到。",
                "If you need chips, modules or materials, call me at {0}. Pay up front and I'll bring them two days later."), Number));
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

        private static Menu Group(string label, IEnumerable<string> items)
        {
            var menu = new Menu { Label = label };
            menu.Items.AddRange(items);
            return menu;
        }

        internal static Menu BuildCatalog(int permit)
        {
            var root = new Menu();
            var materials = new List<string>();
            foreach (Pack p in MaterialPacks)
            {
                if (Math.Max(1, permit) >= p.Permit)
                    materials.Add(p.Id);
            }
            root.Children.Add(Group(Text.Pick("材料", "Materials"), materials));
            if (permit >= 2)
                root.Children.Add(Group(Text.Pick("打印机模组", "Printer modules"), Modules));
            root.Children.Add(Group(Text.Pick("配件与弹匣的3D打印机芯片", "Attachment and magazine 3D printer chips"), AttachmentChips));
            var weapons = new List<string>(WeaponChips);
            if (permit >= 2)
                weapons.AddRange(PermitIIWeaponChips);
            root.Children.Add(Group(Text.Pick("简易枪械、刀具与零件的3D打印机芯片", "Makeshift gun, blade and part 3D printer chips"), weapons));
            var cabinets = new List<string>();
            foreach (string id in new[] { Cabinets.SmallId, Cabinets.LargeId })
            {
                if (Cabinets.Registered(id))
                    cabinets.Add(id);
            }
            if (ChipBox.Registered())
                cabinets.Add(ChipBox.Id);
            if (cabinets.Count > 0)
                root.Children.Add(Group(Text.Pick("枪柜与芯片盒", "Gun cabinets and chip boxes"), cabinets));
            string[] tiers = { "一档", "二档", "三档" };
            for (int t = 0; t < tiers.Length && t < Math.Max(1, permit); t++)
            {
                var tier = new Menu { Label = string.Format(Text.Pick("{0}枪械的3D打印机芯片", "{0} gun 3D printer chips"), Text.TierName(tiers[t])) };
                var classes = new Dictionary<string, Menu>();
                foreach (GunDef d in GunsmithExpansion.Catalog.Guns)
                {
                    if (d.Tier != tiers[t] || !Production.HasChip(d))
                        continue;
                    Menu cls;
                    if (!classes.TryGetValue(d.ClassName, out cls))
                    {
                        cls = new Menu { Label = Text.ClassName(d.ClassName) };
                        classes[d.ClassName] = cls;
                        tier.Children.Add(cls);
                    }
                    cls.Items.Add(Production.ChipId(d));
                }
                root.Children.Add(tier);
            }
            return root;
        }

        // Every item of the catalog for the permit held, in menu order.
        internal static List<string> Catalog()
        {
            var items = new List<string>();
            Collect(BuildCatalog(GunSupply.PermitTop()), items);
            return items;
        }

        private static void Collect(Menu menu, List<string> items)
        {
            items.AddRange(menu.Items);
            foreach (Menu child in menu.Children)
                Collect(child, items);
        }

        // The item made to read the value is destroyed again: from game 1.0.0.12 a shown choice is checked again every
        // 0.2 seconds (DialogChoiceBubble.Update), so a page's prices are worked out once when it is built (ShowMenu,
        // CanAfford), and an item left behind on each check piled up for as long as the page stayed open.
        internal static int Price(string id)
        {
            Pack pack = FindPack(id);
            if (pack != null)
                return PackPrice(pack);
            GameItem item = DirectoryMaster.Item(id, false);
            long value = item != null ? item.unitValue : 0;
            if (item != null)
                item.Destroy();
            int markup = Difficulty.Rules != null ? Difficulty.Rules.retiredSupplierMarkup : 0;
            return (int)Math.Ceiling(value * PriceFactor * (100 + markup) / 100.0);
        }

        internal static int Week()
        {
            return Math.Max(0, StoreStation.GetDayCounter() - 1) / 7;
        }

        internal static int OrderedThisWeek()
        {
            return ModHelper.GetModDataInt(GunsmithMod.ModId, WeekKey, -1) == Week() ? ModHelper.GetModDataInt(GunsmithMod.ModId, WeekCountKey, 0) : 0;
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

        internal static bool CanAfford(int price)
        {
            return PlayerStore.Instance != null && OrderedThisWeek() < WeeklyLimit && PlayerStore.Instance.GetCash() >= price;
        }

        // A pack of materials is not counted in the week's 3.
        internal static bool CanAffordPack(int price)
        {
            return PlayerStore.Instance != null && PlayerStore.Instance.GetCash() >= price;
        }

        internal static bool CanOrder(string id)
        {
            return FindPack(id) != null ? CanAffordPack(Price(id)) : CanAfford(Price(id));
        }

        internal static bool PlaceOrder(string id)
        {
            Pack pack = FindPack(id);
            int price = Price(id);
            if (pack != null ? !CanAffordPack(price) : !CanAfford(price))
                return false;
            PlayerStore.Instance.ModCash(-price, true);
            List<Order> orders = Orders();
            if (pack != null)
            {
                orders.Add(new Order { Id = pack.Id, Nights = DeliveryDays });
                SaveOrders(orders);
                GunsmithMod.Log("phone order " + id + " for " + price + ", " + pack.Items.Length + " items, not counted in the week's " + WeeklyLimit);
                return true;
            }
            orders.Add(new Order { Id = id, Nights = DeliveryDays });
            SaveOrders(orders);
            int count = OrderedThisWeek() + 1;
            ModHelper.SetModDataInt(GunsmithMod.ModId, WeekKey, Week());
            ModHelper.SetModDataInt(GunsmithMod.ModId, WeekCountKey, count);
            GunsmithMod.Log("phone order " + id + " for " + price + ", " + count + " this week");
            return true;
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
        // tries again, as often as it takes. The game does not stop items from being dragged while a choice is shown.
        private static void HandOver(StoreClient courier, Dialogue line)
        {
            string elsewhere;
            int noRoom = Deliver(out elsewhere);
            if (line.choices != null)
                line.choices.Clear();
            if (noRoom > 0)
            {
                line.SetText(courier.displayName, Text.Pick("货到了，可店里哪儿都放不下。你腾个地方，我等着。",
                    "Your order's here, but there's no room for it anywhere. Clear a space, I'll wait."));
                line.SetEndAction(null);
                Dialogue cleared = new Dialogue().SetText("PLAYER", Text.Pick("腾好了。", "There's room now."));
                line.AddChoice(DirectoryMaster.Item("$", true), "cleared", "", cleared, () => HandOver(courier, cleared.NextDialogue()));
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

        // Built like the game's couriers that hand over goods without a sale (StoreClientListSupplier.CreateCourrierChemFree).
        internal static StoreClient CreateCourier()
        {
            var c = new StoreClient();
            c.identifier = CourierId;
            c.displayName = Name();
            c.spriteName = SpriteDict.GetRandomLowerLevelSpriteName();
            c.clientFaction = "FACTION_LOWER_LEVEL";
            c.isBargainAvailable = false;
            c.clientIntent = (StoreClient.ClientIntent)2;
            c.isRegularSellDisable = true;
            c.mainDialogue.SetText(c.displayName, Text.Pick("你电话里订的货，我送来了。钱已经付过了。", "Here's what you ordered on the phone. It's paid for."))
                .SetStartAction(() => HandOver(c, c.mainDialogue))
                .SetEndAction(LeaveAfterHandOver);
            c.allDoneDialogue = new Dialogue().SetText(c.displayName, Text.Pick("回头见。", "See you."))
                .SetEndAction(LeaveAfterHandOver);
            c.CompleteClientCreation(false);
            return c;
        }

        // The game's phone dialogue has 3 choice bubbles; for the catalog more are copied from the last one when the
        // call starts. A bubble selects the choice of its own index (DialogChoiceBubble.index), and Unity points the
        // copy's click listener at the copy. PhoneChoices measures the available game area,
        // wraps labels and paginates for the selected native bubble scale. Other phone calls
        // regain the game's original three positions and heights.
        internal const int ChoiceBubbles = 8;
        private sealed class MenuView
        {
            internal Menu Menu, Root;
            internal int Start, Count;
        }
        private static readonly Dictionary<Dialogue, MenuView> menuPages = new Dictionary<Dialogue, MenuView>();

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
            return page != null && menuPages.ContainsKey(page);
        }

        internal static void PrepareChoicePage(Dialogue page)
        {
            MenuView view;
            if (page == null || !menuPages.TryGetValue(page, out view)) return;
            int count = PhoneChoices.PageCapacity(MenuLabels(view.Menu, view.Start));
            if (count == view.Count) return;
            foreach (var choice in page.choices)
                if (choice.Item1 != null) choice.Item1.Destroy();
            page.choices.Clear();
            ShowMenu(page, view.Menu, view.Root, view.Start);
        }

        private static string ItemChoiceLabel(string id)
        {
            Pack pack = FindPack(id);
            string itemName;
            if (pack != null) itemName = PackName(pack);
            else
            {
                GameItem blueprint = DirectoryMaster.Item(id, false);
                itemName = blueprint != null ? blueprint.name : id;
                if (blueprint != null) blueprint.Destroy();
            }
            return string.Format(Text.Pick("{0}，{1} 信用点。", "{0}, {1} credits."), itemName, Price(id));
        }

        private static List<string> MenuLabels(Menu menu, int start)
        {
            var labels = new List<string>();
            int count = menu.Children.Count > 0 ? menu.Children.Count : menu.Items.Count;
            for (int i = start; i < count && i < start + ChoiceBubbles - 2; i++)
                labels.Add(menu.Children.Count > 0 ? menu.Children[i].Label + Text.Pick("。", ".") : ItemChoiceLabel(menu.Items[i]));
            return labels;
        }

        // The call: a greeting with the week's remaining orders, then the catalog's groups. A group of groups offers one
        // choice for each and "that's all"; a group of items offers a page of them (as many as there are choice
        // bubbles less two), then "what else" (the next page; after the last, back to the first groups) and "that's
        // all".
        internal static Dialogue Call(StorePhoneClient client)
        {
            EnsureChoiceBubbles();
            menuPages.Clear();
            string name = Name();
            var root = new Dialogue();
            int left = WeeklyLimit - OrderedThisWeek();
            root.SetText(name, left > 0
                ? string.Format(Text.Pick("要订什么？先付全款，两天后送到。材料不限件数，别的货这周还能订 {0} 件。",
                    "What do you need? You pay now and I bring it in two days. Materials as many as you like; {0} more of anything else this week."), left)
                : Text.Pick("材料以外的货这周已经订了 3 件，下周再订；材料不限件数。先付全款，两天后送到。",
                    "You've had three things other than materials this week, the rest next week. Materials as many as you like: pay now, they come in two days."));
            Menu catalog = BuildCatalog(GunSupply.PermitTop());
            ShowMenu(root, catalog, catalog, 0);
            return root;
        }

        private static void ShowMenu(Dialogue page, Menu menu, Menu root, int start)
        {
            string name = Name();
            EnsureChoiceBubbles();
            int perPage = PhoneChoices.PageCapacity(MenuLabels(menu, start));
            menuPages[page] = new MenuView { Menu = menu, Root = root, Start = start, Count = perPage };
            if (menu.Children.Count > 0)
            {
                for (int i = start; i < menu.Children.Count && i < start + perPage; i++)
                {
                    Menu c = menu.Children[i];
                    Dialogue open = new Dialogue().SetText("PLAYER", c.Label + Text.Pick("。", "."));
                    page.AddChoice(DirectoryMaster.Item("$", true), "", "", open, () =>
                    {
                        Dialogue reply = open.NextDialogue().SetText(name, c.Children.Count > 0 ? Text.Pick("要哪一类？", "Which kind?") : Text.Pick("有这些。", "I've got these."));
                        ShowMenu(reply, c, root, 0);
                    });
                }
                if (menu.Children.Count > perPage)
                    AddMore(page, menu, root, start, perPage, menu.Children.Count);
                AddBye(page);
                return;
            }
            List<string> catalog = menu.Items;
            for (int i = start; i < catalog.Count && i < start + perPage; i++)
            {
                string id = catalog[i];
                Pack pack = FindPack(id);
                int price = Price(id);
                Dialogue pick = new Dialogue().SetText("PLAYER", ItemChoiceLabel(id));
                page.AddChoice(DirectoryMaster.Item("$", true), "", "", pick, () =>
                {
                    bool placed = PlaceOrder(id);
                    pick.NextDialogue().SetText(name, placed
                        ? Text.Pick("好，两天后送到。", "Fine, it'll be there in two days.")
                        : Text.Pick("钱不够，或者这周订满了。", "Not enough money, or you've ordered enough this week."))
                        .SetEndAction(EndCall);
                }, () => pack != null ? CanAffordPack(price) : CanAfford(price));
            }
            AddMore(page, menu, root, start, perPage, catalog.Count);
            AddBye(page);
        }

        private static void AddMore(Dialogue page, Menu menu, Menu root, int start, int perPage, int count)
        {
            string name = Name();
            int next = start + perPage;
            Dialogue more = new Dialogue().SetText("PLAYER", Text.Pick("还有别的吗？", "What else have you got?"));
            page.AddChoice(DirectoryMaster.Item("$", true), "", "", more, () =>
            {
                if (next < count)
                {
                    // Page length can change with resolution, scale and label wrapping.
                    Dialogue nextPage = more.NextDialogue().SetText(name, Text.Pick("有这些。", "I've got these."));
                    ShowMenu(nextPage, menu, root, next);
                }
                else
                {
                    Dialogue groups = more.NextDialogue().SetText(name, Text.Pick("这一类就这些。别的呢？", "That's all of that kind. Anything else?"));
                    ShowMenu(groups, root, root, 0);
                }
            });
        }

        private static void AddBye(Dialogue page)
        {
            string name = Name();
            Dialogue bye = new Dialogue().SetText("PLAYER", Text.Pick("先这样。", "That's all."));
            page.AddChoice(DirectoryMaster.Item("$", true), "", "", bye, () =>
            {
                bye.NextDialogue().SetText(name, Text.Pick("那回头再说。", "Talk later, then.")).SetEndAction(EndCall);
            });
        }

        private static void EndCall()
        {
            if (PhoneUIManager.Instance != null)
                PhoneUIManager.Instance.OnPhoneDialogEnd(true);
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
