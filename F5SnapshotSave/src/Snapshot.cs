using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

namespace F5SnapshotSave
{
    // The part of an F5 save that the game's own save leaves out and that only exists while the store is running:
    // the customer at the counter (StoreClient with its dialogue, closures and delegates), the waiting customers and
    // the chemical feedback customers as they are, the conversation line on screen, the haggling state of the
    // customer (BargainUIManager), and the item features of the counter items (features are not part of the item
    // encoding). Written by GraphWriter into PlayerStore.modData for the duration of the save.
    internal static class Snapshot
    {
        internal const string ModId = F5SnapshotSaveMod.ModId;
        private const string FormatKey = "snap.format";
        private const string DayKey = "snap.day";
        private const string StateKey = "snap.state";
        private const string TimeKey = "snap.time";
        private const string GraphKey = "snap.graph";
        private const string DialogKey = "snap.dialog";
        private const string NegotiationKey = "snap.negotiation";
        private const string BargainOpenKey = "snap.bargainOpen";
        private const string BargainFieldsKey = "snap.bargainFields";
        private const string SpriteKey = "snap.sprite";
        private static readonly string[] Keys = { FormatKey, DayKey, StateKey, TimeKey, GraphKey, DialogKey, NegotiationKey, BargainOpenKey, BargainFieldsKey, SpriteKey };

        // Haggling state that BargainUIManager keeps for the customer it tracks; Refresh resets it when the tracked
        // customer is not the one at the counter.
        private static readonly string[] BargainFieldNames =
        {
            "trackedClient", "baseTradeRepMultiplier", "discountOffered", "discountPercent", "markupOffered", "markupPercent",
            "markupApplied", "blackmailOffered", "blackmailApplied", "buyingMarkupOffered", "buyingMarkupPercent",
            "buyingDiscountOffered", "buyingDiscountPercent", "buyingDiscountApplied", "threatenOffered", "threatenApplied",
        };

        // The roots of the waiting customers and of the chemical feedback customers. Since 0.8.0 (format 3) the queue root
        // holds only the waiting customers that are in the snapshot, and SlotsRoot holds the whole line: per waiting
        // customer in StoreClientManager.clientStack order an int, the index into the queue root, or a string, the
        // CustomerQueue.Encode text of that one customer, who was left out of the snapshot and is created again by
        // identifier after loading. Formats 1 and 2 have no SlotsRoot; their queue root is the whole line.
        private const string QueueRoot = "queue";
        private const string FeedbackRoot = "feedback";
        private const string SlotsRoot = "queueSlots";

        internal const string DialogNone = "none";
        internal const string DialogWaiting = "wait";
        internal const string DialogChoice = "choice";

        internal sealed class Captured
        {
            internal string Graph;
            internal bool HasCounter;
            internal string DialogState = DialogNone;
            internal bool NegotiationOpen;
            internal bool BargainOpen;
            internal string BargainFields = "";
            internal string Sprite = "";
            internal int Objects;
            internal int Waiting;
            // 0.8.0: the waiting customers left out of the snapshot (created again by identifier after loading).
            internal int Excluded;
        }

        // 0.8.0: why no snapshot can be written. Counter: something of the counter group (the customer at the counter,
        // the conversation, the haggling state, the counter items' features) cannot be stored. Feedback: a customer of the
        // chemical feedback list cannot be stored; such customers are not in the F5ForceSave-format queue and cannot be
        // created again by identifier (yzz 2026-11-12, T-029 quote 10). Queue: the snapshot fails its read-back check
        // without a customer to leave out and without a customer at the counter.
        internal enum Refusal
        {
            None,
            Counter,
            Feedback,
            Queue,
        }

        internal sealed class Outcome
        {
            internal Captured Snapshot;     // null when the save is refused
            internal Refusal Refused;
            internal string Error;          // for the log
            // The IDs of the mods whose objects made F5 leave a waiting customer out or refuse the save (IncompatibleNotice).
            internal readonly List<string> Mods = new List<string>();
        }

        // 0.8.0: one place in the line of waiting customers: a customer rebuilt from the snapshot, or the identifier data of
        // a customer left out of it, created on the first shutter update (CreateLeftOut).
        internal sealed class WaitingSlot
        {
            internal StoreClient Client;
            internal CustomerQueue.Entry Entry;
        }

        internal sealed class Restored
        {
            // The waiting customers rebuilt from the snapshot.
            internal List<StoreClient> Queue = new List<StoreClient>();
            // 0.8.0: the whole line in its order, including the customers left out of the snapshot.
            internal List<WaitingSlot> Slots = new List<WaitingSlot>();
            // 0.8.0: extra data of the mods that follow the spec, given back on the first shutter update (GiveExtraBack).
            internal List<ExtraValue> Extras = new List<ExtraValue>();
            internal List<StoreClient> Feedback = new List<StoreClient>();
            internal StoreClientInstance Counter;
            internal Dialogue Dialog;
            internal GameItem StoreNegotiated;
            internal GameItem Negotiated;
            internal List<KeyValuePair<GameItem, List<ItemFeature>>> Features = new List<KeyValuePair<GameItem, List<ItemFeature>>>();
            internal List<KeyValuePair<FieldInfo, object>> Bargain = new List<KeyValuePair<FieldInfo, object>>();
            internal string DialogState = DialogNone;
            internal bool NegotiationOpen;
            internal bool BargainOpen;
            internal string Sprite = "";
        }

