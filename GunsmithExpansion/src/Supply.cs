using System;
using System.Collections.Generic;
using HarmonyLib;

namespace GunsmithExpansion
{
    // The retired gunsmith's stock (final-strategy 33 ①). The game's visits and stock rolls stay as they are; for the
    // metal printing module and the CNC module, every visit on which the module did not go on the counter raises its
    // chance on the next visit by 15 percentage points, and a visit on which it does sets the raise back to 0. The
    // game rolls each module first (33% and 25%, plus 15 points for each of weapon permits II and III,
    // StoreClientListSpec.CreateRetiredGunsmith); when that roll fails, a second roll with the chance
    // raise / (100 - game chance) puts the module on the counter, so that both rolls together give the game's chance
    // plus the raise. The number of visits each module was missed is kept in PlayerStore.modData.
    internal static class ModulePity
    {
        internal const int StepPercent = 15;
        private const int PerPermitPercent = 15;
        internal static readonly string[] Modules = { "printer_module_metal", "printer_module_cnc" };
        private static readonly int[] GameChance = { 33, 25 };
        private static readonly Random rng = new Random();
        private static readonly HashSet<string> placed = new HashSet<string>();
        private static bool watching;

        // The stock goes on the counter in the end action of the greeting; the checks run around it.
        internal static void Attach(StoreClient client)
        {
            Dialogue d = client != null ? client.mainDialogue : null;
            if (d == null || d.endAction == null)
                return;
            d.endAction = (Action)Delegate.Combine(new Action(Begin), d.endAction, new Action(End));
        }

        internal static void OnPlaced(GameItem item)
        {
            if (watching && item != null)
                placed.Add(item.identifier);
        }

        internal static int Missed(string module)
        {
            return ModHelper.GetModDataInt(GunsmithMod.ModId, "missed_" + module, 0);
        }

        internal static int Chance(int index)
        {
            int steps = (PermitHelper.IsGunPermitII() ? 1 : 0) + (PermitHelper.IsGunPermitIII() ? 1 : 0);
            return GameChance[index] + PerPermitPercent * steps;
        }

        private static void Begin()
        {
            placed.Clear();
            watching = true;
        }

        private static void End()
        {
            if (PlayerStore.Instance == null)
            {
                watching = false;
                return;
            }
            for (int i = 0; i < Modules.Length; i++)
            {
                string id = Modules[i];
                int missed = Missed(id);
                string how = placed.Contains(id) ? "game roll" : null;
                if (how == null && missed > 0)
                {
                    int chance = Chance(i);
                    double second = chance >= 100 ? 0 : Math.Min(1.0, StepPercent * missed / (double)(100 - chance));
                    if (rng.NextDouble() < second)
                    {
                        Sellers.Offer(id);
                        if (placed.Contains(id))
                            how = "raised chance";
                    }
                }
                ModHelper.SetModDataInt(GunsmithMod.ModId, "missed_" + id, how != null ? 0 : missed + 1);
                GunsmithMod.Log("retired gunsmith: " + id + (how != null ? " on the counter (" + how + ")" : " missed")
                    + ", missed before " + missed);
            }
            watching = false;
        }
    }

    // The plasma cutter cuts one metal ingot into 2 scrap metal (final-strategy 35; VVittgenstein 2026-11-08 02:48, current/T-007 quote 23).
    // Until 0.7 it was 3: the game's furnace makes 1 ingot of 2 scrap metal a night, or 2 ingots of 3 with the blast
    // furnace module, so cutting each ingot back into 3 gave 1 or 3 more scrap metal a night (02:26,
    // quote 22). With 2 the furnace without a module gives back what it took; with the blast module it still gives 1 more a night.
    // Repair.cs runs it as a use of the welder on an ingot the player owns.
    internal static class IngotCut
    {
        internal const int ScrapPerIngot = 2;

        internal static bool Applies(GameItem source, GameItem target)
        {
            return source != null && target != null && source.IsID("welder") && target.IsID("metal_ingot") && !source.forceDisableUse
                && GeneralHelper.IsItemEffectivelyOwned(source) && GeneralHelper.IsItemEffectivelyOwned(target) && target.parentInventory != null;
        }

