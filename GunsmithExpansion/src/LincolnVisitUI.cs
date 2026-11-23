using System;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace GunsmithExpansion
{
    // Visit-only UI for T-041 W5. Native APIs and source-image coordinates are documented in
    // out/W5/evidence/native-visit-ui.il.txt and the W5 report.
    internal static class LincolnVisitUI
    {
        private const string FavorRowName = "GSE_LincolnFavorRow";
        private const float SceneWidth = 960f;
        private const float SceneHeight = 540f;
        private static readonly Vector3[] Corners = new Vector3[4];
        private static GameObject ledgerTablet;
        private static GameObject dealerTablet;
        private static DialogUIManager dialog;
        private static RectTransform nameRect;
        private static Vector3 originalNamePosition;
        private static float originalBubbleScale;
        private static bool nameSaved;
        private static bool layoutWarned;
        private static RectTransform favorRow;
        private static TextMeshProUGUI favorText;
        private static RectTransform favorIcon;
        private static string lastFavorText;
        private static float nextFavorRefresh;

        internal static void Begin()
        {
            Restore();
            if (!LincolnShop.Here())
                return;
            layoutWarned = false;
            try
            {
                EnsureTablets();
                RememberName(DialogUIManager.Instance);
                Canvas.willRenderCanvases -= BeforeCanvasRender;
                Canvas.willRenderCanvases += BeforeCanvasRender;
            }
            catch (Exception e)
            {
                GunsmithMod.Warn("Lincoln visit UI initialization: " + e.Message);
            }
        }

        // ShowStage creates and activates this object before the location's Arrive callback.
        // Compare the actual sprite with the two registered Lincoln backgrounds before attaching anything.
        private static void EnsureTablets()
        {
            GameObject stageObject = GameObject.Find("AfterhourStage");
            SpriteRenderer scene = stageObject != null ? stageObject.GetComponent<SpriteRenderer>() : null;
            CustomUIManager custom = CustomUIManager.Instance;
            if (scene == null || scene.sprite == null || custom == null)
            {
                GunsmithMod.Warn("Lincoln tablet hotspots: active AfterhourStage sprite unavailable");
                return;
            }
            Sprite expected = custom.GetSprite(GunsmithMod.ModId + ":" + LincolnShop.SceneYuri);
            if (scene.sprite != expected)
                expected = custom.GetSprite(GunsmithMod.ModId + ":" + LincolnShop.SceneAva);
            if (scene.sprite != expected)
            {
                GunsmithMod.Warn("Lincoln tablet hotspots: AfterhourStage has a different background");
                return;
            }

            // Both 960 x 540 backgrounds share this ledger and the on-duty character's position.
            // Coordinates use the source image's top-left origin. No visible sprite is added.
            ledgerTablet = CreateTablet(scene, "GSE_LincolnLedgerTablet", 341.5f, 247f, 54f, 34f);
            // HideClient disables the original StoreClientMono, so the painted character needs its own hit area.
            dealerTablet = CreateTablet(scene, "GSE_LincolnDealerTablet", 469f, 162f, 130f, 134f);
        }

        private static GameObject CreateTablet(SpriteRenderer scene, string objectName, float x, float y, float width, float height)
        {
            Sprite sprite = scene.sprite;
            float ppu = sprite.pixelsPerUnit;
            if (!(ppu > 0f) || sprite.rect.width <= 0f || sprite.rect.height <= 0f)
                throw new InvalidOperationException("Lincoln background has invalid sprite dimensions");
            Vector2 position = new Vector2(
                (x / SceneWidth * sprite.rect.width - sprite.pivot.x) / ppu,
                ((1f - y / SceneHeight) * sprite.rect.height - sprite.pivot.y) / ppu);
            if (scene.flipX)
                position.x = -position.x;
            if (scene.flipY)
                position.y = -position.y;

            GameObject tablet = new GameObject(objectName);
            try
            {
                tablet.SetActive(false);
                tablet.layer = scene.gameObject.layer;
                tablet.transform.SetParent(scene.transform, false);
                tablet.transform.localPosition = new Vector3(position.x, position.y, 0f);
                BoxCollider2D hit = tablet.AddComponent<BoxCollider2D>();
                hit.size = new Vector2(width / SceneWidth * sprite.rect.width / ppu,
                    height / SceneHeight * sprite.rect.height / ppu);
                Interactable interactable = tablet.AddComponent<Interactable>();
                interactable.isControlable = false;
                interactable.doNotOutline = true;
                tablet.AddComponent<StoreTablet>();
                tablet.AddComponent<TooltipContainer>();
                tablet.SetActive(true);
                return tablet;
            }
            catch
            {
                UnityEngine.Object.Destroy(tablet);
                throw;
            }
        }

        internal static void AfterSay()
        {
            if (!LincolnShop.Here())
                return;
            try
            {
                Canvas.ForceUpdateCanvases();
                PositionName();
            }
            catch (Exception e)
            {
                WarnLayout(e);
            }
        }

        internal static void Tick()
        {
            if (!LincolnShop.Here())
                return;
            try
            {
                PositionName();
                RefreshFavorText();
            }
            catch (Exception e)
            {
                WarnLayout(e);
            }
        }

        // Canvas layout can change the text bubble after the location's Tick, including during typewriter playback.
        private static void BeforeCanvasRender()
        {
            if (!LincolnShop.Here())
                return;
            try
            {
                PositionName();
            }
            catch (Exception e)
            {
                WarnLayout(e);
            }
        }

        private static void WarnLayout(Exception e)
        {
            if (layoutWarned)
                return;
            layoutWarned = true;
            GunsmithMod.Warn("Lincoln visit UI layout: " + e.Message);
        }

        private static void RememberName(DialogUIManager owner)
        {
            RectTransform current = owner != null && owner.clientNameBubble != null
                ? owner.clientNameBubble.transform as RectTransform : null;
            if (current == null)
                return;
            if (nameSaved && dialog == owner && nameRect == current)
                return;
            RestoreName();
            dialog = owner;
            nameRect = current;
            originalNamePosition = current.localPosition;
            originalBubbleScale = EmporiumOption.DialogBubbleScale;
            nameSaved = true;
        }

        private static Camera UICamera(RectTransform rect)
        {
            Canvas canvas = rect.GetComponentInParent<Canvas>();
            if (canvas != null)
                canvas = canvas.rootCanvas;
            if (canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay)
                return null;
            return canvas != null && canvas.worldCamera != null ? canvas.worldCamera : Camera.main;
        }

        private static Rect ScreenBounds(RectTransform rect)
        {
            rect.GetWorldCorners(Corners);
            Camera camera = UICamera(rect);
            Vector2 low = RectTransformUtility.WorldToScreenPoint(camera, Corners[0]);
            Vector2 high = low;
            for (int i = 1; i < Corners.Length; i++)
            {
                Vector2 point = RectTransformUtility.WorldToScreenPoint(camera, Corners[i]);
                low = Vector2.Min(low, point);
                high = Vector2.Max(high, point);
            }
            return Rect.MinMaxRect(low.x, low.y, high.x, high.y);
        }

        private static void PositionName()
        {
            DialogUIManager ui = DialogUIManager.Instance;
            RememberName(ui);
            if (ui == null || dialog != ui || !nameSaved || nameRect == null || ui.clientBubble == null)
                return;
            float bubbleScale = EmporiumOption.DialogBubbleScale;
            if (!Mathf.Approximately(originalBubbleScale, bubbleScale))
            {
                nameRect.localPosition = originalNamePosition;
                ui.ApplyBubbleScale();
                originalNamePosition = nameRect.localPosition;
                originalBubbleScale = bubbleScale;
            }
            RectTransform body = ui.clientBubble.transform as RectTransform;
            if (body == null || !body.gameObject.activeInHierarchy || !nameRect.gameObject.activeInHierarchy)
                return;
            // The name must not contain the body, since moving such a parent would move both rectangles.
            if (body.IsChildOf(nameRect))
                return;
            Rect bodyBounds = ScreenBounds(body);
            Rect nameBounds = ScreenBounds(nameRect);
            float units = Mathf.Abs(nameRect.rect.height) > 0.01f
                ? nameBounds.height / Mathf.Abs(nameRect.rect.height) : 1f;
            float gap = Mathf.Max(2f, 4f * units);
            float shift = bodyBounds.yMax + gap - nameBounds.yMin;
            if (Mathf.Abs(shift) < 0.1f)
                return;
            Vector3 before, after;
            Camera camera = UICamera(nameRect);
            if (RectTransformUtility.ScreenPointToWorldPointInRectangle(nameRect, nameBounds.center, camera, out before)
                && RectTransformUtility.ScreenPointToWorldPointInRectangle(nameRect,
                    nameBounds.center + new Vector2(0f, shift), camera, out after))
                nameRect.position += after - before;
        }

        private static void RestoreName()
        {
            try
            {
                if (nameSaved && nameRect != null)
                {
                    nameRect.localPosition = originalNamePosition;
                    if (dialog != null && !Mathf.Approximately(originalBubbleScale, EmporiumOption.DialogBubbleScale))
                        dialog.ApplyBubbleScale();
                }
            }
            finally
            {
                nameSaved = false;
                nameRect = null;
                dialog = null;
            }
        }

        private static string FavorSummary()
        {
            return string.Format(Text.Pick("好感度 Lv.{0} | 折扣 {1}%", "Favor Lv.{0} | Discount {1}%"),
                Lincoln.Level(), Lincoln.Discount());
        }

        internal static void AddSellingStatus(StoreClient client, NegotiationTabletFrame frame)
        {
            if (!LincolnShop.Here() || client == null || client.identifier != LincolnShop.DealerId
                || frame == null || frame.content == null)
                return;
            ClearFavorRow();
            for (int i = frame.content.childCount - 1; i >= 0; i--)
            {
                Transform child = frame.content.GetChild(i);
                if (child.name == FavorRowName)
                {
                    GameObject stale = child.gameObject;
                    DestroyOwned(ref stale);
                }
            }

            lastFavorText = FavorSummary();
            favorRow = TabletUI.SubHeader(frame, frame.content, lastFavorText, 38f,
                frame.infoCream, () => Lincoln.StatusLine(), TabletStyle.SCREEN);
            favorRow.name = FavorRowName;
            favorRow.SetAsFirstSibling();
            Transform label = favorRow.Find("Text");
            favorText = label != null ? label.GetComponent<TextMeshProUGUI>() : null;
            favorIcon = favorRow.Find("Info") as RectTransform;
            nextFavorRefresh = 0f;
        }

        private static void RefreshFavorText()
        {
            if (favorRow == null || favorText == null || !favorRow.gameObject.activeInHierarchy
                || Time.unscaledTime < nextFavorRefresh)
                return;
            nextFavorRefresh = Time.unscaledTime + 0.25f;
            string current = FavorSummary();
            if (current == lastFavorText)
                return;
            lastFavorText = current;
            favorText.text = current;
            if (favorIcon != null)
            {
                float textWidth = TabletUI.MeasureWidth(favorText, current);
                Vector2 position = favorIcon.anchoredPosition;
                // SubHeader centers the text and icon together, shifting both by 9 native UI units.
                position.x = (favorRow.rect.width + textWidth) * 0.5f + 4f - 9f;
                favorIcon.anchoredPosition = position;
            }
        }

        private static void DestroyOwned(ref GameObject value)
        {
            GameObject target = value;
            value = null;
            if (target == null)
                return;
            target.SetActive(false);
            target.transform.SetParent(null, false);
            UnityEngine.Object.Destroy(target);
        }

        private static void ClearFavorRow()
        {
            GameObject row = favorRow != null ? favorRow.gameObject : null;
            favorRow = null;
            favorText = null;
            favorIcon = null;
            lastFavorText = null;
            nextFavorRefresh = 0f;
            DestroyOwned(ref row);
        }

        internal static void Restore()
        {
            Canvas.willRenderCanvases -= BeforeCanvasRender;
            try
            {
                RestoreName();
            }
            finally
            {
                try
                {
                    DestroyOwned(ref ledgerTablet);
                }
                finally
                {
                    try
                    {
                        DestroyOwned(ref dealerTablet);
                    }
                    finally
                    {
                        ClearFavorRow();
                    }
                }
            }
        }
    }

    [HarmonyPatch(typeof(NegociationUIManager), "BuildSelling")]
    internal static class LincolnShopSellingStatusPatch
    {
        private static void Postfix(StoreClient storeClient, NegotiationTabletFrame ___frame)
        {
            try
            {
                LincolnVisitUI.AddSellingStatus(storeClient, ___frame);
            }
            catch (Exception e)
            {
                GunsmithMod.Warn("Lincoln selling status: " + e.Message);
            }
        }
    }
}
