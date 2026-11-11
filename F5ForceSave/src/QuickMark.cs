using System;
using System.Collections.Generic;
using HarmonyLib;

namespace F5ForceSave
{
    // 1.8.0: every F5 save carries a mark in its own record of the save index. The game keeps the save time of each save in
    // saves_index.es3 in units of 100 nanoseconds (SavePreviewData.lastSaveDateTime for the newest save of a slot,
    // SaveHistoryEntry.saveDateTime for the older ones) and copies it unchanged when the slot file moves into the history
    // (SaveFiles.RotateIntoHistory) and when a slot is copied (SaveFiles.DuplicateSlot, SavePreviewData.CloneForSlot); the
    // screens and the history file names use whole seconds. An F5 save sets the part of its time below one second to Code,
    // so the mark moves, is copied and is deleted together with the save. Up to 1.7.0 the F5 saves of all slots were listed
    // by time in one record (QuickHistory.LegacyKey), and deleting the older saves of one slot also took the time of a copied
    // F5 save of another slot out of that list (reported in pull request #1 on GitHub). Without the mod the game reads the
    // time as before.
    internal static class QuickMark
    {
        // Ticks within the second (0 to 9 999 999) that only an F5 save carries.
        internal const long Code = 4715309;

        internal static bool Has(DateTime time)
        {
            return time.Ticks % TimeSpan.TicksPerSecond == Code;
        }

        // Same second, so the time shown and the history file name do not change.
        internal static DateTime Apply(DateTime time)
        {
            return new DateTime(time.Ticks - time.Ticks % TimeSpan.TicksPerSecond + Code, time.Kind);
        }

        // A save of the game that would carry the code, or no part below one second, by chance is moved by 100 nanoseconds:
        // whole seconds are left to the records the game makes from the files on disk (QuickHistory.IsRelisted).
        internal static DateTime Avoid(DateTime time)
        {
            return Has(time) || time.Ticks % TimeSpan.TicksPerSecond == 0 ? time.AddTicks(1) : time;
        }

        internal static DateTime WholeSeconds(DateTime time)
        {
            return new DateTime(time.Ticks - time.Ticks % TimeSpan.TicksPerSecond, time.Kind);
        }

        // Puts the mark on the record of a save that holds F5 data but has no mark: an F5 save made before 1.8.0, or a record
        // the game made again from the file (the Discover button of the Load Game screen). file is the history file that was
        // loaded, or empty for the slot file (NewGameData.saveFile, see SaveFiles.ResolveLoadFile). Returns true when a
        // record was changed.
        internal static bool EnsureMarked(int slotId, string file)
        {
            if (slotId < 0)
                return false;
            if (string.IsNullOrEmpty(file) || string.Equals(file, SaveFiles.Slot(slotId), StringComparison.OrdinalIgnoreCase))
            {
                SavePreviewData preview = SaveFiles.IndexLoad<SavePreviewData>("preview_" + slotId, null);
                if (preview == null || preview.lastSaveDateTime.Year <= 1 || Has(preview.lastSaveDateTime))
                    return false;
                preview.lastSaveDateTime = Apply(preview.lastSaveDateTime);
                // Writes preview_<slot> and the copy kept by the Load Game screen (SaveUIManager.UpdateCacheEntry).
                SaveFiles.RegisterSlot(slotId, preview);
                return true;
            }
            List<SaveHistoryEntry> history = SaveFiles.LoadHistory(slotId);
            if (history == null)
                return false;
            foreach (SaveHistoryEntry entry in history)
            {
                if (entry == null || !string.Equals(entry.file, file, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (Has(entry.saveDateTime))
                    return false;
                entry.saveDateTime = Apply(entry.saveDateTime);
                // The key that SaveFiles.SaveHistory writes; LoadHistory gives the list in the order the game keeps.
                SaveFiles.IndexSave<List<SaveHistoryEntry>>("history_" + slotId, history);
                return true;
            }
            return false;
        }
    }

    // PlayerStore.SaveGame writes the slot file and then the record of the slot (SavePreviewData.GenerateData, lastSaveDateTime
    // = DateTime.Now) into the save index and into the copy of the Load Game screen. Only the mod that handles F5 changes the
    // time: with F5 Quick Save Plus running this patch does nothing, otherwise it would move the mark of a Plus F5 save.
    [HarmonyPatch(typeof(SavePreviewData), nameof(SavePreviewData.GenerateData))]
    internal static class QuickMarkPatch
    {
        private static void Postfix(SavePreviewData __result)
        {
            if (__result == null || !QuickHistory.Handles())
                return;
            __result.lastSaveDateTime = QuickSave.InProgress
                ? QuickMark.Apply(__result.lastSaveDateTime)
                : QuickMark.Avoid(__result.lastSaveDateTime);
        }
    }

    // SaveFiles.BuildPreviewFromStore makes the record of a slot from its slot file when the save index does not hold it
    // (SaveFiles.ScanAndRegisterOrphans: the repair of the index and the Discover button of the Load Game screen; also
    // RepairDamagedRun, ImportFromFile, and DuplicateSlot for a slot without a record), with the time the file was last
    // written. Whether that slot file is an F5 save is not known here: F5 data left in PlayerStore.modData does not prove
    // it (a save made without the mods keeps the data of an F5 save loaded before). The time is set to whole seconds, as
    // the game sets it for the history files it lists from the disk, so that QuickHistory keeps the save and counts it as
    // neither kind (QuickHistory.IsRelisted); loading it puts the mark on it if it is an F5 save (QuickMark.EnsureMarked).
    [HarmonyPatch(typeof(SaveFiles), "BuildPreviewFromStore")]
    internal static class RebuiltPreviewPatch
    {
        private static void Postfix(SavePreviewData __result)
        {
            if (__result == null || !QuickHistory.Handles())
                return;
            __result.lastSaveDateTime = QuickMark.WholeSeconds(__result.lastSaveDateTime);
        }
    }
}
