using System;
using System.Collections.Generic;
using HarmonyLib;
using Newtonsoft.Json;

namespace F5SnapshotSave
{
    // The game consumes nightLogs when it builds the report. Later openings reuse that UI,
    // which does not survive a scene reload. Keep only the displayed text, never night actions.
    internal static class MorningReport
    {
        private const string DataOwner = "F5ForceSave";
        private const string DataKey = "report.lastMorning";
        private static bool rebuilding;

        private sealed class SavedReport
        {
            public int Day { get; set; }
            public List<string> Lines { get; set; }
        }

        internal static void Capture()
        {
            if (rebuilding || Coexist.Failed) return;
            PlayerStore store = PlayerStore.Instance;
            StoreStation station = StoreStation.Instance;
            if (store == null || station == null || store.nightLogs == null) return;
            ModHelper.SetModData(DataOwner, DataKey, JsonConvert.SerializeObject(new SavedReport {
                Day = station.dayCounter, Lines = new List<string>(store.nightLogs)
            }));
        }

        internal static void RestoreDisplay()
        {
            if (Coexist.Failed) return;
            PlayerStore store = PlayerStore.Instance;
            StoreStation station = StoreStation.Instance;
            StartOfDayUIManager ui = StartOfDayUIManager.Instance;
            if (store == null || station == null || ui == null || ui.contentGroupObject == null
                || ui.contentGroupObject.transform.childCount != 0 || store.nightLogs == null
                || store.nightLogs.Count != 0) return;
            string json = ModHelper.GetModData(DataOwner, DataKey, "");
            if (string.IsNullOrEmpty(json)) return;
            try
            {
                SavedReport saved = JsonConvert.DeserializeObject<SavedReport>(json, new JsonSerializerSettings {
                    TypeNameHandling = TypeNameHandling.None, MetadataPropertyHandling = MetadataPropertyHandling.Ignore
                });
                if (saved == null || saved.Day != station.dayCounter || saved.Lines == null || saved.Lines.Count == 0) return;
                List<string> pendingLogs = store.nightLogs;
                List<Action> pendingActions = store.nightlyAction;
                try
                {
                    rebuilding = true;
                    store.nightLogs = new List<string>(saved.Lines);
                    store.nightlyAction = new List<Action>();
                    ui.InitPanel();
                }
                finally
                {
                    store.nightLogs = pendingLogs;
                    store.nightlyAction = pendingActions;
                    rebuilding = false;
                }
                UnityEngine.Debug.Log(F5SnapshotSaveMod.LogPrefix + "restored morning report text for day " + saved.Day);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning(F5SnapshotSaveMod.LogPrefix + "restoring morning report text failed: " + e);
            }
        }
    }

    [HarmonyPatch(typeof(StartOfDayUIManager), nameof(StartOfDayUIManager.InitPanel))]
    internal static class CaptureMorningReportPatch
    {
        private static void Prefix() { MorningReport.Capture(); }
    }
}
