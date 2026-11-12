using System;
using System.Collections.Generic;
using HarmonyLib;

namespace GunsmithExpansion
{
    // Item 14, those who come to sell guns (VVittgenstein 2026-11-06 08:07, 08:37 and 08:45, current/008 quotes 37, 39 and 40).
    // The game's three sellers (ShowcaseHelper.GetFirearmSellerId: the gun neon sign draws the lower level citizen and the mercenary, the
    // heavy gun neon sign the upper level citizen) each put one gun of the game's on the counter, Fair or Poor, sold at 80% (clientItemFeatureBuying "discountedBuy20"). That gun is now
    // drawn from one pool, every gun alike: the game's guns this seller brings and this mod's guns of the seller's levels:
    //   - lower level citizen: level 1, the game's handmade pistol;
    //   - mercenary: level 1, the game's revolver and heavy handmade pistol;
    //   - upper level citizen: levels 1 and 2, the game's police handgun and SMG.
    // The customers of this mod are identities of those sellers, in the shares they have among the buyers (item 63, NewCustomers), and bring only this mod's guns:
    //   - gang member: level 2, during the gang war, 19.4% of the lower level citizens;
    //   - elite mercenary: level 3, with the third firearm permit in the display case, 9.4% of the mercenaries;
    //   - security tactical officer: level 3, with that permit and security reputation 30 or more, 9.4% of the mercenaries.
    // The game's action still decides the condition and makes its gun; a gun of this mod takes that gun's place on the way to the counter and has the same condition, by this mod's rule
    // (Faults.UpdateCondition). Poor: 2 to 4 hidden faults (DamagedGuns.Create). Fair: 2 to 4 faults repaired in an emergency way (DamagedGuns.CreateFair). The rest of the action, magazines and
    // parts and ammunition, stays as the game's. Agent's decisions: Fair as emergency repairs, its number of faults, and that a share whose condition does not hold stays with the game's seller.
    internal static class GunSellers
    {
        internal const double GangMemberPercent = 19.4;
        internal const double EliteMercPercent = 9.4;
        internal const double OfficerPercent = 9.4;

        private static readonly string[] LevelOne = { "一档" };
        private static readonly string[] LevelTwo = { "二档" };
        private static readonly string[] LevelThree = { "三档" };
        private static readonly string[] LevelsOneAndTwo = { "一档", "二档" };
        private static readonly Random rng = new Random();

        private sealed class Stock
        {
            internal string[] Tiers;
            internal int GameGuns;
        }

        // Set while a seller's stock goes on the counter.
        private static Stock current;

        internal static void OnLowerLevel(StoreClient client)
        {
            NewCustomers.Kind gang = NewCustomers.Find("gseGangMember");
            if (gang != null && gang.Allowed() && rng.NextDouble() * 100.0 < GangMemberPercent)
                Attach(client, gang, LevelTwo, 0);
            else
                Attach(client, null, LevelOne, 1);
        }

        internal static void OnMercenary(StoreClient client)
        {
            double roll = rng.NextDouble() * 100.0;
            NewCustomers.Kind k = roll < EliteMercPercent ? NewCustomers.Find("gseEliteMerc")
                : roll < EliteMercPercent + OfficerPercent ? NewCustomers.Find("gseSecurityTactical")
                : null;
            if (k != null && k.Allowed())
                Attach(client, k, LevelThree, 0);
            else
                Attach(client, null, LevelOne, 2);
        }

        internal static void OnUpperLevel(StoreClient client)
        {
            Attach(client, null, LevelsOneAndTwo, 2);
        }

        private static void Attach(StoreClient client, NewCustomers.Kind kind, string[] tiers, int gameGuns)
        {
            if (client == null)
                return;
            var stock = new Stock { Tiers = tiers, GameGuns = gameGuns };
            if (!Sellers.AroundStock(client, () => current = stock, () => current = null))
                return;
            if (kind != null)
            {
                NewCustomers.Dress(client, kind);
                GunsmithMod.Log(kind.En + " comes to sell a gun");
            }
        }

