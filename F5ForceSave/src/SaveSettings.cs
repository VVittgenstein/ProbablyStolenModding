using System;

namespace F5ForceSave
{
    internal static class SaveSettings
    {
        internal const int MaxManualLimit = 100;
        internal const string LimitKey = "limit_manual_saves";
        internal const string MatchKey = "manual_limit_match_game";
        internal const string CountKey = "manual_save_limit";
        internal const string CopyKey = "exclude_manual_on_copy";
        private const string Section = "存档";
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
            limitManual = config.Define(LimitKey, true, Section,
                () => "限制手动保存数量",
                () => "限制每个存档中手动保存的数量。勾选时按设定上限保留，超出的旧文件会在再次保存时清理；取消勾选后不限制数量。自动保存仍按游戏设置保留。");
            matchGame = config.Define(MatchKey, true, Section,
                () => "手动保存上限匹配原版",
                () => "勾选时，手动保存上限与游戏的“每局保存天数”相同，并随其变化；取消勾选后可单独设置。");
            manualLimit = config.Define(CountKey, Math.Max(1, Math.Min(MaxManualLimit, originalLimit)), Section,
                () => "手动保存保留上限", 1, MaxManualLimit,
                () => "每个存档中手动保存的最多数量。达到上限后，会清理旧文件。勾选“匹配原版”时不可修改；未启用“限制手动保存数量”时，此设置不生效。");
            excludeManualOnCopy = config.Define(CopyKey, false, Section,
                () => "复制存档时排除手动保存", CopyTooltip);
            // Entries use the native Apply flow. The copier reads the currently
            // registered value, so applied changes do not require a restart.
        }

        internal static int EffectiveLimit(bool match, int original, int custom)
        {
            return Math.Max(1, Math.Min(match ? EmporiumOption.SAVE_HISTORY_DAYS_MAX : MaxManualLimit, match ? original : custom));
        }

        internal static bool CanAdjust(bool limited, bool match) { return limited && !match; }

        internal static string CopyTooltip()
        {
            return "点击“应用”后生效，无需重启，仅影响之后创建的副本。复制存档时仅保留自动保存。若最新存档为 F5，副本将从最近的自动保存开始，可能不包含最新的 F5 保存。没有自动保存时无法复制。";
        }
    }
}
