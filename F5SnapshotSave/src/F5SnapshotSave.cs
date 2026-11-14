using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;

[assembly: System.Reflection.AssemblyTitle("F5SnapshotSave")]
[assembly: System.Reflection.AssemblyVersion("0.8.1.0")]

namespace F5SnapshotSave
{
    // Entry point found by the game's ModLoader, which loads mods once per launch and applies the Harmony patches in
    // this assembly (Harmony ID "mod.<manifest ID>") before calling Init and OnEnable.
    public sealed class F5SnapshotSaveMod : IMod
    {
        internal const string ModId = "F5SnapshotSave";
        internal const string LogPrefix = "[F5SnapshotSave] ";
        private static UnityEngine.GameObject host;

        public void Init(ModManifest manifest)
        {
            UnityEngine.Debug.Log(LogPrefix + "v" + manifest.ModVersion + " loaded");
            try
            {
                SaveKey.Load();
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning(LogPrefix + "reading the save key from the mod settings failed; the key is F5: " + e);
            }
            try
            {
                LoadKey.Load();
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning(LogPrefix + "reading the load key from the mod settings failed; the key is F8: " + e);
            }
            try
            {
                F5LoadKey.Load();
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning(LogPrefix + "reading the F5 save load key from the mod settings failed; the key is F7: " + e);
            }
            try
            {
                SaveSettings.Load();
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning(LogPrefix + "reading the save history settings failed; original limits and copying apply: " + e);
            }
        }

        public void OnEnable()
        {
            ModHook.OnGameLoadedLate -= Restore.OnGameLoaded;
            ModHook.OnGameLoadedLate += Restore.OnGameLoaded;
            ModHook.OnGameLoadedLate -= QuickLoad.OnGameLoaded;
            ModHook.OnGameLoadedLate += QuickLoad.OnGameLoaded;
            if (host != null)
                return;
            host = new UnityEngine.GameObject("F5SnapshotSave");
            UnityEngine.Object.DontDestroyOnLoad(host);
            host.AddComponent<F5Listener>();
        }

        public void OnDisable()
        {
            ModHook.OnGameLoadedLate -= QuickLoad.OnGameLoaded;
            ModHook.OnGameLoadedLate -= Restore.OnGameLoaded;
            if (host == null)
                return;
            UnityEngine.Object.Destroy(host);
            host = null;
        }

        // F5ForceSave 1.4.0 leaves the save history to this mod while an object named "F5SnapshotSave" exists. When
        // this mod cannot run alongside it, the object is renamed and F5ForceSave keeps that work.
        internal static void MarkInactive()
        {
            if (host != null)
                host.name = "F5SnapshotSave (inactive)";
        }
    }

    // The rows of the save key and the load key in the game options exist while this listener is on and the
    // arrangement with F5ForceSave has not failed (this mod neither saves nor loads then). F5ForceSave adds its own
    // rows while its listener is on; Coexist switches that listener off, so with both mods enabled only this mod's
    // rows are shown.
    public sealed class F5Listener : UnityEngine.MonoBehaviour
    {
        private float nextAllowedTime;

        private void OnEnable()
        {
            try
            {
                SaveKey.AddOption();
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning(F5SnapshotSaveMod.LogPrefix + "adding the save key to the game options failed; it can still be set in the mod settings: " + e);
            }
            try
            {
                LoadKey.AddOption();
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning(F5SnapshotSaveMod.LogPrefix + "adding the load key to the game options failed; it can still be set in the mod settings: " + e);
            }
            try
            {
                F5LoadKey.AddOption();
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning(F5SnapshotSaveMod.LogPrefix + "adding the F5 save load key to the game options failed; it can still be set in the mod settings: " + e);
            }
        }

        private void OnDisable()
        {
            RemoveOption();
        }

        private static void RemoveOption()
        {
            try
            {
                SaveKey.RemoveOption();
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning(F5SnapshotSaveMod.LogPrefix + "removing the save key from the game options failed: " + e);
            }
            try
            {
                LoadKey.RemoveOption();
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning(F5SnapshotSaveMod.LogPrefix + "removing the load key from the game options failed: " + e);
            }
            try
            {
                F5LoadKey.RemoveOption();
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning(F5SnapshotSaveMod.LogPrefix + "removing the F5 save load key from the game options failed: " + e);
            }
        }

