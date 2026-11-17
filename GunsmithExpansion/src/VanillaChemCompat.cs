using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace GunsmithExpansion
{
    // Compatibility with Vanilla Chemistry Expansion (原版化学扩展; manifest ID vanilla_chem_ext, Workshop 3832077837, read in its
    // version 1.8.1; T-034 item 1). Code written for another mod goes in a file of its own and acts only while that mod is enabled
    // (project-rules/third-party-compat.md, VVittgenstein 2026-11-15 19:09:56). The rest of this mod does not use this file: without
    // it the chemistry is as in src/Chemistry.cs, and the file can go once that mod no longer does what is described here.
    //
    // What happens without this file (read in its code): the game runs ChemicalHelper.HandleTemperatureReaction each second for
    // every liquid container, and both mods add their reactions after it. This mod's nitration (Chemistry.Nitrate) turns plant
    // matter and acid at 40 to 90 degrees into nitrocellulose and water whatever else the container holds, so it also ran in that
    // mod's processes (heating its dream toxin, its esterification with plant matter left in), leaving nitrocellulose in their
    // products and using up the acid they use. That mod's dry distillation (FlavorChemistry.DryDistill) turns dry plant matter at
    // 150 degrees and up into acetic acid, methanol and debris, so it also ran in a container of nitrocellulose dried on a hot
    // plate, on the plant matter left in the propellant mix.
    //
    // What this file does while that mod is enabled (final-strategy 323, VVittgenstein 2026-11-16 21:33:16):
    //   - a container that holds any of the liquids that mod registers (TheirLiquids) gets no nitration;
    //   - in a container that holds nitrocellulose, that mod's dry distillation does not run.
    // This rests on that mod's liquid identifiers and on the name and parameters of its method; when that mod changes them, the part
    // resting on them stops working (final-strategy 324: the players are told). Its method is reached through the game's ModReflect,
    // which reaches another mod's types only when that mod is listed in <Prerequisites> (manifest.xml). What is not found is logged
    // once and that part stays off; the rest of this mod is not affected.
    internal static class VanillaChemCompat
    {
        internal const string ModId = "vanilla_chem_ext";

        // The liquids that mod registers, read in its version 1.8.1 (every liquid it makes: ToxinLiquid, OreLiquids, NcLiquids, and
        // IaLiquids.AddLiquid called from IaLiquids, FlavorChemistry, Lantern, Antibiotics, Crackine, SaltOre and Xinke).
        internal static readonly string[] TheirLiquids =
        {
            "dream_toxin",
            "vce_calcite", "vce_quartz", "vce_quicklime", "vce_calcium_carbonate", "vce_water_glass", "vce_silica_gel",
            "ia_yeast_wine", "ia_yeast_super", "ia_yeast_red", "ia_methanol", "ia_ice",
            "ia_acetic_acid", "ia_ethyl_acetate",
            "nc_spent_fuel", "nc_pu_dissolved", "nc_fission_products", "nc_pu_extract", "nc_plutonium",
            "vce_capsaicin", "vce_lantern_ester",
            "vce_cephalosporin", "vce_bioliquid_waste",
            "vce_crackine_powder",
            "vce_salt",
            "vce_alkaloid",
        };

        private static readonly List<string> theirParts = new List<string>();
        private static int partsForCount = -1;
        private static string plantPart, acidPart, nitroPart;
        private static bool patchTried, nitrationLogged, distillLogged, failureLogged;

        // Asked only for a container where it matters: ModHelper.IsModRunning copies the game's list of mods each time.
        private static bool Active
        {
            get { return ModHelper.IsModRunning(ModId); }
        }

        // The tags of TheirLiquids that are registered, and of plant matter, acid and nitrocellulose; read again when the number of
        // registered liquids changes. Empty when that mod has registered none of its liquids.
        private static List<string> TheirParts()
        {
            Dictionary<string, Liquid> dict = Liquid.LiquidDict;
            if (dict == null)
                return theirParts;
            if (dict.Count != partsForCount)
            {
                partsForCount = dict.Count;
                theirParts.Clear();
                Liquid liquid;
                foreach (string id in TheirLiquids)
                {
                    if (dict.TryGetValue(id, out liquid) && liquid != null && !string.IsNullOrEmpty(liquid.currentPart))
                        theirParts.Add(liquid.currentPart);
                }
                plantPart = dict.TryGetValue(Liquid.LIQUID_PLANT_MATTER, out liquid) && liquid != null ? liquid.currentPart : null;
                acidPart = dict.TryGetValue(Liquid.LIQUID_ACID, out liquid) && liquid != null ? liquid.currentPart : null;
                nitroPart = dict.TryGetValue(Chemistry.NitroId, out liquid) && liquid != null ? liquid.currentPart : null;
            }
            return theirParts;
        }

        private static int Amount(GameItem container, string part)
        {
            return part == null ? 0 : container.GetTagReadonly(part).GetInt();
        }

        // Before this mod's nitration: false keeps it from running in a container that holds one of that mod's liquids.
        internal static bool AllowNitration(GameItem container)
        {
            if (container == null || TheirParts().Count == 0 || Amount(container, plantPart) <= 0 || Amount(container, acidPart) <= 0)
                return true;
            bool holds = false;
            foreach (string part in theirParts)
            {
                if (container.GetTagReadonly(part).GetInt() > 0)
                {
                    holds = true;
                    break;
                }
            }
            if (!holds || !Active)
                return true;
            if (!nitrationLogged)
            {
                nitrationLogged = true;
                GunsmithMod.Log("Vanilla Chemistry Expansion is enabled: no nitration in a container holding one of its liquids (first: " + container.identifier + ")");
            }
            return false;
        }

        // At the first temperature reaction of the run, before that mod's reactions (they run after the game's): its dry
        // distillation gets the prefix below. The only call of its dry distillation in its code is from its reactions there.
        internal static void PatchDryDistillOnce()
        {
            if (patchTried)
                return;
            patchTried = true;
            if (!Active)
                return;
            try
            {
                Type flavor = ModReflect.FindType("FlavorChemistry");
                if (flavor == null)
                    throw new MissingMemberException("FlavorChemistry");
                MethodBase distill = ModReflect.Target(flavor, "DryDistill", new[] { typeof(GameItem), typeof(double) });
                // Under the Harmony ID the game's ModLoader gives this mod, as in CorpLink.cs: the game takes these patches off when it
                // stops the mod.
                var harmony = new Harmony("mod." + GunsmithMod.ModId);
                harmony.Patch(distill, prefix: new HarmonyMethod(AccessTools.Method(typeof(VanillaChemCompat), nameof(DryDistillPrefix))));
                GunsmithMod.Log("Vanilla Chemistry Expansion is enabled: its dry distillation does not run in a container holding nitrocellulose");
            }
            catch (Exception e)
            {
                GunsmithMod.Warn("Vanilla Chemistry Expansion compatibility: its dry distillation was not found and is not held back: " + e.Message);
            }
        }

        // Before that mod's FlavorChemistry.DryDistill(container, temperature): false skips it, which then returns 0 (nothing changed).
        private static bool DryDistillPrefix(GameItem __0)
        {
            try
            {
                if (__0 == null || !Chemistry.Registered)
                    return true;
                TheirParts();
                if (Amount(__0, nitroPart) <= 0 || !Active)
                    return true;
                if (!distillLogged)
                {
                    distillLogged = true;
                    GunsmithMod.Log("no dry distillation of Vanilla Chemistry Expansion in a container holding nitrocellulose (first: " + __0.identifier + ")");
                }
                return false;
            }
            catch (Exception e)
            {
                LogFailure("dry distillation", e);
                return true;
            }
        }

        internal static void LogFailure(string what, Exception e)
        {
            if (failureLogged)
                return;
            failureLogged = true;
            GunsmithMod.Warn("Vanilla Chemistry Expansion compatibility, " + what + ": " + e.Message);
        }
    }

    // Before this mod's own nitration (src/Chemistry.cs), which runs after the game's temperature reactions.
    [HarmonyPatch(typeof(Chemistry), nameof(Chemistry.Nitrate))]
    internal static class VanillaChemCompatNitrationPatch
    {
        private static bool Prefix(GameItem container)
        {
            try
            {
                return VanillaChemCompat.AllowNitration(container);
            }
            catch (Exception e)
            {
                VanillaChemCompat.LogFailure("nitration", e);
                return true;
            }
        }
    }

    // Before the game's temperature reactions, and so before that mod's: the patch of its dry distillation, once.
    [HarmonyPatch(typeof(ChemicalHelper), nameof(ChemicalHelper.HandleTemperatureReaction))]
    internal static class VanillaChemCompatReactionPatch
    {
        private static void Prefix()
        {
            VanillaChemCompat.PatchDryDistillOnce();
        }
    }
}