        // The gun of this mod the pool's draw gives in place of the game's gun of the seller going on the counter, or null when the game's gun stays.
        // The game's gun is left as it is here: GunSellerStockPatch destroys whichever of the two is not on a counter after the game's method.
        internal static GameItem Instead(GameItem item)
        {
            Stock stock = current;
            if (stock == null || item == null || GameGunLevels.Find(item.identifier) == null)
                return null;
            var guns = new List<GunDef>();
            // Not the 2077 add-on's guns: they come only from the sources of final-strategy 280, which do not include those who come to sell guns (VVittgenstein 2026-11-07 17:28 and 20:06,
            // current/T-006 quotes 15 and 20; until then the elite mercenary and the security tactical officer could bring its two tier three guns with the switch on).
            foreach (GunDef d in Catalog.Guns)
            {
                if (Array.IndexOf(stock.Tiers, d.Tier) >= 0)
                    guns.Add(d);
            }
            if (guns.Count == 0 || rng.Next(guns.Count + stock.GameGuns) >= guns.Count)
                return null;
            GunDef def = guns[rng.Next(guns.Count)];
            GameItem gun = GunFrameHelper.GetCondition(item) == ItemFeatureList.EquipmentCondition.Poor ? DamagedGuns.Create(def) : DamagedGuns.CreateFair(def);
            if (gun == null)
                return null;
            GunsmithMod.Log("gun seller brings " + def.Id + " (" + def.Tier + "): " + RepairOrders.Describe(Faults.Get(gun)));
            return gun;
        }

        // Destroys an item that is in no inventory and not destroyed yet (the test of GraphUtils.DestroyIfUnplaced, without its report of an unplaced item).
        internal static void DestroyIfLoose(GameItem item)
        {
            if (item == null || item.parentInventory != null)
                return;
            var element = item as GameItemElement;
            if (element != null && element.IsDestroyed())
                return;
            item.Destroy();
        }
    }

    [HarmonyPatch(typeof(StoreClientListGun), nameof(StoreClientListGun.CreateLLGunSeller))]
    internal static class LowerLevelSellerPatch
    {
        private static void Postfix(StoreClient __result)
        {
            GunSellers.OnLowerLevel(__result);
        }
    }

    [HarmonyPatch(typeof(StoreClientListGun), nameof(StoreClientListGun.CreateMercGunSeller))]
    internal static class MercenarySellerPatch
    {
        private static void Postfix(StoreClient __result)
        {
            GunSellers.OnMercenary(__result);
        }
    }

    [HarmonyPatch(typeof(StoreClientListGun), nameof(StoreClientListGun.CreateULGunSeller))]
    internal static class UpperLevelSellerPatch
    {
        private static void Postfix(StoreClient __result)
        {
            GunSellers.OnUpperLevel(__result);
        }
    }

    // On the way to the counter, as CounterPlacePatch (Supply.cs): the game's PreBuiltItemHelper methods that make the sellers' guns are short enough for the runtime to inline.
    // The gun of this mod goes into Harmony's __args as well as into the argument (T-027 item 1). Harmony (2.4.2 in the game) fills the __args array from the
    // arguments once, before the prefixes, and after each prefix that takes __args writes every element of the array back into its argument. Until 0.7.4 the
    // gun was put only into the ref argument and the game's gun destroyed at once; a prefix with __args that ran after this one, as Vanilla Food Expansion 0.3.10's
    // VendorSpawning.BeforeOffer does when that mod loads after this one, wrote the game's gun back, and the game's method was given the destroyed gun: the seller's
    // gun was gone from the counter. This prefix takes every argument by ref and puts their present values into the array, so the write after it changes only
    // the item; and the game's gun is destroyed after the game's method, by the finalizer, only when it is not on a counter.
    [HarmonyPatch(typeof(PlayerStore), "TryAddDirectSellingItemToTable", new[] { typeof(GameItem), typeof(bool), typeof(bool), typeof(bool), typeof(int) })]
    internal static class GunSellerStockPatch
    {
        private static void Prefix(ref GameItem gameItem, ref bool isOwend, ref bool isStolen, ref bool ignorePerk, ref int forcedHeat, object[] __args, out GameItem[] __state)
        {
            __state = null;
            GameItem gun = GunSellers.Instead(gameItem);
            if (gun != null)
            {
                __state = new[] { gameItem, gun };
                gameItem = gun;
            }
            __args[0] = gameItem;
            __args[1] = isOwend;
            __args[2] = isStolen;
            __args[3] = ignorePerk;
            __args[4] = forcedHeat;
        }

        // Whichever of the two the game's method was given in the end, the one that is on no counter is destroyed; when another patch gave the method the game's gun,
        // that gun stays and this mod's gun goes.
        private static void Finalizer(GameItem gameItem, GameItem[] __state)
        {
            if (__state == null)
                return;
            if (!ReferenceEquals(gameItem, __state[1]))
                GunsmithMod.Log("gun seller: the counter was offered " + (gameItem != null ? gameItem.identifier : "nothing") + " in place of " + __state[1].identifier);
            GunSellers.DestroyIfLoose(__state[0]);
            GunSellers.DestroyIfLoose(__state[1]);
        }
    }
}
