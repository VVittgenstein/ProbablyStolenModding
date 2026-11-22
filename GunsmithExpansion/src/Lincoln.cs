using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;

namespace GunsmithExpansion
{
    // T-013 (current/T-013.md; final-strategy 300 to 306 and 329 to 361): the independent arms dealer Yuri Lincoln, his daughter
    // Ava Lincoln, who takes his place on his days off (305, 330), and his shop Lincoln Arms (350 ⑤). This file keeps what the
    // store's save knows of them, in PlayerStore.modData:
    //   - lincoln_introduced: his first visit is done (306), which opens the shop at night and is when he leaves his card (333).
    //   - lincoln_spent and lincoln_gun_sales: the credits paid to him (at the shop and on 3955) and the credits of the guns the
    //     store sold (338). Favor (342 ①) is spent / 100 + gun sales / 500 points, kept exact and shown to the hundredth.
    //   - lincoln_notified: the highest level already told in the corner (342 ①: only the first time Lv.1 to Lv.4 is reached).
    //   - lincoln_greeted_yuri and lincoln_greeted_ava: the highest level whose first night lines each has said (4-1, 5-1 of the
    //     dialogue draft); lincoln_ava_met: the player has met Ava (on a delivery or at night).
    //   - lincoln_off_<n>: Yuri's day off in days 7n+1 to 7n+7, from n = 1 (350 ④: none in days 1 to 7, one random day in every 7
    //     from day 8, drawn once and kept, so loading does not change it).
    // Agent's arrangements (not confirmed by yzz): gun sales count from his first visit on, so that the level is not told before
    // the player has met him; a save of any version gets his first visit the day after loading when it is past day 3 (306 names
    // the saves with the version record; for the others nothing was said).
    internal static class Lincoln
    {
        internal const long Number = 3955;
        internal const int FirstVisitDay = 3;
        internal const string DealerPortrait = "gse_arms_dealer";
        internal const string DaughterPortrait = "gse_arms_dealer_daughter";
        internal static readonly int[] Thresholds = { 0, 10, 30, 60, 100 };
        internal static readonly int[] DiscountPercent = { 0, 0, 5, 8, 10 };
        internal static readonly int[] PhoneWeekly = { 0, 3, 3, 4, 5 };
        internal const int CreditsPerPointSpent = 100;
        internal const int CreditsPerPointSold = 500;

        private const string IntroducedKey = "lincoln_introduced";
        private const string SpentKey = "lincoln_spent";
        private const string SalesKey = "lincoln_gun_sales";
        private const string NotifiedKey = "lincoln_notified";
        private const string GreetedYuriKey = "lincoln_greeted_yuri";
        private const string GreetedAvaKey = "lincoln_greeted_ava";
        private const string AvaMetKey = "lincoln_ava_met";
        private const string OffPrefix = "lincoln_off_";
        private static readonly Random rng = new Random();

        internal static string DealerName()
        {
            return LincolnTexts.Yuri.Get();
        }

        internal static string DaughterName()
        {
            return LincolnTexts.Ava.Get();
        }

        internal static string ShopName()
        {
            return LincolnTexts.ShopName.Get();
        }

        internal static int Day()
        {
            return StoreStation.GetDayCounter();
        }

        internal static bool Introduced()
        {
            return ModHelper.GetModDataBool(GunsmithMod.ModId, IntroducedKey, false);
        }

        internal static void SetIntroduced()
        {
            ModHelper.SetModDataBool(GunsmithMod.ModId, IntroducedKey, true);
            GunsmithMod.Log("Yuri Lincoln introduced on day " + Day());
        }

        // Favor in 1/500 points: a credit spent with him is 5, a credit of guns sold 1.
        private static long Units()
        {
            long spent = Math.Max(0, ModHelper.GetModDataInt(GunsmithMod.ModId, SpentKey, 0));
            long sold = Math.Max(0, ModHelper.GetModDataInt(GunsmithMod.ModId, SalesKey, 0));
            return spent * (CreditsPerPointSold / CreditsPerPointSpent) + sold;
        }

        internal static int Hundredths()
        {
            return (int)Math.Min(int.MaxValue, Units() * 100 / CreditsPerPointSold);
        }

