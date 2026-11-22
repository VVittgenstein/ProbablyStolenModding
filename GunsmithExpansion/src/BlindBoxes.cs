using System;
using System.Collections.Generic;
using System.Globalization;

namespace GunsmithExpansion
{
    // The retired gunsmith's three blind boxes on 4867 (final-strategy 300, 303, 304, 340, 348, 354; the dialogue draft's section 7). From
    // this version the number offers these instead of his catalog, which went to Lincoln Arms; orders paid before the update are still
    // brought (Phone.Deliver).
    //   ① 3 boxes a week of the three kinds together (weeks of Phone.Week), paid at once, brought 2 days later by his courier.
    //   ② The parts box: 200 credits on the normal difficulty (174, 226, 252 on the others); 4 of this mod's gun mods drawn alike, at most 2
    //     of one, their base values together 150 to 250 on every difficulty.
    //   ③ The materials box: 125 (109, 141, 156); 6 repair materials drawn by the weights screws 20, wire 15, electronics 10, scrap metal 20,
    //     gun parts 20, alcohol 10, fabric 5, of at least 3 kinds and at most 1 fabric, their base values together 100 to 150.
    //   ④ The gun box of a class: one regular gun of this mod of the tier of the highest weapon permit held and of the class chosen (not the
    //     old and antique, not the special tier, the game's or the 2077 Add-On's), in poor 10%, fair 50%, good 35%, excellent 5% condition,
    //     without faults; its price on the normal difficulty the mean base value of the class's guns of that tier x 0.90, rounded to the
    //     nearest 10, on the others x (100 + the retired supplier markup) / 115. Without a permit he says he sells none.
    //   ⑤ Each comes in the carton (gse_box_gun) named after its kind; what is inside is drawn when it is paid and saved with the order.
    // Facts of the implementation: the prices of ② and ③ are those 340 lists; for the challenge difficulty the materials box is 156 there,
    // where the rule of the packs, 125 x 145 / 115, gives 158 (the numbers draft v1 has 158). A difficulty of another mod takes the rule.
    internal static class BlindBoxes
    {
        internal const int WeeklyLimit = 3;
        internal const int DeliveryDays = 2;
        internal const string Parts = "parts";
        internal const string Materials = "materials";
        internal const string GunPrefix = "gun:";
        private const string OrdersKey = "blind_orders";
        private const string WeekKey = "blind_week";
        private const string WeekCountKey = "blind_week_count";

        private static readonly int[] Markups = { 0, 15, 30, 45 };
        private static readonly int[] PartsPrices = { 174, 200, 226, 252 };
        private static readonly int[] MaterialsPrices = { 109, 125, 141, 156 };
        private const int PartsNormal = 200, MaterialsNormal = 125;
        private const int PartsCount = 4, PartsSameMax = 2, PartsMin = 150, PartsMax = 250;
        private const int MaterialsCount = 6, MaterialsKindsMin = 3, MaterialsMin = 100, MaterialsMax = 150;
        private const string Fabric = "kotton_fabric";
        private static readonly string[] MaterialIds = { "nuts_metal", "wire", "common_electronic", "scrap_metal", "gun_part", "rubbing_alcohol", Fabric };
        private static readonly int[] MaterialWeights = { 20, 15, 10, 20, 20, 10, 5 };
        private static readonly int[] ConditionWeights = { 10, 50, 35, 5, 0 };
        private static readonly ItemFeatureList.EquipmentCondition[] Conditions =
        {
            ItemFeatureList.EquipmentCondition.Poor, ItemFeatureList.EquipmentCondition.Fair, ItemFeatureList.EquipmentCondition.Good,
            ItemFeatureList.EquipmentCondition.Excellent, ItemFeatureList.EquipmentCondition.New,
        };
        private const int Tries = 2000;
        private static readonly Random rng = new Random();
        private static readonly Dictionary<string, long> values = new Dictionary<string, long>();

        internal sealed class Order
        {
            internal string Kind;
            internal string Contents;
            internal int Nights;
        }

        private static int Markup()
        {
            return Difficulty.Rules != null ? Difficulty.Rules.retiredSupplierMarkup : 15;
        }

