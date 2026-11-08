using HarmonyLib;

namespace GunsmithExpansion
{
    // The game's nine gun mods that have no fitting limit (red dot sight, makeshift red dot sight, suppressor, Night Owl
    // suppressor, makeshift suppressor, compensator, laser sight, target acquisition device, toy laser pointer) on this
    // mod's guns (final-strategy 104; VVittgenstein 2026-11-04 00:43, on review/2026-11-04/vanilla-mods-audit.md):
    // each fits the guns on the lists of this mod's gun mods of its kind and the game's guns as before (Fittings.GameMods),
    // and on this mod's guns draws the pictures of the eighth redraw batch (Textures/Items/<id>_view_long_k3.png and the
    // like, current/006 section 8) at the gun's scale step (Mounting). The directory sets an item's identifier only after
    // its creator returns, so the patches name their mod.
    internal static class VanillaMods
    {
        internal const string MakeshiftSuppressor = "silencer_makeshift";

        internal static void OnCreated(GameItem mod, string id, string slot)
        {
            if (mod == null)
                return;
            GunModHelper.AllowOnlyWeapon(Fittings.GameMods[id], mod);
            Mounting.EnableOnModGuns(mod, slot, id);
        }
    }

    [HarmonyPatch(typeof(GunModDirectory), "CreateRDS")]
    internal static class RedDotPatch
    {
        private static void Postfix(GameItem __result)
        {
            VanillaMods.OnCreated(__result, "rds", "rail");
        }
    }

    [HarmonyPatch(typeof(GunModDirectory), "CreateRDSMakeshift")]
    internal static class MakeshiftRedDotPatch
    {
        private static void Postfix(GameItem __result)
        {
            VanillaMods.OnCreated(__result, "rds_makeshift", "rail");
        }
    }

    [HarmonyPatch(typeof(GunModDirectory), "CreateSilencer")]
    internal static class SilencerPatch
    {
        private static void Postfix(GameItem __result)
        {
            VanillaMods.OnCreated(__result, "silencer", "barrel");
        }
    }

    [HarmonyPatch(typeof(GunModDirectory), "CreateSilencerAdv")]
    internal static class NightOwlSilencerPatch
    {
        private static void Postfix(GameItem __result)
        {
            VanillaMods.OnCreated(__result, "silencer2", "barrel");
        }
    }

    [HarmonyPatch(typeof(GunModDirectory), "CreateSilencerMakeshift")]
    internal static class MakeshiftSilencerPatch
    {
        private static void Postfix(GameItem __result)
        {
            VanillaMods.OnCreated(__result, VanillaMods.MakeshiftSuppressor, "barrel");
        }
    }

    [HarmonyPatch(typeof(GunModDirectory), "CreateCompensator")]
    internal static class CompensatorPatch
    {
        private static void Postfix(GameItem __result)
        {
            VanillaMods.OnCreated(__result, "compensator", "barrel");
        }
    }

    [HarmonyPatch(typeof(GunModDirectory), "CreateLaser")]
    internal static class LaserPatch
    {
        private static void Postfix(GameItem __result)
        {
            VanillaMods.OnCreated(__result, "laser", "under");
        }
    }

    [HarmonyPatch(typeof(GunModDirectory), "CreateLaser2")]
    internal static class TargetAcquisitionPatch
    {
        private static void Postfix(GameItem __result)
        {
            VanillaMods.OnCreated(__result, "laser2", "under");
        }
    }

    [HarmonyPatch(typeof(GunModDirectory), "CreateLaserToy")]
    internal static class ToyLaserPatch
    {
        private static void Postfix(GameItem __result)
        {
            VanillaMods.OnCreated(__result, "laser_toy", "under");
        }
    }
}
