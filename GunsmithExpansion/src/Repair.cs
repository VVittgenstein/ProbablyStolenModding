using System.Collections.Generic;
using HarmonyLib;

namespace GunsmithExpansion
{
    // What the game's tools and materials do to this mod's guns (final-strategy 15 to 17). A material repairs a known
    // fault fully when the tool it goes with is owned somewhere in the store; the tool alone repairs it in an emergency
    // way. Rust and fouling take rubbing alcohol and kotton fabric together, one of them alone being the emergency way, and
    // after that only the other one finishes it; a use of the fabric takes 200 of its durability of 1000, so a fabric lasts 5 uses
    // (item 8: Xiaoheihe comment A2; the numbers in GPT's reply, VVittgenstein 2026-11-06, current/008 quote 36);
    // a liquid container of the player's alcohol counts as rubbing alcohol, 25 ml a use (Chemistry, cleaning solvent);
    // worn parts take a gun part, and a portion of 3D printing filament (printer_plastic) is their emergency way, used
    // up like the alcohol (final-strategy 92, VVittgenstein 2026-11-03 22:13; from
    // 0.5.2 to 0.5.15 it was the game's animal fat). The magnifier finds the hidden surface faults, the X-ray scanner the
    // hidden internal ones. Refurbishing with gun parts waits for the known faults only: a hidden fault the player has
    // not found does not hold it back (Steam comment of 2026-11-03, current/006 section 7).
    //
    // The game decides through GameItem.MayTarget and CanTarget whether an item may be used on another, and
    // GameItem.Target then runs every use action of both items. The game's own actions do not all check what they are
    // used on (the magnifier opens the inspection window, the X-ray scanner the supply crate window), so the uses
    // below replace GameItem.Target for these pairs instead of adding to it. Without such a use the screwdriver keeps
    // the game's action, taking the gun apart (GunHelper.DisassembleWeapon); for this mod's guns that gives back gun
    // parts worth 20% of the gun's value (final-strategy 53: VVittgenstein allowed it 2026-11-01 13:18 and took the amount at
    // 13:39), less than the 25% a damaged gun costs at the cheapest. The plasma cutter on a metal ingot cuts it into
    // scrap metal (IngotCut in Supply.cs), a blank printer chip on a gun surveys it (Survey in Production.cs), and a
    // reed filter on a small bottle makes a makeshift suppressor (ReedSuppressor in Suppressor.cs); these are handled
    // here with the other uses.
    internal static class Repair
    {
        internal const string ServiceTag = "GSE_SERVICE";
        private const int PartsPercentOfValue = 20;
        private const int GunPartValue = 25;
        internal const int FabricUseDurability = 200;

        private sealed class Plan
        {
            internal bool Inspect;
            internal bool Surface;
            internal Fault Fault;
            internal FaultState Result;
            internal bool UseUpSource;
            internal GameItem UseUpOther;
            internal bool SourceIsSolvent;
            internal bool OtherIsSolvent;
            internal bool SourceIsFabric;
            internal bool OtherIsFabric;
            // the tag an emergency cleaning leaves on the gun
            internal string RustBy;
        }

        private static readonly HashSet<string> Sources = new HashSet<string>
        {
            "magnifier", "xray_scanner", "screwdriver", "wire_cutter", "welder", "nuts_metal", "wire", "common_electronic",
            "scrap_metal", "metal_ingot", "gun_part", "printer_plastic", "rubbing_alcohol", "kotton_fabric",
        };

        // null leaves the game's answer.
        internal static bool? Allow(GameItem source, GameItem target)
        {
            if (IngotCut.Applies(source, target) || ReedSuppressor.Applies(source, target) || Cabinets.MayUpgrade(source, target))
                return true;
            SurveyPlan sample = Survey.Sample(source, target);
            if (sample != null)
                return Survey.CanDo(sample, source);
            if (source == null || !Faults.IsModGun(target) || (!Sources.Contains(source.identifier) && !Chemistry.IsSolvent(source)))
                return null;
            if (Find(source, target) != null)
                return true;
            if (source.IsID("gun_part") && Unfinished(target))
                return false;
            return null;
        }

