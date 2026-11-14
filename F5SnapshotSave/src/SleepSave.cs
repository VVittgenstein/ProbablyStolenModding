using System;
using System.Collections.Generic;
using HarmonyLib;

namespace F5SnapshotSave
{
    // T-031: the save before sleeping. With "Save before sleeping" on (SaveSettings.SleepKey, off by default), clicking
    // "End the Day" on the End of Day tablet first makes an F5 save, as the save key does (QuickSave.SaveBeforeSleep): it
    // carries the F5 mark (QuickMark), counts among the F5 saves kept (QuickHistory) and the F5 save load key loads it.
    // The game saves only after that button: EndOfDayUIManager.OnEndDayButtonClick fades to black, sets the store to
    // MORNING, runs the night (PlayerStore.EndNight), begins the next day and saves it. This save is made before all of
    // that, in the state after closing. The bedroll (StoreBedroll.OnClick, after closing) and MapUIManager.Sleep open the
    // tablet; closing it makes no save. When F5 refuses the save at that moment, nothing is saved and the night goes on.
    // The screen fades to black at once, so the message, and the window naming incompatible mods after it, wait for the
    // morning report of the next day (StartOfDayUIManager.OpenUI, which the game calls after the night unless the game
    // is over). With F5 Quick Save (F5ForceSave) also enabled, this mod makes the save with its own setting; when the
    // arrangement with F5ForceSave fails (Coexist.Failed), F5ForceSave makes it.
    internal static class SleepSave
    {
        private static string pendingNotice;
        private static List<string> pendingMods;
        private static int pendingDay = -1;

        internal static void BeforeNight()
        {
            pendingNotice = null;
            pendingMods = null;
            pendingDay = -1;
            if (!SaveSettings.SaveBeforeSleep || Coexist.Failed)
                return;
            try
            {
                bool saved;
                List<string> mods;
                string message = QuickSave.SaveBeforeSleep(out saved, out mods);
                if (message == null)
                {
                    UnityEngine.Debug.LogWarning(F5SnapshotSaveMod.LogPrefix + "the save before sleeping was not made: the store is not there");
                    return;
                }
                pendingNotice = message;
                pendingMods = mods;
                pendingDay = StoreStation.Instance != null ? StoreStation.Instance.dayCounter : -1;
                UnityEngine.Debug.Log(F5SnapshotSaveMod.LogPrefix + (saved ? "saved before sleeping: " : "not saved before sleeping: ") + message);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning(F5SnapshotSaveMod.LogPrefix + "the save before sleeping failed: " + e);
            }
        }

        // The morning report of the day after the night of the click; a message of an earlier night is dropped.
        internal static void ShowNotice()
        {
            string notice = pendingNotice;
            List<string> mods = pendingMods;
            int day = pendingDay;
            pendingNotice = null;
            pendingMods = null;
            pendingDay = -1;
            if (notice == null)
                return;
            try
            {
                StoreStation station = StoreStation.Instance;
                StoreUIManager ui = StoreUIManager.Instance;
                if (station == null || ui == null || station.dayCounter != day + 1)
                    return;
                ui.Notify(notice, "default");
                if (mods != null)
                    IncompatibleNotice.After(mods);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning(F5SnapshotSaveMod.LogPrefix + "showing the message of the save before sleeping failed: " + e);
            }
        }
    }

    [HarmonyPatch(typeof(EndOfDayUIManager), nameof(EndOfDayUIManager.OnEndDayButtonClick))]
    internal static class EndDayButtonPatch
    {
        private static void Prefix()
        {
            SleepSave.BeforeNight();
        }
    }

    [HarmonyPatch(typeof(StartOfDayUIManager), nameof(StartOfDayUIManager.OpenUI))]
    internal static class MorningNoticePatch
    {
        private static void Postfix()
        {
            SleepSave.ShowNotice();
        }
    }
}
