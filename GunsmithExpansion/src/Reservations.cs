using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Ue = UnityEngine;

namespace GunsmithExpansion
{
    // Reservation orders and appointments (final-strategy 14, 24, 26, 32). The gunsmith sign's customers (GunsmithSign; until 0.6.2, 15% of
    // the game's gun buyers) may come to reserve one of this mod's guns: they name the gun and when they will pick it up (tomorrow, the day after
    // or next week) and pay a 30% deposit when the store takes the order. On that day they come back by appointment
    // (PlayerStore.QueueFuturClient, on top of the day's other customers) and pay the rest. If the day ends without the gun, the deposit goes back
    // to them and the store loses reputation with their faction.
    // From 0.7 (item 19; the numbers of GPT's reply, with Agent's change to the checks, VVittgenstein 2026-11-06, current/008 quote 37; Xiaoheihe comment B):
    //   - The total is the price a buyer pays for the model in good condition (GoodPrice) plus the premium, fixed when the store takes the order; the deposit is 30% of the total, in tens.
    //     Until 0.6.2 it was the gun's value plus that premium, so a gun picked up next week paid less than a printed gun, which is new, pays any buyer (288 against 351 on hard for a gun of value 240).
    //   - The gun has to be of the model, in good condition or better, with every fault fully repaired; until 0.6.2 only the model counted. A reservation taken before 0.7 keeps its old terms (Strict).
    //   - Nobody reserves a gun the store has, in any condition (VVittgenstein 2026-11-06 10:08, current/008 quote 46). Two kinds of order (the same message):
    //       AnyGunPercent of them: a gun of the level of the store's highest weapon permit, for next week, plus 50% (
    //       VVittgenstein 2026-11-06 10:26, quote 47). The store may hold the gun's chip (VVittgenstein 2026-11-07 17:04, current/T-006 quote 11). With no such gun the second kind comes instead.
    //       The others: a gun the store can make (CanMake), level 1 in 2, 3 or 7 days, level 2 in 3, 4 or 7, level 3 in 4, 5 or 7, plus 50%, 35% or 20% (Agent's proposal,
    //       VVittgenstein 2026-11-06 16:47, quote 52; his first days were 1/3/7, 3/5/7, 5/7, quote 47). He may name any of the three days, each as likely: the days are not matched to the nights the store
    //       needs (T-006 quote 11), and a gun is named even when the store lacks its metal ingots and has no furnace (the same quote). The middle day is what a printer with
    //       no module that raises speed takes (24 points a night for level 1, 18 for levels 2 and 3 with the CNC module: 3, 4 and 5 nights); the first is a night less, which takes such a module or a frame
    //       printed before; the last is a week.
    //     Metal ingots, filament, gun parts and the printer's other work are not looked at. With nothing the store can make, the first kind comes instead (VVittgenstein 2026-11-07 20:06,
    //     current/T-006 quote 20; until then nobody came to reserve, and with weapon permit I only a scavenger who says "..." came, GunsmithSign.CreateCustomer).
    // Until 0.6.2 customers whose gun was repaired came back the same way after 3 to 6 days with another gun, and each
    // return booked the next. From 0.7 a customer comes back only when every fault of his gun was fully repaired, and then
    // ReturnPercent of the time: he says so and his faction's reputation goes up (VVittgenstein 2026-11-06 02:18, current/008 quote 21, item 75).
    // The reputation is what water and chemicals better than promised earn (WaterFeatureHelper, ChemicalProductHelper: 3).
    // Agent's decisions: 25%, the line.
    // Open reservations are kept in PlayerStore.modData; how many the store can hold at once comes from the workshop
    // reputation (Workshop.MaxReservations, 2 to 5). Agent's decisions: the deposit rounded to 10
    // credits, 3 reputation lost, the tier of the gun by the weapon permit (RepairOrders.PickTier), only guns that have a printer chip.
    internal static class Reservations
    {
        internal const int DepositPercent = 30;
        internal const int ReputationLoss = 3;
        internal const int ReturnPercent = 25;
        internal const int ReturnReputation = 3;
        internal const string PickupPrefix = "gseReservationPickup";
        internal const string RepairReturnPrefix = "gseRepairReturn";
        private const string Key = "reservations";
        private static readonly Random rng = new Random();
        private static readonly ConditionalWeakTable<GunOrderData, Reservation> pickups = new ConditionalWeakTable<GunOrderData, Reservation>();
        internal static readonly string[] Kinds = { "LL", "Merc", "UL" };
        internal const int AnyGunPercent = 25;
        internal const int AnyGunPremium = 50;
        internal const int NextWeek = 7;
        // By level: the days a customer of the second kind may give; what he adds goes by the day's place in the list.
        private static readonly int[][] MakeDays = { new[] { 2, 3, 7 }, new[] { 3, 4, 7 }, new[] { 4, 5, 7 } };
        private static readonly int[] MakePremiums = { 50, 35, 20 };

        internal sealed class Reservation
        {
            internal int Id;
            internal string Gun;
            internal int Nights;
            internal int Price;
            internal int Deposit;
            internal int Premium;
            internal string Kind;
            internal string Faction;
            // taken from 0.7 on: good condition and no fault are required
            internal bool Strict;
        }

        // Keep the reservation on the order object as well as in ModData. Snapshot saves preserve instance fields;
        // a ConditionalWeakTable entry alone is lost when they restore a new order object.
        internal sealed class PickupOrder : GunOrderData
        {
            internal Reservation Reservation;
        }

        internal static void Register()
        {
            ReservationPickupLayout.Enable();
            foreach (string kind in Kinds)
            {
                string k = kind;
                StoreClientListDict.storeClientDict[PickupPrefix + k] = () => CreatePickup(k);
                StoreClientListDict.storeClientDict[RepairReturnPrefix + k] = () => CreateRepairReturn(k);
            }
        }

