using System;
using HarmonyLib;
using UnityEngine;

namespace GunsmithExpansion
{
    // Order requirements of this mod next to the game's stat, calibre and suppressor ones: the model of a reserved gun
    // (stage 4), the class and tier of a contract gun, one of several models and the condition of a collector's piece,
    // and legal (not contraband under the store's permits; final-strategy 46) for both (stage 6). Each is a GunOrderRequirement whose id starts with GSE_;
    // the game's GunOrderRequirement.IsMet and GetDisplayName and the negotiation window's current value
    // (NegociationUIManager.CurrentValueSuffix: the row is the display name and this suffix) are answered here. The
    // suffix is written like the game's, the gun's value in brackets, green when met and red when not.
    internal static class Requirements
    {
        private const string ModelPrefix = "GSE_MODEL_";
        private const string AnyOfPrefix = "GSE_ANYOF_";
        private const string ClassPrefix = "GSE_CLASS_";
        private const string LegalId = "GSE_LEGAL";
        private const string ConditionPrefix = "GSE_CONDITION_";
        // a reserved gun (item 19): no fault left, hidden ones included, none repaired only in an emergency way
        private const string NoFaultsId = "GSE_NO_FAULTS";
        // repair orders (RepairOrders, 0.6.0): at least one fault repaired; one row per fault
        private const string RepairedId = "GSE_REPAIRED";
        private const string FaultPrefix = "GSE_FAULT_";
        // the certificate's repair fee as a row of its own (VVittgenstein 2026-11-05 22:40 and 23:38, current/008 quotes 17 and 18, item 11)
        private const string RepairFeeId = "GSE_REPAIR_FEE";
        // the budget of a repair customer who also wants the gun improved (current/008 quote 22 point 70, item 70)
        private const string BudgetId = "GSE_BUDGET";
        // best orders (BestOrders, 0.6.2): the total of the six stats; new customers (NewCustomers): the pay row
        private const string BestTotalPrefix = "GSE_TOTAL_";
        private const string MarketPremiumPrefix = "GSE_MARKET_";

        // The game's condition identifiers from worst to best (GunHelper.UpdateCurrentGunStats).
        private static readonly string[] Conditions =
        {
            "equipment_condition_poor", "equipment_condition_fair", "equipment_condition_good", "equipment_condition_excellent", "equipment_condition_new",
        };

        internal static GunOrderRequirement Model(string gunId)
        {
            return new GunOrderRequirement { id = ModelPrefix + gunId, value = 1, isMustBeLargerThan = true };
        }

        // One of the given models.
        internal static GunOrderRequirement Models(string[] gunIds)
        {
            return new GunOrderRequirement { id = AnyOfPrefix + string.Join("|", gunIds), value = 1, isMustBeLargerThan = true };
        }

        // tier is the catalog's tier name or "" for any tier.
        internal static GunOrderRequirement Class(string tier, string[] classes)
        {
            return new GunOrderRequirement { id = ClassPrefix + tier + ":" + string.Join("|", classes), value = 1, isMustBeLargerThan = true };
        }

        internal static GunOrderRequirement Legal()
        {
            return new GunOrderRequirement { id = LegalId, value = 1, isMustBeLargerThan = true };
        }

        // min: 0 poor to 4 new.
        internal static GunOrderRequirement Condition(int min)
        {
            return new GunOrderRequirement { id = ConditionPrefix + min, value = min, isMustBeLargerThan = true };
        }

        internal static GunOrderRequirement Repaired()
        {
            return new GunOrderRequirement { id = RepairedId, value = 1, isMustBeLargerThan = true };
        }

        internal static GunOrderRequirement NoFaults()
        {
            return new GunOrderRequirement { id = NoFaultsId, value = 1, isMustBeLargerThan = true };
        }

        internal static GunOrderRequirement BestTotal(int atLeast)
        {
            return new GunOrderRequirement { id = BestTotalPrefix + atLeast, value = atLeast, isMustBeLargerThan = true };
        }

        internal static GunOrderRequirement MarketPremium(int percent)
        {
            return new GunOrderRequirement { id = MarketPremiumPrefix + percent, value = percent, isMustBeLargerThan = true };
        }

