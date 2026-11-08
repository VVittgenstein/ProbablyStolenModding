using System;
using System.Globalization;

namespace F5SnapshotSave
{
    // Texts shown in the game: Chinese when the game language is Chinese, Russian when it is Russian, English otherwise.
    internal static class Text
    {
        private static string LanguageCode()
        {
            UnityEngine.Localization.Locale locale = ModHelper.GetCurrentLocale();
            return locale != null ? locale.Identifier.Code : null;
        }

        private static string T(string zh, string en, string ru)
        {
            string code = LanguageCode();
            if (code != null && code.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
                return zh;
            if (code != null && code.StartsWith("ru", StringComparison.OrdinalIgnoreCase))
                return ru;
            return en;
        }

        private static string Clock(DateTime time)
        {
            return time.ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        // Added after the day and money of a save on the Load Game screen.
        internal static string SaveKind(bool quick)
        {
            if (quick)
                return " | F5";
            return T(" | 自动", " | Auto", " | Авто");
        }

        private static string Phase(PlayerStore.StoreState state)
        {
            switch (state)
            {
                case PlayerStore.StoreState.OPEN: return T("营业中", "open", "магазин открыт");
                case PlayerStore.StoreState.CLOSED: return T("打烊后", "after closing", "после закрытия");
                default: return T("早上", "morning", "утро");
            }
        }

        private static string ReturnPoint(SavePreviewData last)
        {
            if (last == null)
                return "";
            string clock = Clock(last.lastSaveDateTime);
            return T("现在退出会回到第" + last.dayCount + "天 " + clock + " 的存档。",
                " Quitting now returns to the save of day " + last.dayCount + " at " + clock + ".",
                " Если выйти сейчас, игра вернётся к сохранению дня " + last.dayCount + " (" + clock + ").");
        }

        internal static string Saved(int day, PlayerStore.StoreState state, DateTime time, bool withCounter, int waiting)
        {
            string phase = Phase(state);
            return T("已存档：第" + day + "天" + phase + " " + Clock(time)
                    + (withCounter ? "，柜台上的顾客一起存下" : "")
                    + (waiting > 0 ? "，排队顾客 " + waiting + " 位" : ""),
                "Saved: day " + day + ", " + phase + ", " + Clock(time)
                    + (withCounter ? ", with the customer at the counter" : "")
                    + (waiting > 0 ? ", " + waiting + " customer(s) waiting" : ""),
                "Сохранено: день " + day + ", " + phase + ", " + Clock(time)
                    + (withCounter ? ", вместе с клиентом у прилавка" : "")
                    + (waiting > 0 ? ", клиентов в очереди: " + waiting : ""));
        }

        internal static string Busy()
        {
            string key = SaveKey.Name();
            return T("顾客还在走动或说话。等一句话说完、出现选项或对话结束后再按 " + key + "。",
                "The customer is still walking or talking. Press " + key + " when a line has finished, choices are shown, or the conversation is over.",
                "Клиент ещё идёт или говорит. Нажмите " + key + ", когда реплика закончится, появятся варианты ответа или разговор завершится.");
        }

        internal static string Security()
        {
            return T("逮捕、枪战或保安在场时不能存档。", "Can't save during an arrest or a shootout, or while security is here.",
                "Нельзя сохраниться во время ареста или перестрелки, а также пока здесь охрана.");
        }

        internal static string Inspecting()
        {
            string key = SaveKey.Name();
            return T("检查物品时不能存档，关掉检查界面后再按 " + key + "。", "Can't save while inspecting an item. Close the inspection, then press " + key + ".",
                "Нельзя сохраниться во время осмотра предмета. Закройте осмотр и нажмите " + key + ".");
        }

        internal static string Phone()
        {
            return T("通话中不能存档。", "Can't save during a phone call.", "Нельзя сохраниться во время телефонного разговора.");
        }

        internal static string CounterNotStorable(SavePreviewData last)
        {
            string key = SaveKey.Name();
            return T("柜台上这位顾客现在存不进存档（详见 Player.log），等顾客离开后再按 " + key + "。",
                "The customer at the counter can't be stored right now (see Player.log). Press " + key + " after they leave.",
                "Клиента у прилавка сейчас нельзя сохранить (подробности в Player.log). Нажмите " + key + ", когда он уйдёт.") + ReturnPoint(last);
        }

        internal static string QueueNotStorable(SavePreviewData last)
        {
            return T("排队的顾客现在存不进存档（详见 Player.log）。",
                "The waiting customers can't be stored right now (see Player.log).",
                "Клиентов в очереди сейчас нельзя сохранить (подробности в Player.log).") + ReturnPoint(last);
        }

        internal static string TemporaryNotEmpty()
        {
            string key = SaveKey.Name();
            return T("有物品在不会存档的临时位置，先移回店里再按 " + key + "。",
                "Items in a temporary area are not saved. Move them back, then press " + key + ".",
                "Предметы во временной зоне не сохраняются. Верните их в магазин и нажмите " + key + ".");
        }

        internal static string Dragging()
        {
            string key = SaveKey.Name();
            return T("松开拖动的物品后再按 " + key + "。", "Release the dragged item, then press " + key + ".", "Отпустите перетаскиваемый предмет и нажмите " + key + ".");
        }

        internal static string OutsideStore()
        {
            return T("在店外不能存档。", "Can't save outside the store.", "Вне магазина сохраняться нельзя.");
        }

        internal static string GameOver()
        {
            return T("游戏已结束，不能存档。", "Can't save after game over.", "Игра окончена, сохраниться нельзя.");
        }

        internal static string Failed()
        {
            return T("存档失败，详见 Player.log。", "Save failed, see Player.log.", "Сохранить не удалось, подробности в Player.log.");
        }

        internal static string HighlightConflict()
        {
            string key = SaveKey.Name();
            return T("“客户需求高亮”Mod 也用 " + key + "，高亮同时被切换，可在选项“按键绑定”中改键。",
                "Client Demand Highlight also uses " + key + ", so its highlight toggled too. Change a key under Key Binds.",
                "Client Demand Highlight тоже использует " + key + ", подсветка переключилась. Смените клавишу в разделе «Назначение клавиш».");
        }

        internal static string FellBack()
        {
            return T("这个存档里的顾客无法原样恢复（通常是存档后游戏或 Mod 有更新，详见 Player.log）：柜台上的顾客改为排在队首，对话从头开始；排队顾客的预算和物品重新随机。",
                "The customers in this save can't be restored as they were (usually because the game or a mod was updated after the save; see Player.log): the customer at the counter now waits first in line and starts over, and the waiting customers' budgets and goods are rolled again.",
                "Клиентов из этого сохранения не удаётся восстановить в прежнем виде (обычно потому, что после сохранения обновилась игра или мод; подробности в Player.log): клиент у прилавка теперь стоит первым в очереди и начнёт разговор заново, а бюджеты и товары клиентов в очереди выбраны заново.");
        }

        internal static string TookOver()
        {
            string key = SaveKey.Name();
            return T("F5 存档加强版已接管存档，存档键为 " + key + "（在游戏选项“按键绑定”中修改）。F5 存档（F5ForceSave）可以在 Mod 面板中停用。",
                "F5 Quick Save Plus now handles saving; the save key is " + key + " (change it under Key Binds in the game options). F5 Quick Save (F5ForceSave) can be disabled in the Mods panel.",
                "Сохранением теперь занимается F5 Quick Save Plus, клавиша сохранения: " + key + " (её можно изменить в настройках игры, в разделе «Назначение клавиш»). F5 Quick Save (F5ForceSave) можно отключить в панели модификаций.");
        }

        // The row in the Key Binds section of the game options.
        internal static string KeyOptionLabel()
        {
            return T("立即存档（F5 存档加强版 Mod）", "Save now (F5 Quick Save Plus mod)", "Сохранить сейчас (мод F5 Quick Save Plus)");
        }

        // The entry in this mod's settings window in the Mods panel.
        internal static string KeySettingLabel()
        {
            return T("存档键", "Save key", "Клавиша сохранения");
        }

        internal static string KeySettingTooltip()
        {
            return T("也可以在游戏选项的“按键绑定”中修改。", "Can also be changed under Key Binds in the game options.", "Можно изменить и в настройках игры, в разделе «Назначение клавиш».");
        }

        internal static string LoadKeyOptionLabel()
        {
            return T("快速读档（F5 存档加强版 Mod）", "Quick load (F5 Quick Save Plus mod)", "Быстрая загрузка (мод F5 Quick Save Plus)");
        }

        internal static string LoadKeySettingLabel()
        {
            return T("读档键", "Load key", "Клавиша загрузки");
        }

        internal static string F5LoadKeyOptionLabel()
        {
            return T("读取 F5 存档（F5 存档加强版 Mod）", "Load F5 save (F5 Quick Save Plus mod)", "Загрузка сохранения F5 (мод F5 Quick Save Plus)");
        }

        internal static string F5LoadKeySettingLabel()
        {
            return T("读 F5 存档键", "F5 save load key", "Клавиша загрузки сохранения F5");
        }

        internal static string NoF5Save()
        {
            return T("这个存档位还没有 F5 存档。", "This slot has no F5 save yet.", "В этом слоте пока нет сохранения F5.");
        }

        internal static string AlwaysHighlightConflict()
        {
            string key = F5LoadKey.Name();
            return T("“Always Highlight”Mod 也用 " + key + "，可在选项“按键绑定”中更换本 Mod 的键。",
                "Always Highlight also uses " + key + ". Change this mod's key under Key Binds.",
                "Always Highlight тоже использует " + key + ". Смените клавишу этого мода в разделе «Назначение клавиш».");
        }

        // The notification shows one text and keeps it on one line while it has no line break, so a second sentence follows the
        // first on that line.
        internal static string SameLine(string first, string second)
        {
            bool ended = first.EndsWith("。", StringComparison.Ordinal) || first.EndsWith(".", StringComparison.Ordinal);
            string other = first + (ended ? " " : ". ") + second;
            return T(first + (ended ? "" : "。") + second, other, other);
        }

        internal static string NothingToLoad()
        {
            return T("没有可以读取的存档。", "There is no save to load.", "Нет сохранения для загрузки.");
        }
    }
}
