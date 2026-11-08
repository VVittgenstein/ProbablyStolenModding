using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;

namespace GunsmithExpansion
{
    // Faults of this mod's guns (final-strategy 15 to 17). Screws, wiring and rust are surface faults the magnifier
    // finds; worn parts, a cracked weld and a bent barrel are internal faults the X-ray scanner finds. A fault starts
    // hidden or known (the customer named it) and ends repaired with tool and material, or with the tool alone
    // (emergency repair). All of it is kept in string tags of the gun, which the game saves with the item.
    internal enum Fault { Screws, Wiring, Rust, PartWear, Weld, Barrel }

    internal enum FaultState { Hidden = 0, Known = 1, Emergency = 2, Fixed = 3 }

    internal static class Faults
    {
        internal const string FaultsTag = "GSE_FAULTS";
        internal const string SurfaceCheckedTag = "GSE_SURFACE_CHECKED";
        internal const string InternalCheckedTag = "GSE_INTERNAL_CHECKED";
        // Which of the two cleaning items an emergency cleaning used (item 8; Repair.Cleaning, RepairOrders.FaultPay). A gun cleaned in an emergency before 0.7 has neither.
        internal const string RustByAlcoholTag = "GSE_RUST_BY_ALCOHOL";
        internal const string RustByFabricTag = "GSE_RUST_BY_FABRIC";
        internal static readonly Fault[] All = { Fault.Screws, Fault.Wiring, Fault.Rust, Fault.PartWear, Fault.Weld, Fault.Barrel };

        internal static bool IsModGun(GameItem item)
        {
            return item != null && item.identifier != null && item.identifier.StartsWith("gse_w", StringComparison.Ordinal);
        }

        internal static bool IsSurface(Fault f)
        {
            return f == Fault.Screws || f == Fault.Wiring || f == Fault.Rust;
        }

        internal static bool HasFaults(GameItem gun)
        {
            return IsModGun(gun) && gun.IsTag(FaultsTag) && Get(gun).Count > 0;
        }

        // "Screws=1;Rust=0": fault name and FaultState number, in the order the faults were added.
        internal static Dictionary<Fault, FaultState> Get(GameItem gun)
        {
            var faults = new Dictionary<Fault, FaultState>();
            if (gun == null || !gun.IsTag(FaultsTag))
                return faults;
            string text = gun.GetTagReadonly(FaultsTag).GetString();
            if (string.IsNullOrEmpty(text))
                return faults;
            foreach (string part in text.Split(';'))
            {
                string[] kv = part.Split('=');
                Fault f;
                int state;
                if (kv.Length == 2 && TryParseFault(kv[0], out f) && int.TryParse(kv[1], out state) && state >= 0 && state <= 3)
                    faults[f] = (FaultState)state;
            }
            return faults;
        }

        private static bool TryParseFault(string name, out Fault fault)
        {
            foreach (Fault f in All)
            {
                if (f.ToString() == name)
                {
                    fault = f;
                    return true;
                }
            }
            fault = Fault.Screws;
            return false;
        }

        internal static void Set(GameItem gun, Dictionary<Fault, FaultState> faults)
        {
            var sb = new StringBuilder();
            foreach (KeyValuePair<Fault, FaultState> kv in faults)
            {
                if (sb.Length > 0)
                    sb.Append(';');
                sb.Append(kv.Key.ToString()).Append('=').Append((int)kv.Value);
            }
            string value = sb.ToString();
            gun.EnableTag(FaultsTag, false);
            gun.ModifyTag(FaultsTag, s => s.SetString(value), false);
            UpdateCondition(gun);
        }

        // The wiring fault needs electrics, which the old and antique guns do not have (Agent's decision).
        internal static List<Fault> Possible(bool antique)
        {
            var list = new List<Fault>(All);
            if (antique)
                list.Remove(Fault.Wiring);
            return list;
        }

        // `count` different faults, the first `known` of them known, the rest hidden.
        internal static Dictionary<Fault, FaultState> RandomFaults(bool antique, int count, int known, Random rng)
        {
            List<Fault> pool = Possible(antique);
            var faults = new Dictionary<Fault, FaultState>();
            for (int i = 0; i < count && pool.Count > 0; i++)
            {
                int k = rng.Next(pool.Count);
                faults[pool[k]] = i < known ? FaultState.Known : FaultState.Hidden;
                pool.RemoveAt(k);
            }
            return faults;
        }

