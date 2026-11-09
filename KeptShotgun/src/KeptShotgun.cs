using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;

[assembly: System.Reflection.AssemblyTitle("KeptShotgun")]
[assembly: System.Reflection.AssemblyVersion("1.1.2.0")]

namespace KeptShotgun
{
    // Entry point found by the game's ModLoader, which loads mods once per launch and applies the
    // Harmony patches in this assembly (Harmony ID "mod.<manifest ID>") before calling Init and OnEnable.
    public sealed class KeptShotgunMod : IMod
    {
        internal const string ModId = "KeptShotgun";
        internal const string PerkId = "keptshotgun_kept_shotgun";
        internal const string LogPrefix = "[KeptShotgun] ";

        public void Init(ModManifest manifest)
        {
            UnityEngine.Debug.Log(LogPrefix + "v" + manifest.ModVersion + " loaded");
        }

        // A positive starting perk that costs 1 point. mindEditable lets the game's mind reprogrammer add or
        // remove it during a run. The name and description providers are not written into the save
        // (ES3NonSerializable); StartingPerk looks them up through the registered perk by id.
        public void OnEnable()
        {
            ModHelper.AddStartingPerk(ModId, PerkId, 1, StartingPerk.StartingPerkType.POSITIVE,
                Text.PerkName, Text.PerkDescription, null, new string[0], 0, true);
            PerkIcon.Register();
        }

        public void OnDisable()
        {
            ModHelper.RemoveStartingPerks(ModId);
        }

        internal static bool PerkActive()
        {
            PlayerStore store = PlayerStore.Instance;
            return store != null && store.startingPerks != null && StartingPerk.IsPerkActive(PerkId);
        }

        internal static StoreClient CurrentClient()
        {
            PlayerStore store = PlayerStore.Instance;
            return store != null && store.currentClientInstance != null ? store.currentClientInstance.GetClientBlueprint() : null;
        }

        // 1.1.1: the game 1.0.0.12 (buildid 26496118) lets the player aim at story characters that could not be aimed at
        // before: Eleanor and Lou (forceShootable and holsterResumesDialogue), Serpico (holsterResumesDialogue) and
        // Sawyer (forceShootable); only StoreClientUniqueList.SetupEleanor, LouOperativeHelper.SetupLouShootable,
        // SerpicoStoryline.SetupShootable and StoreClientListStory.SetupSawyer set these two fields. Robbery does not
        // apply to them (VVittgenstein 2026-11-07), so the customers that can be robbed are the ones of 1.1.0.
        internal static bool Robbable(StoreClient client)
        {
            return client != null && !client.forceShootable && !client.holsterResumesDialogue;
        }

        internal static void Notify(string message)
        {
            StoreUIManager ui = StoreUIManager.Instance;
            if (ui != null)
                ui.Notify(message, "default");
            UnityEngine.Debug.Log(LogPrefix + message);
        }
    }

    // The perk icon: a raised hand at the centre, a pump shotgun diagonal behind it. The base game's perk icons are white 32x32 or 36x36
    // sprites at 100 pixels per unit that the perk screens tint by perk type (green for positive perks).
    // StartingPerkElement and NewGamePerksUI look the icon up in StartingPerkIconLoader.perkIcons by perk id and
    // fall back to the "default" icon; StartingPerkIconLoader.Start clears and refills that dictionary, so the
    // icon is added when the mod is enabled and again after each Start. The rows were drawn with art/draw_icon.py.
    internal static class PerkIcon
    {
        private static readonly string[] Rows =
        {
            "....................................",
            "....................................",
            "....................................",
            "....................................",
            "...............#................#...",
            "...............##..#...........##...",
            "...............##.##..........##....",
            ".............#.##.##.........##.....",
            "............##.##.##.#......##.#....",
            "............##.##.##.##....##.#.....",
            "............##.##.##.##...######....",
            "........##..##.##.##.##..####.......",
            "........##..##.##.##.##.######......",
            ".........##.###########.###.........",
            ".........##.###########.####........",
            "..........#############.#...........",
            "..........#############.............",
            "...........############.............",
            "............###########.............",
            "............###########.............",
            "............###########.............",
            "............###########.............",
            "............###########.............",
            "............###########.............",
            ".............#########..............",
            "..........##.#########..............",
            ".........###..#######...............",
            "........####..#######...............",
            ".......#####..#######...............",
            "......#####...#######...............",
            "....#######.........................",
            "...#######..........................",
            "....................................",
            "....................................",
            "....................................",
            "....................................",
        };

