using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace GunsmithExpansion
{
    // Modification orders (final-strategy 23, 54). The game's gun buyers sometimes bring their own gun and ask for one
    // stat to go up by a step (a service order: GunOrderData.CreateServiceOrder picks the gun, CreateServiceGun puts
    // it on the counter and sets the requirements: for each of a tier's main stats a raise of one step, the requirement's
    // value being the current value plus the step less 1, with a reward for two steps, and side stats for one step). Half of these orders (Agent's decision) bring one of
    // this mod's guns of the order's tier that takes gun mods instead. The customer names the gun, what he uses it for
    // (by its class) and the stat he wants higher, and the order's first main stat is that one: the stat drawn by the
    // game is swapped with it, keeping each requirement's distance from the gun's current value. Only stats that this
    // mod's gun mods fitting the gun can raise by at least one step are named. Repair orders (RepairOrders) are left
    // alone.
    internal static class ModOrders
    {
        internal const int ChancePercent = 50;
        internal static readonly string[] Stats =
        {
            "GUN_CURRENT_FIREPOWER_TAG", "GUN_CURRENT_STYLE_TAG", "GUN_CURRENT_RELIABILITY_TAG", "GUN_CURRENT_RECOIL_TAG",
            "GUN_CURRENT_ERGONOMICS_TAG", "GUN_CURRENT_ACCURACY_TAG",
        };
        private static readonly Random rng = new Random();
        private static readonly ConditionalWeakTable<GunOrderData, Plan> plans = new ConditionalWeakTable<GunOrderData, Plan>();

        internal sealed class Plan
        {
            internal GunDef Gun;
            internal string Stat;
        }

        private static readonly Dictionary<string, string[]> Purposes = new Dictionary<string, string[]>
        {
            { "手枪", new[] { "我天天带在身上", "I carry it every day" } },
            { "左轮（含左轮卡宾）", new[] { "跟了我很多年", "This revolver has been with me for years" } },
            { "小口径步枪", new[] { "我拿来打靶，也打些小动物", "I use it for targets and small game" } },
            { "冲锋枪", new[] { "我在下层区跑街全靠它", "I run the lower level streets with it" } },
            { "霰弹枪", new[] { "是店里守夜用的", "It guards my place at night" } },
            { "手枪口径卡宾", new[] { "我出门做护卫时带着", "I take it along on escort jobs" } },
            { "狩猎步枪", new[] { "我在外环打猎用", "I hunt out on the outer ring" } },
            { "杠杆步枪", new[] { "我打猎用，也拿来防身", "I hunt with it and keep myself safe with it" } },
            { "中间威力步枪", new[] { "是我干护卫用的", "I work security" } },
            { "全威力步枪", new[] { "我拿来远距离压制", "I need to hold people off at range" } },
            { "精确步枪", new[] { "我在高处盯街用", "I watch a street from high up" } },
            { "机枪", new[] { "是我们小队压制火力用的", "My crew relies on it to keep heads down" } },
        };

        private static readonly Dictionary<string, string[]> Wishes = new Dictionary<string, string[]>
        {
            { "GUN_CURRENT_FIREPOWER_TAG", new[] { "想让它打得更狠", "I want it to hit harder" } },
            { "GUN_CURRENT_STYLE_TAG", new[] { "想让它看着更体面", "I want it to look sharper" } },
            { "GUN_CURRENT_RELIABILITY_TAG", new[] { "想让它别再出毛病", "I want it to stop acting up" } },
            { "GUN_CURRENT_RECOIL_TAG", new[] { "后坐力太大，想压一压", "I want the kick tamed" } },
            { "GUN_CURRENT_ERGONOMICS_TAG", new[] { "想让它更顺手", "I want it to handle better" } },
            { "GUN_CURRENT_ACCURACY_TAG", new[] { "想让它打得更准", "I want it to shoot straighter" } },
        };

        // GunOrderData.SERVICE_STAT_STEP of the game (read from the assembly 2026-11-02; the same in game 1.0.0.12). It is private,
        // so reading it at run time takes reflection; a version of 2026-11-02 that read it with Traverse was reported by the game's
        // mod scanner as reflection-external (README, 0.2.9).
        private static readonly int[] Steps = { 0, 8, 10, 12 };

        internal static int Step(int tier)
        {
            return Steps[Math.Max(1, Math.Min(3, tier))];
        }

        // The most each stat can go up with this mod's gun mods that fit the gun: the best mod of each slot the gun
        // has open (Fittings.Slots), special mods once. The game adds a mod's stat as a percentage of the gun's base
        // stat plus the mod's own base stat (GunHelper.UpdateCurrentGunStats; this mod's base stats are Items.Base of
        // the percentage).
        internal static int[] MaxRaise(GunDef d)
        {
            var best = new Dictionary<string, int[]>();
            string[] open;
            if (!Fittings.Slots.TryGetValue(d.Id, out open))
                open = new string[0];
            foreach (ModDef m in Catalog.Mods)
            {
                string[] guns;
                if (!Fittings.Guns.TryGetValue(m.Id, out guns) || Array.IndexOf(guns, d.Id) < 0)
                    continue;
                string slot = m.Slot.StartsWith("barrel", StringComparison.Ordinal) ? "barrel" : m.Slot;
                if (Array.IndexOf(open, slot) < 0)
                    continue;
                int[] b;
                if (!best.TryGetValue(slot, out b))
                    best[slot] = b = new int[Stats.Length];
                for (int i = 0; i < Stats.Length; i++)
                    b[i] = Math.Max(b[i], d.Stats[i] * m.Stats[i] / 100 + Items.Base(m.Stats[i]));
            }
            var raise = new int[Stats.Length];
            foreach (int[] b in best.Values)
            {
                for (int i = 0; i < Stats.Length; i++)
                    raise[i] += b[i];
            }
            return raise;
        }

        internal static bool IsModOrder(GunOrderData order)
        {
            Plan p;
            return order != null && plans.TryGetValue(order, out p);
        }

        internal static Plan PlanOf(GunOrderData order)
        {
            Plan p;
            return order != null && plans.TryGetValue(order, out p) ? p : null;
        }

        internal static void TryConvert(GunOrderData order)
        {
            if (order == null || !order.isServiceOrder || order.baseReward <= 0 || RepairOrders.IsRepairOrder(order) || IsModOrder(order)
                || rng.Next(100) >= ChancePercent)
                return;
            Convert(order);
        }

        internal static bool Convert(GunOrderData order)
        {
            string tier = RepairOrders.TierName(order.tier);
            int step = Step(order.tier);
            var guns = new List<KeyValuePair<GunDef, List<string>>>();
            foreach (GunDef d in Catalog.Guns)
            {
                if (d.Tier != tier || d.Slots == Slots.None || !Purposes.ContainsKey(d.ClassName))
                    continue;
                int[] raise = MaxRaise(d);
                var stats = new List<string>();
                // The game's requirement is the current value plus a step less 1 (GunOrderData.CreateServiceGun) and is met above its
                // value (GunOrderRequirement.IsMet), so a raise of one step meets it.
                for (int i = 0; i < Stats.Length; i++)
                {
                    if (raise[i] >= step)
                        stats.Add(Stats[i]);
                }
                if (stats.Count > 0)
                    guns.Add(new KeyValuePair<GunDef, List<string>>(d, stats));
            }
            if (guns.Count == 0)
                return false;
            KeyValuePair<GunDef, List<string>> pick = guns[rng.Next(guns.Count)];
            string stat = pick.Value[rng.Next(pick.Value.Count)];
            Traverse.Create(order).Field("serviceGunId").SetValue(pick.Key.Id);
            order.dialogText = Request(pick.Key, stat);
            plans.Remove(order);
            plans.Add(order, new Plan { Gun = pick.Key, Stat = stat });
            GunsmithMod.Log("modification order on " + pick.Key.Id + " (" + tier + "): " + stat);
            return true;
        }

        internal static string Request(GunDef d, string stat)
        {
            string[] purpose = Purposes[d.ClassName];
            string[] wish = Wishes[stat];
            return string.Format(Text.Pick("这把{1}{0}，{2}。配件钱我另付。", "{0}. With this {1}, {2}. I'll pay for any parts you put on it."),
                Text.Pick(purpose[0], purpose[1]), Text.Pick(d.NameZh, d.NameEn), Text.Pick(wish[0], wish[1]));
        }

        private static int Current(GameItem gun, string stat)
        {
            return gun.IsTag(stat) ? gun.GetTagReadonly(stat).GetInt() : 0;
        }

        // After GunOrderData.CreateServiceGun: the planned stat takes the place of the first main stat.
        internal static void OnServiceGunCreated(GunOrderData order, GameItem gun)
        {
            Plan p = PlanOf(order);
            if (p == null || order.mandatoryRequirements.Count == 0)
                return;
            string first = order.mandatoryRequirements[0].id;
            if (first == p.Stat)
                return;
            var all = new List<GunOrderRequirement>(order.mandatoryRequirements);
            all.AddRange(order.bonusRequirements);
            foreach (GunOrderRequirement r in all)
            {
                if (r.id != first && r.id != p.Stat)
                    continue;
                int offset = r.value - Current(gun, r.id);
                r.id = r.id == first ? p.Stat : first;
                r.value = Current(gun, r.id) + offset;
            }
        }
    }

    // None of the three while the gunsmith sign takes a buyer's look (GunsmithSign.PlainBuyer).
    [HarmonyPatch(typeof(StoreClientListGun), nameof(StoreClientListGun.CreateLLGunBuyer), new[] { typeof(GunOrderData) })]
    internal static class LowerLevelBuyerPatch
    {
        private static void Prefix(ref GunOrderData order)
        {
            if (GunsmithSign.PlainBuyer)
                return;
            ModOrders.TryConvert(order);
            CustomerTypes.TryRetheme(ref order, "LL");
        }

        private static void Postfix(StoreClient __result)
        {
            if (!GunsmithSign.PlainBuyer)
                NewCustomers.Dress(__result);
        }
    }

    [HarmonyPatch(typeof(StoreClientListGun), nameof(StoreClientListGun.CreateMercGunBuyer), new[] { typeof(GunOrderData) })]
    internal static class MercenaryBuyerPatch
    {
        private static void Prefix(ref GunOrderData order)
        {
            if (GunsmithSign.PlainBuyer)
                return;
            ModOrders.TryConvert(order);
            CustomerTypes.TryRetheme(ref order, "Merc");
        }

        private static void Postfix(StoreClient __result)
        {
            if (!GunsmithSign.PlainBuyer)
                NewCustomers.Dress(__result);
        }
    }

    [HarmonyPatch(typeof(StoreClientListGun), nameof(StoreClientListGun.CreateULBuyer), new[] { typeof(GunOrderData) })]
    internal static class UpperLevelBuyerPatch
    {
        private static void Prefix(ref GunOrderData order)
        {
            if (GunsmithSign.PlainBuyer)
                return;
            ModOrders.TryConvert(order);
            CustomerTypes.TryRetheme(ref order, "UL");
        }
    }
}
