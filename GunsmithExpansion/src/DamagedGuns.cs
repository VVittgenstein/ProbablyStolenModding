using System;
using System.Collections.Generic;
using HarmonyLib;

namespace GunsmithExpansion
{
    // Damaged guns for sale and in the dump (final-strategy 21). Thieves and scavengers sometimes have one of this
    // mod's guns among their wares, with 2 to 4 faults nobody tells about; scavenging the dumping grounds at night
    // sometimes turns one up. Their value is about 30% of a whole gun while faults are left (Faults.UpdateValue).
    // On sellers the tier follows the weapon permit (RepairOrders.PickTier; repair customers go by the certificate's level since 0.6.0); thieves also bring the special tier, so that every
    // gun of the catalog can turn up damaged. At the dumping grounds (VVittgenstein 2026-11-06 04:01, current/008 quote 24, item 80; until 0.6.2 4% by the
    // weapon permit): tier one 0.75% of the scavenges, the other tiers 0.25% in all, whatever the permit. Agent's reading: tiers two and three alike; the special tier stays out, as it was.
    internal static class DamagedGuns
    {
        internal const int ThiefChancePercent = 25;
        internal const int ScavengerChancePercent = 20;
        internal const double DumpFirstTierPercent = 0.75;
        internal const double DumpOtherTiersPercent = 0.25;
        internal const int ThiefSpecialPercent = 10;
        private static readonly Random rng = new Random();

        internal static GunDef PickGun(bool allowSpecial)
        {
            string tier = allowSpecial && rng.Next(100) < ThiefSpecialPercent ? "特殊" : RepairOrders.TierName(RepairOrders.PickTier());
            var guns = new List<GunDef>();
            foreach (GunDef d in AddOn2077.ForSale())
            {
                if (d.Tier == tier)
                    guns.Add(d);
            }
            return guns.Count > 0 ? guns[rng.Next(guns.Count)] : null;
        }

        internal static GunDef PickDumpGun(bool firstTier)
        {
            string tier = firstTier ? "一档" : rng.Next(2) == 0 ? "二档" : "三档";
            var guns = new List<GunDef>();
            foreach (GunDef d in AddOn2077.ForSale())
            {
                if (d.Tier == tier)
                    guns.Add(d);
            }
            return guns.Count > 0 ? guns[rng.Next(guns.Count)] : null;
        }

        internal static GameItem Create(GunDef def)
        {
            GameItem gun = DirectoryMaster.Item(def.Id, true);
            if (gun == null)
                return null;
            int count = 2 + rng.Next(3);
            Faults.Set(gun, Faults.RandomFaults(def.Antique, count, 0, rng));
            return gun;
        }

        // A gun in Fair condition (Faults.UpdateCondition): every fault repaired in an emergency way, nothing hidden (GunSellers).
        internal static GameItem CreateFair(GunDef def)
        {
            GameItem gun = DirectoryMaster.Item(def.Id, true);
            if (gun == null)
                return null;
            int count = 2 + rng.Next(3);
            Dictionary<Fault, FaultState> faults = Faults.RandomFaults(def.Antique, count, 0, rng);
            foreach (Fault f in new List<Fault>(faults.Keys))
                faults[f] = FaultState.Emergency;
            Faults.Set(gun, faults);
            return gun;
        }

        // The gun goes on the counter after the seller's wares (Sellers.AfterStock): a thief's or petty thief's (stolen) in a
        // carton (DeliveryBoxes.OfferBoxed; VVittgenstein 2026-11-07 22:35 to 2026-11-08 00:19, current/T-006 quotes 24 to 26), a
        // scavenger's by itself; on the counter top when the front counter has no room (Sellers.Offer).
        internal static bool TryAddToSeller(StoreClient client, int chancePercent, bool stolen, bool allowSpecial)
        {
            if (client == null || rng.Next(100) >= chancePercent)
                return false;
            GunDef def = PickGun(allowSpecial);
            if (def == null)
                return false;
            string who = client.identifier;
            return Sellers.AfterStock(client, () =>
            {
                GameItem gun = Create(def);
                if (gun == null || PlayerStore.Instance == null)
                    return;
                // thieves' guns carry the game's stolen mark; scavengers' may have a filed serial number (Stolen)
                if (!stolen)
                    Stolen.MaybeFile(gun, Stolen.ScavengerPercent);
                if (stolen)
                    DeliveryBoxes.OfferBoxed(gun, true);
                else
                    Sellers.Offer(gun);
                GunsmithMod.Log(who + " brings damaged " + def.Id + ": " + RepairOrders.Describe(Faults.Get(gun)));
            });
        }
    }

    [HarmonyPatch(typeof(StoreClientList), nameof(StoreClientList.CreateThief))]
    internal static class ThiefPatch
    {
        private static void Postfix(StoreClient __result)
        {
            DamagedGuns.TryAddToSeller(__result, DamagedGuns.ThiefChancePercent, true, true);
        }
    }

    [HarmonyPatch(typeof(StoreClientList), nameof(StoreClientList.CreatePettyThief))]
    internal static class PettyThiefPatch
    {
        private static void Postfix(StoreClient __result)
        {
            DamagedGuns.TryAddToSeller(__result, DamagedGuns.ThiefChancePercent, true, true);
        }
    }

    [HarmonyPatch(typeof(StoreClientList), nameof(StoreClientList.CreateScavGeneral))]
    internal static class ScavengerPatch
    {
        private static void Postfix(StoreClient __result)
        {
            DamagedGuns.TryAddToSeller(__result, DamagedGuns.ScavengerChancePercent, false, false);
        }
    }

    // One scavenging attempt at the dumping grounds (ScavHelper.ScavengeDumpingGrounds takes these items).
    [HarmonyPatch(typeof(ScavHelper), nameof(ScavHelper.GetRandomScavengedItem), new[] { typeof(GameItem) }, new[] { ArgumentType.Out })]
    internal static class DumpPatch
    {
        private static readonly Random rng = new Random();

        private static void Postfix(ref List<GameItem> __result)
        {
            double roll = rng.NextDouble() * 100.0;
            if (__result == null || roll >= DamagedGuns.DumpFirstTierPercent + DamagedGuns.DumpOtherTiersPercent)
                return;
            GunDef def = DamagedGuns.PickDumpGun(roll < DamagedGuns.DumpFirstTierPercent);
            GameItem gun = def != null ? DamagedGuns.Create(def) : null;
            if (gun == null)
                return;
            __result.Add(gun);
            GunsmithMod.Log("dumping grounds turn up damaged " + def.Id + ": " + RepairOrders.Describe(Faults.Get(gun)));
        }
    }
}
