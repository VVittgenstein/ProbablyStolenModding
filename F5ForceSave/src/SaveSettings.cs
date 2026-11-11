using System;

namespace F5ForceSave
{
    // The "Saves" group of this mod's settings in the Mods panel (from lin-and-tree's pull request #1): whether the number of
    // F5 saves per slot is limited, whether the limit follows "Saved days per run" in the game options, the limit otherwise,
    // and whether copying a slot on the Load Game screen leaves the F5 saves out (CopyHistory). The defaults keep the
    // retention of 1.7.0. 1.8.0: names and explanations in Chinese, English and Russian; the group name is fixed text in the
    // three languages, as the key group's, because the game takes it once when the entry is defined; lowering the limit asks
    // for confirmation (SaveSettingsUi). With F5 Quick Save Plus running, its own settings apply.
    internal static class SaveSettings
    {
        internal const int MaxManualLimit = 100;
        internal const string LimitKey = "limit_manual_saves";
        internal const string MatchKey = "manual_limit_match_game";
        internal const string CountKey = "manual_save_limit";
        internal const string CopyKey = "exclude_manual_on_copy";
        private const string Section = "存档 / Saves / Сохранения";
        private static ConfigEntry<bool> limitManual;
        private static ConfigEntry<bool> matchGame;
        private static ConfigEntry<int> manualLimit;
        private static ConfigEntry<bool> excludeManualOnCopy;

        internal static bool LimitManual { get { return limitManual == null || limitManual.Value; } }
        internal static bool MatchGame { get { return matchGame == null || matchGame.Value; } }
        internal static int ManualLimit { get { return EffectiveLimit(MatchGame, EmporiumOption.SaveHistoryDays, manualLimit == null ? EmporiumOption.SaveHistoryDays : manualLimit.Value); } }
        internal static bool ExcludeManualOnCopy
        {
            get
            {
                var live = ModConfig.Find(F5ForceSaveMod.ModId)?.Get(CopyKey) as ConfigEntry<bool>;
                return (live ?? excludeManualOnCopy)?.Value ?? false;
            }
        }

        // The limit in effect; 0 when F5 saves are not limited.
        internal static int CurrentLimit()
        {
            return LimitManual ? ManualLimit : 0;
        }

        internal static void Load()
        {
            // ModLoader can initialize before EmporiumOption. In that case read
            // the same existing setting the game itself loads, without saving it.
            int originalLimit = EmporiumOption.SaveHistoryDays;
            if (EmporiumOption.Instance == null)
                originalLimit = Math.Max(1, Math.Min(EmporiumOption.SAVE_HISTORY_DAYS_MAX,
                    ES3.Load<int>("saveHistoryDays", EmporiumOption.SAVE_HISTORY_DAYS_DEFAULT)));
            Define(ModHelper.GetConfig(F5ForceSaveMod.ModId), originalLimit);
        }

        internal static void Define(ModConfig config, int originalLimit)
        {
            limitManual = config.Define(LimitKey, true, Section, Text.LimitSettingLabel, Text.LimitSettingTooltip);
            matchGame = config.Define(MatchKey, true, Section, Text.MatchSettingLabel, Text.MatchSettingTooltip);
            manualLimit = config.Define(CountKey, Math.Max(1, Math.Min(MaxManualLimit, originalLimit)), Section,
                Text.CountSettingLabel, 1, MaxManualLimit, Text.CountSettingTooltip);
            excludeManualOnCopy = config.Define(CopyKey, false, Section, Text.CopySettingLabel, Text.CopySettingTooltip);
            // Entries use the native Apply flow. The copier reads the currently
            // registered value, so applied changes do not require a restart.
        }

        internal static int EffectiveLimit(bool match, int original, int custom)
        {
            return Math.Max(1, Math.Min(match ? EmporiumOption.SAVE_HISTORY_DAYS_MAX : MaxManualLimit, match ? original : custom));
        }

        internal static bool CanAdjust(bool limited, bool match) { return limited && !match; }
    }
}
