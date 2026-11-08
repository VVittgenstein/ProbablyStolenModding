using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace GunsmithExpansion
{
    // The in-game manuals (0.7 list item 38; current/T-007; design in experiments/2026-11-06-manual/design.md). Two items, the Gunsmithing Manual
    // and the Firearms Catalog, open books that the game reads from Books\*.json of the enabled mods at start-up (PaperBookRegistry.LoadMod), one
    // book per language (design section 2.2): gse_manual_zh, gse_manual_en and gse_manual_ru, gse_catalog_zh, _en and _ru, written by
    // tools/build_manual.py. Activated, an item opens the book of the game's language: Chinese, Russian, English for every other language; a book
    // not loaded falls back to the English one, then to the Chinese one.
    // The items are built as the game's three spiral notebook guides (the plasma cartridge, Ribwich and cybernetic hand appraisal guides,
    // MiscItemDirectory.FuelGuide, RibwichGuide and HandGuide, read 2026-11-07 in game 1.0.0.12), whose picture the manuals' own pictures of the
    // twentieth batch recolour (tools/build_redraw_batch20.py): the document type and tag, "important" as all eight of the game's manuals, the guide
    // paper tag, the paper sheet sounds (BookHelper.InitPaperGuide), value 50, used only by a player who can read (HealthData.CanRead, false with the
    // illiterate start, as for the game's manuals). The value is that of these three guides and of the Chemistry Manual (IL_0078 in FuelGuide,
    // RibwichGuide, HandGuide and ChemistryGuide); the other four manuals are worth 25 (WaterGuide, HydroponicGuide, IngotGuide, WineGuide).
    // Until Textures\Items\gse_manual.png and gse_catalog.png are there the items show two of those guides' pictures, items_misc/manual5 and
    // items_misc/manual4, which differ in colour.
    // Where they come from (VVittgenstein 2026-11-07 20:20, current/T-007 quote 15, final-strategy 281):
    // The gunsmith start puts both in the store's inventory, where the game's starts put their
    // manuals (NewGameData.HandleInitialItem: EmporiumTryAdd(invElement, ...) for the water, hydroponic, metallurgy and chemistry manuals); buying
    // the retired gunsmith service puts both on the counter top, where this mod gives the gunsmith sign and the repair certificate
    // (RetiredGunsmithService) and the game's Retired Chemist service puts its manual (NetworkUpgradeList, backInvinvElement); on a visit of the
    // retired gunsmith each of the two that the store owns nowhere goes on his counter for sale, as Dr. Jackson sells the guides the store lacks
    // (StoreClientList.AddIfNotOwn over EmporiumEntry.GetAllOwnedItems). A save gets none on loading.
    // All of it is hooked by the Harmony patches at the end of this file, so that no other file of the mod changes while T-005 and T-006 change them.
    internal static class Manual
    {
        internal sealed class Book
        {
            // The item's identifier, the name of its picture in Textures\Items, and the start of its books' identifiers.
            internal readonly string ItemId;
            internal readonly string NameZh;
            internal readonly string NameEn;
            internal readonly string DescriptionZh;
            internal readonly string DescriptionEn;
            // The picture in the game's atlas Items/items_misc while the item's own picture is missing.
            internal readonly string GameSprite;
            internal bool Registered;

            internal Book(string itemId, string nameZh, string nameEn, string descriptionZh, string descriptionEn, string gameSprite)
            {
                ItemId = itemId;
                NameZh = nameZh;
                NameEn = nameEn;
                DescriptionZh = descriptionZh;
                DescriptionEn = descriptionEn;
                GameSprite = gameSprite;
            }

            internal string BookId(string language)
            {
                return ItemId + "_" + language;
            }
        }

        // The names are those of the design (section 3); the Russian texts come from src/TextRu.cs by the English ones (Text.Pick).
        internal static readonly Book GunsmithingManual = new Book("gse_manual", "枪匠手册", "Gunsmithing Manual",
            "一本实用手册，收录了修枪、维修证书、测绘与打印、订单等枪匠工作的规则、流程与价格。",
            "A handy manual on the gunsmith's trade: the rules, procedures and prices of gun repair, repair certificates, surveying and printing, orders and more.",
            "manual5");
        internal static readonly Book FirearmsCatalog = new Book("gse_catalog", "枪械目录", "Firearms Catalog",
            "一本实用目录，列出全部枪械型号与可修复度、配件、口径与弹药，并说明枪械名词的含义。",
            "A handy catalog that lists every gun model with its repairability, the gun mods, the calibers and ammunition, and explains what the gun terms mean.",
            "manual4");
        internal static readonly Book[] Books = { GunsmithingManual, FirearmsCatalog };

        internal const int Value = 50;
        // The action of the contents' entries and of the contents button on every page: gse_goto_<page>, pages counted from 1 (tools/build_manual.py).
        internal const string JumpPrefix = "gse_goto_";

        private static readonly HashSet<string> hooked = new HashSet<string>();

        internal static void Register(ModItemDirectory directory)
        {
            if (directory == null)
                return;
            foreach (Book book in Books)
            {
                Book b = book;
                try
                {
                    bool picture = ModHelper.ModFileExists(GunsmithMod.ModId, "Textures/Items/" + b.ItemId + ".png");
                    directory.Add(b.ItemId, () => Create(directory, b, picture));
                    b.Registered = true;
                    GunsmithMod.Log("registered " + b.ItemId + (picture ? "" : ", with the game's picture items_misc/" + b.GameSprite + " until Textures/Items/" + b.ItemId + ".png is there"));
                }
                catch (Exception e)
                {
                    GunsmithMod.Warn("registering " + b.ItemId + " failed: " + e.Message);
                }
            }
        }

        private static GameItem Create(ModItemDirectory directory, Book book, bool picture)
        {
            GameItem item = picture ? directory.Empty().SetSpriteAndShapeFromMod(GunsmithMod.ModId, book.ItemId)
                : directory.Empty().SetSpriteAndShape("Items/items_misc", book.GameSprite);
            item.name = Text.Pick(book.NameZh, book.NameEn);
            item.shortDescription = Text.Pick(book.DescriptionZh, book.DescriptionEn);
            item.EnableTag("DOCUMENT", false);
            item.SetGameItemType("DOCUMENT");
            item.EnableTag("IMPORTANT_TAG", false);
            item.EnableTag("GUIDE_PAPER_TAG", false);
            BookHelper.InitPaperGuide(item);
            item.unitValue = Value;
            item.canActivateSlotItemFunc = (it, inventory, marker) => HealthData.CanRead();
            item.mayActivateSlotItemFunc = (it, inventory, marker) => HealthData.CanRead();
            item.onActivateSlotItemFunc = (it, inventory, marker) => Open(book);
            return item;
        }

        // The game's four difficulties by the rules the manual's numbers depend on (Difficulty.Vanilla, game 1.0.0.12; the same table as
        // DIFFICULTIES in tools/manualtables/book_manual.py, which checks these arrays against it and it against the IL). The Gunsmithing Manual
        // is made once for each, with only its numbers and no difficulty named (gse_manual_<language>_<difficulty>), and once with all four
        // (gse_manual_<language>) (VVittgenstein 2026-11-08 02:26, current/T-007
        // quote 22 point 5; 02:48, quote 23; final-strategy 296). A save whose rules match none of the four, as one of a difficulty another
        // mod registers (Difficulty.Register) or changes (ModHelper.ModifyDifficulty), gets the book with all four.
        private static readonly string[] Difficulties = { "cozy", "normal", "hard", "challenge" };
        private static readonly int[] RetiredSupplierMarkup = { 0, 15, 30, 45 };
        private static readonly int[] MerchantDiscount = { 10, 15, 20, 25 };
        private static readonly float[] AppraisalProgressGain = { 1.25f, 1f, 1f, 0.75f };
        private static readonly int[] AppraisalJoinCost = { 1, 1, 2, 3 };
        private static readonly int[] RetailMarkup = { 20, 15, 0, 0 };

        // The difficulty of the save among the four above, or null.
        internal static string DifficultyOfSave()
        {
            DifficultyRules rules = Difficulty.Rules;
            if (rules == null)
                return null;
            for (int i = 0; i < Difficulties.Length; i++)
            {
                if (rules.retiredSupplierMarkup == RetiredSupplierMarkup[i] && rules.merchantDiscount == MerchantDiscount[i]
                    && rules.appraisalProgressGainMultiplier == AppraisalProgressGain[i] && rules.appraisalJoinCostMultiplier == AppraisalJoinCost[i]
                    && rules.retailMarkup == RetailMarkup[i])
                    return Difficulties[i];
            }
            return null;
        }

        // The book of the game's language, else the English one, else the Chinese one, among the books the game has loaded; in each language
        // first the one made for the save's difficulty, which only the Gunsmithing Manual has.
        internal static string LoadedBookId(Book book)
        {
            List<string> loaded = ModHelper.GetPaperBookIds(GunsmithMod.ModId);
            if (loaded == null)
                return null;
            string language = Text.Chinese() ? "zh" : Text.Russian() ? "ru" : "en";
            string difficulty = DifficultyOfSave();
            foreach (string code in new[] { language, "en", "zh" })
            {
                string id = book.BookId(code);
                if (difficulty != null && loaded.Contains(id + "_" + difficulty))
                    return id + "_" + difficulty;
                if (loaded.Contains(id))
                    return id;
            }
            return null;
        }

        internal static void Open(Book book)
        {
            try
            {
                string id = LoadedBookId(book);
                if (id == null)
                {
                    GunsmithMod.Warn("no book of " + book.ItemId + " is loaded (Books/" + book.BookId("zh") + ".json and the others)");
                    return;
                }
                // The paper interface calls this when it builds the book's window and after each rebuild, not when a hidden window is shown again
                // (PaperBookRegistry.Open, FireOpened); it keeps a book's callbacks for the launch, so each book gets this once.
                if (!hooked.Contains(id))
                {
                    ModHelper.OnPaperOpened(GunsmithMod.ModId, id, OnOpened);
                    hooked.Add(id);
                }
                if (ModHelper.OpenPaper(GunsmithMod.ModId, id) == null)
                    GunsmithMod.Warn(id + " could not be opened");
            }
            catch (Exception e)
            {
                GunsmithMod.Warn("opening " + book.ItemId + ": " + e.Message);
            }
        }

        private static void OnOpened(PaperUI ui)
        {
            if (ui == null)
                return;
            try
            {
                AddJumps(ui);
            }
            catch (Exception e)
            {
                GunsmithMod.Warn("contents buttons of " + ui.Id + ": " + e.Message);
            }
            try
            {
                ManualLook.Apply(ui);
            }
            catch (Exception e)
            {
                GunsmithMod.Warn("the game's manual window pictures for " + ui.Id + ": " + e.Message);
            }
        }

        // PaperUI.FlipTo takes the page's index from 0: it shows pageObjects[index] and writes CurrentPage + 1 in the page number
        // (PaperUI.FlipTo, ShowPage, game 1.0.0.12). The paper interface clears these actions when it rebuilds the window (ResetBindings)
        // and calls OnOpened again.
        private static void AddJumps(PaperUI ui)
        {
            int pages = ui.PageCount;
            for (int page = 1; page <= pages; page++)
            {
                int index = page - 1;
                ui.OnAction(JumpPrefix + page, () => ui.FlipTo(index));
            }
        }

        // Both books into a place of the store. An item with no room there goes to an owned hopper lying in it, the after-hours inventory while the player
        // is out, the counter top, the inventory, the display case or the front counter, the first with room, and is destroyed when none has
        // (GraphUtils.EmporiumTryAdd).
        internal static void Give(GraphNodeStorage place, string why)
        {
            foreach (Book book in Books)
            {
                if (!book.Registered)
                    continue;
                GraphUtils.EmporiumTryAdd(place, DirectoryMaster.Item(book.ItemId, true));
                GunsmithMod.Log(book.ItemId + " given (" + why + ")");
            }
        }

        // The retired gunsmith's visit: each book the store owns nowhere (the inventory, the counters, the display case, the after-hours inventory and the
        // other places of EmporiumEntry.GetAllOwnedItems, as Dr. Jackson checks) goes on the front counter for sale. The game destroys what the front
        // counter cannot take (PlayerStore.TryAddDirectSellingItemToTable: TryAcceptAllOrDestroy; so before game 1.0.0.12 too, current/T-006), so the books
        // go before all his other goods (RetiredGunsmithManualPostfix), and without room there on the counter top (Sellers.Offer; VVittgenstein 2026-11-07 22:35 and
        // 23:57, current/T-006 quotes 24 and 25). Runs inside the end action of his greeting, which puts out his stock after it: an exception here would
        // stop the stock.
        internal static void OfferMissing()
        {
            try
            {
                if (PlayerStore.Instance == null || EmporiumEntry.Instance == null)
                    return;
                var owned = new HashSet<string>();
                foreach (GameItem item in EmporiumEntry.Instance.GetAllOwnedItems(false, true))
                {
                    if (item != null && item.identifier != null)
                        owned.Add(item.identifier);
                }
                foreach (Book book in Books)
                {
                    if (!book.Registered || owned.Contains(book.ItemId))
                        continue;
                    bool placed = Sellers.Offer(book.ItemId);
                    GunsmithMod.Log("retired gunsmith: " + book.ItemId + (placed ? " on the counter for sale" : " not offered, neither counter has room"));
                }
            }
            catch (Exception e)
            {
                GunsmithMod.Warn("manuals offered by the retired gunsmith: " + e.Message);
            }
        }
    }

    // The game's multi-page manual window as the books' frame (design.md section 2.3 point 2; interface-paperbook.md section 9.3, "做法 B"). The books
    // name the game's pictures (notebook_manual, paper_buttons_close1 and the others), which the paper interface looks up in a catalog that game 1.0.0.12
    // does not have (PaperAssets.Resolve, Resources/PaperUI/PaperAssetCatalog; interface-paperbook.md section 4.3, not yet seen in the game): the window
    // would show a plain colour and the buttons blue squares (PaperUI.Build, BuildButton). When a book's window is built this takes the pictures of the
    // game's window (MultiPageUIManager, whose fields are public): its background for the window, its close, zoom (WindowButton) and page buttons for
    // the buttons of the book's chrome, matched by their actions, with the pictures those buttons swap to when pointed at and pressed (paper_buttons1
    // to 4 and the like; read from level1 on 2026-11-07), the colour set back to white; the page number takes the colour of the game's. Without the
    // game's window, or without a picture of it, the book keeps what the paper interface drew.
    internal static class ManualLook
    {
        internal static void Apply(PaperUI ui)
        {
            MultiPageUIManager game = MultiPageUIManager.Instance;
            if (game == null || game.panel == null || ui.Root == null || ui.Data == null || ui.Data.chrome == null)
                return;
            CopyPicture(game.panel, ui.Root.GetComponent<UnityEngine.UI.Image>());
            foreach (PaperElementData element in ui.Data.chrome)
            {
                PaperUI.Binding binding = element != null ? ui.GetBinding(element) : null;
                if (binding == null)
                    continue;
                switch (element.action)
                {
                    case "close":
                        CopyButton(Child(game.panel, "CloseButton"), binding);
                        break;
                    case "zoom":
                        CopyButton(Child(game.panel, "WindowButton"), binding);
                        break;
                    case "flip_left":
                        CopyButton(game.buttonLeft, binding);
                        break;
                    case "flip_right":
                        CopyButton(game.buttonRight, binding);
                        break;
                    case "page_indicator":
                        if (binding.text != null && game.pageNumberText != null)
                            binding.text.color = game.pageNumberText.color;
                        break;
                }
            }
        }

        private static GameObject Child(GameObject parent, string name)
        {
            Transform child = parent.transform.Find(name);
            return child != null ? child.gameObject : null;
        }

        private static bool CopyPicture(GameObject from, UnityEngine.UI.Image to)
        {
            UnityEngine.UI.Image picture = from != null ? from.GetComponent<UnityEngine.UI.Image>() : null;
            if (picture == null || picture.sprite == null || to == null)
                return false;
            to.sprite = picture.sprite;
            to.type = picture.type;
            to.color = Color.white;
            return true;
        }

        private static void CopyButton(GameObject from, PaperUI.Binding to)
        {
            if (!CopyPicture(from, to.image))
                return;
            UnityEngine.UI.Button button = from.GetComponent<UnityEngine.UI.Button>();
            if (button == null || to.button == null)
                return;
            to.button.colors = button.colors;
            to.button.spriteState = button.spriteState;
            to.button.transition = button.transition;
        }
    }

    // The items join this mod's other items when the store's item directories start (Items.OnItemDirectoryInit, which GunsmithMod.OnEnable hooks to
    // ModHook.OnModItemDirectoryInit), in the same directory, so a saved manual is rebuilt from its identifier on loading.
    [HarmonyPatch(typeof(Items), nameof(Items.OnItemDirectoryInit))]
    internal static class ManualItemsPostfix
    {
        private static void Postfix(ModItemDirectory directory)
        {
            Manual.Register(directory);
        }
    }

    // The gunsmith start, where GunsmithStartSignPatch gives the sign, the certificate and the pistol cabinet; the order of the two postfixes does not matter.
    [HarmonyPatch(typeof(NewGameData), "HandleInitialItem")]
    internal static class ManualStartPostfix
    {
        private static void Postfix()
        {
            try
            {
                if (NewGameData.Instance != null && NewGameData.Instance.startType == NewGameData.StartType.GUNSMITH && EmporiumEntry.Instance != null)
                    Manual.Give(EmporiumEntry.Instance.invElement, "gunsmith start");
            }
            catch (Exception e)
            {
                GunsmithMod.Warn("manuals with the gunsmith start: " + e.Message);
            }
        }
    }

    // Buying the retired gunsmith service. NetworkUpgrade.Unlock runs the service's own action (his starter kit on the counter top) and then raises
    // ModHook.OnNetworkUpgradeUnlocked, where RetiredGunsmithService gives the sign, the certificate and the pistol cabinet; this runs right after. The
    // gunsmith start sets the service unlocked without Unlock (NewGameData.HandleInitialItem), so the start does not get the books twice. In game 1.0.0.12
    // Unlock is called by the network window (WildUIManager.OnUnlockClicked, WildNetworkUI.OnUseClicked), by starting perks only for the two water taps,
    // and by kits of the developer panel.
    [HarmonyPatch(typeof(NetworkUpgrade), nameof(NetworkUpgrade.Unlock))]
    internal static class ManualServicePostfix
    {
        private static void Postfix(NetworkUpgrade __instance)
        {
            try
            {
                if (__instance != null && __instance.id == NetworkUpgrade.RETIRED_GUNSMITH && EmporiumEntry.Instance != null)
                    Manual.Give(EmporiumEntry.Instance.backInvinvElement, "retired gunsmith service");
            }
            catch (Exception e)
            {
                GunsmithMod.Warn("manuals with the retired gunsmith service: " + e.Message);
            }
        }
    }

    // The retired gunsmith's visit. His stock goes out in the end action of his greeting; Sellers.BeforeStock puts an action at the front of it, so the
    // one added last goes out first. With a priority below the default this postfix runs after RetiredGunsmithPatch (Harmony runs a method's
    // postfixes from the highest priority down), whose goods (the long gun cabinet, the repair goods, the blank chips) are put before the game's:
    // the books go out before all of them.
    [HarmonyPatch(typeof(StoreClientListSpec), nameof(StoreClientListSpec.CreateRetiredGunsmith))]
    [HarmonyPriority(Priority.Low)]
    internal static class RetiredGunsmithManualPostfix
    {
        private static void Postfix(StoreClient __result)
        {
            try
            {
                if (__result != null && !Sellers.BeforeStock(__result, Manual.OfferMissing))
                    GunsmithMod.Warn("the retired gunsmith has no line that puts out his stock; no manuals offered");
            }
            catch (Exception e)
            {
                GunsmithMod.Warn("manuals with the retired gunsmith: " + e.Message);
            }
        }
    }
}