        private static int ByDifficulty(int[] prices, int normal)
        {
            int i = Array.IndexOf(Markups, Markup());
            return i >= 0 ? prices[i] : (normal * (100 + Markup()) + 57) / 115;
        }

        internal static int PartsPrice()
        {
            return ByDifficulty(PartsPrices, PartsNormal);
        }

        internal static int MaterialsPrice()
        {
            return ByDifficulty(MaterialsPrices, MaterialsNormal);
        }

        private static string TierOf(int permit)
        {
            return permit <= 1 ? "一档" : permit == 2 ? "二档" : "三档";
        }

        // The guns a box of the class holds with the permit held, and the classes in the catalog's order.
        internal static List<GunDef> Candidates(int permit, string className)
        {
            var list = new List<GunDef>();
            string tier = TierOf(permit);
            foreach (GunDef d in Catalog.Guns)
            {
                if (d.AddOn || d.Tier != tier || d.ClassName == "老式与古董" || (className != null && d.ClassName != className))
                    continue;
                LincolnGoods.Good g = LincolnGoods.Find(d.Id);
                if (g != null && LincolnGoods.Exists(g))
                    list.Add(d);
            }
            return list;
        }

        internal static List<string> GunClasses(int permit)
        {
            var classes = new List<string>();
            foreach (GunDef d in Candidates(permit, null))
            {
                if (!classes.Contains(d.ClassName))
                    classes.Add(d.ClassName);
            }
            return classes;
        }

        internal static int GunPrice(int permit, string className)
        {
            List<GunDef> guns = Candidates(permit, className);
            if (guns.Count == 0)
                return -1;
            double sum = 0;
            foreach (GunDef d in guns)
                sum += d.Value;
            int normal = (int)Math.Floor(sum / guns.Count * 0.90 / 10 + 0.5) * 10;
            return (normal * (100 + Markup()) + 57) / 115;
        }

        internal static int Week()
        {
            return Phone.Week();
        }

        internal static int OrderedThisWeek()
        {
            return ModHelper.GetModDataInt(GunsmithMod.ModId, WeekKey, -1) == Week() ? ModHelper.GetModDataInt(GunsmithMod.ModId, WeekCountKey, 0) : 0;
        }

        internal static List<Order> Orders()
        {
            var list = new List<Order>();
            string text = ModHelper.GetModData(GunsmithMod.ModId, OrdersKey, "") ?? "";
            foreach (string entry in text.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = entry.Split('|');
                int nights;
                if (parts.Length == 3 && int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out nights))
                    list.Add(new Order { Kind = parts[0], Contents = parts[1], Nights = nights });
            }
            return list;
        }

        private static void SaveOrders(List<Order> orders)
        {
            var entries = new List<string>();
            foreach (Order o in orders)
                entries.Add(o.Kind + "|" + o.Contents + "|" + o.Nights.ToString(CultureInfo.InvariantCulture));
            ModHelper.SetModData(GunsmithMod.ModId, OrdersKey, string.Join(";", entries.ToArray()));
        }

        private static long ValueOf(string id)
        {
            long value;
            if (values.TryGetValue(id, out value))
                return value;
            GameItem probe = DirectoryMaster.Item(id, false);
            value = probe != null ? probe.unitValue : 0;
            if (probe != null)
                probe.Destroy();
            values[id] = value;
            return value;
        }

        // ② Drawn until a draw meets the rules; after that many tries the draw closest to the range is taken (logged).
        internal static List<string> DrawParts()
        {
            var pool = new List<ModDef>();
            foreach (ModDef d in Catalog.Mods)
            {
                LincolnGoods.Good g = LincolnGoods.Find(d.Id);
                if (g == null || LincolnGoods.Exists(g))
                    pool.Add(d);
            }
            List<string> best = null;
            int bestOff = int.MaxValue;
            for (int t = 0; t < Tries && pool.Count > 0; t++)
            {
                var ids = new List<string>();
                int sum = 0;
                bool ok = true;
                for (int i = 0; i < PartsCount; i++)
                {
                    ModDef d = pool[rng.Next(pool.Count)];
                    if (ids.FindAll(x => x == d.Id).Count >= PartsSameMax)
                    {
                        ok = false;
                        break;
                    }
                    ids.Add(d.Id);
                    sum += d.Value;
                }
                if (!ok)
                    continue;
                int off = sum < PartsMin ? PartsMin - sum : sum > PartsMax ? sum - PartsMax : 0;
                if (off == 0)
                    return ids;
                if (off < bestOff)
                {
                    best = ids;
                    bestOff = off;
                }
            }
            GunsmithMod.Warn("parts box drawn outside its value range by " + bestOff);
            return best ?? new List<string>();
        }