        // true when this mod handled the use, so the game's GameItem.Target does not run.
        internal static bool Handle(GameItem source, GameItem target)
        {
            if (IngotCut.Applies(source, target))
            {
                IngotCut.Cut(source, target);
                return true;
            }
            if (ReedSuppressor.Applies(source, target))
            {
                ReedSuppressor.Make(source, target);
                return true;
            }
            if (Cabinets.MayUpgrade(source, target))
            {
                Cabinets.Upgrade(source, target);
                return true;
            }
            SurveyPlan sample = Survey.Sample(source, target);
            if (sample != null && Survey.CanDo(sample, source))
            {
                Survey.Do(source, sample);
                return true;
            }
            if (source == null || !Faults.IsModGun(target))
                return false;
            Plan plan = Find(source, target);
            if (plan == null)
                return false;
            Apply(plan, source, target);
            return true;
        }

        internal static int PartsFor(GameItem gun)
        {
            return System.Math.Max(1, (int)(gun.unitValue * PartsPercentOfValue / 100 / GunPartValue));
        }

        // In place of GunHelper.DisassembleWeapon for this mod's guns: the same steps, then the gun parts.
        internal static void Disassemble(GameItem gun)
        {
            if (!gun.IsTag("ADV_GUN_TAG"))
                return;
            GameInventory parent = gun.parentInventory;
            if (parent == null)
                return;
            int parts = PartsFor(gun);
            gun.TryUnloadAll();
            gun.Destroy();
            for (int i = 0; i < parts; i++)
                GraphUtils.EmporiumTryAdd(parent, DirectoryMaster.Item("gun_part", true));
            // the game's DisassembleWeapon ends with this sound
            if (EmporiumAudioPlayer.Instance != null && EmporiumAudioPlayer.Instance.ratchet != null)
                EmporiumAudioPlayer.Instance.ratchet.Play();
            GunsmithMod.Log(gun.identifier + " taken apart into " + parts + " gun parts");
        }

        // The line the game shows while an item is dragged over another (TooltipUseHelper), for the uses above and
        // for taking this mod's guns apart; null leaves the game's line.
        internal static string UseLine(GameItem source, GameItem target, out RenderHandler.ColorPalette color)
        {
            color = RenderHandler.ColorPalette.LightGreen;
            if (IngotCut.Applies(source, target))
                return IngotCut.UseLine(target);
            if (ReedSuppressor.Applies(source, target))
                return ReedSuppressor.UseLine(target);
            if (Cabinets.MayUpgrade(source, target))
                return Cabinets.UseLine(source, target);
            SurveyPlan sample = Survey.Sample(source, target);
            if (sample != null)
                return Survey.UseLine(sample, target, source, out color);
            if (source == null || !Faults.IsModGun(target))
                return null;
            // Written like the game's lines: the action in bold, the gun underlined.
            string gun = "<u>" + target.GetDisplayName(true) + "</u>";
            Plan plan = Find(source, target);
            if (plan == null)
            {
                // Gun parts on a gun with faults left are refused (Allow) with no line: the game writes no line over a target it refuses
                // (ItemMouseDragHandler.Drag, game 1.0.0.12), so the gray line written here until 0.7 did not show (VVittgenstein 2026-11-07 20:06, current/T-006 quote 20).
                if (!source.IsID("screwdriver") || !source.MayTarget(target))
                    return null;
                color = RenderHandler.ColorPalette.LightRed;
                return string.Format(Text.Pick("<b>拆解</b>{0}，得到 {1} 个{2}", "<b>Take apart</b> {0} into {1} {2}"), gun, PartsFor(target), DirectoryMaster.Item("gun_part", false).name);
            }
            if (plan.Inspect)
                return string.Format(plan.Surface ? Text.Pick("<b>检查</b>{0}的表面故障", "<b>Check</b> {0} for surface faults") : Text.Pick("<b>检查</b>{0}的内部故障", "<b>Check</b> {0} for internal faults"), gun);
            string fault = Text.FaultName(plan.Fault);
            string line = plan.Result == FaultState.Emergency
                ? string.Format(Text.Pick("<b>应急修复</b>{0}的{1}", "<b>Emergency repair</b> of {1} on {0}"), gun, fault)
                : string.Format(Text.Pick("<b>修复</b>{0}的{1}", "<b>Repair</b> {1} on {0}"), gun, fault);
            if (plan.SourceIsSolvent)
                line += string.Format(Text.Pick("，用掉{0}中的 {1} 毫升", ", using {1} ml from {0}"), source.name, Chemistry.SolventUseMl);
            if (plan.SourceIsFabric)
                line += string.Format(Text.Pick("，用掉{0}的 {1} 点耐久度", ", using {1} durability of {0}"), source.name, FabricUseDurability);
            if (plan.UseUpOther != null)
                line += plan.OtherIsSolvent
                    ? string.Format(Text.Pick("，同时用掉{0}中的 {1} 毫升", ", also using {1} ml from {0}"), plan.UseUpOther.name, Chemistry.SolventUseMl)
                    : plan.OtherIsFabric
                    ? string.Format(Text.Pick("，同时用掉{0}的 {1} 点耐久度", ", also using {1} durability of {0}"), plan.UseUpOther.name, FabricUseDurability)
                    : string.Format(Text.Pick("，同时用掉{0}", ", also using up {0}"), plan.UseUpOther.name);
            return line;
        }