        internal static bool IsMarketPremium(GunOrderRequirement r)
        {
            return r != null && r.id != null && r.id.StartsWith(MarketPremiumPrefix, StringComparison.Ordinal);
        }

        // GunOrderRequirement.GetReward is answered by FaultRowRewardPatch; met as soon as a fault is repaired, when the order can be accepted.
        internal static GunOrderRequirement RepairFee()
        {
            return new GunOrderRequirement { id = RepairFeeId, value = 1, isMustBeLargerThan = true };
        }

        // The order's total is within the budget of the customer at the counter (RepairOrders.WithinBudget).
        internal static GunOrderRequirement Budget()
        {
            return new GunOrderRequirement { id = BudgetId, value = 1, isMustBeLargerThan = true };
        }

        internal static bool IsRepairFee(GunOrderRequirement r)
        {
            return r != null && r.id == RepairFeeId;
        }

        internal static string FaultRowId(Fault fault)
        {
            return FaultPrefix + fault;
        }

        // reward is the full pay for the gun's tier; GunOrderRequirement.GetReward is answered by FaultRowRewardPatch.
        internal static GunOrderRequirement FaultRow(Fault fault)
        {
            return new GunOrderRequirement { id = FaultRowId(fault), value = 1, isMustBeLargerThan = true };
        }

        internal static bool TryFaultRow(GunOrderRequirement r, out Fault fault)
        {
            fault = Fault.Screws;
            if (r == null || r.id == null || !r.id.StartsWith(FaultPrefix, StringComparison.Ordinal))
                return false;
            string name = r.id.Substring(FaultPrefix.Length);
            foreach (Fault f in Faults.All)
            {
                if (f.ToString() == name)
                {
                    fault = f;
                    return true;
                }
            }
            return false;
        }

        private static bool Repaired(FaultState s)
        {
            return s == FaultState.Fixed || s == FaultState.Emergency;
        }

        internal static bool IsOwn(GunOrderRequirement r)
        {
            return r != null && r.id != null && (r.id.StartsWith(ModelPrefix, StringComparison.Ordinal) || r.id.StartsWith(AnyOfPrefix, StringComparison.Ordinal)
                || r.id.StartsWith(ClassPrefix, StringComparison.Ordinal) || r.id == LegalId || r.id.StartsWith(ConditionPrefix, StringComparison.Ordinal)
                || r.id == RepairedId || r.id == NoFaultsId || r.id == RepairFeeId || r.id == BudgetId || r.id.StartsWith(FaultPrefix, StringComparison.Ordinal)
                || r.id.StartsWith(BestTotalPrefix, StringComparison.Ordinal) || r.id.StartsWith(MarketPremiumPrefix, StringComparison.Ordinal));
        }

        // The class and tier of this mod's guns and of the game's own at this mod's levels (GameGunLevels).
        private static bool ClassOf(GameItem gun, out string className, out string tier)
        {
            GunDef d = RepairOrders.Find(gun.identifier);
            GameGunLevels.GameGun g = d == null ? GameGunLevels.Find(gun.identifier) : null;
            className = d != null ? d.ClassName : g != null ? g.ClassName : null;
            tier = d != null ? d.Tier : g != null ? RepairOrders.TierName(g.Level) : null;
            return className != null;
        }

        private static void ParseClass(string id, out string tier, out string[] classes)
        {
            string body = id.Substring(ClassPrefix.Length);
            int colon = body.IndexOf(':');
            tier = colon > 0 ? body.Substring(0, colon) : "";
            classes = body.Substring(colon + 1).Split('|');
        }

        internal static int ConditionRank(GameItem gun)
        {
            ItemFeature feature = gun != null ? gun.FindItemFeatureByCategory("CATEGORY_EQUIPMENT_CONDITION") : null;
            return feature != null && feature.realCondition != null ? Array.IndexOf(Conditions, feature.realCondition.identifier) : -1;
        }

        internal static string ConditionName(int rank)
        {
            switch (rank)
            {
                case 0: return Text.Pick("差", "poor");
                case 1: return Text.Pick("一般", "fair");
                case 2: return Text.Pick("良好", "good");
                case 3: return Text.Pick("极好", "excellent");
                case 4: return Text.Pick("全新", "new");
                default: return "?";
            }
        }

