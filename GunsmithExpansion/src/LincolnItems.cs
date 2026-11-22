using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace GunsmithExpansion
{
    // The two new items of T-013:
    //   - The business card of Lincoln Arms (final-strategy 333, 353): given on Yuri Lincoln's first visit (LincolnVisits). Built as the
    //     game's card of the same kind (MiscItemDirectory.CardRev: a document worth nothing, activated to show its face). Its face is
    //     the picture gse_lincoln_card_open (137 x 85, the only words on it King Of War), shown in a copy of the game's card panel
    //     (PaperUIManager.cardRev) with the picture in place of the game's; a click closes it. Its text names the phone line once the
    //     line is open (Lv.1, final-strategy 342 ③; the dialogue draft's section 0).
    //   - The ammunition box (final-strategy 349, 351, 356): 2 x 2 cells, 6 x 6 cells inside, nothing packaged, a picture closed and
    //     one open; it takes what the game's unused ammunition box takes (UnusedDirectory.AmmoBox: the store's own items of the type
    //     AMMUNITION); value 100; sold at Lincoln Arms from Lv.2, 1 a day.
    // Agent's arrangements: the box shows its open picture while its window is open; the sounds are the game's case sounds.
    internal static class LincolnItems
    {
        internal const string CardId = "gse_lincoln_card";
        internal const string CardFace = "gse_lincoln_card_open";
        internal const string AmmoBoxId = "gse_ammo_box";
        internal const string AmmoBoxClosed = "gse_ammo_box_closed";
        internal const string AmmoBoxOpen = "gse_ammo_box_open";
        internal const int AmmoBoxValue = 100;
        private const int AmmoBoxSize = 6;

        private static GameObject cardPanel;

        private static bool Picture(string name)
        {
            return ModHelper.ModFileExists(GunsmithMod.ModId, "Textures/Items/" + name + ".png");
        }

        internal static void Register(ModItemDirectory directory)
        {
            int n = 0;
            if (Picture(CardId))
            {
                directory.Add(CardId, () => CreateCard(directory));
                n++;
            }
            if (Picture(AmmoBoxClosed) && Picture(AmmoBoxOpen))
            {
                directory.Add(AmmoBoxId, () => CreateAmmoBox(directory));
                n++;
            }
            GunsmithMod.Log("registered " + n + " items of Lincoln Arms");
        }

        private static GameItem CreateCard(ModItemDirectory directory)
        {
            GameItem card = directory.Empty().SetSpriteAndShapeFromMod(GunsmithMod.ModId, CardId);
            card.name = LincolnTexts.CardName.Get();
            card.shortDescription = CardText();
            card.unitValue = 0;
            card.canActivateSlotItemFunc = (it, inventory, marker) => true;
            card.mayActivateSlotItemFunc = (it, inventory, marker) => true;
            card.onActivateSlotItemFunc = (it, inventory, marker) => ShowCard();
            card.SetGameItemType("DOCUMENT");
            AudioHelper.InitCartonCard(card);
            card.EnableTag("IMPORTANT_TAG", false);
            card.onLoaded = (Action<GameItem>)Delegate.Combine(card.onLoaded, new Action<GameItem>(c => Relabel(c)));
            return card;
        }

        private static string CardText()
        {
            return (Lincoln.Level() >= 1 ? LincolnTexts.CardDescriptionPhone : LincolnTexts.CardDescription).Get();
        }

        // A saved card keeps the text and name it was saved with (SaveManager.DecodeNodesInternal).
        private static void Relabel(GameItem card)
        {
            if (card == null)
                return;
            card.name = LincolnTexts.CardName.Get();
            card.shortDescription = CardText();
        }

        // When the phone line opens (Lincoln.AfterChange), the cards the store has say so.
        internal static void RelabelCards()
        {
            if (EmporiumEntry.Instance == null)
                return;
            foreach (GameItem item in EmporiumEntry.Instance.GetAllOwnedItems(false, true))
            {
                if (item != null && item.IsID(CardId))
                    Relabel(item);
            }
        }

        internal static GameItem NewCard()
        {
            GameItem card = Picture(CardId) ? DirectoryMaster.Item(CardId, true) : null;
            if (card != null)
                GeneralHelper.SetItemOwned(card, true);
            return card;
        }

        private static Sprite Face()
        {
            Sprite face = CustomUIManager.Instance != null ? CustomUIManager.Instance.GetSprite(GunsmithMod.ModId + ":" + CardFace, true) : null;
            if (face != null && face.texture != null)
                face.texture.filterMode = FilterMode.Point;
            return face;
        }

        internal static void ShowCard()
        {
            try
            {
                PaperUIManager paper = PaperUIManager.Instance;
                if (paper == null || paper.cardRev == null)
                    return;
                if (cardPanel == null && !BuildCardPanel(paper))
                    return;
                cardPanel.SetActive(true);
                cardPanel.transform.SetAsLastSibling();
                if (paper.audioSourcePickup != null)
                    paper.audioSourcePickup.Play();
            }
            catch (Exception e)
            {
                GunsmithMod.Warn("business card: " + e.Message);
            }
        }

        // The game's card panel copied: its largest picture shows the face, at the face's proportions and the panel's height; the copy's
        // buttons close the copy; its words are cleared (the face carries its own).
        private static bool BuildCardPanel(PaperUIManager paper)
        {
            Sprite face = Face();
            if (face == null)
            {
                GunsmithMod.Warn("business card: no picture " + CardFace);
                return false;
            }
            cardPanel = UnityEngine.Object.Instantiate(paper.cardRev, paper.cardRev.transform.parent);
            cardPanel.name = "GseLincolnCard";
            Image main = null;
            float area = -1;
            foreach (Image image in cardPanel.GetComponentsInChildren<Image>(true))
            {
                Rect r = image.rectTransform.rect;
                if (image.sprite != null && r.width * r.height > area)
                {
                    main = image;
                    area = r.width * r.height;
                }
            }
            if (main != null)
            {
                main.sprite = face;
                main.preserveAspect = true;
                float height = main.rectTransform.rect.height;
                if (height > 0)
                    main.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, height * face.rect.width / face.rect.height);
            }
            foreach (TMPro.TMP_Text text in cardPanel.GetComponentsInChildren<TMPro.TMP_Text>(true))
            {
                text.text = "";
                text.enabled = false;
            }
            Button[] buttons = cardPanel.GetComponentsInChildren<Button>(true);
            if (buttons.Length == 0)
                buttons = new[] { cardPanel.AddComponent<Button>() };
            foreach (Button button in buttons)
            {
                button.onClick = new Button.ButtonClickedEvent();
                button.onClick.AddListener(CloseCard);
            }
            cardPanel.SetActive(false);
            return true;
        }

        internal static void CloseCard()
        {
            if (cardPanel != null)
                cardPanel.SetActive(false);
            PaperUIManager paper = PaperUIManager.Instance;
            if (paper != null && paper.audioSourceDrop != null)
                paper.audioSourceDrop.Play();
        }

        private static GameItem CreateAmmoBox(ModItemDirectory directory)
        {
            var made = DirectoryUtils.CreateInventoryWindow(AmmoBoxSize, AmmoBoxSize, true);
            PixelWindow window = made.Item1;
            GameInventory inside = made.Item2;
            GameItem box = directory.Empty().SetContentWindow(window).SetSpriteAndShapeFromMod(GunsmithMod.ModId, AmmoBoxClosed);
            box.name = Text.Pick("弹药盒", "Ammo Box");
            box.shortDescription = Text.Pick("存放弹药的金属盒，里面 6×6 格，只能放弹药。", "A metal box for ammunition, 6 × 6 cells inside. It takes nothing but ammunition.");
            box.unitValue = AmmoBoxValue;
            box.EnableTag("IMPORTANT_TAG", false);
            box.EnableTag("CONTAINER_TAG", false);
            box.SetGameItemType("STORAGE");
            ContainerHelper.AddContainerTooltipTag(box);
            AudioHelper.InitCase(box);
            window.SetTitle(box.name);
            window.Validate();
            inside.mayInventoryAddItemFunc = (Func<GameItem, GameInventory, bool>)Delegate.Combine(inside.mayInventoryAddItemFunc,
                new Func<GameItem, GameInventory, bool>((item, target) => item != null && item.IsGameItemType("AMMUNITION") && GeneralHelper.IsItemOwned(item)));
            inside.EnableTag("NO_HOPPER_INVENTORY_TAG");
            window.openCallback = (Action)Delegate.Combine(window.openCallback, new Action(() => box.SetSpriteAndShapeFromMod(GunsmithMod.ModId, AmmoBoxOpen)));
            window.closeCallback = (Action)Delegate.Combine(window.closeCallback, new Action(() => box.SetSpriteAndShapeFromMod(GunsmithMod.ModId, AmmoBoxClosed)));
            box.onLoaded = (Action<GameItem>)Delegate.Combine(box.onLoaded, new Action<GameItem>(b =>
            {
                if (b != null && (b.contentWindow == null || !b.contentWindow.IsVisible()))
                    b.SetSpriteAndShapeFromMod(GunsmithMod.ModId, AmmoBoxClosed);
            }));
            return box;
        }
    }

    // PixelWindow.Hide (double-click toggle, CloseContentWindow, leaving the store) does not invoke closeCallback.
    [HarmonyPatch(typeof(PixelWindow), nameof(PixelWindow.Hide))]
    internal static class LincolnAmmoBoxHidePatch
    {
        private static void Postfix(PixelWindow __instance)
        {
            if (__instance == null || __instance.parentItems == null)
                return;
            foreach (GameItem item in __instance.parentItems)
            {
                if (item != null && item.IsID(LincolnItems.AmmoBoxId))
                    item.SetSpriteAndShapeFromMod(GunsmithMod.ModId, LincolnItems.AmmoBoxClosed);
            }
        }
    }
}
