using System;
using System.Collections.Generic;

namespace GunsmithExpansion
{
    // Customer types for the game's custom gun orders (final-strategy 22, 30, 54). Half of the custom orders (not the
    // service orders) of the game's gun buyers are given a customer type that fits the buyer: lower level residents come
    // as lower level buyers, hunters and warehouse guards as mercenaries, collectors and sport shooters as upper level
    // buyers. The type names what the gun is for and which stats matter, and the order's required stats are those: for
    // each, the value that ReachPercent (90%) of the guns of the type's classes reach in good condition, this mod's guns
    // and the game's own guns of those classes counted together (final-strategy 120, VVittgenstein 2026-11-04 16:08
    // and 16:23; until 0.6.1 it was 60% of this mod's guns only, and the 75th percentile for the tier three types).
    // The game's best orders ("give me your best .22") are BestOrders', not rewritten here (final-strategy 122). The
    // game's reward, bonus requirements and tier stay. The gang member and the two rifle mercenaries, which the filter
    // in Pick never picked, became customers of their own in 0.6.2 (NewCustomers), and from 0.7 TryRetheme draws those among the buyer's types. Agent's decisions: the share, the
    // types' classes and stats, the lines.
    internal static class CustomerTypes
    {
        internal const int ChancePercent = 50;
        private static readonly Random rng = new Random();

        internal sealed class CustomerType
        {
            internal string Kind;
            internal int Tier;
            internal string[] Classes;
            internal string[] Stats;
            internal string Zh;
            internal string En;
        }

        internal static readonly CustomerType[] Types =
        {
            new CustomerType { Kind = "LL", Tier = 1, Classes = new[] { "手枪", "左轮（含左轮卡宾）" }, Stats = new[] { "GUN_CURRENT_RELIABILITY_TAG" },
                Zh = "我住下层区，想要一把靠得住的手枪防身。关键时候别卡壳。", En = "I live down in the lower level. I need a pistol I can rely on, one that won't jam when it counts." },
            new CustomerType { Kind = "Merc", Tier = 2, Classes = new[] { "狩猎步枪", "杠杆步枪" }, Stats = new[] { "GUN_CURRENT_ACCURACY_TAG" },
                Zh = "我在外环打猎，要一把打得准的步枪。", En = "I hunt out on the outer ring. I need a rifle that shoots straight." },
            new CustomerType { Kind = "Merc", Tier = 2, Classes = new[] { "霰弹枪" }, Stats = new[] { "GUN_CURRENT_FIREPOWER_TAG", "GUN_CURRENT_RELIABILITY_TAG" },
                Zh = "我给人看仓库，要一把霰弹枪。威力要大，还不能出毛病。", En = "I guard a warehouse. I need a shotgun that hits hard and never fails." },
            new CustomerType { Kind = "UL", Tier = 1, Classes = new[] { "手枪", "小口径步枪" }, Stats = new[] { "GUN_CURRENT_ACCURACY_TAG", "GUN_CURRENT_ERGONOMICS_TAG" },
                Zh = "我练比赛射击，要一把打得准、握着舒服的枪。", En = "I shoot in competitions. I need a gun that's accurate and comfortable to hold." },
            new CustomerType { Kind = "UL", Tier = 1, Classes = new[] { "左轮（含左轮卡宾）", "老式与古董" }, Stats = new[] { "GUN_CURRENT_STYLE_TAG" },
                Zh = "我收藏枪。想要一把好看的，摆出来有面子。", En = "I collect guns. I want a handsome one, something worth showing off." },
        };

        internal const int ReachPercent = 90;

        // The value ReachPercent of the guns reach: the guns' values from low to high, the lowest (100 - ReachPercent)%
        // skipped.
        internal static int Threshold(string[] classes, string stat)
        {
            int index = Array.IndexOf(ModOrders.Stats, stat);
            var values = new List<int>();
            foreach (GunDef d in Catalog.Guns)
            {
                if (Array.IndexOf(classes, d.ClassName) >= 0)
                    values.Add(d.Stats[index]);
            }
            foreach (GameGunLevels.GameGun g in GameGunLevels.Guns)
            {
                if (Array.IndexOf(classes, g.ClassName) >= 0)
                    values.Add(g.Stats[index]);
            }
            if (values.Count == 0)
                return 0;
            values.Sort();
            return values[(int)Math.Floor(values.Count * (100 - ReachPercent) / 100.0)];
        }

        private static List<CustomerType> Fit(string kind, int tier)
        {
            var fit = new List<CustomerType>();
            foreach (CustomerType t in Types)
            {
                if (t.Kind == kind && t.Tier <= Math.Max(1, tier))
                    fit.Add(t);
            }
            return fit;
        }

        internal static CustomerType Pick(string kind, int tier)
        {
            List<CustomerType> fit = Fit(kind, tier);
            return fit.Count > 0 ? fit[rng.Next(fit.Count)] : null;
        }

        internal static void TryRetheme(ref GunOrderData order, string kind)
        {
            if (order == null || order.isServiceOrder || order.isBestOrder || Reservations.PickupOf(order) != null || Contracts.IsCourierOrder(order)
                || Collection.IsCollectionOrder(order) || NewCustomers.IsOrder(order))
                return;
            if (rng.Next(100) >= ChancePercent)
                return;
            // One draw among the buyer's types and the new customers who stand in for this buyer (NewCustomers), so that each has its
            // share of the custom orders. A new customer whose condition does not hold leaves its share to the types.
            List<CustomerType> fit = Fit(kind, order.tier);
            List<NewCustomers.Kind> standIns = NewCustomers.StandIns(kind);
            if (fit.Count + standIns.Count == 0)
                return;
            int i = rng.Next(fit.Count + standIns.Count);
            if (i >= fit.Count)
            {
                NewCustomers.Kind k = standIns[i - fit.Count];
                if (k.Allowed())
                {
                    order = NewCustomers.NewOrder(k);
                    return;
                }
                if (fit.Count == 0)
                    return;
                i = rng.Next(fit.Count);
            }
            Retheme(order, fit[i]);
        }

        // A requirement is met above its value, so each is set one below the threshold.
        internal static void Retheme(GunOrderData order, CustomerType type)
        {
            order.mandatoryRequirements.Clear();
            foreach (string stat in type.Stats)
                order.mandatoryRequirements.Add(new GunOrderRequirement { id = stat, value = Threshold(type.Classes, stat) - 1, isMustBeLargerThan = true });
            order.bonusRequirements.RemoveAll(r => Array.IndexOf(type.Stats, r.id) >= 0);
            order.dialogText = Text.Pick(type.Zh, type.En);
            GunsmithMod.Log("custom order for " + type.Kind + " tier " + type.Tier + ": " + type.En);
        }
    }
}
