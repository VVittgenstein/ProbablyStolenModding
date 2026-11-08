using HarmonyLib;

namespace GunsmithExpansion
{
    // Workshop reputation (final-strategy 40). Guns the store makes itself carry a mark: the game scales a gun's base
    // stats by its print quality when a printed frame is finished into the gun (GunFrameHelper.Finish) and when the
    // printer makes a gun whole (MachinePrinter.HandlePrintedItemQuality), both through GunHelper.MultiplyBaseStat,
    // which is where the mark is set; item tags are saved with the item (SaveItemNode.itemState). Delivering such a gun
    // on a gun buyer's custom order or a contract raises the reputation by 1, on a reservation pickup by 2. Bought
    // guns, repairs and modification orders (the customer's own gun) do not count. The reputation sets how many
    // reservations the store can hold at once (2, then 3 from 5 points, 4 from 15 and 5 from 30) and how many guns a
    // contract asks for (4, then 5 from 10 points and 6 from 25). Kept in PlayerStore.modData; the store gets a notice
    // when it rises. Agent's decisions: the points and the thresholds.
    internal static class Workshop
    {
        internal const string SelfMadeTag = "GSE_SELF_MADE_TAG";
        private const string Key = "workshop_reputation";
        internal const int CustomOrderPoints = 1;
        internal const int ReservationPoints = 2;
        internal const int ContractPoints = 1;

        internal static int Reputation
        {
            get { return ModHelper.GetModDataInt(GunsmithMod.ModId, Key, 0); }
        }

        internal static int MaxReservations()
        {
            int r = Reputation;
            return r >= 30 ? 5 : r >= 15 ? 4 : r >= 5 ? 3 : 2;
        }

        internal static bool IsSelfMade(GameItem gun)
        {
            return gun != null && gun.IsTag(SelfMadeTag);
        }

        internal static void MarkSelfMade(GameItem item)
        {
            if (item != null && item.IsGameItemType("FIREARM"))
                item.EnableTag(SelfMadeTag, false);
        }

        internal static void Add(int points, string why)
        {
            int r = Reputation + points;
            ModHelper.SetModDataInt(GunsmithMod.ModId, Key, r);
            if (StoreUIManager.Instance != null)
                StoreUIManager.Instance.Notify(string.Format(Text.Pick("工坊名声 +{0}（{1}），现为 {2}", "Workshop reputation +{0} ({1}), now {2}"), points, why, r), "default");
            GunsmithMod.Log("workshop reputation +" + points + " for a " + why + ", now " + r);
        }

        // Before GunOrderData.OnOrderAccepted pays the order, while the gun is still the negotiated one.
        internal static void OnOrderAccepted(GunOrderData order)
        {
            if (order == null || order.isServiceOrder || !IsSelfMade(GunOrderData.ResolveOrderGun()))
                return;
            if (Reservations.PickupOf(order) != null)
                Add(ReservationPoints, Text.Pick("预订", "reservation"));
            else if (Contracts.IsCourierOrder(order))
                Add(ContractPoints, Text.Pick("合同", "contract"));
            else
                Add(CustomOrderPoints, Text.Pick("定制订单", "custom order"));
        }

        // Guns of a long-term contract (final-strategy 40: the workshop reputation sets the contracts' size).
        internal static int ContractSize()
        {
            int r = Reputation;
            return r >= 25 ? 6 : r >= 10 ? 5 : 4;
        }

        internal static void AppendTooltip(RichTextBuilder builder, GameItem item)
        {
            if (IsSelfMade(item))
                builder.AddLine(Text.Pick("本店自造", "Made in this workshop"), true, RenderHandler.ColorPalette.LightGreen);
        }
    }

    [HarmonyPatch(typeof(GunHelper), nameof(GunHelper.MultiplyBaseStat))]
    internal static class SelfMadePatch
    {
        private static void Postfix(GameItem item)
        {
            Workshop.MarkSelfMade(item);
        }
    }
}