        internal static string PointsText()
        {
            int h = Hundredths();
            return (h / 100).ToString(CultureInfo.InvariantCulture) + "." + (h % 100).ToString("00", CultureInfo.InvariantCulture);
        }

        internal static int Level()
        {
            long units = Units();
            int level = 0;
            for (int i = 1; i < Thresholds.Length; i++)
            {
                if (units >= (long)Thresholds[i] * CreditsPerPointSold)
                    level = i;
            }
            return level;
        }

        internal static int Discount()
        {
            return DiscountPercent[Level()];
        }

        internal static int WeeklyPhoneItems()
        {
            return PhoneWeekly[Level()];
        }

        internal static string StatusLine()
        {
            return LincolnTexts.StatusLine[Level()].Format(PointsText());
        }

        // Credits paid to him, at the shop (LincolnShop.OnBought) or on 3955 (LincolnPhone).
        internal static void AddSpent(int credits, string what)
        {
            if (credits <= 0)
                return;
            int before = Level();
            int total = Add(SpentKey, credits);
            GunsmithMod.Log("Lincoln favor: " + credits + " credits spent (" + what + "), " + total + " in all, favor " + PointsText());
            AfterChange(before);
        }

        // Credits of a gun the store sold (338): at the counter, a custom order, a reservation picked up, a contract's gun.
        internal static void AddGunSale(int credits, string what)
        {
            if (credits <= 0 || !Introduced())
                return;
            int before = Level();
            int total = Add(SalesKey, credits);
            GunsmithMod.Log("Lincoln favor: gun sold for " + credits + " (" + what + "), " + total + " in all, favor " + PointsText());
            AfterChange(before);
        }

        private static int Add(string key, int credits)
        {
            long total = (long)Math.Max(0, ModHelper.GetModDataInt(GunsmithMod.ModId, key, 0)) + credits;
            int stored = (int)Math.Min(int.MaxValue, total);
            ModHelper.SetModDataInt(GunsmithMod.ModId, key, stored);
            return stored;
        }

        private static void AfterChange(int before)
        {
            int now = Level();
            if (now <= before)
                return;
            int told = ModHelper.GetModDataInt(GunsmithMod.ModId, NotifiedKey, 0);
            if (now > told)
            {
                ModHelper.SetModDataInt(GunsmithMod.ModId, NotifiedKey, now);
                if (StoreUIManager.Instance != null)
                    StoreUIManager.Instance.Notify(LincolnTexts.LevelNotice[now].Get(), "default");
                GunsmithMod.Log("Lincoln favor level " + now + " reached");
            }
            if (now >= 1)
            {
                LincolnPhone.EnsureContact();
                LincolnItems.RelabelCards();
            }
        }

        // The first night lines (4-1, 5-1): the level each of them last greeted at, -1 when never.
        internal static int Greeted(bool ava)
        {
            return ModHelper.GetModDataInt(GunsmithMod.ModId, ava ? GreetedAvaKey : GreetedYuriKey, -1);
        }

        internal static void SetGreeted(bool ava, int level)
        {
            ModHelper.SetModDataInt(GunsmithMod.ModId, ava ? GreetedAvaKey : GreetedYuriKey, level);
        }

        internal static bool AvaMet()
        {
            return ModHelper.GetModDataBool(GunsmithMod.ModId, AvaMetKey, false);
        }

        internal static void SetAvaMet()
        {
            ModHelper.SetModDataBool(GunsmithMod.ModId, AvaMetKey, true);
        }

        // Who is at the shop, answers 3955 and brings its orders on a day (350 ③ ④).
        internal static bool AvaOnDuty()
        {
            return AvaOnDuty(Day());
        }

        internal static bool AvaOnDuty(int day)
        {
            if (day < 8)
                return false;
            return DayOff((day - 1) / 7) == day;
        }

        internal static int DayOff(int period)
        {
            string key = OffPrefix + period.ToString(CultureInfo.InvariantCulture);
            int first = period * 7 + 1;
            int day = ModHelper.GetModDataInt(GunsmithMod.ModId, key, 0);
            if (day < first || day > first + 6)
            {
                day = first + rng.Next(7);
                ModHelper.SetModDataInt(GunsmithMod.ModId, key, day);
                GunsmithMod.Log("Yuri Lincoln takes day " + day + " off");
            }
            return day;
        }