        private static UnityEngine.Sprite sprite;

        private static UnityEngine.Sprite Get()
        {
            if (sprite != null)
                return sprite;
            int size = Rows.Length;
            var texture = new UnityEngine.Texture2D(size, size, UnityEngine.TextureFormat.RGBA32, false);
            texture.filterMode = UnityEngine.FilterMode.Point;
            texture.wrapMode = UnityEngine.TextureWrapMode.Clamp;
            var pixels = new UnityEngine.Color32[size * size];
            for (int row = 0; row < size; row++)
            {
                string line = Rows[row];
                int y = size - 1 - row;  // texture rows start at the bottom
                for (int x = 0; x < size; x++)
                    pixels[y * size + x] = line[x] == '#' ? new UnityEngine.Color32(255, 255, 255, 255) : new UnityEngine.Color32(255, 255, 255, 0);
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            texture.name = KeptShotgunMod.PerkId;
            texture.hideFlags = UnityEngine.HideFlags.DontUnloadUnusedAsset;
            sprite = UnityEngine.Sprite.Create(texture, new UnityEngine.Rect(0, 0, size, size), new UnityEngine.Vector2(0.5f, 0.5f), 100f);
            sprite.name = KeptShotgunMod.PerkId;
            sprite.hideFlags = UnityEngine.HideFlags.DontUnloadUnusedAsset;
            return sprite;
        }

        internal static void Register()
        {
            try
            {
                Dictionary<string, UnityEngine.Sprite> icons = StartingPerkIconLoader.perkIcons;
                if (icons != null)
                    icons[KeptShotgunMod.PerkId] = Get();
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning(KeptShotgunMod.LogPrefix + "registering the perk icon failed: " + e);
            }
        }
    }

    [HarmonyPatch(typeof(StartingPerkIconLoader), "Start")]
    internal static class PerkIconLoaderPatch
    {
        private static void Postfix()
        {
            PerkIcon.Register();
        }
    }

    // The officer who announces that all augs are eliminated (StoreClientListSecurity.CreateAugAllClearOfficer)
    // says three lines; the end action of the second one calls Shotgun.LockShotgun, which sets
    // PlayerStore.isShootingUnlocked to false and hides the shotgun. With the perk the call is skipped, and the
    // third line ("dialog_sec_aug_all_clear_3", "You won't be needed the gun anymore.") is replaced by
    // OfficerLinePatch. Since the game 1.0.0.12 the second line takes the gun only without Weapon Permit I, and the
    // third line of a player holding that permit is set by the game when it is shown (dialog_sec_aug_all_clear_keep:
    // the permit covers the shotgun); that line is left as it is. The game 1.0.0.12 also calls LockShotgun when Weapon
    // Permit I is revoked (Shotgun.OnGunPermitIRevoked); with the perk that call is skipped as well (VVittgenstein 2026-11-07),
    // see RevocationLinePatch.
    [HarmonyPatch(typeof(Shotgun), nameof(Shotgun.LockShotgun))]
    internal static class LockShotgunPatch
    {
        private static bool Prefix()
        {
            if (!KeptShotgunMod.PerkActive())
                return true;
            UnityEngine.Debug.Log(KeptShotgunMod.LogPrefix + "Shotgun.LockShotgun skipped; the shotgun stays");
            return false;
        }
    }

