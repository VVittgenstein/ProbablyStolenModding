using System;
using System.Collections.Generic;

namespace Moneylending
{
    // One kind of borrower. Names, sprites and faction setup are the ones the game uses for the matching
    // customer (StoreClientList / StoreClientUniqueList factories); the loan terms are this mod's.
    internal sealed class Profile
    {
        internal string Id;
        internal string Faction;
        internal Action<StoreClient> Setup;
        internal string NameKey;
        internal Func<string> Sprite;
        internal int VisitWeight;

        internal int MinAmount;
        internal int MaxAmount;
        internal int AmountPerTenDays;
        internal int MinTerm;
        internal int MaxTerm;              // 0: until the next Sunday (payday and rent day)
        internal int MinRate;              // percent; MinRate and MaxRate are the defaults of the player's settings (Settings.RateRange)
        internal int MaxRate;
        internal int FreeChance;           // percent of requests that offer no interest at all

        internal int DefaultBase;          // percent
        internal int ArrestChance;         // percent, rolled before the default
        internal int RaiseStep;            // points added by "ask for more interest"
        internal int RaiseBase;            // percent chance that the borrower accepts it, before reputation
        internal bool Gentle;              // the middle choice lends without interest when interest was asked
        internal int RepOnLend;
        internal int RepOnGentle;
        internal int RepOnRaise;
        internal int RepOnRepay;

        // The player can lend to Lower Level people and revolutionaries without interest; the reputation for it goes to
        // their faction (Lending.CreditWaived). Upper Level citizens, thieves and tourists do not have this choice.
        internal bool CanWaive
        {
            get { return Faction == Profiles.LowerLevel || Faction == Profiles.Revolution; }
        }

        internal int CollateralChance;     // percent; the merchant's third request always brings one
        internal string[] CollateralItems;
        internal string[] GoodsItems;
        internal bool GoodsStolen;

        // Weights of the ways a borrower who does not default repays.
        internal int OnTime;
        internal int Partial;
        internal int Late;
        internal int Goods;
        internal int Bonus;

        internal bool SawyerCan;           // false when nobody is left to collect from
        internal int SawyerRepCost;        // reputation lost with the borrower's faction when the Crew collects
        internal int OfferAccept;          // percent, when the player offers a loan at the counter

        internal Lines Lines
        {
            get { return Text.Borrowers[Id]; }
        }

        internal string DisplayName()
        {
            return LocHelper.GetLocalizedClientName(NameKey);
        }

        // This kind's part of the keys in the configuration file (Settings): the game's name key without "name_", which gives
        // scavenger, lower_level_parent, miner, merchant, upper_level_citizen, thief, revolutionary_fighter and tourist.
        internal string ConfigName
        {
            get { return NameKey != null && NameKey.StartsWith("name_", StringComparison.Ordinal) ? NameKey.Substring("name_".Length) : Id; }
        }
    }

    internal static class Profiles
    {
        internal const string Scav = "scav";
        internal const string Caretaker = "caretaker";
        internal const string Worker = "worker";
        internal const string Merchant = "merchant";
        internal const string UpperBroke = "ulBroke";
        internal const string Thief = "thief";
        internal const string Rev = "rev";
        internal const string Tourist = "tourist";

        internal const string LowerLevel = StoreReputation.FACTION_LOWER_LEVEL;
        internal const string UpperLevel = StoreReputation.FACTION_UPPER_LEVEL;
        internal const string BlackMarket = StoreReputation.FACTION_BLACK_MARKET;
        internal const string Revolution = StoreReputation.FACTION_REVOLUTION;
        internal const string TouristFaction = StoreReputation.FACTION_TOURIST;