        // Tomorrow: the reservations taken before this change.
        internal static string When(int days)
        {
            return days <= 1 ? Text.Pick("明天", "tomorrow") : days == 2 ? Text.Pick("后天", "the day after tomorrow")
                : days >= NextWeek ? Text.Pick("下个星期", "next week") : string.Format(Text.Pick("{0} 天后", "in {0} days"), days);
        }

        internal static List<Reservation> All()
        {
            var list = new List<Reservation>();
            string text = ModHelper.GetModData(GunsmithMod.ModId, Key, "");
            foreach (string entry in (text ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] f = entry.Split(',');
                int id, nights, price, deposit, premium;
                if (f.Length < 8 || !int.TryParse(f[0], out id) || !int.TryParse(f[2], out nights) || !int.TryParse(f[3], out price)
                    || !int.TryParse(f[4], out deposit) || !int.TryParse(f[5], out premium))
                    continue;
                list.Add(new Reservation { Id = id, Gun = f[1], Nights = nights, Price = price, Deposit = deposit, Premium = premium, Kind = f[6], Faction = f[7], Strict = f.Length > 8 && f[8] == "1" });
            }
            return list;
        }

        private static void Save(List<Reservation> list)
        {
            var entries = new List<string>();
            foreach (Reservation r in list)
            {
                entries.Add(string.Join(",", new[]
                {
                    r.Id.ToString(CultureInfo.InvariantCulture), r.Gun, r.Nights.ToString(CultureInfo.InvariantCulture),
                    r.Price.ToString(CultureInfo.InvariantCulture), r.Deposit.ToString(CultureInfo.InvariantCulture),
                    r.Premium.ToString(CultureInfo.InvariantCulture), r.Kind, r.Faction, r.Strict ? "1" : "0",
                }));
            }
            ModHelper.SetModData(GunsmithMod.ModId, Key, string.Join(";", entries.ToArray()));
        }

        internal static int Due(Reservation r)
        {
            return r.Price - r.Deposit + r.Price * r.Premium / 100;
        }

        // A task of the gunsmith sign's customers (GunsmithSign.CreateCustomer), while the store has room for another reservation and there is a gun to reserve.
        internal static bool CanOffer()
        {
            if (PlayerStore.Instance == null || All().Count >= Workshop.MaxReservations())
                return false;
            Shop shop = Shop.Now();
            return OfPermitLevel(shop).Count > 0 || Makeable(null, shop).Count > 0;
        }

        // What a buyer pays for a gun of this model in good condition: the gun's value, the game's markup for a restricted gun with its permit (half of the store's contraband markup,
        // ItemFeatureList.RestrictedLowMarkUp and so on: 17, 35, 50%) and the difficulty's retail markup (0 to 20%). Agent's reading: the two markups are added.
        internal static int GoodPrice(GunDef d)
        {
            PlayerStore store = PlayerStore.Instance;
            int restricted = store == null ? 0 : (d.Tier == "一档" ? store.contrabandMarkupLow : d.Tier == "二档" ? store.contrabandMarkupMid : store.contrabandMarkupHigh) / 2;
            int retail = Difficulty.Rules != null ? Difficulty.Rules.retailMarkup : 0;
            return (int)Math.Round(d.Value * (100 + restricted + retail) / 100.0);
        }

        // What the store has to make guns with, as it stands now.
        private sealed class Shop
        {
            internal readonly HashSet<string> Owned = new HashSet<string>();
            internal readonly List<GameItem> Printers = new List<GameItem>();

            internal static Shop Now()
            {
                var shop = new Shop();
                foreach (GameItem item in EmporiumEntry.Instance != null ? EmporiumEntry.Instance.GetAllOwnedItems(false, true) : new List<GameItem>())
                {
                    if (item == null || item.identifier == null)
                        continue;
                    shop.Owned.Add(item.identifier);
                    if (item.identifier == "printer")
                        shop.Printers.Add(item);
                }
                return shop;
            }
        }

        // The guns a reservation can name: of the guns with a printer chip (final-strategy 26), those the store does not have, of a tier the weapon permits cover.
        private static List<GunDef> Open(string tier, Shop shop)
        {
            int top = Math.Max(1, Ammo.PermitTop());
            var guns = new List<GunDef>();
            foreach (GunDef d in Catalog.Guns)
            {
                if (Production.HasChip(d) && Production.Level(d) <= top && (tier == null || d.Tier == tier) && !shop.Owned.Contains(d.Id))
                    guns.Add(d);
            }
            return guns;
        }

        // The first kind of order: a gun of the level of the store's highest weapon permit, whether or not the store has its chip.
        private static List<GunDef> OfPermitLevel(Shop shop)
        {
            return Open(RepairOrders.TierName(Math.Max(1, Ammo.PermitTop())), shop);
        }

        // Whether the store can make this gun (quote 46): with the gun's frame in the store, which a gun part finishes; else with its chip and a printer that can print it: for tiers two
        // and three the metal printing and CNC modules somewhere in the store, and for tier three a printer with a module that raises quality (Production.CreateChip, Precision). The metal ingots, the
        // furnaces and the nights the printing takes are not looked at (T-006 quote 11).
        private static bool CanMake(GunDef d, Shop shop)
        {
            if (shop.Owned.Contains(Production.FrameId(d)))
                return true;
            if (!shop.Owned.Contains(Production.ChipId(d)))
                return false;
            int level = Production.Level(d);
            if (level >= 2 && (!shop.Owned.Contains("printer_module_metal") || !shop.Owned.Contains("printer_module_cnc")))
                return false;
            foreach (GameItem printer in shop.Printers)
            {
                if (level < 3 || Precision.HasQualityModule(printer))
                    return true;
            }
            return false;
        }

