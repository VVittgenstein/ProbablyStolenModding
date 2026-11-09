using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace GunsmithExpansion
{
    // The gunsmith neon sign (final-strategy 82, 93, 98; VVittgenstein 2026-11-03 19:25, 22:13, 22:44). It comes with the first repair certificate
    // (Certificates) and a pistol cabinet from the retired gunsmith service, and from the gunsmith start, which unlocks that service
    // (final-strategy 82 and 97; VVittgenstein 2026-11-05, current/008 quote 1 point 2 A). From 0.6.0 to 0.6.2 it also took the place of the game's firearm
    // neon sign (sign_firearm) at the gunsmith start, at weapon permit I and in Dr. Jackson's stock; from 0.7 the game hands out its own sign
    // there (current/008 quote 22 point 18). A store has one gunsmith sign from these.
    // What it draws (VVittgenstein 2026-11-06 02:18 and 03:40, current/008 quotes 21 and 23, item 17;
    // the table in review/2026-11-05/customer-queue-facts.md section 8): nobody of the game's. Until 0.6.2 it drew gun sellers at 20% and gun buyers at
    // 50%, and the game's gun buyers turned into this mod's customers at 15%, 10% and 25%. From 0.7, in the display case
    // (ShowcaseHelper.GetDisplayCasePrediction) it draws one customer, in the third batch and last in its order: 50%, raised for every day none came
    // (x (2 + days missed) / 2) and by the market, the numbers of the game's weapon permit I (ShowcaseHelper.GetGunBuyerChance). He comes for one of the
    // tasks the store can take that day, each as likely as the others (CreateCustomer): a reservation while the store has room for one (Reservations), ammunition
    // in bulk and a collector's gun from the second weapon permit on (Ammo, Collection). He comes as a faction the store's permits bring (PickKind). Value 100 as the game's sign.
    internal static class GunsmithSign
    {
        internal const string Id = "gse_sign_gunsmith";
        internal const string VanillaId = "sign_firearm";
        internal const string ClientId = "gseSignCustomer";
        internal const int CustomerPercent = 50;
        private const string MissedKey = "sign_buyer_days_missed";
        private static readonly System.Random rng = new System.Random();

        internal static void Register(ModItemDirectory directory)
        {
            if (!ModHelper.ModFileExists(GunsmithMod.ModId, "Textures/Items/" + Id + ".png"))
            {
                GunsmithMod.Warn("the gunsmith sign's picture is missing; no gunsmith sign");
                return;
            }
            directory.Add(Id, () => Create(directory));
            GunsmithMod.Log("registered the gunsmith neon sign");
        }

        internal static bool Registered()
        {
            return ModHelper.ModFileExists(GunsmithMod.ModId, "Textures/Items/" + Id + ".png");
        }

        private static string Description()
        {
            return Text.Pick("将其放置在展示柜中，可吸引来预订枪、批量买弹药和收藏枪的顾客。",
                "Place it in the display case to attract customers who reserve guns, buy ammunition in bulk or collect guns.");
        }

        private static GameItem Create(ModItemDirectory directory)
        {
            GameItem item = directory.Empty().SetSpriteAndShapeFromMod(GunsmithMod.ModId, Id);
            item.name = Text.Pick("枪匠霓虹灯牌", "Gunsmith Neon Sign");
            item.shortDescription = Description();
            item.unitValue = 100;
            item.EnableTag("IMPORTANT_TAG", false);
            ShowcaseHelper.InitShowcaseSpecialItem(item, "FIREARM");
            return item;
        }

        internal static void RegisterClient()
        {
            StoreClientListDict.storeClientDict[ClientId] = CreateCustomer;
        }

        // "*" after the chance in the store's list of customers while it is raised by days missed or by the market, as the game marks its gun buyers'
        // (ShowcaseHelper.GetChanceSuffix).
        internal static string ChanceSuffix()
        {
            return ModHelper.GetModDataInt(GunsmithMod.ModId, MissedKey, 0) > 0 || GunOrderData.GetMarketModifierPercent() != 0 ? "*" : "";
        }

        internal static int Chance()
        {
            int missed = ModHelper.GetModDataInt(GunsmithMod.ModId, MissedKey, 0);
            int chance = Mathf.Min(100, CustomerPercent * (2 + missed) / 2);
            float market = Mathf.Max(0f, 1f + GunOrderData.GetMarketModifierPercent() / 100f);
            return Mathf.Clamp(Mathf.RoundToInt(chance * market), 0, 100);
        }

        // The tasks the store can take now.
        private static List<Func<StoreClient>> Tasks()
        {
            var tasks = new List<Func<StoreClient>>();
            if (Reservations.CanOffer())
                tasks.Add(Reservations.CreateSignCustomer);
            if (Ammo.CanSellInBulk())
                tasks.Add(Ammo.CreateSignCustomer);
            if (Collection.CanOrder())
                tasks.Add(Collection.CreateSignCustomer);
            return tasks;
        }

        // One of the tasks, each as likely as the others (item 17; VVittgenstein asked what it means, current/008 quote 23, and agreed, quote 24). A task that turns out
        // to have nothing to offer leaves it to the others.
        private static StoreClient CreateCustomer()
        {
            try
            {
                List<Func<StoreClient>> tasks = Tasks();
                while (tasks.Count > 0)
                {
                    int i = rng.Next(tasks.Count);
                    StoreClient client = tasks[i]();
                    if (client != null)
                        return client;
                    tasks.RemoveAt(i);
                }
            }
            catch (Exception e)
            {
                GunsmithMod.Warn("gunsmith sign customer not created: " + e.Message);
            }
            return Reservations.Nobody();
        }

        // The faction kind of a sign customer, up to the highest weapon permit given: the game's permits bring lower level buyers with permit I,
        // mercenaries with II, upper level with III (ShowcaseHelper.GUN_BUYERS), so a store with permit I has no mercenary asking for a tier two gun
        // (current/008 quote 20 point 17). Agent's: in weight lower level 100, mercenary 90, upper level 40, as the repair customers (Certificates).
        internal static string PickKind(int top)
        {
            int roll = rng.Next(100 + (top >= 2 ? 90 : 0) + (top >= 3 ? 40 : 0));
            return roll < 100 ? "LL" : roll < 190 ? "Merc" : "UL";
        }

        // A buyer of the game of that kind, for the customer's name, face and faction. A plain one: the patches of the game's buyers (LowerLevelBuyerPatch and the
        // others) are off meanwhile, which would turn some into custom orders and into the gang member, the elite mercenary or the security tactical officer, who are
        // not reservation or ammunition customers (final-strategy 214). Until then the sign's customers could look like them (found 2026-11-07 by current/T-007).
        internal static bool PlainBuyer;

        internal static StoreClient Look(string kind)
        {
            PlainBuyer = true;
            try
            {
                return kind == "Merc" ? StoreClientListGun.CreateMercGunBuyer() : kind == "UL" ? StoreClientListGun.CreateULBuyer() : StoreClientListGun.CreateLLGunBuyer();
            }
            finally
            {
                PlainBuyer = false;
            }
        }

        internal static void AddPredictions(List<ValueTuple<string, int, string>> prediction)
        {
            if (ShowcaseHelper.IsShowcaseContainItemById(Id) && Tasks().Count > 0)
                prediction.Add(new ValueTuple<string, int, string>(ClientId, Chance(), Id));
        }

        internal static void OnRolled(List<ValueTuple<string, int, string>> prediction, HashSet<string> spawnedIds)
        {
            if (prediction == null || !prediction.Exists(p => p.Item1 == ClientId))
                return;
            int missed = spawnedIds != null && spawnedIds.Contains(ClientId) ? 0 : ModHelper.GetModDataInt(GunsmithMod.ModId, MissedKey, 0) + 1;
            ModHelper.SetModDataInt(GunsmithMod.ModId, MissedKey, missed);
        }

        internal static GameItem NewSign(bool owned)
        {
            GameItem sign = DirectoryMaster.Item(Id, true);
            GeneralHelper.SetItemOwned(sign, owned);
            return sign;
        }

        internal static bool OwnsSign()
        {
            if (EmporiumEntry.Instance == null)
                return false;
            foreach (GameItem item in EmporiumEntry.Instance.GetAllOwnedItems(false, true))
            {
                if (item != null && item.IsID(Id))
                    return true;
            }
            return false;
        }

        // The retired gunsmith service: the sign unless the store has one, and the first certificate.
        internal static void GrantWithService(GraphNodeStorage place, string why)
        {
            if (!Registered() || EmporiumEntry.Instance == null)
                return;
            if (!OwnsSign())
            {
                GraphUtils.EmporiumTryAdd(place ?? EmporiumEntry.Instance.invElement, NewSign(true));
                SaveRecord.Given("gunsmith_sign");
                GunsmithMod.Log("a gunsmith sign given (" + why + ")");
            }
            Certificates.GrantFirst(place);
        }

        // A new game. The gunsmith start has the game's firearm sign and weapon permit I (NewGameData.HandleInitialItem) and unlocks the retired
        // gunsmith service without its action, so it gets what the service gives here.
        internal static void OnNewGame()
        {
            SaveRecord.OnNewGame();
            if (NewGameData.Instance != null && NewGameData.Instance.startType == NewGameData.StartType.GUNSMITH && EmporiumEntry.Instance != null)
                GrantWithService(EmporiumEntry.Instance.invElement, "gunsmith start");
        }

        // A save from before 0.7 that has the retired gunsmith service and no certificate gets nothing on loading (VVittgenstein 2026-11-07 20:16,
        // current/T-006 quote 21; quote 22). Until then such a store got
        // the sign and the first certificate on every load.
    }

    // The store's list of customers (ClienteleUIManager) names the sign's customer by what draws him: which task he comes for is decided when he comes.
    [HarmonyPatch(typeof(StoreClientListDict), nameof(StoreClientListDict.GetClientDisplayName))]
    internal static class SignCustomerNamePatch
    {
        private static bool Prefix(string identifier, ref string __result)
        {
            if (identifier != GunsmithSign.ClientId)
                return true;
            __result = Text.Pick("枪匠灯牌的顾客", "Gunsmith sign customer");
            return false;
        }
    }

    // The list writes the line of a special item of the display case (SHOWCASE_SPECIAL_ITEM_TAG, which the gunsmith sign has as the game's
    // signs do: InitShowcaseSpecialItem) as "<item>: seller (<type>)", without the customer's name (ClienteleUIManager.RefreshUI, game
    // 1.0.0.12), so the sign's customer read as a gun seller (item 17: the list says "枪匠灯牌的顾客"). Afterwards the sign's line is written
    // as other items' lines, "<item>: <customer> (NN%)", with the mark the game puts after a gun buyer's chance raised by days missed or by
    // the market (ShowcaseHelper.GetChanceSuffix knows the game's gun buyers only).
    [HarmonyPatch(typeof(ClienteleUIManager), nameof(ClienteleUIManager.RefreshUI))]
    internal static class SignClienteleLinePatch
    {
        private static void Postfix(ClienteleUIManager __instance)
        {
            try
            {
                if (__instance == null || __instance.normalCustomerTMP == null || string.IsNullOrEmpty(__instance.normalCustomerTMP.text)
                    || EmporiumEntry.Instance == null || EmporiumEntry.Instance.showcaseElement == null)
                    return;
                GameItem sign = EmporiumEntry.Instance.showcaseElement.childItems.Find(i => i != null && i.identifier == GunsmithSign.Id);
                if (sign == null || !sign.IsTag("SHOWCASE_SPECIAL_ITEM_TAG"))
                    return;
                string head = "- " + GeneralHelper.GetDisplayNameFromID(GunsmithSign.Id) + ": ";
                string chance = " (" + GunsmithSign.Chance() + "%";
                string game = head + LocHelper.GetLocalizedUI("ui_clientele_seller", new object[0])
                    + " (" + TypeHelper.GetTypeDisplayName(sign.GetTagReadonly("SHOWCASE_SPECIAL_ITEM_TAG").GetString()) + ")" + chance;
                string text = __instance.normalCustomerTMP.text;
                if (text.Contains(game))
                    __instance.normalCustomerTMP.text = text.Replace(game, head + StoreClientListDict.GetClientDisplayName(GunsmithSign.ClientId) + chance + GunsmithSign.ChanceSuffix());
            }
            catch (Exception e)
            {
                GunsmithMod.Warn("customer list line of the gunsmith sign: " + e.Message);
            }
        }
    }

    internal static class GunsmithStartSignHook
    {
        internal static void OnNewGameStarted(string startId)
        {
            try
            {
                GunsmithSign.OnNewGame();
            }
            catch (Exception e)
            {
                GunsmithMod.Warn("gunsmith start sign: " + e.Message);
            }
            // A pistol cabinet with the gunsmith start (VVittgenstein 2026-11-04, current/006 quotes 35 point 4 and 36).
            try
            {
                if (NewGameData.Instance != null && NewGameData.Instance.startType == NewGameData.StartType.GUNSMITH && EmporiumEntry.Instance != null)
                    Cabinets.GrantStarter(EmporiumEntry.Instance.invElement, "gunsmith start");
            }
            catch (Exception e)
            {
                GunsmithMod.Warn("gunsmith start cabinet: " + e.Message);
            }
        }
    }

    // The retired gunsmith service (network upgrade RETIRED_GUNSMITH): the gunsmith sign, the first repair certificate and
    // a pistol cabinet with his starter kit on the counter (VVittgenstein 2026-11-04, current/006 quotes 35 point 4 and 36; 2026-11-05, current/008 quote 1 point 2 A for the sign and the certificate). The game fires
    // ModHook.OnNetworkUpgradeUnlocked after the upgrade's own action (NetworkUpgrade.Unlock, read 2026-11-04); the
    // gunsmith start sets the upgrade unlocked without that call, so the two do not both give them. The service's own action queues the
    // retired gunsmith and puts his starter kit on the counter, without a sign (NetworkUpgradeList.UnlockActions, read 2026-11-04).
    internal static class RetiredGunsmithService
    {
        internal static void OnUpgradeUnlocked(string id)
        {
            if (id != NetworkUpgrade.RETIRED_GUNSMITH || EmporiumEntry.Instance == null)
                return;
            try
            {
                GunsmithSign.GrantWithService(EmporiumEntry.Instance.backInvinvElement, "retired gunsmith service");
                Cabinets.GrantStarter(EmporiumEntry.Instance.backInvinvElement, "retired gunsmith service");
            }
            catch (Exception e)
            {
                GunsmithMod.Warn("retired gunsmith service: " + e.Message);
            }
        }
    }
}
