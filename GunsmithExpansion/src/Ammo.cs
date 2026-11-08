using System;
using System.Collections.Generic;
using HarmonyLib;

namespace GunsmithExpansion
{
    // Ammunition of the calibre families this mod's guns need (final-strategy 29, 31, 36). The game has .22 rounds
    // (small_pistol_ammo) and 10 mm rounds (heavy_pistol_ammo); this mod adds 12 gauge shells, intermediate rifle
    // rounds (5.56), full-power rifle rounds (7.62), heavy rounds (.50 class), 40 mm grenades and .45 rounds (VVittgenstein
    // 2026-11-05 13:59, current/008 quote 4 point 25: the game's revolver and nine of this mod's guns take .45 and neither
    // the game nor the mod had the rounds; picture current/008 unit U151). Like the game's they
    // are AMMUNITION items with the gun calibre tag, so the game's buyers who ask for ammunition for the gun they have
    // just bought take them (StoreClientListGun.CreateDeliveryFollowUp, GunHelper.IsAmmunitionOfCaliber). An item is
    // registered only when its picture is in the mod (Textures/Items/<picture>.png, current/005 unit U43). The game's
    // lower level gunsmith sells the families the store's firearm permit covers next to his pistol rounds; thieves
    // bring heavy rounds and grenades; security and evidence crates hold some. From the second permit on the gunsmith sign's
    // customers may come for a batch of one family (ammunition orders; until 0.6.2, 10% of the game's lower level and mercenary gun buyers). The reloader of final-strategy 36 is in
    // src/Reloader.cs (0.5.8).
    // Values and contraband levels follow the game's rounds (VVittgenstein 2026-11-02, current/005 quote 50; tools/gen_catalog.py
    // writes them to Catalog.Ammo): the value grows with the mean firepower of the guns of the calibre along the line
    // through the game's .22 and 10 mm rounds and by 6 for each contraband level above 1, as the game's rounds do
    // (VVittgenstein 2026-11-02, quote 55: the rarer and the more forbidden, the dearer); 40 mm grenades start from the game's
    // flashbang; the level is one below the lowest level of the guns of the calibre, at least 1. Heavy rounds and 40 mm grenades are black market
    // only (the design draft lists them as ordered or black market only): the lower level gunsmith does not sell them.
    // Agent's decisions: who sells how many, the size of the ammunition orders.
    // What a buyer asks for (item 90; VVittgenstein 2026-11-06 16:34, current/008 quote 51; Steam comment of 玄九丸). The game's buyer who asks for rounds after his gun does not look at what the store holds (StoreClientListGun.CreateDeliveryFollowUp, game 1.0.0.12), so a buyer asks only for a
    // family of the player's level that can be got (VVittgenstein 2026-11-07 17:04, current/T-006 quote 11; Asked): the family's contraband level is not above the
    // store's highest weapon permit (VVittgenstein 2026-11-07 17:26, quote 14), and the mod or the game gives a way to get it (VVittgenstein 2026-11-07 17:10, quote 12), which every family
    // registered here has: the game's lower level gunsmith, thieves, crates and the reloader's dies above. The same holds for this mod's families when the game's buyer asks after his gun
    // (AmmoFollowUpPatch); the game's own rounds are left as they are.
    internal static class Ammo
    {
        internal const int VendorPercent = 50;
        internal const int ThiefPercent = 20;
        internal const int CratePercent = 15;
        private static readonly Random rng = new Random();
        private static readonly HashSet<string> registered = new HashSet<string>();

        internal sealed class Family
        {
            internal string Id;
            internal string Picture;
            internal int Caliber;
            internal string NameZh;
            internal string NameEn;
            internal bool BlackMarket;

            internal int Value
            {
                get { return Catalog.Ammo[Caliber][0]; }
            }

            internal int Contraband
            {
                get { return Catalog.Ammo[Caliber][1]; }
            }
        }

        internal static readonly Family[] Families =
        {
            new Family { Id = "gse_ammo_12", Picture = "ammo_12", Caliber = 12, NameZh = "12 号霰弹", NameEn = "12-gauge shells" },
            new Family { Id = "gse_ammo_556", Picture = "ammo_556", Caliber = 556, NameZh = "中间威力步枪弹", NameEn = "Intermediate rifle rounds" },
            new Family { Id = "gse_ammo_762", Picture = "ammo_762", Caliber = 762, NameZh = "全威力步枪弹", NameEn = "Full-power rifle rounds" },
            new Family { Id = "gse_ammo_50", Picture = "ammo_50", Caliber = 50, NameZh = "重型弹", NameEn = "Heavy rounds", BlackMarket = true },
            new Family { Id = "gse_ammo_40", Picture = "ammo_40", Caliber = 40, NameZh = "40 毫米榴弹", NameEn = "40 mm grenades", BlackMarket = true },
            new Family { Id = "gse_ammo_45", Picture = "ammo_45", Caliber = 45, NameZh = ".45 手枪弹", NameEn = ".45 rounds" },
        };