        internal static readonly List<Profile> All = new List<Profile>
        {
            new Profile
            {
                Id = Scav, Faction = LowerLevel, Setup = StoreClientFactionSetup.InitScav,
                NameKey = "name_scavenger", Sprite = SpriteDict.GetRandomScavSpriteName, VisitWeight = 25,
                MinAmount = 60, MaxAmount = 150, AmountPerTenDays = 10, MinTerm = 3, MaxTerm = 5, MinRate = 30, MaxRate = 40,
                DefaultBase = 15, RaiseStep = 15, RaiseBase = 70, RepOnRepay = 1,
                GoodsItems = Items.ScavHaul,
                OnTime = 45, Late = 25, Goods = 30,
                SawyerCan = true, SawyerRepCost = 3, OfferAccept = 60,
            },
            new Profile
            {
                Id = Caretaker, Faction = LowerLevel, Setup = StoreClientFactionSetup.InitLowerLevelCitizen,
                NameKey = "name_lower_level_parent", Sprite = SpriteDict.GetRandomScavSpriteName, VisitWeight = 15,
                MinAmount = 150, MaxAmount = 350, AmountPerTenDays = 10, MinTerm = 6, MaxTerm = 8, MinRate = 15, MaxRate = 25,
                DefaultBase = 10, RaiseStep = 15, RaiseBase = 75, Gentle = true, RepOnGentle = 2, RepOnRaise = -2, RepOnRepay = 1,
                OnTime = 40, Partial = 25, Late = 35,
                SawyerCan = true, SawyerRepCost = 5, OfferAccept = 55,
            },
            new Profile
            {
                Id = Worker, Faction = LowerLevel, Setup = StoreClientFactionSetup.InitWorker,
                NameKey = "name_miner", Sprite = SpriteDict.GetRandomMinerSpriteName, VisitWeight = 20,
                MinAmount = 150, MaxAmount = 350, AmountPerTenDays = 20, MinTerm = 0, MaxTerm = 0, MinRate = 20, MaxRate = 25,
                DefaultBase = 5, RaiseStep = 10, RaiseBase = 55, RepOnRepay = 1,
                OnTime = 80, Partial = 10, Late = 10,
                SawyerCan = true, SawyerRepCost = 3, OfferAccept = 45,
            },
            new Profile
            {
                Id = Merchant, Faction = LowerLevel, Setup = null,
                NameKey = "name_merchant", Sprite = SpriteDict.GetRandomMerchantSpriteName, VisitWeight = 12,
                MinAmount = 200, MaxAmount = 400, AmountPerTenDays = 30, MinTerm = 7, MaxTerm = 10, MinRate = 20, MaxRate = 30,
                DefaultBase = 8, RaiseStep = 10, RaiseBase = 50, RepOnRepay = 1,
                CollateralItems = Items.MerchantPledge,
                OnTime = 65, Partial = 25, Late = 10,
                SawyerCan = true, SawyerRepCost = 2, OfferAccept = 40,
            },
            new Profile
            {
                Id = UpperBroke, Faction = UpperLevel, Setup = StoreClientFactionSetup.InitUpperLevelCitizen,
                NameKey = "name_upper_level_citizen", Sprite = SpriteDict.GetRandomUpperMaleSpriteName, VisitWeight = 6,
                MinAmount = 400, MaxAmount = 900, AmountPerTenDays = 50, MinTerm = 5, MaxTerm = 7, MinRate = 20, MaxRate = 40,
                DefaultBase = 35, RaiseStep = 15, RaiseBase = 75, RepOnRepay = 2,
                CollateralChance = 100, CollateralItems = Items.LuxuryPledge,
                OnTime = 40, Late = 15, Bonus = 45,
                SawyerCan = false, OfferAccept = 85,
            },
            new Profile
            {
                Id = Thief, Faction = BlackMarket, Setup = StoreClientFactionSetup.InitBlackMarket,
                NameKey = "name_thief", Sprite = SpriteDict.GetRandomShadySpriteName, VisitWeight = 10,
                MinAmount = 150, MaxAmount = 400, AmountPerTenDays = 20, MinTerm = 4, MaxTerm = 6, MinRate = 35, MaxRate = 55,
                DefaultBase = 20, ArrestChance = 10, RaiseStep = 15, RaiseBase = 60, RepOnRepay = 2,
                GoodsItems = Items.StolenGoods, GoodsStolen = true,
                OnTime = 40, Late = 10, Goods = 50,
                SawyerCan = true, SawyerRepCost = 2, OfferAccept = 50,
            },
            new Profile
            {
                Id = Rev, Faction = Revolution, Setup = StoreClientFactionSetup.InitRev,
                NameKey = "name_revolutionary_fighter", Sprite = SpriteDict.GetRandomRevSpriteName, VisitWeight = 6,
                MinAmount = 80, MaxAmount = 210, AmountPerTenDays = 20, MinTerm = 6, MaxTerm = 8, MinRate = 10, MaxRate = 15, FreeChance = 50,
                DefaultBase = 25, RaiseStep = 15, RaiseBase = 35, Gentle = true, RepOnLend = 2, RepOnGentle = 3, RepOnRaise = -2, RepOnRepay = 1,
                GoodsItems = Items.RaidedGoods,
                OnTime = 50, Late = 30, Goods = 20,
                SawyerCan = false, OfferAccept = 40,
            },
            new Profile
            {
                Id = Tourist, Faction = TouristFaction, Setup = StoreClientFactionSetup.InitTourist,
                NameKey = "name_tourist", Sprite = SpriteDict.GetRandomLowerLevelSpriteName, VisitWeight = 6,
                MinAmount = 60, MaxAmount = 180, AmountPerTenDays = 0, MinTerm = 2, MaxTerm = 3, MinRate = 50, MaxRate = 65,
                DefaultBase = 30, RaiseStep = 15, RaiseBase = 60,
                OnTime = 80, Bonus = 20,
                SawyerCan = false, OfferAccept = 35,
            },
        };

