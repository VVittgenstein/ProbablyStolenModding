using HarmonyLib;

namespace GunsmithExpansion
{
    // Compatibility with Hypertech Storage Bay Expansion (超科技储藏区扩建; manifest ID hypertech_storage_bay_expansion, Workshop
    // 3833101873, read in its version 1.3.2; T-033). Code written for another mod goes in a file of its own and acts only while
    // that mod is enabled (project-rules/third-party-compat.md, VVittgenstein 2026-11-15 19:09:56). The rest of this mod does not
    // use this file: without it the mod is as in 0.7.6, and the file can go once that mod no longer does what is described here.
    //
    // What that mod does: its postfix of GameItem.Target (PatchMethods.TargetPostfix, which calls
    // HyperStorage.RunVanillaScrewdriverActionIfNeeded) calls GunHelper.DisassembleWeapon whenever the source is a screwdriver and
    // the target a gun (ADV_GUN_TAG) that is still there, without checking whose gun it is. This mod's screwdriver repair takes the
    // place of the game's use (TargetPatch in Repair.cs returns false), so the repaired gun is still there, and that postfix took
    // it apart in the same use, a repair customer's gun too.
    //
    // What this file does, using only the game's methods: in a GameItem.Target call in which the game's method did not run (a
    // prefix took its place) and the target is a gun of this mod, GunHelper.DisassembleWeapon leaves that gun whole from the
    // call's postfixes until the call has ended; a GameItem.Target call nested in them starts with no gun held. In every other
    // call nothing changes; the game's own use of the screwdriver runs GameItem.Target's method, which takes the gun apart before
    // any postfix. The patches are applied with the rest of this mod and do nothing while that mod is not enabled.
    internal static class HypertechCompat
    {
        internal const string ModId = "hypertech_storage_bay_expansion";

        private static bool logged;

        // The gun of the GameItem.Target call in which a prefix took the place of the game's method, from that call's postfixes
        // to its finalizers. It is set only in a call whose prefix below ran, so its finalizer always restores the value from
        // before the call.
        internal static GameItem ReplacedUseTarget;

        // Asked in each call: the game registers a mod when it loads it, and that mod may load after this one, so an answer read
        // when this mod starts would not be final (run t033-fix-ht-cf, 2026-11-15 19:22; CorpLink.cs has the same note).
        internal static bool Active
        {
            get
            {
                bool on = ModHelper.IsModRunning(ModId);
                if (on && !logged)
                {
                    logged = true;
                    GunsmithMod.Log("Hypertech Storage Bay Expansion is enabled: its screwdriver fallback is kept off this mod's guns in a use that took the place of the game's");
                }
                return on;
            }
        }
    }

    [HarmonyPatch(typeof(GameItem), nameof(GameItem.Target))]
    internal static class HypertechCompatTargetPatch
    {
        // The game's Harmony (2.4.2) skips a prefix after another returned false only when the prefix can change the call: it
        // returns bool, or takes an argument other than __instance, __originalMethod and __state that is out, ref, or of a class
        // type (MethodCreator.AddPrefixes, MethodCreatorTools.AffectsOriginal). This one takes only __state, so it runs in every
        // call unless an earlier prefix threw. __state keeps the value of an enclosing GameItem.Target call, and the value is
        // cleared for this call, so a nested call's own use is not held back by the enclosing one. __state stays null when that mod
        // is not enabled or an earlier prefix threw, and the postfix and the finalizer then change nothing.
        [HarmonyPriority(Priority.First)]
        private static void Prefix(out GameItem[] __state)
        {
            if (!HypertechCompat.Active)
            {
                __state = null;
                return;
            }
            __state = new[] { HypertechCompat.ReplacedUseTarget };
            HypertechCompat.ReplacedUseTarget = null;
        }

        // Before that mod's postfix, which has the normal priority.
        [HarmonyPriority(Priority.First)]
        private static void Postfix(GameItem targetItem, bool __runOriginal, GameItem[] __state)
        {
            if (__state != null && !__runOriginal && Faults.IsModGun(targetItem))
                HypertechCompat.ReplacedUseTarget = targetItem;
        }

        // Runs after every postfix of the call, that mod's too.
        private static void Finalizer(GameItem[] __state)
        {
            if (__state != null)
                HypertechCompat.ReplacedUseTarget = __state[0];
        }
    }

    [HarmonyPatch(typeof(GunHelper), nameof(GunHelper.DisassembleWeapon))]
    internal static class HypertechCompatDisassemblePatch
    {
        // Before DisassemblePatch in Repair.cs, which takes this mod's guns apart.
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(GameItem targetItem)
        {
            if (targetItem == null || targetItem != HypertechCompat.ReplacedUseTarget)
                return true;
            GunsmithMod.Log(targetItem.identifier + " kept whole: a take-apart after a use that took the place of the game's (Hypertech Storage Bay Expansion compatibility)");
            return false;
        }
    }
}