    // The officer who revokes Weapon Permit I (StoreClientListSecurity.CreateRevokePermitOfficer, the game 1.0.0.12)
    // adds a line asking the player to hand over the shotgun ("dialog_sec_revoke_shotgun") when
    // Shotgun.WouldPermitRevocationTakeGun is true; that method has no other caller. With the perk the shotgun is not
    // taken, so the line is left out (VVittgenstein 2026-11-07).
    [HarmonyPatch(typeof(Shotgun), nameof(Shotgun.WouldPermitRevocationTakeGun))]
    internal static class RevocationLinePatch
    {
        private static void Postfix(ref bool __result)
        {
            if (__result && KeptShotgunMod.PerkActive())
            {
                __result = false;
                UnityEngine.Debug.Log(KeptShotgunMod.LogPrefix + "the permit officer does not ask for the shotgun; it stays");
            }
        }
    }

    // The officer's dialogue is built with LocHelper.GetLocalizedDialogue when the officer is created; this key is
    // used only for that line.
    [HarmonyPatch(typeof(LocHelper), nameof(LocHelper.GetLocalizedDialogue))]
    internal static class OfficerLinePatch
    {
        private const string GunLineKey = "dialog_sec_aug_all_clear_3";

        private static void Postfix(string key, ref string __result)
        {
            if (key == GunLineKey && KeptShotgunMod.PerkActive())
                __result = Text.OfficerKeepGun();
        }
    }


    // The aiming view: clicking the shotgun runs Shotgun.OnClick, which enters it with the base game's transition
    // (StoreUIManager.EnterShootingMode) when CanPickup allows it. Arm.Update, active only while aiming, shoots on
    // the left mouse button (StoreUIManager.Shoot when CanShoot) and holsters on the right one
    // (StoreUIManager.ExitShootingMode; a customer who was not shot then leaves). With the perk the middle button
    // robs the customer at gunpoint. While the customer hands everything over, the left and right buttons are
    // ignored for at most Robbery.MaxSeconds.
    [HarmonyPatch(typeof(Arm), "Update")]
    internal static class ArmUpdatePatch
    {
        private static bool Prefix()
        {
            if (!Robbery.InProgress)
                return true;
            if (Robbery.Expired())
            {
                Robbery.Abort("the customer's line did not finish");
                return true;
            }
            return !(UnityEngine.Input.GetMouseButtonDown(0) || UnityEngine.Input.GetMouseButtonDown(1));
        }

        private static void Postfix()
        {
            if (!UnityEngine.Input.GetMouseButtonDown(2) || Robbery.InProgress || !KeptShotgunMod.PerkActive())
                return;
            StoreUIManager ui = StoreUIManager.Instance;
            if (ui == null || !ui.isShootingMode || !ui.CanShoot() || !KeptShotgunMod.Robbable(KeptShotgunMod.CurrentClient()))
                return;
            Robbery.Start();
        }
    }

    // The hints at the bottom of the aiming view (StoreUIManager.shootingUI, "_ShootingUI" in the scene) are two
    // children, each a mouse icon with a "Text (TMP)" child: "Shoot" (left button) and "Holster" (right button).
    // With the perk a copy of the second one, with a middle-button icon and the text "Rob", is placed between
    // them; without the perk the base game's layout is restored and the copy is hidden. The copy loses the two
    // localization components (LocalizeStringEvent would put "Holster" back) and takes its font from the
    // original text, which TMPLocalizer sets for the current language.
    internal static class AimHint
    {
        private const string CopyName = "KeptShotgunRobHint";
        private static readonly float[] PerkLayout = { -250f, -55f, 140f };  // left, middle, right icon x
        private static float[] baseLayout;
        private static UnityEngine.Sprite middleIcon;

