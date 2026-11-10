using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace GunsmithExpansion
{
    // Layout is measured in screen pixels, including the game's letterboxed UI camera.
    // Keep the user's native font and bubble scale; wrap labels and reduce the page size
    // when necessary. Each page keeps both its navigation and hang-up choices visible.
    internal static class PhoneChoices
    {
        private static DialoguePhoneUIManager owner;
        private static readonly List<Vector2> positions = new List<Vector2>();
        private static readonly Dictionary<DialogChoiceBubble, float> heights = new Dictionary<DialogChoiceBubble, float>();
        private static float initialScale;
        private static bool arranging;

        internal static int Ensure()
        {
            var ui = DialoguePhoneUIManager.Instance;
            if (ui == null || ui.dialogChoiceBubbles == null || ui.dialogChoiceBubbles.Count < 3) return 0;
            var bubbles = ui.dialogChoiceBubbles;
            if (owner != ui)
            {
                owner = ui; positions.Clear(); heights.Clear();
                for (int i = 0; i < 3; i++) positions.Add(((RectTransform)bubbles[i].transform).anchoredPosition);
                initialScale = bubbles[0].transform.localScale.x;
                if (ui.GetComponent<PhoneChoicesWatcher>() == null) ui.gameObject.AddComponent<PhoneChoicesWatcher>();
            }
            var last = bubbles[2];
            while (bubbles.Count < Phone.ChoiceBubbles)
            {
                var copy = UnityEngine.Object.Instantiate(last.gameObject, last.transform.parent);
                var bubble = copy.GetComponent<DialogChoiceBubble>();
                bubble.index = bubbles.Count; copy.SetActive(false); bubbles.Add(bubble);
            }
            foreach (var bubble in bubbles)
                if (!heights.ContainsKey(bubble)) heights[bubble] = ((RectTransform)bubble.transform).rect.height;
            CopyScale(ui);
            return bubbles.Count;
        }

        private static void CopyScale(DialoguePhoneUIManager ui)
        {
            for (int i = 3; i < ui.dialogChoiceBubbles.Count; i++)
                ui.dialogChoiceBubbles[i].transform.localScale = ui.dialogChoiceBubbles[2].transform.localScale;
        }

        private static Camera CameraFor(Component component)
        {
            var canvas = component.GetComponentInParent<Canvas>();
            return canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        }

        internal static Rect ScreenBounds(RectTransform rect)
        {
            var corners = new Vector3[4]; rect.GetWorldCorners(corners);
            Camera camera = CameraFor(rect);
            Vector2 a = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
            Vector2 b = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
            return Rect.MinMaxRect(a.x, a.y, b.x, b.y);
        }

        internal static Rect AvailableArea()
        {
            Rect safe = Screen.safeArea;
            int width = LetterboxManager.GameAreaWidth, height = LetterboxManager.GameAreaHeight;
            Rect game = width > 0 && height > 0
                ? new Rect(LetterboxManager.GameAreaX, LetterboxManager.GameAreaY, width, height)
                : new Rect(0, 0, Screen.width, Screen.height);
            Rect area = Rect.MinMaxRect(Mathf.Max(safe.xMin, game.xMin), Mathf.Max(safe.yMin, game.yMin),
                Mathf.Min(safe.xMax, game.xMax), Mathf.Min(safe.yMax, game.yMax));
            float margin = Mathf.Max(8, area.height * 0.015f);
            area = Rect.MinMaxRect(area.xMin + margin, area.yMin + margin, area.xMax - margin, area.yMax - margin);
            var ui = DialoguePhoneUIManager.Instance;
            if (ui != null && ui.clientBubble != null && ui.clientBubble.gameObject.activeInHierarchy)
            {
                Rect client = ScreenBounds((RectTransform)ui.clientBubble.transform);
                // The phone's client line can be below the choices. Use the larger side
                // of that line so buttons never obscure the currently displayed speech.
                float below = Mathf.Max(0, client.yMin - margin - area.yMin);
                float above = Mathf.Max(0, area.yMax - client.yMax - margin);
                if (client.yMax > area.yMin && client.yMin < area.yMax)
                {
                    if (above >= below) area.yMin = Mathf.Min(area.yMax, client.yMax + margin);
                    else area.yMax = Mathf.Max(area.yMin, client.yMin - margin);
                }
            }
            return area;
        }

        private static Vector2 PixelScale(DialogChoiceBubble bubble)
        {
            var rect = (RectTransform)bubble.transform;
            Rect screen = ScreenBounds(rect);
            return new Vector2(screen.width / Mathf.Max(1, rect.rect.width), screen.height / Mathf.Max(1, rect.rect.height));
        }

        private static float Left(DialogChoiceBubble bubble, Rect area)
        {
            // Keep the native left edge when it leaves at least half the viewport for text.
            float native = ScreenBounds((RectTransform)bubble.transform).xMin;
            return Mathf.Clamp(native, area.xMin, area.xMax - area.width * 0.55f);
        }

        private static float TextWidth(DialogChoiceBubble bubble, Rect area)
        {
            var group = bubble.GetComponent<HorizontalLayoutGroup>();
            float padding = group != null ? group.padding.horizontal : 20;
            return Mathf.Max(12, (area.xMax - Left(bubble, area)) / Mathf.Max(0.01f, PixelScale(bubble).x) - padding - 2);
        }

        // Word breaks for spaced languages and grapheme boundaries for Chinese or long
        // unspaced tokens. StringInfo avoids splitting surrogate pairs/combining sequences.
        internal static string Wrap(TMPro.TMP_Text label, string text, float width)
        {
            if (string.IsNullOrEmpty(text) || label.GetPreferredValues(text).x <= width) return text;
            var lines = new List<string>();
            foreach (string paragraph in text.Split('\n'))
            {
                string line = "";
                var elements = StringInfo.GetTextElementEnumerator(paragraph);
                while (elements.MoveNext())
                {
                    string part = (string)elements.Current;
                    string next = line + part;
                    if (line.Length > 0 && label.GetPreferredValues(next).x > width)
                    {
                        int split = line.LastIndexOf(' ');
                        if (split > 0)
                        {
                            lines.Add(line.Substring(0, split).TrimEnd());
                            line = line.Substring(split + 1) + part;
                        }
                        else { lines.Add(line.TrimEnd()); line = part.TrimStart(); }
                    }
                    else line = next;
                }
                lines.Add(line.TrimEnd());
            }
            return string.Join("\n", lines.ToArray());
        }

        private static float Height(DialogChoiceBubble bubble, string text, float width)
        {
            float native; if (!heights.TryGetValue(bubble, out native)) native = ((RectTransform)bubble.transform).rect.height;
            float line = bubble.TMP.GetPreferredValues("M").y;
            float content = bubble.TMP.GetPreferredValues(Wrap(bubble.TMP, text, width)).y;
            return Mathf.Max(native, native + content - line);
        }

        internal static int PageCapacity(List<string> labels)
        {
            if (Ensure() == 0) return 1;
            RestoreNative(owner);
            var bubble = owner.dialogChoiceBubbles[0];
            Rect area = AvailableArea();
            float width = TextWidth(bubble, area), scale = PixelScale(bubble).y;
            float gap = Mathf.Max(3, 2 * scale);
            float total = (Height(bubble, Text.Pick("还有别的吗？", "What else have you got?"), width)
                + Height(bubble, Text.Pick("先这样。", "That's all."), width)) * scale + gap;
            int count = 0;
            foreach (string label in labels)
            {
                float extra = Height(bubble, label, width) * scale + gap;
                if (count > 0 && total + extra > area.height) break;
                total += extra; count++;
                if (count >= Phone.ChoiceBubbles - 2) break;
            }
            return Mathf.Max(1, count);
        }

        private static void MoveScreen(RectTransform rect, float x, float y)
        {
            var parent = rect.parent as RectTransform;
            if (parent == null) return;
            Camera camera = CameraFor(rect);
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(camera, rect.position), a, b;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screen, camera, out a)
                && RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screen + new Vector2(x, y), camera, out b))
                rect.anchoredPosition += b - a;
        }

        private static void RestoreNative(DialoguePhoneUIManager ui)
        {
            float ratio = initialScale > 0 ? ui.dialogChoiceBubbles[0].transform.localScale.x / initialScale : 1;
            for (int i = 0; i < ui.dialogChoiceBubbles.Count; i++)
            {
                var bubble = ui.dialogChoiceBubbles[i]; var rect = (RectTransform)bubble.transform;
                int nativeIndex = Mathf.Min(i, 2);
                rect.anchoredPosition = positions[0] + (positions[nativeIndex] - positions[0]) * ratio;
                float height; if (heights.TryGetValue(bubble, out height)) rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
            }
        }

        internal static void Arrange(DialoguePhoneUIManager ui)
        {
            if (arranging || ui == null || ui != owner || positions.Count < 3) return;
            arranging = true;
            try
            {
                CopyScale(ui); RestoreNative(ui);
                if (!Phone.IsMenuPage(ui.currentDialog))
                {
                    // Preserve the previous generic arrangement for another mod's call
                    // that also uses the additional bubbles.
                    int shown = ui.currentDialog != null ? Mathf.Min(ui.currentDialog.choices.Count, ui.dialogChoiceBubbles.Count) : 0;
                    if (shown > 3)
                    {
                        float ratio = initialScale > 0 ? ui.dialogChoiceBubbles[0].transform.localScale.x / initialScale : 1;
                        Vector2 step = (positions[1] - positions[0]) * ratio;
                        Vector2 nativeLast = positions[0] + (positions[2] - positions[0]) * ratio;
                        for (int i = 0; i < shown; i++)
                            ((RectTransform)ui.dialogChoiceBubbles[i].transform).anchoredPosition = nativeLast + step * (i - (shown - 1));
                    }
                    return;
                }
                Rect area = AvailableArea();
                int count = Mathf.Min(ui.currentDialog.choices.Count, ui.dialogChoiceBubbles.Count);
                var first = ui.dialogChoiceBubbles[0];
                float left = Left(first, area), width = TextWidth(first, area);
                float gap = Mathf.Max(3, 2 * PixelScale(first).y);
                var bounds = new List<Rect>();
                float total = Mathf.Max(0, count - 1) * gap;
                for (int i = 0; i < count; i++)
                {
                    var bubble = ui.dialogChoiceBubbles[i]; var rect = (RectTransform)bubble.transform;
                    string text = ui.currentDialog.choices[i].Item2.plainText;
                    bubble.TMP.text = Wrap(bubble.TMP, text, width);
                    rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Height(bubble, text, width));
                    LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
                    Rect row = ScreenBounds(rect); bounds.Add(row); total += row.height;
                }
                float nativeBottom = ScreenBounds((RectTransform)ui.dialogChoiceBubbles[2].transform).yMin;
                float bottom = Mathf.Clamp(nativeBottom, area.yMin, Mathf.Max(area.yMin, area.yMax - total));
                float top = bottom + total;
                for (int i = 0; i < count; i++)
                {
                    var rect = (RectTransform)ui.dialogChoiceBubbles[i].transform;
                    MoveScreen(rect, left - bounds[i].xMin, top - bounds[i].yMax);
                    top -= bounds[i].height + gap;
                }
            }
            finally { arranging = false; }
        }

        internal static void Refresh(DialoguePhoneUIManager ui)
        {
            if (ui == null || !Phone.IsMenuPage(ui.currentDialog) || ui.dialogChoiceBubbles.Count == 0
                || !ui.dialogChoiceBubbles[0].gameObject.activeInHierarchy) return;
            ui.DisplayChoice();
        }

        internal static Vector4 LayoutKey()
        {
            if (owner == null || owner.dialogChoiceBubbles.Count == 0) return Vector4.zero;
            Vector2 scale = PixelScale(owner.dialogChoiceBubbles[0]);
            return new Vector4(Screen.width, Screen.height, scale.x, scale.y);
        }
    }

    internal sealed class PhoneChoicesWatcher : MonoBehaviour
    {
        private Vector4 previous;
        private Rect previousArea;
        private void LateUpdate()
        {
            Vector4 next = PhoneChoices.LayoutKey();
            Rect area = PhoneChoices.AvailableArea();
            if (next.x != previous.x || next.y != previous.y || Mathf.Abs(next.z - previous.z) > 0.002f || Mathf.Abs(next.w - previous.w) > 0.002f
                || Mathf.Abs(area.x - previousArea.x) > 1 || Mathf.Abs(area.y - previousArea.y) > 1
                || Mathf.Abs(area.width - previousArea.width) > 1 || Mathf.Abs(area.height - previousArea.height) > 1)
            {
                previous = next; previousArea = area;
                PhoneChoices.Refresh(GetComponent<DialoguePhoneUIManager>());
            }
        }
    }
}