        // A known fault not yet repaired with its material holds refurbishing back; a hidden one does not.
        private static bool Unfinished(GameItem gun)
        {
            foreach (FaultState s in Faults.Get(gun).Values)
            {
                if (s == FaultState.Known || s == FaultState.Emergency)
                    return true;
            }
            return false;
        }

        private static Plan Find(GameItem source, GameItem gun)
        {
            if (source.forceDisableUse || !GeneralHelper.IsItemEffectivelyOwned(source) || !gun.IsTag(Faults.FaultsTag))
                return null;
            Dictionary<Fault, FaultState> faults = Faults.Get(gun);
            if (faults.Count == 0)
                return null;
            switch (source.identifier)
            {
                case "magnifier":
                    return gun.IsTag(Faults.SurfaceCheckedTag) ? null : new Plan { Inspect = true, Surface = true };
                case "xray_scanner":
                    return gun.IsTag(Faults.InternalCheckedTag) ? null : new Plan { Inspect = true, Surface = false };
            }
            if (!GeneralHelper.IsItemEffectivelyOwned(gun) && !gun.IsTag(ServiceTag))
                return null;
            if (Chemistry.IsSolvent(source))
                return Cleaning(source, gun, faults, true);
            switch (source.identifier)
            {
                case "screwdriver": return ToolOnly(faults, Fault.Screws);
                case "wire_cutter": return ToolOnly(faults, Fault.Wiring);
                case "welder": return ToolOnly(faults, Fault.Weld, Fault.Barrel);
                case "nuts_metal": return Material(source, faults, "screwdriver", Fault.Screws);
                case "wire":
                case "common_electronic": return Material(source, faults, "wire_cutter", Fault.Wiring);
                case "scrap_metal":
                case "metal_ingot": return Material(source, faults, "welder", Fault.Weld, Fault.Barrel);
                case "gun_part": return Material(source, faults, null, Fault.PartWear);
                case "printer_plastic": return MaterialOnly(faults, Fault.PartWear);
                case "rubbing_alcohol": return Cleaning(source, gun, faults, true);
                case "kotton_fabric": return HasDurability(source) ? Cleaning(source, gun, faults, false) : null;
                default: return null;
            }
        }

        private static Plan ToolOnly(Dictionary<Fault, FaultState> faults, params Fault[] kinds)
        {
            foreach (Fault f in kinds)
            {
                FaultState s;
                if (faults.TryGetValue(f, out s) && s == FaultState.Known)
                    return new Plan { Fault = f, Result = FaultState.Emergency };
            }
            return null;
        }

        // The emergency way of a material without a tool: the material is used up.
        private static Plan MaterialOnly(Dictionary<Fault, FaultState> faults, Fault kind)
        {
            Plan plan = ToolOnly(faults, kind);
            if (plan != null)
                plan.UseUpSource = true;
            return plan;
        }

        private static Plan Material(GameItem source, Dictionary<Fault, FaultState> faults, string tool, params Fault[] kinds)
        {
            foreach (Fault f in kinds)
            {
                FaultState s;
                if (faults.TryGetValue(f, out s) && (s == FaultState.Known || s == FaultState.Emergency))
                {
                    if (tool != null && FindOwned(tool, source) == null)
                        return null;
                    return new Plan { Fault = f, Result = FaultState.Fixed, UseUpSource = true };
                }
            }
            return null;
        }

