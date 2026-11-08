using System;
using System.Collections.Generic;
using HarmonyLib;

namespace GunsmithExpansion
{
    // Printer chips, frames and surveying (final-strategy 11, 33 ③, 37, 38). Every gun of tiers one to three has a
    // printer chip and a frame, except the old and antique guns, which like the special tier can only be bought. The game's printer prints the frame from the chip, and a gun part finishes the frame into the
    // gun (GunFrameHelper.Finish), as with the game's own frame chips. Tier one chips print in plastic like the game's
    // pistol chip; tier two and three chips print in metal ingots and need the metal printing module and the CNC
    // module, like the game's frame chips. The game sets a printed frame's quality from the printer's quality and,
    // for ingots, their purity, and the finished gun's base stats are multiplied by it (MachinePrinter.
    // HandlePrintedItemQuality, GunHelper.MultiplyBaseStat), which is final-strategy 38.
    //
    // Surveying: a blank printer chip of a level dragged onto one of the store's guns of that level becomes that gun's printer chip; the gun is kept. Level one needs a magnifier in the
    // store, levels two and three a portable X-ray scanner. The game's own guns too (GameGunChips below). The retired gunsmith brings two on every visit: one of level one and one of the
    // store's highest firearm permit (Agent's proposal; VVittgenstein 2026-11-06 09:19, current/008 quote 41).
    //
    // Values (item 76; VVittgenstein 2026-11-06 08:07, 08:45 and 09:19, current/008 quotes 37, 40 and 41):
    // every chip of a level has one value, and on the normal difficulty, with the retired gunsmith's +15% and the
    // merchant's -15%, a blank chip of level 1, 2, 3 costs 400, 1000, 1800 and its chip sells for 445, 1100, 2000: 45, 100, 200 for the work of surveying, about half of the game's custom
    // order of that level (base rewards 90, 198, 358 on average). The chips of the game's guns keep their values (GameGunChips; VVittgenstein 09:45, current/008 quote 44).
    // Agent's reading: on the other difficulties the same values take the game's markups, so the difference is another one. Until 0.6.2 a chip was worth 800 / 280 of its gun's value and the one
    // blank chip 120. A frame still is worth 170 / 280 of its gun's value, rounded to tens, as the game's frame chips (VVittgenstein 2026-11-02, current/005 quote 50).
    // Progress: 50, 65 and 80 for tiers one, two and three (VVittgenstein 2026-11-08 04:30, current/T-005 quote 19). Until 0.7 it was
    // 50 for plastic, as the game's .22 pistol chip, and for metal 50 + 10 per contraband level, as the game's frame chips (60 for the level one
    // Nestfire, 70 for the level two Ironcraft 10), so 70 and 80 for tiers two and three. A printer adds 24 + 24 × its modules' performance ÷ 100
    // points a night (18 with the CNC module), so tier two takes 4 nights without a speed module, 3 with one performance module (22 points).
    // Agent's decisions: plastic per tier one chip 1 per 6 cells of the gun (2 to 5), metal ingots per tier two
    // chip 1 per 9 cells (1 to 3), one more for tier three. Tier three chips print only with precision printing (Precision below).
    internal static class Production
    {
        // Level one keeps the id of the one blank chip there was until 0.6.2, so that those in old saves are of level one.
        internal const string BlankId = "gse_blank_chip";
        private static readonly int[] BlankValues = { 348, 870, 1565 };
        private static readonly int[] ChipValues = { 524, 1294, 2353 };

        internal static string BlankIdOf(int level)
        {
            return level <= 1 ? BlankId : BlankId + "_" + level;
        }

        // 1 to 3; 0 for anything else than a blank chip.
        internal static int BlankLevel(GameItem item)
        {
            if (item == null)
                return 0;
            for (int level = 1; level <= 3; level++)
            {
                if (item.IsID(BlankIdOf(level)))
                    return level;
            }
            return 0;
        }