        // The values of a snapshot as they were stored in the save, kept after the keys have been removed from
        // PlayerStore.modData.
        internal sealed class Stored
        {
            internal string Format;
            internal string Graph;
            internal string Dialog;
            internal string Negotiation;
            internal string BargainOpen;
            internal string BargainFields;
            internal string Sprite;
        }

        internal static Stored Take()
        {
            return new Stored
            {
                Format = Get(FormatKey),
                Graph = Get(GraphKey),
                Dialog = Get(DialogKey),
                Negotiation = Get(NegotiationKey),
                BargainOpen = Get(BargainOpenKey),
                BargainFields = Get(BargainFieldsKey),
                Sprite = Get(SpriteKey),
            };
        }

        private static List<FieldInfo> BargainFields()
        {
            var list = new List<FieldInfo>();
            foreach (string name in BargainFieldNames)
            {
                FieldInfo f = typeof(BargainUIManager).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (f != null)
                    list.Add(f);
            }
            return list;
        }

        // Items on the front and back counter, including items inside them.
        private static List<GameItem> CounterItems(EmporiumEntry entry)
        {
            var result = new List<GameItem>();
            var seen = new HashSet<object>(ReferenceComparer.Instance);
            var stack = new Stack<GraphNodeStorage>();
            stack.Push(entry.frontInvinvElement);
            stack.Push(entry.backInvinvElement);
            while (stack.Count > 0)
            {
                GraphNodeStorage cur = stack.Pop();
                if (cur == null || !seen.Add(cur))
                    continue;
                var item = cur as GameItem;
                if (item != null)
                    result.Add(item);
                List<GraphNodeStorage> children = cur.children;
                if (children == null)
                    continue;
                foreach (GraphNodeStorage child in children)
                    stack.Push(child);
            }
            return result;
        }

        // The snapshot of the store as it is, or null with the reason in error (QuickSave.TrySave uses CaptureOutcome).
        internal static Captured Capture(PlayerStore store, EmporiumEntry entry, bool withCounter, string dialogState, out string error)
        {
            Outcome outcome = CaptureOutcome(store, entry, withCounter, dialogState);
            error = outcome.Error;
            return outcome.Snapshot;
        }