        // Rubbing alcohol (or an alcohol solvent) and kotton fabric; the source is the one dragged onto the gun. A full cleaning takes one of each: after an emergency cleaning with
        // one of them only the other finishes it (item 8; until 0.6.2 two uses of either did, and two uses of the fabric would now cost less than one of the alcohol). A gun cleaned in an
        // emergency before 0.7 has no record of what with, so either finishes it.
        private static Plan Cleaning(GameItem source, GameItem gun, Dictionary<Fault, FaultState> faults, bool sourceIsAlcohol)
        {
            FaultState s;
            if (!faults.TryGetValue(Fault.Rust, out s))
                return null;
            bool solvent = Chemistry.IsSolvent(source);
            if (s == FaultState.Emergency)
            {
                if (gun.IsTag(sourceIsAlcohol ? Faults.RustByAlcoholTag : Faults.RustByFabricTag))
                    return null;
                return new Plan { Fault = Fault.Rust, Result = FaultState.Fixed, UseUpSource = true, SourceIsSolvent = solvent, SourceIsFabric = !sourceIsAlcohol };
            }
            if (s != FaultState.Known)
                return null;
            GameItem other = sourceIsAlcohol ? FindFabric(source) : FindOwned("rubbing_alcohol", source) ?? Chemistry.FindOwnedSolvent(source);
            return new Plan
            {
                Fault = Fault.Rust,
                Result = other != null ? FaultState.Fixed : FaultState.Emergency,
                UseUpSource = true,
                UseUpOther = other,
                SourceIsSolvent = solvent,
                SourceIsFabric = !sourceIsAlcohol,
                OtherIsSolvent = other != null && Chemistry.IsSolvent(other),
                OtherIsFabric = other != null && sourceIsAlcohol,
                RustBy = other != null ? null : sourceIsAlcohol ? Faults.RustByAlcoholTag : Faults.RustByFabricTag,
            };
        }

        // A fabric worn out as a water filter stays in the store with durability 0 (WaterHelper.HandleFilter).
        private static bool HasDurability(GameItem fabric)
        {
            return DurabilityHelper.GetRealDurability(fabric) > 0;
        }

        private static GameItem FindFabric(GameItem except)
        {
            if (EmporiumEntry.Instance == null)
                return null;
            foreach (GameItem item in EmporiumEntry.Instance.GetAllOwnedItems(false, true))
            {
                if (item != null && item != except && item.IsID("kotton_fabric") && HasDurability(item))
                    return item;
            }
            return null;
        }

        private static void UseFabric(GameItem fabric)
        {
            DurabilityHelper.ChangeDurability(fabric, -FabricUseDurability);
            if (!HasDurability(fabric))
                fabric.Destroy();
        }

        // The faults the store can repair now, at least in an emergency way: the pairs of Find, without the materials that need a tool (item 9, Faults.NameOneRepairable).
        internal static HashSet<Fault> RepairableNow()
        {
            var can = new HashSet<Fault>();
            if (EmporiumEntry.Instance == null)
                return can;
            foreach (GameItem item in EmporiumEntry.Instance.GetAllOwnedItems(false, true))
            {
                if (item == null || item.identifier == null)
                    continue;
                if (Chemistry.IsSolvent(item))
                    can.Add(Fault.Rust);
                switch (item.identifier)
                {
                    case "screwdriver": can.Add(Fault.Screws); break;
                    case "wire_cutter": can.Add(Fault.Wiring); break;
                    case "welder": can.Add(Fault.Weld); can.Add(Fault.Barrel); break;
                    case "gun_part":
                    case "printer_plastic": can.Add(Fault.PartWear); break;
                    case "rubbing_alcohol": can.Add(Fault.Rust); break;
                    case "kotton_fabric":
                        if (HasDurability(item))
                            can.Add(Fault.Rust);
                        break;
                }
            }
            return can;
        }

        private static readonly Dictionary<string, string> itemNames = new Dictionary<string, string>();

        private static string ItemName(string id)
        {
            string name;
            if (!itemNames.TryGetValue(id, out name))
            {
                GameItem item = DirectoryMaster.Item(id, false);
                name = item != null && !string.IsNullOrEmpty(item.name) ? item.name : id;
                itemNames[id] = name;
            }
            return name;
        }