        internal static void Register(ModItemDirectory directory)
        {
            foreach (Family family in Families)
            {
                Family f = family;
                if (!ModHelper.ModFileExists(GunsmithMod.ModId, "Textures/Items/" + f.Picture + ".png"))
                    continue;
                directory.Add(f.Id, () => Create(directory, f));
                registered.Add(f.Id);
            }
            GunsmithMod.Log("registered " + registered.Count + " ammunition families");
        }

        internal static bool IsRegistered(Family f)
        {
            return registered.Contains(f.Id);
        }

        // Built like the game's GunsItemDirectory.SmallPistolRound, ammunition type tag included (the game's
        // AmmoBoxHelper.Package reads it from the rounds it boxes).
        private static GameItem Create(ModItemDirectory directory, Family f)
        {
            GameItem item = directory.Empty().SetSpriteAndShapeFromMod(GunsmithMod.ModId, f.Picture);
            item.name = Text.Pick(f.NameZh, f.NameEn);
            // the game's own calibre names, which for 12 gauge already ends in 口径
            item.shortDescription = string.Format(Text.Pick("适用口径：{0}。", "Caliber: {0}."), GunHelper.GetCaliberName(f.Caliber));
            item.SetValue(f.Value);
            ContrabandHelper.InitContrabandItem(item, f.Contraband);
            item.SetGameItemType("AMMUNITION");
            int caliber = f.Caliber;
            item.ModifyTag("GUN_CALIBER_TAG", state => state.Enable().SetInt(caliber), false);
            item.ModifyTag("AMMO_TYPE_TAG", state => state.Enable().SetString("NORMAL_AMMO"), false);
            return item;
        }

        internal static int PermitTop()
        {
            for (int level = 3; level >= 1; level--)
            {
                if (PermitHelper.HavePermit("FIREARM", level))
                    return level;
            }
            return 0;
        }

        private static List<Family> Available(Func<Family, bool> which)
        {
            var list = new List<Family>();
            foreach (Family f in Families)
            {
                if (IsRegistered(f) && which(f))
                    list.Add(f);
            }
            return list;
        }

        private static void Put(Family f, int count)
        {
            for (int i = 0; i < count; i++)
                Sellers.Offer(f.Id);
        }

        // The game's lower level gunsmith: the families the permit covers.
        internal static void StockVendor(StoreClient client)
        {
            int top = Math.Max(1, PermitTop());
            List<Family> legal = Available(f => !f.BlackMarket && f.Contraband <= top);
            if (legal.Count == 0 || rng.Next(100) >= VendorPercent)
                return;
            Family pick = legal[rng.Next(legal.Count)];
            int count = 2 + rng.Next(3);
            Sellers.AfterStock(client, () =>
            {
                if (PlayerStore.Instance != null)
                    Put(pick, count);
            });
        }

        // Thieves: heavy rounds and grenades, stolen.
        internal static void StockThief(StoreClient client)
        {
            List<Family> heavy = Available(f => f.BlackMarket);
            if (client == null || heavy.Count == 0 || !RNG.Roll(ThiefPercent * Factions.ThiefGunFactor()))
                return;
            Family pick = heavy[rng.Next(heavy.Count)];
            int count = 1 + rng.Next(3);
            Sellers.AfterStock(client, () =>
            {
                if (PlayerStore.Instance == null)
                    return;
                for (int i = 0; i < count; i++)
                    Sellers.Offer(pick.Id, true);
            });
        }

        // Crates: security crates hold legal long gun ammunition, evidence crates the rest.
        internal static void FillCrate(GameItem crate, bool evidence)
        {
            List<Family> fit = Available(f => evidence ? f.Contraband >= 3 : f.Contraband <= 2);
            if (crate == null || fit.Count == 0 || rng.Next(100) >= CratePercent)
                return;
            Family pick = fit[rng.Next(fit.Count)];
            for (int i = 0; i < 2; i++)
            {
                GameItem round = DirectoryMaster.Item(pick.Id, true);
                if (round != null && GraphUtils.TryAcceptAll(crate, round, -1) != 0)
                    round.Destroy();
            }
        }

