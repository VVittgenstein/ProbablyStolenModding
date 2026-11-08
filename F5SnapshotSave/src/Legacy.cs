using System;
using System.Collections.Generic;
using System.Globalization;

namespace F5SnapshotSave
{
    // The data F5 Quick Save (F5ForceSave 1.2.x) keeps in PlayerStore.modData under its own mod ID: day and store
    // state of the save, front counter items, the waiting customers by identifier, and flags of the day. This mod
    // writes the same keys next to its snapshot, so that F5ForceSave alone can still load these saves (customers
    // re-created by identifier, the customer at the counter first in line), and it reads them back itself: for saves
    // made by F5ForceSave, and as the fallback when a snapshot cannot be rebuilt.
    internal static class Legacy
    {
        internal const string ModId = "F5ForceSave";
        private const string DayKey = "save.day";
        private const string StateKey = "save.state";
        private const string FrontCounterKey = "save.frontCounter";
        private const string QueueKey = "save.customerQueue";
        private const string ReportFiledKey = "today.correctReportFile";
        private const string WantedVisitedKey = "today.wantedVisited";
        private const string WantedNameKey = "today.wantedName";
        private const string WantedIdKey = "today.wantedId";
        private const string CallNextKey = "today.callNextWhenPossible";
        private static readonly string[] Keys = { DayKey, StateKey, FrontCounterKey, QueueKey, ReportFiledKey, WantedVisitedKey, WantedNameKey, WantedIdKey, CallNextKey };

        internal sealed class Loaded
        {
            internal int Day;
            internal PlayerStore.StoreState State;
            internal bool FrontCounterRestored;
            internal List<CustomerQueue.Entry> Queue;
            internal bool QueueStored;
        }

        internal static void Write(PlayerStore store, EmporiumEntry entry, StoreStation station, List<StoreClient> queue)
        {
            Set(DayKey, station.dayCounter.ToString(CultureInfo.InvariantCulture));
            Set(StateKey, ((int)store.storeState).ToString(CultureInfo.InvariantCulture));
            Set(FrontCounterKey, QuickSave.HasItems(entry.frontInvinvElement)
                ? store.EncodeSaveItem("save_bag", entry.frontInvinvElement, entry.frontInvWindow)
                : "");
            Set(QueueKey, CustomerQueue.Encode(queue));
            Set(ReportFiledKey, store.correctReportFileToday ? "1" : "0");
            Set(WantedVisitedKey, store.wantedPersonVisitedToday ? "1" : "0");
            Set(WantedNameKey, store.wantedPersonVisitedName ?? "");
            Set(WantedIdKey, store.wantedPersonVisitedId ?? "");
            Set(CallNextKey, store.callNextWhenPossible ? "1" : "0");
        }

        internal static void Clear()
        {
            foreach (string key in Keys)
                ModHelper.RemoveModData(ModId, key);
        }

        internal static bool Present()
        {
            return ModHelper.HasModData(ModId, DayKey);
        }

        // Applies the flags of the day and puts the front counter items back. Returns null when the data does not
        // belong to the loaded day and state.
        internal static Loaded Apply(PlayerStore store, EmporiumEntry entry, StoreStation station)
        {
            int day = ParseInt(Get(DayKey), -1);
            int state = ParseInt(Get(StateKey), -1);
            if (day != station.dayCounter || state != (int)store.storeState)
            {
                UnityEngine.Debug.LogWarning(F5SnapshotSaveMod.LogPrefix + "F5 data for day " + day + " state " + state
                    + " does not match the loaded day " + station.dayCounter + " state " + (int)store.storeState + "; not applied");
                return null;
            }

            store.correctReportFileToday = Get(ReportFiledKey) == "1";
            store.wantedPersonVisitedToday = Get(WantedVisitedKey) == "1";
            store.wantedPersonVisitedName = Get(WantedNameKey);
            store.wantedPersonVisitedId = Get(WantedIdKey);
            store.callNextWhenPossible = Get(CallNextKey) == "1";

            var loaded = new Loaded { Day = day, State = store.storeState };
            string frontCounter = Get(FrontCounterKey);
            if (!string.IsNullOrEmpty(frontCounter))
            {
                store.DecodeSaveItem(frontCounter, entry.frontInvinvElement);
                loaded.FrontCounterRestored = true;
            }
            loaded.QueueStored = ModHelper.HasModData(ModId, QueueKey);
            loaded.Queue = loaded.QueueStored ? CustomerQueue.Decode(Get(QueueKey)) : null;
            return loaded;
        }

        private static void Set(string key, string value)
        {
            ModHelper.SetModData(ModId, key, value);
        }

        private static string Get(string key)
        {
            return ModHelper.GetModData(ModId, key, "");
        }

        internal static int ParseInt(string text, int fallback)
        {
            int value;
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : fallback;
        }
    }