        internal static void Cut(GameItem welder, GameItem ingot)
        {
            GameInventory parent = ingot.parentInventory;
            ingot.Destroy();
            for (int i = 0; i < ScrapPerIngot; i++)
                GraphUtils.EmporiumTryAdd(parent, DirectoryMaster.Item("scrap_metal", true));
            if (AudioManager.Instance != null && !string.IsNullOrEmpty(welder.soundUse))
                AudioManager.Instance.TryPlayOne(new[] { welder.soundUse });
            if (OverlayHandler.current != null)
                OverlayHandler.current.UpdateInventoryStateUI();
            GunsmithMod.Log("metal ingot cut into " + ScrapPerIngot + " scrap metal");
        }

        internal static string UseLine(GameItem ingot)
        {
            return string.Format(Text.Pick("<b>切割</b>{0}，得到 {1} 份{2}", "<b>Cut</b> {0} into {1} {2}"), "<u>" + ingot.GetDisplayName(true) + "</u>",
                ScrapPerIngot, DirectoryMaster.Item("scrap_metal", false).name);
        }
    }

    // The game puts a seller's wares on the counter in the end action of the last line of the greeting that has one;
    // an action combined after it adds more wares, one combined before it puts its wares out first. The front counter
    // takes what fits, each item in the first place it finds; the game destroys the rest (PlayerStore.TryAddDirectSellingItemToTable,
    // GraphUtils.TryAcceptAllOrDestroy, logged by GraphUtils.ReportUnplaced), as it did before game 1.0.0.12 too.
    // This mod's own wares go out through Offer: what the front counter cannot take goes on the counter top (VVittgenstein 2026-11-07
    // 22:35, current/T-006 quote 24 point 2; 23:57, quote 25: this mod's goods only,
    // and with no room there either as the game does). The counter top is where the player bargains
    // (PlayerStore.AddedItemToWeightedArea): a seller's item there is in the bargaining at once, counts in the wholesale, and
    // goes with the seller like the wares on the front counter (PlayerStore.RemoveNotOwnedItemFromTable). The game's own wares
    // are left as they are, and so are the guns that GunSellers.Instead puts in place of the game's guns inside the game's
    // own placing (GunSellerStockPatch).
    internal static class Sellers
    {
        // An item this mod offers for the current seller: the front counter when it fits there, else the counter top when it
        // fits there and a client is at the counter, else the game's own way, which destroys it. true when it is on one of
        // the two counters. The counter top needs the client: the client's prices and features go on what is put there
        // (GameItem.OnAddedToWeightedArea skips them with no client), and Dr. Jackson's stock at his own place goes out
        // before he is the client (DrJacksonLocation.Arrive: PlaceInventorInventory, then ActivateClientOutsideStore).
        internal static bool Offer(GameItem item, bool stolen = false, int forcedHeat = 0)
        {
            PlayerStore store = PlayerStore.Instance;
            if (item == null || store == null)
                return false;
            EmporiumEntry entry = EmporiumEntry.Instance;
            if (store.currentClientInstance != null && entry != null && entry.frontInvinvElement != null && entry.backInvinvElement != null
                && !GraphUtils.CanAccept(entry.frontInvinvElement, item) && GraphUtils.CanAccept(entry.backInvinvElement, item))
                return OnCounterTop(store, entry.backInvinvElement, item, stolen, forcedHeat);
            return store.TryAddDirectSellingItemToTable(item, false, stolen, false, forcedHeat);
        }

        // The game's string overload makes the item only when the front counter has a free cell; this makes it as that
        // overload does (DirectoryMaster.Item with true) and offers it as above.
        internal static bool Offer(string id, bool stolen = false)
        {
            if (PlayerStore.Instance == null || string.IsNullOrEmpty(id))
                return false;
            return Offer(DirectoryMaster.Item(id, true), stolen, 0);
        }

