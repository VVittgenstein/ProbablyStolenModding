using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using HarmonyLib;

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

        internal static void Register()
        {
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
            assigned.Add(due.Id);
            GunDef gun = RepairOrders.Find(due.Gun);
            var order = new GunOrderData();
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
            StoreClient client = Buyer(kind, order);
            GunsmithMod.Log("reservation " + due.Id + " pickup for " + due.Gun + ", due " + Due(due));
            return client;
        }

        private static readonly HashSet<int> assigned = new HashSet<int>();
        private static readonly ConditionalWeakTable<GunOrderData, GunOrderData> pleased = new ConditionalWeakTable<GunOrderData, GunOrderData>();

        internal static Reservation PickupOf(GunOrderData order)
        {
            Reservation r;
            return order != null && pickups.TryGetValue(order, out r) ? r : null;
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
            List<Reservation> list = All(), keep = new List<Reservation>();
            foreach (Reservation r in list)
            {
                if (r.Nights > 0)
                {
                    r.Nights--;
                    keep.Add(r);
                    continue;
                }
                assigned.Remove(r.Id);
                if (PlayerStore.Instance != null)
                    PlayerStore.Instance.ModCash(-r.Deposit, true);
                StoreReputation rep = !string.IsNullOrEmpty(r.Faction) ? StoreReputation.GetStoreReputation(r.Faction) : null;
                if (rep != null)
                    StoreReputation.ModReputation(r.Faction, -ReputationLoss, true);
                GunsmithMod.Log("reservation " + r.Id + " for " + r.Gun + " not delivered: deposit " + r.Deposit + " returned, " + r.Faction + " reputation -" + ReputationLoss);
            }
            Save(keep);
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