        private static readonly string[] MiddleIconRows =
        {
            ".......#########.......",
            ".....##----#----##.....",
            "....#------#------#....",
            "...#------###------#...",
            "..#------#OOO#------#..",
            ".#-------#OOO#-------#.",
            ".#-------#OOO#-------#.",
            "#--------#OOO#--------#",
            "#--------#OOO#--------#",
            "#--------#OOO#--------#",
            "#---------###---------#",
            "#----------#----------#",
            "#----------#----------#",
            "#----------#----------#",
            "#----------#----------#",
            "#----------#----------#",
            "#######################",
            "#---------------------#",
            "#---------------------#",
            "#---------------------#",
            "#---------------------#",
            "#---------------------#",
            "#---------------------#",
            "#---------------------#",
            "#---------------------#",
            "#---------------------#",
            "#---------------------#",
            "#---------------------#",
            "#---------------------#",
            "#---------------------#",
            "#---------------------#",
            ".#-------------------#.",
            ".#-------------------#.",
            "..#-----------------#..",
            "...#---------------#...",
            "....#-------------#....",
            ".....##---------##.....",
            ".......#########.......",
        };

        // The colours of the base game's mouse_icon_left / mouse_icon_right: body, pressed button, outline.
        private static UnityEngine.Color32 Colour(char c)
        {
            switch (c)
            {
                case '#': return new UnityEngine.Color32(41, 34, 52, 255);
                case '-': return new UnityEngine.Color32(224, 208, 155, 255);
                case 'O': return new UnityEngine.Color32(204, 119, 85, 255);
                default: return new UnityEngine.Color32(41, 34, 52, 0);
            }
        }

        private static UnityEngine.Sprite MiddleIcon()
        {
            if (middleIcon != null)
                return middleIcon;
            int width = MiddleIconRows[0].Length, height = MiddleIconRows.Length;
            var texture = new UnityEngine.Texture2D(width, height, UnityEngine.TextureFormat.RGBA32, false);
            texture.filterMode = UnityEngine.FilterMode.Point;
            texture.wrapMode = UnityEngine.TextureWrapMode.Clamp;
            var pixels = new UnityEngine.Color32[width * height];
            for (int row = 0; row < height; row++)
            {
                int y = height - 1 - row;  // texture rows start at the bottom
                for (int x = 0; x < width; x++)
                    pixels[y * width + x] = Colour(MiddleIconRows[row][x]);
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            texture.name = CopyName;
            texture.hideFlags = UnityEngine.HideFlags.DontUnloadUnusedAsset;
            middleIcon = UnityEngine.Sprite.Create(texture, new UnityEngine.Rect(0, 0, width, height), new UnityEngine.Vector2(0.5f, 0.5f), 100f);
            middleIcon.name = CopyName;
            middleIcon.hideFlags = UnityEngine.HideFlags.DontUnloadUnusedAsset;
            return middleIcon;
        }

        internal static void OnEnterShootingMode()
        {
            StoreUIManager ui = StoreUIManager.Instance;
            if (ui != null && ui.shootingUI != null)
                ModHelper.StartCoroutine(KeptShotgunMod.ModId, ApplyWhenShown(ui));
        }

        // The base game activates shootingUI once its transition into the aiming view has finished.
        private static IEnumerator ApplyWhenShown(StoreUIManager ui)
        {
            float deadline = UnityEngine.Time.unscaledTime + 3f;
            while (ui != null && ui.isShootingMode && ui.shootingUI != null && !ui.shootingUI.activeInHierarchy && UnityEngine.Time.unscaledTime < deadline)
                yield return null;
            try
            {
                Apply(ui);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning(KeptShotgunMod.LogPrefix + "updating the aiming hints failed: " + e);
            }
        }

        private static void Apply(StoreUIManager ui)
        {
            if (ui == null || ui.shootingUI == null)
                return;
            UnityEngine.Transform root = ui.shootingUI.transform;
            UnityEngine.Transform copy = root.Find(CopyName);
            UnityEngine.RectTransform shoot = null, holster = null;
            for (int i = 0; i < root.childCount; i++)
            {
                UnityEngine.Transform child = root.GetChild(i);
                if (child == copy)
                    continue;
                if (shoot == null) shoot = child as UnityEngine.RectTransform;
                else if (holster == null) holster = child as UnityEngine.RectTransform;
            }
            if (shoot == null || holster == null)
                return;
            if (baseLayout == null)
                baseLayout = new[] { shoot.anchoredPosition.x, holster.anchoredPosition.x };

            // Without the perk, and for the story characters robbery does not apply to, the base game's two hints.
            bool perk = KeptShotgunMod.PerkActive() && KeptShotgunMod.Robbable(KeptShotgunMod.CurrentClient());
            if (!perk)
            {
                SetX(shoot, baseLayout[0]);
                SetX(holster, baseLayout[1]);
                if (copy != null)
                    copy.gameObject.SetActive(false);
                return;
            }

            if (copy == null)
            {
                UnityEngine.GameObject made = UnityEngine.Object.Instantiate(holster.gameObject, root, false);
                made.name = CopyName;
                foreach (UnityEngine.Localization.Components.LocalizeStringEvent e in made.GetComponentsInChildren<UnityEngine.Localization.Components.LocalizeStringEvent>(true))
                {
                    e.enabled = false;
                    UnityEngine.Object.Destroy(e);
                }
                foreach (TMPLocalizer l in made.GetComponentsInChildren<TMPLocalizer>(true))
                {
                    l.enabled = false;
                    UnityEngine.Object.Destroy(l);
                }
                UnityEngine.UI.Image icon = made.GetComponent<UnityEngine.UI.Image>();
                if (icon != null)
                    icon.sprite = MiddleIcon();
                copy = made.transform;
            }

            TMPro.TextMeshProUGUI source = holster.GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
            TMPro.TextMeshProUGUI text = copy.GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
            if (text != null)
            {
                if (source != null)
                {
                    text.font = source.font;
                    text.fontSharedMaterial = source.fontSharedMaterial;
                    text.fontSize = source.fontSize;
                    text.color = source.color;
                }
                text.text = Text.RobHint();
            }
            SetX(shoot, PerkLayout[0]);
            SetX((UnityEngine.RectTransform)copy, PerkLayout[1]);
            SetX(holster, PerkLayout[2]);
            copy.gameObject.SetActive(true);
        }

        private static void SetX(UnityEngine.RectTransform t, float x)
        {
            t.anchoredPosition = new UnityEngine.Vector2(x, t.anchoredPosition.y);
        }
    }