        internal static string BlankName(int level)
        {
            return level <= 1 ? Text.Pick("一级空白3D打印机芯片", "Restricted I Blank 3D Printer Chip")
                : level == 2 ? Text.Pick("二级空白3D打印机芯片", "Restricted II Blank 3D Printer Chip")
                : Text.Pick("三级空白3D打印机芯片", "Restricted III Blank 3D Printer Chip");
        }

        private static string BlankDescription(int level)
        {
            return level <= 1
                ? Text.Pick("没有写入图纸的3D打印机芯片。拖到店里一把一级管制枪械上测绘，就成了这种枪的3D打印机芯片，枪还在。店里要有放大镜。",
                    "A 3D printer chip with no blueprint on it. Drag it onto a Restricted I gun of the store to survey the gun and get its 3D printer chip; the gun is kept. Needs a magnifying glass in the store.")
                : level == 2
                ? Text.Pick("没有写入图纸的3D打印机芯片。拖到店里一把二级管制枪械上测绘，就成了这种枪的3D打印机芯片，枪还在。店里要有便携式X射线扫描仪。",
                    "A 3D printer chip with no blueprint on it. Drag it onto a Restricted II gun of the store to survey the gun and get its 3D printer chip; the gun is kept. Needs a portable X-ray scanner in the store.")
                : Text.Pick("没有写入图纸的3D打印机芯片。拖到店里一把三级管制枪械上测绘，就成了这种枪的3D打印机芯片，枪还在。店里要有便携式X射线扫描仪。",
                    "A 3D printer chip with no blueprint on it. Drag it onto a Restricted III gun of the store to survey the gun and get its 3D printer chip; the gun is kept. Needs a portable X-ray scanner in the store.");
        }

        // This mod's level of a gun of tiers one to three.
        internal static int Level(GunDef d)
        {
            return d.Tier == "一档" ? 1 : d.Tier == "二档" ? 2 : 3;
        }

        internal static int ChipValueOfLevel(int level)
        {
            return ChipValues[level - 1];
        }

        internal static bool HasChip(GunDef d)
        {
            return d != null && d.Tier != "特殊" && !d.Antique && !d.AddOn;
        }

        internal static string ChipId(GunDef d)
        {
            return "gse_chip_" + d.Id.Substring(4);
        }

        internal static string FrameId(GunDef d)
        {
            return "gse_frame_" + d.Id.Substring(4);
        }

        internal static int Cells(GunDef d)
        {
            string[] parts = (d.Size ?? "").Split('x');
            int w, h;
            return parts.Length == 2 && int.TryParse(parts[0], out w) && int.TryParse(parts[1], out h) ? w * h : 12;
        }

        private static int Round10(double v)
        {
            return (int)(Math.Round(v / 10.0) * 10);
        }

        internal static int ChipValue(GunDef d)
        {
            return ChipValueOfLevel(Level(d));
        }

        internal static int FrameValue(GunDef d)
        {
            return FrameValueOf(d.Value);
        }

        // The chips this mod adds for the game's guns (GameGunChips): as the game's frame chips, whose Nestfire and Ironcraft 10 chips are worth 350 and 450 for guns of 120 and 160.
        internal static int GameGunChipValue(int gunValue)
        {
            return Round10(gunValue * 800.0 / 280);
        }

        internal static int FrameValueOf(int gunValue)
        {
            return Math.Max(10, Round10(gunValue * 170.0 / 280));
        }

        internal static int Plastic(GunDef d)
        {
            return Math.Min(5, Math.Max(2, (Cells(d) + 5) / 6));
        }

        internal static int Ingots(GunDef d)
        {
            int n = Math.Min(3, Math.Max(1, (Cells(d) + 8) / 9));
            return d.Tier == "三档" ? n + 1 : n;
        }

        internal static int Progress(GunDef d)
        {
            return d.Tier == "一档" ? 50 : d.Tier == "二档" ? 65 : 80;
        }