        private void Update()
        {
            if (!Coexist.Checked)
            {
                Coexist.Check();
                if (Coexist.Failed)
                    RemoveOption();
            }
            // SuppressHotkeys is true while the game options wait for a key to bind and in the frame that ends the
            // wait, so pressing either key there binds it without saving or loading.
            if (KeyBindHelper.SuppressHotkeys || UnityEngine.Time.unscaledTime < nextAllowedTime || QuickLoad.Pending)
                return;
            bool save = SaveKey.Pressed();
            bool load = !save && LoadKey.Pressed();
            bool loadF5 = !save && !load && F5LoadKey.Pressed();
            if (!save && !load && !loadF5)
                return;
            nextAllowedTime = UnityEngine.Time.unscaledTime + 1f;
            if (Coexist.Failed)
                return;
            try
            {
                if (save)
                    QuickSave.TrySave();
                else if (load)
                    QuickLoad.TryLoad(this);
                else
                    QuickLoad.TryLoadLastF5(this);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning(F5SnapshotSaveMod.LogPrefix + (save ? "save" : "load") + " key handling failed: " + e);
            }
        }
    }

    // F5 Quick Save (F5ForceSave) handles F5 as well. When it is running, its key listener is switched off and its
    // load handler is skipped; this mod then handles F5 and reads the F5ForceSave data itself. If that cannot be
    // arranged, this mod stays inactive.
    internal static class Coexist
    {
        internal const string OtherId = "F5ForceSave";
        internal static bool Checked;
        internal static bool OtherRunning;
        internal static bool Failed;
        internal static bool NoticePending;

        internal static void Check()
        {
            Checked = true;
            try
            {
                LoadedMod other = null;
                if (ModLoader.Mods != null)
                {
                    foreach (LoadedMod m in ModLoader.Mods)
                    {
                        if (m != null && m.Manifest != null && m.Manifest.ID == OtherId && m.Instance != null && m.Assembly != null)
                            other = m;
                    }
                }
                if (other == null)
                    return;
                OtherRunning = true;
                Type listener = other.Assembly.GetType("F5ForceSave.F5Listener", false);
                Type extras = other.Assembly.GetType("F5ForceSave.SaveExtras", false);
                MethodInfo onLoaded = extras != null ? extras.GetMethod("OnGameLoaded", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public) : null;
                if (listener == null || onLoaded == null)
                {
                    Failed = true;
                    F5SnapshotSaveMod.MarkInactive();
                    UnityEngine.Debug.LogWarning(F5SnapshotSaveMod.LogPrefix + "F5ForceSave is running but its listener or load handler was not found; this mod stays inactive");
                    return;
                }
                var harmony = new Harmony("mod." + F5SnapshotSaveMod.ModId + ".coexist");
                harmony.Patch(onLoaded, prefix: new HarmonyMethod(typeof(Coexist).GetMethod(nameof(SkipOtherLoad), BindingFlags.Static | BindingFlags.NonPublic)));
                foreach (UnityEngine.Object o in UnityEngine.Object.FindObjectsOfType(listener))
                {
                    var behaviour = o as UnityEngine.Behaviour;
                    if (behaviour != null)
                        behaviour.enabled = false;
                }
                NoticePending = true;
                UnityEngine.Debug.Log(F5SnapshotSaveMod.LogPrefix + "F5ForceSave is running: its F5 listener is switched off and its load handler skipped; F5 is handled here");
            }
            catch (Exception e)
            {
                Failed = true;
                F5SnapshotSaveMod.MarkInactive();
                UnityEngine.Debug.LogWarning(F5SnapshotSaveMod.LogPrefix + "arranging F5 with F5ForceSave failed; this mod stays inactive: " + e);
            }
        }

        private static bool SkipOtherLoad()
        {
            return false;
        }
    }

    // The game itself saves only at the start of a day (PlayerStore.SaveGame after the end-of-day sequence and after
    // a new game). F5 calls the same SaveGame at any time in the store: in the morning, while open (also with a
    // customer at the counter), and after closing. What SaveGame leaves out is written into PlayerStore.modData,
    // which is part of the same slot file, and removed again after the save.
    // The save before sleeping (SleepSave, T-031) is the same save, made when "End the Day" is clicked.
    internal static class QuickSave
    {
        internal static bool InProgress;
        internal static bool SlotWritten;

        internal static void TrySave()
        {
            PlayerStore store = PlayerStore.HasInstance ? PlayerStore.Instance : null;
            StoreUIManager ui = StoreUIManager.Instance;
            StoreStation station = StoreStation.Instance;
            EmporiumEntry entry = EmporiumEntry.Instance;
            if (store == null || ui == null || station == null || entry == null)
                return;
            if (StoreUIManager.IsAnyTyping())
                return;

            bool saved;
            List<string> mods;
            string message = Save(store, ui, station, entry, false, out saved, out mods);
            Report(ui, message, saved);
            if (mods != null)
                IncompatibleNotice.After(mods);
        }