        // The day a customer of the second kind gives, by the gun's level and the day's place in MakeDays.
        internal static int MakeDay(int level, int choice)
        {
            return MakeDays[level - 1][choice];
        }

        // The second kind: of one tier or, with tier null, of every tier, what the store can make.
        private static List<GunDef> Makeable(string tier, Shop shop)
        {
            var can = new List<GunDef>();
            foreach (GunDef d in Open(tier, shop))
            {
                if (CanMake(d, shop))
                    can.Add(d);
            }
            return can;
        }

        internal static StoreClient CreateSignCustomer()
        {
            Shop shop = Shop.Now();
            GunDef gun;
            int days, premium;
            List<GunDef> guns = rng.Next(100) < AnyGunPercent ? OfPermitLevel(shop) : null;
            List<GunDef> makeable = null;
            if (guns == null || guns.Count == 0)
            {
                makeable = Makeable(RepairOrders.TierName(RepairOrders.PickTier()), shop);
                if (makeable.Count == 0)
                    makeable = Makeable(null, shop);
                // with nothing the store can make, the first kind (VVittgenstein 2026-11-07 20:06, current/T-006 quote 20)
                if (makeable.Count == 0)
                    guns = OfPermitLevel(shop);
            }
            if (makeable == null || makeable.Count == 0)
            {
                if (guns == null || guns.Count == 0)
                    return null;
                gun = guns[rng.Next(guns.Count)];
                days = NextWeek;
                premium = AnyGunPremium;
            }
            else
            {
                gun = makeable[rng.Next(makeable.Count)];
                int pick = rng.Next(MakePremiums.Length);
                days = MakeDay(Production.Level(gun), pick);
                premium = MakePremiums[pick];
            }
            string kind = GunsmithSign.PickKind(Ammo.PermitTop());
            return CreateOffer(GunsmithSign.Look(kind), kind, gun, days, premium);
        }

        internal static StoreClient CreateOffer(StoreClient look, string kind, GunDef gun, int days, int premium)
        {
            var client = new StoreClient();
            client.identifier = "gseReservationOffer";
            client.displayName = look.displayName;
            client.spriteName = look.spriteName;
            client.clientFaction = look.clientFaction;
            client.clientIntent = StoreClient.ClientIntent.DIALOGUE;
            client.isBargainAvailable = false;
            client.CanReturn = false;
            int price = GoodPrice(gun);
            int total = price + price * premium / 100;
            int deposit = (int)Math.Round(total * DepositPercent / 1000.0) * 10;
            string gunName = Text.Pick(gun.NameZh, gun.NameEn);
            string name = client.displayName;
            Dialogue offer = client.mainDialogue.SetText(name, string.Format(Text.Pick(
                "我想订一把{0}，{1}来取，成色良好以上，故障全部修好。一共 {2} 信用点，先付三成定金 {3}，余款取货时付。",
                "I'd like to reserve a {0}, I'll pick it up {1}: good condition or better, with every fault repaired. {2} credits in all, a 30% deposit of {3} now and the rest when I pick it up."),
                gunName, When(days), total, deposit));
            Dialogue take = new Dialogue().SetText("PLAYER", string.Format(Text.Pick("行，{0}来取。", "Deal. Come {0}."), When(days)));
            take.SetNextDialogue(new Dialogue().SetText(name, Text.Pick("那就说定了。", "It's a deal, then.")).SetEndAction(Leave));
            offer.AddChoice(DirectoryMaster.Item("$", true), "take", "", take, () => Accept(gun, days, price, deposit, premium, kind, look.clientFaction));
            Dialogue no = new Dialogue().SetText("PLAYER", Text.Pick("这个我做不了。", "I can't do that one."));
            no.SetNextDialogue(new Dialogue().SetText(name, Text.Pick("那算了。", "Never mind, then.")).SetEndAction(Leave));
            offer.AddChoice(DirectoryMaster.Item("$", true), "no", "", no, null);
            client.CompleteClientCreation(false);
            GunsmithMod.Log("reservation customer for " + gun.Id + " in " + days + " days");
            return client;
        }

        internal static void Accept(GunDef gun, int days, int price, int deposit, int premium, string kind, string faction)
        {
            if (PlayerStore.Instance == null)
                return;
            List<Reservation> list = All();
            int id = ModHelper.GetModDataInt(GunsmithMod.ModId, "reservation_next", 1);
            ModHelper.SetModDataInt(GunsmithMod.ModId, "reservation_next", id + 1);
            var reservation = new Reservation { Id = id, Gun = gun.Id, Nights = days, Price = price, Deposit = deposit, Premium = premium, Kind = kind, Faction = faction ?? "", Strict = true };
            list.Add(reservation);
            Save(list);
            PlayerStore.Instance.ModCash(deposit, true);
            PlayerStore.Instance.QueueFuturClient(PickupPrefix + kind, days);
            OrderSlips.Reservation(gun, days, deposit, Due(reservation));
            GunsmithMod.Log("reservation " + id + ": " + gun.Id + " in " + days + " days, deposit " + deposit + ", premium " + premium + "%");
        }

        internal static void Leave()
        {
            if (DialogUIManager.Instance != null)
                DialogUIManager.Instance.CurrentClientLeave(1f);
        }

        private static StoreClient Buyer(string kind, GunOrderData order)
        {
            switch (kind)
            {
                case "Merc": return StoreClientListGun.CreateMercGunBuyer(order);
                case "UL": return StoreClientListGun.CreateULBuyer(order);
                default: return StoreClientListGun.CreateLLGunBuyer(order);
            }
        }

