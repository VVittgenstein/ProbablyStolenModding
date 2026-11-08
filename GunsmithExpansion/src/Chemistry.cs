using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace GunsmithExpansion
{
    // The chemistry link (final-strategy 42, 83, 100; VVittgenstein 2026-11-03 20:35 point 3, 22:44 point 6: the recipes and uses
    // are the Agent's to design). The propellant and the cleaning solvent come from the player's chemistry, made with the
    // game's own chemistry (ChemicalHelper, ChemFinisherRecipe, MachineChemFinisher; read 2026-11-04):
    //   - Nitrocellulose, a liquid of this mod (gse_nitrocellulose), forms in a chemistry container that holds plant
    //     matter (cellulose) and acid while the container is between 40 and 90 degrees: each second 1% of the smaller of
    //     the two (at least 10 microlitres) turns into as much nitrocellulose and as much water (the water a nitration
    //     gives off). Kotton fabric goes into the game's blender as 40 ml of plant matter; the plant matter of the game's
    //     own plants works the same.
    //   - The game's chem finisher has a recipe for propellant: 5 ml of a dry mix a can, nitrocellulose the active
    //     ingredient, so the can's purity is the share of nitrocellulose as for the game's drugs (ChemicalProductHelper.
    //     GradeProduct). Propellant bought from the chemist and the retired gunsmith is street standard (60 to 79%), as the
    //     game's bought drugs are.
    //   - In the ammo reloader a can of at least near pharma grade (80%) makes one round more a batch, one below cut
    //     product (45%) one round less, at least one.
    //   - Cleaning solvent is alcohol, the solvent of the game's extractions: a liquid container whose content is at least
    //     60% alcohol (the street standard grade) with at least 25 ml of alcohol cleans rust and fouling as rubbing
    //     alcohol does, 25 ml of its content a use.
    // Agent's decisions: the temperatures, the rate, the water given off, 40 ml a fabric, 5 ml a can, the purity steps,
    // 60% and 25 ml.
    internal static class Chemistry
    {
        internal const string NitroId = "gse_nitrocellulose";
        internal const string RecipeId = Reloader.PropellantId;
        internal const int RecipeVolume = 5000;
        internal const double CottonPlantMatterMl = 40;
        internal const double NitrationMinTemp = 40;
        internal const double NitrationMaxTemp = 90;
        internal const double NitrationShare = 0.01;
        internal const int NitrationMinStep = 10;
        internal const int HighGrade = 80;
        internal const int LowGrade = 45;
        internal const int SolventAlcoholPercent = 60;
        internal const int SolventUseMl = 25;
        private static readonly MethodInfo setIdentifier = AccessTools.Method(typeof(Liquid), "SetIdentifier");
        private static bool registered;

        internal static bool Registered
        {
            get { return registered; }
        }

        // The liquid and the recipe, once; the propellant's composition needs the recipe.
        internal static void Register()
        {
            if (registered)
                return;
            try
            {
                if (Liquid.Liquids == null || Liquid.LiquidDict == null || ChemFinisherRecipe.chemFinisherRecipes == null || setIdentifier == null)
                {
                    GunsmithMod.Warn("the game's chemistry was not found; no nitrocellulose");
                    return;
                }
                if (!Liquid.LiquidDict.ContainsKey(NitroId))
                {
                    var liquid = new Liquid();
                    liquid.displayName = Text.Pick("硝化棉", "Nitrocellulose");
                    setIdentifier.Invoke(liquid, new object[] { NitroId });
                    liquid.baseMaxPurityFromMachine = 12;
                    liquid.basePurifierRemoveAmount = 4000;
                    liquid.particleSize = 5;
                    liquid.color = "e3dcc4";
                    Liquid.Liquids.Add(liquid);
                    Liquid.LiquidDict[NitroId] = liquid;
                }
                if (!ChemFinisherRecipe.chemFinisherRecipes.Exists(r => r.identifier == RecipeId))
                {
                    ChemFinisherRecipe.chemFinisherRecipes.Add(new ChemFinisherRecipe
                    {
                        identifier = RecipeId,
                        displayName = Text.Pick("发射药", "Propellant"),
                        requiredVolume = RecipeVolume,
                        isRequireDry = true,
                        activeIngredientId = NitroId,
                    });
                }
                registered = true;
                GunsmithMod.Log("registered nitrocellulose and the chem finisher's propellant recipe");
            }
            catch (Exception e)
            {
                GunsmithMod.Warn("chemistry: " + e.Message);
            }
        }

        // A can of propellant as a chemical product of the finisher, made street standard (as MedsItemDirectory's drugs).
        internal static void InitPropellant(GameItem item)
        {
            if (!registered)
                return;
            ChemicalHelper.InitChemItem(item, false, false);
            ChemFinisherRecipe.SetCompositionStreetStandard(item, RecipeId);
            ChemicalProductHelper.InitChemicalProduct(item, NitroId);
        }

        // Rounds more (or fewer) a batch for the propellant's purity.
        internal static int YieldChange(GameItem propellant)
        {
            if (propellant == null || !ChemicalProductHelper.HasChemicalPurity(propellant))
                return 0;
            double purity = ChemicalProductHelper.GetPurity(propellant);
            return purity >= HighGrade ? 1 : purity < LowGrade ? -1 : 0;
        }

        private static int Part(GameItem container, string liquidId)
        {
            return container.GetTagReadonly(Liquid.GetCurrentPartFromId(liquidId)).GetInt();
        }

        private static void SetPart(GameItem container, string liquidId, int value)
        {
            int v = Math.Max(0, value);
            container.ModifyTag(Liquid.GetCurrentPartFromId(liquidId), s => s.Enable().SetInt(v), true);
        }

        // Each second for a chemistry container, after the game's reactions (ChemicalHelper.HandleTemperatureReaction).
        internal static void Nitrate(GameItem container)
        {
            if (!registered || container == null || !container.IsTag("CONTAINER_TEMP_TAG"))
                return;
            double temp = container.GetTagReadonly("CONTAINER_TEMP_TAG").GetDouble();
            if (temp < NitrationMinTemp || temp > NitrationMaxTemp)
                return;
            int plant = Part(container, Liquid.LIQUID_PLANT_MATTER), acid = Part(container, Liquid.LIQUID_ACID);
            int limit = Math.Min(plant, acid);
            if (limit <= 0)
                return;
            int amount = Math.Min(limit, Math.Max(NitrationMinStep, (int)(limit * NitrationShare)));
            SetPart(container, Liquid.LIQUID_PLANT_MATTER, plant - amount);
            SetPart(container, Liquid.LIQUID_ACID, acid - amount);
            SetPart(container, NitroId, Part(container, NitroId) + amount);
            SetPart(container, Liquid.LIQUID_WATER, Part(container, Liquid.LIQUID_WATER) + amount);
        }

        // The game's kotton fabric goes into the blender as plant matter.
        internal static void MakeBlendable(GameItem fabric)
        {
            if (!registered || fabric == null)
                return;
            ChemicalHelper.InitChemItem(fabric, true, false);
            ChemicalHelper.SetChemContentML(fabric, Liquid.LIQUID_PLANT_MATTER, CottonPlantMatterMl);
        }

        // A liquid container with enough alcohol to clean with.
        internal static bool IsSolvent(GameItem item)
        {
            if (item == null || !item.IsTag("LIQUID_CONTAINER_TAG"))
                return false;
            int total = WaterHelper.GetTotalVolume(item);
            int alcohol = Part(item, Liquid.LIQUID_ALCOHOL);
            return alcohol >= SolventUseMl * 1000 && total > 0 && alcohol * 100L >= total * (long)SolventAlcoholPercent;
        }

        internal static void UseSolvent(GameItem item)
        {
            WaterHelper.RemoveMl(item, SolventUseMl);
        }

        internal static GameItem FindOwnedSolvent(GameItem except)
        {
            if (EmporiumEntry.Instance == null)
                return null;
            foreach (GameItem item in EmporiumEntry.Instance.GetAllOwnedItems(false, true))
            {
                if (item != null && item != except && IsSolvent(item))
                    return item;
            }
            return null;
        }
    }

    [HarmonyPatch(typeof(ChemicalHelper), nameof(ChemicalHelper.HandleTemperatureReaction))]
    internal static class NitrationPatch
    {
        private static void Postfix(GameItem container)
        {
            try
            {
                Chemistry.Nitrate(container);
            }
            catch (Exception e)
            {
                GunsmithMod.Warn("nitration: " + e.Message);
            }
        }
    }

    [HarmonyPatch(typeof(AmenitiesItemDirectory), "KottonFabric")]
    internal static class BlendableCottonPatch
    {
        private static void Postfix(GameItem __result)
        {
            try
            {
                Chemistry.MakeBlendable(__result);
            }
            catch (Exception e)
            {
                GunsmithMod.Warn("kotton fabric: " + e.Message);
            }
        }
    }

    // The reloader counts its rounds as the game's printer counts gun parts (PRINTER_GUN_PART_COUNT_INT, set here from
    // the ingot's purity); the propellant's purity then adds or takes one.
    [HarmonyPatch(typeof(MachinePrinter), "CaptureIngotQuality")]
    internal static class ReloaderPropellantPatch
    {
        private static void Postfix(GameItem printer, GameItem chip, List<GameItem> tray)
        {
            try
            {
                if (printer == null || chip == null || !printer.IsTag(Reloader.IdentityTag) || !chip.IsTag(Reloader.DieTag)
                    || !printer.IsTag("PRINTER_GUN_PART_COUNT_INT"))
                    return;
                GameItem propellant = null;
                foreach (GameItem item in MachineProgressHelper.GetItemsToConsume(chip, tray))
                {
                    if (item != null && item.IsID(Reloader.PropellantId))
                    {
                        propellant = item;
                        break;
                    }
                }
                int change = Chemistry.YieldChange(propellant);
                if (change == 0)
                    return;
                int count = Math.Max(1, printer.GetTagReadonly("PRINTER_GUN_PART_COUNT_INT").GetInt() + change);
                printer.ModifyTag("PRINTER_GUN_PART_COUNT_INT", s => s.SetInt(count), false);
                GunsmithMod.Log("reloader batch: " + count + " rounds with propellant of purity " + ChemicalProductHelper.GetPurity(propellant).ToString("F0"));
            }
            catch (Exception e)
            {
                GunsmithMod.Warn("reloader propellant: " + e.Message);
            }
        }
    }
}