        // The printer's output tray has to hold the widest frame a chip prints.
        internal static int OutputWidth()
        {
            int widest = MachinePrinter.OUTPUT_WIDTH;
            foreach (GunDef d in Catalog.Guns)
            {
                if (!HasChip(d))
                    continue;
                string[] parts = (d.Size ?? "").Split('x');
                int w;
                if (parts.Length == 2 && int.TryParse(parts[0], out w) && w > widest)
                    widest = w;
            }
            return widest;
        }

        internal static void Register(ModItemDirectory directory)
        {
            int chips = 0;
            for (int level = 1; level <= 3; level++)
            {
                int blankLevel = level;
                directory.Add(BlankIdOf(blankLevel), () => CreateBlank(directory, blankLevel));
            }
            foreach (GunDef def in Catalog.Guns)
            {
                GunDef d = def;
                if (!HasChip(d))
                    continue;
                try
                {
                    directory.Add(ChipId(d), () => CreateChip(directory, d));
                    directory.Add(FrameId(d), () => CreateFrame(directory, d));
                    chips++;
                }
                catch (Exception e)
                {
                    GunsmithMod.Warn("registering the chip or frame of " + d.Id + " failed: " + e.Message);
                }
            }
            GunsmithMod.Log("registered 3 blank chips and " + chips + " printer chips and frames");
            GameGunChips.Register(directory);
            Precision.Register();
        }

        private static GameItem CreateBlank(ModItemDirectory directory, int level)
        {
            GameItem item = directory.Empty().SetSpriteAndShapeFromMod(GunsmithMod.ModId, BlankId + "_" + level);
            item.name = BlankName(level);
            item.shortDescription = BlankDescription(level);
            item.unitValue = BlankValues[level - 1];
            AudioHelper.InitSmallModule(item);
            return item;
        }

        private static GameItem CreateChip(ModItemDirectory directory, GunDef d)
        {
            string sprite = d.Tier == "一档" ? "printer_chip2" : d.Tier == "二档" ? "printer_chip_pistol" : "printer_chip4";
            GameItem chip = directory.Empty().SetSpriteAndShape("Items/items_tool", sprite);
            chip.name = string.Format(Text.Pick("{0}3D打印机芯片", "{0} 3D Printer Chip"), Text.Pick(d.NameZh, d.NameEn));
            chip.shortDescription = LocHelper.GetLocalizedItem("item_printer_chip_desc", Array.Empty<object>());
            chip.unitValue = ChipValue(d);
            chip.EnableTag("PRINTER_CHIP_TAG", false);
            chip.EnableTag("PRINTER_FRAME_CHIP_TAG", false);
            if (d.Tier == "一档")
            {
                MachineProgressHelper.SetSingleAdditionalRequiredItem(chip, "printer_plastic", Plastic(d));
                MachineProgressHelper.InitProgressSourceItem(chip, Progress(d), FrameId(d), true, 1, null);
            }
            else
            {
                MachineProgressHelper.SetSingleAdditionalRequiredItem(chip, "metal_ingot", Ingots(d));
                MachineProgressHelper.InitProgressSourceItem(chip, Progress(d), FrameId(d), true, 1, "Metal Printing Module");
                MachineProgressHelper.AddRequiredMachineTag(chip, "CNC Module");
                if (d.Tier == "三档")
                    MachineProgressHelper.AddRequiredMachineTag(chip, Precision.QualityTag);
            }
            AudioHelper.InitSmallModule(chip);
            return chip;
        }

        // Built like the game's GunsItemDirectory.CreateHandmadePistolFrame, with the gun's picture.
        private static GameItem CreateFrame(ModItemDirectory directory, GunDef d)
        {
            GameItem frame = directory.Empty().SetSpriteAndShapeFromMod(GunsmithMod.ModId, d.Id);
            frame.name = string.Format(Text.Pick("{0}机匣", "{0} Frame"), Text.Pick(d.NameZh, d.NameEn));
            if (!string.IsNullOrEmpty(d.Maker))
                frame.EnableManufacturer(d.Maker);
            frame.SetValue(FrameValue(d));
            GunFrameHelper.InitFrame(frame, d.Id, d.Contraband);
            return frame;
        }

