using System;
using TMPro;
using UnityEngine;

namespace GunsmithExpansion
{
    // A header outside the top edge of the classic front counter window.
    // PixelWindow's native title, padding, rectangle, visibility and header-button list are preserved.
    internal static class LincolnShelfHeader
    {
        private const int Height = 20;
        private const int ButtonSize = 16;
        private const int Margin = 2;
        private const int Gap = 4;
        private static PixelWindow window;
        private static TreeNodeRender header;
        private static TreeNodeRender titleNode;
        private static MachineryButton previous;
        private static MachineryButton next;
        private static int laidOutWidth = -1;

        internal static void Capture(PixelWindow target)
        {
            Restore();
            window = target;
        }

        internal static void Attach(Action pageUp, Action pageDown)
        {
            if (window == null || window.handler == null || header != null || RenderHandler.current == null)
                return;
            try
            {
                header = TreeNodeRender.Instantiate();
                header.gameObject.name = "GSE Lincoln Shelf Header";
                header.SetParent(window.handler);
                MachineryStyle.SetSliced(header, MachineryStyle.BevelFrame);
                header.image.raycastTarget = false;
                if (header.mask != null)
                    header.mask.enabled = false;
                RectTransform rect = header.rectTransform;
                rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 0f);
                rect.anchoredPosition = new Vector2(0f, Margin);
                rect.localScale = Vector3.one;
                rect.localRotation = Quaternion.identity;

                titleNode = TreeNodeRender.InstantiateText();
                titleNode.gameObject.name = "GSE Lincoln Shelf Rows";
                titleNode.SetParent(header);
                MachineryStyle.InitText(titleNode, 10, MachineryStyle.TITLE_TEXT,
                    HorizontalAlignmentOptions.Center, false);
                TMP_Text text = titleNode.text;
                text.richText = false;
                text.enableAutoSizing = true;
                text.fontSizeMin = 7f;
                text.fontSizeMax = 10f;
                text.enableWordWrapping = false;
                text.overflowMode = TextOverflowModes.Ellipsis;
                text.margin = Vector4.zero;
                SetTopLeft(titleNode.rectTransform);

                previous = new MachineryButton("<", () =>
                {
                    if (LincolnShop.Here() && pageUp != null)
                        pageUp();
                }, ButtonSize, ButtonSize);
                previous.handler.gameObject.name = "GSE Lincoln Previous Page";
                previous.handler.SetParent(header);
                SetTopLeft(previous.handler.rectTransform);
                previous.SetDescription(Text.Pick("< 上一页", "< Prev Page"));

                next = new MachineryButton(">", () =>
                {
                    if (LincolnShop.Here() && pageDown != null)
                        pageDown();
                }, ButtonSize, ButtonSize);
                next.handler.gameObject.name = "GSE Lincoln Next Page";
                next.handler.SetParent(header);
                SetTopLeft(next.handler.rectTransform);
                next.SetDescription(Text.Pick("下一页 >", "Next Page >"));

                previous.SetDisabled(true);
                next.SetDisabled(true);
                header.rectTransform.SetAsLastSibling();
                laidOutWidth = -1;
                Tick();
            }
            catch (Exception e)
            {
                Restore();
                GunsmithMod.Warn("Lincoln shelf header: " + e.Message);
            }
        }

        private static void SetTopLeft(RectTransform rect)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
        }

        internal static void Refresh(string title, bool canUp, bool canDown)
        {
            if (header == null || titleNode == null)
                return;
            titleNode.text.text = title ?? string.Empty;
            if (previous != null)
            {
                previous.SetDisabled(!canUp);
                previous.SetDescription(Text.Pick("< 上一页", "< Prev Page"));
            }
            if (next != null)
            {
                next.SetDisabled(!canDown);
                next.SetDescription(Text.Pick("下一页 >", "Next Page >"));
            }
            Tick();
        }

        internal static void Tick()
        {
            if (window == null || window.handler == null || header == null)
                return;
            // PixelWindow.widthPixels is the native pixel-space width.
            // The handler rectangle can have an independent size; the game lays out its children from widthPixels.
            int width = Math.Max(1, window.widthPixels);
            if (laidOutWidth == width)
                return;
            laidOutWidth = width;
            header.rectTransform.sizeDelta = new Vector2(width, Height);
            int left = Margin + ButtonSize + Gap;
            int textWidth = Math.Max(1, width - left * 2);
            MachineryStyle.Place(titleNode, left, 0, textWidth, Height);
            MachineryStyle.Place(previous.handler, Margin, Margin, ButtonSize, ButtonSize);
            MachineryStyle.Place(next.handler, Math.Max(Margin, width - Margin - ButtonSize),
                Margin, ButtonSize, ButtonSize);
        }

        internal static bool Over()
        {
            if (header == null || !header.gameObject.activeInHierarchy)
                return false;
            RectTransform rect = header.rectTransform;
            Canvas canvas = rect.GetComponentInParent<Canvas>();
            Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera : null;
            return RectTransformUtility.RectangleContainsScreenPoint(rect, Input.mousePosition, camera);
        }

        internal static void Restore()
        {
            // Only objects created by this helper are removed.
            // No native PixelWindow property or existing child is changed by the helper.
            TreeNodeRender oldHeader = header;
            MachineryButton oldPrevious = previous;
            MachineryButton oldNext = next;
            header = null;
            titleNode = null;
            previous = null;
            next = null;
            window = null;
            laidOutWidth = -1;
            if (oldHeader != null)
                oldHeader.gameObject.SetActive(false);
            try
            {
                RemoveButton(oldPrevious);
            }
            finally
            {
                try
                {
                    RemoveButton(oldNext);
                }
                finally
                {
                    if (oldHeader != null)
                        UnityEngine.Object.Destroy(oldHeader.gameObject);
                }
            }
        }

        private static void RemoveButton(MachineryButton button)
        {
            if (button == null)
                return;
            button.SetCallback(null);
            if (button.handler == null)
                return;
            GameObject obj = button.handler.gameObject;
            obj.SetActive(false);
            try
            {
                button.Reset();
            }
            finally
            {
                UnityEngine.Object.Destroy(obj);
            }
        }
    }
}
