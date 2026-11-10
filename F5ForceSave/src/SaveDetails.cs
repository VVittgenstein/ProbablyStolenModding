using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace F5ForceSave
{
    // A projection read by the game's own ES3 reader. Unknown fields are skipped:
    // inspecting a menu row never constructs a PlayerStore or its inventory graph.
    // This projection is never written to a save.
    public sealed class SaveRecordFields
    {
        [ES3Serializable] public Dictionary<string, string> modData = null;
        [ES3Serializable] public int storeState = -1;
        [ES3Serializable] public int lastAfterhourOutingDay = int.MinValue;
    }

    internal static class SaveDetails
    {
        internal enum Kind { Auto, Quick }
        internal sealed class Record
        {
            internal Kind Type;
            internal bool Readable;
            internal int State = -1;
            internal int Waiting = -1;
            internal int WentOut = -1;
        }
        private sealed class Cached
        {
            internal long Length, Stamp;
            internal int Day;
            internal Record Value;
            internal LinkedListNode<string> Node;
        }
        private const int CacheCapacity = 2048;
        private static readonly Dictionary<string, Cached> cache = new Dictionary<string, Cached>(StringComparer.OrdinalIgnoreCase);
        private static readonly LinkedList<string> recent = new LinkedList<string>();

        internal static Record Read(string file, int day)
        {
            try
            {
                // Menu/history callers supply file names, never arbitrary paths.
                if (string.IsNullOrEmpty(file) || Path.GetFileName(file) != file)
                    return new Record();
                string path = SaveFiles.AbsolutePath(file);
                var stat = new FileInfo(path);
                if (!stat.Exists) return new Record();
                Cached found;
                if (cache.TryGetValue(path, out found) && found.Length == stat.Length && found.Stamp == stat.LastWriteTimeUtc.Ticks && found.Day == day)
                {
                    recent.Remove(found.Node);
                    recent.AddLast(found.Node);
                    return found.Value;
                }
                Record record = null;
                try
                {
                    using (ES3Reader reader = Open(path))
                    {
                        SaveRecordFields fields;
                        if (SaveHeader.TryRead(reader, out fields)) record = FromFields(fields, day);
                    }
                }
                catch (FormatException) { } // Old/unusual layouts use the original projection below.
                if (record == null)
                    using (ES3Reader reader = Open(path)) record = ReadBody(reader, day);
                if (!record.Readable) return record;
                if (found == null)
                {
                    if (cache.Count >= CacheCapacity)
                    {
                        cache.Remove(recent.First.Value);
                        recent.RemoveFirst();
                    }
                    found = new Cached { Node = recent.AddLast(path) };
                    cache[path] = found;
                }
                else { recent.Remove(found.Node); recent.AddLast(found.Node); }
                found.Length = stat.Length;
                found.Stamp = stat.LastWriteTimeUtc.Ticks;
                found.Day = day;
                found.Value = record;
                return record;
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning(F5ForceSaveMod.LogPrefix + "reading saved details for " + file + " failed: " + e.Message);
                return new Record();
            }
        }

        private static ES3Reader Open(string path)
        {
            return ES3Reader.Create(new ES3Settings(path, ES3.Location.File) { typeChecking = false });
        }

        internal static Record ReadBody(ES3Reader reader, int day)
        {
            if (reader == null) return new Record();
            SaveRecordFields fields = reader.Read<SaveRecordFields>("playerStore", null);
            return fields == null ? new Record() : FromFields(fields, day);
        }

        internal static Record FromFields(SaveRecordFields fields, int day)
        {
            var result = new Record { Readable = true, State = fields.storeState };
            string savedDay, state, queue;
            if (fields.modData != null && fields.modData.TryGetValue("F5ForceSave:save.day", out savedDay)
                && fields.modData.TryGetValue("F5ForceSave:save.state", out state)
                && Int(savedDay) == day && Int(state) == fields.storeState && fields.storeState >= 0 && fields.storeState <= 2)
            {
                // Existing 1.6.2 saves already contain this copy-safe evidence.
                result.Type = Kind.Quick;
                if (fields.modData.TryGetValue("F5ForceSave:save.customerQueue", out queue) && queue != null)
                {
                    string[] entries = queue.Split('|');
                    if (entries[0] == "v1")
                    {
                        result.Waiting = entries.Length - 1;
                        for (int i = 1; i < entries.Length; i++)
                            if (entries[i].Split(';').Length != 6) { result.Waiting = -1; break; }
                    }
                }
            }
            if (fields.storeState == 0)
            {
                result.WentOut = fields.lastAfterhourOutingDay == int.MinValue ? -1 : fields.lastAfterhourOutingDay == day ? 1 : 0;
            }
            return result;
        }

        internal static Kind Resolve(Record record, bool legacyMark)
        {
            // A successfully read native save stays Auto even when a legacy
            // timestamp belongs to a different file saved in the same second.
            if (record.Readable) return record.Type;
            return legacyMark ? Kind.Quick : record.Type;
        }

        internal static bool IsQuick(string file, int day, bool legacyMark)
        {
            return Resolve(Read(file, day), legacyMark) == Kind.Quick;
        }

        private static int Int(string value)
        {
            int result;
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result) ? result : -1;
        }

        internal static string Format(Record record, string language)
        {
            if (record.Readable && record.Type != Kind.Quick) return "";
            bool zh = language != null && language.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
            bool ru = language != null && language.StartsWith("ru", StringComparison.OrdinalIgnoreCase);
            var parts = new List<string>();
            switch (record.State)
            {
                case 0: parts.Add(zh ? "打烊后" : ru ? "После закрытия" : "After closing"); break;
                case 1: parts.Add(zh ? "营业中" : ru ? "Магазин открыт" : "Shop open"); break;
                case 2: parts.Add(zh ? "开店前" : ru ? "До открытия" : "Before opening"); break;
                default: parts.Add(zh ? "店铺状态未知" : ru ? "Состояние магазина неизвестно" : "Shop state unknown"); break;
            }
            if (record.State == 1)
                parts.Add(record.Waiting >= 0 ? (zh ? "排队 " + record.Waiting + " 人" : ru ? "В очереди: " + record.Waiting : "Waiting: " + record.Waiting)
                    : (zh ? "排队人数未知" : ru ? "Очередь: неизвестно" : "Queue unknown"));
            if (record.State == 0)
            {
                parts.Add(record.WentOut == 1 ? (zh ? "已外出" : ru ? "Уже выходил сегодня" : "Went out tonight")
                    : record.WentOut == 0 ? (zh ? "未外出" : ru ? "Ещё не выходил" : "Not out tonight")
                    : (zh ? "外出记录未知" : ru ? "Выход: неизвестно" : "Outing unknown"));
            }
            return string.Join(" · ", parts.ToArray());
        }
    }
}
