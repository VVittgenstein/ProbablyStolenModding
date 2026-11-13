using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace GunsmithExpansion
{
    // Long-term contracts (final-strategy 27, 34, 39, 40, 46, 47). Late in the game the police and the revolutionaries
    // (once the store holds the third firearm permit) order guns of one kind in bulk. Their contact comes to the
    // counter with the terms and the store takes the contract or not. The guns go in two batches, half after 3 days
    // and the rest after 7; on each due day one courier per gun comes by appointment with a gun order for one gun of
    // the contract's classes and tier, with one stat at or above a bar that the classes' guns as bought seldom reach
    // and a well printed gun with gun mods does (final-strategy 39); the police take legal guns only (46). Every gun is
    // paid on delivery at the contract's unit price, the same at every difficulty (47). A batch delivered in full
    // raises the faction's reputation; if a due day ends with guns missing, the contract is called off and the
    // faction's reputation drops. The number of guns follows the workshop reputation (40; Workshop.ContractSize). The
    // contract in force is kept in PlayerStore.modData; there is one at a time. From 0.6.2 the revolutionaries also
    // order the special tier (critical contraband: anti-materiel rifles, grenade launchers, heavy machine guns), which
    // had no buyer (final-strategy 128; VVittgenstein 2026-11-04 16:36).
    //
    // Agent's decisions (the rewards and the frequency are open question 2 of final-strategy, for VVittgenstein to confirm):
    // the police need the third permit and security reputation 30 or more and buy tier three intermediate rifles or
    // tier two shotguns by reliability; the revolutionaries need the third permit too (VVittgenstein 2026-11-06, current/008 quote 35,
    // item 82; until then a store with permit I got their order for tier three guns, quote 31 A) and revolution reputation
    // 30 or more and buy tier three
    // intermediate rifles, full-power rifles and machine guns by firepower, contraband allowed; the bar is 125% of the
    // 75th percentile of the classes' base stat; the unit price is 125% of the classes' average value; an offer comes
    // with a 30% chance each night once allowed (60% for the revolutionaries during their mobilization), and not
    // before 7 nights after the last contract or a declined offer; reputation +3 per full batch, -5 for a called off
    // contract. The special tier: one of the revolutionaries' offers in four, the unit price by the same rule (2020).
    // Its bar and size are VVittgenstein's: firepower 95 (2026-11-04 16:59, final-strategy 129), which all five special guns reach
    // in good condition; 1 or 2 guns, one gun due after 3 days, two guns after 3 and 7 days (2026-11-04, final-strategy 131); the Agent's choice is even odds between the two.
    internal static class Contracts
    {
        internal const string OfferPrefix = "gseContractOffer";
        internal const string CourierPrefix = "gseContractCourier";
        internal static readonly int[] BatchNights = { 3, 7 };
        internal const int OfferChancePercent = 30;
        internal const int WaitNights = 7;
        internal const int ReputationNeeded = 30;
        internal const int BatchReputation = 3;
        internal const int CalledOffReputation = 5;
        private const string Key = "contract";
        private const string WaitKey = "contract_wait";
        private static readonly Random rng = new Random();
        private static readonly ConditionalWeakTable<GunOrderData, Courier> couriers = new ConditionalWeakTable<GunOrderData, Courier>();

        internal sealed class Option
        {
            internal string Tier;
            internal string[] Classes;
            // the option's share of the kind's offers, against the other options' weights
            internal int Weight = 1;
            // guns in the contract, one of these; null follows the workshop reputation (Workshop.ContractSize)
            internal int[] Sizes;
            // the stat's bar set by VVittgenstein; 0 computes it from the classes' guns (Bar)
            internal int FixedBar;
        }

        internal sealed class Kind
        {
            internal string Key;
            internal string Faction;
            internal Option[] Options;
            internal string Stat;
            internal bool Legal;
        }

        internal static readonly Kind[] Kinds =
        {
            new Kind
            {
                Key = "Sec", Faction = "FACTION_SECURITY", Stat = "GUN_CURRENT_RELIABILITY_TAG", Legal = true,
                Options = new[] { new Option { Tier = "三档", Classes = new[] { "中间威力步枪" } }, new Option { Tier = "二档", Classes = new[] { "霰弹枪" } } },
            },
            new Kind
            {
                Key = "Rev", Faction = "FACTION_REVOLUTION", Stat = "GUN_CURRENT_FIREPOWER_TAG", Legal = false,
                Options = new[]
                {
                    new Option { Tier = "三档", Classes = new[] { "中间威力步枪", "全威力步枪", "机枪" }, Weight = 3 },
                    // firepower 95: VVittgenstein 2026-11-04 16:59 (final-strategy 129)
                    new Option { Tier = "特殊", Classes = new[] { "反器材步枪", "榴弹发射器", "重机枪" }, Weight = 1, Sizes = new[] { 1, 2 }, FixedBar = 95 },
                },
            },
        };

        internal static Option PickOption(Kind k)
        {
            int total = 0;
            foreach (Option o in k.Options)
                total += o.Weight;
            int roll = rng.Next(total);
            foreach (Option o in k.Options)
            {
                if (roll < o.Weight)
                    return o;
                roll -= o.Weight;
            }
            return k.Options[0];
        }

        internal sealed class Contract
        {
            internal string Kind;
            internal string Tier;
            internal string[] Classes;
            internal bool Legal;
            internal string Stat;
            internal int Bar;
            internal int Price;
            internal int[] Size = new int[2];
            internal int[] Nights = new int[2];
            internal int[] Done = new int[2];

            internal int Total
            {
                get { return Size[0] + Size[1]; }
            }
        }

        // Internal for F5 Quick Save Plus's extra data (F5SnapshotSpec), which keeps a courier's entry with his order.
        internal sealed class Courier
        {
            internal int Batch;
        }

        internal static void Register()
        {
            foreach (Kind kind in Kinds)
            {
                string k = kind.Key;
                StoreClientListDict.storeClientDict[OfferPrefix + k] = () => CreateOffer(k);
                StoreClientListDict.storeClientDict[CourierPrefix + k] = () => CreateCourier(k);
            }
        }

        internal static Kind Find(string key)
        {
            foreach (Kind k in Kinds)
            {
                if (k.Key == key)
                    return k;
            }
            return null;
        }

        internal static Contract Load()
        {
            string[] f = (ModHelper.GetModData(GunsmithMod.ModId, Key, "") ?? "").Split(';');
            if (f.Length < 13 || Find(f[0]) == null)
                return null;
            var c = new Contract { Kind = f[0], Tier = f[1], Classes = f[2].Split('|'), Legal = f[3] == "1", Stat = f[4] };
            int[] n = new int[8];
            for (int i = 0; i < 8; i++)
            {
                if (!int.TryParse(f[5 + i], NumberStyles.Integer, CultureInfo.InvariantCulture, out n[i]))
                    return null;
            }
            c.Bar = n[0];
            c.Price = n[1];
            c.Size[0] = n[2];
            c.Nights[0] = n[3];
            c.Done[0] = n[4];
            c.Size[1] = n[5];
            c.Nights[1] = n[6];
            c.Done[1] = n[7];
            return c;
        }

        internal static void Save(Contract c)
        {
            if (c == null)
            {
                ModHelper.SetModData(GunsmithMod.ModId, Key, "");
                return;
            }
            var f = new List<string> { c.Kind, c.Tier, string.Join("|", c.Classes), c.Legal ? "1" : "0", c.Stat };
            foreach (int v in new[] { c.Bar, c.Price, c.Size[0], c.Nights[0], c.Done[0], c.Size[1], c.Nights[1], c.Done[1] })
                f.Add(v.ToString(CultureInfo.InvariantCulture));
            ModHelper.SetModData(GunsmithMod.ModId, Key, string.Join(";", f.ToArray()));
        }

        internal static bool Eligible(Kind k)
        {
            if (!PermitHelper.IsGunPermitIII())
                return false;
            return (k.Key == "Sec" ? StoreReputation.GetSecReputation() : StoreReputation.GetRevReputation()) >= ReputationNeeded;
        }

        private static List<GunDef> Guns(string tier, string[] classes)
        {
            var list = new List<GunDef>();
            foreach (GunDef d in Catalog.Guns)
            {
                if (d.Tier == tier && Array.IndexOf(classes, d.ClassName) >= 0)
                    list.Add(d);
            }
            return list;
        }

        internal static int Bar(string tier, string[] classes, string stat)
        {
            int index = Array.IndexOf(ModOrders.Stats, stat);
            var values = new List<int>();
            foreach (GunDef d in Guns(tier, classes))
                values.Add(d.Stats[index]);
            if (values.Count == 0)
                return 0;
            values.Sort();
            return (int)Math.Round(values[(int)Math.Floor(0.75 * (values.Count - 1))] * 1.25);
        }

        internal static int Price(string tier, string[] classes)
        {
            List<GunDef> guns = Guns(tier, classes);
            if (guns.Count == 0)
                return 0;
            double sum = 0;
            foreach (GunDef d in guns)
                sum += d.Value;
            return (int)(Math.Round(sum / guns.Count * 1.25 / 10.0) * 10);
        }

        internal static Contract Terms(Kind k, Option option)
        {
            int n = option.Sizes != null ? option.Sizes[rng.Next(option.Sizes.Length)] : Workshop.ContractSize();
            var c = new Contract { Kind = k.Key, Tier = option.Tier, Classes = option.Classes, Legal = k.Legal, Stat = k.Stat };
            c.Bar = option.FixedBar > 0 ? option.FixedBar : Bar(option.Tier, option.Classes, k.Stat);
            c.Price = Price(option.Tier, option.Classes);
            c.Size[0] = (n + 1) / 2;
            c.Size[1] = n - c.Size[0];
            c.Nights[0] = BatchNights[0];
            c.Nights[1] = BatchNights[1];
            return c;
        }

        private static string ContactName(Kind k)
        {
            return k.Key == "Sec" ? Text.Pick("治安部采购员", "Security procurement officer") : Text.Pick("革命军联络人", "Revolution contact");
        }

        private static string ClassList(Contract c)
        {
            return string.Join(Text.Pick("或", " or "), Array.ConvertAll(c.Classes, Text.ClassName));
        }

        internal static string OfferLine(Contract c)
        {
            string stat = Text.StatName(c.Stat);
            if (c.Kind == "Sec")
                return string.Format(Text.Pick(
                    "治安部要补一批装备。{0} 把{1}，{2}，必须合法，{3}不低于 {4}。分两批交：{5} 天后 {6} 把，{7} 天后 {8} 把。每把 {9} 信用点，到货就付。误了日子，合同作废。",
                    "Security needs equipment: {0} {1} ({2}, legal guns only), {3} at least {4}. Two batches: {6} in {5} days, {8} in {7} days. {9} credits a gun, paid on delivery. Miss a date and the contract is off."),
                    c.Total, ClassList(c), Text.TierName(c.Tier), stat, c.Bar, BatchNights[0], c.Size[0], BatchNights[1], c.Size[1], c.Price);
            if (c.Size[1] == 0)
                return string.Format(Text.Pick(
                    "我们需要枪。{0} 把{1}，{2}，{3}不低于 {4}。{5} 天后交。{6} 信用点，到货就付。别让我们失望。",
                    "We need a gun: {0} {1} ({2}), {3} at least {4}, in {5} days. {6} credits, paid on delivery. Don't let us down."),
                    c.Total, ClassList(c), Text.TierName(c.Tier), stat, c.Bar, BatchNights[0], c.Price);
            return string.Format(Text.Pick(
                "我们需要枪。{0} 把{1}，{2}，{3}不低于 {4}。分两批：{5} 天后 {6} 把，{7} 天后 {8} 把。每把 {9} 信用点，到货就付。别让我们失望。",
                "We need guns: {0} {1} ({2}), {3} at least {4}. Two batches: {6} in {5} days, {8} in {7} days. {9} credits a gun, paid on delivery. Don't let us down."),
                c.Total, ClassList(c), Text.TierName(c.Tier), stat, c.Bar, BatchNights[0], c.Size[0], BatchNights[1], c.Size[1], c.Price);
        }

        // The contact who brings the terms.
        internal static StoreClient CreateOffer(string key)
        {
            Kind k = Find(key);
            if (k == null || Load() != null || !Eligible(k))
                return Reservations.Nobody();
            Contract c = Terms(k, PickOption(k));
            var client = new StoreClient();
            client.identifier = "gseContractOffer";
            client.displayName = ContactName(k);
            client.spriteName = k.Key == "Sec" ? SpriteDict.GetRandomSecSpriteName() : SpriteDict.GetRandomRevSpriteName();
            client.clientFaction = k.Faction;
            client.clientIntent = StoreClient.ClientIntent.DIALOGUE;
            client.isBargainAvailable = false;
            client.CanReturn = false;
            string name = client.displayName;
            Dialogue offer = client.mainDialogue.SetText(name, OfferLine(c));
            Dialogue take = new Dialogue().SetText("PLAYER", Text.Pick("这份合同我接了。", "I'll take the contract."));
            take.SetNextDialogue(new Dialogue().SetText(name, Text.Pick("很好。到日子会有人来取。", "Good. Someone will come for them on the day.")).SetEndAction(Reservations.Leave));
            offer.AddChoice(DirectoryMaster.Item("$", true), "take", "", take, () => Accept(c));
            Dialogue no = new Dialogue().SetText("PLAYER", Text.Pick("这次不接。", "Not this time."));
            no.SetNextDialogue(new Dialogue().SetText(name, Text.Pick("那就算了。", "Never mind, then.")).SetEndAction(Reservations.Leave));
            offer.AddChoice(DirectoryMaster.Item("$", true), "no", "", no, Decline);
            client.CompleteClientCreation(false);
            GunsmithMod.Log("contract offer from " + k.Key + ": " + c.Total + " x " + string.Join("/", c.Classes) + " " + c.Tier + ", " + c.Stat + " >= " + c.Bar + ", " + c.Price + " each");
            return client;
        }

        internal static void Accept(Contract c)
        {
            if (PlayerStore.Instance == null || Load() != null)
                return;
            Save(c);
            for (int b = 0; b < 2; b++)
            {
                for (int i = 0; i < c.Size[b]; i++)
                    PlayerStore.Instance.QueueFuturClient(CourierPrefix + c.Kind, c.Nights[b]);
            }
            OrderSlips.Contract(c);
            GunsmithMod.Log("contract taken: " + c.Size[0] + " guns in " + c.Nights[0] + " days, " + c.Size[1] + " in " + c.Nights[1]);
        }

        internal static void Decline()
        {
            ModHelper.SetModDataInt(GunsmithMod.ModId, WaitKey, WaitNights);
            GunsmithMod.Log("contract offer declined");
        }

        // A courier of a batch due today: a gun order for one gun of the contract.
        internal static StoreClient CreateCourier(string key)
        {
            Contract c = Load();
            Kind k = Find(key);
            int b = -1;
            if (c != null && c.Kind == key)
            {
                for (int i = 0; i < 2 && b < 0; i++)
                {
                    if (c.Nights[i] == 0 && c.Done[i] < c.Size[i])
                        b = i;
                }
            }
            if (b < 0 || k == null)
                return Reservations.Nobody();
            var order = new GunOrderData();
            // the game's orders go up to tier three; the special tier orders as tier three
            order.tier = c.Tier == "一档" ? 1 : c.Tier == "二档" ? 2 : 3;
            order.mandatoryRequirements.Add(Requirements.Class(c.Tier, c.Classes));
            if (c.Legal)
                order.mandatoryRequirements.Add(Requirements.Legal());
            order.mandatoryRequirements.Add(new GunOrderRequirement { id = c.Stat, value = c.Bar - 1, isMustBeLargerThan = true });
            order.baseReward = c.Price;
            order.dialogText = c.Total == 1
                ? string.Format(Text.Pick("我按合同来取枪。{0} 信用点。", "I'm here for the contract gun. {0} credits."), c.Price)
                : string.Format(Text.Pick("我按合同来取第 {0} 批的枪。这批 {1} 把，已经交了 {2} 把。每把 {3} 信用点。",
                    "I'm here for batch {0} of the contract: {1} guns, {2} delivered so far. {3} credits a gun."), b + 1, c.Size[b], c.Done[b], c.Price);
            couriers.Remove(order);
            couriers.Add(order, new Courier { Batch = b });
            StoreClient client = StoreClientListGun.CreateMercGunBuyer(order);
            client.displayName = ContactName(k);
            client.spriteName = k.Key == "Sec" ? SpriteDict.GetRandomSecSpriteName() : SpriteDict.GetRandomRevSpriteName();
            client.clientFaction = k.Faction;
            for (Dialogue d = client.mainDialogue; d != null; d = d.nextDialogue)
                d.title = client.displayName;
            GunsmithMod.Log("contract courier for batch " + (b + 1) + " (" + c.Done[b] + "/" + c.Size[b] + ")");
            return client;
        }

        internal static bool IsCourierOrder(GunOrderData order)
        {
            Courier cr;
            return order != null && couriers.TryGetValue(order, out cr);
        }

        internal static Courier CourierOf(GunOrderData order)
        {
            Courier cr;
            return order != null && couriers.TryGetValue(order, out cr) ? cr : null;
        }

        // The entry of an order that F5 Quick Save Plus made anew from its snapshot on load (F5SnapshotSpec.LoadExtra).
        internal static void PutCourier(GunOrderData order, Courier courier)
        {
            couriers.Remove(order);
            couriers.Add(order, courier);
        }

        // Before GunOrderData.OnOrderAccepted pays the gun.
        internal static void OnOrderAccepted(GunOrderData order)
        {
            Courier cr;
            if (order == null || !couriers.TryGetValue(order, out cr))
                return;
            Contract c = Load();
            if (c == null)
                return;
            Kind k = Find(c.Kind);
            c.Done[cr.Batch]++;
            if (c.Done[cr.Batch] == c.Size[cr.Batch])
            {
                StoreReputation.ModReputation(k.Faction, BatchReputation, true);
                GunsmithMod.Log("contract batch " + (cr.Batch + 1) + " delivered in full");
            }
            if (c.Done[0] >= c.Size[0] && c.Done[1] >= c.Size[1])
            {
                Save(null);
                ModHelper.SetModDataInt(GunsmithMod.ModId, WaitKey, WaitNights);
                GunsmithMod.Log("contract fulfilled");
            }
            else
            {
                Save(c);
            }
        }

        // Each night (StorePhoneClient.OnEndDay): count down the batches, call off a contract whose due day ended with
        // guns missing, and maybe send a contact with an offer for tomorrow.
        internal static void OnNight()
        {
            Contract c = Load();
            if (c != null)
            {
                bool missed = false;
                for (int b = 0; b < 2; b++)
                {
                    if (c.Done[b] >= c.Size[b])
                        continue;
                    if (c.Nights[b] > 0)
                        c.Nights[b]--;
                    else
                        missed = true;
                }
                if (missed)
                {
                    Kind k = Find(c.Kind);
                    StoreReputation.ModReputation(k.Faction, -CalledOffReputation, true);
                    Save(null);
                    ModHelper.SetModDataInt(GunsmithMod.ModId, WaitKey, WaitNights);
                    GunsmithMod.Log("contract called off: batch due with guns missing");
                }
                else
                {
                    Save(c);
                }
                return;
            }
            int wait = ModHelper.GetModDataInt(GunsmithMod.ModId, WaitKey, 0);
            if (wait > 0)
            {
                ModHelper.SetModDataInt(GunsmithMod.ModId, WaitKey, wait - 1);
                return;
            }
            var eligible = new List<Kind>();
            foreach (Kind k in Kinds)
            {
                if (Eligible(k))
                    eligible.Add(k);
            }
            if (eligible.Count == 0 || PlayerStore.Instance == null)
                return;
            Kind pick = eligible[rng.Next(eligible.Count)];
            int chance = pick.Key == "Rev" && Factions.Mobilizing() ? OfferChancePercent * 2 : OfferChancePercent;
            if (rng.Next(100) >= chance)
                return;
            PlayerStore.Instance.QueueFuturClient(OfferPrefix + pick.Key, 1);
            // the offer comes tomorrow; no other one is sent in the meantime
            ModHelper.SetModDataInt(GunsmithMod.ModId, WaitKey, 2);
            GunsmithMod.Log("contract offer from " + pick.Key + " tomorrow");
        }
    }

    [HarmonyPatch(typeof(GunOrderData), nameof(GunOrderData.GetBaseReward))]
    internal static class ContractRewardPatch
    {
        private static void Postfix(GunOrderData __instance, ref int __result)
        {
            if (Contracts.IsCourierOrder(__instance))
                __result = __instance.baseReward;
        }
    }

    [HarmonyPatch(typeof(StorePhoneClient), nameof(StorePhoneClient.OnEndDay))]
    internal static class ContractNightPatch
    {
        private static void Postfix()
        {
            Contracts.OnNight();
        }
    }
}