        internal static bool IsLegal(GameItem gun)
        {
            return gun != null && ContrabandHelper.GetContrabandLevel(gun) == 0;
        }

        internal static bool IsMet(GunOrderRequirement r, GameItem item)
        {
            if (item == null)
                return false;
            if (r.id.StartsWith(ModelPrefix, StringComparison.Ordinal))
                return item.identifier == r.id.Substring(ModelPrefix.Length);
            if (r.id.StartsWith(AnyOfPrefix, StringComparison.Ordinal))
                return Array.IndexOf(r.id.Substring(AnyOfPrefix.Length).Split('|'), item.identifier) >= 0;
            if (r.id.StartsWith(ClassPrefix, StringComparison.Ordinal))
            {
                string tier;
                string[] classes;
                ParseClass(r.id, out tier, out classes);
                string gunClass, gunTier;
                return ClassOf(item, out gunClass, out gunTier) && Array.IndexOf(classes, gunClass) >= 0 && (tier.Length == 0 || gunTier == tier);
            }
            if (r.id == LegalId)
                return IsLegal(item);
            if (r.id.StartsWith(BestTotalPrefix, StringComparison.Ordinal))
                return BestOrders.Total(item) >= r.value;
            if (r.id.StartsWith(MarketPremiumPrefix, StringComparison.Ordinal))
                return true;
            if (r.id.StartsWith(ConditionPrefix, StringComparison.Ordinal))
                return ConditionRank(item) >= r.value;
            if (r.id == BudgetId)
                return RepairOrders.WithinBudget(item);
            if (r.id == NoFaultsId)
            {
                foreach (FaultState s in Faults.Get(item).Values)
                {
                    if (s != FaultState.Fixed)
                        return false;
                }
                return true;
            }
            if (r.id == RepairedId || r.id == RepairFeeId)
            {
                foreach (FaultState s in Faults.Get(item).Values)
                {
                    if (Repaired(s))
                        return true;
                }
                return false;
            }
            Fault fault;
            if (TryFaultRow(r, out fault))
            {
                FaultState s;
                return Faults.Get(item).TryGetValue(fault, out s) && Repaired(s);
            }
            return false;
        }

        internal static string DisplayName(GunOrderRequirement r)
        {
            if (r.id.StartsWith(ModelPrefix, StringComparison.Ordinal))
            {
                GunDef d = RepairOrders.Find(r.id.Substring(ModelPrefix.Length));
                return Text.Pick("型号：", "Model: ") + (d != null ? Text.Pick(d.NameZh, d.NameEn) : "?");
            }
            if (r.id.StartsWith(AnyOfPrefix, StringComparison.Ordinal))
            {
                string[] names = Array.ConvertAll(r.id.Substring(AnyOfPrefix.Length).Split('|'), id =>
                {
                    GunDef d = RepairOrders.Find(id);
                    return d != null ? Text.Pick(d.NameZh, d.NameEn) : id;
                });
                return Text.Pick("型号：", "Model: ") + string.Join(Text.Pick("、", ", "), names);
            }
            if (r.id.StartsWith(ClassPrefix, StringComparison.Ordinal))
            {
                string tier;
                string[] classes;
                ParseClass(r.id, out tier, out classes);
                string[] names = Array.ConvertAll(classes, Text.ClassName);
                string list = string.Join(Text.Pick("或", " or "), names);
                return Text.Pick("类别：", "Class: ") + list + (tier.Length > 0 ? Text.Pick("（" + Text.TierName(tier) + "）", " (" + Text.TierName(tier) + ")") : "");
            }
            if (r.id == LegalId)
                return Text.Pick("合法：不是违禁品", "Legal: not contraband");
            if (r.id.StartsWith(ConditionPrefix, StringComparison.Ordinal))
                return string.Format(Text.Pick("成色：{0}或更好", "Condition: {0} or better"), ConditionName(r.value));
            if (r.id == RepairedId)
                return Text.Pick("至少修好一项故障", "At least one fault repaired");
            if (r.id == NoFaultsId)
                return Text.Pick("故障全部修好", "Every fault fully repaired");
            if (r.id.StartsWith(BestTotalPrefix, StringComparison.Ordinal))
                return string.Format(Text.Pick("六项属性总分：不低于 {0}", "Total of the six stats: at least {0}"), r.value);
            if (r.id.StartsWith(MarketPremiumPrefix, StringComparison.Ordinal))
                return string.Format(Text.Pick("市场价，另加 {0}%", "Market price, plus {0}%"), r.value);
            if (r.id == RepairFeeId)
                return string.Format(Text.Pick("{0} 级维修费", "Level {0} repair fee"), Certificates.Rank());
            if (r.id == BudgetId)
            {
                StoreClient client = RepairOrders.CurrentCustomer();
                return string.Format(Text.Pick("合计不超过预算 {0}", "Total within the budget of {0}"), client != null ? client.GetBudget() : 0);
            }
            Fault fault;
            if (TryFaultRow(r, out fault))
                return Text.FaultName(fault);
            return r.id;
        }