        // 0.8.0 (plan 3.3 to 3.6, 3.10): an object that cannot be stored no longer ends the whole save. Each failure is
        // assigned by the start of its path to the customer that first reached the object: a waiting customer is left
        // out of the snapshot and the snapshot is written again, until nothing fails; something of the counter group
        // refuses the save with the counter text as before, a chemical feedback customer with the feedback text. Then the
        // read-back check runs as before; when it fails, each feedback customer and each stored waiting customer is
        // written and read back alone, failing waiting customers are left out and everything starts again, a failing
        // feedback customer refuses the save, and with nobody to leave out the save is refused as before. The mods whose
        // types caused a failure are collected for the window that names them.
        internal static Outcome CaptureOutcome(PlayerStore store, EmporiumEntry entry, bool withCounter, string dialogState)
        {
            var outcome = new Outcome();
            SnapshotSchema.CheckGame();
            AdapterSet adapters = SpecAdapters.Collect();

            StoreClientManager manager = store.storeClientManager;
            List<StoreClient> waiting = WithoutNull(manager != null ? manager.clientStack : null);
            List<StoreClient> feedback = WithoutNull(manager != null ? manager.chemicalFeedbackStack : null);
            var excluded = new HashSet<object>(ReferenceComparer.Instance);
            while (true)
            {
                var stored = new List<StoreClient>();
                foreach (StoreClient c in waiting)
                {
                    if (!excluded.Contains(c))
                        stored.Add(c);
                }
                GraphWriter w;
                Captured cap = Write(store, entry, withCounter, dialogState, waiting, stored, feedback, excluded, adapters, out w);
                if (w.Failures.Count > 0)
                {
                    var failing = new SortedDictionary<int, WriteFailure>();
                    WriteFailure counterFailure = null, feedbackFailure = null;
                    int feedbackIndex = -1;
                    foreach (WriteFailure f in w.Failures)
                    {
                        AddMod(outcome.Mods, f.Mod);
                        int i;
                        if (RootIndex(f.Path, QueueRoot, out i) && i < stored.Count)
                        {
                            if (!failing.ContainsKey(i))
                                failing[i] = f;
                        }
                        else if (RootIndex(f.Path, FeedbackRoot, out i))
                        {
                            if (feedbackFailure == null)
                            {
                                feedbackFailure = f;
                                feedbackIndex = i;
                            }
                        }
                        else if (counterFailure == null)
                            counterFailure = f;
                    }
                    if (counterFailure != null)
                        return Refuse(outcome, withCounter ? Refusal.Counter : Refusal.Queue,
                            (withCounter ? "the customer at the counter: " : "") + string.Join("; ", w.Errors.GetRange(0, Math.Min(10, w.Errors.Count)).ToArray()));
                    if (feedbackFailure != null)
                        return Refuse(outcome, Refusal.Feedback, "chemical feedback customer " + feedbackIndex + " '"
                            + (feedbackIndex >= 0 && feedbackIndex < feedback.Count ? feedback[feedbackIndex].identifier : "?") + "': " + Describe(feedbackFailure));
                    foreach (KeyValuePair<int, WriteFailure> pair in failing)
                        Exclude(excluded, waiting, stored[pair.Key], Describe(pair.Value));
                    continue;
                }

                // Read the snapshot back at once (items stored with the item encoding are not decoded here), so that a
                // snapshot that cannot be rebuilt on this game version is never written.
                var check = new GraphReader(SnapshotContext.Create(adapters), true);
                if (check.Read(cap.Graph))
                {
                    cap.Excluded = excluded.Count;
                    outcome.Snapshot = cap;
                    LogMods(outcome.Mods);
                    return outcome;
                }
                string readBack = "read-back check: " + string.Join("; ", check.Errors.GetRange(0, Math.Min(10, check.Errors.Count)).ToArray());
                for (int i = 0; i < feedback.Count; i++)
                {
                    string problem;
                    if (!ReadsBackAlone(FeedbackRoot, feedback[i], adapters, outcome.Mods, out problem))
                        return Refuse(outcome, Refusal.Feedback, "chemical feedback customer " + i + " '" + feedback[i].identifier + "': " + problem);
                }
                bool any = false;
                foreach (StoreClient c in stored)
                {
                    string problem;
                    if (!ReadsBackAlone(QueueRoot, c, adapters, outcome.Mods, out problem))
                    {
                        Exclude(excluded, waiting, c, problem);
                        any = true;
                    }
                }
                if (!any)
                {
                    foreach (Assembly a in check.FailedAssemblies)
                        AddMod(outcome.Mods, ModOf(a));
                    return Refuse(outcome, withCounter ? Refusal.Counter : Refusal.Queue, readBack);
                }
            }
        }

        // One writing of the snapshot without the excluded waiting customers. The roots are written in this order: the
        // stored waiting customers, the feedback customers, the counter group, the line (SlotsRoot); an object shared by
        // several of them belongs to the first that reaches it. Up to 0.7.1 the portraits of CorpPresence visitors followed
        // as the root corpPortraits; 0.8.0 no longer supports CorpPresence (T-029).
        private static Captured Write(PlayerStore store, EmporiumEntry entry, bool withCounter, string dialogState, List<StoreClient> waiting,
            List<StoreClient> stored, List<StoreClient> feedback, HashSet<object> excluded, AdapterSet adapters, out GraphWriter writer)
        {
            var cap = new Captured { HasCounter = withCounter, DialogState = withCounter ? dialogState : DialogNone, Waiting = waiting.Count };
            var w = new GraphWriter(SnapshotContext.Create(adapters));
            writer = w;
            w.AddRootList(QueueRoot, stored);
            w.AddRootList(FeedbackRoot, feedback);

            if (withCounter)
            {
                w.AddRoot("counter", store.currentClientInstance);
                DialogUIManager dialogs = DialogUIManager.Instance;
                w.AddRoot("dialog", dialogs != null && cap.DialogState != DialogNone ? dialogs.currentDialog : null);
                w.AddRoot("storeNegotiated", store.currentNegociatedItem);
                NegociationUIManager negotiation = NegociationUIManager.Instance;
                w.AddRoot("negotiated", negotiation != null ? negotiation.currentNegociatedItem : null);
                cap.NegotiationOpen = negotiation != null && negotiation.IsOpen;

                BargainUIManager bargain = BargainUIManager.Instance;
                if (bargain != null)
                {
                    List<FieldInfo> fields = BargainFields();
                    var values = new List<object>();
                    var names = new List<string>();
                    foreach (FieldInfo f in fields)
                    {
                        values.Add(f.GetValue(bargain));
                        names.Add(f.Name);
                    }
                    w.AddRootList("bargain", values);
                    cap.BargainFields = string.Join(",", names.ToArray());
                    cap.BargainOpen = bargain.IsOpen;
                }

                var features = new List<object>();
                foreach (GameItem item in CounterItems(entry))
                {
                    features.Add(item);
                    features.Add(item.itemFeatures);
                }
                w.AddRootList("features", features);
                cap.Sprite = SpriteKeyOf(store.currentClientInstance);
            }

            var slots = new List<object>();
            int index = 0;
            foreach (StoreClient c in waiting)
            {
                if (excluded.Contains(c))
                    slots.Add(CustomerQueue.Encode(new List<StoreClient> { c }));
                else
                    slots.Add(index++);
            }
            w.AddRootList(SlotsRoot, slots);

            cap.Graph = w.Finish();
            cap.Objects = w.ObjectCount;
            return cap;
        }

