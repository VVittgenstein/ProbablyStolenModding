using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;

namespace GunsmithExpansion
{
    // The line 3955 of Lincoln Arms (final-strategy 337, 342 ③ to ⑥, 350 ② ③ ④; the dialogue draft's sections 2, 3 and 6): in the phone
    // book from favor Lv.1. Yuri answers, Ava on his day off, each in the voice of the level (LincolnTexts.Voice). The regular goods of the
    // shop that are in today's stock, at the shop's prices (LincolnGoods.Price): ordered goods take their piece from the day's stock;
    // 3 a week at Lv.1 and Lv.2, 4 at Lv.3, 5 at Lv.4 (weeks of days 1 to 7, 8 to 14 and so on, Phone.Week), the material packs without
    // limit; paid at once, counted for the favor, brought 2 days later by the one on duty that day, in the delivery boxes
    // (DeliveryBoxes). Guns are offered in each condition the level opens (342 ④ ⑤: excellent from Lv.2, new from Lv.3).
    // The menu: a group (大类), then a kind (类), then a page of goods; "What else" turns the page, after the last one back to the
    // groups. Agent's arrangements (not confirmed by yzz): the groups and kinds (guns by restriction level and class; gun mods by the
    // categories of the manuals; rounds and reloading; chips and modules by the retired gunsmith's old catalog; materials and packs;
    // storage and books); a group of one kind opens its goods at once; a call ends after an order, as the retired gunsmith's did; the
    // goods of a guns' list are listed once for each condition.
    internal static class LincolnPhone
    {
        internal const string DialogFuncId = "GseLincolnArms";
        internal const string CourierId = "gseLincolnDelivery";
        internal const int DeliveryDays = 2;
        private const string OrdersKey = "lincoln_orders";
        private const string WeekKey = "lincoln_week";
        private const string WeekCountKey = "lincoln_week_count";

        internal sealed class Order
        {
            internal string Key;
            internal int Condition;
            internal int Price;
            internal int Nights;
        }

        private sealed class Group
        {
            internal string Label;
            internal readonly List<Kind> Kinds = new List<Kind>();
        }

        private sealed class Kind
        {
            internal string Label;
            internal readonly List<LincolnGoods.Good> Goods = new List<LincolnGoods.Good>();
        }

        private static readonly Dictionary<string, string[]> ModCategories = new Dictionary<string, string[]>
        {
            // the names of the manuals (tools/manualtables/book_catalog.py MOD_CATEGORIES)
            { "红点与全息", new[] { "红点与全息瞄具", "Red dot and holographic sights" } },
            { "中倍率瞄具", new[] { "中倍率瞄具", "Medium-power sights" } },
            { "远距瞄准镜", new[] { "远距瞄准镜", "Long-range scopes" } },
            { "机械瞄具", new[] { "机械瞄具", "Iron sights" } },
            { "镜桥与底座", new[] { "镜桥与底座", "Mounts and risers" } },
            { "消音器", new[] { "消音器", "Suppressors" } },
            { "枪口装置", new[] { "枪口装置", "Muzzle devices" } },
            { "前握把与止手", new[] { "前握把与止手", "Foregrips and handstops" } },
            { "两脚架与单脚架", new[] { "两脚架与单脚架", "Bipods and monopods" } },
            { "战术灯与激光", new[] { "战术灯与激光", "Lights and lasers" } },
            { "枪托与底盘", new[] { "枪托与底盘", "Stocks and chassis" } },
            { "弹匣与弹鼓", new[] { "弹匣与弹鼓", "Magazines and drums" } },
            { "护木", new[] { "护木", "Handguards" } },
            { "握把", new[] { "握把", "Grips" } },
            { "内部零件", new[] { "内部零件", "Internal parts" } },
        };

