using System;
using System.Collections.Generic;
using System.Globalization;

namespace F5SnapshotSave
{
    // The second line under an F5 save on the Load Game screen (from lin-and-tree's pull request #1): the state of the shop
    // when it was saved (before opening, open, after closing), the number of waiting customers while open, and whether the
    // player had gone out that night after closing. 0.7.0, as F5ForceSave 1.8.0, writes these values at the F5 save into one record of the save
    // index (IndexKey), by the save time with its mark (QuickMark), so a copy of a slot shows them as well and the save
    // files are not opened (the pull request read them from the files, which the game's mod scan counts as the files
    // capability). Only the newest MaxRecords are kept; deleting a save does not remove its values. F5 saves made before
    // 0.7.0 have no values and no second line.
    internal static class SaveDetails
    {
        internal const string IndexKey = "f5_quick_details";
        private const int MaxRecords = 400;
        private const string Version = "v1";

        internal sealed class Record
        {
            internal int State = -1;     // PlayerStore.StoreState: CLOSED 0, OPEN 1, MORNING 2
            internal int Waiting = -1;   // waiting customers while open
            internal int WentOut = -1;   // after closing: 1 gone out that night, 0 not
        }

        internal static Record Make(PlayerStore store, StoreStation station, int waiting)
        {
            var record = new Record { State = (int)store.storeState };
            if (store.storeState == PlayerStore.StoreState.OPEN)
                record.Waiting = Math.Max(0, waiting);
            if (store.storeState == PlayerStore.StoreState.CLOSED)
                record.WentOut = store.lastAfterhourOutingDay == station.dayCounter ? 1 : 0;
            return record;
        }

        internal static void Add(DateTime saved, Record record)
        {
            if (record == null)
                return;
            Dictionary<long, Record> all = Load();
            all[saved.Ticks] = record;
            var times = new List<long>(all.Keys);
            times.Sort();
            if (times.Count > MaxRecords)
                times.RemoveRange(0, times.Count - MaxRecords);
            var parts = new List<string>(times.Count + 1) { Version };
            foreach (long time in times)
            {
                Record r = all[time];
                parts.Add(string.Join(",", new[]
                {
                    time.ToString(CultureInfo.InvariantCulture),
                    r.State.ToString(CultureInfo.InvariantCulture),
                    r.Waiting.ToString(CultureInfo.InvariantCulture),
                    r.WentOut.ToString(CultureInfo.InvariantCulture),
                }));
            }
            SaveFiles.IndexSave<string>(IndexKey, string.Join(";", parts.ToArray()));
        }

        // By save time in ticks (with the mark).
        internal static Dictionary<long, Record> Load()
        {
            var all = new Dictionary<long, Record>();
            string text = SaveFiles.IndexLoad<string>(IndexKey, "") ?? "";
            string[] parts = text.Split(';');
            if (parts.Length == 0 || parts[0] != Version)
                return all;
            for (int i = 1; i < parts.Length; i++)
            {
                string[] fields = parts[i].Split(',');
                long time;
                if (fields.Length < 4 || !long.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out time))
                    continue;
                all[time] = new Record { State = Int(fields[1]), Waiting = Int(fields[2]), WentOut = Int(fields[3]) };
            }
            return all;
        }

        private static int Int(string value)
        {
            int result;
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result) ? result : -1;
        }

        internal static string Format(Record record)
        {
            var parts = new List<string> { Text.DetailState(record.State) };
            if (record.State == (int)PlayerStore.StoreState.OPEN && record.Waiting >= 0)
                parts.Add(Text.DetailWaiting(record.Waiting));
            if (record.State == (int)PlayerStore.StoreState.CLOSED && record.WentOut >= 0)
                parts.Add(Text.DetailWentOut(record.WentOut == 1));
            return string.Join(" · ", parts.ToArray());
        }
    }
}
