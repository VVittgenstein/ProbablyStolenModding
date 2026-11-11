using System;
using System.Collections.Generic;
using System.Globalization;

namespace F5SnapshotSave
{
    // F5 saves and the game's own saves (at the start of each day and for a new game) share the slot's history
    // (SaveFiles.LoadHistory). Before every save the game moves the slot file into the history (RotateIntoHistory)
    // and then keeps "Saved days per run" (EmporiumOption.SaveHistoryDays) saves in all, the slot file included,
    // deleting the oldest history entries (TrimHistory). Here the two kinds are counted apart: the game's saves keep
    // SaveHistoryDays places, the slot file included; the F5 saves keep the number set in this mod's settings
    // (SaveSettings, by default also SaveHistoryDays, or no limit).
    // 0.7.0, as F5ForceSave 1.8.0 (from lin-and-tree's pull request #1, which first counted the kinds with their own limits): a new F5 save deletes
    // at most the oldest F5 save, so F5 saves already kept are not deleted because the limit was lowered; the game's own saves
    // and the listing of history files found on disk do not delete F5 saves.
    // An F5 save is told by the mark in its save time (QuickMark). F5 saves made before 0.7.0 (and before F5ForceSave 1.8.0)
    // have no mark; for them the list of 0.3.0 to 0.6.1 and F5ForceSave 1.4.0 to 1.7.0 under LegacyKey (the save times in
    // whole seconds of the F5 saves of all slots) is read, and no longer written. Loading such a save puts the mark on its
    // record (QuickMark.EnsureMarked). F5 Quick Save (F5ForceSave) 1.8.0 marks and counts in the same way and leaves this
    // work to this mod while this mod runs. On the Load Game screen the day and money of every save end with " | F5" or
    // " | Auto", and an F5 save made since 0.7.0 shows a second line (SaveDetails).
    internal static class QuickHistory
    {
        internal const string LegacyKey = "f5_quick_saves";

        private static bool trimming;
        private static bool newQuickSave;
        private static bool latestIsAuto;
        private static readonly Queue<DateTime> pendingLabels = new Queue<DateTime>();
        private static HashSet<long> labelLegacy;
        private static Dictionary<long, SaveDetails.Record> labelDetails;

        // True when this mod handles F5 saves and the save history. When the arrangement with F5ForceSave fails, this mod
        // does not save and F5ForceSave does this work.
        internal static bool Handles()
        {
            return !Coexist.Failed;
        }

        internal static void BeginRotation(bool quick)
        {
            trimming = Handles();
            newQuickSave = quick;
            latestIsAuto = !quick;
        }

        internal static void EndRotation()
        {
            trimming = false;
            newQuickSave = false;
            latestIsAuto = false;
        }

        // SaveFiles.RestoreHistoryFromDisk (the game 1.0.0.12): the slot file is already in place and takes one place of
        // its kind, as the save being made does in RotateIntoHistory; a slot file whose kind is not known (a record in whole
        // seconds, RebuiltPreviewPatch) takes none. No save is being made, so no F5 save is deleted.
        internal static void BeginRestore(int slotId)
        {
            bool auto = false;
            try
            {
                SavePreviewData preview = SaveFiles.IndexLoad<SavePreviewData>("preview_" + slotId, null);
                HashSet<long> legacy = LoadLegacy();
                auto = preview != null && !IsQuick(preview.lastSaveDateTime, legacy) && !IsRelisted(preview.lastSaveDateTime, legacy);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning(F5SnapshotSaveMod.LogPrefix + "reading the slot file of slot " + slotId + " before listing its history failed; it takes no place: " + e);
            }
            trimming = Handles();
            newQuickSave = false;
            latestIsAuto = auto;
        }

