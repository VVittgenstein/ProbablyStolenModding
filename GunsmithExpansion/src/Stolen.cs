using System;
using HarmonyLib;

namespace GunsmithExpansion
{
    // Stolen guns with filed serial numbers (final-strategy 48; first proposed by the Agent in draft 1). Some guns
    // brought in for repair and some damaged guns that scavengers sell carry a filed serial number (thieves' guns
    // already come with the game's stolen mark). It does not show until the gun's surface is checked with a magnifier
    // (Repair, the check that finds hidden surface faults); the check gives the gun the game's stolen mark
    // (StolenHelper.InitStolenItem), so it shows as stolen and the game's rules for stolen goods apply from then on:
    // reporting the customer with the counter's security button pays the game's reward for a customer selling stolen
    // goods (PlayerStore.OnCustomerReported, which looks for stolen items on the counter that the store does not own),
    // and security inspections take it. A security inspection also finds a filed serial nobody has checked
    // (ContrabandHelper.InspectItem). With a repair customer the store can refuse the job, repair the gun anyway for
    // 50% more pay, or report the customer. Agent's decisions: 15% of repair customers' guns, 15% of scavengers'
    // damaged guns, the 50%.
    internal static class Stolen
    {
        internal const string FiledTag = "GSE_FILED_SERIAL_TAG";
        internal const string FoundTag = "GSE_FILED_SERIAL_FOUND_TAG";
        internal const int RepairPercent = 15;
        internal const int ScavengerPercent = 15;
        internal const int RepairBonusPercent = 50;
        private static readonly Random rng = new Random();

        internal static void MaybeFile(GameItem gun, int percent)
        {
            if (gun != null && rng.Next(100) < percent)
            {
                gun.EnableTag(FiledTag, false);
                GunsmithMod.Log(gun.identifier + " has a filed serial number");
            }
        }

        internal static bool IsFiled(GameItem gun)
        {
            return gun != null && gun.IsTag(FiledTag);
        }

        internal static bool IsFound(GameItem gun)
        {
            return gun != null && gun.IsTag(FoundTag);
        }

        // After a magnifier check of the gun's surface.
        internal static void OnSurfaceCheck(GameItem gun)
        {
            if (!IsFiled(gun) || IsFound(gun))
                return;
            gun.EnableTag(FoundTag, false);
            if (!StolenHelper.IsStolenItem(gun))
                StolenHelper.InitStolenItem(gun, 0);
            if (StoreUIManager.Instance != null)
                StoreUIManager.Instance.Notify(Text.Pick("序列号被磨掉了：这是一把赃枪。", "The serial number is filed off: this gun is stolen."), "default");
            GunsmithMod.Log(gun.identifier + ": filed serial number found, marked stolen");
        }

        internal static int RepairPay(GameItem gun, int pay)
        {
            return IsFound(gun) ? pay * (100 + RepairBonusPercent) / 100 : pay;
        }

        internal static void AppendTooltip(RichTextBuilder builder, GameItem item)
        {
            if (IsFound(item))
                builder.AddLine(Text.Pick("序列号被磨掉（赃枪）", "Serial number filed off (stolen)"), true, RenderHandler.ColorPalette.Red);
        }
    }

    // An inspection finds a filed serial number too; the gun then gets the game's stolen mark and is handled like the
    // other stolen goods it finds.
    [HarmonyPatch(typeof(ContrabandHelper), nameof(ContrabandHelper.InspectItem))]
    internal static class FiledSerialInspectionPatch
    {
        private static void Postfix(GameItem gameItem, ref bool __result)
        {
            if (__result || !Stolen.IsFiled(gameItem) || gameItem.IsTag("ITEM_BEHIND_LOCK_TAG") || gameItem.IsTag("ITEM_HIDDEN_TAG") || gameItem.IsTag("LOCKED_TAG"))
                return;
            if (!StolenHelper.IsStolenItem(gameItem))
                StolenHelper.InitStolenItem(gameItem, 0);
            __result = true;
        }
    }
}