        // Writes one customer alone and reads it back, as the whole snapshot is read back.
        private static bool ReadsBackAlone(string root, StoreClient client, AdapterSet adapters, List<string> mods, out string problem)
        {
            problem = null;
            var w = new GraphWriter(SnapshotContext.Create(adapters));
            w.AddRootList(root, new List<StoreClient> { client });
            string text = w.Finish();
            if (w.Failures.Count > 0)
            {
                foreach (WriteFailure f in w.Failures)
                    AddMod(mods, f.Mod);
                problem = string.Join("; ", w.Errors.GetRange(0, Math.Min(5, w.Errors.Count)).ToArray());
                return false;
            }
            var check = new GraphReader(SnapshotContext.Create(adapters), true);
            if (check.Read(text))
                return true;
            foreach (Assembly a in check.FailedAssemblies)
                AddMod(mods, ModOf(a));
            problem = "read-back check: " + string.Join("; ", check.Errors.GetRange(0, Math.Min(5, check.Errors.Count)).ToArray());
            return false;
        }

        private static List<StoreClient> WithoutNull(List<StoreClient> clients)
        {
            var list = new List<StoreClient>();
            if (clients != null)
            {
                foreach (StoreClient c in clients)
                {
                    if (c != null)
                        list.Add(c);
                }
            }
            return list;
        }

        // The index of the root list entry a path starts in: 3 for "queue[3].mainDialogue" and root "queue".
        private static bool RootIndex(string path, string root, out int index)
        {
            index = -1;
            if (path == null || !path.StartsWith(root + "[", StringComparison.Ordinal))
                return false;
            int start = root.Length + 1;
            int close = path.IndexOf(']', start);
            return close > start && int.TryParse(path.Substring(start, close - start), NumberStyles.None, CultureInfo.InvariantCulture, out index);
        }

        private static void Exclude(HashSet<object> excluded, List<StoreClient> waiting, StoreClient client, string reason)
        {
            excluded.Add(client);
            int position = -1;
            for (int i = 0; i < waiting.Count; i++)
            {
                if (ReferenceEquals(waiting[i], client))
                    position = i;
            }
            UnityEngine.Debug.LogWarning(F5SnapshotSaveMod.LogPrefix + "waiting customer " + position + " '" + client.identifier
                + "' left out of the snapshot; it is created again by identifier after loading: " + reason);
        }

        private static string Describe(WriteFailure f)
        {
            string text = f.Path + ": " + f.Message;
            if (f.Type != null)
                text += " [" + (f.Type.FullName ?? f.Type.Name) + ", " + f.Type.Assembly.GetName().Name + (f.Mod != null ? ", mod " + f.Mod : "") + "]";
            else if (f.Mod != null)
                text += " [mod " + f.Mod + "]";
            return text;
        }

        private static Outcome Refuse(Outcome outcome, Refusal refusal, string error)
        {
            outcome.Snapshot = null;
            outcome.Refused = refusal;
            outcome.Error = error;
            LogMods(outcome.Mods);
            return outcome;
        }