        // The steps of PlayerStore.TryAddDirectSellingItemToTable(GameItem) in game 1.0.0.12 with the counter top as the
        // place: contraband withheld from a distrusted faction, the item set not owned, the stolen mark, the gambling
        // addiction perk. The module count of the retired gunsmith (ModulePity) hears of the placing here, since
        // CounterPlacePatch sees only the game's method.
        private static bool OnCounterTop(PlayerStore store, GameGridInventory top, GameItem item, bool stolen, int forcedHeat)
        {
            StoreClient client = store.currentClientInstance != null ? store.currentClientInstance.GetClientBlueprint() : null;
            if (client != null && ContrabandHelper.IsContraband(item)
                && ((client.clientFaction == "FACTION_LOWER_LEVEL" && StoreReputation.IsPerkUnlocked("LLDISTRUSTED"))
                    || (client.clientFaction == "FACTION_BLACK_MARKET" && StoreReputation.IsPerkUnlocked("BM_DISTRUSTED"))))
            {
                GraphUtils.DestroyIfUnplaced(item, "distrusted faction, contraband offer withheld");
                return false;
            }
            GeneralHelper.SetItemOwned(item, false);
            if (stolen && store.isStolenItemIntroduced)
                StolenHelper.InitStolenItem(item, forcedHeat);
            string id = item.identifier;
            GraphUtils.TryAcceptAll(top, item, -1);
            if (!GraphUtils.DestroyIfUnplaced(item, "counter top full"))
                return false;
            StartingPerkContent.HandleGamblingAddictionItemAdded(item);
            ModulePity.OnPlaced(item);
            GunsmithMod.Log(id + " on the counter top, the front counter has no room for it");
            return true;
        }

        private static Dialogue StockLine(StoreClient client)
        {
            if (client == null)
                return null;
            Dialogue last = null;
            int steps = 0;
            for (Dialogue d = client.mainDialogue; d != null && steps < 20; d = d.nextDialogue, steps++)
            {
                if (d.endAction != null)
                    last = d;
            }
            return last;
        }

        internal static bool AfterStock(StoreClient client, Action action)
        {
            Dialogue last = StockLine(client);
            if (last == null)
                return false;
            last.endAction = (Action)Delegate.Combine(last.endAction, action);
            return true;
        }

        internal static bool BeforeStock(StoreClient client, Action action)
        {
            Dialogue last = StockLine(client);
            if (last == null)
                return false;
            last.endAction = (Action)Delegate.Combine(action, last.endAction);
            return true;
        }

        // `after` also runs when the stock fails.
        internal static bool AroundStock(StoreClient client, Action before, Action after)
        {
            Dialogue last = StockLine(client);
            if (last == null)
                return false;
            Action stock = last.endAction;
            last.endAction = () =>
            {
                before();
                try
                {
                    stock();
                }
                finally
                {
                    after();
                }
            };
            return true;
        }
    }

    // New sources of guns and gun parts (final-strategy 34). The security surplus merchant, who in the game sells guns
    // by the weapon permit held, also sells this mod's tier one to three guns that the permit covers, up to a tier set
    // by the security reputation: tier one below 30, tier two from 30, tier three from 60. Thieves, and the security
    // and evidence crates the game fills with weapons, bring guns the permit does not cover and the special tier.
    // Scavengers also give gun parts; at the dumping grounds there are only those of the game's own table (until 0.6.2 this mod added 5% of the scavenges;
    // VVittgenstein 2026-11-06 04:01, current/008 quote 24, item 80). Each gun is picked by tier first, every allowed tier
    // alike, then by gun within the tier. The chances and reputation steps are the Agent's decision.
    internal static class GunSupply
    {
        internal const int PoliceFirstPercent = 50;
        internal const int PoliceSecondPercent = 30;
        internal const int ThiefPercent = 15;
        internal const int SecurityCratePercent = 20;
        internal const int EvidenceCratePercent = 10;
        internal const int ScavengerPartsPercent = 25;
        private const int CrateTries = 6;
        private static readonly string[] TierNames = { "一档", "二档", "三档" };
        private static readonly Random rng = new Random();

