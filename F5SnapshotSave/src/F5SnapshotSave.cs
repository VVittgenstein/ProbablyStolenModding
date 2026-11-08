using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;

[assembly: System.Reflection.AssemblyTitle("F5SnapshotSave")]
[assembly: System.Reflection.AssemblyVersion("0.5.1.0")]

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
            bool save = UnityEngine.Input.GetKeyDown(SaveKey.Current);
            bool load = !save && UnityEngine.Input.GetKeyDown(LoadKey.Current);
            bool loadF5 = !save && !load && UnityEngine.Input.GetKeyDown(F5LoadKey.Current);
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

            bool withCounter;
            string dialogState;
            string refusal = Refusal(store, ui, entry, out withCounter, out dialogState);
            if (refusal != null)
            {
                Report(ui, refusal, false);
                return;
            }

            string error;
            Snapshot.Captured cap = Snapshot.Capture(store, entry, withCounter, dialogState, out error);
            if (cap == null)
            {
                UnityEngine.Debug.LogWarning(F5SnapshotSaveMod.LogPrefix + "snapshot not possible: " + error);
                Report(ui, withCounter ? Text.CounterNotStorable(SaveUIManager.GetPreview(store.saveSlotId)) : Text.QueueNotStorable(SaveUIManager.GetPreview(store.saveSlotId)), false);
                return;
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
                + (withCounter ? ", customer at the counter '" + store.currentClientInstance.GetClientBlueprint().identifier + "', conversation " + cap.DialogState : ""));
            if (SlotWritten)
            {
                QuickHistory.RecordLatest(store.saveSlotId);
                Report(ui, Text.Saved(station.dayCounter, store.storeState, DateTime.Now, withCounter, cap.Waiting), true);
            }
            else
                Report(ui, Text.Failed(), false);
        }

        private static string Refusal(PlayerStore store, StoreUIManager ui, EmporiumEntry entry, out bool withCounter, out string dialogState)
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
                return Text.CounterNotStorable(SaveUIManager.GetPreview(store.saveSlotId));
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
        internal static List<StoreClient> PendingQueue;
        internal static List<StoreClient> PendingFeedback;
        internal static Snapshot.Restored PendingCounter;
        internal static string PendingNotice;
        // 0.5.1: the key-conflict sentence of the load key (QuickLoad.OnGameLoaded) when this load also has a notice of
        // its own; the two are shown together, as Notice.Show does for the other messages of that key.
        internal static string PendingConflict;

        internal static bool AnythingPending
        {
            get { return PendingOpenShutter || PendingShowFrontArea || PendingQueue != null || PendingFeedback != null || PendingCounter != null || PendingNotice != null; }
        }

        internal static void OnGameLoaded()
        {
            SkipMorningReport = false;
            PendingOpenShutter = false;
            PendingShowFrontArea = false;
            PendingQueue = null;
            PendingFeedback = null;
            PendingCounter = null;
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
                        List<string> errors;
                        exact = Snapshot.Read(out errors);
                        if (exact == null)
                        {
                            UnityEngine.Debug.LogWarning(F5SnapshotSaveMod.LogPrefix + "the snapshot could not be rebuilt; customers are re-created by identifier: "
                                + string.Join("; ", errors.GetRange(0, Math.Min(12, errors.Count)).ToArray()));
                            PendingNotice = Text.FellBack();
                        }
                    }
                    Snapshot.ClearKeys();
                }

                if (exact != null)
                {
                    PendingQueue = exact.Queue;
                    PendingFeedback = exact.Feedback.Count > 0 ? exact.Feedback : null;
                    PendingCounter = exact.Counter != null ? exact : null;
                    UnityEngine.Debug.Log(F5SnapshotSaveMod.LogPrefix + "snapshot read: day " + legacy.Day + " " + store.storeState
                        + ", waiting customers " + exact.Queue.Count + ", feedback customers " + exact.Feedback.Count
                        + (exact.Counter != null ? ", customer at the counter '" + exact.Counter.GetClientBlueprint().identifier + "', conversation " + exact.DialogState : ""));
                }
                else if (legacy.Queue != null && legacy.Queue.Count > 0)
                {
                    int failed;
                    PendingQueue = CustomerQueue.CreateAll(legacy.Queue, out failed);
                    UnityEngine.Debug.Log(F5SnapshotSaveMod.LogPrefix + "F5 data read: day " + legacy.Day + " " + store.storeState + ", customers re-created by identifier: "
                        + PendingQueue.Count + (failed > 0 ? ", " + failed + " could not be created" : ""));
                }
                else if (!legacy.QueueStored && store.storeState == PlayerStore.StoreState.OPEN)
                    UnityEngine.Debug.LogWarning(F5SnapshotSaveMod.LogPrefix + "this F5 save was made while open by F5ForceSave before 1.2.0; the waiting customers were not stored");
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning(F5SnapshotSaveMod.LogPrefix + "reading the F5 data failed: " + e);
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
            List<StoreClient> queue = Restore.PendingQueue;
            List<StoreClient> feedback = Restore.PendingFeedback;
            Snapshot.Restored counter = Restore.PendingCounter;
            string notice = Restore.PendingNotice;
            string conflict = Restore.PendingConflict;
            Restore.PendingQueue = null;
            Restore.PendingFeedback = null;
            Restore.PendingCounter = null;
            Restore.PendingNotice = null;
            Restore.PendingConflict = null;
            PlayerStore store = PlayerStore.Instance;
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
            if (notice != null && StoreUIManager.Instance != null)
            {
                string shown = notice;
                if (conflict != null)
                    shown = Notice.Show(StoreUIManager.Instance, notice, conflict);
                else
                    StoreUIManager.Instance.Notify(notice, "default");
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
            shutter.StartCoroutine(MetaHelper.WaitAndDo(duration, () => FinishOpening(shutter)));
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