        // The customer who comes for a reservation due today: a gun order for the model, in good condition with every fault repaired (Strict).
        internal static StoreClient CreatePickup(string kind)
        {
            SyncAssignments();
            List<Reservation> list = All();
            Reservation due = null;
            foreach (Reservation r in list)
            {
                if (r.Nights <= 0 && r.Kind == kind && !assigned.Contains(r.Id))
                {
                    due = r;
                    break;
                }
            }
            if (due == null)
                return Nobody();
            return CreatePickup(due);
        }

        private static StoreClient CreatePickup(Reservation due)
        {
            assigned.Add(due.Id);
            GunDef gun = RepairOrders.Find(due.Gun);
            var order = new PickupOrder { Reservation = due };
            order.tier = gun != null && gun.Tier == "三档" ? 3 : gun != null && gun.Tier == "二档" ? 2 : 1;
            order.mandatoryRequirements.Add(Requirements.Model(due.Gun));
            if (due.Strict)
            {
                order.mandatoryRequirements.Add(Requirements.Condition(2));
                order.mandatoryRequirements.Add(Requirements.NoFaults());
            }
            order.baseReward = Due(due);
            order.dialogText = string.Format(Text.Pick("我来取预订的{0}。余款一共 {1} 信用点。", "I'm here for the {0} I reserved. The rest comes to {1} credits."),
                gun != null ? Text.Pick(gun.NameZh, gun.NameEn) : due.Gun, Due(due));
            pickups.Remove(order);
            pickups.Add(order, due);
            StoreClient client = Buyer(due.Kind, order);
            // Identifier-only saves must recreate a pickup, not the ordinary gun buyer used for its appearance.
            client.identifier = PickupPrefix + due.Kind;
            client.CanReturn = false;
            // Like the rent collector, the bell cannot dismiss this customer. The explicit unavailable-item
            // choice below cancels the reservation; the ordinary gun-order window remains available for trading.
            client.dismissable = false;
            client.mainDialogue.SetText(client.displayName, Text.Pick("我来取之前预订的枪。", "I'm here to collect my reserved gun."));
            client.mainDialogue.priority = 3;
            Dialogue request = client.mainDialogue.nextDialogue;
            request.priority = 3; // The game's acceptance dialogue has priority 4 and must be able to replace this choice.
            Dialogue unavailable = new Dialogue().SetText("PLAYER", Text.Pick("我现在没有你要的东西", "I don't have what you need right now."));
            unavailable.NextDialogue().SetText(client.displayName, Text.Pick("好吧", "Okay.")).SetEndAction(Leave);
            request.AddChoice(DirectoryMaster.Item("slip", true), unavailable.plainText,
                string.Format(Text.Pick("退还 {0} 信用点定金，派系声望降低 {1}。",
                    "Refund the {0}-credit deposit; faction reputation decreases by {1}."), due.Deposit, ReputationLoss), unavailable,
                () => { if (IsOpenPickup(client.gunOrderData)) Cancel(client.gunOrderData); },
                () => IsOpenPickup(client.gunOrderData));
            if (client.acceptDealDialogue != null)
                client.acceptDealDialogue.SetText(client.displayName, Text.Pick("这个就是我要的", "That's exactly what I wanted."));
            client.tryInterceptDismiss = () => BlockDismiss(client);
            GunsmithMod.Log("reservation " + due.Id + " pickup for " + due.Gun + ", due " + Due(due));
            return client;
        }

        private static readonly HashSet<int> assigned = new HashSet<int>();
        private static PlayerStore assignedStore;
        private static bool recoverPickupsAfterLoad;
        private static readonly ConditionalWeakTable<GunOrderData, GunOrderData> pleased = new ConditionalWeakTable<GunOrderData, GunOrderData>();

        internal static void OnGameLoading()
        {
            ReservationPickupLayout.Restore();
            assigned.Clear();
            assignedStore = null;
            recoverPickupsAfterLoad = true;
        }

        private static HashSet<int> ActivePickups()
        {
            var ids = new HashSet<int>();
            PlayerStore store = PlayerStore.Instance;
            if (store == null)
                return ids;
            StoreClient current = store.currentClientInstance != null ? store.currentClientInstance.GetClientBlueprint() : null;
            Reservation reservation = current != null ? PickupOf(current.gunOrderData) : null;
            if (reservation != null)
                ids.Add(reservation.Id);
            if (store.storeClientManager != null && store.storeClientManager.clientStack != null)
            {
                foreach (StoreClient client in store.storeClientManager.clientStack)
                {
                    reservation = client != null ? PickupOf(client.gunOrderData) : null;
                    if (reservation != null)
                        ids.Add(reservation.Id);
                }
            }
            return ids;
        }

        // Exact snapshots restore orders without calling our factories. Seed their IDs before assigning another
        // customer of the same kind; factory-created objects are still protected before they enter the live queue.
        private static void SyncAssignments()
        {
            if (!ReferenceEquals(assignedStore, PlayerStore.Instance))
            {
                assigned.Clear();
                assignedStore = PlayerStore.Instance;
            }
            assigned.UnionWith(ActivePickups());
        }

