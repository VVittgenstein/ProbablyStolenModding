using System;
using System.Collections.Generic;
using System.Globalization;

namespace F5ForceSave
{
    // F5 saves and the game's own saves (at the start of each day and for a new game) share the slot's history
    // (SaveFiles.LoadHistory). Before every save the game moves the slot file into the history (RotateIntoHistory)
    // and then keeps "Saved days per run" (EmporiumOption.SaveHistoryDays) saves in all, the slot file included,
    // deleting the oldest history entries (TrimHistory). Here the two kinds are counted apart: up to SaveHistoryDays
    // of each kind are kept, the slot file included, and the oldest of a kind is deleted.
    // Which saves are F5 saves is recorded in the save index under IndexKey: the save time of each F5 save in whole
    // seconds, as the slot preview (SavePreviewData.lastSaveDateTime) and the history entries
    // (SaveHistoryEntry.saveDateTime) hold it. F5 Quick Save Plus (F5SnapshotSave) keeps the same record. On the Load
    // Game screen the day and money of every save end with " | F5" or " | Auto".
    internal static class QuickHistory
    {
        internal const string IndexKey = "f5_quick_saves";
        private const int MaxRecorded = 400;

        private static bool trimming;
        private static bool quickInProgress;
        private static readonly Queue<DateTime> pendingLabels = new Queue<DateTime>();
        private static HashSet<long> labelMarks;

        // F5 Quick Save Plus handles F5 and this record itself while it runs. Its object is named "F5SnapshotSave"
        // only then; it renames the object when it cannot run alongside this mod.
        internal static bool HandledByPlus()
        {
            return UnityEngine.GameObject.Find("F5SnapshotSave") != null;
        }

        internal static void BeginRotation(bool quick)
        {
            trimming = !HandledByPlus();
            quickInProgress = quick;
        }

        internal static void EndRotation()
        {
            trimming = false;
            quickInProgress = false;
        }

        // SaveFiles.RestoreHistoryFromDisk (the game 1.0.0.12): the slot file is already in place and takes one place of
        // its kind, as the save being made does in RotateIntoHistory.
        internal static void BeginRestore(int slotId)
        {
            bool quick = false;
            try
            {
                SavePreviewData preview = SaveFiles.IndexLoad<SavePreviewData>("preview_" + slotId, null);
                quick = preview != null && IsQuick(preview.lastSaveDateTime);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning(F5ForceSaveMod.LogPrefix + "reading the slot file of slot " + slotId + " before listing its history failed; it counts as the game's save: " + e);
            }
            BeginRotation(quick);
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
                HashSet<long> marks = Load();
                // The save being made becomes the slot file and takes one place of its kind.
                int quickRoom = quickInProgress ? perKind - 1 : perKind;
                int autoRoom = quickInProgress ? perKind : perKind - 1;
                var keep = new List<SaveHistoryEntry>();
                var drop = new List<SaveHistoryEntry>();
                foreach (SaveHistoryEntry entry in history)
                {
                    bool quick = entry != null && marks.Contains(Key(entry.saveDateTime));
                    if (quick ? quickRoom > 0 : autoRoom > 0)
                    {
                        keep.Add(entry);
                        if (quick)
                            quickRoom--;
                        else
                            autoRoom--;
                    }
                    else
                        drop.Add(entry);
                }
                history.Clear();
                history.AddRange(keep);
                history.AddRange(drop);
                keepCount = keep.Count;
                if (drop.Count == 0)
                    return;
                var names = new List<string>();
                foreach (SaveHistoryEntry entry in drop)
                {
                    if (entry == null)
                        continue;
                    names.Add(entry.file);
                    marks.Remove(Key(entry.saveDateTime));
                }
                Save(marks);
                UnityEngine.Debug.Log(F5ForceSaveMod.LogPrefix + "save history: up to " + perKind + " of each kind kept; removed " + string.Join(", ", names.ToArray()));
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning(F5ForceSaveMod.LogPrefix + "keeping F5 saves and the game's saves apart in the history failed; the game's own trimming applies: " + e);
            }
        }

