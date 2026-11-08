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
        }

        internal sealed class Restored
        {
            internal List<StoreClient> Queue = new List<StoreClient>();
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

        internal static Captured Capture(PlayerStore store, EmporiumEntry entry, bool withCounter, string dialogState, out string error)
        {
            error = null;
            var cap = new Captured { HasCounter = withCounter, DialogState = withCounter ? dialogState : DialogNone };
            var ctx = SnapshotContext.Create();
            var w = new GraphWriter(ctx);
            StoreClientManager manager = store.storeClientManager;
            w.AddRootList("queue", manager != null && manager.clientStack != null ? manager.clientStack : new List<StoreClient>());
            w.AddRootList("feedback", manager != null && manager.chemicalFeedbackStack != null ? manager.chemicalFeedbackStack : new List<StoreClient>());
            cap.Waiting = manager != null && manager.clientStack != null ? manager.clientStack.Count : 0;

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

            string text = w.Finish();
            cap.Objects = w.ObjectCount;
            if (w.Errors.Count > 0)
            {
                error = string.Join("; ", w.Errors.GetRange(0, Math.Min(10, w.Errors.Count)).ToArray());
                return null;
            }

            // Read the snapshot back at once (items stored with the item encoding are not decoded here), so that a
            // snapshot that cannot be rebuilt on this game version is never written.
            var check = new GraphReader(SnapshotContext.Create(), true);
            if (!check.Read(text))
            {
                error = "read-back check: " + string.Join("; ", check.Errors.GetRange(0, Math.Min(10, check.Errors.Count)).ToArray());
                return null;
            }
            cap.Graph = text;
            return cap;
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

        internal static Restored Read(out List<string> errors)
        {
            errors = new List<string>();
            string format = Get(FormatKey);
            if (format != Codec.Format.ToString(CultureInfo.InvariantCulture))
            {
                errors.Add("unknown snapshot format " + format);
                return null;
            }
            var reader = new GraphReader(SnapshotContext.Create(), false);
            if (!reader.Read(Get(GraphKey)))
            {
                errors.AddRange(reader.Errors);
                return null;
            }
            foreach (string warning in reader.Warnings)
                UnityEngine.Debug.LogWarning(F5SnapshotSaveMod.LogPrefix + warning);

            var r = new Restored
            {
                DialogState = Get(DialogKey),
                NegotiationOpen = Get(NegotiationKey) == "1",
                BargainOpen = Get(BargainOpenKey) == "1",
                Sprite = Get(SpriteKey),
            };
            foreach (object o in reader.Root("queue"))
            {
                var c = o as StoreClient;
                if (c != null)
                    r.Queue.Add(c);
            }
            foreach (object o in reader.Root("feedback"))
            {
                var c = o as StoreClient;
                if (c != null)
                    r.Feedback.Add(c);
            }
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
                string[] names = Get(BargainFieldsKey).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
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