    // Customers by identifier (the F5ForceSave 1.2.x format "v1|<customer>|<customer>..."), re-created after loading
    // with the game's own factories; values a factory rolls, such as budgets, are rolled again.
    internal static class CustomerQueue
    {
        // StoreClientManager.AddAugClient appends this to the identifier of a display-case buyer it turns into a
        // disguised aug.
        private const string DisguisedAugSuffix = "_disguised_aug";

        // Customers that StoreClientManager queues but StoreClientListDict cannot create.
        private const string AugMedId = "aug_med";
        private const string AugMaterialId = "aug_material";
        private const string FlexiBuyerId = "lower_level_flexi_buyer";

        internal sealed class Entry
        {
            internal string Identifier;
            internal bool IsAug;
            internal int AugTier;
            internal string AttractedItemId;
            internal string AttractedType;
            internal string SourceModId;
        }

        private static string FactoryKey(string identifier)
        {
            return identifier.EndsWith(DisguisedAugSuffix, StringComparison.Ordinal)
                ? identifier.Substring(0, identifier.Length - DisguisedAugSuffix.Length)
                : identifier;
        }

        private static bool InDictionary(string key)
        {
            Dictionary<string, Func<StoreClient>> factories = StoreClientListDict.storeClientDict;
            return factories != null && factories.ContainsKey(key);
        }

        private static StoreClient Create(Entry entry)
        {
            string key = FactoryKey(entry.Identifier);
            StoreClient client;
            if (InDictionary(key))
                client = StoreClientListDict.CreateStoreClient(key);
            else if (key == AugMedId)
                client = StoreClientList.CreateAugMed(entry.AugTier);
            else if (key == AugMaterialId)
                client = StoreClientList.CreateAugMaterial(entry.AugTier);
            else if (key == FlexiBuyerId)
                client = StoreClientList.CreateFlexiBuyer();
            else
                return null;
            if (client == null)
                return null;

            // Set by StoreClientManager after the factory returns.
            client.identifier = entry.Identifier;
            if (!string.IsNullOrEmpty(entry.AttractedItemId))
                client.attractedItemId = entry.AttractedItemId;
            if (!string.IsNullOrEmpty(entry.AttractedType))
                client.attractedType = entry.AttractedType;
            if (entry.IsAug && !client.isAug)
                AugHelper.MakeDisguisedAug(client, entry.AugTier);
            if (string.IsNullOrEmpty(client.sourceModId) && !string.IsNullOrEmpty(entry.SourceModId))
                client.sourceModId = entry.SourceModId;
            return client;
        }

        internal static List<StoreClient> CreateAll(List<Entry> entries, out int failed)
        {
            var created = new List<StoreClient>();
            failed = 0;
            foreach (Entry entry in entries)
            {
                StoreClient client = null;
                try
                {
                    client = Create(entry);
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogWarning(F5SnapshotSaveMod.LogPrefix + "creating the customer '" + entry.Identifier + "' failed: " + e);
                }
                if (client == null)
                    failed++;
                else
                    created.Add(client);
            }
            return created;
        }

        internal static string Encode(List<StoreClient> clients)
        {
            var parts = new List<string> { "v1" };
            foreach (StoreClient client in clients)
            {
                if (client == null)
                    continue;
                parts.Add(string.Join(";", new[]
                {
                    Escape(client.identifier),
                    client.isAug ? "1" : "0",
                    client.augTier.ToString(CultureInfo.InvariantCulture),
                    Escape(client.attractedItemId),
                    Escape(client.attractedType),
                    Escape(client.sourceModId),
                }));
            }
            return string.Join("|", parts.ToArray());
        }

        internal static List<Entry> Decode(string text)
        {
            var entries = new List<Entry>();
            string[] parts = (text ?? "").Split('|');
            if (parts[0] != "v1")
            {
                UnityEngine.Debug.LogWarning(F5SnapshotSaveMod.LogPrefix + "unknown waiting-customer data '" + parts[0] + "'; not restored");
                return entries;
            }
            for (int i = 1; i < parts.Length; i++)
            {
                string[] fields = parts[i].Split(';');
                if (fields.Length < 6 || fields[0].Length == 0)
                    continue;
                int tier;
                int.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out tier);
                entries.Add(new Entry
                {
                    Identifier = Unescape(fields[0]),
                    IsAug = fields[1] == "1",
                    AugTier = tier,
                    AttractedItemId = Unescape(fields[3]),
                    AttractedType = Unescape(fields[4]),
                    SourceModId = Unescape(fields[5]),
                });
            }
            return entries;
        }

        private static string Escape(string value)
        {
            return (value ?? "").Replace("%", "%25").Replace(";", "%3B").Replace("|", "%7C");
        }

        private static string Unescape(string value)
        {
            return value.Replace("%7C", "|").Replace("%3B", ";").Replace("%25", "%");
        }
    }
}