        internal static int PermitTop()
        {
            for (int level = 3; level >= 1; level--)
            {
                if (PermitHelper.HavePermit("FIREARM", level))
                    return level;
            }
            return 0;
        }

        internal static List<string> PoliceTiers(int permitTop, int securityReputation)
        {
            int top = Math.Min(permitTop, securityReputation >= 60 ? 3 : securityReputation >= 30 ? 2 : 1);
            var tiers = new List<string>();
            for (int t = 1; t <= top; t++)
                tiers.Add(TierNames[t - 1]);
            return tiers;
        }

        internal static List<string> ContrabandTiers(int permitTop)
        {
            var tiers = new List<string>();
            for (int t = permitTop + 1; t <= 3; t++)
                tiers.Add(TierNames[t - 1]);
            tiers.Add("特殊");
            return tiers;
        }

        internal static GunDef Pick(List<string> tiers)
        {
            if (tiers.Count == 0)
                return null;
            string tier = tiers[rng.Next(tiers.Count)];
            var guns = new List<GunDef>();
            foreach (GunDef d in AddOn2077.ForSale())
            {
                if (d.Tier == tier)
                    guns.Add(d);
            }
            return guns.Count > 0 ? guns[rng.Next(guns.Count)] : null;
        }

        internal static void AddPoliceStock(StoreClient client)
        {
            Sellers.AfterStock(client, () =>
            {
                if (PlayerStore.Instance == null)
                    return;
                List<string> tiers = PoliceTiers(PermitTop(), StoreReputation.GetSecReputation());
                foreach (int chance in new[] { PoliceFirstPercent, PoliceSecondPercent })
                {
                    GunDef def = rng.Next(100) < chance ? Pick(tiers) : null;
                    GameItem gun = def != null ? DirectoryMaster.Item(def.Id, true) : null;
                    if (gun == null)
                        continue;
                    Sellers.Offer(gun);
                    GunsmithMod.Log("security surplus merchant brings " + def.Id + " (" + def.Tier + ")");
                }
            });
        }

        // The game scales the thieves' other wares by the black market reputation in the same way. The gun comes in a carton
        // (DeliveryBoxes.OfferBoxed; VVittgenstein 2026-11-07 22:35 to 2026-11-08 00:19, current/T-006 quotes 24 to 26).
        internal static void AddThiefGun(StoreClient client)
        {
            // Factions: more during a gang war, none during a crackdown
            if (client == null || !RNG.Roll(ThiefPercent * StoreClientList.GetBMRepStockMult() * Factions.ThiefGunFactor()))
                return;
            GunDef def = Pick(ContrabandTiers(PermitTop()));
            if (def == null)
                return;
            string who = client.identifier;
            Sellers.AfterStock(client, () =>
            {
                GameItem gun = DirectoryMaster.Item(def.Id, true);
                if (gun == null || PlayerStore.Instance == null)
                    return;
                DeliveryBoxes.OfferBoxed(gun, true);
                GunsmithMod.Log(who + " brings " + def.Id + " (" + def.Tier + ")");
            });
        }

        // Called before the game's filler with the same luck multiplier, so the gun goes into the empty crate and the
        // game's items fill the space left. Only guns no larger than the crate's grid are tried, picked by tier among
        // the tiers that have one.
        internal static void FillCrate(GameItem crate, float luckMult, int chance, string kind)
        {
            if (crate == null || !RNG.Roll(chance * luckMult))
                return;
            GridShape grid = CrateGrid(crate);
            if (grid == null)
                return;
            var byTier = new Dictionary<string, List<GunDef>>();
            List<string> tiers = ContrabandTiers(PermitTop());
            foreach (GunDef d in AddOn2077.ForSale())
            {
                if (!tiers.Contains(d.Tier) || !Fits(d.Size, grid.width, grid.height))
                    continue;
                if (!byTier.ContainsKey(d.Tier))
                    byTier[d.Tier] = new List<GunDef>();
                byTier[d.Tier].Add(d);
            }
            if (byTier.Count == 0)
                return;
            var withGuns = new List<string>(byTier.Keys);
            for (int i = 0; i < CrateTries; i++)
            {
                List<GunDef> guns = byTier[withGuns[rng.Next(withGuns.Count)]];
                GunDef def = guns[rng.Next(guns.Count)];
                GameItem gun = DirectoryMaster.Item(def.Id, true);
                if (gun == null)
                    return;
                if (GraphUtils.TryAcceptAll(crate, gun, -1) == 0)
                {
                    GunsmithMod.Log(kind + " crate holds " + def.Id + " (" + def.Tier + ")");
                    return;
                }
                gun.Destroy();
            }
        }