        // The retired gunsmith's fixed stock, put out before the game's wares so that a full counter does not leave it off.
        internal static void AddBlankChips(StoreClient client)
        {
            Sellers.BeforeStock(client, () =>
            {
                if (PlayerStore.Instance == null)
                    return;
                Sellers.Offer(BlankIdOf(1));
                Sellers.Offer(BlankIdOf(Math.Max(1, GunSupply.PermitTop())));
            });
        }
    }

    // The printer's output tray. The game builds it 9 x 5 cells (MachinePrinter.CreateMachineInventoryWindow); since the
    // guns got their real sizes (0.5.14) 70 of the 121 chips print a frame wider than that (VVittgenstein 2026-11-05 13:05, current/008
    // quote 3 point 6). Before game 1.0.0.12 FinishPrinting did not check that the print went
    // in (read 2026-11-05), so such a frame was lost and the tray stayed empty. From 1.0.0.12 it places the print with
    // GraphUtils.EmporiumTryAdd: the tray, an owned intake hopper lying in it, the after-hours inventory while the player is
    // out, the counter top, the inventory, the display case, the front counter; with all of them full it is destroyed. The
    // tray is widened to the widest frame, so the frame stays in the printer; a saved printer is built by the same code, so it
    // gets the wider tray on loading. The reloader, built on the same machine, makes only ammunition and keeps the game's size.
    [HarmonyPatch(typeof(MachinePrinter), nameof(MachinePrinter.CreateMachineInventoryWindow))]
    internal static class PrinterOutputPatch
    {
        internal static bool KeepGameSize;

        private static void Postfix(ValueTuple<PixelWindow, GameSlotInventory, GameGridInventory, GameGridInventory, GameSlotInventory, GameGridInventory, GameSlotInventory> __result)
        {
            if (KeepGameSize || __result.Item6 == null)
                return;
            __result.Item6.SetShape(Production.OutputWidth(), MachinePrinter.OUTPUT_HEIGHT);
            if (__result.Item1 != null)
                __result.Item1.Validate();
        }
    }

    // Precision printing of tier three chips (final-strategy 37): the printer needs a module that raises quality in its
    // module bay and takes only metal ingots of high purity or better. The module is asked for as a machine tag, like
    // the game's metal printing and CNC modules (MachineProgressHelper.AddRequiredMachineTag, listed in the chip's
    // tooltip); the printer gets or loses the tag from its modules each time it checks a precision chip
    // (MachineProgressHelper.CanStart). Any installed module with a quality bonus above 0 counts: the game's quality
    // (+8), fineness (+25) and neural core modules. Ingots of lower purity are left out of what the printer counts and
    // consumes for these chips, so the frame's quality comes from the high purity ingots (MachinePrinter.
    // CaptureIngotQuality reads the same list). Agent's decisions: "high purity" is the game's high grade or better
    // (high, very high, perfect), and which modules count.
    internal static class Precision
    {
        internal const string QualityTag = "GSE Quality Module";
        private const string QualityBonus = "BONUS_PERCENTAGE_QUALITY_INT";
        private static readonly string[] HighPurity = { "INGOT_PURITY_HIGH", "INGOT_PURITY_VERY_HIGH", "INGOT_PURITY_PERFECT" };

        internal static void Register()
        {
            MachineProgressHelper.SetMachineTagDisplayName(QualityTag, () => Text.Pick("提高品质的模组", "A module that raises quality"));
        }

        internal static bool IsPrecisionChip(GameItem chip)
        {
            if (chip == null || chip.identifier == null || !chip.identifier.StartsWith("gse_chip_", StringComparison.Ordinal))
                return false;
            GunDef d = RepairOrders.Find("gse_" + chip.identifier.Substring("gse_chip_".Length));
            return d != null && d.Tier == "三档";
        }

        internal static bool HasQualityModule(GameItem machine)
        {
            List<GameItem> modules = machine != null ? MachineHelper.GetModules(machine) : null;
            if (modules == null)
                return false;
            foreach (GameItem m in modules)
            {
                if (m != null && m.IsTag(QualityBonus) && m.GetTagReadonly(QualityBonus).GetInt() > 0)
                    return true;
            }
            return false;
        }

