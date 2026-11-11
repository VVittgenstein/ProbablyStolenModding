using System;
using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace F5SnapshotSave
{
    // The Load Game screen shows the day and money of a save in one text (Plate.info on a slot plate, Row/Info on a row of the
    // expanded history). A text that holds the second line of an F5 save (SaveDetails) gets more room; the layout is the one
    // of lin-and-tree's pull request #1. 0.7.0, as F5ForceSave 1.8.0, changes only the texts with a second line and puts back the layout it found
    // when the same text later shows a save without one.
    internal static class SaveDetailsUi
    {
        private const string Marker = "\n<indent=10px>";

        private sealed class Original
        {
            internal Vector2 AnchorMin, AnchorMax, Pivot, Position, Size;
            internal TextAlignmentOptions Alignment;
            internal bool RichText, Wrapping;
            internal TextOverflowModes Overflow;
        }

        private static readonly Dictionary<TextMeshProUGUI, Original> originals = new Dictionary<TextMeshProUGUI, Original>();

        internal static string Describe(string title, SaveDetails.Record record)
        {
            return title + Marker + "<size=80%><color=#8FB9B5>" + SaveDetails.Format(record) + "</color></size></indent>";
        }

        internal static void Fit(TextMeshProUGUI label, bool plate)
        {
            if (label == null || !QuickHistory.Handles())
                return;
            bool detailed = label.text != null && label.text.Contains(Marker);
            Original original;
            bool known = originals.TryGetValue(label, out original);
            if (!detailed)
            {
                if (known)
                {
                    Restore(label, original);
                    originals.Remove(label);
                }
                return;
            }
            if (!known)
            {
                if (originals.Count >= 256)
                    Prune();
                originals[label] = Capture(label);
            }
            // Uses the text the game set up for DayAndMoney, with its font and canvas.
            WildNetworkUI.SetRect(label.rectTransform, plate ? 82f : 0f, plate ? 48f : 17f, 488f, plate ? 44f : 46f);
            label.alignment = TextAlignmentOptions.TopLeft;
            label.richText = true;
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Overflow;
        }

        private static Original Capture(TextMeshProUGUI label)
        {
            RectTransform rect = label.rectTransform;
            return new Original
            {
                AnchorMin = rect.anchorMin,
                AnchorMax = rect.anchorMax,
                Pivot = rect.pivot,
                Position = rect.anchoredPosition,
                Size = rect.sizeDelta,
                Alignment = label.alignment,
                RichText = label.richText,
                Wrapping = label.enableWordWrapping,
                Overflow = label.overflowMode,
            };
        }

        private static void Restore(TextMeshProUGUI label, Original original)
        {
            RectTransform rect = label.rectTransform;
            rect.anchorMin = original.AnchorMin;
            rect.anchorMax = original.AnchorMax;
            rect.pivot = original.Pivot;
            rect.anchoredPosition = original.Position;
            rect.sizeDelta = original.Size;
            label.alignment = original.Alignment;
            label.richText = original.RichText;
            label.enableWordWrapping = original.Wrapping;
            label.overflowMode = original.Overflow;
        }

        // Texts of closed screens are destroyed; their entries are dropped.
        private static void Prune()
        {
            var gone = new List<TextMeshProUGUI>();
            foreach (TextMeshProUGUI key in originals.Keys)
            {
                if (key == null)
                    gone.Add(key);
            }
            foreach (TextMeshProUGUI key in gone)
                originals.Remove(key);
        }
    }

    [HarmonyPatch(typeof(LoadGameUI), "Bind")]
    internal static class SaveDetailsPlatePatch
    {
        private static void Postfix(object __0)
        {
            try
            {
                // Plate.info is under Body, rather than a child of Plate.root.
                SaveDetailsUi.Fit(ModReflect.Get<TextMeshProUGUI>(__0, "info"), true);
            }
            catch (Exception e) { UnityEngine.Debug.LogWarning(F5SnapshotSaveMod.LogPrefix + "save details UI: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(LoadGameUI), "BuildExpanded")]
    internal static class SaveDetailsRowsPatch
    {
        private static void Postfix(object __0, List<SaveHistoryEntry> history)
        {
            try
            {
                var expanded = ModReflect.Get<RectTransform>(__0, "expanded");
                Transform rows = expanded == null ? null : expanded.Find("Rows");
                if (rows == null || history == null) return;
                foreach (Transform row in rows)
                {
                    if (row.name == "Row") SaveDetailsUi.Fit(row.Find("Info")?.GetComponent<TextMeshProUGUI>(), false);
                }
            }
            catch (Exception e) { UnityEngine.Debug.LogWarning(F5SnapshotSaveMod.LogPrefix + "history details UI: " + e.Message); }
        }
    }
}
