using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace GunsmithExpansion
{
    // Three customers of this mod (final-strategy 118, 123, 126; VVittgenstein 2026-11-04 16:08, 16:23): the gang member (submachine guns, during the black market's gang war), the elite
    // mercenary (tier three rifles, while the third firearm permit is in the display case) and the security tactical
    // officer (tier three rifles, legal only, with the third permit in the display case and security reputation 30 or
    // more). They replace the customer types that never appeared (until 0.6.1 CustomerTypes had a gang member and two
    // rifle mercenaries, which its tier filter never picked). Each wants a gun of named classes with named stats at the
    // value 90% of those classes' guns reach (CustomerTypes.Threshold, final-strategy 124) and pays the gun's market price
    // plus 25%: the game's fixed order rewards stay far below these guns' value. Portraits: Textures/Items/gse_gang_member,
    // gse_elite_merc, gse_security_tactical (current/006 section 10); until a picture is there, one of the game's.
    // From 0.7 (VVittgenstein 2026-11-05 23:38 and 2026-11-06 01:18, current/008 quotes 18 and 20, item 63: they are an identity of the
    // game's buyers, so that buyers of a low background can ask for a high tier gun, and they appear as often as the three types
    // they replaced) they have no chance of their own in the display case: CustomerTypes.TryRetheme draws them among the buyer's
    // customer types. A lower level buyer is a gang member on 19.4% of its visits (7/9 custom orders x 50% x 1 of 2), a mercenary
    // buyer an elite mercenary or an officer on 9.4% each (6/8 x 50% x 1 of 4). Until 0.6.2 they had 40, 25 and 25% a day and
    // took a place in the queue. Agent's decisions: the officer takes the share of the second rifle type; the conditions stay
    // (the gang war; the third permit in the display case; the reputation); the rifles the officer takes; the lines.
    internal static class NewCustomers
    {
        internal const int PremiumPercent = 25;
        internal const int SecurityReputationNeeded = 30;
        private static readonly System.Random rng = new System.Random();
        private static readonly ConditionalWeakTable<GunOrderData, Kind> orders = new ConditionalWeakTable<GunOrderData, Kind>();

        internal sealed class Want
        {
            internal string Tier;
            internal string[] Classes;
            internal string[] Stats;
            internal bool Legal;
            internal string Zh;
            internal string En;
        }

        internal sealed class Kind
        {
            internal string Id;
            internal string Portrait;
            internal string Faction;
            internal string Zh;
            internal string En;
            internal string StandsFor;
            internal Func<bool> Allowed;
            internal Func<string> GameSprite;
            internal Want[] Wants;
        }

        internal static readonly Kind[] Kinds =
        {
            new Kind
            {
                Id = "gseGangMember", Portrait = "gse_gang_member", Faction = "FACTION_BLACK_MARKET", Zh = "帮派成员", En = "Gang Member", StandsFor = "LL",
                Allowed = Factions.GangWar, GameSprite = SpriteDict.GetRandomBMMaleSpriteName,
                Wants = new[]
                {
                    new Want { Tier = "二档", Classes = new[] { "冲锋枪" }, Stats = new[] { "GUN_CURRENT_FIREPOWER_TAG", "GUN_CURRENT_ERGONOMICS_TAG" },
                        Zh = "帮里要一把冲锋枪。火力要猛，拿着要顺手。", En = "My crew needs a submachine gun. Plenty of firepower, and easy to handle." },
                },
            },
            new Kind
            {
                Id = "gseEliteMerc", Portrait = "gse_elite_merc", Faction = "FACTION_LOWER_LEVEL", Zh = "特级佣兵", En = "Elite Mercenary", StandsFor = "Merc",
                Allowed = ThirdPermitShown, GameSprite = SpriteDict.GetRandomMercSpriteName,
                Wants = new[]
                {
                    new Want { Tier = "三档", Classes = new[] { "精确步枪" }, Stats = new[] { "GUN_CURRENT_ACCURACY_TAG", "GUN_CURRENT_FIREPOWER_TAG" },
                        Zh = "我接了个远距离的活，要一把精确步枪。打得准，威力够。", En = "I took a long-range job. I need a precision rifle: accurate and powerful." },
                    new Want { Tier = "三档", Classes = new[] { "中间威力步枪", "全威力步枪" }, Stats = new[] { "GUN_CURRENT_RELIABILITY_TAG", "GUN_CURRENT_RECOIL_TAG" },
                        Zh = "我们佣兵队要换步枪。要耐用，后坐力好控制。", En = "My mercenary crew is changing rifles. They have to be tough, with recoil we can control." },
                },
            },
            new Kind
            {
                Id = "gseSecurityTactical", Portrait = "gse_security_tactical", Faction = "FACTION_SECURITY", Zh = "特警", En = "Security Tactical Officer", StandsFor = "Merc",
                Allowed = () => ThirdPermitShown() && StoreReputation.GetSecReputation() >= SecurityReputationNeeded, GameSprite = SpriteDict.GetRandomSecSpriteName,
                Wants = new[]
                {
                    new Want { Tier = "三档", Classes = new[] { "中间威力步枪", "全威力步枪", "精确步枪" }, Stats = new[] { "GUN_CURRENT_RELIABILITY_TAG" }, Legal = true,
                        Zh = "我们特警队要添一把步枪。要靠得住，手续要干净。", En = "Our tactical unit needs another rifle. It has to be reliable, with clean papers." },
                },
            },
        };

        internal static void Register()
        {
            foreach (Kind k in Kinds)
            {
                Kind kind = k;
                StoreClientListDict.storeClientDict[kind.Id] = () => CreateClient(kind);
            }
        }

        internal static Kind Find(string id)
        {
            foreach (Kind k in Kinds)
            {
                if (k.Id == id)
                    return k;
            }
            return null;
        }

        internal static bool IsOrder(GunOrderData order)
        {
            Kind k;
            return order != null && orders.TryGetValue(order, out k);
        }

        private static bool ThirdPermitShown()
        {
            return ShowcaseHelper.IsShowcaseContainItemById("permit_gun_3");
        }

        // The new customers who stand in for the buyers of this kind (LL, Merc).
        internal static List<Kind> StandIns(string buyer)
        {
            var list = new List<Kind>();
            foreach (Kind k in Kinds)
            {
                if (k.StandsFor == buyer)
                    list.Add(k);
            }
            return list;
        }

        // An order of the customer: one of its wants at random.
        internal static GunOrderData NewOrder(Kind k)
        {
            return CreateOrder(k, k.Wants[rng.Next(k.Wants.Length)]);
        }

        // The game's buyer just made with one of these orders takes the customer's name, face and faction.
        internal static void Dress(StoreClient client)
        {
            GunOrderData order = client != null ? client.gunOrderData : null;
            Kind k;
            if (order == null || !orders.TryGetValue(order, out k))
                return;
            Dress(client, k);
            GunsmithMod.Log(k.En + " comes: " + order.dialogText);
        }

        // Also for those who come to sell a gun (GunSellers).
        internal static void Dress(StoreClient client, Kind k)
        {
            client.displayName = Text.Pick(k.Zh, k.En);
            client.spriteName = Certificates.PortraitKey(k.Portrait) ?? k.GameSprite();
            client.clientFaction = k.Faction;
            for (Dialogue d = client.mainDialogue; d != null; d = d.nextDialogue)
                d.title = client.displayName;
        }

        // What the customer pays for the gun: the market price and the premium, as GunOrderData.GetBestPremium rounds.
        internal static int Pay(GameItem gun)
        {
            if (gun == null)
                return 0;
            int market = GunOrderData.GetMarketPrice(gun);
            return market + Mathf.RoundToInt(market * PremiumPercent / 100f);
        }

        internal static GunOrderData CreateOrder(Kind k, Want w)
        {
            var order = new GunOrderData();
            order.tier = w.Tier == "二档" ? 2 : 3;
            order.mandatoryRequirements.Add(Requirements.Class(w.Tier, w.Classes));
            if (w.Legal)
                order.mandatoryRequirements.Add(Requirements.Legal());
            foreach (string stat in w.Stats)
                order.mandatoryRequirements.Add(new GunOrderRequirement { id = stat, value = CustomerTypes.Threshold(w.Classes, stat) - 1, isMustBeLargerThan = true });
            order.bonusRequirements.Add(Requirements.MarketPremium(PremiumPercent));
            order.baseReward = 0;
            order.dialogText = Text.Pick(w.Zh, w.En);
            orders.Remove(order);
            orders.Add(order, k);
            return order;
        }

        internal static StoreClient CreateClient(Kind k)
        {
            try
            {
                GunOrderData order = NewOrder(k);
                return k.StandsFor == "LL" ? StoreClientListGun.CreateLLGunBuyer(order) : StoreClientListGun.CreateMercGunBuyer(order);
            }
            catch (Exception e)
            {
                GunsmithMod.Warn(k.En + " not created: " + e.Message);
                return Reservations.Nobody();
            }
        }
    }

    // The order's only pay is its market price row, whose amount follows the gun on the counter.
    [HarmonyPatch(typeof(GunOrderRequirement), nameof(GunOrderRequirement.GetReward))]
    internal static class MarketRowRewardPatch
    {
        private static void Postfix(GunOrderRequirement __instance, ref int __result)
        {
            if (Requirements.IsMarketPremium(__instance))
                __result = NewCustomers.Pay(GunOrderData.ResolveOrderGun());
        }
    }
}