        // Old F5 saves may have stored a pickup as ll_gun_buyer/merc_gun_buyer/ul_gun_buyer after the appointment
        // was consumed. Rebuild only due reservations with no live pickup or due future appointment. The ordinary
        // buyer cannot be distinguished from a real ordinary buyer and is kept. This runs once after F5 restoration.
        internal static int RecoverMissingPickupsAfterLoad()
        {
            if (!recoverPickupsAfterLoad || PlayerStore.Instance == null || PlayerStore.Instance.storeClientManager == null)
                return 0;
            recoverPickupsAfterLoad = false;
            PlayerStore store = PlayerStore.Instance;
            if (store.storeState != PlayerStore.StoreState.OPEN)
                return 0;
            SyncAssignments();
            HashSet<int> covered = ActivePickups();
            var future = new Dictionary<string, int>();
            if (store.futurStoreClientIdQueue != null && store.futurStoreClientDayQueue != null)
            {
                int count = Math.Min(store.futurStoreClientIdQueue.Count, store.futurStoreClientDayQueue.Count);
                for (int i = 0; i < count; i++)
                {
                    string id = store.futurStoreClientIdQueue[i];
                    if (id == null || !id.StartsWith(PickupPrefix, StringComparison.Ordinal) || store.futurStoreClientDayQueue[i] > 0)
                        continue;
                    string kind = id.Substring(PickupPrefix.Length);
                    int old;
                    future.TryGetValue(kind, out old);
                    future[kind] = old + 1;
                }
            }
            int restored = 0;
            foreach (Reservation reservation in All())
            {
                if (reservation.Nights > 0 || covered.Contains(reservation.Id) || Array.IndexOf(Kinds, reservation.Kind) < 0
                    || RepairOrders.Find(reservation.Gun) == null)
                    continue;
                int waiting;
                if (future.TryGetValue(reservation.Kind, out waiting) && waiting > 0)
                {
                    future[reservation.Kind] = waiting - 1;
                    covered.Add(reservation.Id);
                    continue;
                }
                StoreClient client = CreatePickup(reservation);
                store.storeClientManager.AddClient(client);
                if (store.storeClientManager.clientStack.Contains(client))
                {
                    covered.Add(reservation.Id);
                    restored++;
                    GunsmithMod.Log("restored missing pickup for reservation " + reservation.Id);
                }
                else
                {
                    assigned.Remove(reservation.Id);
                    GunsmithMod.Warn("missing pickup could not enter the queue for reservation " + reservation.Id);
                }
            }
            return restored;
        }

        internal static Reservation PickupOf(GunOrderData order)
        {
            PickupOrder pickup = order as PickupOrder;
            if (pickup != null)
                return pickup.Reservation;
            Reservation r;
            return order != null && pickups.TryGetValue(order, out r) ? r : null;
        }

        private static bool IsOpenPickup(GunOrderData order)
        {
            Reservation reservation = PickupOf(order);
            return reservation != null && All().Exists(r => r.Id == reservation.Id);
        }

        // The public player-dismiss path consumes this callback before invoking it. Re-arm it without changing
        // the conversation, negotiation window, money or order, including when another mod calls that path directly.
        internal static bool BlockDismiss(StoreClient client)
        {
            if (client == null || !IsOpenPickup(client.gunOrderData))
                return false;
            client.tryInterceptDismiss = () => BlockDismiss(client);
            return true;
        }

        internal static bool Cancel(GunOrderData order)
        {
            Reservation reservation = PickupOf(order);
            if (reservation == null)
                return false;
            List<Reservation> list = All();
            Reservation current = list.Find(r => r.Id == reservation.Id);
            if (current == null)
                return false;
            list.Remove(current);
            Save(list);
            // Selecting the cancellation and the offer button can happen before the departure dialogue finishes.
            // Close this customer's trade immediately, while keeping any gun placed by the player owned by them.
            StoreClient client = PlayerStore.Instance != null && PlayerStore.Instance.currentClientInstance != null
                ? PlayerStore.Instance.currentClientInstance.GetClientBlueprint() : null;
            if (client != null && client.gunOrderData == order)
            {
                if (!client.isBusinessComplete)
                    client.CompleteBusiness();
                if (NegociationUIManager.Instance != null)
                    NegociationUIManager.Instance.CloseUI();
            }
            SettleFailure(current, "cancelled");
            return true;
        }

        private static void SettleFailure(Reservation reservation, string reason)
        {
            assigned.Remove(reservation.Id);
            if (PlayerStore.Instance != null)
                PlayerStore.Instance.ModCash(-reservation.Deposit, true);
            StoreReputation rep = !string.IsNullOrEmpty(reservation.Faction) ? StoreReputation.GetStoreReputation(reservation.Faction) : null;
            if (rep != null)
                StoreReputation.ModReputation(reservation.Faction, -ReputationLoss, true);
            GunsmithMod.Log("reservation " + reservation.Id + " for " + reservation.Gun + " " + reason + ": deposit " + reservation.Deposit
                + " returned, " + reservation.Faction + " reputation -" + ReputationLoss);
        }

        internal static void OnOrderAccepted(GunOrderData order)
        {
            Reservation r = PickupOf(order);
            if (r != null)
            {
                List<Reservation> list = All();
                list.RemoveAll(x => x.Id == r.Id);
                Save(list);
                assigned.Remove(r.Id);
                if (DialogUIManager.Instance != null)
                    DialogUIManager.Instance.HideChoiceBubble();
                GunsmithMod.Log("reservation " + r.Id + " delivered");
                return;
            }
            TryBookReturn(order);
        }

        // A customer with every fault of every gun fully repaired may come back with another gun in 3 to 6 days.
        private static void TryBookReturn(GunOrderData order)
        {
            if (order == null || !order.isServiceOrder || PlayerStore.Instance == null || PlayerStore.Instance.currentClientInstance == null)
                return;
            GameItem gun = order.GetPlacedServiceGun();
            StoreClient client = PlayerStore.Instance.currentClientInstance.GetClientBlueprint();
            if (client == null || !Faults.IsModGun(gun) || !gun.IsTag(Repair.ServiceTag) || !RepairOrders.DoneAndAllFixed(order, gun) || rng.Next(100) >= ReturnPercent)
                return;
            int days = 3 + rng.Next(4);
            PlayerStore.Instance.QueueFuturClient(RepairReturnPrefix + RepairOrders.KindOf(order), days);
            if (!string.IsNullOrEmpty(client.clientFaction))
                StoreReputation.ModReputation(client.clientFaction, ReturnReputation, true);
            pleased.Remove(order);
            pleased.Add(order, order);
            GunsmithMod.Log("repair customer comes back in " + days + " days");
        }