    [HarmonyPatch(typeof(StoreUIManager), nameof(StoreUIManager.EnterShootingMode))]
    internal static class EnterShootingModePatch
    {
        private static void Postfix()
        {
            AimHint.OnEnterShootingMode();
        }
    }

    // Robbery at gunpoint: the customer says one line, and when it has been shown (its end action), the
    // customer's remaining budget (StoreClient.GetBudget when useClientBudget is set; the negotiation panel
    // compares prices with it directly) goes to the player, and the customer's items on the front counter and in
    // the back room, which PlayerStore.RemoveNotOwnedItemFromTable would destroy when the customer leaves, become
    // the player's (the same ownership change EmporiumEntry.TransferOwnershipFrontInv and TransferOwnershipBackInv
    // make for an accepted barter; they are not marked stolen). The customer's faction loses 20 reputation, the
    // value taken is recorded as a ROBBERY crime, the customer leaves through PlayerStore.DismissCurrentClient and
    // the gun is holstered (ExitShootingMode; its own DismissCurrentClient returns when no customer is present).
    // An aug that leaves this way counts as left alive, as when it is dismissed in the base game.
    internal static class Robbery
    {
        internal const string CrimeId = "ROBBERY";
        internal const float MaxSeconds = 8f;
        private const int ReputationLoss = 20;

        internal static bool InProgress;
        private static float startedAt;

        internal static bool Expired()
        {
            return UnityEngine.Time.unscaledTime - startedAt > MaxSeconds;
        }

