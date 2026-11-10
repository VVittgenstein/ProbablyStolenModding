using System;
using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace F5ForceSave
{
    internal static class SaveDetailsUi
    {
        internal static string LanguageCode()
        {
            var locale = ModHelper.GetCurrentLocale();
            return locale == null ? null : locale.Identifier.Code;
        }
        internal static string Describe(string title, SaveDetails.Record record, bool quick, string language)
        {
            return quick ? title + "\n<indent=10px><size=80%><color=#8FB9B5>" + SaveDetails.Format(record, language) + "</color></size></indent>" : title;
        }

        internal static void Fit(TextMeshProUGUI label, bool plate)
        {
            if (label == null || QuickHistory.HandledByPlus()) return;
            bool detailed = label.text != null && label.text.Contains("\n<indent=10px>");
            // Use the same native text component as DayAndMoney. Its font,
            // localization and canvas are already configured by the game.
            WildNetworkUI.SetRect(label.rectTransform, plate ? 82f : 0f,
                detailed ? (plate ? 48f : 17f) : (plate ? 56f : 25f),
                detailed ? 488f : 308f, detailed ? (plate ? 44f : 46f) : 27f);
            label.alignment = detailed ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.MidlineLeft;
            label.richText = true;
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Overflow;
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
            catch (Exception e) { UnityEngine.Debug.LogWarning(F5ForceSaveMod.LogPrefix + "save details UI: " + e.Message); }
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
            catch (Exception e) { UnityEngine.Debug.LogWarning(F5ForceSaveMod.LogPrefix + "history details UI: " + e.Message); }
        }
    }
}