        // T-031: the save before sleeping (SleepSave), with the same checks and the same save as the key. The message and
        // the mods for IncompatibleNotice are returned for SleepSave to show on the next morning; the message is null when
        // the store is not there. No key was pressed, so a text field with the focus does not stop it, and a refusal leaves
        // out the sentence about the save a quit would return to: the game saves the next morning right after.
        internal static string SaveBeforeSleep(out bool saved, out List<string> mods)
        {
            saved = false;
            mods = null;
            PlayerStore store = PlayerStore.HasInstance ? PlayerStore.Instance : null;
            StoreUIManager ui = StoreUIManager.Instance;
            StoreStation station = StoreStation.Instance;
            EmporiumEntry entry = EmporiumEntry.Instance;
            if (store == null || ui == null || station == null || entry == null)
                return null;
            return Save(store, ui, station, entry, true, out saved, out mods);
        }

        // mods: the mods for IncompatibleNotice after the message (a refused snapshot or a save), otherwise null.
        private static string Save(PlayerStore store, StoreUIManager ui, StoreStation station, EmporiumEntry entry, bool beforeSleep,
            out bool saved, out List<string> mods)
        {
            saved = false;
            mods = null;
            bool withCounter;
            string dialogState;
            string refusal = Refusal(store, ui, entry, !beforeSleep, out withCounter, out dialogState);
            if (refusal != null)
                return beforeSleep ? Text.SleepNotSaved(refusal) : refusal;

            // 0.8.0: a waiting customer carrying something this mod cannot store is left out of the snapshot and created
            // again by identifier after loading; the save is refused only for the counter group and for the chemical
            // feedback customers (plan 3.4). A game whose code differs from the field table still fails the whole save.
            Snapshot.Outcome outcome;
            try
            {
                outcome = Snapshot.CaptureOutcome(store, entry, withCounter, dialogState);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning(F5SnapshotSaveMod.LogPrefix + "Snapshot.Capture threw: " + e);
                return beforeSleep ? Text.SleepFailed() : Text.Failed();
            }
            Snapshot.Captured cap = outcome.Snapshot;
            if (cap == null)
            {
                UnityEngine.Debug.LogWarning(F5SnapshotSaveMod.LogPrefix + "snapshot not possible: " + outcome.Error);
                SavePreviewData last = beforeSleep ? null : SaveUIManager.GetPreview(store.saveSlotId);
                string refused;
                if (outcome.Refused == Snapshot.Refusal.Feedback)
                    refused = Text.FeedbackNotStorable(last);
                else if (outcome.Refused == Snapshot.Refusal.Counter)
                    refused = Text.CounterNotStorable(last);
                else
                    refused = Text.QueueNotStorable(last);
                mods = outcome.Mods;
                return beforeSleep ? Text.SleepNotSaved(refused) : refused;
            }

            // The customer at the counter goes first into the F5ForceSave-format queue, so that F5ForceSave alone
            // (or this mod, when the snapshot cannot be rebuilt) puts them first in line.
            var queue = new List<StoreClient>();
            if (withCounter)
                queue.Add(store.currentClientInstance.GetClientBlueprint());
            if (store.storeClientManager != null && store.storeClientManager.clientStack != null)
                queue.AddRange(store.storeClientManager.clientStack);

            InProgress = true;
            SlotWritten = false;
            try
            {
                Legacy.Write(store, entry, station, queue);
                Snapshot.WriteKeys(cap, station.dayCounter, store.storeState);
                store.SaveGame();
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning(F5SnapshotSaveMod.LogPrefix + "SaveGame threw: " + e);
            }
            finally
            {
                InProgress = false;
                Legacy.Clear();
                Snapshot.ClearKeys();
            }

            UnityEngine.Debug.Log(F5SnapshotSaveMod.LogPrefix + "snapshot: " + cap.Objects + " objects, " + cap.Graph.Length + " characters"
                + (withCounter ? ", customer at the counter '" + store.currentClientInstance.GetClientBlueprint().identifier + "', conversation " + cap.DialogState : "")
                + (cap.Excluded > 0 ? ", " + cap.Excluded + " waiting customer(s) left out" : ""));
            if (!SlotWritten)
                return beforeSleep ? Text.SleepFailed() : Text.Failed();
            saved = true;
            mods = outcome.Mods;
            QuickHistory.RecordLatest(store.saveSlotId, SaveDetails.Make(store, station, cap.Waiting));
            string regenerated = cap.Excluded > 0 ? Text.SavedRegenerated(cap.Excluded) : "";
            if (beforeSleep)
                return Text.SleepSaved(station.dayCounter, store.storeState, DateTime.Now, regenerated);
            return Text.Saved(station.dayCounter, store.storeState, DateTime.Now, withCounter, cap.Waiting) + regenerated;
        }