        // ③
        internal static List<string> DrawMaterials()
        {
            int total = 0;
            foreach (int w in MaterialWeights)
                total += w;
            List<string> best = null;
            int bestOff = int.MaxValue;
            for (int t = 0; t < Tries; t++)
            {
                var ids = new List<string>();
                long sum = 0;
                for (int i = 0; i < MaterialsCount; i++)
                {
                    int roll = rng.Next(total), k = 0;
                    while (roll >= MaterialWeights[k])
                    {
                        roll -= MaterialWeights[k];
                        k++;
                    }
                    ids.Add(MaterialIds[k]);
                    sum += ValueOf(MaterialIds[k]);
                }
                var kinds = new HashSet<string>(ids);
                if (kinds.Count < MaterialsKindsMin || ids.FindAll(x => x == Fabric).Count > 1)
                    continue;
                int off = sum < MaterialsMin ? (int)(MaterialsMin - sum) : sum > MaterialsMax ? (int)(sum - MaterialsMax) : 0;
                if (off == 0)
                    return ids;
                if (off < bestOff)
                {
                    best = ids;
                    bestOff = off;
                }
            }
            GunsmithMod.Warn("materials box drawn outside its value range by " + bestOff);
            return best ?? new List<string>();
        }

        // ④ The gun and its condition, as id@condition.
        internal static string DrawGun(int permit, string className)
        {
            List<GunDef> guns = Candidates(permit, className);
            if (guns.Count == 0)
                return null;
            GunDef d = guns[rng.Next(guns.Count)];
            int roll = rng.Next(100), i = 0;
            while (i < ConditionWeights.Length - 1 && roll >= ConditionWeights[i])
            {
                roll -= ConditionWeights[i];
                i++;
            }
            return d.Id + "@" + ((int)Conditions[i]).ToString(CultureInfo.InvariantCulture);
        }

        private static bool Place(string kind, List<string> contents, int price)
        {
            PlayerStore store = PlayerStore.Instance;
            if (store == null || OrderedThisWeek() >= WeeklyLimit || store.GetCash() < price || contents == null || contents.Count == 0)
                return false;
            store.ModCash(-price, true);
            List<Order> orders = Orders();
            orders.Add(new Order { Kind = kind, Contents = string.Join(",", contents.ToArray()), Nights = DeliveryDays });
            SaveOrders(orders);
            int count = OrderedThisWeek() + 1;
            ModHelper.SetModDataInt(GunsmithMod.ModId, WeekKey, Week());
            ModHelper.SetModDataInt(GunsmithMod.ModId, WeekCountKey, count);
            GunsmithMod.Log("blind box " + kind + " ordered for " + price + ": " + string.Join(", ", contents.ToArray()) + "; " + count + " this week");
            return true;
        }