        // SaveFiles.TrimHistory as RotateIntoHistory calls it: history holds the entries newest first (the slot file
        // that was just moved in comes first) and keepCount is SaveHistoryDays - 1. The entries to keep are put first
        // and keepCount is set to their number, so the game removes the others and deletes their files as usual.
        internal static void Trim(List<SaveHistoryEntry> history, ref int keepCount)
        {
            if (!trimming || history == null)
                return;
            try
            {
                int perKind = Math.Max(1, keepCount + 1);
                HashSet<long> legacy = LoadLegacy();
                int quickLimit = SaveSettings.LimitManual ? SaveSettings.ManualLimit : 0;
                keepCount = OrderForRetention(history, latestIsAuto, newQuickSave, perKind, quickLimit,
                    entry => entry != null && IsQuick(entry.saveDateTime, legacy),
                    entry => entry != null && IsRelisted(entry.saveDateTime, legacy));
                if (history.Count == keepCount)
                    return;
                var names = new List<string>();
                foreach (SaveHistoryEntry entry in history.GetRange(keepCount, history.Count - keepCount))
                {
                    if (entry != null)
                        names.Add(entry.file);
                }
                UnityEngine.Debug.Log(F5SnapshotSaveMod.LogPrefix + "save history: " + perKind + " of the game's saves kept, F5 saves "
                    + (quickLimit > 0 ? "limited to " + quickLimit.ToString(CultureInfo.InvariantCulture) : "not limited")
                    + "; removed " + string.Join(", ", names.ToArray()));
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning(F5SnapshotSaveMod.LogPrefix + "keeping F5 saves and the game's saves apart in the history failed; the game's own trimming applies: " + e);
            }
        }

        // Puts the entries to keep first and returns their number; the game deletes the rest and saves the history index.
        // history is newest first. latestIsAuto: the slot file (the save being made, or the slot file in place) is one of the
        // game's saves and takes one of their places; an F5 save there takes a place of its own kind, and a slot file whose
        // kind is not known takes none. newQuick: an F5 save is being made. quickLimit 0 means no limit. With more F5
        // saves than the limit (the limit was lowered), a new F5 save deletes only the oldest one. isRelisted: an entry whose
        // kind is not known (IsRelisted); it is kept and takes no place.
        internal static int OrderForRetention(List<SaveHistoryEntry> history, bool latestIsAuto, bool newQuick, int autoLimit,
            int quickLimit, Func<SaveHistoryEntry, bool> isQuick, Func<SaveHistoryEntry, bool> isRelisted)
        {
            var quick = new List<bool>(history.Count);
            var relisted = new List<bool>(history.Count);
            int quickCount = 0;
            foreach (SaveHistoryEntry entry in history)
            {
                bool q = isQuick(entry);
                quick.Add(q);
                relisted.Add(!q && isRelisted(entry));
                if (q)
                    quickCount++;
            }
            int quickRoom = quickLimit > 0 && newQuick ? Math.Max(quickLimit - 1, quickCount - 1) : int.MaxValue;
            int autoRoom = Math.Max(1, autoLimit) - (latestIsAuto ? 1 : 0);
            var keep = new List<SaveHistoryEntry>();
            var drop = new List<SaveHistoryEntry>();
            for (int i = 0; i < history.Count; i++)
            {
                if (relisted[i])
                    keep.Add(history[i]);
                else if (quick[i] ? quickRoom > 0 : autoRoom > 0)
                {
                    keep.Add(history[i]);
                    if (quick[i])
                        quickRoom--;
                    else
                        autoRoom--;
                }
                else
                    drop.Add(history[i]);
            }
            history.Clear();
            history.AddRange(keep);
            history.AddRange(drop);
            return keep.Count;
        }

        // After an F5 save: the record of the slot holds the mark (QuickMarkPatch); its time is the key of the details shown on
        // the Load Game screen.
        internal static void RecordLatest(int slotId, SaveDetails.Record details)
        {
            try
            {
                if (QuickMark.EnsureMarked(slotId, null))
                    UnityEngine.Debug.LogWarning(F5SnapshotSaveMod.LogPrefix + "the F5 save of slot " + slotId + " had no mark; it was added afterwards");
                SavePreviewData preview = SaveFiles.IndexLoad<SavePreviewData>("preview_" + slotId, null);
                if (preview == null || preview.lastSaveDateTime.Year <= 1)
                    return;
                SaveDetails.Add(preview.lastSaveDateTime, details);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning(F5SnapshotSaveMod.LogPrefix + "recording the details of the F5 save failed; the Load Game screen shows it without them: " + e);
            }
        }