        internal static void Start()
        {
            StoreClient client = KeptShotgunMod.CurrentClient();
            DialogUIManager dialog = DialogUIManager.Instance;
            if (client == null || dialog == null)
                return;
            InProgress = true;
            startedAt = UnityEngine.Time.unscaledTime;
            Dialogue reply = new Dialogue().SetText(client.displayName, Text.RobReply()).SetEndAction(Finish);
            if (!dialog.StartDialogue(reply, true))
                Finish();
        }

        internal static void Abort(string reason)
        {
            InProgress = false;
            UnityEngine.Debug.LogWarning(KeptShotgunMod.LogPrefix + "robbery stopped: " + reason);
        }

        private static void Finish()
        {
            if (!InProgress)
                return;
            try
            {
                Execute();
            }
            finally
            {
                InProgress = false;
                StoreUIManager ui = StoreUIManager.Instance;
                if (ui != null && ui.isShootingMode)
                    ui.ExitShootingMode();
            }
        }

        internal static int CashOf(StoreClient client)
        {
            return client != null && client.useClientBudget ? Math.Max(0, client.GetBudget()) : 0;
        }

        internal static List<GameItem> CustomerGoods()
        {
            var goods = new List<GameItem>();
            EmporiumEntry entry = EmporiumEntry.Instance;
            if (entry == null)
                return goods;
            AddNotOwned(entry.frontInvinvElement, goods);
            AddNotOwned(entry.backInvinvElement, goods);
            return goods;
        }

        private static void AddNotOwned(GameInventory inventory, List<GameItem> goods)
        {
            if (inventory == null || inventory.childItems == null)
                return;
            foreach (GameItem item in inventory.childItems)
            {
                if (item != null && !GeneralHelper.IsItemOwned(item))
                    goods.Add(item);
            }
        }

        internal static long ValueOf(List<GameItem> goods)
        {
            long total = 0;
            foreach (GameItem item in goods)
                total += Math.Max(0L, item.GetNegociatedValue());
            return total;
        }

        private static void Execute()
        {
            try
            {
                PlayerStore store = PlayerStore.Instance;
                StoreClient client = KeptShotgunMod.CurrentClient();
                if (store == null || client == null)
                    return;

                int cash = CashOf(client);
                List<GameItem> goods = CustomerGoods();
                long value = cash + ValueOf(goods);

                // Same clean-up as PlayerStore.OnItemBought after a purchase.
                foreach (GameItem item in goods)
                {
                    GeneralHelper.SetItemOwned(item, true);
                    item.RemoveFeatureRelatedToEvent();
                    item.RemoveFeatureTemporaryBuying();
                }
                if (cash > 0)
                {
                    client.OverrideBudget(0);
                    store.ModCash(cash, true);
                }

                // ModCurrentClientReputation needs the customer to still be current.
                StoreReputation.ModCurrentClientReputation(-ReputationLoss, true);

                // SecData.CommitCrime scales the amount for the Intimidating and Blabbermouth perks; the
                // difference of the totals is what was recorded.
                int recorded = 0;
                SecData sec = store.secData;
                if (sec != null && value > 0)
                {
                    int before = sec.GetTotalCrimeValue();
                    sec.CommitCrime(CrimeId, (int)Math.Min(int.MaxValue, value));
                    recorded = sec.GetTotalCrimeValue() - before;
                }

                string id = client.identifier;
                store.DismissCurrentClient(false, true, true);
                UnityEngine.Debug.Log(KeptShotgunMod.LogPrefix + "robbed '" + id + "': cash " + cash + ", items " + goods.Count
                    + ", value " + value + ", crime recorded " + recorded + ", reputation -" + ReputationLoss);
                KeptShotgunMod.Notify(Text.Robbed(cash, goods.Count, recorded));
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning(KeptShotgunMod.LogPrefix + "robbery failed: " + e);
            }
        }
    }