        internal static bool IsHighPurity(GameItem ingot)
        {
            return Array.IndexOf(HighPurity, IngotPurityHelper.GetPurity(ingot)) >= 0;
        }

        // What the printer may count and consume for a precision chip: everything except metal ingots below high purity.
        internal static List<GameItem> Usable(List<GameItem> items)
        {
            return items == null ? null : items.FindAll(i => i == null || !i.IsID("metal_ingot") || IsHighPurity(i));
        }

        internal static void SyncTag(GameItem machine)
        {
            if (HasQualityModule(machine))
                machine.EnableTag(QualityTag, false);
            else if (machine.IsTag(QualityTag))
                machine.DisableTag(QualityTag, false);
        }
    }

    [HarmonyPatch(typeof(MachineProgressHelper), nameof(MachineProgressHelper.CanStart))]
    internal static class PrecisionStartPatch
    {
        private static void Prefix(GameItem sourceItem, GameItem machine)
        {
            if (machine != null && Precision.IsPrecisionChip(sourceItem))
                Precision.SyncTag(machine);
        }
    }

    [HarmonyPatch(typeof(MachineProgressHelper), nameof(MachineProgressHelper.HasRequiredAdditionalItems))]
    internal static class PrecisionIngotCountPatch
    {
        private static void Prefix(GameItem sourceItem, ref List<GameItem> providedItems)
        {
            if (Precision.IsPrecisionChip(sourceItem))
                providedItems = Precision.Usable(providedItems);
        }
    }

    [HarmonyPatch(typeof(MachineProgressHelper), nameof(MachineProgressHelper.GetItemsToConsume))]
    internal static class PrecisionIngotUsePatch
    {
        private static void Prefix(GameItem sourceItem, ref List<GameItem> inventory)
        {
            if (Precision.IsPrecisionChip(sourceItem))
                inventory = Precision.Usable(inventory);
        }
    }

    [HarmonyPatch(typeof(MachineProgressHelper), nameof(MachineProgressHelper.AddAdditionalItemRequirementsTooltip))]
    internal static class PrecisionTooltipPatch
    {
        private static void Postfix(RichTextBuilder builder, GameItem item)
        {
            if (Precision.IsPrecisionChip(item))
                builder.AddLine(Text.Pick("精密打印：金属锭须为高纯度或更高，打印机须装有提高品质的模组。",
                    "Precision printing: the metal ingots must be of high purity or better, and the printer needs a module that raises quality."),
                    true, RenderHandler.ColorPalette.Yellow);
        }
    }

    // Surveying the game's own guns (VVittgenstein 2026-11-05 18:22, current/008 quote 9 item 47; until
    // then a blank chip did nothing on them and showed no line). The level is this mod's (GameGunLevels), which is the one the
    // player sees: restricted I needs a magnifier, restricted II an X-ray scanner, as for this mod's guns. The gun is kept.
    //   - The game has chips for three of its guns (ModuleDirectory): surveying those gives the game's chip, at the game's value (250, 350, 450).
    //   - The other four get a chip and a frame here, by the rules of this mod's chips above: restricted I prints in plastic
    //     like the game's pistol chip, restricted II in metal ingots with the metal printing and CNC modules; values and
    //     amounts by the game's formulas from the gun's value and its 4 x 3 or 7 x 3 cells (570, 690, 800, 890; item 76 leaves them as they were: Agent's interpretation,
    //     since the game has no price for them). A gun part finishes the frame (GunFrameHelper.Finish), which also gives the gun the printer's quality.
    // Agent's decisions: the seven guns of GameGunLevels (the stun gun is not a firearm: its directory never calls GunHelper.
    // InitGun and it has no FIREARM type), the new chips, and that they come only from surveying, not from the phone.
    internal static class GameGunChips
    {
        internal sealed class NewChip
        {
            internal string GunId;
            internal string Sheet;
            internal string Sprite;
            internal string NameKey;
            internal string Maker;
            internal int Value;
            internal int Materials;
        }