        // withReturnPoint: the refusals that name the save a quit would return to (the newest save of the slot) do so.
        private static string Refusal(PlayerStore store, StoreUIManager ui, EmporiumEntry entry, bool withReturnPoint, out bool withCounter, out string dialogState)
        {
            withCounter = false;
            dialogState = Snapshot.DialogNone;
            if (ModHelper.IsOutsideStore())
                return Text.OutsideStore();

            GameOverUIManager gameOver = GameOverUIManager.Instance;
            if (gameOver != null && gameOver.uiPanel != null && gameOver.uiPanel.activeSelf)
                return Text.GameOver();

            ItemMouseDragHandler drag = ItemMouseDragHandler.current;
            ItemMultiSelectHandler groupDrag = ItemMultiSelectHandler.current;
            if ((drag != null && drag.IsDraggingItem) || (groupDrag != null && groupDrag.IsGroupDragging))
                return Text.Dragging();

            // 0.5.1: !ui.CanShoot() also covers a customer who survives a shot (the game 1.0.0.12, StoreUIManager.ShootSurvivor):
            // from the shot until that customer's survived-shot line starts, about 0.3 s after the aiming view closes, the
            // line waits in StoreUIManager.pendingSurvivedShot, which a snapshot does not hold, and CanShoot is false.
            if (store.isClientBeingArrested || store.isHandlingShootout || ui.isShot || ui.isShootingMode || !ui.CanShoot()
                || Active(ui.secGameObject) || Active(ui.secGameObject2))
                return Text.Security();

            InspectionUIManager inspection = InspectionUIManager.Instance;
            if (inspection != null && ((inspection.panel != null && inspection.panel.activeInHierarchy) || (inspection.confrontPanel != null && inspection.confrontPanel.activeInHierarchy)))
                return Text.Inspecting();

            DialoguePhoneUIManager phone = DialoguePhoneUIManager.Instance;
            if (phone != null && phone.currentDialog != null)
                return Text.Phone();

            if (HasItems(entry.backInvinvElementCounter) || HasItems(entry.afterhourInventory)
                || HasItems(entry.responseInventory) || HasItems(entry.responseInventoryClosable)
                || HasItems(entry.swapBufferElement))
                return Text.TemporaryNotEmpty();

            if (!store.IsClientPresent())
                return null;
            if (store.storeState != PlayerStore.StoreState.OPEN)
                return Text.CounterNotStorable(withReturnPoint ? SaveUIManager.GetPreview(store.saveSlotId) : null);
            string busy = Stability.Check(store, ui, out dialogState);
            if (busy != null)
            {
                UnityEngine.Debug.Log(F5SnapshotSaveMod.LogPrefix + "not saved, customer at the counter: " + busy);
                return Text.Busy();
            }
            withCounter = true;
            return null;
        }

        private static bool Active(GenericClientMono mono)
        {
            return mono != null && mono.gameObject.activeInHierarchy;
        }

        internal static bool HasItems(GameInventory inventory)
        {
            if (inventory == null)
                return false;
            List<GameItem> items = inventory.childItems;
            return items != null && items.Count > 0;
        }

        // ReputationUIManager.Notify has one text: a second call would replace this message, so the key conflict is
        // added to it.
        private static void Report(StoreUIManager ui, string message, bool saved)
        {
            if (KeyConflict.WithHighlight(SaveKey.Current))
                message = Notice.Show(ui, message, Text.HighlightConflict());
            else
                ui.Notify(message, "default");
            UnityEngine.Debug.Log(F5SnapshotSaveMod.LogPrefix + (saved ? "saved: " : "not saved: ") + message);
        }
    }

    // After loading: the F5 data of the save (this mod's snapshot and the F5ForceSave-format data) is read in
    // ModHook.OnGameLoadedLate, right after PlayerStore and the store inventories have been read from the slot file;
    // the customers and the open store are put back on the first shutter update, when the store scene has started.
    internal static class Restore
    {
        internal static bool SkipMorningReport;
        internal static bool PendingOpenShutter;
        internal static bool PendingShowFrontArea;
        // 0.8.0: the snapshot read in OnGameLoaded (the waiting customers and their places in the line, the feedback
        // customers, the customer at the counter, extra data), put back on the first shutter update.
        internal static Snapshot.Restored PendingSnapshot;
        // 0.8.0: the F5ForceSave-format queue (the customer at the counter first, then the waiting customers, by
        // identifier). Its customers are created on the first shutter update when no snapshot is put back: there is none,
        // it could not be read, or its extra data could not be given back. Up to 0.7.1 they were created in OnGameLoaded.
        internal static List<CustomerQueue.Entry> PendingLegacyQueue;
        internal static string PendingNotice;
        // 0.5.1: the key-conflict sentence of the load key (QuickLoad.OnGameLoaded) when this load also has a notice of
        // its own; the two are shown together, as Notice.Show does for the other messages of that key.
        internal static string PendingConflict;