        internal static Profile Get(string id)
        {
            foreach (Profile p in All)
            {
                if (p.Id == id)
                    return p;
            }
            return null;
        }

        // Kinds of customers the game sends, matched to a borrower profile for a loan offered at the counter.
        // Named characters, officials and the mod's own visitors return null with a reply instead.
        private static readonly Dictionary<string, string> ByIdentifier = new Dictionary<string, string>
        {
            { "merchant", Merchant }, { "lower_level_rare_merchant", Merchant }, { "supplier", Merchant },
            { "miner", Worker }, { "minerMinor", Worker }, { "iceMiner", Worker }, { "maintenanceWorker", Worker },
            { "lowerRepairman", Worker }, { "foreman", Worker }, { "annoyedEngineer", Worker },
            { "sickChildCaretaker", Caretaker }, { "familyMother", Caretaker }, { "citizenHousehold", Caretaker },
            { "youngSpacerHousehold", Caretaker }, { "lowerLevelElder", Caretaker }, { "sickLowers", Caretaker },
            { "hungrySpacer", Caretaker },
            { "brokeUpperLevel", UpperBroke },
            { "shadyMerchant", Thief }, { "street_dealer", Thief },
        };

        internal static Profile ForCustomer(StoreClient client, out string reply)
        {
            reply = null;
            if (client == null)
                return null;
            if (client.isSecurity || client.clientFaction == StoreReputation.FACTION_SECURITY)
            {
                reply = Text.DeclineSecurity();
                return null;
            }
            // Named characters are told apart by nonShootable. The game 1.0.0.12 (buildid 26496118) made Eleanor, Lou and
            // Serpico shootable, clearing nonShootable; only StoreClientUniqueList.SetupEleanor,
            // LouOperativeHelper.SetupLouShootable and SerpicoStoryline.SetupShootable set holsterResumesDialogue, so
            // checking it as well gives the answers of 1.4.0 again (1.4.1).
            if (client.nonShootable || client.holsterResumesDialogue || client.clientFaction == "NONE" || client.sourceModId == MoneylendingMod.ModId)
            {
                reply = Text.DeclineOther();
                return null;
            }

            string id;
            string identifier = client.identifier ?? "";
            if (identifier.EndsWith("_disguised_aug", StringComparison.Ordinal))
                identifier = identifier.Substring(0, identifier.Length - "_disguised_aug".Length);
            if (!ByIdentifier.TryGetValue(identifier, out id))
            {
                if (client.IsTag("SCAV")) id = Scav;
                else if (client.IsTag("WORKER")) id = Worker;
                else if (client.IsTag("TOURIST")) id = Tourist;
                else if (client.IsTag("REV") || client.clientFaction == Revolution) id = Rev;
                else if (client.IsTag("BLACK_MARKET") || client.clientFaction == BlackMarket) id = Thief;
                else if (client.clientFaction == UpperLevel)
                {
                    reply = Text.DeclineRich();
                    return null;
                }
                else if (client.clientFaction == TouristFaction) id = Tourist;
                else if (client.clientFaction == LowerLevel) id = Caretaker;
                else
                {
                    reply = Text.DeclineOther();
                    return null;
                }
            }
            return Get(id);
        }
    }
}