        // What repairs a fault, for the gun's tooltip (Steam comment of 2026-11-03: the tools and materials had to be
        // tried one by one). The same pairs as Find above. One line for the full repair and, while the fault is not
        // repaired at all, one for the emergency repair (0.6.2: they were one line, which the game's tooltip broke
        // badly). After an emergency cleaning the line names the cleaning item not used yet (Cleaning above); `gun` tells which, and a gun
        // cleaned before 0.7, or none given, names both.
        internal static List<string> HowTo(Fault fault, FaultState state, GameItem gun = null)
        {
            string full, emergency;
            string or = Text.Pick("或", " or ");
            switch (fault)
            {
                case Fault.Screws:
                    full = string.Format(Text.Pick("{0}，店里要有{1}", "{0}, with a {1} in the store"), ItemName("nuts_metal"), ItemName("screwdriver"));
                    emergency = ItemName("screwdriver");
                    break;
                case Fault.Wiring:
                    full = string.Format(Text.Pick("{0}，店里要有{1}", "{0}, with a {1} in the store"), ItemName("wire") + or + ItemName("common_electronic"), ItemName("wire_cutter"));
                    emergency = ItemName("wire_cutter");
                    break;
                case Fault.Rust:
                    string alcohol = string.Format(Text.Pick("{0}（或酒精占{1}%以上的液体{2}毫升）", "{0} (or {2} ml of a liquid at least {1}% alcohol)"),
                        ItemName("rubbing_alcohol"), Chemistry.SolventAlcoholPercent, Chemistry.SolventUseMl);
                    full = state == FaultState.Emergency && gun != null && gun.IsTag(Faults.RustByAlcoholTag)
                        ? ItemName("kotton_fabric")
                        : state == FaultState.Emergency && gun != null && gun.IsTag(Faults.RustByFabricTag)
                        ? alcohol
                        : state == FaultState.Emergency
                        ? string.Format(Text.Pick("{0}（或酒精占{1}%以上的液体{2}毫升）或{3}", "{0} (or {2} ml of a liquid at least {1}% alcohol) or {3}"),
                            ItemName("rubbing_alcohol"), Chemistry.SolventAlcoholPercent, Chemistry.SolventUseMl, ItemName("kotton_fabric"))
                        : string.Format(Text.Pick("{0}（或酒精占{1}%以上的液体{2}毫升）加{3}", "{0} (or {2} ml of a liquid at least {1}% alcohol) with {3}"),
                            ItemName("rubbing_alcohol"), Chemistry.SolventAlcoholPercent, Chemistry.SolventUseMl, ItemName("kotton_fabric"));
                    emergency = Text.Pick("两样只有一样时", "only one of the two");
                    break;
                case Fault.PartWear:
                    full = ItemName("gun_part");
                    emergency = ItemName("printer_plastic");
                    break;
                default:
                    full = string.Format(Text.Pick("{0}，店里要有{1}", "{0}, with a {1} in the store"), ItemName("scrap_metal") + or + ItemName("metal_ingot"), ItemName("welder"));
                    emergency = ItemName("welder");
                    break;
            }
            var lines = new List<string> { string.Format(Text.Pick("完整修复：{0}", "Full repair: {0}"), full) };
            if (state != FaultState.Emergency)
                lines.Add(string.Format(Text.Pick("应急修复：{0}", "Emergency repair: {0}"), emergency));
            return lines;
        }

        internal static GameItem FindOwned(string id, GameItem except)
        {
            if (EmporiumEntry.Instance == null)
                return null;
            foreach (GameItem item in EmporiumEntry.Instance.GetAllOwnedItems(false, true))
            {
                if (item != null && item != except && item.IsID(id))
                    return item;
            }
            return null;
        }

        private static void Apply(Plan plan, GameItem source, GameItem gun)
        {
            Dictionary<Fault, FaultState> faults = Faults.Get(gun);
            if (plan.Inspect)
            {
                foreach (Fault f in new List<Fault>(faults.Keys))
                {
                    if (Faults.IsSurface(f) == plan.Surface && faults[f] == FaultState.Hidden)
                        faults[f] = FaultState.Known;
                }
                gun.EnableTag(plan.Surface ? Faults.SurfaceCheckedTag : Faults.InternalCheckedTag, false);
                GunsmithMod.Log(gun.identifier + (plan.Surface ? " surface" : " internal") + " check with " + source.identifier);
                if (plan.Surface)
                    Stolen.OnSurfaceCheck(gun);
            }
            else
            {
                faults[plan.Fault] = plan.Result;
                if (plan.RustBy != null)
                    gun.EnableTag(plan.RustBy, false);
                GunsmithMod.Log(gun.identifier + " " + plan.Fault + " -> " + plan.Result + " with " + source.identifier
                    + (plan.UseUpOther != null ? " and " + plan.UseUpOther.identifier : ""));
            }
            Faults.Set(gun, faults);
            RepairOrders.OnFaultsChanged(gun);
            if (AudioManager.Instance != null && !string.IsNullOrEmpty(source.soundUse))
                AudioManager.Instance.TryPlayOne(new[] { source.soundUse });
            if (plan.UseUpOther != null)
            {
                if (plan.OtherIsSolvent)
                    Chemistry.UseSolvent(plan.UseUpOther);
                else if (plan.OtherIsFabric)
                    UseFabric(plan.UseUpOther);
                else
                    plan.UseUpOther.Destroy();
            }
            if (plan.UseUpSource)
            {
                if (plan.SourceIsSolvent)
                    Chemistry.UseSolvent(source);
                else if (plan.SourceIsFabric)
                    UseFabric(source);
                else
                    source.Destroy();
            }
            if (OverlayHandler.current != null)
                OverlayHandler.current.UpdateInventoryStateUI();
        }
    }

