using System;
using System.Globalization;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace F5ForceSave
{
    // Only this mod's count row needs a numeric readout. Other rows and applying
    // or resetting their widgets remain in ModConfigWindow.
    [HarmonyPatch(typeof(ModConfigWindow), "BuildEntry")]
    internal static class SaveSettingsCountRowPatch
    {
        private static bool Prefix(CustomUIBuilder b, ModConfig config, ConfigEntry entry)
        {
            if (config.ModId != F5ForceSaveMod.ModId || entry.Key != SaveSettings.CountKey) return true;
            string label = entry.Label();
            if (entry.Tooltip != null)
            {
                string link = "modcfg:" + config.ModId + ":" + entry.Key;
                TMPIconHover.CustomLinks[link] = entry.Tooltip;
                label += " <link=\"" + link + "\"><sprite name=\"info\" tint=1></link>";
            }
            b.BeginRow(4f, TextAnchor.MiddleLeft);
            b.AddLabel(label, "label_" + entry.Key);
            b.AddFlexibleSpace();
            b.AddLabel(ConfigEntry.Format(entry.BoxedValue), "value_" + entry.Key);
            b.Last.SetFixedWidth(56f);
            b.Last.text.alignment = TextAlignmentOptions.Right;
            b.Last.text.enableWordWrapping = false;
            b.AddSlider(entry.Min, entry.Max, Convert.ToSingle(entry.BoxedValue, CultureInfo.InvariantCulture), null, true, entry.Key);
            b.Last.SetFixedWidth(306f);
            b.End();
            return false;
        }
    }

    internal sealed class SaveSettingsControls
    {
        private static WeakReference<SaveSettingsControls> active;
        private readonly CustomUIWindow window;
        private readonly ModConfig config;
        private readonly CustomUIElement limit, match, slider, value;
        private int custom;
        private bool wasMatched;

        private SaveSettingsControls(CustomUIWindow window, ModConfig config)
        {
            this.window = window;
            this.config = config;
            limit = window.Get(SaveSettings.LimitKey);
            match = window.Get(SaveSettings.MatchKey);
            slider = window.Get(SaveSettings.CountKey);
            value = window.Get("value_" + SaveSettings.CountKey);
            custom = ((ConfigEntry<int>)config.Get(SaveSettings.CountKey)).Value;
        }

        internal static void Attach(CustomUIWindow window)
        {
            if (window == null || window.id != "mod_config:" + F5ForceSaveMod.ModId) return;
            var view = new SaveSettingsControls(window, ModConfig.Find(F5ForceSaveMod.ModId));
            active = new WeakReference<SaveSettingsControls>(view);
            view.limit.OnToggle(_ => view.Refresh());
            view.match.OnToggle(_ => view.Refresh());
            view.slider.OnSlider(v =>
            {
                if (!view.match.GetBool()) view.custom = Mathf.RoundToInt(v);
                view.Refresh();
            });
            view.Refresh();
        }

        internal static SaveSettingsControls For(CustomUIWindow window)
        {
            SaveSettingsControls view;
            return active != null && active.TryGetTarget(out view) && ReferenceEquals(view.window, window) ? view : null;
        }

        internal static void RefreshOpen()
        {
            SaveSettingsControls view;
            if (active != null && active.TryGetTarget(out view) && view.window.IsAlive) view.Refresh();
        }

        internal void Refresh()
        {
            bool matched = match.GetBool();
            if (matched && !wasMatched) custom = Mathf.RoundToInt(slider.GetFloat());
            wasMatched = matched;
            int count = SaveSettings.EffectiveLimit(matched, EmporiumOption.SaveHistoryDays, custom);
            slider.SetFloat(count);
            slider.SetInteractable(SaveSettings.CanAdjust(limit.GetBool(), matched));
            match.SetInteractable(limit.GetBool());
            value.SetText(count.ToString(CultureInfo.InvariantCulture));
        }

        internal void PrepareApply()
        {
            // Save the user's custom limit even while showing the matched game
            // value. Switching matching off restores that custom value.
            if (match.GetBool()) slider.SetFloat(custom);
        }

        internal void Reset()
        {
            custom = ((ConfigEntry<int>)config.Get(SaveSettings.CountKey)).Default;
            wasMatched = match.GetBool();
            Refresh();
        }
    }

    [HarmonyPatch(typeof(ModConfigWindow), nameof(ModConfigWindow.Open))]
    internal static class SaveSettingsOpenPatch
    {
        private static void Postfix(CustomUIWindow __result)
        {
            try { SaveSettingsControls.Attach(__result); }
            catch (Exception e) { Debug.LogWarning(F5ForceSaveMod.LogPrefix + "settings UI: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(ModConfigWindow), "Apply")]
    internal static class SaveSettingsApplyPatch
    {
        private static void Prefix(CustomUIWindow window) { SaveSettingsControls.For(window)?.PrepareApply(); }
        private static void Postfix(CustomUIWindow window)
        {
            var view = SaveSettingsControls.For(window);
            if (view == null) return;
            view.Refresh();
            Debug.Log(F5ForceSaveMod.LogPrefix + "copy exclusion applied: " + (SaveSettings.ExcludeManualOnCopy ? "on" : "off"));
        }
    }

    [HarmonyPatch(typeof(ModConfigWindow), "ResetWidgets")]
    internal static class SaveSettingsResetPatch
    {
        private static void Postfix(CustomUIWindow window) { SaveSettingsControls.For(window)?.Reset(); }
    }

    [HarmonyPatch(typeof(GameOptionUIManager), nameof(GameOptionUIManager.OnApplySetting))]
    internal static class SaveSettingsGameOptionsPatch
    {
        private static void Postfix() { SaveSettingsControls.RefreshOpen(); }
    }
}
