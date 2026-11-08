using System;
using System.Collections.Generic;
using HarmonyLib;

namespace GunsmithExpansion
{
    // The game's "best of its kind" orders (GunOrderData.CreateBestOrder: "Give me your best {caliber}", only the caliber
    // is checked, market price plus 50%) remade (VVittgenstein 2026-11-04; final-strategy 119, 122, 125, 130, 133).
    // The caliber and the pay stay the game's way. The gun must also be better than 90% of the guns it is compared with:
    // the guns of the caliber, this mod's and the game's, of the levels up to the one the buyer stands for (the order's
    // tier: lower level buyers 1, mercenaries 2, upper level buyers 3; the game's guns at this mod's levels), by the
    // total of the six stats at good condition. The gun delivered counts with its current stats, so condition and gun
    // mods count (Agent's interpretation). Each buyer asks for one of the calibers below, at even odds.
    internal static class BestOrders
    {
        internal const int BetterThanPercent = 90;
        private static readonly Random rng = new Random();
        private static readonly string[] Tiers = { "一档", "二档", "三档", "特殊" };
        private static readonly Dictionary<int, int> thresholds = new Dictionary<int, int>();

        // By the order's tier.
        private static readonly int[][] Calibers =
        {
            new[] { 22, 10, 45 },
            new[] { 10, 45, 12, 762 },
            new[] { 10, 556, 762 },
        };

        internal static int[] CalibersOf(int tier)
        {
            return Calibers[Math.Max(1, Math.Min(3, tier)) - 1];
        }

        internal static int PickCaliber(int tier)
        {
            int[] list = CalibersOf(tier);
            return list[rng.Next(list.Length)];
        }

        private static int Sum(int[] stats)
        {
            int sum = 0;
            foreach (int s in stats)
                sum += s;
            return sum;
        }

        // The lowest total that is better than BetterThanPercent of the guns compared; 0 when there are none.
        internal static int Threshold(int tier, int caliber)
        {
            int key = tier * 1000 + caliber;
            int t;
            if (thresholds.TryGetValue(key, out t))
                return t;
            var totals = new List<int>();
            foreach (GunDef d in Catalog.Guns)
            {
                if (d.Caliber == caliber && Array.IndexOf(Tiers, d.Tier) + 1 <= tier)
                    totals.Add(Sum(d.Stats));
            }
            foreach (GameGunLevels.GameGun g in GameGunLevels.Guns)
            {
                if (g.Caliber == caliber && g.Level <= tier)
                    totals.Add(Sum(g.Stats));
            }
            totals.Sort();
            t = totals.Count > 0 ? totals[Math.Min(totals.Count - 1, totals.Count * BetterThanPercent / 100)] : 0;
            thresholds[key] = t;
            return t;
        }

        // The six current stats added up, what the row of the order compares.
        internal static int Total(GameItem gun)
        {
            int sum = 0;
            foreach (string stat in ModOrders.Stats)
            {
                if (gun.IsTag(stat))
                    sum += gun.GetTagReadonly(stat).GetInt();
            }
            return sum;
        }

        internal static void OnCreated(GunOrderData order, int tier, int caliber)
        {
            order.mandatoryRequirements.Add(Requirements.BestTotal(Threshold(tier, caliber)));
            // the game's Chinese line names the caliber alone ("拿你最好的10毫米来"), which with this mod's names reads as
            // a request for ammunition; the other languages keep the game's line
            if (Text.CaliberName(caliber) != null && Text.Chinese())
                order.dialogText = string.Format("拿你最好的用{0}的枪来。我按它的价值付钱，再多给一半。", Text.CaliberName(caliber));
            GunsmithMod.Log("best order, tier " + tier + ", caliber " + caliber + ", total at least " + Threshold(tier, caliber));
        }
    }

    [HarmonyPatch(typeof(GunOrderData), nameof(GunOrderData.CreateBestOrder))]
    internal static class BestOrderPatch
    {
        private static void Prefix(int tier, ref int caliber)
        {
            caliber = BestOrders.PickCaliber(tier);
        }

        private static void Postfix(GunOrderData __result, int tier, int caliber)
        {
            BestOrders.OnCreated(__result, tier, caliber);
        }
    }
}