        internal static bool AnythingPending
        {
            get { return PendingOpenShutter || PendingShowFrontArea || PendingSnapshot != null || PendingLegacyQueue != null || PendingNotice != null; }
        }

        internal static void OnGameLoaded()
        {
            SkipMorningReport = false;
            PendingOpenShutter = false;
            PendingShowFrontArea = false;
            PendingSnapshot = null;
            PendingLegacyQueue = null;
            PendingConflict = null;
            PendingNotice = Coexist.NoticePending ? Text.TookOver() : null;
            Coexist.NoticePending = false;
            if (Coexist.Failed)
                return;
            try
            {
                PlayerStore store = PlayerStore.HasInstance ? PlayerStore.Instance : null;
                StoreStation station = StoreStation.Instance;
                EmporiumEntry entry = EmporiumEntry.Instance;
                if (store == null || station == null || entry == null)
                    return;
                bool snapshotPresent = Snapshot.Present();
                if (!Legacy.Present())
                {
                    if (snapshotPresent)
                        Snapshot.ClearKeys();
                    return;
                }

                Legacy.Loaded legacy = Legacy.Apply(store, entry, station);
                Legacy.Clear();
                if (legacy == null)
                {
                    Snapshot.ClearKeys();
                    return;
                }
                MarkLoadedSave(store);
                if (legacy.FrontCounterRestored)
                    PendingShowFrontArea = true;
                if (store.storeState == PlayerStore.StoreState.OPEN)
                {
                    PendingOpenShutter = true;
                    PendingShowFrontArea = true;
                }
                SkipMorningReport = store.storeState != PlayerStore.StoreState.MORNING;

                Snapshot.Restored exact = null;
                if (snapshotPresent)
                {
                    string reason;
                    if (!Snapshot.BelongsToLoadedSave(store, station, out reason))
                        UnityEngine.Debug.LogWarning(F5SnapshotSaveMod.LogPrefix + "snapshot not used: " + reason);
                    else
                    {
                        // 0.8.0: an exception while reading the snapshot falls back to the F5ForceSave-format queue like a
                        // snapshot that cannot be rebuilt. Up to 0.7.1 it reached the catch below, which skipped that
                        // queue, so no waiting customer came back (plan 3.7).
                        List<string> errors;
                        try
                        {
                            exact = Snapshot.Read(out errors);
                        }
                        catch (Exception e)
                        {
                            exact = null;
                            errors = new List<string> { "reading the snapshot threw: " + e };
                        }
                        if (exact == null)
                        {
                            UnityEngine.Debug.LogWarning(F5SnapshotSaveMod.LogPrefix + "the snapshot could not be rebuilt; customers are re-created by identifier: "
                                + string.Join("; ", errors.GetRange(0, Math.Min(12, errors.Count)).ToArray()));
                            PendingNotice = Text.FellBack();
                        }
                    }
                    Snapshot.ClearKeys();
                }

                if (legacy.Queue != null && legacy.Queue.Count > 0)
                    PendingLegacyQueue = legacy.Queue;
                if (exact != null)
                {
                    PendingSnapshot = exact;
                    UnityEngine.Debug.Log(F5SnapshotSaveMod.LogPrefix + "snapshot read: day " + legacy.Day + " " + store.storeState
                        + ", waiting customers " + exact.Slots.Count + (exact.Slots.Count > exact.Queue.Count ? " (" + (exact.Slots.Count - exact.Queue.Count) + " created again by identifier when the store has started)" : "")
                        + ", feedback customers " + exact.Feedback.Count + (exact.Extras.Count > 0 ? ", extra data " + exact.Extras.Count : "")
                        + (exact.Counter != null ? ", customer at the counter '" + exact.Counter.GetClientBlueprint().identifier + "', conversation " + exact.DialogState : ""));
                }
                else if (PendingLegacyQueue != null)
                    UnityEngine.Debug.Log(F5SnapshotSaveMod.LogPrefix + "F5 data read: day " + legacy.Day + " " + store.storeState + ", "
                        + PendingLegacyQueue.Count + " customer(s) are re-created by identifier when the store has started");
                else if (!legacy.QueueStored && store.storeState == PlayerStore.StoreState.OPEN)
                    UnityEngine.Debug.LogWarning(F5SnapshotSaveMod.LogPrefix + "this F5 save was made while open by F5ForceSave before 1.2.0; the waiting customers were not stored");
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning(F5SnapshotSaveMod.LogPrefix + "reading the F5 data failed: " + e);
            }
        }