        // Item 9 (VVittgenstein 2026-11-06, current/008 quote 36): at least one of the faults a repair customer names can be repaired in the store now, at least in an emergency way
        // (`can`: Repair.RepairableNow). When none of them can, a repairable fault of the gun that he does not name takes the place of a named one; when the gun has none, a repairable fault
        // it does not have takes that place. False when the store can repair no fault this gun can have.
        internal static bool NameOneRepairable(Dictionary<Fault, FaultState> faults, bool antique, HashSet<Fault> can, Random rng)
        {
            var named = new List<Fault>();
            var hidden = new List<Fault>();
            foreach (KeyValuePair<Fault, FaultState> kv in faults)
            {
                if (kv.Value == FaultState.Known)
                {
                    if (can.Contains(kv.Key))
                        return true;
                    named.Add(kv.Key);
                }
                else if (can.Contains(kv.Key))
                {
                    hidden.Add(kv.Key);
                }
            }
            if (named.Count == 0)
                return false;
            Fault swapped = named[rng.Next(named.Count)];
            if (hidden.Count > 0)
            {
                faults[hidden[rng.Next(hidden.Count)]] = FaultState.Known;
                faults[swapped] = FaultState.Hidden;
                return true;
            }
            List<Fault> others = Possible(antique).FindAll(f => can.Contains(f) && !faults.ContainsKey(f));
            if (others.Count == 0)
                return false;
            faults.Remove(swapped);
            faults[others[rng.Next(others.Count)]] = FaultState.Known;
            return true;
        }

        internal static void AddRandom(GameItem gun, int count, int known, Random rng)
        {
            Set(gun, RandomFaults(gun.IsTag(GunsItemDirectory.ANTIQUE_FIREARM_TAG), count, known, rng));
        }

        // The game's conditions are New, Excellent, Good, Fair and Poor. A gun with a fault that is not repaired is Poor,
        // one whose faults are all repaired but some only in an emergency way is Fair, and one with every fault
        // repaired with material is Good, the condition this mod's guns are made in (Agent's decision); with the repair
        // certificate's fourth level it is Excellent (final-strategy 96).
        internal static void UpdateCondition(GameItem gun)
        {
            Dictionary<Fault, FaultState> faults = Get(gun);
            if (faults.Count == 0)
                return;
            ItemFeatureList.EquipmentCondition condition = ItemFeatureList.EquipmentCondition.Good;
            foreach (FaultState s in faults.Values)
            {
                if (s == FaultState.Hidden || s == FaultState.Known)
                {
                    condition = ItemFeatureList.EquipmentCondition.Poor;
                    break;
                }
                if (s == FaultState.Emergency)
                    condition = ItemFeatureList.EquipmentCondition.Fair;
            }
            if (condition == ItemFeatureList.EquipmentCondition.Good && Certificates.Rank() >= 4)
                condition = ItemFeatureList.EquipmentCondition.Excellent;
            if (GunFrameHelper.GetCondition(gun) != condition)
                GunHelper.SetEquipmentCondition(gun, condition);
            UpdateValue(gun, condition == ItemFeatureList.EquipmentCondition.Poor);
        }

        // A damaged gun is worth about 30% of a whole one (final-strategy 21: bought for 25 to 35%). The game adds the
        // value modifiers of an item's features: Poor condition gives -25, this feature -45 while a fault is not
        // repaired; it goes away when every fault is repaired, which leaves Good (0) or Fair (-12).
        internal const string ValueFeatureCategory = "CATEGORY_GSE_FAULTS";
        private const int ValueModifier = -45;

        private static void UpdateValue(GameItem gun, bool unrepaired)
        {
            ItemFeature feature = gun.FindItemFeatureByCategory(ValueFeatureCategory);
            if (unrepaired && feature == null)
            {
                feature = new ItemFeature(ValueFeatureCategory);
                feature.identifier = ValueFeatureCategory;
                string text = Text.Pick("有未修的故障", "Unrepaired faults");
                feature.SetPublicDisplay(text);
                feature.SetActualDisplay(text);
                feature.SetValueModifier(ValueModifier);
                feature.SetExposed(true);
                gun.AddItemFeature(feature);
            }
            else if (!unrepaired && feature != null)
            {
                gun.RemoveFeatureByCategory(ValueFeatureCategory);
            }
        }