        // What the gun has, for the brackets after the row; null leaves the brackets out.
        private static string Current(GunOrderRequirement r, GameItem gun)
        {
            if (r.id.StartsWith(ClassPrefix, StringComparison.Ordinal))
            {
                string gunClass, gunTier;
                return ClassOf(gun, out gunClass, out gunTier) ? Text.ClassName(gunClass) + Text.Pick("，", ", ") + Text.TierName(gunTier) : Text.Pick("其他枪", "another gun");
            }
            if (r.id.StartsWith(BestTotalPrefix, StringComparison.Ordinal))
                return BestOrders.Total(gun).ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (r.id == LegalId)
                return IsLegal(gun) ? Text.Pick("合法", "legal") : Text.Pick("违禁", "contraband");
            if (r.id.StartsWith(ConditionPrefix, StringComparison.Ordinal))
                return ConditionName(ConditionRank(gun));
            if (r.id == BudgetId)
                return string.Format(Text.Pick("合计 {0}", "total {0}"), RepairOrders.CurrentTotal(gun));
            if (r.id == RepairedId)
            {
                int done = 0;
                foreach (FaultState s in Faults.Get(gun).Values)
                {
                    if (Repaired(s))
                        done++;
                }
                return string.Format(Text.Pick("已修 {0} 项", "{0} repaired"), done);
            }
            Fault fault;
            if (TryFaultRow(r, out fault))
            {
                FaultState s;
                Faults.Get(gun).TryGetValue(fault, out s);
                return Text.FaultStateName(s);
            }
            return null;
        }

        internal static string Suffix(GunOrderRequirement r, bool met, GameItem gun)
        {
            string value = gun != null ? Current(r, gun) : null;
            if (value == null)
                return "";
            return " <color=#" + ColorUtility.ToHtmlStringRGB(met ? TabletStyle.GREEN : TabletStyle.RED) + ">(" + value + ")</color>";
        }
    }

    [HarmonyPatch(typeof(GunOrderRequirement), nameof(GunOrderRequirement.IsMet))]
    internal static class OwnRequirementPatch
    {
        private static bool Prefix(GunOrderRequirement __instance, GameItem item, ref bool __result)
        {
            if (!Requirements.IsOwn(__instance))
                return true;
            __result = Requirements.IsMet(__instance, item);
            return false;
        }
    }

    [HarmonyPatch(typeof(GunOrderRequirement), nameof(GunOrderRequirement.GetDisplayName))]
    internal static class OwnRequirementNamePatch
    {
        private static bool Prefix(GunOrderRequirement __instance, ref string __result)
        {
            if (!Requirements.IsOwn(__instance))
                return true;
            __result = Requirements.DisplayName(__instance);
            return false;
        }
    }

    [HarmonyPatch(typeof(NegociationUIManager), "CurrentValueSuffix")]
    internal static class OwnRequirementSuffixPatch
    {
        private static bool Prefix(GunOrderRequirement requirement, bool met, GameItem gun, ref string __result)
        {
            if (!Requirements.IsOwn(requirement))
                return true;
            __result = Requirements.Suffix(requirement, met, gun);
            return false;
        }
    }
}