        internal static bool IsPleased(GunOrderData order)
        {
            GunOrderData o;
            return order != null && pleased.TryGetValue(order, out o);
        }

        // Each night (StorePhoneClient.OnEndDay): a reservation whose day has passed without the gun is called off.
        internal static void OnNight()
        {
            List<Reservation> list = All(), keep = new List<Reservation>(), expired = new List<Reservation>();
            foreach (Reservation r in list)
            {
                if (r.Nights > 0)
                {
                    r.Nights--;
                    keep.Add(r);
                    continue;
                }
                expired.Add(r);
            }
            Save(keep);
            foreach (Reservation reservation in expired)
                SettleFailure(reservation, "not delivered");
        }

        internal static StoreClient CreateRepairReturn(string kind)
        {
            return RepairOrders.CreateCustomer(kind, Certificates.PickTier(), true) ?? Nobody();
        }

        // Comes when the record it was queued for is gone: says nothing and leaves.
        internal static StoreClient Nobody()
        {
            var client = new StoreClient();
            client.identifier = "gseNobody";
            client.displayName = LocHelper.GetLocalizedClientName("name_scavenger");
            client.spriteName = SpriteDict.GetRandomLowerLevelSpriteName();
            client.clientFaction = "FACTION_LOWER_LEVEL";
            client.clientIntent = StoreClient.ClientIntent.DIALOGUE;
            client.mainDialogue.SetText(client.displayName, "...").SetEndAction(Leave);
            client.CompleteClientCreation(false);
            return client;
        }
    }

    // Only the active pickup request shares the screen with the negotiation tablet. Keep its existing dialogue
    // and button widgets, moving them by measured screen bounds; restore them before another page/customer uses them.
    internal static class ReservationPickupLayout
    {
        private sealed class Original
        {
            internal Ue.RectTransform Rect;
            internal Ue.Vector3 Position;
            internal Ue.Vector2 Size;
            internal float Height;
            internal TMPro.TMP_Text Label;
            internal string Text;
            internal string Wrapped;
            internal float WrapWidth = -1f;
            internal float FontSize;
            internal TMPro.TMP_FontAsset Font;
            internal float ExtraHeight;
        }
        private static readonly List<Original> originals = new List<Original>();
        private static DialogUIManager owner;
        private static Dialogue page;
        private static bool enabled = true;
        private static float bubbleScale;

        internal static void Enable() { enabled = true; }
        internal static void Disable() { enabled = false; Restore(); }

        private static Ue.Camera Camera(Ue.RectTransform rect)
        {
            Ue.Canvas canvas = rect.GetComponentInParent<Ue.Canvas>();
            if (canvas != null) canvas = canvas.rootCanvas;
            return canvas == null || canvas.renderMode == Ue.RenderMode.ScreenSpaceOverlay ? null
                : canvas.worldCamera != null ? canvas.worldCamera : Ue.Camera.main;
        }

        internal static Ue.Rect ScreenBounds(Ue.RectTransform rect)
        {
            var corners = new Ue.Vector3[4];
            rect.GetWorldCorners(corners);
            Ue.Camera camera = Camera(rect);
            Ue.Vector2 low = Ue.RectTransformUtility.WorldToScreenPoint(camera, corners[0]), high = low;
            foreach (Ue.Vector3 corner in corners)
            {
                Ue.Vector2 point = Ue.RectTransformUtility.WorldToScreenPoint(camera, corner);
                low = Ue.Vector2.Min(low, point); high = Ue.Vector2.Max(high, point);
            }
            return Ue.Rect.MinMaxRect(low.x, low.y, high.x, high.y);
        }

        private static Original Remember(Ue.RectTransform rect, TMPro.TMP_Text label)
        {
            if (rect == null) return null;
            Original found = originals.Find(value => value.Rect == rect);
            if (found != null) return found;
            var saved = new Original { Rect = rect, Position = rect.localPosition, Size = rect.sizeDelta,
                Height = rect.rect.height, Label = label, Text = label != null ? label.text : null };
            originals.Add(saved); return saved;
        }

        private static void RestoreGeometry(Original saved, bool restoreText = true)
        {
            if (saved.Rect == null) return;
            saved.Rect.localPosition = saved.Position; saved.Rect.sizeDelta = saved.Size;
            if (restoreText && saved.Label != null && saved.Wrapped != null && saved.Label.text == saved.Wrapped)
                saved.Label.text = saved.Text;
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(saved.Rect);
        }

        internal static void Restore()
        {
            foreach (Original saved in originals) RestoreGeometry(saved);
            originals.Clear(); owner = null; page = null;
        }

        private static string Wrap(TMPro.TMP_Text label, string text, float width)
        {
            if (string.IsNullOrEmpty(text) || label.GetPreferredValues(text, float.PositiveInfinity, float.PositiveInfinity).x <= width) return text;
            var lines = new List<string>();
            foreach (string paragraph in text.Split('\n'))
            {
                string line = "";
                foreach (char character in paragraph)
                {
                    string next = line + character;
                    if (line.Length > 0 && label.GetPreferredValues(next, float.PositiveInfinity, float.PositiveInfinity).x > width)
                    {
                        int space = next.LastIndexOf(' ');
                        if (space > 0) { lines.Add(next.Substring(0, space).TrimEnd()); line = next.Substring(space + 1); }
                        else { lines.Add(line); line = character.ToString(); }
                    }
                    else line = next;
                }
                lines.Add(line);
            }
            return string.Join("\n", lines.ToArray());
        }