    [HarmonyPatch(typeof(GameItem), nameof(GameItem.MayTarget))]
    internal static class MayTargetPatch
    {
        private static void Postfix(GameItem __instance, GameItem targetItem, ref bool __result)
        {
            bool? allow = Repair.Allow(__instance, targetItem);
            if (allow.HasValue)
                __result = allow.Value;
        }
    }

    [HarmonyPatch(typeof(GameItem), nameof(GameItem.CanTarget))]
    internal static class CanTargetPatch
    {
        private static void Postfix(GameItem __instance, GameItem targetItem, ref bool __result)
        {
            bool? allow = Repair.Allow(__instance, targetItem);
            if (allow.HasValue)
                __result = allow.Value;
        }
    }

    [HarmonyPatch(typeof(GameItem), nameof(GameItem.Target))]
    internal static class TargetPatch
    {
        private static bool Prefix(GameItem __instance, GameItem targetItem)
        {
            return !Repair.Handle(__instance, targetItem);
        }
    }

    // GunHelper.DisassembleWeapon gives parts back only for the game's own guns; for this mod's guns it would only
    // destroy them.
    [HarmonyPatch(typeof(GunHelper), nameof(GunHelper.DisassembleWeapon))]
    internal static class DisassemblePatch
    {
        private static bool Prefix(GameItem targetItem)
        {
            if (!Faults.IsModGun(targetItem))
                return true;
            Repair.Disassemble(targetItem);
            return false;
        }
    }

    [HarmonyPatch(typeof(TooltipUseHelper), nameof(TooltipUseHelper.HasUseTooltip))]
    internal static class HasUseTooltipPatch
    {
        private static void Postfix(GameItem sourceItem, GameItem targetItem, ref bool __result)
        {
            RenderHandler.ColorPalette color;
            if (Repair.UseLine(sourceItem, targetItem, out color) != null)
                __result = true;
        }
    }

    [HarmonyPatch(typeof(TooltipUseHelper), nameof(TooltipUseHelper.HandleUseTooltip))]
    internal static class HandleUseTooltipPatch
    {
        private static bool Prefix(GameItem sourceItem, GameItem targetItem, RichTextBuilder customBuilder)
        {
            RenderHandler.ColorPalette color;
            string line = Repair.UseLine(sourceItem, targetItem, out color);
            if (line == null)
                return true;
            customBuilder.Add(line, color);
            return false;
        }
    }

    [HarmonyPatch(typeof(GunHelper), nameof(GunHelper.CreateTooltip))]
    internal static class GunTooltipPatch
    {
        // The gun's calibre above its stats, written like them, on every gun with stats, the game's too (VVittgenstein 2026-11-05,
        // current/008 quote 1 point 4; a Xiaoheihe player: the magazines had to be tried one by one). The game's tooltip
        // does not show it; the game's word for it (GunHelper.IDToString) and the calibre's name (GetCaliberName) are used.
        private static void Prefix(RichTextBuilder builder, GameItem item)
        {
            if (item == null || !item.IsTag("ADV_GUN_TAG") || !item.IsTag("GUN_CALIBER_TAG"))
                return;
            builder.Add(GunHelper.IDToString("GUN_CALIBER_TAG") + ": ", RenderHandler.ColorPalette.LightBlue);
            builder.AddLine(GunHelper.GetCaliberName(item.GetTagReadonly("GUN_CALIBER_TAG").GetInt()), true);
        }

        private static void Postfix(RichTextBuilder builder, GameItem item)
        {
            Faults.AppendTooltip(builder, item);
            Workshop.AppendTooltip(builder, item);
            Stolen.AppendTooltip(builder, item);
        }
    }
}