        // 0.7.0: the loaded save holds F5 data; its record in the save index gets the mark if it has none (QuickMark).
        private static void MarkLoadedSave(PlayerStore store)
        {
            try
            {
                NewGameData data = NewGameData.Instance;
                int slot = data != null ? data.saveSlot : store.saveSlotId;
                string file = data != null ? data.saveFile : null;
                if (QuickMark.EnsureMarked(slot, file))
                    UnityEngine.Debug.Log(F5SnapshotSaveMod.LogPrefix + "marked the loaded F5 save as an F5 save in the save index: slot " + slot + ", " + (string.IsNullOrEmpty(file) ? "the slot file" : file));
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning(F5SnapshotSaveMod.LogPrefix + "marking the loaded F5 save in the save index failed: " + e);
            }
        }
    }

    // Every save, F5 or the game's own, first moves the slot file into the history, as the game does. Up to 0.2.0 a
    // second F5 save on the same day overwrote the slot file instead, which replaced the game's save of that morning.
    // QuickHistory counts F5 saves and the game's saves apart when the history is trimmed.
    [HarmonyPatch(typeof(SaveFiles), nameof(SaveFiles.RotateIntoHistory))]
    internal static class RotateIntoHistoryPatch
    {
        private static void Prefix()
        {
            QuickHistory.BeginRotation(QuickSave.InProgress);
        }

        private static Exception Finalizer(Exception __exception)
        {
            QuickHistory.EndRotation();
            return __exception;
        }
    }

    [HarmonyPatch(typeof(SaveFiles), "TrimHistory")]
    internal static class TrimHistoryPatch
    {
        private static void Prefix(List<SaveHistoryEntry> history, ref int keepCount)
        {
            QuickHistory.Trim(history, ref keepCount);
        }
    }

    // 0.5.1: since the game 1.0.0.12 (buildid 26496118) the Discover button of the Load Game screen and the repair of an
    // unreadable save index register save files missing from the index (SaveFiles.ScanAndRegisterOrphans) and list a
    // slot's history files found in the save folder (SaveFiles.RestoreHistoryFromDisk), which trims that list with
    // TrimHistory as RotateIntoHistory does. There, too, F5 saves and the game's saves are kept apart.
    [HarmonyPatch(typeof(SaveFiles), "RestoreHistoryFromDisk")]
    internal static class RestoreHistoryFromDiskPatch
    {
        private static void Prefix(int slotId)
        {
            QuickHistory.BeginRestore(slotId);
        }

        private static Exception Finalizer(Exception __exception)
        {
            QuickHistory.EndRotation();
            return __exception;
        }
    }

    // The Load Game screen: " | F5" or " | Auto" after the day and money of each save, on the slot plates (the newest
    // save of the slot) and on the rows of the expanded history.
    [HarmonyPatch(typeof(LoadGameUI), "Bind")]
    internal static class LoadPlatePatch
    {
        private static void Prefix(int index, List<int> ___entries)
        {
            QuickHistory.BeginPlate(index, ___entries);
        }

        private static Exception Finalizer(Exception __exception)
        {
            QuickHistory.EndLabels();
            return __exception;
        }
    }

    [HarmonyPatch(typeof(LoadGameUI), "BuildExpanded")]
    internal static class LoadRowsPatch
    {
        private static void Prefix(List<SaveHistoryEntry> history)
        {
            QuickHistory.BeginRows(history);
        }

        private static Exception Finalizer(Exception __exception)
        {
            QuickHistory.EndLabels();
            return __exception;
        }
    }

    [HarmonyPatch(typeof(LoadGameUI), "DayAndMoney")]
    internal static class DayAndMoneyPatch
    {
        private static void Postfix(ref string __result)
        {
            __result = QuickHistory.Label(__result);
        }
    }

    // SaveGame logs and swallows write errors, so success is taken from WriteSlotFile completing. A save that is not
    // an F5 save of this mod drops snapshot keys that may have been left in modData.
    [HarmonyPatch(typeof(PlayerStore), "WriteSlotFile")]
    internal static class WriteSlotFilePatch
    {
        private static void Prefix()
        {
            if (!QuickSave.InProgress && Snapshot.Present())
                Snapshot.ClearKeys();
        }

        private static void Postfix()
        {
            if (QuickSave.InProgress)
                QuickSave.SlotWritten = true;
        }
    }

    // PlayerStore.LoadGame decodes the display case items, and each one goes through AddedItemToDisplayCase, which
    // calls ShowDisplayCaseTimingTipOnce; that panel ("items added after opening take effect the next day") would
    // appear after loading a save made while open. The tip is skipped while LoadGame runs.
    [HarmonyPatch(typeof(PlayerStore), nameof(PlayerStore.LoadGame))]
    internal static class LoadGamePatch
    {
        internal static bool Loading;

