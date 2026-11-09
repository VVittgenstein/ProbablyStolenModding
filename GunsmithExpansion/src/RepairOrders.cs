using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace GunsmithExpansion
{
    // Repair customers (final-strategy 13, 14, 16 to 18, 25). Until 0.5.15, 30% of the game's gun sellers (lower level,
    // mercenary and upper level) came with one of this mod's guns to repair instead; from 0.6.0 they come only by the
    // repair certificate in the display case (Certificates; final-strategy 97, VVittgenstein 2026-11-03 22:44: the 30% is
    // dropped), and the tier of their gun follows the certificate's level, no longer the weapon permit (final-strategy
    // 96). They use the game's repair order: the buyer of
    // the same faction gets a GunOrderData service order, the gun goes on the customer's side of the counter at the end
    // of the greeting and the negotiation window opens (StoreClientListGun.OnOrderGreetingEnd); accepting the offer
    // pays GunOrderData.GetTotalReward. For these orders the game's stat requirements are dropped, the gun gets faults,
    // and the pay for the faults: per fault 50, 70, 110 or 110 by the gun's tier, all of it when the fault is repaired
    // with material, half when it is repaired in an emergency way, nothing when it is known and left. The game's market
    // and markup modifiers do not apply. The second way of coming back (by phone on an agreed day) needs the
    // appointments of stage 4.
    // From 0.6.0 (current/006 sections 4 and 7): the game lets an order be accepted once its mandatory requirements are
    // met, and with none the offer could be accepted before any repair: the customer then left with the gun and paid 0
    // (Steam comment of 2026-11-03, reproduced in GunsmithExpansion/testing/harness/GseBugHarness.cs). A repair order now has one mandatory
    // requirement, at least one fault repaired, and one bonus requirement per fault the store knows of, paying what
    // that fault earns (final-strategy 94: the window lists every fault with its amount); a fault found by a check gets
    // its row then. A hidden fault left takes nothing off (until 0.5.15 half its pay; final-strategy 102, VVittgenstein
    // 2026-11-03 23:47). From 0.6.0 every fault pays three quarters of the
    // 0.5.15 amount, and the internal faults, which only the X-ray scanner finds, half as much again (VVittgenstein 23:59):
    // surface 38, 53, 83, internal 56, 79, 124, rounded
    // to whole credits. The repair fee of the store's repair certificate (Certificates.Fee) is the base reward until a fault is
    // repaired and from then on a row of the window, "2 级维修费 +30" (VVittgenstein 2026-11-05 22:40 and 23:38, current/008 quotes 17 and 18, item 11).
    // The pay per fault is work plus material, as the game's service orders pay their fee and the value of the parts put
    // on the gun (GunOrderData.GetPartsValue) (VVittgenstein 2026-11-02, current/005 quotes 50 and 54: repairs are the early way to
    // earn money, which the game lacks). Work: what a service order of the same tier pays without the gun mods, its fee
    // (GunOrderData.SERVICE_FEE 30, 60, 100) and its second step and side step rewards (SERVICE_SECOND_STEP_REWARD 15,
    // 25, 40; SERVICE_SIDE_STATS 2, 2, 3 x SERVICE_SIDE_STEP_REWARD 12, 20, 32), divided by the mean of three faults
    // (23, 42, 79). Material: the mean value of the cheapest full repair of the six faults (screws 14, wiring 16, rust
    // and fouling 35 + 65, part wear 25, weld and barrel 10 each: 29). Rounded to tens; the game's orders have three
    // tiers, so the special tier pays as tier three.
    // From 0.7 (current/008 quotes 18 point A, 20, 21 and 22; items 70, 74 and 10):
    //   - The customer has an identifier of his own (Certificates.ClientId), as the game's appraisal customers do: the game sends away the
    //     second of two customers of one identifier (StoreClientManager.ClientGenCheck), and he used to have the gun buyers'.
    //   - Two kinds of customer. One in three also wants the gun improved and names a budget for repairs and parts together; a total over
    //     the budget cannot be accepted (Requirements.Budget). The others only want it repaired, and no part goes on their gun
    //     (RepairOnlyModPatch). A gun with no gun mod window is always of the second kind.
    //   - Both kinds take the game's discount and extra charge, with the game's percentages, chances and budget changes (RepairBargain.cs):
    //     every amount of the order goes through Bargained.
    //   - A repair counts as a sale for the reputation (OnOrderAccepted).
    //   - At least one fault each gun's customer names can be repaired in the store now, and with nothing to repair with he does not come (item 9; Faults.NameOneRepairable). With a
    //     screwdriver alone, a customer now averages 47.6 credits on hard; until 0.6.2 he left at the first gun nothing could be done on, 20.
    //   - Rust and fouling pay by what they were cleaned with (item 8; RustPay).
    //   - A customer brings 1 to 3 guns (VVittgenstein 2026-11-06 03:40 and 04:01, current/008 quotes 23 and 24, item 81; the game's appraisal customers put 2 to 4 items on the counter at once,
    //     StoreClientListAppraisal.CreateAppraisalRandom, and settle each by itself; a gun order's window and pay know one gun, GunOrderData.serviceGun). One after the other: when one is
    //     paid he goes on to the next and it comes on the counter (NextGunLines; the game goes on with the customer when a follow-up line is there); his words are VVittgenstein's (quote 25). Sent away, he takes the guns not
    //     yet on the counter with him. Whether he wants them improved goes for him as a whole; the fee, the fault pay, the reputation and the promotion progress go by the gun; the budget
    //     is the guns' budgets together, each gun's pay taken off when paid; the store's offer holds for all his guns; he comes back only if every gun was fully repaired.
    internal static class RepairOrders
    {
        private static readonly Random rng = new Random();
        private static readonly ConditionalWeakTable<GunOrderData, Planned> planned = new ConditionalWeakTable<GunOrderData, Planned>();

        // Agent's numbers, which VVittgenstein accepted (current/008 quote 22 point 70): one customer in three; the budget by the
        // gun's tier, in tens, whatever the faults are, so that it does not give away how many are hidden.
        internal const int UpgradeOneIn = 3;
        private static readonly int[][] BudgetRanges = { new[] { 150, 350 }, new[] { 250, 450 }, new[] { 400, 700 } };
        // 1, 2 and 3 guns (Agent's numbers, which VVittgenstein accepted, current/008 quote 24 point 81).
        private static readonly int[] GunsPercent = { 70, 20, 10 };
        private static readonly MethodInfo greetingEnd = AccessTools.Method(typeof(StoreClientListGun), "OnOrderGreetingEnd");
        private static bool withoutBargain;

        private sealed class Planned
        {
            internal GunDef Gun;
            internal Dictionary<Fault, FaultState> Faults;
            internal bool Upgrade;
            internal int Budget;
            internal Party Party;
        }

        // What the guns of one customer share.
        private sealed class Party
        {
            internal string Kind = "LL";
            internal readonly List<GunOrderData> Orders = new List<GunOrderData>();
            internal bool AllFixed = true;
        }

        internal static int TierPay(string tier)
        {
            switch (tier)
            {
                case "一档": return 50;
                case "二档": return 70;
                default: return 110;
            }
        }

        // 4 is the special tier, which only the repair certificate's fourth level brings (Certificates.PickTier).
        internal static string TierName(int tier)
        {
            return tier <= 1 ? "一档" : tier == 2 ? "二档" : tier == 3 ? "三档" : "特殊";
        }

        // The highest weapon permit held marks the phase (final-strategy 45): none or permit I, tier one guns; permit
        // II, up to tier two; permit III, up to tier three. The top tier comes 60% of the time (Agent's decision).
        // From 0.6.0 the reservations and the damaged guns use it; the repair customers use Certificates.PickTier.
        internal static int PickTier()
        {
            int top = 1;
            for (int level = 3; level >= 2; level--)
            {
                if (PermitHelper.HavePermit("FIREARM", level))
                {
                    top = level;
                    break;
                }
            }
            if (top == 1 || rng.Next(100) < 60)
                return top;
            return 1 + rng.Next(top - 1);
        }

        // A repair customer of the given faction kind (LL, Merc, UL) whose first gun is of the given tier; null when none. `returning`: one who
        // was here before (Reservations.CreateRepairReturn); `guns` how many he brings, by GunsPercent when 0.
        internal static StoreClient CreateCustomer(string kind, int tier, bool returning = false, int guns = 0)
        {
            GunOrderData order = CreateOrder(tier, rng.Next(UpgradeOneIn) == 0);
            Planned p;
            if (order == null || !planned.TryGetValue(order, out p))
                return null;
            var party = new Party { Kind = kind };
            party.Orders.Add(order);
            p.Party = party;
            int budget = p.Budget;
            int count = guns > 0 ? guns : RollGuns();
            while (party.Orders.Count < count)
            {
                GunOrderData more = CreateOrder(Certificates.PickTier(), p.Upgrade);
                Planned q;
                if (more == null || !planned.TryGetValue(more, out q))
                    break;
                q.Party = party;
                party.Orders.Add(more);
                budget += q.Budget;
            }
            StoreClient client = kind == "Merc" ? StoreClientListGun.CreateMercGunBuyer(order)
                : kind == "UL" ? StoreClientListGun.CreateULBuyer(order) : StoreClientListGun.CreateLLGunBuyer(order);
            if (client == null)
                return null;
            client.identifier = Certificates.ClientId;
            client.isBargainAvailable = true;
            bool many = party.Orders.Count > 1;
            if (many)
                order.dialogText = Text.RepairRequest(Text.Pick(p.Gun.NameZh, p.Gun.NameEn), p.Faults, Text.GunTurn.First);
            if (p.Upgrade)
            {
                // SetBudget applies the game's budget modifiers (perks, the store's attractiveness); he names what comes out of them.
                client.SetBudget(budget);
                order.dialogText += Text.UpgradeRequest(client.GetBudget());
                if (many)
                    order.dialogText += Text.SharedBudget();
            }
            else
            {
                order.dialogText += Text.RepairOnlyRequest();
            }
            // The buyer's first line is one of the game's custom order greetings, the second the order's text.
            if (client.mainDialogue != null)
            {
                client.mainDialogue.SetText(client.displayName, returning ? Text.RepairReturnGreeting(many) : many ? Text.RepairGreetingMany() : Text.RepairGreeting(rng.Next(2)));
                if (client.mainDialogue.nextDialogue != null)
                    client.mainDialogue.nextDialogue.SetText(client.displayName, order.dialogText);
            }
            return client;
        }

        private static int RollGuns()
        {
            int roll = rng.Next(100);
            for (int i = 0; i < GunsPercent.Length; i++)
            {
                if (roll < GunsPercent[i])
                    return i + 1;
                roll -= GunsPercent[i];
            }
            return 1;
        }

        // The faction kind the customer of the order came as.
        internal static string KindOf(GunOrderData order)
        {
            Planned p;
            return order != null && planned.TryGetValue(order, out p) && p.Party != null ? p.Party.Kind : "LL";
        }

        // After a gun was paid: the lines that bring the customer's next gun, or null when this was his last.
        internal static Dialogue NextGunLines(StoreClient client)
        {
            GunOrderData order = client != null ? client.gunOrderData : null;
            Planned p;
            if (order == null || client.clientIntent != StoreClient.ClientIntent.GUNSMITH || !planned.TryGetValue(order, out p) || p.Party == null)
                return null;
            int i = p.Party.Orders.IndexOf(order);
            if (i < 0 || i + 1 >= p.Party.Orders.Count)
                return null;
            GunOrderData next = p.Party.Orders[i + 1];
            Planned q;
            if (!planned.TryGetValue(next, out q))
                return null;
            int left = p.Party.Orders.Count - i - 1;
            // Of three guns the second is "another" and the third "the third" or "the last"; of two the second is "another" or "the last".
            Text.GunTurn turn = left > 1 || rng.Next(2) == 0 ? (left == 1 && i == 1 ? Text.GunTurn.Third : Text.GunTurn.Another) : Text.GunTurn.Last;
            next.dialogText = Text.RepairRequest(Text.Pick(q.Gun.NameZh, q.Gun.NameEn), q.Faults, turn);
            // The gun just paid has been taken off the budget (OnOrderAccepted): he names what is left.
            next.dialogText += q.Upgrade ? Text.UpgradeLeft(client.GetBudget()) + (left > 1 ? Text.SharedBudget() : "") : Text.RepairOnlyRequest();
            // The window's rows are of the gun just paid; it opens again with the next gun on the counter (StoreClientListGun.OnOrderGreetingEnd).
            if (NegociationUIManager.Instance != null)
                NegociationUIManager.Instance.CloseUI();
            var first = new Dialogue();
            first.SetText(client.displayName, Text.GunDone());
            // as the game's follow-up lines (StoreClientListGun.CreateDeliveryFollowUp)
            first.priority = 4;
            first.NextDialogue().SetText(client.displayName, next.dialogText).SetEndAction(() =>
            {
                client.gunOrderData = next;
                greetingEnd.Invoke(null, new object[] { client });
            });
            GunsmithMod.Log("repair customer has another gun: " + (i + 2) + " of " + p.Party.Orders.Count);
            return first;
        }

        // When a gun is paid: whether the customer is done and every one of his guns was fully repaired.
        internal static bool DoneAndAllFixed(GunOrderData order, GameItem gun)
        {
            bool fixedNow = AllFixed(gun);
            Planned p;
            if (!planned.TryGetValue(order, out p) || p.Party == null)
                return fixedNow;
            if (!fixedNow)
                p.Party.AllFixed = false;
            return p.Party.AllFixed && p.Party.Orders[p.Party.Orders.Count - 1] == order;
        }

        // The gun has a gun mod window (Items.CreateGun): only those with no slot at all, and not the revolver with its special slot.
        private static bool TakesMods(GunDef d)
        {
            string[] open;
            return d.Slots != Slots.None || (Fittings.Slots.TryGetValue(d.Id, out open) && Array.IndexOf(open, "special") >= 0);
        }

        private static int RollBudget(int tier)
        {
            int[] range = BudgetRanges[Math.Max(1, Math.Min(tier, 3)) - 1];
            return range[0] + 10 * rng.Next((range[1] - range[0]) / 10 + 1);
        }

        // Two to four faults, at least one of them hidden; the customer names the others (Agent's decision).
        internal static GunOrderData CreateOrder(int tier, bool wantsUpgrade = false)
        {
            string tierName = TierName(tier);
            var guns = new List<GunDef>();
            foreach (GunDef d in Catalog.Guns)
            {
                if (d.Tier == tierName && (!wantsUpgrade || TakesMods(d)))
                    guns.Add(d);
            }
            if (guns.Count == 0)
                return wantsUpgrade ? CreateOrder(tier) : null;
            // At least one fault he names can be repaired in the store now (item 9); with nothing to repair with he does not come, or brings no more guns.
            HashSet<Fault> can = Repair.RepairableNow();
            if (can.Count == 0)
                return null;
            GunDef gun = null;
            Dictionary<Fault, FaultState> faults = null;
            for (int tries = 0; tries < 8 && faults == null; tries++)
            {
                gun = guns[rng.Next(guns.Count)];
                int count = 2 + rng.Next(3);
                int hidden = 1 + rng.Next(count - 1);
                faults = Faults.RandomFaults(gun.Antique, count, count - hidden, rng);
                if (!Faults.NameOneRepairable(faults, gun.Antique, can, rng))
                    faults = null;
            }
            if (faults == null)
                return null;

            GunOrderData order = GunOrderData.CreateServiceOrder(Math.Min(tier, 3));
            ModReflect.Set(order, "serviceGunId", gun.Id);
            order.baseReward = 0;
            order.dialogText = Text.RepairRequest(Text.Pick(gun.NameZh, gun.NameEn), faults);
            int budget = wantsUpgrade ? RollBudget(tier) : 0;
            planned.Remove(order);
            planned.Add(order, new Planned { Gun = gun, Faults = faults, Upgrade = wantsUpgrade, Budget = budget });
            GunsmithMod.Log("repair customer with " + gun.Id + " (" + tierName + "), faults " + Describe(faults) + (wantsUpgrade ? ", wants it improved, budget " + budget : ", repair only"));
            return order;
        }

        // Every fault of the gun fully repaired: none hidden, none left, none repaired in an emergency way.
        internal static bool AllFixed(GameItem gun)
        {
            Dictionary<Fault, FaultState> faults = Faults.Get(gun);
            if (faults.Count == 0)
                return false;
            foreach (FaultState s in faults.Values)
            {
                if (s != FaultState.Fixed)
                    return false;
            }
            return true;
        }

        internal static bool IsRepairOrder(GunOrderData order)
        {
            Planned p;
            return order != null && order.isServiceOrder && planned.TryGetValue(order, out p);
        }

        // GunOrderData.CreateServiceGun has made the gun from serviceGunId, set the game's condition and the
        // SERVICE_GUN_TAG and filled the stat requirements.
        internal static void OnServiceGunCreated(GunOrderData order, GameItem gun)
        {
            order.mandatoryRequirements.Clear();
            order.bonusRequirements.Clear();
            Planned p;
            Dictionary<Fault, FaultState> faults;
            if (planned.TryGetValue(order, out p))
            {
                faults = p.Faults;
            }
            else
            {
                // An order whose plan was not kept (for example one saved while the customer was in the store).
                int count = 2 + rng.Next(3);
                faults = Faults.RandomFaults(gun.IsTag(GunsItemDirectory.ANTIQUE_FIREARM_TAG), count, 1, rng);
            }
            gun.EnableTag(Repair.ServiceTag, false);
            Faults.Set(gun, new Dictionary<Fault, FaultState>(faults));
            Stolen.MaybeFile(gun, Stolen.RepairPercent);
            order.mandatoryRequirements.Add(Requirements.Repaired());
            if (p != null && p.Upgrade)
                order.mandatoryRequirements.Add(Requirements.Budget());
            SyncRows(order, gun);
        }

        // One bonus requirement per fault the store knows of, in the order of the gun's faults; rows are only added.
        internal static void SyncRows(GunOrderData order, GameItem gun)
        {
            if (!order.bonusRequirements.Exists(r => Requirements.IsRepairFee(r)))
                order.bonusRequirements.Insert(0, Requirements.RepairFee());
            foreach (KeyValuePair<Fault, FaultState> kv in Faults.Get(gun))
            {
                if (kv.Value == FaultState.Hidden)
                    continue;
                string id = Requirements.FaultRowId(kv.Key);
                if (!order.bonusRequirements.Exists(r => r.id == id))
                    order.bonusRequirements.Add(Requirements.FaultRow(kv.Key));
            }
        }

        // The repair order of the customer at the counter whose gun this is, or null.
        internal static GunOrderData OrderOf(GameItem gun)
        {
            if (gun == null || PlayerStore.Instance == null || PlayerStore.Instance.currentClientInstance == null)
                return null;
            StoreClient client = PlayerStore.Instance.currentClientInstance.GetClientBlueprint();
            GunOrderData order = client != null ? client.gunOrderData : null;
            if (order == null || !order.isServiceOrder || !gun.IsTag(Repair.ServiceTag))
                return null;
            return Traverse.Create(order).Field("serviceGun").GetValue<GameItem>() == gun ? order : null;
        }

        // The customer at the counter while he is here about a repair order. He stops being that when he leaves after a refused extra
        // charge (the game's bargain makes him DIALOGUE) or stays to buy a melee weapon (StoreClientListGun.TryStayForMeleeWeapon).
        internal static StoreClient CurrentCustomer()
        {
            if (!PlayerStore.HasInstance || PlayerStore.Instance.currentClientInstance == null)
                return null;
            StoreClient client = PlayerStore.Instance.currentClientInstance.GetClientBlueprint();
            return client != null && client.clientIntent == StoreClient.ClientIntent.GUNSMITH && IsRepairOrder(client.gunOrderData) ? client : null;
        }

        // No part goes on this customer's gun: he only wants it repaired, or the plan of the order was not kept.
        internal static bool IsRepairOnly(GameItem gun)
        {
            GunOrderData order = OrderOf(gun);
            Planned p;
            return order != null && !(planned.TryGetValue(order, out p) && p.Upgrade);
        }

        // An amount of a repair order after the store's offer to the customer at the counter: a discount takes its percent off, an extra
        // charge he accepted adds its percent.
        internal static int Bargained(int amount)
        {
            BargainUIManager bargain = BargainUIManager.Instance;
            if (withoutBargain || amount == 0 || bargain == null || CurrentCustomer() == null)
                return amount;
            int percent = bargain.DiscountOffered ? -bargain.DiscountPercent
                : ModReflect.Get<bool>(bargain, "markupApplied") ? bargain.MarkupPercent : 0;
            return percent == 0 ? amount : (int)Math.Round(amount * (100 + percent) / 100.0, MidpointRounding.AwayFromZero);
        }

        // The total of the customer's order against his budget (Requirements.Budget).
        internal static int CurrentTotal(GameItem gun)
        {
            GunOrderData order = OrderOf(gun);
            return order != null ? order.GetTotalReward(gun) : 0;
        }

        internal static bool WithinBudget(GameItem gun)
        {
            StoreClient client = CurrentCustomer();
            return client != null && OrderOf(gun) != null && CurrentTotal(gun) <= client.GetBudget();
        }

        // A repair counts as a sale for the reputation (current/008 quote 22 point 70). As at a sale the
        // game takes the amount before the bargain (BargainUIManager.GetPreBargainValue) times the customer's trade multiplier, which its
        // discount and extra charge change (BargainUIManager.RecomputeTradeRepMultiplier), so a discount earns more reputation.
        internal static void OnOrderAccepted(GunOrderData order)
        {
            StoreClient client = CurrentCustomer();
            GameItem gun = order != null ? order.GetPlacedServiceGun() : null;
            if (client == null || client.gunOrderData != order || !Faults.IsModGun(gun) || !gun.IsTag(Repair.ServiceTag) || string.IsNullOrEmpty(client.clientFaction))
                return;
            int value;
            withoutBargain = true;
            try
            {
                value = order.GetTotalReward(gun);
            }
            finally
            {
                withoutBargain = false;
            }
            StoreReputation.OnItemTraded(client.clientFaction, value, value, client, true, null);
            GunsmithMod.Log("repair order traded for reputation: " + value + " before the bargain, multiplier " + client.GetTradeRepMultiplier(true));
            if (client.useClientBudget)
                client.ModBudget(-order.GetTotalReward(gun));
        }

        // After a check or a repair of a service gun: rows for the faults found, and the window shown again.
        internal static void OnFaultsChanged(GameItem gun)
        {
            GunOrderData order = OrderOf(gun);
            if (order == null)
                return;
            SyncRows(order, gun);
            if (NegociationUIManager.Instance != null && NegociationUIManager.Instance.IsOpen)
                NegociationUIManager.Instance.RefreshUI();
        }

        // The full pay of one fault of the gun: three quarters of the tier's 0.5.15 pay, half as much again for an
        // internal fault, rounded to whole credits (38, 53, 83; 56, 79, 124).
        internal static int FullPay(GameItem gun, Fault fault)
        {
            string tier = TierOf(gun);
            return fault == Fault.Rust ? RustPay(tier)[0] : TierFaultPay(tier, Faults.IsSurface(fault));
        }

        private static string TierOf(GameItem gun)
        {
            GunDef def = Find(gun.identifier);
            return def != null ? def.Tier : "一档";
        }

        // Rust and fouling by the gun's tier: repaired fully, in an emergency way with alcohol only, in an emergency way with one use of fabric (item 8; the numbers of GPT's reply, which
        // VVittgenstein accepted 2026-11-06, current/008 quote 36). On hard a full cleaning costs 62.8: alcohol 46, a fifth of a fabric 16.8; until 0.6.2 it paid 38, 53, 83 and half of that
        // in an emergency way. The special tier pays as tier three, as with the other faults.
        private static readonly int[][] RustPays = { new[] { 83, 56, 27 }, new[] { 98, 64, 34 }, new[] { 128, 79, 49 } };

        internal static int[] RustPay(string tier)
        {
            return RustPays[tier == "一档" ? 0 : tier == "二档" ? 1 : 2];
        }

        // A gun cleaned in an emergency before 0.7 has no record of what with: the lower amount.
        private static int RustEmergencyPay(GameItem gun)
        {
            int[] pay = RustPay(TierOf(gun));
            return gun.IsTag(Faults.RustByAlcoholTag) ? pay[1] : pay[2];
        }

        // The full pay of one surface or internal fault of a gun of the tier (also listed in the certificate's tooltip).
        internal static int TierFaultPay(string tier, bool surface)
        {
            double pay = TierPay(tier) * 0.75 * (surface ? 1.0 : 1.5);
            return (int)Math.Round(pay, MidpointRounding.AwayFromZero);
        }

        // What one fault pays as it stands: all of it repaired with material, half repaired in an emergency way (rust and fouling by what it was cleaned with); a row
        // not met shows the full amount, what repairing it would pay.
        internal static int FaultPay(GameItem gun, Fault fault)
        {
            if (gun == null || !Faults.IsModGun(gun))
                return 0;
            FaultState s;
            Faults.Get(gun).TryGetValue(fault, out s);
            int full = FullPay(gun, fault);
            int pay = s != FaultState.Emergency ? full : fault == Fault.Rust ? RustEmergencyPay(gun) : full / 2;
            return Stolen.RepairPay(gun, pay);
        }

        // The pay for the repaired faults (the rows met), as the order's total without the repair fee and the parts.
        internal static int Pay(GameItem gun)
        {
            if (gun == null || !Faults.IsModGun(gun))
                return 0;
            int total = 0;
            foreach (KeyValuePair<Fault, FaultState> kv in Faults.Get(gun))
            {
                if (kv.Value == FaultState.Fixed || kv.Value == FaultState.Emergency)
                    total += FaultPay(gun, kv.Key);
            }
            return total;
        }

        internal static GunDef Find(string id)
        {
            foreach (GunDef d in Catalog.Guns)
            {
                if (d.Id == id)
                    return d;
            }
            return AddOn2077.Find(id);
        }

        internal static string Describe(Dictionary<Fault, FaultState> faults)
        {
            var parts = new List<string>();
            foreach (KeyValuePair<Fault, FaultState> kv in faults)
                parts.Add(kv.Key + "=" + kv.Value);
            return string.Join(",", parts.ToArray());
        }
    }

    // A service gun of this mod is a repair order's unless it belongs to a modification order (ModOrders); repair orders
    // pay 0 as the game's base fee, which also tells them apart when the plan was not kept.
    [HarmonyPatch(typeof(GunOrderData), nameof(GunOrderData.CreateServiceGun))]
    internal static class ServiceGunPatch
    {
        private static void Postfix(GunOrderData __instance, GameItem __result)
        {
            if (!Faults.IsModGun(__result))
                return;
            if (ModOrders.IsModOrder(__instance) || (__instance.baseReward > 0 && !RepairOrders.IsRepairOrder(__instance)))
                ModOrders.OnServiceGunCreated(__instance, __result);
            else
                RepairOrders.OnServiceGunCreated(__instance, __result);
        }
    }

    [HarmonyPatch(typeof(GunOrderData), nameof(GunOrderData.GetBaseReward))]
    internal static class RepairPayPatch
    {
        private static void Postfix(GunOrderData __instance, ref int __result)
        {
            if (!__instance.isServiceOrder)
                return;
            GameItem gun = __instance.GetPlacedServiceGun();
            if (Faults.IsModGun(gun) && gun.IsTag(Repair.ServiceTag))
                __result = RepairOrders.Bargained(__instance.bonusRequirements.Exists(r => Requirements.IsRepairFee(r) && r.IsMet(gun)) ? 0 : Certificates.Fee());
        }
    }

    // A fault row pays what the fault earns as it stands (RepairOrders.FaultPay), without the game's market modifiers.
    [HarmonyPatch(typeof(GunOrderRequirement), nameof(GunOrderRequirement.GetReward))]
    internal static class FaultRowRewardPatch
    {
        private static void Postfix(GunOrderRequirement __instance, ref int __result)
        {
            if (Requirements.IsRepairFee(__instance))
            {
                __result = RepairOrders.Bargained(Certificates.Fee());
                return;
            }
            Fault fault;
            if (!Requirements.TryFaultRow(__instance, out fault))
                return;
            GunOrderData order = PlayerStore.Instance != null && PlayerStore.Instance.currentClientInstance != null
                && PlayerStore.Instance.currentClientInstance.GetClientBlueprint() != null
                ? PlayerStore.Instance.currentClientInstance.GetClientBlueprint().gunOrderData : null;
            GameItem gun = order != null && order.isServiceOrder ? order.GetPlacedServiceGun() : null;
            __result = gun != null ? RepairOrders.Bargained(RepairOrders.FaultPay(gun, fault)) : __instance.reward;
        }
    }

    // A customer with another gun goes on with it (RepairOrders.NextGunLines) instead of the game's follow-up.
    [HarmonyPatch(typeof(StoreClientListGun), nameof(StoreClientListGun.CreateDeliveryFollowUp))]
    internal static class RepairNextGunPatch
    {
        private static bool Prefix(StoreClient storeClient, ref Dialogue __result)
        {
            Dialogue next = RepairOrders.NextGunLines(storeClient);
            if (next == null)
                return true;
            __result = next;
            return false;
        }
    }

    // The negotiation window names a repair order as one; the game calls every gun order a custom gun order.
    [HarmonyPatch(typeof(NegociationUIManager), "SectionTitle")]
    internal static class RepairTitlePatch
    {
        private static void Postfix(StoreClient storeClient, ref string __result)
        {
            if (storeClient != null && RepairOrders.IsRepairOrder(storeClient.gunOrderData))
                __result = Text.Pick("修枪订单", "Gun repair order");
        }
    }
}