        private const string Pistols = "Items/items_weapons_ranged_energy";
        private const string Long = "Items/items_weapons_ranged_antique";

        private static readonly Dictionary<string, string> GameChips = new Dictionary<string, string>
        {
            { "printed_gun", "printer_chip_pistol" },
            { "handmade_pistol", "printer_chip_handmade_pistol_frame" },
            { "heavy_handmade_pistol", "printer_chip_heavy_handmade_pistol_frame" },
        };

        // Identifier, picture, name key, maker and value from GunsItemDirectory (read from the assembly 2026-11-05).
        private static readonly NewChip[] NewChips =
        {
            new NewChip { GunId = "glock_receiver", Sheet = Pistols, Sprite = "glock_receiver", NameKey = "item_glock_receiver_name", Maker = "meridian", Value = 200, Materials = 2 },
            new NewChip { GunId = "revolver", Sheet = Pistols, Sprite = "revolver", NameKey = "item_revolver_name", Maker = "frontier", Value = 240, Materials = 2 },
            new NewChip { GunId = "smg", Sheet = Long, Sprite = "heavy_smg", NameKey = "item_smg_name", Maker = "meridian", Value = 280, Materials = 3 },
            new NewChip { GunId = "shotgun", Sheet = Long, Sprite = "shotgun", NameKey = "item_shotgun_name", Maker = "meridian", Value = 310, Materials = 3 },
        };

        private static string ChipIdOf(string gunId)
        {
            return "gse_chip_v_" + gunId;
        }

        private static string FrameIdOf(string gunId)
        {
            return "gse_frame_v_" + gunId;
        }

        // The chip a survey of this game gun gives, or null when it is not one of the seven.
        internal static string ChipFor(string gunId)
        {
            if (GameGunLevels.Find(gunId) == null)
                return null;
            string chip;
            return GameChips.TryGetValue(gunId, out chip) ? chip : ChipIdOf(gunId);
        }

        internal static void Register(ModItemDirectory directory)
        {
            foreach (NewChip def in NewChips)
            {
                NewChip n = def;
                directory.Add(ChipIdOf(n.GunId), () => CreateChip(directory, n));
                directory.Add(FrameIdOf(n.GunId), () => CreateFrame(directory, n));
            }
            GunsmithMod.Log("registered " + NewChips.Length + " printer chips and frames of the game's guns");
        }

        private static string GunName(NewChip n)
        {
            return LocHelper.GetLocalizedItem(n.NameKey, Array.Empty<object>());
        }

        private static GameItem CreateChip(ModItemDirectory directory, NewChip n)
        {
            bool first = GameGunLevels.Find(n.GunId).Level == 1;
            GameItem chip = directory.Empty().SetSpriteAndShape("Items/items_tool", first ? "printer_chip2" : "printer_chip_pistol");
            chip.name = string.Format(Text.Pick("{0}3D打印机芯片", "{0} 3D Printer Chip"), GunName(n));
            chip.shortDescription = LocHelper.GetLocalizedItem("item_printer_chip_desc", Array.Empty<object>());
            chip.unitValue = Production.GameGunChipValue(n.Value);
            chip.EnableTag("PRINTER_CHIP_TAG", false);
            chip.EnableTag("PRINTER_FRAME_CHIP_TAG", false);
            if (first)
            {
                MachineProgressHelper.SetSingleAdditionalRequiredItem(chip, "printer_plastic", n.Materials);
                MachineProgressHelper.InitProgressSourceItem(chip, 50, FrameIdOf(n.GunId), true, 1, null);
            }
            else
            {
                MachineProgressHelper.SetSingleAdditionalRequiredItem(chip, "metal_ingot", n.Materials);
                MachineProgressHelper.InitProgressSourceItem(chip, 65, FrameIdOf(n.GunId), true, 1, "Metal Printing Module");
                MachineProgressHelper.AddRequiredMachineTag(chip, "CNC Module");
            }
            AudioHelper.InitSmallModule(chip);
            return chip;
        }

