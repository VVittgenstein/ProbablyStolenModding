using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace GunsmithExpansion
{
    // Collection orders (final-strategy 28, 46). Once the store holds the second firearm permit, the gunsmith sign's customers may be collectors
    // (GunsmithSign; item 17). Until 0.6.2 a quarter of the upper level gun buyers' custom orders were, and the game sends upper level gun buyers
    // only while the third firearm permit is in the display case (ShowcaseHelper.GUN_BUYERS: ll_gun_buyer with permit_gun_1,
    // merc_gun_buyer with permit_gun_2, ul_gun_buyer with permit_gun_3), so they came from the third permit on. A
    // collector wants an old or antique gun, a revolver
    // (the engraved one among them) or a match gun, in excellent condition or new, with a good look, and legal: the
    // order's requirements become the kind of gun, legal, the condition and a style that 60% of the kind's guns reach;
    // the game's bonus requirements on other stats stay. The collector pays for the gun the way the game's best orders
    // do (GunOrderData.GetTotalReward: the market price plus GetBestPremium, 50% of it), plus the bonus rewards met:
    // the game's base rewards follow the order's tier (190 to 210 for tier two, 350 to 370 for tier three, before the
    // game's market and markup changes), so 0.4.0's doubled base reward stayed below the value of three of the four old
    // and antique guns of this mod (570 to 1090 until 0.5.8, 280 to 380 in 0.5.9, 440 to 540 in 0.5.10 and 0.5.11).
    // From 0.5.12 the old and antique guns are worth what their stats give at tier one for everyone else (280 to 380),
    // and the collectors pay on their own value of them, tier three's (Catalog.CollectorValues, 700 to 790): the market
    // price is scaled by that value over the gun's value before the 50% (VVittgenstein 2026-11-03, current/005 quote 58: the
    // upper level people are willing to buy antiques, the others look at the gun as it is).
    // Agent's decisions (VVittgenstein 2026-11-02 left the numbers to the Agent if they fit the game's): the three
    // kinds and their lines, the condition, the style percentile, the reward.
    internal static class Collection
    {
        internal const string ClientId = "gseCollector";
        private const int OrderTries = 8;
        internal const int MinCondition = 3;
        internal static readonly string[] MatchGuns = { "gse_w023", "gse_w095", "gse_w102", "gse_w130", "gse_w135" };
        private static readonly Random rng = new Random();
        private static readonly ConditionalWeakTable<GunOrderData, Theme> orders = new ConditionalWeakTable<GunOrderData, Theme>();

        internal sealed class Theme
        {
            internal string[] Classes;
            internal string[] Models;
            internal string Zh;
            internal string En;
        }

        internal static readonly Theme[] Themes =
        {
            new Theme { Classes = new[] { "老式与古董" },
                Zh = "我收藏老枪。想找一把成色极好的老式枪，样子体面，来路干净，别是违禁品。",
                En = "I collect old guns. I want an antique in excellent condition, handsome, with clean papers. No contraband." },
            new Theme { Classes = new[] { "左轮（含左轮卡宾）" },
                Zh = "我在找一把好看的左轮，有雕花更好。成色要极好，还得合法。",
                En = "I'm looking for a handsome revolver, engraved if you have one. Excellent condition, and it has to be legal." },
            new Theme { Models = MatchGuns,
                Zh = "我收藏比赛用枪。成色要极好，外观要讲究，还得合法。",
                En = "I collect match guns. Excellent condition, a fine finish, and legal." },
        };

        private static List<GunDef> Guns(Theme t)
        {
            var list = new List<GunDef>();
            foreach (GunDef d in Catalog.Guns)
            {
                if (t.Classes != null ? Array.IndexOf(t.Classes, d.ClassName) >= 0 : Array.IndexOf(t.Models, d.Id) >= 0)
                    list.Add(d);
            }
            return list;
        }

        internal static int Style(Theme t)
        {
            int index = Array.IndexOf(ModOrders.Stats, "GUN_CURRENT_STYLE_TAG");
            var values = new List<int>();
            foreach (GunDef d in Guns(t))
                values.Add(d.Stats[index]);
            if (values.Count == 0)
                return 0;
            values.Sort();
            return values[(int)Math.Floor(values.Count * 0.4)];
        }

        // What a collector pays for the gun before the bonus rewards: the market price plus 50% of it, as
        // GunOrderData.GetBestPremium rounds it; an old or antique gun's market price taken at the collectors' value.
        internal static int Pay(GameItem gun)
        {
            int market = GunOrderData.GetMarketPrice(gun);
            GunDef d = gun != null ? RepairOrders.Find(gun.identifier) : null;
            int collector;
            if (d != null && d.Antique && d.Value > 0 && Catalog.CollectorValues.TryGetValue(d.Id, out collector))
                market = (int)Math.Round(market * (double)collector / d.Value);
            return market + UnityEngine.Mathf.RoundToInt(market * 50 / 100f);
        }

        internal static bool IsCollectionOrder(GunOrderData order)
        {
            Theme t;
            return order != null && orders.TryGetValue(order, out t);
        }

        // A task of the gunsmith sign's customers (GunsmithSign.CreateCustomer), from the second firearm permit on.
        internal static bool CanOrder()
        {
            return PermitHelper.IsGunPermitII() || PermitHelper.IsGunPermitIII();
        }

        // Made from a custom order of the game's upper level buyers, whose bonus requirements stay.
        internal static StoreClient CreateSignCustomer()
        {
            GunOrderData order = null;
            for (int i = 0; i < OrderTries && (order == null || order.isServiceOrder || order.isBestOrder); i++)
                order = StoreClientListGun.GetRandomHighOrder();
            if (order == null || order.isServiceOrder || order.isBestOrder)
                order = GunOrderData.CreateHighOrderGeneral();
            Convert(order, Themes[rng.Next(Themes.Length)]);
            StoreClient client = StoreClientListGun.CreateULBuyer(order);
            if (client != null)
                client.identifier = ClientId;
            return client;
        }

        // A requirement is met above its value, so the style is set one below what 60% of the guns reach.
        internal static void Convert(GunOrderData order, Theme t)
        {
            order.mandatoryRequirements.Clear();
            order.mandatoryRequirements.Add(t.Classes != null ? Requirements.Class("", t.Classes) : Requirements.Models(t.Models));
            order.mandatoryRequirements.Add(Requirements.Legal());
            order.mandatoryRequirements.Add(Requirements.Condition(MinCondition));
            order.mandatoryRequirements.Add(new GunOrderRequirement { id = "GUN_CURRENT_STYLE_TAG", value = Style(t) - 1, isMustBeLargerThan = true });
            order.bonusRequirements.RemoveAll(r => r.id == "GUN_CURRENT_STYLE_TAG");
            order.dialogText = Text.Pick(t.Zh, t.En);
            orders.Remove(order);
            orders.Add(order, t);
            GunsmithMod.Log("collection order: " + t.En);
        }
    }

    // The game's total for a custom order is the base reward plus the bonus rewards met; for a collection order the
    // base reward is replaced by the best order's payment for the gun.
    [HarmonyPatch(typeof(GunOrderData), nameof(GunOrderData.GetTotalReward))]
    internal static class CollectionRewardPatch
    {
        private static void Postfix(GunOrderData __instance, GameItem gun, ref int __result)
        {
            if (gun != null && !__instance.isBestOrder && Collection.IsCollectionOrder(__instance))
                __result += Collection.Pay(gun) - __instance.GetBaseReward();
        }
    }
}