        // The last F5 save of a slot. file is null when that is the slot file, otherwise the name of its history file;
        // false when the slot has no F5 save. SaveFiles.LoadHistory lists the entries whose files exist, newest first.
        internal static bool FindLastQuick(int slotId, out string file)
        {
            file = null;
            HashSet<long> legacy = LoadLegacy();
            SavePreviewData preview = SaveUIManager.GetPreview(slotId);
            if (preview != null && IsQuick(preview.lastSaveDateTime, legacy))
                return true;
            List<SaveHistoryEntry> history = SaveFiles.LoadHistory(slotId);
            if (history == null)
                return false;
            foreach (SaveHistoryEntry entry in history)
            {
                if (entry != null && IsQuick(entry.saveDateTime, legacy))
                {
                    file = entry.file;
                    return true;
                }
            }
            return false;
        }

        internal static bool IsQuick(DateTime saved)
        {
            return IsQuick(saved, LoadLegacy());
        }

        internal static bool IsQuick(DateTime saved, HashSet<long> legacy)
        {
            return QuickMark.Has(saved) || (legacy != null && legacy.Contains(Seconds(saved)));
        }

        // SaveFiles.ScanAndRegisterOrphans (the repair of the save index, and the Discover button of the Load Game screen)
        // lists the history files of a slot that the save index does not hold (SaveFiles.RestoreHistoryFromDisk) with the
        // time from the file name, in whole seconds, so an F5 save listed there has lost its mark; the record it makes from a
        // slot file gets whole seconds as well (RebuiltPreviewPatch). Telling the kind of such a save would need reading the
        // save file (the files capability). Such an entry, neither marked nor in the list of earlier versions, is kept: it
        // takes no place of either kind, and no trimming deletes it. Loading it puts the mark back if it is an F5 save
        // (QuickMark.EnsureMarked). The saves of the game never have whole seconds (QuickMark.Avoid).
        internal static bool IsRelisted(DateTime saved, HashSet<long> legacy)
        {
            return saved.Ticks % TimeSpan.TicksPerSecond == 0 && !IsQuick(saved, legacy);
        }

        // LoadGameUI.Bind fills a slot plate with the newest save of the slot (its preview); LoadGameUI.BuildExpanded
        // fills one row per history entry, in the order of the list. Both call LoadGameUI.DayAndMoney once per save,
        // in that order.
        internal static void BeginPlate(int index, List<int> slots)
        {
            EndLabels();
            if (slots == null || index < 0 || index >= slots.Count || !Handles())
                return;
            SavePreviewData preview = SaveUIManager.GetPreview(slots[index]);
            if (preview != null)
                pendingLabels.Enqueue(preview.lastSaveDateTime);
        }

        internal static void BeginRows(List<SaveHistoryEntry> history)
        {
            EndLabels();
            if (history == null || !Handles())
                return;
            foreach (SaveHistoryEntry entry in history)
                pendingLabels.Enqueue(entry != null ? entry.saveDateTime : DateTime.MinValue);
        }

        internal static void EndLabels()
        {
            pendingLabels.Clear();
            labelLegacy = null;
            labelDetails = null;
        }

        internal static string Label(string dayAndMoney)
        {
            if (pendingLabels.Count == 0)
                return dayAndMoney;
            DateTime saved = pendingLabels.Dequeue();
            try
            {
                if (labelLegacy == null)
                    labelLegacy = LoadLegacy();
                bool quick = IsQuick(saved, labelLegacy);
                string title = dayAndMoney + Text.SaveKind(quick);
                if (!quick)
                    return title;
                if (labelDetails == null)
                    labelDetails = SaveDetails.Load();
                SaveDetails.Record details;
                return labelDetails.TryGetValue(saved.Ticks, out details) ? SaveDetailsUi.Describe(title, details) : title;
            }
            catch (Exception)
            {
                return dayAndMoney;
            }
        }

        private static long Seconds(DateTime time)
        {
            return time.Ticks / TimeSpan.TicksPerSecond;
        }

        internal static HashSet<long> LoadLegacy()
        {
            var marks = new HashSet<long>();
            string text = SaveFiles.IndexLoad<string>(LegacyKey, "") ?? "";
            foreach (string part in text.Split(','))
            {
                long value;
                if (long.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                    marks.Add(value);
            }
            return marks;
        }
    }
}
