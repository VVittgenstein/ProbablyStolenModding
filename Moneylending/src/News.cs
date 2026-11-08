namespace Moneylending
{
    // Station news (StoreEventManager events) that change how borrowers fare. Read on the night before a due day,
    // when the list still holds the events of the day that ended.
    internal static class News
    {
        private static bool Active(string id)
        {
            StoreStation station = StoreStation.Instance;
            return station != null && station.storeEventManager != null && station.storeEventManager.IsEventActive(id);
        }

        // Security is hunting the Revolution: cells go to ground.
        internal static bool CrackdownActive()
        {
            return Active("crackdown") || Active("sec_raid_revolution") || Active("sec_martial_law");
        }

        // More patrols and raids: thieves are caught more often.
        internal static bool SecurityHeat()
        {
            return Active("crackdown") || Active("sec_raid_black_market") || Active("sec_increase_patrols") || Active("sec_martial_law");
        }

        internal static bool GangWar()
        {
            return Active("bm_gang_war");
        }

        // Workers on strike are not paid.
        internal static bool WorkerStrike()
        {
            return Active("ll_worker_strike");
        }
    }
}