        // The game's gun mods sold at the shop, by the categories above.
        private static readonly Dictionary<string, string> GameModCategories = new Dictionary<string, string>
        {
            { "rds", "红点与全息" }, { "rds_makeshift", "红点与全息" }, { "laser", "战术灯与激光" }, { "laser2", "战术灯与激光" },
            { "laser_toy", "战术灯与激光" }, { "silencer", "消音器" }, { "silencer2", "消音器" }, { "silencer_makeshift", "消音器" },
            { "internal_springs_improved", "内部零件" },
        };

        // The game's guns sold at the shop, by the classes of the catalog.
        private static readonly Dictionary<string, string> GameGunClasses = new Dictionary<string, string>
        {
            { "handmade_pistol", "手枪" }, { "heavy_handmade_pistol", "手枪" }, { "glock_receiver", "手枪" }, { "revolver", "左轮（含左轮卡宾）" },
            { "smg", "冲锋枪" }, { "shotgun", "霰弹枪" },
        };

        internal static void Register()
        {
            PhoneClientList.DialogFuncs[DialogFuncId] = Call;
            StoreClientListDict.storeClientDict[CourierId] = CreateCourier;
        }

        // In the phone book from Lv.1 (Lincoln.AfterChange, Lincoln.OnGameLoaded), as the retired gunsmith's number (Phone.EnsureContact).
        internal static void EnsureContact()
        {
            PlayerStore store = PlayerStore.Instance;
            Dictionary<long, StorePhoneClient> book = store != null ? store.PhoneClientDict : null;
            if (book == null)
                return;
            StorePhoneClient contact;
            if (!book.TryGetValue(Lincoln.Number, out contact) || contact == null)
            {
                contact = new StorePhoneClient
                {
                    displayName = "Lincoln Arms",
                    locID = "",
                    phoneClientType = StorePhoneClient.PhoneClientType.Supplier,
                    phoneState = StorePhoneClient.PhoneState.Regular,
                    dialogFuncId = DialogFuncId,
                    cooldownDuration = 0,
                    alias = new List<string> { "lincoln_arms" },
                };
                book[Lincoln.Number] = contact;
                GunsmithMod.Log("Lincoln Arms' line " + Lincoln.Number + " in the phone book");
            }
            contact.dialedBefore = true;
        }

        internal static bool IsContact(StorePhoneClient contact)
        {
            return contact != null && contact.dialogFuncId == DialogFuncId;
        }

        internal static int OrderedThisWeek()
        {
            return ModHelper.GetModDataInt(GunsmithMod.ModId, WeekKey, -1) == Phone.Week() ? ModHelper.GetModDataInt(GunsmithMod.ModId, WeekCountKey, 0) : 0;
        }

        internal static int LeftThisWeek()
        {
            return Math.Max(0, Lincoln.WeeklyPhoneItems() - OrderedThisWeek());
        }

