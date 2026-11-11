using System;
using System.Globalization;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace F5SnapshotSave
{
    // The settings window of this mod in the Mods panel (ModConfigWindow, from lin-and-tree's pull request #1): the limit row
    // shows its number, and the limit follows the two toggles above it. Applying and resetting the other rows stays with
    // ModConfigWindow.
    [HarmonyPatch(typeof(ModConfigWindow), "BuildEntry")]
    internal static class SaveSettingsCountRowPatch
    {
        private static bool Prefix(CustomUIBuilder b, ModConfig config, ConfigEntry entry)
        {
            if (config.ModId != F5SnapshotSaveMod.ModId || entry.Key != SaveSettings.CountKey) return true;
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
        private const string ConfirmWindowId = "f5snapshotsave_confirm_limit";
        private static WeakReference<SaveSettingsControls> active;
        private readonly CustomUIWindow window;
        private readonly ModConfig config;
        private readonly CustomUIElement limit, match, slider, value;
        private int custom;
        private bool wasMatched;
        private bool confirmed;

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
            if (window == null || window.id != "mod_config:" + F5SnapshotSaveMod.ModId) return;
            var view = new SaveSettingsControls(window, ModConfig.Find(F5SnapshotSaveMod.ModId));
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

        // The limit the window would apply; 0 for no limit.
        private int PendingLimit()
        {
            return limit.GetBool() ? SaveSettings.EffectiveLimit(match.GetBool(), EmporiumOption.SaveHistoryDays, custom) : 0;
        }

        // 0.7.0, as F5ForceSave 1.8.0: lowering the limit, or limiting again, asks first: the F5 saves already kept are not deleted because of it,
        // and each new F5 save then deletes only the oldest one (QuickHistory.OrderForRetention). Returns true while the
        // question is open; Confirm applies the window again. When the question cannot be shown the change is applied, since
        // it deletes nothing by itself.
        internal bool AskBeforeApply()
        {
            if (confirmed)
            {
                confirmed = false;
                return false;
            }
            int before = SaveSettings.CurrentLimit();
            int after = PendingLimit();
            if (after <= 0 || (before > 0 && after >= before))
                return false;
            try
            {
                ShowQuestion(after);
                Debug.Log(F5SnapshotSaveMod.LogPrefix + "lowering the F5 save limit from " + (before > 0 ? before.ToString(CultureInfo.InvariantCulture) : "no limit")
                    + " to " + after + ": asking for confirmation");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning(F5SnapshotSaveMod.LogPrefix + "the confirmation could not be shown; the settings are applied: " + e);
                return false;
            }
        }

        private void ShowQuestion(int after)
        {
            CustomUIManager manager = CustomUIManager.Instance;
            if (manager == null)
                throw new InvalidOperationException("no CustomUIManager");
            if (manager.IsOpen(ConfirmWindowId))
                manager.CloseWindow(ConfirmWindowId);
            // The layer of the settings window (ModConfigWindow.Open): the main menu has no StoreUIManager.
            string layer = StoreUIManager.Instance != null ? "meta" : "menu";
            CustomUIBuilder b = manager.CreateWindow(ConfirmWindowId, Text.LowerLimitTitle(), layer);
            b.SetSize(760f, 240f).Center().SetCloseOnEscape(true);
            b.AddLabel(Text.LowerLimitLine1(after), "line1");
            b.AddLabel(Text.LowerLimitLine2(), "line2");
            b.AddLabel(Text.LowerLimitLine3(), "line3");
            b.AddSpace(8f);
            b.BeginRow(12f, TextAnchor.MiddleCenter);
            b.AddFlexibleSpace();
            b.AddButton(Text.CancelButton(), () => Answer(false), "cancel");
            b.Last.SetFixedWidth(180f);
            b.AddButton(Text.ConfirmButton(), () => Answer(true), "confirm");
            b.Last.SetFixedWidth(180f);
            b.AddFlexibleSpace();
            b.End();
            CustomUIWindow question = b.Show();
            question.BringToFront();
        }

        private void Answer(bool yes)
        {
            CustomUIManager manager = CustomUIManager.Instance;
            if (manager != null && manager.IsOpen(ConfirmWindowId))
                manager.CloseWindow(ConfirmWindowId);
            Debug.Log(F5SnapshotSaveMod.LogPrefix + "lowering the F5 save limit " + (yes ? "confirmed" : "cancelled; nothing changed"));
            if (!yes || !window.IsAlive)
                return;
            CustomUIElement apply = window.Get("apply");
            if (apply == null || !apply.IsAlive || apply.button == null)
            {
                Debug.LogWarning(F5SnapshotSaveMod.LogPrefix + "the Apply button of the settings window was not found; nothing changed");
                return;
            }
            confirmed = true;
            apply.button.onClick.Invoke();
            confirmed = false;
        }
    }

    [HarmonyPatch(typeof(ModConfigWindow), nameof(ModConfigWindow.Open))]
    internal static class SaveSettingsOpenPatch
    {
        private static void Postfix(CustomUIWindow __result)
        {
            try { SaveSettingsControls.Attach(__result); }
            catch (Exception e) { Debug.LogWarning(F5SnapshotSaveMod.LogPrefix + "settings UI: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(ModConfigWindow), "Apply")]
    internal static class SaveSettingsApplyPatch
    {
        private static bool Prefix(CustomUIWindow window, out bool __state)
        {
            __state = false;
            SaveSettingsControls view = SaveSettingsControls.For(window);
            if (view == null) return true;
            if (view.AskBeforeApply()) return false;
            view.PrepareApply();
            __state = true;
            return true;
        }

        private static void Postfix(CustomUIWindow window, bool __state)
        {
            if (!__state) return;
            SaveSettingsControls view = SaveSettingsControls.For(window);
            if (view == null) return;
            view.Refresh();
            int limit = SaveSettings.CurrentLimit();
            Debug.Log(F5SnapshotSaveMod.LogPrefix + "save settings applied: F5 saves " + (limit > 0 ? "limited to " + limit.ToString(CultureInfo.InvariantCulture) : "not limited")
                + ", copy exclusion " + (SaveSettings.ExcludeManualOnCopy ? "on" : "off"));
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