        internal static GridShape CrateGrid(GameItem crate)
        {
            GameGridInventory inv = crate.contentWindow != null ? crate.contentWindow.child as GameGridInventory : null;
            return inv != null ? inv.inventoryShape : null;
        }

        // GunDef.Size is "<columns>x<rows>".
        internal static bool Fits(string size, int columns, int rows)
        {
            string[] parts = (size ?? "").Split('x');
            int w, h;
            return parts.Length == 2 && int.TryParse(parts[0], out w) && int.TryParse(parts[1], out h) && w <= columns && h <= rows;
        }

        internal static void AddScavengerParts(StoreClient client)
        {
            if (client == null || rng.Next(100) >= ScavengerPartsPercent)
                return;
            int count = 1 + rng.Next(3);
            string who = client.identifier;
            Sellers.AfterStock(client, () =>
            {
                if (PlayerStore.Instance == null)
                    return;
                for (int i = 0; i < count; i++)
                    Sellers.Offer("gun_part");
                GunsmithMod.Log(who + " brings " + count + " gun parts");
            });
        }
    }

    [HarmonyPatch(typeof(StoreClientList), nameof(StoreClientList.CreateSecSurplusMerchant))]
    internal static class SecuritySurplusPatch
    {
        private static void Postfix(StoreClient __result)
        {
            GunSupply.AddPoliceStock(__result);
        }
    }

    [HarmonyPatch(typeof(StoreClientList), nameof(StoreClientList.CreateThief))]
    internal static class ThiefGunPatch
    {
        private static void Postfix(StoreClient __result)
        {
            GunSupply.AddThiefGun(__result);
        }
    }

    [HarmonyPatch(typeof(StoreClientList), nameof(StoreClientList.CreatePettyThief))]
    internal static class PettyThiefGunPatch
    {
        private static void Postfix(StoreClient __result)
        {
            GunSupply.AddThiefGun(__result);
        }
    }

    [HarmonyPatch(typeof(StoreClientList), nameof(StoreClientList.CreateScavGeneral))]
    internal static class ScavengerPartsPatch
    {
        private static void Postfix(StoreClient __result)
        {
            GunSupply.AddScavengerParts(__result);
        }
    }

    // Before the game's filler: after it, the crate's 7x5 grid is too full for most guns (6 of 300 security crates
    // held one in the test of 2026-11-01 23:57, against 21% rolled).
    [HarmonyPatch(typeof(PreBuiltItemHelper), "FillSecurity")]
    internal static class SecurityCratePatch
    {
        private static void Prefix(GameItem gameItem, float luckMult)
        {
            GunSupply.FillCrate(gameItem, luckMult, GunSupply.SecurityCratePercent, "security");
            Ammo.FillCrate(gameItem, false);
        }
    }

    [HarmonyPatch(typeof(PreBuiltItemHelper), "FillEvidence")]
    internal static class EvidenceCratePatch
    {
        private static void Prefix(GameItem gameItem, float luckMult)
        {
            GunSupply.FillCrate(gameItem, luckMult, GunSupply.EvidenceCratePercent, "evidence");
            Ammo.FillCrate(gameItem, true);
        }
    }