        internal static List<Order> Orders()
        {
            var list = new List<Order>();
            string text = ModHelper.GetModData(GunsmithMod.ModId, OrdersKey, "") ?? "";
            foreach (string entry in text.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = entry.Split('|');
                int condition, price, nights;
                if (parts.Length == 4 && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out condition)
                    && int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out price)
                    && int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out nights))
                    list.Add(new Order { Key = parts[0], Condition = condition, Price = price, Nights = nights });
            }
            return list;
        }

        private static void SaveOrders(List<Order> orders)
        {
            var entries = new List<string>();
            foreach (Order o in orders)
                entries.Add(o.Key + "|" + o.Condition.ToString(CultureInfo.InvariantCulture) + "|" + o.Price.ToString(CultureInfo.InvariantCulture)
                    + "|" + o.Nights.ToString(CultureInfo.InvariantCulture));
            ModHelper.SetModData(GunsmithMod.ModId, OrdersKey, string.Join(";", entries.ToArray()));
        }

        // Each night (LincolnNightPatch): pending orders a day closer; with one due, the one on duty tomorrow comes (as Phone.OnNight).
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
            PlayerStore store = PlayerStore.Instance;
            if (due && store != null && !store.futurStoreClientIdQueue.Contains(CourierId))
            {
                store.QueueFuturClient(CourierId, 0);
                GunsmithMod.Log("Lincoln Arms delivery queued for tomorrow");
            }
        }

        private static string Speaker()
        {
            return Lincoln.OnDutyName();
        }

        private static int Voice()
        {
            return LincolnTexts.Voice(Lincoln.AvaOnDuty(), Lincoln.Level());
        }

        // The call (PhoneClientList.DialogFuncs): the opening by the week's share left, then the groups.
        internal static Dialogue Call(StorePhoneClient client)
        {
            Phone.EnsureChoiceBubbles();
            PhoneMenus.Clear();
            int voice = Voice();
            var root = new Dialogue();
            root.SetText(Speaker(), (LeftThisWeek() > 0 ? LincolnTexts.PhoneOpen[voice] : LincolnTexts.PhoneOpenQuotaUsed[voice]).Get());
            ShowGroups(root, Catalog());
            return root;
        }

        // What the line offers now: the regular goods of the level and the permits held that are in today's stock (342 ⑦: no rare guns).
        private static List<Group> Catalog()
        {
            int level = Lincoln.Level();
            var groups = new List<Group>();
            Group guns = NewGroup(groups, Text.Pick("整枪", "Guns"));
            Group mods = NewGroup(groups, Text.Pick("枪械配件", "Gun mods"));
            Group rounds = NewGroup(groups, Text.Pick("弹药与复装", "Rounds and reloading"));
            Group chips = NewGroup(groups, Text.Pick("芯片与打印机模组", "Chips and printer modules"));
            Group materials = NewGroup(groups, Text.Pick("材料", "Materials"));
            Group storage = NewGroup(groups, Text.Pick("枪柜、收纳与书", "Cabinets, storage and books"));
            foreach (LincolnGoods.Good g in LincolnGoods.Unlocked(level, GunSupply.PermitTop()))
            {
                if (LincolnGoods.Left(g) <= 0)
                    continue;
                switch (g.Kind)
                {
                    case LincolnGoods.Kind.Gun:
                        AddTo(guns, GunKind(g), g);
                        break;
                    case LincolnGoods.Kind.GunMod:
                        AddTo(mods, ModKind(g), g);
                        break;
                    case LincolnGoods.Kind.Rounds:
                        AddTo(rounds, Text.Pick("弹药", "Rounds"), g);
                        break;
                    case LincolnGoods.Kind.Reloading:
                        AddTo(rounds, Text.Pick("复装器材", "Reloading gear"), g);
                        break;
                    case LincolnGoods.Kind.Chip:
                        AddTo(chips, ChipKind(g), g);
                        break;
                    case LincolnGoods.Kind.Module:
                        AddTo(chips, Text.Pick("打印机模组", "Printer modules"), g);
                        break;
                    case LincolnGoods.Kind.Material:
                        AddTo(materials, Text.Pick("散装维修材料", "Repair materials"), g);
                        break;
                    case LincolnGoods.Kind.Pack:
                        AddTo(materials, Text.Pick("固定材料包", "Material packs"), g);
                        break;
                    case LincolnGoods.Kind.Storage:
                        AddTo(storage, Text.Pick("枪柜与芯片盒", "Gun cabinets and chip boxes"), g);
                        break;
                    case LincolnGoods.Kind.Book:
                        AddTo(storage, Text.Pick("书", "Books"), g);
                        break;
                }
            }
            groups.RemoveAll(gr => gr.Kinds.Count == 0);
            return groups;
        }

        private static Group NewGroup(List<Group> groups, string label)
        {
            var g = new Group { Label = label };
            groups.Add(g);
            return g;
        }

        private static void AddTo(Group group, string kindLabel, LincolnGoods.Good g)
        {
            Kind kind = group.Kinds.Find(k => k.Label == kindLabel);
            if (kind == null)
            {
                kind = new Kind { Label = kindLabel };
                group.Kinds.Add(kind);
            }
            kind.Goods.Add(g);
        }

        private static string GunKind(LincolnGoods.Good g)
        {
            string cls = g.Gun != null ? g.Gun.ClassName : null;
            if (cls == null && !GameGunClasses.TryGetValue(g.ItemId, out cls))
                cls = "手枪";
            string tier = g.Tier == 1 ? "一档" : g.Tier == 2 ? "二档" : "三档";
            // Japanese: "ピストル（制限付き I）"; the English order with a comma reads wrongly there (the T-013 Japanese translation's report).
            string format = Text.Japanese() ? "{1}（{0}）" : Text.Pick("{0}{1}", "{1}, {0}");
            return string.Format(format, Text.TierName(tier), Text.ClassName(cls));
        }

        private static string ModKind(LincolnGoods.Good g)
        {
            string category = null;
            foreach (ModDef d in GunsmithExpansion.Catalog.Mods)
            {
                if (d.Id == g.ItemId)
                    category = d.Category;
            }
            if (category == null && !GameModCategories.TryGetValue(g.ItemId, out category))
                category = "内部零件";
            string[] names;
            return ModCategories.TryGetValue(category, out names) ? Text.Pick(names[0], names[1]) : category;
        }

        // As the retired gunsmith's catalog grouped them (Phone.BuildCatalog until T-013), the blank chip of a level with that tier.
        private static string ChipKind(LincolnGoods.Good g)
        {
            if (Array.IndexOf(Phone.AttachmentChips, g.ItemId) >= 0)
                return Text.Pick("配件与弹匣的3D打印机芯片", "Attachment and magazine 3D printer chips");
            if (Array.IndexOf(Phone.WeaponChips, g.ItemId) >= 0 || Array.IndexOf(Phone.PermitIIWeaponChips, g.ItemId) >= 0)
                return Text.Pick("简易枪械、刀具与零件的3D打印机芯片", "Makeshift gun, blade and part 3D printer chips");
            string tier = g.Level <= 0 ? "一档" : g.Level == 1 ? "二档" : "三档";
            return string.Format(Text.Pick("{0}枪械的3D打印机芯片", "{0} gun 3D printer chips"), Text.TierName(tier));
        }

        private static void ShowGroups(Dialogue page, List<Group> groups)
        {
            var entries = new List<PhoneMenus.Entry>();
            foreach (Group gr in groups)
            {
                Group group = gr;
                entries.Add(new PhoneMenus.Entry
                {
                    Label = group.Label + Text.FullStop(),
                    Pick = line =>
                    {
                        int voice = Voice();
                        if (group.Kinds.Count == 1)
                            ShowGoods(line.NextDialogue().SetText(Speaker(), LincolnTexts.PhoneList[voice].Get()), group.Kinds[0], groups, 0);
                        else
                            ShowKinds(line.NextDialogue().SetText(Speaker(), LincolnTexts.PhoneWhichKind[voice].Get()), group, groups);
                    },
                });
            }
            PhoneMenus.ShowList(page, entries, 0, LincolnTexts.PhoneMore.Get(), (line, next) => MoreGroups(line, next, groups, entries),
                false, LincolnTexts.PhoneBye.Get(), HangUp);
        }

        private static void MoreGroups(Dialogue line, int next, List<Group> groups, List<PhoneMenus.Entry> entries)
        {
            int voice = Voice();
            Dialogue page = line.NextDialogue().SetText(Speaker(), (next >= 0 ? LincolnTexts.PhoneNextPage[voice] : LincolnTexts.PhoneLastPage[voice]).Get());
            PhoneMenus.ShowList(page, entries, Math.Max(0, next), LincolnTexts.PhoneMore.Get(), (l, n) => MoreGroups(l, n, groups, entries),
                false, LincolnTexts.PhoneBye.Get(), HangUp);
        }

        private static void ShowKinds(Dialogue page, Group group, List<Group> groups)
        {
            var entries = new List<PhoneMenus.Entry>();
            foreach (Kind k in group.Kinds)
            {
                Kind kind = k;
                entries.Add(new PhoneMenus.Entry
                {
                    Label = kind.Label + Text.FullStop(),
                    Pick = line => ShowGoods(line.NextDialogue().SetText(Speaker(), LincolnTexts.PhoneList[Voice()].Get()), kind, groups, 0),
                });
            }
            PhoneMenus.ShowList(page, entries, 0, LincolnTexts.PhoneMore.Get(), (line, next) =>
            {
                int voice = Voice();
                if (next >= 0)
                    ShowKindsFrom(line.NextDialogue().SetText(Speaker(), LincolnTexts.PhoneNextPage[voice].Get()), entries, next, groups);
                else
                    ShowGroups(line.NextDialogue().SetText(Speaker(), LincolnTexts.PhoneLastPage[voice].Get()), groups);
            }, false, LincolnTexts.PhoneBye.Get(), HangUp);
        }

        private static void ShowKindsFrom(Dialogue page, List<PhoneMenus.Entry> entries, int start, List<Group> groups)
        {
            PhoneMenus.ShowList(page, entries, start, LincolnTexts.PhoneMore.Get(), (line, next) =>
            {
                int voice = Voice();
                if (next >= 0)
                    ShowKindsFrom(line.NextDialogue().SetText(Speaker(), LincolnTexts.PhoneNextPage[voice].Get()), entries, next, groups);
                else
                    ShowGroups(line.NextDialogue().SetText(Speaker(), LincolnTexts.PhoneLastPage[voice].Get()), groups);
            }, false, LincolnTexts.PhoneBye.Get(), HangUp);
        }

        // A page of goods: each with its price; "What else" turns the page, after the last back to the groups (6-2).
        private static void ShowGoods(Dialogue page, Kind kind, List<Group> groups, int start)
        {
            var entries = new List<PhoneMenus.Entry>();
            int level = Lincoln.Level();
            foreach (LincolnGoods.Good good in kind.Goods)
            {
                LincolnGoods.Good g = good;
                var conditions = LincolnGoods.IsGun(g) ? LincolnGoods.PhoneConditions(level)
                    : new List<ItemFeatureList.EquipmentCondition> { ItemFeatureList.EquipmentCondition.Good };
                foreach (ItemFeatureList.EquipmentCondition each in conditions)
                {
                    ItemFeatureList.EquipmentCondition condition = each;
                    var entry = new PhoneMenus.Entry();
                    entry.Pick = line => PlaceOrder(g, condition, line);
                    entry.Enabled = () => g.Kind == LincolnGoods.Kind.Pack || LeftThisWeek() > 0;
                    entry.MakeLabel = () => LincolnTexts.PhoneItemChoice.Format(LincolnGoods.Name(g, condition), LincolnGoods.Price(g, condition));
                    entries.Add(entry);
                }
            }
            ShowGoodsFrom(page, entries, start, groups);
        }

        private static void ShowGoodsFrom(Dialogue page, List<PhoneMenus.Entry> entries, int start, List<Group> groups)
        {
            PhoneMenus.ShowList(page, entries, start, LincolnTexts.PhoneMore.Get(), (line, next) =>
            {
                int voice = Voice();
                if (next >= 0)
                    ShowGoodsFrom(line.NextDialogue().SetText(Speaker(), LincolnTexts.PhoneNextPage[voice].Get()), entries, next, groups);
                else
                    ShowGroups(line.NextDialogue().SetText(Speaker(), LincolnTexts.PhoneLastPage[voice].Get()), groups);
            }, true, LincolnTexts.PhoneBye.Get(), HangUp);
        }

        private static void HangUp(Dialogue line)
        {
            line.NextDialogue().SetText(Speaker(), LincolnTexts.PhoneHangUp[Voice()].Get()).SetEndAction(PhoneMenus.EndCall);
        }

        // Paid at once at today's price; the piece comes off today's stock; not counted in the week when a material pack.
        private static void PlaceOrder(LincolnGoods.Good g, ItemFeatureList.EquipmentCondition condition, Dialogue line)
        {
            int voice = Voice();
            Dialogue reply = line.NextDialogue();
            PlayerStore store = PlayerStore.Instance;
            int price = LincolnGoods.Price(g, condition);
            bool pack = g.Kind == LincolnGoods.Kind.Pack;
            if (store == null || price < 0 || LincolnGoods.Left(g) <= 0 || (!pack && LeftThisWeek() <= 0))
            {
                reply.SetText(Speaker(), LincolnTexts.PhoneOpenQuotaUsed[voice].Get()).SetEndAction(PhoneMenus.EndCall);
                return;
            }
            if (store.GetCash() < price)
            {
                reply.SetText(Speaker(), LincolnTexts.PhoneNoMoney[voice].Get()).SetEndAction(PhoneMenus.EndCall);
                return;
            }
            store.ModCash(-price, true);
            List<Order> orders = Orders();
            orders.Add(new Order { Key = g.Key, Condition = (int)condition, Price = price, Nights = DeliveryDays });
            SaveOrders(orders);
            LincolnGoods.Take(g, 1);
            if (!pack)
            {
                int count = OrderedThisWeek() + 1;
                ModHelper.SetModDataInt(GunsmithMod.ModId, WeekKey, Phone.Week());
                ModHelper.SetModDataInt(GunsmithMod.ModId, WeekCountKey, count);
            }
            GunsmithMod.Log("ordered from Lincoln Arms: " + g.Key + " (" + condition + ") for " + price + ", " + OrderedThisWeek() + " this week");
            Lincoln.AddSpent(price, "phone: " + g.Key);
            reply.SetText(Speaker(), LincolnTexts.PhoneOrdered[voice].Get()).SetEndAction(PhoneMenus.EndCall);
        }

        // The delivery: Yuri, or Ava on his day off; Ava first says who she is to a player who never met her (3-1).
        internal static StoreClient CreateCourier()
        {
            bool ava = Lincoln.AvaOnDuty();
            var c = new StoreClient();
            c.identifier = CourierId;
            c.displayName = ava ? Lincoln.DaughterName() : Lincoln.DealerName();
            c.spriteName = Lincoln.OnDutyPortrait();
            c.clientFaction = "FACTION_LOWER_LEVEL";
            c.isBargainAvailable = false;
            c.nonShootable = true;
            c.clientIntent = (StoreClient.ClientIntent)2;
            c.isRegularSellDisable = true;
            string name = c.displayName;
            Dialogue line = c.mainDialogue;
            if (ava && !Lincoln.AvaMet())
            {
                LincolnTexts.Line[] intro = LincolnTexts.AvaIntro[Lincoln.Level() >= 2 ? 1 : 0];
                line = c.mainDialogue.SetText(name, intro[0].Get()).SetStartAction(Lincoln.SetAvaMet).NextDialogue().SetText(name, intro[1].Get()).NextDialogue();
            }
            else if (ava)
                c.mainDialogue.SetStartAction(Lincoln.SetAvaMet);
            Dialogue delivery = line;
            delivery.SetText(name, (ava ? LincolnTexts.AvaDelivered : LincolnTexts.YuriDelivered)[Math.Max(1, Lincoln.Level())].Get());
            Action handOver = () => HandOver(c, ava, delivery);
            delivery.startAction = (Action)Delegate.Combine(delivery.startAction, handOver);
            c.allDoneDialogue = new Dialogue().SetText(name, (ava ? LincolnTexts.AvaDeliveryBye : LincolnTexts.YuriDeliveryBye)[Math.Max(1, Lincoln.Level())].Get())
                .SetEndAction(Leave);
            c.CompleteClientCreation(false);
            return c;
        }

        // Every order that is due, as Phone.HandOver: without room for a box the one on duty waits until the player has made some.
        private static void HandOver(StoreClient courier, bool ava, Dialogue line)
        {
            int level = Math.Max(1, Lincoln.Level());
            string name = courier.displayName;
            string elsewhere;
            int noRoom = Deliver(out elsewhere);
            if (line.choices != null)
                line.choices.Clear();
            if (noRoom > 0)
            {
                line.SetText(name, (ava ? LincolnTexts.AvaNoRoom : LincolnTexts.YuriNoRoom)[level].Get());
                line.SetEndAction(null);
                Dialogue cleared = new Dialogue().SetText("PLAYER", LincolnTexts.Cleared.Get());
                line.AddChoice(DirectoryMaster.Item("$", true), "", "", cleared, () => HandOver(courier, ava, cleared.NextDialogue()));
                return;
            }
            line.SetText(name, elsewhere != null
                ? (ava ? LincolnTexts.AvaDeliveredElsewhere : LincolnTexts.YuriDeliveredElsewhere)[level].Format(elsewhere)
                : (ava ? LincolnTexts.AvaDelivered : LincolnTexts.YuriDelivered)[level].Get());
            line.NextDialogue().SetText(name, (ava ? LincolnTexts.AvaDeliveryBye : LincolnTexts.YuriDeliveryBye)[level].Get()).SetEndAction(Leave);
        }

        // Hands over what is due; returns how many due orders found no room. A good no longer known (a removed item) is dropped from the
        // list with its payment returned.
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
                LincolnGoods.Good g = LincolnGoods.Find(o.Key);
                if (g == null)
                {
                    PlayerStore.Instance.ModCash(o.Price, false);
                    GunsmithMod.Warn("Lincoln Arms order of an unknown good " + o.Key + " refunded: " + o.Price);
                    continue;
                }
                bool ok;
                if (g.Kind == LincolnGoods.Kind.Pack)
                    ok = DeliveryBoxes.HandPack(g.Pack, ref elsewhere, DeliveryBoxes.FromLincoln);
                else
                {
                    GameItem item = LincolnGoods.Make(g, (ItemFeatureList.EquipmentCondition)o.Condition);
                    if (item == null)
                    {
                        left.Add(o);
                        noRoom++;
                        continue;
                    }
                    GeneralHelper.SetItemOwned(item, true);
                    GeneralHelper.TrackBuyPrice(item, o.Price);
                    ok = DeliveryBoxes.Hand(item, boxes, ref elsewhere, DeliveryBoxes.FromLincoln);
                }
                if (ok)
                    handed++;
                else
                {
                    left.Add(o);
                    noRoom++;
                }
            }
            SaveOrders(left);
            GunsmithMod.Log("Lincoln Arms delivered " + handed + " orders, " + noRoom + " without room, " + (left.Count - noRoom) + " still on order");
            return noRoom;
        }

        private static void Leave()
        {
            if (DialogUIManager.Instance != null)
                DialogUIManager.Instance.CurrentClientLeave(1f);
        }
    }

    // The phone book names the line by the game's client names (StorePhoneClient.GetLocalizedDisplayName); this one has none there.
    [HarmonyPatch(typeof(StorePhoneClient), nameof(StorePhoneClient.GetLocalizedDisplayName))]
    internal static class LincolnContactNamePatch
    {
        private static void Postfix(StorePhoneClient __instance, ref string __result)
        {
            if (LincolnPhone.IsContact(__instance))
                __result = Lincoln.ShopName();
            else if (__instance != null && __instance.dialogFuncId == Phone.DialogFuncId)
                __result = Phone.Name();
        }
    }
}
