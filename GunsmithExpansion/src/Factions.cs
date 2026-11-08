using System;
using System.Collections.Generic;
using HarmonyLib;

namespace GunsmithExpansion
{
    // Faction and news links (final-strategy 43). The game's faction operations are its news events that move prices
    // and risk; this mod ties its own parts to three of them while they run (StoreOperationManager.IsOperationActive):
    // during the black market's gang war thieves bring guns twice as often and gang members come for submachine guns
    // (NewCustomers); during the police crackdown or martial law thieves bring no guns; during
    // the revolutionaries' mobilization their contract offers come twice as often (Contracts). Guns sold to the
    // revolutionaries (gun orders of revolution buyers, contract guns included) are counted, and when the factions pick
    // their next operation (StoreOperationManager.QueueOperation draws one of the possible operations at random) each
    // possible mobilization gets one more draw per 5 guns sold, at most 4 more. Agent's decisions: which operations,
    // the effects and the numbers.
    internal static class Factions
    {
        private const string SoldKey = "revolution_guns";
        internal const int GunsPerDraw = 5;
        internal const int MaxExtraDraws = 4;
        internal static readonly string[] MobilizationIds = { "rev_mobilization", "rev_mobilization_secret" };

        internal static bool Active(string operation)
        {
            StoreStation station = StoreStation.Instance;
            return station != null && station.storeOperationManager != null && station.storeOperationManager.IsOperationActive(operation);
        }

        internal static bool GangWar()
        {
            return Active("bm_gang_war");
        }

        internal static bool Crackdown()
        {
            return Active("crackdown") || Active("sec_martial_law");
        }

        internal static bool Mobilizing()
        {
            return Active("rev_mobilization") || Active("rev_mobilization_secret");
        }

        internal static float ThiefGunFactor()
        {
            return Crackdown() ? 0f : GangWar() ? 2f : 1f;
        }

        internal static int RevolutionGuns
        {
            get { return ModHelper.GetModDataInt(GunsmithMod.ModId, SoldKey, 0); }
        }

        // Before GunOrderData.OnOrderAccepted: a gun sold to a revolution buyer.
        internal static void OnOrderAccepted(GunOrderData order)
        {
            StoreClientInstance current = PlayerStore.Instance != null ? PlayerStore.Instance.currentClientInstance : null;
            StoreClient client = current != null ? current.GetClientBlueprint() : null;
            if (order == null || order.isServiceOrder || client == null || client.clientFaction != "FACTION_REVOLUTION")
                return;
            ModHelper.SetModDataInt(GunsmithMod.ModId, SoldKey, RevolutionGuns + 1);
            GunsmithMod.Log("gun sold to the revolution, " + RevolutionGuns + " so far");
        }

        internal static int ExtraDraws()
        {
            return Math.Min(MaxExtraDraws, RevolutionGuns / GunsPerDraw);
        }

        internal static void WeightMobilization(List<string> operations)
        {
            int extra = ExtraDraws();
            if (operations == null || extra == 0)
                return;
            foreach (string id in MobilizationIds)
            {
                if (!operations.Contains(id))
                    continue;
                for (int i = 0; i < extra; i++)
                    operations.Add(id);
                GunsmithMod.Log(id + " gets " + extra + " more draws");
            }
        }
    }

    [HarmonyPatch(typeof(StoreOperationManager), nameof(StoreOperationManager.QueueOperation))]
    internal static class MobilizationPatch
    {
        private static void Prefix(StoreOperationManager __instance)
        {
            Factions.WeightMobilization(__instance.availableOperations);
        }
    }
}
