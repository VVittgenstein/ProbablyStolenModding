using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace GunsmithExpansion
{
    // Repair certificates (final-strategy 91, 93, 95 to 97, 102; current/006; VVittgenstein 2026-11-03 21:49 to 23:47), made
    // like the game's appraisal certificates (AppraisalRankHelper, read 2026-11-03):
    //   - Four levels, one item each (gse_cert_repair1 to 4, pictures of the same names, current/006 unit U127). The
    //     store's level and progress are kept in PlayerStore.modData.
    //   - A certificate in the display case draws repair customers: one prediction of the display case
    //     (ShowcaseHelper.GetDisplayCasePrediction) whose chance is the level's 33, 42, 48 or 55%, raised for every
    //     day none came as the game raises the appraisal customers' (chance x (2 + days missed) / 2, at most 100).
    //     Repair customers come only this way (final-strategy 97; until 0.5.15 they were 30% of the gun sellers).
    //   - Progress per accepted repair order: 4 for every fault repaired with material, 2 for every fault repaired in
    //     an emergency way (VVittgenstein 2026-11-06 01:18, current/008 quote 20, item 72; until 0.6.2 3 and 1), times the game's appraisal progress multiplier of the difficulty. A hidden fault left on the
    //     gun takes nothing off (VVittgenstein 2026-11-04 20:38, current/006 quote 54; until the first 0.6.2 build it halved the order's progress).
    //   - At 100, 150 and 250 a member of the gunsmiths' guild comes the next day and offers the next level for the
    //     game's appraisal join cost x 1.25, 1.5 and 1.75; turned down for now he comes back in 7 days, turned down for
    //     good never (as AppraisalRankHelper.DeclinePromotionForNow and ForGood). His portrait is this mod's
    //     Textures/Items/gse_guild_member.png (unit U128), put into the game's SpriteDict.
    //   - Benefits (final-strategy 96): the repair fee of each order 15, 30, 50, 80 (the order's base reward); the
    //     customers' guns up to tier one, two, three, and at level four also the special tier; refurbishing with gun
    //     parts 0, 5, 10, 15 points more likely; at level four a gun whose faults are all repaired with material comes
    //     out Excellent.
    //   - The first certificate comes with the gunsmith start and with the retired gunsmith service, together with the
    //     gunsmith neon sign (GunsmithSign; final-strategy 82, 97; VVittgenstein 2026-11-05, current/008 quote 1 point 2 A). A save
    //     from before 0.7 that has the service and no certificate gets nothing on loading (VVittgenstein 2026-11-07 20:16, current/T-006
    //     quote 21). From 0.6.0 to 0.6.2 it came with weapon permit I
    //     instead of the service, where the game hands out its firearm sign (NetworkUpgradeList.UnlockActions, read
    //     2026-11-04); stores that got it that way keep it.
    //   - The tooltip lists the repair fee, the pay per fault for the tiers the customers bring, the chance a repair
    //     customer comes, the refurbishing bonus, Excellent at level four and the promotion progress, like the game's
    //     appraisal certificate (VVittgenstein 2026-11-04, current/006 quote 35 point 2).
    // Agent's decisions: the customers' factions (lower level 100, mercenary 90, upper level 40 in weight), the share of
    // each tier (the top tier 60%, the rest even; at level four the special tier 20%), the lines and the names.
    internal static class Certificates
    {
        internal const string IdPrefix = "gse_cert_repair";
        internal const string ClientId = "gseRepairCustomer";
        internal const string GuildMemberId = "gseGuildMember";
        internal const string Portrait = "gse_guild_member";
        internal static readonly int[] AttractionPercent = { 33, 42, 48, 55 };
        internal static readonly int[] Fees = { 15, 30, 50, 80 };
        internal static readonly int[] Thresholds = { 100, 150, 250 };
        internal const int FixedProgress = 4;
        internal const int EmergencyProgress = 2;
        internal static readonly float[] CostShares = { 1.25f, 1.5f, 1.75f };
        internal static readonly int[] RefurbishBonus = { 0, 5, 10, 15 };
        private const string RankKey = "repair_rank";
        private const string ProgressKey = "repair_progress_tenths";
        private const string PromotionDayKey = "repair_promotion_day";
        private const string DeclinedKey = "repair_promotion_declined";
        private const string MissedKey = "repair_days_missed";
        private static readonly System.Random rng = new System.Random();

        internal static void Register(ModItemDirectory directory)
        {
            int n = 0;
            for (int k = 1; k <= 4; k++)
            {
                int level = k;
                if (!ModHelper.ModFileExists(GunsmithMod.ModId, "Textures/Items/" + ItemId(level) + ".png"))
                    continue;
                directory.Add(ItemId(level), () => Create(directory, level));
                n++;
            }
            GunsmithMod.Log("registered " + n + " repair certificates");
        }

        internal static void RegisterClients()
        {
            StoreClientListDict.storeClientDict[ClientId] = CreateRepairCustomer;
            StoreClientListDict.storeClientDict[GuildMemberId] = CreateGuildMember;
        }

        internal static string ItemId(int level)
        {
            return IdPrefix + level;
        }

        // 1 to 4 for a repair certificate, else 0.
        internal static int LevelOf(GameItem item)
        {
            int level;
            if (item == null || item.identifier == null || !item.identifier.StartsWith(IdPrefix, StringComparison.Ordinal)
                || !int.TryParse(item.identifier.Substring(IdPrefix.Length), out level) || level < 1 || level > 4)
                return 0;
            return level;
        }

        internal static string Name(int level)
        {
            switch (level)
            {
                case 1: return Text.Pick("枪匠工会初级维修证书", "Gunsmiths' Guild Junior Repair Certificate");
                case 2: return Text.Pick("枪匠工会中级维修证书", "Gunsmiths' Guild Journeyman Repair Certificate");
                case 3: return Text.Pick("枪匠工会高级维修证书", "Gunsmiths' Guild Senior Repair Certificate");
                default: return Text.Pick("枪匠工会大师级维修证书", "Gunsmiths' Guild Master Repair Certificate");
            }
        }

        private static GameItem Create(ModItemDirectory directory, int level)
        {
            GameItem item = directory.Empty().SetSpriteAndShapeFromMod(GunsmithMod.ModId, ItemId(level));
            item.name = Name(level);
            item.flavorText = Text.Pick("证明你具备为枪械检查故障并修理的资格。", "Certifies you to inspect and repair firearms.");
            item.shortDescription = Text.Pick("将其放入展示柜，可吸引需要修枪的顾客到访。", "Place it in the display case to attract customers who need a gun repaired.");
            item.unitValue = 0;
            item.EnableTag("IMPORTANT_TAG", false);
            item.SetGameItemType("DOCUMENT");
            AudioHelper.InitPaperSheet(item);
            return item;
        }

        private static int Today()
        {
            return StoreStation.GetDayCounter();
        }

        // 0 before the store has a certificate, else 1 to 4.
        internal static int Rank()
        {
            return Mathf.Clamp(ModHelper.GetModDataInt(GunsmithMod.ModId, RankKey, 0), 0, 4);
        }

        private static void SetRank(int rank)
        {
            ModHelper.SetModDataInt(GunsmithMod.ModId, RankKey, Mathf.Clamp(rank, 0, 4));
        }

        internal static float Progress()
        {
            return ModHelper.GetModDataInt(GunsmithMod.ModId, ProgressKey, 0) / 10f;
        }

        private static void SetProgress(float progress)
        {
            ModHelper.SetModDataInt(GunsmithMod.ModId, ProgressKey, Mathf.RoundToInt(progress * 10f));
        }

        internal static int Fee()
        {
            int rank = Rank();
            return rank >= 1 ? Fees[rank - 1] : 0;
        }

        internal static GameItem DisplayedCertificate()
        {
            if (EmporiumEntry.Instance == null || EmporiumEntry.Instance.showcaseElement == null)
                return null;
            foreach (GameItem item in EmporiumEntry.Instance.showcaseElement.childItems)
            {
                if (LevelOf(item) > 0)
                    return item;
            }
            return null;
        }

        internal static int Chance()
        {
            int rank = Math.Max(1, Rank());
            int missed = ModHelper.GetModDataInt(GunsmithMod.ModId, MissedKey, 0);
            return Mathf.Min(100, AttractionPercent[rank - 1] * (2 + missed) / 2);
        }

        // In the place of the game's appraisal customer, which ShowcaseHelper.GetDisplayCasePrediction adds just before the firearm signs' sellers: the game rolls the
        // predictions in order and stops at the day's number of customers (StoreClientManager.HandleNormalClient), so a prediction at the end of the list is not rolled
        // on the days the ones before it fill that number (VVittgenstein 2026-11-07 20:06, current/T-006 quote 20; until then it was added at the end).
        internal static void AddPrediction(List<ValueTuple<string, int, string>> prediction)
        {
            GameItem cert = DisplayedCertificate();
            // with nothing in the store to repair with he does not come (item 9)
            if (cert == null || Rank() <= 0 || Repair.RepairableNow().Count == 0)
                return;
            var entry = new ValueTuple<string, int, string>(ClientId, Chance(), cert.identifier);
            int at = prediction.FindIndex(p => p.Item3 == ShowcaseHelper.SIGN_FIREARM || p.Item3 == ShowcaseHelper.SIGN_FIREARM_HEAVY);
            if (at < 0)
                prediction.Add(entry);
            else
                prediction.Insert(at, entry);
        }

        internal static void OnRolled(List<ValueTuple<string, int, string>> prediction, HashSet<string> spawnedIds)
        {
            if (prediction == null || !prediction.Exists(p => p.Item1 == ClientId))
                return;
            int missed = spawnedIds != null && spawnedIds.Contains(ClientId) ? 0 : ModHelper.GetModDataInt(GunsmithMod.ModId, MissedKey, 0) + 1;
            ModHelper.SetModDataInt(GunsmithMod.ModId, MissedKey, missed);
        }

        // Tier of a repair customer's gun by the certificate's level; 4 is the special tier.
        internal static int PickTier()
        {
            int rank = Math.Max(1, Rank());
            if (rank >= 4 && rng.Next(100) < 20)
                return 4;
            int top = Math.Min(rank, 3);
            if (top == 1 || rng.Next(100) < 60)
                return top;
            return 1 + rng.Next(top - 1);
        }

        private static StoreClient CreateRepairCustomer()
        {
            int roll = rng.Next(230);
            string kind = roll < 100 ? "LL" : roll < 190 ? "Merc" : "UL";
            StoreClient client = null;
            try
            {
                client = RepairOrders.CreateCustomer(kind, PickTier());
            }
            catch (Exception e)
            {
                GunsmithMod.Warn("repair customer not created: " + e.Message);
            }
            return client ?? Reservations.Nobody();
        }

        internal static void OnRepairAccepted(GunOrderData order)
        {
            if (order == null || !order.isServiceOrder)
                return;
            GameItem gun = order.GetPlacedServiceGun();
            if (!Faults.IsModGun(gun) || !gun.IsTag(Repair.ServiceTag))
                return;
            int fixedCount = 0, emergency = 0;
            foreach (FaultState s in Faults.Get(gun).Values)
            {
                if (s == FaultState.Fixed) fixedCount++;
                else if (s == FaultState.Emergency) emergency++;
            }
            AddProgress(FixedProgress * fixedCount + EmergencyProgress * emergency);
        }

        private static float GainMultiplier()
        {
            try
            {
                return Difficulty.Rules != null ? Difficulty.Rules.appraisalProgressGainMultiplier : 1f;
            }
            catch (Exception)
            {
                return 1f;
            }
        }

        internal static bool CanProgress()
        {
            int rank = Rank();
            return rank >= 1 && rank <= 3 && !ModHelper.GetModDataBool(GunsmithMod.ModId, DeclinedKey, false);
        }

        internal static bool PromotionReady()
        {
            return CanProgress() && Progress() >= Thresholds[Rank() - 1];
        }

        internal static void AddProgress(float raw)
        {
            if (!CanProgress() || PromotionReady() || raw <= 0f)
                return;
            float threshold = Thresholds[Rank() - 1];
            float progress = Mathf.Min(threshold, Progress() + raw * GainMultiplier());
            SetProgress(progress);
            GunsmithMod.Log("repair certificate progress " + progress + " of " + threshold + " (+" + raw + ")");
            if (progress >= threshold)
                ModHelper.SetModDataInt(GunsmithMod.ModId, PromotionDayKey, Today() + 1);
        }

        internal static int PromotionCost(int toRank)
        {
            int join = 85;
            try
            {
                join = Traverse.Create(PlayerStore.Instance).Method("GetAppraisalJoinCost").GetValue<int>();
            }
            catch (Exception)
            {
                join = 85;
            }
            return Mathf.RoundToInt(join * CostShares[Mathf.Clamp(toRank - 2, 0, 2)]);
        }

        // Each morning, with the game's appraisal promotion (AppraisalRankHelper.HandlePromotionClient).
        internal static void HandlePromotion(StoreClientManager manager)
        {
            if (manager == null || !PromotionReady())
                return;
            int day = ModHelper.GetModDataInt(GunsmithMod.ModId, PromotionDayKey, -1);
            if (day < 0 || day > Today())
                return;
            manager.AddClient(CreateGuildMember());
            GunsmithMod.Log("the gunsmiths' guild member comes today");
        }

        // A portrait of this mod (Textures/Items/<name>.png) put into the game's SpriteDict: its key, or null when the
        // picture is not there. Also the new customers' portraits (NewCustomers).
        internal static string PortraitKey(string portrait)
        {
            try
            {
                SpriteDict dict = SpriteDict.Instance;
                if (dict == null || dict.spriteDictionary == null)
                    return null;
                if (dict.spriteDictionary.ContainsKey(portrait))
                    return portrait;
                LoadedMod mod = ModLoader.Mods.Find(m => m.Instance is GunsmithMod);
                Sprite own;
                if (mod == null || mod.Sprites == null || !mod.Sprites.TryGetValue(portrait, out own) || own == null)
                    return null;
                // the size and pivot of the game's own portraits, taken from the clerk's
                Sprite model = dict.GetSprite(SpriteDict.GetClerkSpriteName());
                Vector2 pivot = model != null ? new Vector2(model.pivot.x / model.rect.width, model.pivot.y / model.rect.height) : new Vector2(0.5f, 0.5f);
                float ppu = model != null ? model.pixelsPerUnit : own.pixelsPerUnit;
                own.texture.filterMode = FilterMode.Point;
                Sprite sprite = Sprite.Create(own.texture, own.rect, pivot, ppu);
                sprite.name = portrait;
                dict.spriteDictionary[portrait] = sprite;
                return portrait;
            }
            catch (Exception e)
            {
                GunsmithMod.Warn("portrait " + portrait + " not set: " + e.Message);
                return null;
            }
        }

        private static StoreClient CreateGuildMember()
        {
            var client = new StoreClient();
            client.identifier = GuildMemberId;
            // not to be shot, as the game's appraisal board member who offers a promotion (StoreClientListAppraisal.CreatePromotionOffer;
            // VVittgenstein 2026-11-07 20:06, current/T-006 quote 20)
            client.nonShootable = true;
            client.displayName = Text.Pick("枪匠工会成员", "Gunsmiths' Guild Member");
            client.spriteName = PortraitKey(Portrait) ?? SpriteDict.GetClerkSpriteName();
            client.clientFaction = "FACTION_LOWER_LEVEL";
            client.clientIntent = StoreClient.ClientIntent.DIALOGUE;
            client.isBargainAvailable = false;
            client.CanReturn = false;
            string name = client.displayName;
            int toRank = Math.Min(4, Rank() + 1);
            int cost = PromotionCost(toRank);
            Dialogue first = client.mainDialogue.SetText(name, Text.Pick(
                "你好，我是枪匠工会的成员。听说你这阵子修了不少枪，手艺不错。",
                "Hello, I'm a member of the Gunsmiths' Guild. I hear you've repaired a good many guns lately, and done good work."));
            Dialogue ask = new Dialogue().SetText(name, string.Format(Text.Pick(
                "工会可以给你换一张{0}。晋升费只需 {1} 信用点。",
                "The guild can issue you a {0}. The promotion fee is only {1} credits."), Name(toRank), cost));
            first.SetNextDialogue(ask);

            Dialogue accept = new Dialogue().SetText("PLAYER", LocHelper.GetLocalizedDialogue("dialog_chem_feedback_choice_accept", cost));
            accept.SetNextDialogue(new Dialogue().SetText(name, Text.Pick(
                "这是新证书。我们会给你介绍更多修枪的顾客，带来的枪也会更好。",
                "Here's the new certificate. We'll send more repair customers your way, with better guns.")).SetEndAction(Reservations.Leave));
            ask.AddChoice(DirectoryMaster.Item("$", true), "accept", "", accept, () => Promote(toRank, cost),
                () => PlayerStore.Instance != null && PlayerStore.Instance.GetCash() >= cost);

            Dialogue later = new Dialogue().SetText("PLAYER", LocHelper.GetLocalizedDialogue("dialog_landlord_vending_player_decline"));
            later.SetNextDialogue(new Dialogue().SetText(name, LocHelper.GetLocalizedDialogue("dialog_appraisal_board_later_1")).SetEndAction(Reservations.Leave));
            ask.AddChoice(DirectoryMaster.Item("$", true), "later", "", later, DeclineForNow);

            Dialogue never = new Dialogue().SetText("PLAYER", Text.Pick("[不晋升]", "[No promotion]"));
            never.SetNextDialogue(new Dialogue().SetText(name, Text.Pick("真遗憾。工会不会再来打扰了。", "Shame. The guild won't trouble you again.")).SetEndAction(Reservations.Leave));
            ask.AddChoice(DirectoryMaster.Item("$", true), "never", "", never, DeclineForGood);
            client.CompleteClientCreation(false);
            return client;
        }

        private static void Promote(int toRank, int cost)
        {
            if (PlayerStore.Instance == null || Rank() >= toRank)
                return;
            PlayerStore.Instance.ModCash(-cost, true);
            SetRank(toRank);
            SetProgress(0f);
            ModHelper.SetModDataInt(GunsmithMod.ModId, PromotionDayKey, -1);
            ReplaceCertificate(toRank);
            GunsmithMod.Log("repair certificate promoted to level " + toRank + " for " + cost);
        }

        private static void DeclineForNow()
        {
            ModHelper.SetModDataInt(GunsmithMod.ModId, PromotionDayKey, Today() + 7);
        }

        private static void DeclineForGood()
        {
            ModHelper.SetModDataBool(GunsmithMod.ModId, DeclinedKey, true);
            ModHelper.SetModDataInt(GunsmithMod.ModId, PromotionDayKey, -1);
        }

        private static List<GameItem> OwnedCertificates()
        {
            var list = new List<GameItem>();
            if (EmporiumEntry.Instance == null)
                return list;
            foreach (GameItem item in EmporiumEntry.Instance.GetAllOwnedItems(false, true))
            {
                if (LevelOf(item) > 0)
                    list.Add(item);
            }
            return list;
        }

        private static GameItem NewCertificate(int level)
        {
            GameItem cert = DirectoryMaster.Item(ItemId(level), true);
            GeneralHelper.SetItemOwned(cert, true);
            return cert;
        }

        // The old certificates go; the new one takes the place of the first of them (as AppraisalRankHelper.
        // ReplaceCertificate), else the inventory.
        private static void ReplaceCertificate(int level)
        {
            List<GameItem> old = OwnedCertificates();
            GameInventory place = old.Count > 0 ? old[0].parentInventory : null;
            foreach (GameItem item in old)
                item.Destroy();
            GraphUtils.EmporiumTryAdd((GraphNodeStorage)place ?? EmporiumEntry.Instance.invElement, NewCertificate(level));
        }

        // The first certificate (gunsmith start, retired gunsmith service); a store that already has a level but lost its
        // certificate gets that level's again.
        internal static void GrantFirst(GraphNodeStorage place)
        {
            if (EmporiumEntry.Instance == null)
                return;
            int rank = Rank();
            if (rank == 0)
            {
                SetRank(1);
                rank = 1;
            }
            else if (OwnedCertificates().Count > 0)
            {
                return;
            }
            GraphUtils.EmporiumTryAdd(place ?? EmporiumEntry.Instance.invElement, NewCertificate(rank));
            SaveRecord.Given("first_certificate");
            GunsmithMod.Log("repair certificate level " + rank + " given");
        }

        internal static int RefurbishBonusPoints()
        {
            int rank = Rank();
            return rank >= 1 ? RefurbishBonus[rank - 1] : 0;
        }

        private static readonly string[] TierKeys = { "一档", "二档", "三档", "特殊" };

        // The game's name of the Excellent condition (its condition feature); the mod's own word if that fails.
        internal static string ExcellentName()
        {
            try
            {
                ItemCondition c = ItemConditionList.CreateEquipementConditionExcellent();
                string name = c != null ? System.Text.RegularExpressions.Regex.Replace(c.display ?? "", "<[^>]*>", "").Trim() : "";
                if (name.Length > 0 && name.IndexOf('_') < 0)
                    return name;
            }
            catch (Exception)
            {
            }
            return Text.Pick("极好", "Excellent");
        }

        // The certificate's tooltip lines, where the game writes the appraisal certificate's (AppraisalRankHelper.
        // CreateCertificateTooltip, read 2026-11-04: an empty line, the fees, the chance of a visit, rising after days
        // without one, what may be appraised, the promotion progress of the store's own level, in LightCyan and the
        // progress in LightGreen).
        internal static void AppendTooltip(RichTextBuilder builder, GameItem item)
        {
            int level = LevelOf(item);
            if (level == 0)
                return;
            RenderHandler.ColorPalette info = RenderHandler.ColorPalette.LightCyan;
            bool own = level == Rank();
            builder.AddLine(" ", true);
            Lines.Add(builder, "", string.Format(Text.Pick("基础维修费：每单{0}信用点", "Base repair fee: {0} credits an order"), Fees[level - 1]), info);
            Lines.Add(builder, "", Text.Pick("每项故障的报酬（信用点）：", "Pay per fault (credits):"), info);
            int tiers = level >= 4 ? 4 : level;
            for (int t = 0; t < tiers; t++)
            {
                string tier = TierKeys[t];
                int[] rust = RepairOrders.RustPay(tier);
                Lines.Add(builder, "", LocHelper.BulletLine(string.Format(Text.Pick("{0}：表面故障{1}，内部故障{2}，锈蚀积垢{3}（应急：只用酒精{4}，只用织物{5}）", "{0}: surface {1}, internal {2}, rust and fouling {3} (emergency: alcohol only {4}, fabric only {5})"),
                    Text.TierName(tier), RepairOrders.TierFaultPay(tier, true), RepairOrders.TierFaultPay(tier, false), rust[0], rust[1], rust[2])), info);
            }
            Lines.Add(builder, "", Text.Pick("其他故障应急修复的报酬减半。", "Other emergency repairs pay half."), info);
            int chance = own ? Chance() : AttractionPercent[level - 1];
            Lines.Add(builder, "", string.Format(Text.Pick("修枪顾客来访概率：每天{0}%", "Repair customer visit chance: {0}% a day"), chance), info);
            if (own && ModHelper.GetModDataInt(GunsmithMod.ModId, MissedKey, 0) > 0 && chance < 100)
                // the game's Chinese line names the appraisal customer; its English and Russian lines name none
                Lines.Add(builder, "", Text.Chinese()
                    ? string.Format("每天没有修枪顾客上门，几率都会提高，基础为{0}%", AttractionPercent[level - 1])
                    : LocHelper.GetLocalizedMechanicOrDefault("mech_appraisal_cert_chance_ramp", "Rises every day without one, from {0}%", AttractionPercent[level - 1]), info);
            if (RefurbishBonus[level - 1] > 0)
                Lines.Add(builder, "", string.Format(Text.Pick("用枪械零件翻新的成功率：+{0}%", "Refurbishing with gun parts: +{0}% chance"),
                    RefurbishBonus[level - 1]), info);
            if (level >= 4)
                Lines.Add(builder, "", string.Format(Text.Pick("全部故障都完整修复的枪，成色为{0}", "A gun with every fault fully repaired comes out {0}"),
                    ExcellentName()), info);
            if (!own)
                return;
            if (PromotionReady())
                Lines.Add(builder, "", Text.Pick("已达到晋升条件，枪匠工会成员将上门。", "Promotion ready: a Gunsmiths' Guild member will come by."),
                    RenderHandler.ColorPalette.LightGreen);
            else if (CanProgress())
                Lines.Add(builder, "", LocHelper.GetLocalizedMechanic("mech_appraisal_cert_progress",
                    Mathf.Clamp((int)(Progress() * 100f / Thresholds[level - 1]), 0, 100)), RenderHandler.ColorPalette.LightGreen);
        }
    }

    [HarmonyPatch(typeof(ShowcaseHelper), nameof(ShowcaseHelper.GetDisplayCasePrediction))]
    internal static class DisplayCasePredictionPatch
    {
        private static void Postfix(List<ValueTuple<string, int, string>> __result)
        {
            if (__result == null)
                return;
            try
            {
                Certificates.AddPrediction(__result);
                GunsmithSign.AddPredictions(__result);
            }
            catch (Exception e)
            {
                GunsmithMod.Warn("display case prediction: " + e.Message);
            }
        }
    }

    [HarmonyPatch(typeof(AppraisalRankHelper), nameof(AppraisalRankHelper.OnCustomerRolled))]
    internal static class CustomerRolledPatch
    {
        private static void Postfix(List<ValueTuple<string, int, string>> prediction, HashSet<string> spawnedIds)
        {
            Certificates.OnRolled(prediction, spawnedIds);
            GunsmithSign.OnRolled(prediction, spawnedIds);
        }
    }

    [HarmonyPatch(typeof(AppraisalRankHelper), nameof(AppraisalRankHelper.HandlePromotionClient))]
    internal static class GuildPromotionPatch
    {
        private static void Postfix(StoreClientManager manager)
        {
            try
            {
                Certificates.HandlePromotion(manager);
            }
            catch (Exception e)
            {
                GunsmithMod.Warn("guild promotion: " + e.Message);
            }
        }
    }

    // The game calls this for every item's tooltip (GameItemElement.GetTooltipBasic) and leaves at once for an item
    // that is not an appraisal certificate; the repair certificate's lines go here, where the appraisal lines would.
    [HarmonyPatch(typeof(AppraisalRankHelper), "CreateCertificateTooltip")]
    internal static class CertificateTooltipPatch
    {
        private static void Postfix(RichTextBuilder builder, GameItem item)
        {
            try
            {
                Certificates.AppendTooltip(builder, item);
            }
            catch (Exception e)
            {
                GunsmithMod.Warn("certificate tooltip: " + e.Message);
            }
        }
    }

    // The clientele panel names this mod's display case customers without building them.
    [HarmonyPatch(typeof(StoreClientListDict), nameof(StoreClientListDict.GetClientDisplayName))]
    internal static class OwnClientNamePatch
    {
        private static bool Prefix(string identifier, ref string __result)
        {
            if (identifier == Certificates.ClientId)
            {
                __result = Text.Pick("修枪顾客", "Repair customer");
                return false;
            }
            if (identifier == Certificates.GuildMemberId)
            {
                __result = Text.Pick("枪匠工会成员", "Gunsmiths' Guild Member");
                return false;
            }
            NewCustomers.Kind kind = NewCustomers.Find(identifier);
            if (kind != null)
            {
                __result = Text.Pick(kind.Zh, kind.En);
                return false;
            }
            return true;
        }
    }

    // Refurbishing with gun parts: the certificate's points on top of the game's chance.
    [HarmonyPatch(typeof(GunHelper), nameof(GunHelper.GetRefurbishChance))]
    internal static class RefurbishBonusPatch
    {
        private static void Postfix(ref int __result)
        {
            if (__result > 0)
                __result = Mathf.Min(100, __result + Certificates.RefurbishBonusPoints());
        }
    }

    // A failed refurbishment keeps the chance it was rolled at as the gun's current repairability, so that the next try adds the gun's repairability
    // once more (GunHelper.TryRepairGun with GetRefurbishChance). That chance has the certificate's points in it, which then came again on every
    // failure: a level 4 certificate on a gun of repairability 10 went 25, 50, 75, 100% where its 15 points give 25, 35, 45, 55% (item 12). The game's chance without the points is kept instead; a success sets it to 0 as the game does.
    [HarmonyPatch(typeof(GunHelper), nameof(GunHelper.TryRepairGun))]
    internal static class RefurbishKeepPatch
    {
        private static void Prefix(GameItem targetItem, out int __state)
        {
            __state = targetItem != null && targetItem.IsTag("ADV_GUN_TAG")
                ? targetItem.GetTagReadonly("GUN_CURRENT_REPAIRBILITY_TAG").GetInt() + targetItem.GetTagReadonly("GUN_REPAIRBILITY_TAG").GetInt()
                : 0;
        }

        private static void Postfix(GameItem targetItem, int __state)
        {
            if (targetItem == null || !targetItem.IsTag("ADV_GUN_TAG") || targetItem.GetTagReadonly("GUN_CURRENT_REPAIRBILITY_TAG").GetInt() == 0)
                return;
            int kept = __state;
            targetItem.ModifyTag("GUN_CURRENT_REPAIRBILITY_TAG", state => state.Enable().SetInt(kept), false);
        }
    }
}