        // After an F5 save: the slot preview now holds the time of this save.
        internal static void RecordLatest(int slotId)
        {
            try
            {
                SavePreviewData preview = SaveFiles.IndexLoad<SavePreviewData>("preview_" + slotId, null);
                if (preview == null || preview.lastSaveDateTime.Year <= 1)
                    return;
                HashSet<long> marks = Load();
                marks.Add(Key(preview.lastSaveDateTime));
                Save(marks);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning(F5ForceSaveMod.LogPrefix + "recording the F5 save failed; the Load Game screen shows it as Auto: " + e);
            }
        }

        // The last F5 save of a slot. file is null when that is the slot file, otherwise the name of its history file;
        // false when the slot has no recorded F5 save. SaveFiles.LoadHistory lists the entries whose files exist, newest
        // first.
        internal static bool FindLastQuick(int slotId, out string file)
        {
            file = null;
            HashSet<long> marks = Load();
            SavePreviewData preview = SaveUIManager.GetPreview(slotId);
            if (preview != null && marks.Contains(Key(preview.lastSaveDateTime)))
                return true;
            List<SaveHistoryEntry> history = SaveFiles.LoadHistory(slotId);
            if (history == null)
                return false;
            foreach (SaveHistoryEntry entry in history)
            {
                if (entry != null && marks.Contains(Key(entry.saveDateTime)))
                {
                    file = entry.file;
                    return true;
                }
            }
            return false;
        }

        internal static bool IsQuick(DateTime saved)
        {
            return Load().Contains(Key(saved));
        }

        // LoadGameUI.Bind fills a slot plate with the newest save of the slot (its preview); LoadGameUI.BuildExpanded
        // fills one row per history entry, in the order of the list. Both call LoadGameUI.DayAndMoney once per save,
        // in that order.
        internal static void BeginPlate(int index, List<int> slots)
        {
            EndLabels();
            if (slots == null || index < 0 || index >= slots.Count || HandledByPlus())
                return;
            SavePreviewData preview = SaveUIManager.GetPreview(slots[index]);
            if (preview != null)
                pendingLabels.Enqueue(preview.lastSaveDateTime);
        }

        internal static void BeginRows(List<SaveHistoryEntry> history)
        {
            EndLabels();
            if (history == null || HandledByPlus())
                return;
            foreach (SaveHistoryEntry entry in history)
                pendingLabels.Enqueue(entry != null ? entry.saveDateTime : DateTime.MinValue);
        }

        internal static void EndLabels()
        {
            pendingLabels.Clear();
            labelMarks = null;
        }

        internal static string Label(string dayAndMoney)
        {
            if (pendingLabels.Count == 0)
                return dayAndMoney;
            DateTime saved = pendingLabels.Dequeue();
            try
            {
                if (labelMarks == null)
                    labelMarks = Load();
                return dayAndMoney + Text.SaveKind(labelMarks.Contains(Key(saved)));
            }
            catch (Exception)
            {
                return dayAndMoney;
            }
        }

        private static long Key(DateTime time)
        {
            return time.Ticks / TimeSpan.TicksPerSecond;
        }

        private static HashSet<long> Load()
        {
            var marks = new HashSet<long>();
            string text = SaveFiles.IndexLoad<string>(IndexKey, "") ?? "";
            foreach (string part in text.Split(','))
            {
                long value;
                if (long.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                    marks.Add(value);
            }
            return marks;
        }

        // The newest MaxRecorded times are kept; entries of deleted slots are not looked up.
        private static void Save(HashSet<long> marks)
        {
            var times = new List<long>(marks);
            times.Sort();
            if (times.Count > MaxRecorded)
                times.RemoveRange(0, times.Count - MaxRecorded);
            var parts = new List<string>(times.Count);
            foreach (long time in times)
                parts.Add(time.ToString(CultureInfo.InvariantCulture));
            SaveFiles.IndexSave<string>(IndexKey, string.Join(",", parts.ToArray()));
        }
    }
}
