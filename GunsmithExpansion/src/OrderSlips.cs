using System.Collections.Generic;

namespace GunsmithExpansion
{
    // Gun orders on paper (item 20; VVittgenstein 2026-11-05 22:40, current/008 quote 17; 2026-11-06, quote 29): when the store takes a
    // reservation or a contract, what was agreed and on which days comes on the counter as a slip, so that it is not only in what the customer said. It is the game's own paper item,
    // as its appraisal slip (AppraisalServiceHelper.GiveAppraisalFee, PreBuiltItemHelper.CreatePaperWithText), so there is no new picture. It stays until the player throws it away.
    internal static class OrderSlips
    {
        private static void Give(string title, string body)
        {
            if (PlayerStore.Instance == null)
                return;
            GameItem slip = PreBuiltItemHelper.CreatePaperWithText(body, title);
            // The game's TryAddDirectSellingItemToTable destroys what the front counter cannot take (GraphUtils.TryAcceptAllOrDestroy), so
            // whether it fits is checked first; a slip that does not goes to the inventory, or wherever GraphUtils.EmporiumTryAdd finds room after it.
            EmporiumEntry store = EmporiumEntry.Instance;
            if (store != null && !GraphUtils.CanAccept(store.frontInvinvElement, slip))
            {
                GeneralHelper.SetItemOwned(slip, true);
                GraphUtils.EmporiumTryAdd(store.invElement, slip);
            }
            else
                PlayerStore.Instance.TryAddDirectSellingItemToTable(slip, true, false, false, 0);
            GunsmithMod.Log("order slip: " + title);
        }

        private static int Day(int nights)
        {
            return StoreStation.GetDayCounter() + nights;
        }

        internal static void Reservation(GunDef gun, int nights, int deposit, int due)
        {
            string name = Text.Pick(gun.NameZh, gun.NameEn);
            Give(string.Format(Text.Pick("预订单：{0}", "Reservation: {0}"), name), string.Format(Text.Pick(
                "枪：{0}\n取货：第 {1} 天（{2}）\n已收定金：{3} 信用点\n取货时收：{4} 信用点\n当天交不出枪：退回定金，声望 -{5}",
                "Gun: {0}\nPickup: day {1} ({2})\nDeposit received: {3} credits\nDue at pickup: {4} credits\nNo gun on the day: the deposit goes back, reputation -{5}"),
                name, Day(nights), Reservations.When(nights), deposit, due, Reservations.ReputationLoss));
        }

        internal static void Contract(Contracts.Contract c)
        {
            var lines = new List<string>();
            string classes = string.Join(Text.Pick("或", " or "), System.Array.ConvertAll(c.Classes, Text.ClassName));
            lines.Add(string.Format(Text.Pick("{0} 把{1}（{2}），{3}不低于 {4}", "{0} {1} ({2}), {3} at least {4}"), c.Total, classes, Text.TierName(c.Tier), Text.StatName(c.Stat), c.Bar)
                + (c.Legal ? Text.Pick("，必须合法", ", legal guns only") : ""));
            for (int b = 0; b < c.Size.Length; b++)
            {
                if (c.Size[b] > 0)
                    lines.Add(string.Format(Text.Pick("第 {0} 天：{1} 把", "Day {0}: {1} guns"), Day(c.Nights[b]), c.Size[b]));
            }
            lines.Add(string.Format(Text.Pick("每把 {0} 信用点，到货就付", "{0} credits a gun, paid on delivery"), c.Price));
            lines.Add(Text.Pick("误了日子，合同作废", "Miss a date and the contract is off"));
            Give(Text.Pick("枪械合同", "Gun contract"), string.Join("\n", lines.ToArray()));
        }
    }
}