    // Threaten in the bargaining panel (BargainUIManager.OnBargainButton calls ThreatenAug; the button is shown
    // once augs are introduced). An aug accepts in the base game: threatenDiscount (20) on the customer's goods,
    // threatenMarkup (20) on the player's goods, trade reputation multipliers 0 (RecomputeTradeRepMultiplier), and
    // a reluctant acceptance line. On a regular customer the base game calls HandleFalseThreaten: -10 reputation
    // and the customer leaves, or -7 and the customer stays without a discount (50 percent each). With the perk
    // and the shotgun kept, a regular customer leaves half of the time (-10, one of the base game's bargain leave
    // lines, then PlayerStore.DismissCurrentClient), and otherwise loses 4 reputation and accepts the same terms
    // as an aug: ThreatenAug runs with StoreClient.isAug set, since it decides success from the flag, and the flag
    // is restored before CreateAcceptReluctantDialog picks its lines (AcceptReluctantDialogPatch) and after
    // ThreatenAug returns (the finalizer).
    [HarmonyPatch(typeof(BargainUIManager), nameof(BargainUIManager.ThreatenAug))]
    internal static class ThreatenPatch
    {
        private const int LeaveChance = 50;
        private const int LeaveReputationLoss = 10;
        private const int AcceptReputationLoss = 4;

        internal static bool Coercing;
        internal static bool OriginalIsAug;
        private static StoreClient coerced;

        internal static bool Applies()
        {
            PlayerStore store = PlayerStore.Instance;
            return store != null && store.isShootingUnlocked && KeptShotgunMod.PerkActive();
        }

        private static bool Prefix(BargainUIManager __instance, ref bool ___threatenOffered)
        {
            Coercing = false;
            coerced = null;
            if (!Applies() || !__instance.CanThreatenAug())
                return true;
            StoreClient client = KeptShotgunMod.CurrentClient();
            if (client == null || client.isAug)
                return true;

            if (RNG.Roll(LeaveChance))
            {
                ___threatenOffered = true;
                StoreReputation.ModReputation(client.clientFaction, -LeaveReputationLoss, true);
                string line = LocHelper.GetLocalizedDialogue("dialog_bargain_leave_" + RNG.GetRandomInt(1, 3).ToString(CultureInfo.InvariantCulture), new object[0]);
                DialogUIManager.Instance.StartDialogue(new Dialogue().SetText(client.displayName, line).SetEndAction(LeaveNow), true);
                UnityEngine.Debug.Log(KeptShotgunMod.LogPrefix + "'" + client.identifier + "' refused the threat and leaves; reputation -" + LeaveReputationLoss);
                return false;
            }

            StoreReputation.ModReputation(client.clientFaction, -AcceptReputationLoss, true);
            OriginalIsAug = client.isAug;
            coerced = client;
            Coercing = true;
            client.isAug = true;
            UnityEngine.Debug.Log(KeptShotgunMod.LogPrefix + "'" + client.identifier + "' gave in to the threat; reputation -" + AcceptReputationLoss);
            return true;
        }

        private static Exception Finalizer(Exception __exception)
        {
            Restore();
            return __exception;
        }

        internal static void Restore()
        {
            if (!Coercing)
                return;
            if (coerced != null)
                coerced.isAug = OriginalIsAug;
            coerced = null;
            Coercing = false;
        }

        private static void LeaveNow()
        {
            PlayerStore store = PlayerStore.Instance;
            if (store != null && store.currentClientInstance != null)
                store.DismissCurrentClient(false, true, true);
        }
    }

    [HarmonyPatch(typeof(BargainUIManager), "CreateAcceptReluctantDialog")]
    internal static class AcceptReluctantDialogPatch
    {
        private static void Prefix()
        {
            ThreatenPatch.Restore();
        }
    }