        private static GameItem CreateFrame(ModItemDirectory directory, NewChip n)
        {
            GameItem frame = directory.Empty().SetSpriteAndShape(n.Sheet, n.Sprite);
            frame.name = string.Format(Text.Pick("{0}机匣", "{0} Frame"), GunName(n));
            frame.EnableManufacturer(n.Maker);
            frame.SetValue(Production.FrameValueOf(n.Value));
            GunFrameHelper.InitFrame(frame, n.GunId, GameGunLevels.Find(n.GunId).Level);
            return frame;
        }
    }

    internal sealed class SurveyPlan
    {
        internal string GunId;
        internal string Tool;
        internal string Chip;
        internal int Level;
    }

    // Surveying, run from the uses in Repair.cs.
    internal static class Survey
    {
        private const string FirstLevelTool = "magnifier";
        private const string HigherLevelTool = "xray_scanner";

        internal static SurveyPlan Sample(GameItem source, GameItem target)
        {
            if (source == null || target == null || Production.BlankLevel(source) == 0 || source.forceDisableUse)
                return null;
            SurveyPlan plan = null;
            if (Faults.IsModGun(target))
            {
                GunDef d = RepairOrders.Find(target.identifier);
                if (Production.HasChip(d))
                    plan = new SurveyPlan { GunId = d.Id, Level = Production.Level(d), Chip = Production.ChipId(d) };
            }
            else
            {
                string chip = GameGunChips.ChipFor(target.identifier);
                if (chip != null)
                    plan = new SurveyPlan { GunId = target.identifier, Level = GameGunLevels.Find(target.identifier).Level, Chip = chip };
            }
            if (plan == null || !GeneralHelper.IsItemEffectivelyOwned(source) || !GeneralHelper.IsItemEffectivelyOwned(target))
                return null;
            plan.Tool = plan.Level == 1 ? FirstLevelTool : HigherLevelTool;
            return plan;
        }

        internal static bool HasTool(SurveyPlan plan, GameItem blank)
        {
            return Repair.FindOwned(plan.Tool, blank) != null;
        }

        // The blank chip of the gun's level, and the tool in the store.
        internal static bool CanDo(SurveyPlan plan, GameItem blank)
        {
            return Production.BlankLevel(blank) == plan.Level && HasTool(plan, blank);
        }

        internal static void Do(GameItem blank, SurveyPlan plan)
        {
            GameInventory parent = blank.parentInventory;
            GameItem tool = Repair.FindOwned(plan.Tool, blank);
            blank.Destroy();
            GraphUtils.EmporiumTryAdd(parent, DirectoryMaster.Item(plan.Chip, true));
            if (tool != null && AudioManager.Instance != null && !string.IsNullOrEmpty(tool.soundUse))
                AudioManager.Instance.TryPlayOne(new[] { tool.soundUse });
            if (OverlayHandler.current != null)
                OverlayHandler.current.UpdateInventoryStateUI();
            GunsmithMod.Log("surveyed " + plan.GunId + " with " + plan.Tool + " into " + plan.Chip);
        }

        // The line over a gun the blank chip can survey. A survey it cannot do (CanDo) is refused (Repair.Allow) with no line: the game writes no line over a target it
        // refuses (ItemMouseDragHandler.Drag, game 1.0.0.12), so the gray lines written here until 0.7 did not show (VVittgenstein 2026-11-07 20:06, current/T-006 quote 20).
        internal static string UseLine(SurveyPlan plan, GameItem gun, GameItem blank, out RenderHandler.ColorPalette color)
        {
            color = RenderHandler.ColorPalette.LightGreen;
            if (!CanDo(plan, blank))
                return null;
            string gunName = "<u>" + gun.GetDisplayName(true) + "</u>";
            return string.Format(Text.Pick("<b>测绘</b>{0}，得到{1}", "<b>Survey</b> {0} into a {1}"), gunName, DirectoryMaster.Item(plan.Chip, false).name);
        }
    }
}