        // The mod of an assembly, for the window naming incompatible mods; the game, the framework and this mod are not named.
        private static string ModOf(Assembly a)
        {
            try
            {
                return a != null ? ModLoader.ModIdOfAssembly(a) : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void AddMod(List<string> mods, string mod)
        {
            if (!string.IsNullOrEmpty(mod) && mod != F5SnapshotSaveMod.ModId && !mods.Contains(mod))
                mods.Add(mod);
        }

        private static void LogMods(List<string> mods)
        {
            if (mods.Count == 0)
                return;
            var names = new List<string>();
            foreach (string mod in mods)
                names.Add(mod + " (" + IncompatibleNotice.DisplayName(mod) + ")");
            UnityEngine.Debug.LogWarning(F5SnapshotSaveMod.LogPrefix + "mods whose objects could not be stored in this F5 save: " + string.Join(", ", names.ToArray()));
        }

        // The sprite shown for the customer: GetNextClient shows SpriteDict.ShowClientSprite(client.GetSprite()), but
        // the waiting customers of the morning get a random sprite name instead. The name of the shown sprite is
        // stored when it differs from the client's own key.
        private static string SpriteKeyOf(StoreClientInstance instance)
        {
            try
            {
                StoreClient client = instance.GetClientBlueprint();
                string key = client.GetSprite();
                StoreClientMono mono = StoreClientMono.Instance;
                if (mono == null || mono.image == null || mono.image.sprite == null || SpriteDict.Instance == null)
                    return key;
                if (ReferenceEquals(SpriteDict.Instance.GetSprite(key), mono.image.sprite))
                    return key;
                return mono.image.sprite.name;
            }
            catch (Exception)
            {
                return "";
            }
        }

        internal static void WriteKeys(Captured cap, int day, PlayerStore.StoreState state)
        {
            Set(FormatKey, Codec.Format.ToString(CultureInfo.InvariantCulture));
            Set(DayKey, day.ToString(CultureInfo.InvariantCulture));
            Set(StateKey, ((int)state).ToString(CultureInfo.InvariantCulture));
            Set(TimeKey, DateTime.Now.Ticks.ToString(CultureInfo.InvariantCulture));
            Set(GraphKey, cap.Graph);
            Set(DialogKey, cap.DialogState);
            Set(NegotiationKey, cap.NegotiationOpen ? "1" : "0");
            Set(BargainOpenKey, cap.BargainOpen ? "1" : "0");
            Set(BargainFieldsKey, cap.BargainFields);
            Set(SpriteKey, cap.Sprite ?? "");
        }

        internal static void ClearKeys()
        {
            foreach (string key in Keys)
                ModHelper.RemoveModData(ModId, key);
        }

        internal static bool Present()
        {
            return ModHelper.HasModData(ModId, GraphKey);
        }

        // A snapshot is used only for the save it was written with: same day and store state, and written within two
        // minutes of the time the game recorded for the save that was loaded. Keys left behind in modData by a save
        // made while this mod was not running fail the time check.
        // 0.5.1: an earlier save of the slot (the F7 key, or an earlier save on the Load Game screen, LoadGameUI.LoadSlot)
        // is loaded through NewGameData.saveFile (SaveFiles.ResolveLoadFile) and is checked against its own entry in the
        // slot's history. Up to 0.5.0 every load was checked against the slot's newest save (preview_<slot>), so the
        // snapshot of an F5 save that had moved into the history was not used.
        internal static bool BelongsToLoadedSave(PlayerStore store, StoreStation station, out string reason)
        {
            reason = null;
            int day = Legacy.ParseInt(Get(DayKey), -1);
            int state = Legacy.ParseInt(Get(StateKey), -1);
            if (day != station.dayCounter || state != (int)store.storeState)
            {
                reason = "snapshot of day " + day + " state " + state + ", loaded day " + station.dayCounter + " state " + (int)store.storeState;
                return false;
            }
            long ticks;
            if (!long.TryParse(Get(TimeKey), NumberStyles.Integer, CultureInfo.InvariantCulture, out ticks))
            {
                reason = "no time";
                return false;
            }
            DateTime saved;
            string which;
            if (!LoadedSaveTime(store.saveSlotId, out saved, out which))
                return true;
            double seconds = Math.Abs((saved - new DateTime(ticks)).TotalSeconds);
            if (seconds > 120)
            {
                reason = "snapshot written " + new DateTime(ticks).ToString("u", CultureInfo.InvariantCulture) + ", " + which + " saved "
                    + saved.ToString("u", CultureInfo.InvariantCulture);
                return false;
            }
            return true;
        }

        // The time the game recorded for the save that was loaded: the slot's history entry of NewGameData.saveFile
        // when an earlier save was loaded, otherwise the slot's own record. False when there is no record; the snapshot
        // is then used, as before. A history file missing from the history list is checked against the slot's record.
        private static bool LoadedSaveTime(int slot, out DateTime saved, out string which)
        {
            saved = DateTime.MinValue;
            which = "slot";
            string file = NewGameData.Instance != null ? NewGameData.Instance.saveFile : null;
            if (!string.IsNullOrEmpty(file))
            {
                try
                {
                    List<SaveHistoryEntry> history = SaveFiles.LoadHistory(slot);
                    if (history != null)
                    {
                        foreach (SaveHistoryEntry entry in history)
                        {
                            if (entry != null && string.Equals(entry.file, file, StringComparison.Ordinal))
                            {
                                saved = entry.saveDateTime;
                                which = file;
                                return true;
                            }
                        }
                    }
                    which = "slot (" + file + " is not in the history list)";
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogWarning(F5SnapshotSaveMod.LogPrefix + "reading the history of slot " + slot + " failed; the snapshot is checked against the slot's newest save: " + e);
                    which = "slot";
                }
            }
            SavePreviewData preview = SaveFiles.IndexLoad<SavePreviewData>("preview_" + slot, null);
            if (preview == null)
                return false;
            saved = preview.lastSaveDateTime;
            return true;
        }

        // Reads the snapshot of the save that was loaded, in ModHook.OnGameLoadedLate, with the adapters collected for
        // this read. Up to 0.7.1 a snapshot that pointed at the state of CorpPresence was read on the first shutter update
        // instead; 0.8.0 no longer supports CorpPresence (T-029, yzz 2026-11-12) and does not read such a snapshot.
        internal static Restored Read(out List<string> errors)
        {
            return ReadStored(Take(), SpecAdapters.Collect(), out errors);
        }

        internal static Restored ReadStored(Stored stored, AdapterSet adapters, out List<string> errors)
        {
            errors = new List<string>();
            string format = stored.Format;
            if (!Codec.Accepts(format))
            {
                errors.Add("unknown snapshot format " + format);
                return null;
            }
            var reader = new GraphReader(SnapshotContext.Create(adapters), false);
            if (!reader.Read(stored.Graph))
            {
                errors.AddRange(reader.Errors);
                return null;
            }
            foreach (string warning in reader.Warnings)
                UnityEngine.Debug.LogWarning(F5SnapshotSaveMod.LogPrefix + warning);

            var r = new Restored
            {
                DialogState = stored.Dialog,
                NegotiationOpen = stored.Negotiation == "1",
                BargainOpen = stored.BargainOpen == "1",
                Sprite = stored.Sprite,
            };
            List<object> queue = reader.Root(QueueRoot);
            foreach (object o in queue)
            {
                var c = o as StoreClient;
                if (c != null)
                    r.Queue.Add(c);
            }
            if (format == "3")
                ReadSlots(reader, queue, r, errors);
            else
            {
                foreach (StoreClient c in r.Queue)
                    r.Slots.Add(new WaitingSlot { Client = c });
            }
            foreach (object o in reader.Root(FeedbackRoot))
            {
                var c = o as StoreClient;
                if (c != null)
                    r.Feedback.Add(c);
            }
            r.Extras.AddRange(reader.Extras);
            r.Counter = reader.RootValue("counter") as StoreClientInstance;
            if (r.Counter != null)
            {
                r.Dialog = reader.RootValue("dialog") as Dialogue;
                r.StoreNegotiated = reader.RootValue("storeNegotiated") as GameItem;
                r.Negotiated = reader.RootValue("negotiated") as GameItem;
                List<object> features = reader.Root("features");
                for (int i = 0; i + 1 < features.Count; i += 2)
                {
                    var item = features[i] as GameItem;
                    var list = features[i + 1] as List<ItemFeature>;
                    if (item != null && list != null)
                        r.Features.Add(new KeyValuePair<GameItem, List<ItemFeature>>(item, list));
                }
                string[] names = (stored.BargainFields ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                List<object> values = reader.Root("bargain");
                for (int i = 0; i < names.Length && i < values.Count; i++)
                {
                    FieldInfo f = typeof(BargainUIManager).GetField(names[i], BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    if (f == null)
                        errors.Add("BargainUIManager." + names[i] + " not found");
                    else
                        r.Bargain.Add(new KeyValuePair<FieldInfo, object>(f, values[i]));
                }
                if (r.DialogState != DialogNone && r.Dialog == null)
                    errors.Add("conversation line missing");
            }
            if (reader.Errors.Count > 0)
                errors.AddRange(reader.Errors);
            return errors.Count == 0 ? r : null;
        }

        // Format 3: the line from SlotsRoot. Every entry of the queue root has exactly one place; a customer that was left
        // out is kept as identifier data and created on the first shutter update, not now (CreateLeftOut).
        private static void ReadSlots(GraphReader reader, List<object> queue, Restored r, List<string> errors)
        {
            if (!reader.HasRoot(SlotsRoot))
            {
                errors.Add(SlotsRoot + " missing");
                return;
            }
            var used = new bool[queue.Count];
            foreach (object slot in reader.Root(SlotsRoot))
            {
                if (slot is int)
                {
                    int i = (int)slot;
                    StoreClient c = i >= 0 && i < queue.Count ? queue[i] as StoreClient : null;
                    if (c == null || used[i])
                    {
                        errors.Add(SlotsRoot + ": no waiting customer " + i + " in the snapshot");
                        continue;
                    }
                    used[i] = true;
                    r.Slots.Add(new WaitingSlot { Client = c });
                }
                else if (slot is string)
                {
                    List<CustomerQueue.Entry> entries = CustomerQueue.Decode((string)slot);
                    if (entries.Count > 1)
                        errors.Add(SlotsRoot + ": more than one customer in one place");
                    else if (entries.Count == 1)
                        r.Slots.Add(new WaitingSlot { Entry = entries[0] });
                    else
                        UnityEngine.Debug.LogWarning(F5SnapshotSaveMod.LogPrefix + "a waiting customer left out of the snapshot has no identifier; it does not come back");
                }
                else
                    errors.Add(SlotsRoot + ": unexpected entry " + (slot == null ? "null" : slot.GetType().Name));
            }
            for (int i = 0; i < used.Length; i++)
            {
                if (!used[i])
                    errors.Add(SlotsRoot + ": waiting customer " + i + " of the snapshot has no place in the line");
            }
        }

        // 0.8.0 (plan 3.6, spec 4.4): the waiting customers rebuilt from the snapshot, in their order. They are put back on
        // the first shutter update after the extra data has been given back; the customers left out of the snapshot are
        // created after that (CreateLeftOut).
        internal static List<StoreClient> RebuiltWaiting(Restored r)
        {
            var line = new List<StoreClient>();
            foreach (WaitingSlot slot in r.Slots)
            {
                if (slot.Client != null)
                    line.Add(slot.Client);
            }
            return line;
        }

        // 0.8.0: creates the customers that were left out of the snapshot by identifier, each factory once per load, and puts
        // each in its place in the line. Called when the rebuilt waiting customers are back at the front of the line (index
        // 0 onwards of stack) and the customer at the counter is back: factories look at the store as it is, for example
        // Gunsmith Expansion's pickup factory gives a customer only a reservation that no customer in the store or in the
        // line holds yet (Reservations.SyncAssignments). Factories can also have side effects (Moneylending's borrowing
        // visitor updates its ledger), and by now every mod's load handlers have run. A customer that cannot be created
        // does not come back. Returns the number of customers created.
        internal static int CreateLeftOut(Restored r, List<StoreClient> stack)
        {
            int placed = 0;
            int created = 0;
            foreach (WaitingSlot slot in r.Slots)
            {
                if (slot.Client != null)
                {
                    placed++;
                    continue;
                }
                int failed;
                List<StoreClient> made = CustomerQueue.CreateAll(new List<CustomerQueue.Entry> { slot.Entry }, out failed);
                if (made.Count > 0)
                {
                    stack.Insert(Math.Min(placed, stack.Count), made[0]);
                    placed++;
                    created++;
                    UnityEngine.Debug.Log(F5SnapshotSaveMod.LogPrefix + "created again by identifier: '" + slot.Entry.Identifier + "'");
                }
                else
                    UnityEngine.Debug.LogWarning(F5SnapshotSaveMod.LogPrefix + "the waiting customer '" + slot.Entry.Identifier + "' left out of the snapshot could not be created again; it does not come back");
            }
            return created;
        }

        // 0.8.0 (spec 3.6, 4.4): gives the extra data of the snapshot back to the mods that wrote it, on the first shutter
        // update before any customer is created or put back and before the counter view is restored, with the adapters
        // collected now. False when a provider has no adapter with loadExtra now or a loadExtra throws: the snapshot is then
        // not used. What was given back before the failure sits in weak-keyed tables under objects of this snapshot, which
        // are dropped with it (loadExtra contract, spec 3.1).
        internal static bool GiveExtraBack(Restored r, out string problem)
        {
            problem = null;
            if (r.Extras.Count == 0)
                return true;
            AdapterSet adapters = SpecAdapters.Collect();
            foreach (ExtraValue extra in r.Extras)
            {
                Adapter adapter = adapters.For(extra.Provider);
                if (adapter == null || adapter.LoadExtra == null)
                {
                    problem = "no adapter of " + extra.Provider.GetName().Name + " takes its extra data back";
                    return false;
                }
                try
                {
                    adapter.LoadExtra(extra.Owner, extra.Value);
                }
                catch (Exception e)
                {
                    problem = "loadExtra of mod '" + adapter.Mod + "' threw: " + e;
                    return false;
                }
            }
            UnityEngine.Debug.Log(F5SnapshotSaveMod.LogPrefix + "extra data given back: " + r.Extras.Count + " record(s)");
            return true;
        }

        // Puts the customer back at the counter as they were when F5 was pressed: arrived (no walk, no arrival
        // actions, no second count of the visit), the haggling state, the item features of the counter items, the
        // negotiation tablet and haggling panel as they were, and the conversation line on screen. The line is shown
        // again with the game's own display method, which leads to the same waiting for advance or the same choices;
        // its start action is not run again, and its end action has already run (Dialogue.isEndActionExecuted).
        internal static void ApplyCounter(Restored r)
        {
            PlayerStore store = PlayerStore.Instance;
            StoreUIManager ui = StoreUIManager.Instance;
            StoreClient client = r.Counter.GetClientBlueprint();

            store.currentClientInstance = r.Counter;
            store.isClientArrived = true;
            store.currentNegociatedItem = r.StoreNegotiated;
            if (NegociationUIManager.Instance != null)
                NegociationUIManager.Instance.currentNegociatedItem = r.Negotiated;
            foreach (KeyValuePair<GameItem, List<ItemFeature>> pair in r.Features)
                pair.Key.itemFeatures = pair.Value;
            BargainUIManager bargain = BargainUIManager.Instance;
            if (bargain != null)
            {
                foreach (KeyValuePair<FieldInfo, object> field in r.Bargain)
                    field.Key.SetValue(bargain, field.Value);
            }

            StoreClientMono mono = ui.clientGameObject;
            if (mono != null && SpriteDict.Instance != null)
            {
                UnityEngine.Sprite sprite = SpriteDict.Instance.GetSprite(string.IsNullOrEmpty(r.Sprite) ? client.GetSprite() : r.Sprite);
                if (sprite != null && mono.image != null)
                    mono.image.sprite = sprite;
            }
            if (mono != null)
                mono.SetImmediatlyArrive();
            if (StoreSecButton.Instance != null)
                StoreSecButton.Instance.ResetLight();
            ui.isAboutToStartMainDialog = false;

            DialogUIManager dialogs = DialogUIManager.Instance;
            if (dialogs != null && r.Dialog != null && r.DialogState != DialogNone)
            {
                // Showing the line again adds it to the conversation history a second time; the history is put back.
                string[] history = store.dialogHistory != null ? store.dialogHistory.ToArray() : null;
                dialogs.currentDialog = r.Dialog;
                if (r.Dialog.title == "PLAYER")
                    dialogs.DisplayerPlayerText(r.Dialog);
                else if (r.Dialog.title == "SEC")
                    dialogs.DisplaySecText(r.Dialog);
                else
                    dialogs.DisplayClientText(r.Dialog);
                if (history != null)
                {
                    store.dialogHistory.Clear();
                    foreach (string line in history)
                        store.dialogHistory.Enqueue(line);
                    if (RecorderUIManager.Instance != null)
                        RecorderUIManager.Instance.RefreshUI();
                }
            }
            if (r.NegotiationOpen && NegociationUIManager.Instance != null && !NegociationUIManager.Instance.IsOpen)
                NegociationUIManager.Instance.OpenUI();
            if (r.BargainOpen && bargain != null && !bargain.IsOpen)
                bargain.OpenUI();
        }

        private static void Set(string key, string value)
        {
            ModHelper.SetModData(ModId, key, value);
        }

        private static string Get(string key)
        {
            return ModHelper.GetModData(ModId, key, "");
        }
    }

    // Whether the customer at the counter is at a point where the snapshot can be taken: arrived, not walking, the
    // main conversation started, and the conversation either over, waiting for the player to advance, or showing its
    // choices, with nothing scheduled (no typewriter running, no automatic advance, no pending end action, no delayed
    // game step from MetaHelper.WaitAndDo).
    internal static class Stability
    {
        private static readonly FieldInfo UncompletedField = typeof(DialogUIManager).GetField("displayingUncompletedDialog", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo AutoAdvanceField = typeof(DialogUIManager).GetField("isAutoAdvancePending", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo EndPendingField = typeof(DialogUIManager).GetField("isEndPending", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo ScheduledEndField = typeof(DialogUIManager).GetField("scheduledEndActionLines", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo TypingMethod = typeof(DialogUIManager).GetMethod("IsAnyBubbleTyping", BindingFlags.Instance | BindingFlags.NonPublic);

        // Returns null when stable, otherwise the reason (for the log); dialogState is "none", "wait" or "choice".
        internal static string Check(PlayerStore store, StoreUIManager ui, out string dialogState)
        {
            dialogState = Snapshot.DialogNone;
            if (!store.isClientArrived)
                return "customer not arrived";
            if (ui.clientGameObject != null && ui.clientGameObject.IsMoving())
                return "customer walking";
            if (ui.isAboutToStartMainDialog)
                return "conversation about to start";
            if (Timers.Pending)
                return "delayed game step pending";

            DialogUIManager d = DialogUIManager.Instance;
            if (d == null)
                return "no dialogue manager";
            if (d.isSecMonologuePlaying)
                return "security monologue";
            if (UncompletedField == null || AutoAdvanceField == null || EndPendingField == null || ScheduledEndField == null || TypingMethod == null)
                return "dialogue manager changed";
            if (UncompletedField.GetValue(d) != null)
                return "line still being shown";
            if ((bool)TypingMethod.Invoke(d, null))
                return "typewriter running";
            if ((bool)AutoAdvanceField.GetValue(d))
                return "automatic advance pending";
            if ((bool)EndPendingField.GetValue(d))
                return "conversation ending";
            var scheduled = ScheduledEndField.GetValue(d) as System.Collections.ICollection;
            if (scheduled != null && scheduled.Count > 0)
                return "end action pending";

            StoreClient client = store.currentClientInstance.GetClientBlueprint();
            if (client.mainDialogue != null && !client.isMainDialogueStarted)
                return "main conversation not started";

            Dialogue line = d.currentDialog;
            if (line == null)
                return null;
            if (line.choices != null && line.choices.Count > 0)
            {
                bool shown = false;
                if (d.dialogChoiceBubbles != null)
                {
                    foreach (DialogChoiceBubble bubble in d.dialogChoiceBubbles)
                    {
                        if (bubble != null && bubble.gameObject.activeSelf)
                            shown = true;
                    }
                }
                if (!shown)
                    return "choices not shown yet";
                dialogState = Snapshot.DialogChoice;
                return null;
            }
            if (d.isWaitingForManualAdvance)
            {
                dialogState = Snapshot.DialogWaiting;
                return null;
            }
            return "line in progress";
        }
    }

    // Latest end time of the delayed steps started with MetaHelper.WaitAndDo (the conversation flow, the arrival of
    // a customer, the shutter, arrests). A step stopped before its end only blocks until its end time has passed.
    internal static class Timers
    {
        private static float latestEnd;

        internal static void Note(float duration)
        {
            float end = UnityEngine.Time.time + Math.Max(0f, duration);
            if (end > latestEnd)
                latestEnd = end;
        }

        internal static bool Pending
        {
            get { return UnityEngine.Time.time < latestEnd; }
        }
    }
}