        // Each night (LincolnNightPatch), with the courier of the old orders (Phone.OnNight, Phone.CourierId).
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
            if (due && store != null && !store.futurStoreClientIdQueue.Contains(Phone.CourierId))
            {
                store.QueueFuturClient(Phone.CourierId, 0);
                GunsmithMod.Log("retired gunsmith blind box delivery queued for tomorrow");
            }
        }

        internal static bool AnyDue()
        {
            foreach (Order o in Orders())
            {
                if (o.Nights <= 0)
                    return true;
            }
            return false;
        }

        // The boxes due: returns how many found no room; boxes counts those handed over.
        internal static int Deliver(out string elsewhere, out int boxes)
        {
            elsewhere = null;
            boxes = 0;
            List<Order> orders = Orders(), left = new List<Order>();
            int noRoom = 0;
            foreach (Order o in orders)
            {
                if (o.Nights > 0 || PlayerStore.Instance == null)
                {
                    left.Add(o);
                    continue;
                }
                var items = new List<GameItem>();
                foreach (string entry in o.Contents.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    int at = entry.IndexOf('@');
                    string id = at > 0 ? entry.Substring(0, at) : entry;
                    GameItem item = DirectoryMaster.Item(id, true);
                    if (item == null)
                    {
                        GunsmithMod.Warn("blind box item " + id + " not made");
                        continue;
                    }
                    int condition;
                    if (at > 0 && int.TryParse(entry.Substring(at + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out condition))
                        GunHelper.SetEquipmentCondition(item, (ItemFeatureList.EquipmentCondition)condition);
                    items.Add(item);
                }
                GameItem box = DeliveryBoxes.FilledBox(items, o.Kind);
                if (box == null)
                {
                    GunsmithMod.Warn("blind box " + o.Kind + " could not be filled; the order is dropped");
                    continue;
                }
                if (DeliveryBoxes.PlaceItem(box, ref elsewhere))
                    boxes++;
                else
                {
                    box.Destroy();
                    left.Add(o);
                    noRoom++;
                }
            }
            SaveOrders(left);
            if (boxes > 0 || noRoom > 0)
                GunsmithMod.Log("retired gunsmith's courier brought " + boxes + " blind boxes, " + noRoom + " without room");
            return noRoom;
        }

        // ⑤ The carton's name and text by its kind.
        internal static void Label(string kind, out string name, out string text)
        {
            if (kind != null && kind.StartsWith(GunPrefix, StringComparison.Ordinal))
            {
                string cls = Text.ClassName(kind.Substring(GunPrefix.Length));
                name = LincolnTexts.GunBoxName.Format(cls);
                text = LincolnTexts.GunBoxDescription.Format(cls);
            }
            else if (kind == Materials)
            {
                name = LincolnTexts.MaterialBoxName.Get();
                text = LincolnTexts.MaterialBoxDescription.Get();
            }
            else
            {
                name = LincolnTexts.PartsBoxName.Get();
                text = LincolnTexts.PartsBoxDescription.Get();
            }
        }

        // The call on 4867 (7-1).
        internal static Dialogue Call(StorePhoneClient client)
        {
            Phone.EnsureChoiceBubbles();
            PhoneMenus.Clear();
            var root = new Dialogue();
            if (OrderedThisWeek() >= WeeklyLimit)
            {
                root.SetText(Phone.Name(), LincolnTexts.RetiredOpenWeekDone.Get());
                PhoneMenus.AddChoice(root, LincolnTexts.RetiredWeekDoneReply.Get(),
                    line => line.NextDialogue().SetText(Phone.Name(), LincolnTexts.RetiredHangUpShort.Get()).SetEndAction(PhoneMenus.EndCall));
                return root;
            }
            root.SetText(Phone.Name(), LincolnTexts.RetiredOpen.Get());
            Top(root);
            return root;
        }

        private static void Top(Dialogue page)
        {
            PhoneMenus.AddChoice(page, LincolnTexts.RetiredAskSpare.Get(), line => Spare(line.NextDialogue().SetText(Phone.Name(), LincolnTexts.RetiredSpareReply.Get())));
            PhoneMenus.AddChoice(page, LincolnTexts.RetiredAskMaterials.Get(), line => MaterialsQuote(line.NextDialogue()));
            PhoneMenus.AddChoice(page, LincolnTexts.RetiredBye.Get(),
                line => line.NextDialogue().SetText(Phone.Name(), LincolnTexts.RetiredHangUp.Get()).SetEndAction(PhoneMenus.EndCall));
        }

        private static void Spare(Dialogue page)
        {
            PhoneMenus.AddChoice(page, LincolnTexts.RetiredParts.Get(), line => PartsQuote(line.NextDialogue()));
            PhoneMenus.AddChoice(page, LincolnTexts.RetiredGuns.Get(), line => Guns(line.NextDialogue()));
            PhoneMenus.AddChoice(page, LincolnTexts.RetiredBack.Get(), line => Top(line.NextDialogue().SetText(Phone.Name(), LincolnTexts.RetiredMaterialsNoReply.Get())));
        }

        private static void PartsQuote(Dialogue page)
        {
            int price = PartsPrice();
            page.SetText(Phone.Name(), LincolnTexts.RetiredPartsQuote.Format(price));
            PhoneMenus.AddChoice(page, LincolnTexts.RetiredPartsYes.Get(), line =>
            {
                bool placed = PlayerStore.Instance != null && PlayerStore.Instance.GetCash() >= price && Place(Parts, DrawParts(), price);
                line.NextDialogue().SetText(Phone.Name(), (placed ? LincolnTexts.RetiredPartsDone : LincolnTexts.RetiredPartsNoMoney).Get())
                    .SetEndAction(PhoneMenus.EndCall);
            });
            PhoneMenus.AddChoice(page, LincolnTexts.RetiredPartsNo.Get(), line => Top(line.NextDialogue().SetText(Phone.Name(), LincolnTexts.RetiredPartsNoReply.Get())));
        }

        private static void MaterialsQuote(Dialogue page)
        {
            int price = MaterialsPrice();
            page.SetText(Phone.Name(), LincolnTexts.RetiredMaterialsQuote.Format(price));
            PhoneMenus.AddChoice(page, LincolnTexts.RetiredMaterialsYes.Get(), line =>
            {
                bool placed = PlayerStore.Instance != null && PlayerStore.Instance.GetCash() >= price && Place(Materials, DrawMaterials(), price);
                line.NextDialogue().SetText(Phone.Name(), (placed ? LincolnTexts.RetiredMaterialsDone : LincolnTexts.RetiredMaterialsNoMoney).Get())
                    .SetEndAction(PhoneMenus.EndCall);
            });
            PhoneMenus.AddChoice(page, LincolnTexts.RetiredMaterialsNo.Get(),
                line => Top(line.NextDialogue().SetText(Phone.Name(), LincolnTexts.RetiredMaterialsNoReply.Get())));
        }

        // Without a weapon permit he sells no gun and the player goes back to the choice of parts or guns (340 ④).
        private static void Guns(Dialogue page)
        {
            int permit = GunSupply.PermitTop();
            List<string> classes = permit >= 1 ? GunClasses(permit) : new List<string>();
            if (classes.Count == 0)
            {
                page.SetText(Phone.Name(), LincolnTexts.RetiredGunsNoPermit.Get());
                PhoneMenus.AddChoice(page, LincolnTexts.RetiredGunsNoPermitBack.Get(),
                    line => Spare(line.NextDialogue().SetText(Phone.Name(), LincolnTexts.RetiredSpareReply.Get())));
                return;
            }
            page.SetText(Phone.Name(), LincolnTexts.RetiredGunsAsk.Get());
            var entries = new List<PhoneMenus.Entry>();
            foreach (string c in classes)
            {
                string cls = c;
                entries.Add(new PhoneMenus.Entry
                {
                    MakeLabel = () => LincolnTexts.RetiredGunsChoice.Format(Text.ClassName(cls), GunPrice(permit, cls)),
                    Pick = line =>
                    {
                        int price = GunPrice(permit, cls);
                        string gun = DrawGun(permit, cls);
                        bool placed = price > 0 && gun != null && PlayerStore.Instance != null && PlayerStore.Instance.GetCash() >= price
                            && Place(GunPrefix + cls, new List<string> { gun }, price);
                        line.NextDialogue().SetText(Phone.Name(), placed ? LincolnTexts.RetiredGunsDone.Format(Text.ClassName(cls)) : LincolnTexts.RetiredGunsNoMoney.Get())
                            .SetEndAction(PhoneMenus.EndCall);
                    },
                });
            }
            GunPage(page, entries, 0);
        }

        private static void GunPage(Dialogue page, List<PhoneMenus.Entry> entries, int start)
        {
            PhoneMenus.ShowList(page, entries, start, LincolnTexts.RetiredGunsMore.Get(), (line, next) =>
                GunPage(line.NextDialogue().SetText(Phone.Name(), LincolnTexts.RetiredGunsAsk.Get()), entries, Math.Max(0, next)),
                false, LincolnTexts.RetiredGunsNo.Get(), line => Top(line.NextDialogue().SetText(Phone.Name(), LincolnTexts.RetiredMaterialsNoReply.Get())));
        }
    }
}