        private static void FitText(Original saved, float screenWidth)
        {
            if (saved == null || saved.Label == null || saved.Rect == null || string.IsNullOrEmpty(saved.Text)) return;
            Ue.RectTransform textRect = saved.Label.rectTransform;
            Ue.Rect textBounds = ScreenBounds(textRect), bubble = ScreenBounds(saved.Rect);
            float pixelsPerUnit = textRect.rect.width > 0f ? textBounds.width / textRect.rect.width : 1f;
            if (pixelsPerUnit <= 0f) return;
            float padding = Ue.Mathf.Max(0f, bubble.width - textBounds.width);
            float limit = Ue.Mathf.Max(20f, (screenWidth - padding) / pixelsPerUnit);
            if (saved.Wrapped == null || Ue.Mathf.Abs(saved.WrapWidth - limit) > 0.1f
                || !Ue.Mathf.Approximately(saved.FontSize, saved.Label.fontSize) || saved.Font != saved.Label.font)
            {
                saved.Wrapped = Wrap(saved.Label, saved.Text, limit);
                saved.ExtraHeight = Ue.Mathf.Max(0f, saved.Label.GetPreferredValues(saved.Wrapped, float.PositiveInfinity, float.PositiveInfinity).y
                    - saved.Label.GetPreferredValues(saved.Text, float.PositiveInfinity, float.PositiveInfinity).y);
                saved.WrapWidth = limit; saved.FontSize = saved.Label.fontSize; saved.Font = saved.Label.font;
            }
            saved.Label.text = saved.Wrapped;
            float textScale = textRect.rect.height > 0f ? textBounds.height / textRect.rect.height : pixelsPerUnit;
            float rootScale = saved.Rect.rect.height > 0f ? bubble.height / saved.Rect.rect.height : pixelsPerUnit;
            if (rootScale > 0f) saved.Rect.SetSizeWithCurrentAnchors(Ue.RectTransform.Axis.Vertical, saved.Height + saved.ExtraHeight * textScale / rootScale);
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(saved.Rect);
        }