        // The families a buyer asks for (item 90, at the head of this class): of the registered families, those whose contraband level is not above the store's highest weapon permit.
        internal static List<Family> Asked()
        {
            int top = PermitTop();
            return Available(f => f.Contraband <= top);
        }

        // A task of the gunsmith sign's customers (GunsmithSign.CreateCustomer), from the second firearm permit on: a batch of one family, as a lower
        // level buyer or a mercenary.
        internal static bool CanSellInBulk()
        {
            return PermitTop() >= 2 && Asked().Count > 0;
        }

        private static List<Family> FitFor(string kind, List<Family> families)
        {
            return families.FindAll(f => kind == "LL" ? f.Caliber == 12 || f.Caliber == 556 : f.Caliber != 12);
        }

        internal static StoreClient CreateSignCustomer()
        {
            List<Family> can = Asked();
            string kind = GunsmithSign.PickKind(2);
            List<Family> fit = FitFor(kind, can);
            // A family one kind does not ask for: the other kind comes.
            if (fit.Count == 0)
            {
                kind = kind == "LL" ? "Merc" : "LL";
                fit = FitFor(kind, can);
            }
            if (fit.Count == 0)
                return null;
            return CreateBuyer(GunsmithSign.Look(kind), fit[rng.Next(fit.Count)], 6 + rng.Next(7));
        }

        // Like the game's buyer who asks for ammunition after a gun (StoreClientListGun.ConvertToBuyer with
        // GetAmmoBudget) the line gives the budget only: the store's price adds the game's retail and controlled goods
        // markups at the counter (GameItem.IsApplyMarkUp), so the number of rounds the budget covers is known there.
        internal static StoreClient CreateBuyer(StoreClient look, Family f, int count)
        {
            var client = new StoreClient();
            client.identifier = "gseAmmoBuyer";
            client.displayName = look.displayName;
            client.spriteName = look.spriteName;
            client.clientFaction = look.clientFaction;
            client.clientIntent = StoreClient.ClientIntent.BUY;
            client.SetBudget(f.Value * count * 120 / 100);
            client.noAcceptingContraband = false;
            client.clientBuyingTagList.Clear();
            int caliber = f.Caliber;
            client.willAcceptItem = item => GunHelper.IsAmmunitionOfCaliber(item, caliber);
            client.mainDialogue.SetText(client.displayName, string.Format(Text.Pick("我想成批买些{0}，预算 {1} 信用点。", "I need {0} in bulk. My budget is {1} credits."),
                Text.Pick(f.NameZh, f.NameEn), client.GetBudget()));
            client.AddBasicDialog();
            client.CompleteClientCreation(false);
            GunsmithMod.Log("ammunition buyer for " + f.Id + " x " + count);
            return client;
        }
    }

    // The game's buyer who, having got his gun, asks for rounds for it (StoreClientListGun.CreateDeliveryFollowUp): he asks only when the gun's calibre tag holds a number. For a family of this
    // mod above the store's level (item 90, Ammo.Asked) the number is 0 while the game decides, and he may still ask for the game's other things.
    [HarmonyPatch(typeof(StoreClientListGun), nameof(StoreClientListGun.CreateDeliveryFollowUp))]
    internal static class AmmoFollowUpPatch
    {
        private static void Prefix(GameItem gun, out int __state)
        {
            __state = 0;
            if (gun == null || !gun.IsTag("GUN_CALIBER_TAG"))
                return;
            int caliber = gun.GetTagReadonly("GUN_CALIBER_TAG").GetInt();
            Ammo.Family family = Array.Find(Ammo.Families, f => f.Caliber == caliber);
            if (family == null || !Ammo.IsRegistered(family) || Ammo.Asked().Contains(family))
                return;
            __state = caliber;
            gun.ModifyTag("GUN_CALIBER_TAG", state => state.SetInt(0), false);
        }

        private static void Finalizer(GameItem gun, int __state)
        {
            if (__state != 0)
                gun.ModifyTag("GUN_CALIBER_TAG", state => state.SetInt(__state), false);
        }
    }

    [HarmonyPatch(typeof(StoreClientList), nameof(StoreClientList.CreateGunsmith))]
    internal static class AmmoVendorPatch
    {
        private static void Postfix(StoreClient __result)
        {
            Ammo.StockVendor(__result);
        }
    }

    [HarmonyPatch(typeof(StoreClientList), nameof(StoreClientList.CreateThief))]
    internal static class AmmoThiefPatch
    {
        private static void Postfix(StoreClient __result)
        {
            Ammo.StockThief(__result);
        }
    }
}