        internal static string OnDutyName()
        {
            return AvaOnDuty() ? DaughterName() : DealerName();
        }

        internal static string OnDutyPortrait()
        {
            string key = Certificates.PortraitKey(AvaOnDuty() ? DaughterPortrait : DealerPortrait);
            return key ?? SpriteDict.GetRandomMercSpriteName();
        }

        // Each night (StorePhoneClient.OnEndDay): his first visit is queued for the next day from the night before day 3 on, and
        // again after a day he was not received (306).
        internal static void OnNight()
        {
            PlayerStore store = PlayerStore.Instance;
            if (store == null || Introduced() || Day() < FirstVisitDay - 1)
                return;
            if (store.futurStoreClientIdQueue != null && store.futurStoreClientIdQueue.Contains(LincolnVisits.IntroId))
                return;
            store.QueueFuturClient(LincolnVisits.IntroId, 0);
            GunsmithMod.Log("Yuri Lincoln's first visit queued for tomorrow");
        }

        // A save loaded on or after day 3 that never received him: he comes the next day (the night queues him). Nothing else to do here.
        internal static void OnGameLoaded()
        {
            if (PlayerStore.HasInstance && Level() >= 1)
                LincolnPhone.EnsureContact();
        }

        // Gun sales (338) at the counter: the game's StoreClient.OnItemBought(item, price) from PlayerStore.SellItem. A gun
        // cabinet counts the guns inside it only, their share of the price by their value (338: 整柜出售枪柜时只计柜内枪械的成交额).
        internal static void OnClientBought(GameItem item, int price)
        {
            if (item == null || price <= 0)
                return;
            if (item.IsGameItemType("FIREARM"))
            {
                AddGunSale(price, "sold at the counter: " + item.identifier);
                return;
            }
            if (!Cabinets.IsCabinet(item))
                return;
            long guns = 0, all = Math.Max(1, item.GetNegociatedValue());
            foreach (GameSlotInventory slot in Cabinets.SlotsOf(item))
            {
                if (slot == null)
                    continue;
                foreach (GameItem inside in slot.childItems)
                {
                    if (inside != null && inside.IsGameItemType("FIREARM"))
                        guns += inside.GetNegociatedValue();
                }
            }
            if (guns > 0)
                AddGunSale((int)Math.Min(price, price * guns / all), "guns in a cabinet sold at the counter");
        }

        // Gun orders (338): custom orders, reservations picked up (deposit and the rest together), contract guns; not repair or
        // service orders. Called before GunOrderData.OnOrderAccepted pays (OrderAcceptedPatch), while the gun is the one on the counter.
        internal static void OnOrderAccepted(GunOrderData order)
        {
            if (order == null || order.isServiceOrder)
                return;
            GameItem gun = GunOrderData.ResolveOrderGun();
            if (gun == null)
                return;
            int reward = order.GetTotalReward(gun);
            Reservations.Reservation r = Reservations.PickupOf(order);
            if (r != null)
                AddGunSale(reward + Math.Max(0, r.Deposit), "reservation picked up");
            else if (Contracts.IsCourierOrder(order))
                AddGunSale(reward, "contract gun");
            else
                AddGunSale(reward, "gun order");
        }
    }

    [HarmonyPatch(typeof(StoreClient), nameof(StoreClient.OnItemBought))]
    internal static class LincolnGunSalePatch
    {
        private static void Postfix(GameItem item, int cost)
        {
            try
            {
                Lincoln.OnClientBought(item, cost);
            }
            catch (Exception e)
            {
                GunsmithMod.Warn("Lincoln favor from a sale: " + e.Message);
            }
        }
    }

    [HarmonyPatch(typeof(StorePhoneClient), nameof(StorePhoneClient.OnEndDay))]
    internal static class LincolnNightPatch
    {
        private static void Postfix()
        {
            try
            {
                Lincoln.OnNight();
                LincolnPhone.OnNight();
                BlindBoxes.OnNight();
            }
            catch (Exception e)
            {
                GunsmithMod.Warn("Lincoln at night: " + e.Message);
            }
        }
    }
}