    // Repair goods in the retired gunsmith's stock (VVittgenstein 2026-11-04, current/006 quote 35 point 3). The game sells the wire cutter and the plasma cutter nowhere for certain: they come from
    // scavenging, crates and a starting perk (read 2026-11-04). On every visit:
    //   - each of these two tools the store does not own goes on the counter, as Dr. Jackson's magnifier
    //     (StoreClientList.AddIfNotOwn). The game's stock always has a screwdriver and Dr. Jackson has the magnifier if
    //     the store lacks it (3 x 3 cells) and the X-ray scanner (final-strategy 89);
    //   - two different repair materials, one of each, picked at random from those the repairs use; the game's stock
    //     always has four of the 3D printing filament.
    // They go out before the game's stock. The counter has 72 cells (18 x 4) and the rest of the stock takes 51 on average
    // (40 to 62, 120 visits of testing/harness/GseCounterHarness.cs, 2026-11-04), and what is left at the end is mostly the
    // last row, where nothing two rows high fits: put out last, as first written, two thirds of the materials were left off.
    // Put out first, the tools and materials are always there; the game's goods then lost 23 items in 120 visits while the
    // store lacked both tools and 2 items in 120 visits with both owned (with two of each material, 86 items with both owned).
    // Agent's decisions: the lists, two kinds a visit, one of each, going first; the prices are the game's for the retired gunsmith.
    internal static class RepairGoods
    {
        internal static readonly string[] Tools = { "wire_cutter", "welder" };
        internal static readonly string[] Materials =
        {
            "nuts_metal", "wire", "common_electronic", "scrap_metal", "gun_part", "rubbing_alcohol", "kotton_fabric",
        };
        internal const int MaterialKinds = 2;
        internal const int EachMaterial = 1;
        private static readonly Random rng = new Random();

        // The tools and the materials of the last visit, as logged.
        internal static List<string> LastTools = new List<string>();
        internal static List<string> LastMaterials = new List<string>();

        internal static void Stock(StoreClient client)
        {
            Sellers.BeforeStock(client, () =>
            {
                if (PlayerStore.Instance == null || EmporiumEntry.Instance == null)
                    return;
                var owned = new HashSet<string>();
                foreach (GameItem item in EmporiumEntry.Instance.GetAllOwnedItems(false, true))
                {
                    if (item != null && item.identifier != null)
                        owned.Add(item.identifier);
                }
                var tools = new List<string>();
                foreach (string tool in Tools)
                {
                    if (owned.Contains(tool))
                        continue;
                    Sellers.Offer(tool);
                    tools.Add(tool);
                }
                var materials = new List<string>();
                var pool = new List<string>(Materials);
                for (int k = 0; k < MaterialKinds && pool.Count > 0; k++)
                {
                    int i = rng.Next(pool.Count);
                    for (int n = 0; n < EachMaterial; n++)
                        Sellers.Offer(pool[i]);
                    materials.Add(pool[i]);
                    pool.RemoveAt(i);
                }
                LastTools = tools;
                LastMaterials = materials;
                GunsmithMod.Log("retired gunsmith repair goods: tools [" + string.Join(", ", tools.ToArray()) + "], materials ["
                    + string.Join(", ", materials.ToArray()) + "] x" + EachMaterial);
            });
        }
    }

    [HarmonyPatch(typeof(StoreClientListSpec), nameof(StoreClientListSpec.CreateRetiredGunsmith))]
    internal static class RetiredGunsmithPatch
    {
        private static void Postfix(StoreClient __result)
        {
            ModulePity.Attach(__result);
            Production.AddBlankChips(__result);
            Reloader.StockRetiredGunsmith(__result);
            RepairGoods.Stock(__result);
            Cabinets.StockLarge(__result);
            Phone.AttachIntro(__result);
        }
    }

    [HarmonyPatch(typeof(PlayerStore), "TryAddDirectSellingItemToTable", new[] { typeof(GameItem), typeof(bool), typeof(bool), typeof(bool), typeof(int) })]
    internal static class CounterPlacePatch
    {
        private static void Postfix(GameItem gameItem, bool __result)
        {
            if (__result)
                ModulePity.OnPlaced(gameItem);
        }
    }
}