        // The fault lines of the gun's tooltip (0.6.2 layout, VVittgenstein 2026-11-04 current/006 quote 35 point 1): each fault
        // still to repair as a list line of the game (LocHelper.BulletLine), under it the full and the emergency repair on
        // lines of their own in the game's colour for explanations, then the two checks as list lines. Every line is cut
        // to the tooltip's width by Lines, so none of them is broken by the game.
        // A fault repaired with material is no longer listed, and the section goes once nothing is left to repair and both
        // checks are done (VVittgenstein 2026-11-05, current/008 quote 1 point 2 B: until 0.6.2 it stayed on a gun with every fault
        // repaired). A check not done keeps its line, or the section's end would tell the player no hidden fault is left.
        internal static void AppendTooltip(RichTextBuilder builder, GameItem gun)
        {
            if (!IsModGun(gun) || !gun.IsTag(FaultsTag))
                return;
            Dictionary<Fault, FaultState> faults = Get(gun);
            if (faults.Count == 0)
                return;
            bool surface = gun.IsTag(SurfaceCheckedTag), inside = gun.IsTag(InternalCheckedTag);
            var open = new List<KeyValuePair<Fault, FaultState>>();
            foreach (KeyValuePair<Fault, FaultState> kv in faults)
            {
                if (kv.Value == FaultState.Known || kv.Value == FaultState.Emergency)
                    open.Add(kv);
            }
            if (open.Count == 0 && surface && inside)
                return;
            builder.AddLine(open.Count > 0 ? Text.Pick("故障", "Faults") : Text.Pick("故障检查", "Fault checks"), true, RenderHandler.ColorPalette.Yellow);
            foreach (KeyValuePair<Fault, FaultState> kv in open)
            {
                Lines.Add(builder, "", LocHelper.BulletLine(Text.FaultName(kv.Key) + Text.Pick("：", ": ") + Text.FaultStateName(kv.Value)),
                    RenderHandler.ColorPalette.None);
                foreach (string how in Repair.HowTo(kv.Key, kv.Value, gun))
                    Lines.Add(builder, "   ", how, RenderHandler.ColorPalette.LightCyan);
            }
            Lines.Add(builder, "", LocHelper.BulletLine(Text.Pick("表面检查（放大镜）：", "Surface check (magnifier): ")
                + (surface ? Text.Pick("已做", "done") : Text.Pick("未做", "not done"))), RenderHandler.ColorPalette.None);
            Lines.Add(builder, "", LocHelper.BulletLine(Text.Pick("内部检查（便携式X射线扫描仪）：", "Internal check (portable X-ray scanner): ")
                + (inside ? Text.Pick("已做", "done") : Text.Pick("未做", "not done"))), RenderHandler.ColorPalette.None);
        }
    }

    // ItemFeature.GetExposedString reaches GetFormattedActualModifier, which reads realCondition.modValue without checking
    // useCondition; the "unrepaired faults" feature of Faults.UpdateValue has no conditions, so the game's method throws
    // (read 2026-11-05 and called outside the game, ProbablyStolen review/2026-11-05/no-trade-button-report.md). Its one
    // caller, NegociationUIManager.BuildExpandedRow, draws the expanded row of the item alone on the counter, and the
    // exception left the window without its footer and its buy or sell button (Xiaoheihe and Steam reports, current/006).
    // This covers the guns already in saved games too, and nothing is saved differently.
    [HarmonyPatch(typeof(ItemFeature), nameof(ItemFeature.GetExposedString))]
    internal static class FaultFeatureTextPatch
    {
        private static bool Prefix(ItemFeature __instance, ref string __result)
        {
            if (!__instance.IsCategory(Faults.ValueFeatureCategory))
                return true;
            __result = __instance.GetActualDisplay() + " (" + __instance.GetActualValueModifier() + "%)";
            return false;
        }
    }
}