        private static void Prefix()
        {
            Loading = true;
        }

        private static Exception Finalizer(Exception __exception)
        {
            Loading = false;
            return __exception;
        }
    }

    [HarmonyPatch(typeof(PlayerStore), "ShowDisplayCaseTimingTipOnce")]
    internal static class DisplayCaseTipPatch
    {
        private static bool Prefix()
        {
            return !LoadGamePatch.Loading;
        }
    }

    // EmporiumEntry.Start shows the morning report after loading when the save still holds night logs, and the report
    // turns a CLOSED store into MORNING. An F5 save made while open or after closing is not a morning. The skip
    // covers only that call during the load: it is dropped when EmporiumEntry.Start ends.
    [HarmonyPatch(typeof(StartOfDayUIManager), nameof(StartOfDayUIManager.ShowMorningReport))]
    internal static class ShowMorningReportPatch
    {
        private static bool Prefix()
        {
            if (!Restore.SkipMorningReport)
                return true;
            Restore.SkipMorningReport = false;
            UnityEngine.Debug.Log(F5SnapshotSaveMod.LogPrefix + "skipped the morning report for an F5 save made during the day");
            return false;
        }
    }

    [HarmonyPatch(typeof(EmporiumEntry), "Start")]
    internal static class EmporiumStartPatch
    {
        private static Exception Finalizer(Exception __exception)
        {
            Restore.SkipMorningReport = false;
            if (__exception == null) MorningReport.RestoreDisplay();
            return __exception;
        }
    }

    [HarmonyPatch(typeof(MetaHelper), nameof(MetaHelper.WaitAndDo))]
    internal static class WaitAndDoPatch
    {
        private static void Prefix(float duration)
        {
            Timers.Note(duration);
        }
    }