    // The explanation of the Threaten button (key "bargain_ui_threathen_info", read through LocHelper.GetLocalizedUI)
    // says that a threat on a regular customer always fails; with the perk and the shotgun kept it gives the
    // outcomes above. It is the same for every customer, so it does not tell an aug apart.
    [HarmonyPatch(typeof(LocHelper), nameof(LocHelper.GetLocalizedUI))]
    internal static class ThreatenInfoPatch
    {
        private const string InfoKey = "bargain_ui_threathen_info";

        private static void Postfix(string key, ref string __result)
        {
            if (key == InfoKey && ThreatenPatch.Applies())
                __result = Text.ThreatenInfo();
        }
    }

    // SecData.GetCrimeString names the known crime types and returns the "other" text for the rest.
    [HarmonyPatch(typeof(SecData), nameof(SecData.GetCrimeString))]
    internal static class CrimeNamePatch
    {
        private static void Postfix(string crimeID, ref string __result)
        {
            if (crimeID == Robbery.CrimeId)
                __result = Text.RobberyCrime();
        }
    }

    // Texts shown in the game: Chinese when the game language is Chinese, Russian when it is Russian, English otherwise.
    internal static class Text
    {
        private static string LanguageCode()
        {
            UnityEngine.Localization.Locale locale = ModHelper.GetCurrentLocale();
            return locale != null ? locale.Identifier.Code : null;
        }

        private static string T(string zh, string en, string ru)
        {
            string code = LanguageCode();
            if (code != null && code.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
                return zh;
            if (code != null && code.StartsWith("ru", StringComparison.OrdinalIgnoreCase))
                return ru;
            return en;
        }

        private static string N(long value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        internal static string PerkName()
        {
            return T("持枪抢劫与威胁议价", "Armed Robbery & Intimidation", "Вооружённое ограбление и запугивание");
        }

        internal static string PerkDescription()
        {
            return T("改造体被消灭后，你被允许留着霰弹枪。举枪时按鼠标中键可以抢劫顾客；讲价时用“威胁”，普通顾客也可能答应打折。",
                "You are allowed to keep the shotgun after the augs are eliminated. While aiming, the middle mouse button robs the customer; when bargaining, Threaten can get a regular customer to give you a discount.",
                "После уничтожения аугов вам разрешают оставить дробовик. Когда вы целитесь, средняя кнопка мыши грабит клиента; в переговорах вариант «Угрожать» может заставить сделать скидку и обычного клиента.");
        }

        internal static string OfficerKeepGun()
        {
            return T("这把枪你可以留着。", "You can keep the gun.", "Это оружие можете оставить себе.");
        }

        internal static string RobHint()
        {
            return T("抢劫", "Rob", "Ограбить");
        }

        internal static string RobReply()
        {
            return T("好、好，别开枪……都给你。", "Okay, okay, don't shoot... Take it all.", "Ладно, ладно, не стреляйте... Забирайте всё.");
        }

        internal static string ThreatenInfo()
        {
            return T("用枪威胁这位顾客，要求在这次交易中拿到更好的价格。改造体会接受；普通顾客一半会接受（声望 -4），一半会离开（声望 -10）。",
                "Threaten this customer with your gun to get better prices in this trade. An aug accepts; a regular customer accepts half of the time (-4 reputation) and leaves otherwise (-10 reputation).",
                "Пригрозить клиенту оружием, чтобы получить в этой сделке цены получше. Ауг соглашается; обычный клиент соглашается в половине случаев (репутация -4), а в остальных уходит (репутация -10).");
        }

        internal static string Robbed(int cash, int items, int crime)
        {
            return T("抢到 " + N(cash) + " 信用点和 " + N(items) + " 件货，抢劫罪 +" + N(crime) + "。",
                "Took " + N(cash) + " credits and " + N(items) + " item(s). Robbery +" + N(crime) + ".",
                "Добыча: " + N(cash) + " Кр. и товаров: " + N(items) + ". Ограбление +" + N(crime) + ".");
        }

        internal static string RobberyCrime()
        {
            return T("抢劫", "Robbery", "Ограбление");
        }
    }
}