        private static void Place(List<Ue.RectTransform> roots, Ue.Rect area)
        {
            roots.RemoveAll(rect => rect == null || !rect.gameObject.activeInHierarchy);
            if (roots.Count == 0) return;
            Ue.Rect bounds = ScreenBounds(roots[0]);
            foreach (Ue.RectTransform rect in roots)
            {
                Ue.Rect other = ScreenBounds(rect);
                bounds = Ue.Rect.MinMaxRect(Ue.Mathf.Min(bounds.xMin, other.xMin), Ue.Mathf.Min(bounds.yMin, other.yMin),
                    Ue.Mathf.Max(bounds.xMax, other.xMax), Ue.Mathf.Max(bounds.yMax, other.yMax));
            }
            Ue.Vector2 shift = new Ue.Vector2(Ue.Mathf.Clamp(bounds.xMin, area.xMin, Ue.Mathf.Max(area.xMin, area.xMax - bounds.width)) - bounds.xMin,
                Ue.Mathf.Clamp(bounds.yMin, area.yMin, Ue.Mathf.Max(area.yMin, area.yMax - bounds.height)) - bounds.yMin);
            foreach (Ue.RectTransform rect in roots)
            {
                if (roots.Exists(other => other != rect && rect.IsChildOf(other))) continue;
                Ue.RectTransform parent = rect.parent as Ue.RectTransform;
                if (parent == null) continue;
                Ue.Vector2 start, finish;
                Ue.Vector2 center = ScreenBounds(rect).center;
                if (Ue.RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, center, Camera(parent), out start)
                    && Ue.RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, center + shift, Camera(parent), out finish))
                    rect.localPosition += new Ue.Vector3(finish.x - start.x, finish.y - start.y, 0f);
            }
        }

        internal static void Refresh()
        {
            if (!enabled) return;
            DialogUIManager ui = DialogUIManager.Instance;
            NegociationUIManager trade = NegociationUIManager.Instance;
            StoreClient client = PlayerStore.Instance != null && PlayerStore.Instance.currentClientInstance != null
                ? PlayerStore.Instance.currentClientInstance.GetClientBlueprint() : null;
            Dialogue request = client != null && client.mainDialogue != null ? client.mainDialogue.nextDialogue : null;
            if (ui == null || trade == null || !trade.IsOpen || trade.uiObject == null || client == null || client.isBusinessComplete
                || Reservations.PickupOf(client.gunOrderData) == null || ui.currentDialog != request || request == null
                || request.choices.Count != 1 || ui.IsLineStillDisplaying(request))
            { Restore(); return; }
            float currentScale = EmporiumOption.DialogBubbleScale;
            if (owner != ui || page != request || !Ue.Mathf.Approximately(bubbleScale, currentScale))
            {
                Restore();
                ui.ApplyBubbleScale();
                owner = ui; page = request; bubbleScale = currentScale;
            }
            Ue.RectTransform panel = trade.uiObject.transform as Ue.RectTransform;
            if (panel == null) { Restore(); return; }
            Original body = Remember(ui.clientBubble != null ? ui.clientBubble.transform as Ue.RectTransform : null,
                ui.clientBubble != null ? ui.clientBubble.textfield : null);
            Original name = Remember(ui.clientNameBubble != null ? ui.clientNameBubble.transform as Ue.RectTransform : null, null);
            Original choice = ui.dialogChoiceBubbles != null && ui.dialogChoiceBubbles.Count > 0 && ui.dialogChoiceBubbles[0].gameObject.activeInHierarchy
                ? Remember(ui.dialogChoiceBubbles[0].transform as Ue.RectTransform, ui.dialogChoiceBubbles[0].TMP) : null;
            foreach (Original saved in originals) RestoreGeometry(saved, false);
            Ue.Rect safe = Ue.Screen.safeArea;
            if (safe.width <= 0f || safe.height <= 0f) safe = new Ue.Rect(0f, 0f, Ue.Screen.width, Ue.Screen.height);
            Ue.Rect tablet = ScreenBounds(panel);
            float gap = Ue.Mathf.Max(4f, Ue.Mathf.Min(safe.width, safe.height) * 0.01f);
            Ue.Rect area = Ue.Rect.MinMaxRect(safe.xMin + gap, safe.yMin + gap, Ue.Mathf.Max(safe.xMin + gap, tablet.xMin - gap), safe.yMax - gap);
            // A player can drag the tablet to the far left. Use its right side when the left side has no usable space.
            if (area.width < safe.width * 0.15f && safe.xMax - tablet.xMax - 2f * gap > area.width)
                area = Ue.Rect.MinMaxRect(tablet.xMax + gap, safe.yMin + gap, safe.xMax - gap, safe.yMax - gap);
            FitText(body, area.width); FitText(choice, area.width);
            Place(new List<Ue.RectTransform> { body != null ? body.Rect : null, name != null ? name.Rect : null }, area);
            Place(new List<Ue.RectTransform> { choice != null ? choice.Rect : null }, area);
        }
    }

    [HarmonyPatch(typeof(NegociationUIManager), "LateUpdate")]
    internal static class ReservationPickupLayoutPatch
    {
        private static void Postfix() { ReservationPickupLayout.Refresh(); }
    }

    [HarmonyPatch(typeof(NegociationUIManager), nameof(NegociationUIManager.CloseUI))]
    internal static class ReservationPickupLayoutClosedPatch
    {
        private static void Postfix() { ReservationPickupLayout.Restore(); }
    }

    [HarmonyPatch(typeof(DialogUIManager), nameof(DialogUIManager.EndConversation))]
    internal static class ReservationPickupLayoutEndedPatch
    {
        private static void Postfix() { ReservationPickupLayout.Restore(); }
    }

    // Run before load hooks and before either F5 mod restores waiting customers from their saved identifiers.
    [HarmonyPatch(typeof(PlayerStore), nameof(PlayerStore.LoadGame))]
    internal static class ReservationLoadPatch
    {
        private static void Prefix()
        {
            Reservations.OnGameLoading();
        }
    }

    // Both F5 variants restore their waiting queue (and the exact snapshot's current customer) in this update.
    [HarmonyPatch(typeof(StoreShutterButton), "Update")]
    [HarmonyAfter("mod.F5ForceSave", "mod.F5SnapshotSave")]
    internal static class ReservationRestoreQueuePatch
    {
        private static void Postfix()
        {
            Reservations.RecoverMissingPickupsAfterLoad();
        }
    }

    [HarmonyPatch(typeof(StoreClientListGun), "TryStayForMeleeWeapon")]
    internal static class ReservationDismissPatch
    {
        private static bool Prefix(StoreClient storeClient, ref bool __result)
        {
            if (storeClient == null || Reservations.PickupOf(storeClient.gunOrderData) == null)
                return true;
            __result = Reservations.BlockDismiss(storeClient);
            return false;
        }
    }

    [HarmonyPatch(typeof(StoreClientListGun), nameof(StoreClientListGun.CreateDeliveryFollowUp))]
    internal static class ReservationFollowUpPatch
    {
        private static bool Prefix(StoreClient storeClient, ref Dialogue __result)
        {
            if (storeClient == null || Reservations.PickupOf(storeClient.gunOrderData) == null)
                return true;
            if (NegociationUIManager.Instance != null)
                NegociationUIManager.Instance.CloseUI();
            __result = null;
            return false;
        }
    }

    [HarmonyPatch(typeof(GunOrderData), nameof(GunOrderData.OnOrderAccepted))]
    internal static class OrderAcceptedPatch
    {
        private static void Prefix(GunOrderData __instance)
        {
            Workshop.OnOrderAccepted(__instance);
            Factions.OnOrderAccepted(__instance);
            Contracts.OnOrderAccepted(__instance);
            Certificates.OnRepairAccepted(__instance);
            RepairOrders.OnOrderAccepted(__instance);
            Reservations.OnOrderAccepted(__instance);
        }
    }

    // The customer who will come back says so in the first line after the deal: the game's follow-up when it has one (it may ask
    // for ammunition next), its closing line otherwise (NegociationUIManager.OnOfferAcceptClick).
    [HarmonyPatch(typeof(StoreClientListGun), "CreateDeliveryFollowUp")]
    internal static class RepairPraisePatch
    {
        private static void Postfix(StoreClient storeClient, Dialogue __result)
        {
            if (storeClient == null || !Reservations.IsPleased(storeClient.gunOrderData))
                return;
            Dialogue first = __result ?? storeClient.acceptDealDialogue;
            if (first != null)
                first.SetText(storeClient.displayName, Text.Pick("你修得很好，我还会再来的。", "You did good work on this. I'll be back."));
        }
    }

    [HarmonyPatch(typeof(GunOrderData), nameof(GunOrderData.GetBaseReward))]
    internal static class ReservationRewardPatch
    {
        private static void Postfix(GunOrderData __instance, ref int __result)
        {
            Reservations.Reservation r = Reservations.PickupOf(__instance);
            if (r != null)
                __result = Reservations.Due(r);
        }
    }

    [HarmonyPatch(typeof(StorePhoneClient), nameof(StorePhoneClient.OnEndDay))]
    internal static class ReservationNightPatch
    {
        private static void Postfix()
        {
            Reservations.OnNight();
        }
    }
}