    // On the first shutter update after an F5 save is loaded (the store scene has finished starting): the waiting
    // customers are put back in front of any added after loading, then, for a save made while open, the door is
    // opened with the game's own animation and the front area shown (the opening logic of the day is not run again),
    // and the customer at the counter is put back. While the door animation runs, the shutter button stays locked
    // (PlayerStore.lockShutterButtonFlag); when it ends, the button is unlocked and blockerMaskObject (the image over
    // the shop window that otherwise takes the clicks on the vending machines) is deactivated, as
    // StoreShutterButton.OpenShutter does.
    [HarmonyPatch(typeof(StoreShutterButton), "Update")]
    internal static class ShutterUpdatePatch
    {
        private static void Postfix(StoreShutterButton __instance, bool ___isClosed, float ___duration)
        {
            if (!Restore.AnythingPending)
                return;
            Snapshot.Restored exact = Restore.PendingSnapshot;
            List<CustomerQueue.Entry> legacyQueue = Restore.PendingLegacyQueue;
            string notice = Restore.PendingNotice;
            string conflict = Restore.PendingConflict;
            Restore.PendingSnapshot = null;
            Restore.PendingLegacyQueue = null;
            Restore.PendingNotice = null;
            Restore.PendingConflict = null;
            PlayerStore store = PlayerStore.Instance;

            // 0.8.0, in this order (plan 3.6 and 3.9, spec 4.4): the extra data of the snapshot goes back to the mods that
            // wrote it; then the rebuilt waiting customers are put back, the door is opened and the customer at the counter
            // is put back; then the customers that were left out of the snapshot are created and put in their places in the
            // line, so that their factories see the restored customers in the store. When the extra data cannot be given
            // back, the snapshot is not used and the customers are created once from the F5ForceSave-format queue. Every
            // customer factory runs once per load.
            if (exact != null)
            {
                string problem = null;
                bool given;
                try
                {
                    given = Snapshot.GiveExtraBack(exact, out problem);
                }
                catch (Exception e)
                {
                    given = false;
                    problem = e.ToString();
                }
                if (!given)
                {
                    UnityEngine.Debug.LogWarning(F5SnapshotSaveMod.LogPrefix + "giving the extra data back failed; the snapshot is not used and the customers are re-created by identifier: " + problem);
                    exact = null;
                    notice = Text.FellBack();
                }
            }
            List<StoreClient> queue = null;
            List<StoreClient> feedback = null;
            Snapshot.Restored counter = null;
            if (exact != null)
            {
                queue = Snapshot.RebuiltWaiting(exact);
                feedback = exact.Feedback.Count > 0 ? exact.Feedback : null;
                counter = exact.Counter != null ? exact : null;
            }
            else if (legacyQueue != null && legacyQueue.Count > 0)
            {
                int failed;
                queue = CustomerQueue.CreateAll(legacyQueue, out failed);
                UnityEngine.Debug.Log(F5SnapshotSaveMod.LogPrefix + "customers re-created by identifier: " + queue.Count + (failed > 0 ? ", " + failed + " could not be created" : ""));
            }
            try
            {
                StoreClientManager manager = store.storeClientManager;
                if (queue != null && manager != null)
                {
                    if (manager.clientStack == null)
                        manager.clientStack = new List<StoreClient>();
                    manager.clientStack.InsertRange(0, queue);
                    var names = new List<string>();
                    foreach (StoreClient c in queue)
                        names.Add(c.identifier);
                    UnityEngine.Debug.Log(F5SnapshotSaveMod.LogPrefix + "put back " + queue.Count + " waiting customer(s): " + string.Join(", ", names.ToArray()));
                }
                if (feedback != null && manager != null)
                {
                    if (manager.chemicalFeedbackStack == null)
                        manager.chemicalFeedbackStack = new List<StoreClient>();
                    manager.chemicalFeedbackStack.InsertRange(0, feedback);
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning(F5SnapshotSaveMod.LogPrefix + "putting the waiting customers back failed: " + e);
            }
            try
            {
                if (Restore.PendingOpenShutter && ___isClosed)
                    OpenDoor(__instance, ___duration);
                if (Restore.PendingShowFrontArea && EmporiumEntry.Instance != null)
                    EmporiumEntry.Instance.ShowFrontArea();
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning(F5SnapshotSaveMod.LogPrefix + "restoring the open shop view failed: " + e);
            }
            finally
            {
                Restore.PendingOpenShutter = false;
                Restore.PendingShowFrontArea = false;
            }
            if (counter != null)
            {
                try
                {
                    Snapshot.ApplyCounter(counter);
                    UnityEngine.Debug.Log(F5SnapshotSaveMod.LogPrefix + "customer at the counter put back: '" + counter.Counter.GetClientBlueprint().identifier
                        + "', conversation " + counter.DialogState + (counter.Dialog != null ? " (" + Short(counter.Dialog.plainText) + ")" : ""));
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogWarning(F5SnapshotSaveMod.LogPrefix + "putting the customer at the counter back failed: " + e);
                }
            }
            if (exact != null)
            {
                try
                {
                    StoreClientManager manager = store.storeClientManager;
                    if (manager != null)
                    {
                        if (manager.clientStack == null)
                            manager.clientStack = new List<StoreClient>();
                        int created = Snapshot.CreateLeftOut(exact, manager.clientStack);
                        if (created > 0)
                        {
                            var names = new List<string>();
                            foreach (StoreClient c in manager.clientStack)
                                names.Add(c != null ? c.identifier : "null");
                            UnityEngine.Debug.Log(F5SnapshotSaveMod.LogPrefix + "waiting line with the customers created again: " + string.Join(", ", names.ToArray()));
                        }
                    }
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogWarning(F5SnapshotSaveMod.LogPrefix + "creating the customers left out of the snapshot failed: " + e);
                }
            }
            if (StoreUIManager.Instance != null && (notice != null || conflict != null))
            {
                // A load-key conflict sentence waited for this update because the snapshot could still be dropped here
                // (QuickLoad.OnGameLoaded); without a notice of its own it is shown alone.
                string shown = notice ?? conflict;
                if (notice != null && conflict != null)
                    shown = Notice.Show(StoreUIManager.Instance, notice, conflict);
                else
                    StoreUIManager.Instance.Notify(shown, "default");
                UnityEngine.Debug.Log(F5SnapshotSaveMod.LogPrefix + "notice: " + shown);
            }
        }

        private static string Short(string s)
        {
            if (string.IsNullOrEmpty(s))
                return "";
            return s.Length > 40 ? s.Substring(0, 40) + "…" : s;
        }

        private static void OpenDoor(StoreShutterButton shutter, float duration)
        {
            PlayerStore store = PlayerStore.Instance;
            if (store != null)
                store.lockShutterButtonFlag++;
            shutter.ToggleShutterDoor();
            ModHelper.StartCoroutine(F5SnapshotSaveMod.ModId, MetaHelper.WaitAndDo(duration, () => FinishOpening(shutter)));
        }

        // The steps of the callback that StoreShutterButton.OpenShutter schedules after the door animation.
        private static void FinishOpening(StoreShutterButton shutter)
        {
            PlayerStore store = PlayerStore.Instance;
            if (store != null)
                store.lockShutterButtonFlag--;
            if (shutter != null && shutter.blockerMaskObject != null)
                shutter.blockerMaskObject.gameObject.SetActive(false);
            UnityEngine.Debug.Log(F5SnapshotSaveMod.LogPrefix + "door animation finished; shutter button unlocked and the shop window cleared for clicks");
        }
    }
}
