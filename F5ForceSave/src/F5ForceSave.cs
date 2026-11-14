using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;

[assembly: System.Reflection.AssemblyTitle("F5ForceSave")]
[assembly: System.Reflection.AssemblyVersion("1.8.1.0")]

namespace F5ForceSave
{
    // Entry point found by the game's ModLoader, which loads mods once per launch and applies the
    // Harmony patches in this assembly (Harmony ID "mod.<manifest ID>") before calling Init and OnEnable.
    public sealed class F5ForceSaveMod : IMod
    {
        internal const string ModId = "F5ForceSave";
        internal const string LogPrefix = "[F5ForceSave] ";
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
            ModHook.OnGameLoadedLate -= SaveExtras.OnGameLoaded;
            ModHook.OnGameLoadedLate += SaveExtras.OnGameLoaded;
            ModHook.OnGameLoadedLate -= QuickLoad.OnGameLoaded;
            ModHook.OnGameLoadedLate += QuickLoad.OnGameLoaded;
            if (host != null)
                return;
            host = new UnityEngine.GameObject("F5ForceSave");
            UnityEngine.Object.DontDestroyOnLoad(host);
            host.AddComponent<F5Listener>();
        }

        public void OnDisable()
        {
            ModHook.OnGameLoadedLate -= SaveExtras.OnGameLoaded;
            ModHook.OnGameLoadedLate -= QuickLoad.OnGameLoaded;
            if (host == null)
                return;
            UnityEngine.Object.Destroy(host);
            host = null;
        }
    }

    // F5 Quick Save Plus (F5SnapshotSave) finds this component by its type name and switches it off when both mods
    // are enabled; it then handles the save key and the load key itself. The rows of both keys in the game options
    // exist only while this component is on, so they are not shown while the keys would have no effect.
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
                UnityEngine.Debug.LogWarning(F5ForceSaveMod.LogPrefix + "adding the save key to the game options failed; it can still be set in the mod settings: " + e);
            }
            try
            {
                LoadKey.AddOption();
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning(F5ForceSaveMod.LogPrefix + "adding the load key to the game options failed; it can still be set in the mod settings: " + e);
            }
            try
            {
                F5LoadKey.AddOption();
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning(F5ForceSaveMod.LogPrefix + "adding the F5 save load key to the game options failed; it can still be set in the mod settings: " + e);
            }
        }

        private void OnDisable()
        {
            try
            {
                SaveKey.RemoveOption();
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning(F5ForceSaveMod.LogPrefix + "removing the save key from the game options failed: " + e);
            }
            try
            {
                LoadKey.RemoveOption();
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning(F5ForceSaveMod.LogPrefix + "removing the load key from the game options failed: " + e);
            }
            try
            {
                F5LoadKey.RemoveOption();
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning(F5ForceSaveMod.LogPrefix + "removing the F5 save load key from the game options failed: " + e);
            }
        }

        private void Update()
        {
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
                UnityEngine.Debug.LogWarning(F5ForceSaveMod.LogPrefix + (save ? "save" : "load") + " key handling failed: " + e);
            }
        }
    }

    // The save key, F5 unless the player changes it. The value is an entry of the game's mod configuration
    // (ModHelper.GetConfig), which the game keeps in <persistentDataPath>\ModConfigs\F5ForceSave.json as the
    // UnityEngine.KeyCode name under "save_key". Two parts of the game's own UI change it: a row that this mod adds
    // to the Key Binds section of the game options, where the game waits for the next key press and refuses a key
    // that another row uses, and this mod's settings window in the Mods panel, a list of the accepted names. The
    // file can also be edited while the game is closed; the game replaces a name that is not in the list with F5
    // when it reads the file.
    internal static class SaveKey
    {
        internal const string ConfigKey = "save_key";
        internal const string OptionId = "save_key";
        private const string Section = "按键 / Key / Клавиши";
        private static ConfigEntry<string> entry;

        internal static UnityEngine.KeyCode Current = UnityEngine.KeyCode.F5;
        // 1.8.0: the modifier of a key combination (KeyModifier.cs).
        internal static readonly ModifierSetting Modifier = new ModifierSetting("save_key_modifier", Text.SaveModifierSettingLabel, Text.SaveModifierOptionLabel);

        internal static void Load()
        {
            ModConfig config = ModHelper.GetConfig(F5ForceSaveMod.ModId);
            entry = config.Define(ConfigKey, UnityEngine.KeyCode.F5.ToString(), Section, Text.KeySettingLabel, Choices(), Text.KeySettingTooltip);
            Apply(entry.Value);
            entry.OnChanged += name =>
            {
                Apply(name);
                UnityEngine.Debug.Log(F5ForceSaveMod.LogPrefix + "save key set to " + Current);
            };
            // Written at every start, so the file exists and shows the key in effect for players who edit it by hand.
            config.Save();
            UnityEngine.Debug.Log(F5ForceSaveMod.LogPrefix + "save key " + Current + " (" + config.FilePath + ", entry " + ConfigKey + ")");
            Modifier.Load();
        }

        private static void Apply(string name)
        {
            UnityEngine.KeyCode key;
            if (TryParse(name, out key))
            {
                Current = key;
                return;
            }
            Current = UnityEngine.KeyCode.F5;
            UnityEngine.Debug.LogWarning(F5ForceSaveMod.LogPrefix + "'" + name + "' is not a key this mod accepts; the save key is F5");
        }

        internal static bool TryParse(string name, out UnityEngine.KeyCode key)
        {
            key = UnityEngine.KeyCode.None;
            UnityEngine.KeyCode parsed;
            if (string.IsNullOrEmpty(name) || !Enum.TryParse(name, false, out parsed)
                || !Enum.IsDefined(typeof(UnityEngine.KeyCode), parsed) || !Bindable(parsed))
                return false;
            key = parsed;
            return true;
        }

        // The keys taken from the game's key capture (KeyBindHelper.CaptureKeys): every keyboard key (above None and below
        // Mouse0) except Escape, which cancels the capture, and the mouse's side buttons Mouse3 to Mouse6, which the capture
        // also returns since the game 1.0.0.12 and which the game's rows show as "Mouse 4" to "Mouse 7". The left button is
        // reserved by the game and its own rows use the right button and the wheel button; those three are not taken.
        internal static bool Bindable(UnityEngine.KeyCode key)
        {
            if (key >= UnityEngine.KeyCode.Mouse3 && key <= UnityEngine.KeyCode.Mouse6)
                return true;
            return key > UnityEngine.KeyCode.None && key < UnityEngine.KeyCode.Mouse0 && key != UnityEngine.KeyCode.Escape;
        }

        // In KeyCode order. Names that share a value (RightMeta, RightCommand and RightApple, for example) appear
        // once, as KeyCode.ToString gives them; the row in the game options stores the same text.
        internal static string[] Choices()
        {
            var names = new List<string>();
            foreach (UnityEngine.KeyCode key in Enum.GetValues(typeof(UnityEngine.KeyCode)))
            {
                string name = key.ToString();
                if (Bindable(key) && !names.Contains(name))
                    names.Add(name);
            }
            return names.ToArray();
        }

        private static void Set(UnityEngine.KeyCode key)
        {
            if (!Bindable(key))
                return;
            if (entry != null)
                entry.Set(key.ToString()); // OnChanged applies it; the game writes the file
            else
                Current = key;
        }

        // The game's name for the key, as the Key Binds rows show it (F5, Left Shift, Numpad 0), after the modifier
        // (Ctrl+F5).
        internal static string Name()
        {
            return Modifier.Name(Current);
        }

        internal static bool Pressed()
        {
            return Modifier.Pressed(Current);
        }

        // The game's own options are registered first (GameOptionRegistry.EnsureVanilla, otherwise run when the
        // options window first opens), so the Key Binds section stays after the other sections and this row comes
        // after the game's own key rows.
        internal static void AddOption()
        {
            GameOptionRegistry.EnsureVanilla();
            ModHelper.AddGameOptionKeyBind(F5ForceSaveMod.ModId, GameOptionRegistry.SECTION_KEYBIND, OptionId, Text.KeyOptionLabel, () => Current, Set);
            Modifier.AddOption();
        }

        internal static void RemoveOption()
        {
            ModHelper.RemoveGameOption(F5ForceSaveMod.ModId, OptionId);
            Modifier.RemoveOption();
        }
    }

    // The game itself saves only at the start of a day (PlayerStore.SaveGame after the end-of-day sequence and
    // after a new game). F5 calls the same SaveGame in the morning, while the shop is open and no customer is
    // at the counter, and after closing. What SaveGame leaves out at those times is stored by SaveExtras in
    // PlayerStore.modData, which is written into the same slot file, and put back after loading.
    // StoreClientManager.chemicalFeedbackStack (customers queued for the next day after chemical sales) is
    // ES3NonSerializable as well and is not stored; the game's own saves lose it in the same way.
    // The save before sleeping (SleepSave, T-031) is the same save, made when "End the Day" is clicked.
    internal static class QuickSave
    {
        internal static bool InProgress;
        internal static bool SlotWritten;

        internal static void TrySave()
        {
            PlayerStore store = PlayerStore.Instance;
            StoreUIManager ui = StoreUIManager.Instance;
            StoreStation station = StoreStation.Instance;
            EmporiumEntry entry = EmporiumEntry.Instance;
            if (store == null || ui == null || station == null || entry == null)
                return;
            if (StoreUIManager.IsAnyTyping())
                return;

            bool saved;
            string message = Save(store, station, entry, false, out saved);
            Report(ui, message, saved);
        }

        // T-031: the save before sleeping (SleepSave), with the same checks and the same save as the key. The message is
        // returned for SleepSave to show on the next morning; null when the store is not there. No key was pressed, so a
        // text field with the focus does not stop it, and a refusal leaves out the sentence about the save a quit would
        // return to: the game saves the next morning right after.
        internal static string SaveBeforeSleep(out bool saved)
        {
            saved = false;
            PlayerStore store = PlayerStore.HasInstance ? PlayerStore.Instance : null;
            StoreStation station = StoreStation.Instance;
            EmporiumEntry entry = EmporiumEntry.Instance;
            if (store == null || station == null || entry == null || StoreUIManager.Instance == null)
                return null;
            return Save(store, station, entry, true, out saved);
        }

        private static string Save(PlayerStore store, StoreStation station, EmporiumEntry entry, bool beforeSleep, out bool saved)
        {
            saved = false;
            string refusal = Refusal(store, entry, !beforeSleep);
            if (refusal != null)
                return beforeSleep ? Text.SleepNotSaved(refusal) : refusal;

            InProgress = true;
            SlotWritten = false;
            try
            {
                SaveExtras.Write(store, entry, station);
                store.SaveGame();
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning(F5ForceSaveMod.LogPrefix + "SaveGame threw: " + e);
            }
            finally
            {
                InProgress = false;
                SaveExtras.Clear();
            }

            if (!SlotWritten)
                return beforeSleep ? Text.SleepFailed() : Text.Failed();
            saved = true;
            int waiting = CustomerQueue.Count(store.storeClientManager);
            QuickHistory.RecordLatest(store.saveSlotId, SaveDetails.Make(store, station, waiting));
            return beforeSleep
                ? Text.SleepSaved(station.dayCounter, store.storeState, DateTime.Now)
                : Text.Saved(station.dayCounter, store.storeState, DateTime.Now, waiting);
        }

        // withReturnPoint: the refusals that name the save a quit would return to (the newest save of the slot) do so.
        private static string Refusal(PlayerStore store, EmporiumEntry entry, bool withReturnPoint)
        {
            if (ModHelper.IsOutsideStore())
                return Text.OutsideStore();

            GameOverUIManager gameOver = GameOverUIManager.Instance;
            if (gameOver != null && gameOver.uiPanel != null && gameOver.uiPanel.activeSelf)
                return Text.GameOver();

            ItemMouseDragHandler drag = ItemMouseDragHandler.current;
            ItemMultiSelectHandler groupDrag = ItemMultiSelectHandler.current;
            if ((drag != null && drag.IsDraggingItem) || (groupDrag != null && groupDrag.IsGroupDragging))
                return Text.Dragging();

            // The customer being served is not part of the save (ES3NonSerializable in PlayerStore). IsClientPresent
            // is the game's own check (currentClientInstance, cleared by DismissCurrentClient); isClientArrived is not
            // used because the game leaves it true after the customer has gone.
            if (store.IsClientPresent() || store.isClientBeingArrested || store.isHandlingShootout)
                return Text.CustomerPresent(Last(store, withReturnPoint));

            if (HasItems(entry.backInvinvElementCounter) || HasItems(entry.afterhourInventory)
                || HasItems(entry.responseInventory) || HasItems(entry.responseInventoryClosable)
                || HasItems(entry.swapBufferElement))
                return Text.TemporaryNotEmpty();

            List<int> notStorable = CustomerQueue.PositionsNotStorable(store.storeClientManager);
            if (notStorable.Count > 0)
                return Text.QueueNotStorable(notStorable, Last(store, withReturnPoint));

            return null;
        }

        private static SavePreviewData Last(PlayerStore store, bool wanted)
        {
            return wanted ? SaveUIManager.GetPreview(store.saveSlotId) : null;
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
            UnityEngine.Debug.Log(F5ForceSaveMod.LogPrefix + (saved ? "saved: " : "not saved: ") + message);
        }
    }

    // State that PlayerStore.SaveGame does not write but an F5 save needs: the items on the front counter,
    // the customers still waiting today, flags of the current day that PlayerStore marks ES3NonSerializable,
    // and the store state the save was made in. The values go into PlayerStore.modData only for the duration
    // of an F5 save.
    internal static class SaveExtras
    {
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

        // Set after loading an F5 save, read by the patches below.
        internal static bool SkipMorningReport;
        internal static bool PendingOpenShutter;
        internal static bool PendingShowFrontArea;
        internal static List<CustomerQueue.Entry> PendingQueue;

        internal static void Write(PlayerStore store, EmporiumEntry entry, StoreStation station)
        {
            Set(DayKey, station.dayCounter.ToString(CultureInfo.InvariantCulture));
            Set(StateKey, ((int)store.storeState).ToString(CultureInfo.InvariantCulture));
            Set(FrontCounterKey, QuickSave.HasItems(entry.frontInvinvElement)
                ? store.EncodeSaveItem("save_bag", entry.frontInvinvElement, entry.frontInvWindow)
                : "");
            Set(QueueKey, CustomerQueue.Encode(store.storeClientManager));
            Set(ReportFiledKey, store.correctReportFileToday ? "1" : "0");
            Set(WantedVisitedKey, store.wantedPersonVisitedToday ? "1" : "0");
            Set(WantedNameKey, store.wantedPersonVisitedName ?? "");
            Set(WantedIdKey, store.wantedPersonVisitedId ?? "");
            Set(CallNextKey, store.callNextWhenPossible ? "1" : "0");
        }

        internal static void Clear()
        {
            foreach (string key in Keys)
                ModHelper.RemoveModData(F5ForceSaveMod.ModId, key);
        }

        // ModHook.OnGameLoadedLate: PlayerStore and the store inventories have just been read from the slot file.
        internal static void OnGameLoaded()
        {
            SkipMorningReport = false;
            PendingOpenShutter = false;
            PendingShowFrontArea = false;
            PendingQueue = null;
            try
            {
                PlayerStore store = PlayerStore.Instance;
                StoreStation station = StoreStation.Instance;
                EmporiumEntry entry = EmporiumEntry.Instance;
                if (store == null || station == null || entry == null || !ModHelper.HasModData(F5ForceSaveMod.ModId, DayKey))
                    return;

                int day = ParseInt(Get(DayKey), -1);
                int state = ParseInt(Get(StateKey), -1);
                if (day != station.dayCounter || state != (int)store.storeState)
                {
                    UnityEngine.Debug.LogWarning(F5ForceSaveMod.LogPrefix + "extra save data for day " + day + " state " + state
                        + " does not match the loaded day " + station.dayCounter + " state " + (int)store.storeState + "; not applied");
                    Clear();
                    return;
                }

                store.correctReportFileToday = Get(ReportFiledKey) == "1";
                store.wantedPersonVisitedToday = Get(WantedVisitedKey) == "1";
                store.wantedPersonVisitedName = Get(WantedNameKey);
                store.wantedPersonVisitedId = Get(WantedIdKey);
                store.callNextWhenPossible = Get(CallNextKey) == "1";

                string frontCounter = Get(FrontCounterKey);
                if (!string.IsNullOrEmpty(frontCounter))
                {
                    store.DecodeSaveItem(frontCounter, entry.frontInvinvElement);
                    PendingShowFrontArea = true;
                }

                if (store.storeState == PlayerStore.StoreState.OPEN)
                {
                    PendingOpenShutter = true;
                    PendingShowFrontArea = true;
                }
                SkipMorningReport = store.storeState != PlayerStore.StoreState.MORNING;

                // Saves made before 1.2.0 have no queue entry; their waiting customers were not stored.
                string queue = ModHelper.HasModData(F5ForceSaveMod.ModId, QueueKey) ? Get(QueueKey) : null;
                List<CustomerQueue.Entry> waiting = queue != null ? CustomerQueue.Decode(queue) : null;
                if (waiting != null && waiting.Count > 0)
                    PendingQueue = waiting;
                if (queue == null && store.storeState == PlayerStore.StoreState.OPEN)
                    UnityEngine.Debug.LogWarning(F5ForceSaveMod.LogPrefix + "this F5 save was made while open by a version before 1.2.0; the customers waiting at that time were not stored");

                MarkLoadedSave(store);
                UnityEngine.Debug.Log(F5ForceSaveMod.LogPrefix + "restored an F5 save of day " + day + " in state " + store.storeState
                    + "; front counter items: " + (string.IsNullOrEmpty(frontCounter) ? "none" : entry.frontInvinvElement.childItems.Count.ToString(CultureInfo.InvariantCulture))
                    + "; waiting customers: " + (waiting != null ? waiting.Count.ToString(CultureInfo.InvariantCulture) : "not stored"));
                Clear();
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning(F5ForceSaveMod.LogPrefix + "restoring the F5 save data failed: " + e);
            }
        }

        // 1.8.0: the loaded save holds F5 data; its record in the save index gets the mark if it has none (QuickMark).
        private static void MarkLoadedSave(PlayerStore store)
        {
            try
            {
                NewGameData data = NewGameData.Instance;
                int slot = data != null ? data.saveSlot : store.saveSlotId;
                string file = data != null ? data.saveFile : null;
                if (QuickMark.EnsureMarked(slot, file))
                    UnityEngine.Debug.Log(F5ForceSaveMod.LogPrefix + "marked the loaded F5 save as an F5 save in the save index: slot " + slot + ", " + (string.IsNullOrEmpty(file) ? "the slot file" : file));
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning(F5ForceSaveMod.LogPrefix + "marking the loaded F5 save in the save index failed: " + e);
            }
        }

        private static void Set(string key, string value)
        {
            ModHelper.SetModData(F5ForceSaveMod.ModId, key, value);
        }

        private static string Get(string key)
        {
            return ModHelper.GetModData(F5ForceSaveMod.ModId, key, "");
        }

        private static int ParseInt(string text, int fallback)
        {
            int value;
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : fallback;
        }
    }

    // Every save, F5 or the game's own, first moves the slot file into the history, as the game does. Up to 1.3.0 a
    // second F5 save on the same day overwrote the slot file instead, which replaced the game's save of that
    // morning. QuickHistory counts F5 saves and the game's saves apart when the history is trimmed.
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

    // 1.6.1: since the game 1.0.0.12 (buildid 26496118) the Discover button of the Load Game screen and the repair of an
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

    // SaveGame logs and swallows write errors, so success is taken from WriteSlotFile completing.
    [HarmonyPatch(typeof(PlayerStore), "WriteSlotFile")]
    internal static class WriteSlotFilePatch
    {
        private static void Postfix()
        {
            if (QuickSave.InProgress)
                QuickSave.SlotWritten = true;
        }
    }

    // PlayerStore.LoadGame (called from EmporiumEntry.Start) decodes the display case items, and each one goes
    // through PlayerStore.AddedItemToDisplayCase, which calls ShowDisplayCaseTimingTipOnce. That method opens the
    // "items added after opening take effect the next day" panel when storeState is OPEN and the tip has not been
    // shown in this run (displayCaseTimingTipShown, which is saved). The game's own saves are made in the morning;
    // after loading an F5 save made while open, the panel appeared without the player adding anything. The tip is
    // skipped while LoadGame runs and displayCaseTimingTipShown is left as it is.
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

    // EmporiumEntry.Start shows the morning report after loading when the save still holds night logs, and the
    // report turns a CLOSED store into MORNING. An F5 save made while open or after closing is not a morning.
    // The skip covers only that call during the load (see EmporiumStartPatch).
    [HarmonyPatch(typeof(StartOfDayUIManager), nameof(StartOfDayUIManager.ShowMorningReport))]
    internal static class ShowMorningReportPatch
    {
        private static bool Prefix()
        {
            if (!SaveExtras.SkipMorningReport)
                return true;
            SaveExtras.SkipMorningReport = false;
            UnityEngine.Debug.Log(F5ForceSaveMod.LogPrefix + "skipped the morning report for an F5 save made during the day");
            return false;
        }
    }

    // EmporiumEntry.Start loads the save (PlayerStore.LoadGame, which raises ModHook.OnGameLoadedLate) and calls
    // ShowMorningReport only if the save holds night logs. The night logs are cleared when the morning report is
    // shown, so an F5 save made later that day usually holds none. Up to 1.2.2 the skip then stayed set, and the
    // next call, the report of the following morning (StartOfDayUIManager.OpenUI after the end of the day), was
    // skipped; its night logs stayed and appeared a day later together with the next night's. The skip is
    // dropped when Start ends.
    [HarmonyPatch(typeof(EmporiumEntry), "Start")]
    internal static class EmporiumStartPatch
    {
        private static Exception Finalizer(Exception __exception)
        {
            SaveExtras.SkipMorningReport = false;
            if (__exception == null) MorningReport.RestoreDisplay();
            return __exception;
        }
    }

    // On the first shutter update after an F5 save is loaded (the store scene has finished starting), the
    // waiting customers are put back. After an F5 save made while open is loaded, the shutter starts closed and
    // the front counter hidden; the door is then opened with the game's own animation and the front area is
    // shown. The opening logic of the day (customer generation) is not run again.
    // While the door animation runs, StoreShutterButton.OpenShutter keeps the shutter button locked
    // (PlayerStore.lockShutterButtonFlag); when it ends, the game unlocks the button and deactivates
    // blockerMaskObject. That image (BlockerMask in the scene, active from the start and again whenever the
    // shutter closes) covers the shop window and receives the clicks aimed at the vending machine and the
    // water vending machine outside. The door opened here goes through the same steps.
    [HarmonyPatch(typeof(StoreShutterButton), "Update")]
    internal static class ShutterUpdatePatch
    {
        private static void Postfix(StoreShutterButton __instance, bool ___isClosed, float ___duration)
        {
            if (!SaveExtras.PendingOpenShutter && !SaveExtras.PendingShowFrontArea && SaveExtras.PendingQueue == null)
                return;
            List<CustomerQueue.Entry> queue = SaveExtras.PendingQueue;
            SaveExtras.PendingQueue = null;
            if (queue != null)
                CustomerQueue.Restore(PlayerStore.Instance, queue);
            try
            {
                if (SaveExtras.PendingOpenShutter && ___isClosed)
                    OpenDoor(__instance, ___duration);
                if (SaveExtras.PendingShowFrontArea && EmporiumEntry.Instance != null)
                    EmporiumEntry.Instance.ShowFrontArea();
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning(F5ForceSaveMod.LogPrefix + "restoring the open shop view failed: " + e);
            }
            finally
            {
                SaveExtras.PendingOpenShutter = false;
                SaveExtras.PendingShowFrontArea = false;
            }
        }

        private static void OpenDoor(StoreShutterButton shutter, float duration)
        {
            PlayerStore store = PlayerStore.Instance;
            if (store != null)
                store.lockShutterButtonFlag++;
            shutter.ToggleShutterDoor();
            ModHelper.StartCoroutine(F5ForceSaveMod.ModId, MetaHelper.WaitAndDo(duration, () => FinishOpening(shutter)));
        }

        // The steps of the callback that StoreShutterButton.OpenShutter schedules after the door animation.
        private static void FinishOpening(StoreShutterButton shutter)
        {
            PlayerStore store = PlayerStore.Instance;
            if (store != null)
                store.lockShutterButtonFlag--;
            if (shutter != null && shutter.blockerMaskObject != null)
                shutter.blockerMaskObject.gameObject.SetActive(false);
            UnityEngine.Debug.Log(F5ForceSaveMod.LogPrefix + "door animation finished; shutter button unlocked and the shop window cleared for clicks");
        }
    }

    // The customers still waiting today (StoreClientManager.clientStack; PlayerStore.GetNextClient takes them
    // from index 0) are ES3NonSerializable: the game itself saves only before the shop opens, when the list is
    // empty. An F5 save stores each waiting customer's identifier and the fields that customer generation sets
    // after a customer is created. After loading, the customers are created again with the game's own
    // factories and put back in the same order; values a factory rolls, such as budgets, are rolled again.
    internal static class CustomerQueue
    {
        // StoreClientManager.AddAugClient appends this to the identifier of a display-case buyer it turns into
        // a disguised aug.
        private const string DisguisedAugSuffix = "_disguised_aug";

        // Customers that StoreClientManager queues but StoreClientListDict cannot create. Their factories take
        // no argument other than the aug tier.
        private const string AugMedId = "aug_med";
        private const string AugMaterialId = "aug_material";
        private const string FlexiBuyerId = "lower_level_flexi_buyer";
        private const string RevRaiderId = "revRaider";
        private const string RevQuartermasterId = "revQuartermaster";

        internal sealed class Entry
        {
            internal string Identifier;
            internal bool IsAug;
            internal int AugTier;
            internal string AttractedItemId;
            internal string AttractedType;
            internal string SourceModId;
        }

        internal static int Count(StoreClientManager manager)
        {
            return manager != null && manager.clientStack != null ? manager.clientStack.Count : 0;
        }

        // Places in the queue (1 = next) of customers that cannot be created again after loading.
        internal static List<int> PositionsNotStorable(StoreClientManager manager)
        {
            var positions = new List<int>();
            if (manager == null || manager.clientStack == null)
                return positions;
            for (int i = 0; i < manager.clientStack.Count; i++)
            {
                StoreClient client = manager.clientStack[i];
                if (client != null && !CanCreate(client.identifier))
                    positions.Add(i + 1);
            }
            return positions;
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

        private static bool CanCreate(string identifier)
        {
            if (string.IsNullOrEmpty(identifier))
                return false;
            string key = FactoryKey(identifier);
            return InDictionary(key) || key == AugMedId || key == AugMaterialId || key == FlexiBuyerId
                || key == RevRaiderId || key == RevQuartermasterId;
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
            // HandlePerkClient queues these reputation sellers directly, outside StoreClientListDict.
            else if (key == RevRaiderId)
                client = StoreClientListRev.CreateRevRaider();
            else if (key == RevQuartermasterId)
                client = StoreClientListRev.CreateRevQuartermaster();
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

        internal static void Restore(PlayerStore store, List<Entry> entries)
        {
            if (store == null || store.storeClientManager == null)
                return;
            StoreClientManager manager = store.storeClientManager;
            if (manager.clientStack == null)
                manager.clientStack = new List<StoreClient>();

            var created = new List<StoreClient>();
            var names = new List<string>();
            int failed = 0;
            foreach (Entry entry in entries)
            {
                StoreClient client = null;
                try
                {
                    client = Create(entry);
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogWarning(F5ForceSaveMod.LogPrefix + "creating the waiting customer '" + entry.Identifier + "' failed: " + e);
                }
                if (client == null)
                {
                    failed++;
                    continue;
                }
                created.Add(client);
                names.Add(client.identifier);
            }

            // Customers already in the list were added after loading; the stored ones were waiting before them.
            manager.clientStack.InsertRange(0, created);
            UnityEngine.Debug.Log(F5ForceSaveMod.LogPrefix + "put back " + created.Count + " waiting customer(s)"
                + (failed > 0 ? ", " + failed + " could not be created" : "") + ": " + string.Join(", ", names.ToArray()));
        }

        internal static string Encode(StoreClientManager manager)
        {
            var parts = new List<string> { "v1" };
            if (manager != null && manager.clientStack != null)
            {
                foreach (StoreClient client in manager.clientStack)
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
            }
            return string.Join("|", parts.ToArray());
        }

        internal static List<Entry> Decode(string text)
        {
            var entries = new List<Entry>();
            string[] parts = (text ?? "").Split('|');
            if (parts[0] != "v1")
            {
                UnityEngine.Debug.LogWarning(F5ForceSaveMod.LogPrefix + "unknown waiting-customer data '" + parts[0] + "'; not restored");
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

    // Texts shown in the game: Chinese when the game language is Chinese, Russian when it is Russian, English otherwise.
    internal static class Text
    {
        private static string LanguageCode()
        {
            UnityEngine.Localization.Locale locale = ModHelper.GetCurrentLocale();
            return locale != null ? locale.Identifier.Code : null;
        }

        private static string T(string zh, string en, string ru)
        {
            string code = LanguageCode();
            if (code != null && code.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
                return zh;
            if (code != null && code.StartsWith("ru", StringComparison.OrdinalIgnoreCase))
                return ru;
            return en;
        }

        private static string Clock(DateTime time)
        {
            return time.ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        private static string Phase(PlayerStore.StoreState state)
        {
            switch (state)
            {
                case PlayerStore.StoreState.OPEN: return T("营业中", "open", "магазин открыт");
                case PlayerStore.StoreState.CLOSED: return T("打烊后", "after closing", "после закрытия");
                default: return T("早上", "morning", "утро");
            }
        }

        private static string ReturnPoint(SavePreviewData last)
        {
            if (last == null)
                return "";
            string clock = Clock(last.lastSaveDateTime);
            return T("现在退出会回到第" + last.dayCount + "天 " + clock + " 的存档。",
                " Quitting now returns to the save of day " + last.dayCount + " at " + clock + ".",
                " Если выйти сейчас, игра вернётся к сохранению дня " + last.dayCount + " (" + clock + ").");
        }

        internal static string Saved(int day, PlayerStore.StoreState state, DateTime time, int waiting)
        {
            string phase = Phase(state);
            return T("已存档：第" + day + "天" + phase + " " + Clock(time) + (waiting > 0 ? "，排队顾客 " + waiting + " 位" : ""),
                "Saved: day " + day + ", " + phase + ", " + Clock(time) + (waiting > 0 ? ", " + waiting + " customer(s) waiting" : ""),
                "Сохранено: день " + day + ", " + phase + ", " + Clock(time) + (waiting > 0 ? ", клиентов в очереди: " + waiting : ""));
        }

        internal static string QueueNotStorable(List<int> positions, SavePreviewData last)
        {
            var numbers = new List<string>();
            foreach (int position in positions)
                numbers.Add(position.ToString(CultureInfo.InvariantCulture));
            bool one = positions.Count == 1;
            string key = SaveKey.Name();
            return T("排队的顾客中第 " + string.Join("、", numbers.ToArray()) + " 位无法存进存档，等" + (one ? "这位顾客" : "这几位顾客") + "来过以后再按 " + key + "。",
                (one ? "Waiting customer no. " : "Waiting customers no. ") + string.Join(", ", numbers.ToArray()) + " can't be stored in the save. Press " + key + " after "
                    + (one ? "this customer has" : "these customers have") + " visited.",
                (one ? "Клиента под номером " : "Клиентов под номерами ") + string.Join(", ", numbers.ToArray()) + " в очереди нельзя сохранить. Нажмите " + key + ", когда "
                    + (one ? "он побывает" : "они побывают") + " у прилавка.")
                + ReturnPoint(last);
        }

        internal static string CustomerPresent(SavePreviewData last)
        {
            string key = SaveKey.Name();
            return T("顾客在柜台时不能存档，等顾客离开后再按 " + key + "。",
                "Can't save while a customer is at the counter. Press " + key + " after they leave.",
                "Нельзя сохраниться, пока у прилавка клиент. Нажмите " + key + ", когда он уйдёт.") + ReturnPoint(last);
        }

        internal static string TemporaryNotEmpty()
        {
            string key = SaveKey.Name();
            return T("有物品在不会存档的临时位置，先移回店里再按 " + key + "。",
                "Items in a temporary area are not saved. Move them back, then press " + key + ".",
                "Предметы во временной зоне не сохраняются. Верните их в магазин и нажмите " + key + ".");
        }

        internal static string Dragging()
        {
            string key = SaveKey.Name();
            return T("松开拖动的物品后再按 " + key + "。", "Release the dragged item, then press " + key + ".", "Отпустите перетаскиваемый предмет и нажмите " + key + ".");
        }

        // The row in the Key Binds section of the game options.
        internal static string KeyOptionLabel()
        {
            return T("立即存档（F5 存档 Mod）", "Save now (F5 Quick Save mod)", "Сохранить сейчас (мод F5 Quick Save)");
        }

        // The entry in this mod's settings window in the Mods panel.
        internal static string KeySettingLabel()
        {
            return T("存档键", "Save key", "Клавиша сохранения");
        }

        internal static string KeySettingTooltip()
        {
            return T("也可以在游戏选项的“按键绑定”中修改。", "Can also be changed under Key Binds in the game options.", "Можно изменить и в настройках игры, в разделе «Назначение клавиш».");
        }

        internal static string LoadKeyOptionLabel()
        {
            return T("快速读档（F5 存档 Mod）", "Quick load (F5 Quick Save mod)", "Быстрая загрузка (мод F5 Quick Save)");
        }

        internal static string LoadKeySettingLabel()
        {
            return T("读档键", "Load key", "Клавиша загрузки");
        }

        internal static string F5LoadKeyOptionLabel()
        {
            return T("读取 F5 存档（F5 存档 Mod）", "Load F5 save (F5 Quick Save mod)", "Загрузка сохранения F5 (мод F5 Quick Save)");
        }

        internal static string F5LoadKeySettingLabel()
        {
            return T("读 F5 存档键", "F5 save load key", "Клавиша загрузки сохранения F5");
        }

        internal static string NoF5Save()
        {
            return T("这个存档位还没有 F5 存档。", "This slot has no F5 save yet.", "В этом слоте пока нет сохранения F5.");
        }

        internal static string AlwaysHighlightConflict()
        {
            string key = F5LoadKey.Name();
            return T("“Always Highlight”Mod 也用 " + key + "，可在选项“按键绑定”中更换本 Mod 的键。",
                "Always Highlight also uses " + key + ". Change this mod's key under Key Binds.",
                "Always Highlight тоже использует " + key + ". Смените клавишу этого мода в разделе «Назначение клавиш».");
        }

        // The notification shows one text and keeps it on one line while it has no line break, so a second sentence follows the
        // first on that line.
        internal static string SameLine(string first, string second)
        {
            bool ended = first.EndsWith("。", StringComparison.Ordinal) || first.EndsWith(".", StringComparison.Ordinal);
            string other = first + (ended ? " " : ". ") + second;
            return T(first + (ended ? "" : "。") + second, other, other);
        }

        internal static string NothingToLoad()
        {
            return T("没有可以读取的存档。", "There is no save to load.", "Нет сохранения для загрузки.");
        }

        internal static string OutsideStore()
        {
            return T("在店外不能存档。", "Can't save outside the store.", "Вне магазина сохраняться нельзя.");
        }

        internal static string GameOver()
        {
            return T("游戏已结束，不能存档。", "Can't save after game over.", "Игра окончена, сохраниться нельзя.");
        }

        internal static string Failed()
        {
            return T("存档失败，详见 Player.log。", "Save failed, see Player.log.", "Сохранить не удалось, подробности в Player.log.");
        }

        internal static string HighlightConflict()
        {
            string key = SaveKey.Name();
            return T("“客户需求高亮”Mod 也用 " + key + "，高亮同时被切换，可在选项“按键绑定”中改键。",
                "Client Demand Highlight also uses " + key + ", so its highlight toggled too. Change a key under Key Binds.",
                "Client Demand Highlight тоже использует " + key + ", подсветка переключилась. Смените клавишу в разделе «Назначение клавиш».");
        }

        // 1.8.0: the modifiers of the three keys (KeyModifier.cs). The rows in the Key Binds section of the game options.
        internal static string SaveModifierOptionLabel()
        {
            return T("立即存档的修饰键（F5 存档 Mod）", "Save now: modifier (F5 Quick Save mod)", "Сохранить сейчас: модификатор (мод F5 Quick Save)");
        }

        internal static string LoadModifierOptionLabel()
        {
            return T("快速读档的修饰键（F5 存档 Mod）", "Quick load: modifier (F5 Quick Save mod)", "Быстрая загрузка: модификатор (мод F5 Quick Save)");
        }

        internal static string F5LoadModifierOptionLabel()
        {
            return T("读取 F5 存档的修饰键（F5 存档 Mod）", "Load F5 save: modifier (F5 Quick Save mod)", "Загрузка сохранения F5: модификатор (мод F5 Quick Save)");
        }

        // The choices of those rows, in the order of KeyModifier.
        internal static System.Collections.Generic.IList<string> ModifierChoices()
        {
            return new System.Collections.Generic.List<string> { T("无", "None", "Нет"), "Ctrl", "Shift", "Alt" };
        }

        // The entries in this mod's settings window in the Mods panel.
        internal static string SaveModifierSettingLabel()
        {
            return T("存档键的修饰键", "Save key modifier", "Модификатор клавиши сохранения");
        }

        internal static string LoadModifierSettingLabel()
        {
            return T("读档键的修饰键", "Load key modifier", "Модификатор клавиши загрузки");
        }

        internal static string F5LoadModifierSettingLabel()
        {
            return T("读 F5 存档键的修饰键", "F5 save load key modifier", "Модификатор клавиши загрузки сохранения F5");
        }

        internal static string ModifierSettingTooltip()
        {
            return T("与这个键一起按住的键，例如选 Ctrl 后用 Ctrl+F5。选 None 时，只在没有按住 Ctrl、Shift、Alt 时生效。也可以在游戏选项的“按键绑定”中修改。",
                "The key to hold together with this key, for example Ctrl for Ctrl+F5. With None the key works only while Ctrl, Shift and Alt are not held. Can also be changed under Key Binds in the game options.",
                "Клавиша, которую нужно удерживать вместе с этой клавишей, например Ctrl для Ctrl+F5. При None клавиша действует, только если не удерживаются Ctrl, Shift и Alt. Можно изменить и в настройках игры, в разделе «Назначение клавиш».");
        }

        // The Apply button of the settings window, as ModConfigWindow.Open names it.
        private static string ApplyName()
        {
            return LocHelper.GetLocalizedMainMenu("option_apply", new object[0]);
        }

        // The name of the game option "Saved days per run" in the language of the game, as the game options show it
        // (GameOptionRegistry.RegisterVanilla).
        private static string SaveDays()
        {
            return LocHelper.GetLocalizedOrDefault("MainMenu", "option_save_history_days", "Saved days per run");
        }

        // 1.8.0: the "Saves" group of this mod's settings (SaveSettings).
        internal static string LimitSettingLabel()
        {
            return T("限制 F5 存档的数量", "Limit the number of F5 saves", "Ограничить число сохранений F5");
        }

        internal static string LimitSettingTooltip()
        {
            return T("勾选时，每个存档位的 F5 存档按下面的上限保留；超出上限时，每存一份新的 F5 存档删除最旧的一份。调低上限不会删除已有的 F5 存档。取消勾选后不限制数量。自动存档仍按游戏选项“" + SaveDays() + "”保留。" + PlusNote(),
                "When on, each save slot keeps F5 saves up to the limit below; above it, each new F5 save deletes the oldest one. Lowering the limit does not delete existing F5 saves. When off, the number is not limited. Autosaves still follow \"" + SaveDays() + "\" in the game options." + PlusNote(),
                "Если включено, в каждом слоте хранится не больше сохранений F5, чем указано ниже; сверх предела каждое новое сохранение F5 удаляет самое старое. Уменьшение предела не удаляет уже имеющиеся сохранения F5. Если выключено, число не ограничено. Автосохранения по-прежнему хранятся по настройке игры «" + SaveDays() + "»." + PlusNote());
        }

        internal static string MatchSettingLabel()
        {
            return T("F5 存档上限与游戏相同", "F5 save limit same as the game", "Предел сохранений F5 как в игре");
        }

        internal static string MatchSettingTooltip()
        {
            return T("勾选时，F5 存档的上限等于游戏选项“" + SaveDays() + "”，并随它变化；取消勾选后可以单独设置。",
                "When on, the limit of F5 saves equals \"" + SaveDays() + "\" in the game options and follows it; when off, it can be set on its own.",
                "Если включено, предел сохранений F5 равен настройке игры «" + SaveDays() + "» и меняется вместе с ней; если выключено, его можно задать отдельно.");
        }

        internal static string CountSettingLabel()
        {
            return T("F5 存档保留上限", "F5 saves to keep", "Сколько хранить сохранений F5");
        }

        internal static string CountSettingTooltip()
        {
            return T("每个存档位最多保留的 F5 存档份数。勾选“F5 存档上限与游戏相同”时不能修改；没有勾选“限制 F5 存档的数量”时不起作用。",
                "The most F5 saves each save slot keeps. It cannot be changed while \"F5 save limit same as the game\" is on, and has no effect while \"Limit the number of F5 saves\" is off.",
                "Наибольшее число сохранений F5 в каждом слоте. Не меняется, пока включено «Предел сохранений F5 как в игре», и не действует, пока выключено «Ограничить число сохранений F5».");
        }

        internal static string CopySettingLabel()
        {
            return T("复制存档位时不带 F5 存档", "Leave F5 saves out when copying a slot", "Не копировать сохранения F5 при копировании слота");
        }

        internal static string CopySettingTooltip()
        {
            return T("勾选后，在读档界面复制存档位时只复制自动存档；最新一份是 F5 存档时，副本从最近一份自动存档开始。存档位没有自动存档时不能复制。点“" + ApplyName() + "”后生效，不需要重启游戏，只影响之后的复制。" + PlusNote(),
                "When on, copying a save slot on the Load Game screen copies only the autosaves; if the newest save is an F5 save, the copy starts from the latest autosave. A slot without autosaves cannot be copied. Takes effect after \"" + ApplyName() + "\", without a restart, for later copies only." + PlusNote(),
                "Если включено, при копировании слота на экране загрузки копируются только автосохранения; если последнее сохранение сделано через F5, копия начинается с последнего автосохранения. Слот без автосохранений скопировать нельзя. Действует после «" + ApplyName() + "», без перезапуска, только для следующих копий." + PlusNote());
        }

        // T-031: "End the Day", the button of the End of Day tablet, in the language of the game (EndOfDayUIManager.InitUI).
        private static string EndDayName()
        {
            return LocHelper.GetLocalizedOrDefault("UI", "ui_end_day", "End the Day");
        }

        // T-031: the save before sleeping (SaveSettings.SleepKey, SleepSave).
        internal static string SleepSettingLabel()
        {
            return T("睡前自动存档", "Save before sleeping", "Сохранять перед сном");
        }

        internal static string SleepSettingTooltip()
        {
            string key = F5LoadKey.Name();
            return T("勾选后，每晚点“" + EndDayName() + "”时先存一份 F5 存档：读档界面标“| F5”，按 " + key + " 可以读取，计入 F5 存档的保留份数。" + PlusNote(),
                "When on, clicking \"" + EndDayName() + "\" each night first makes an F5 save: it is marked \"| F5\" on the Load Game screen, " + key + " loads it, and it counts toward the F5 saves kept." + PlusNote(),
                "Если включено, каждый вечер при нажатии «" + EndDayName() + "» сначала делается сохранение F5: на экране загрузки оно помечено «| F5», " + key + " загружает его, и оно учитывается в числе хранимых сохранений F5." + PlusNote());
        }

        // T-031: the messages of the save before sleeping, shown when the next morning begins (SleepSave).
        internal static string SleepSaved(int day, PlayerStore.StoreState state, DateTime time)
        {
            string phase = Phase(state);
            string key = F5LoadKey.Name();
            return T("睡前已存档：第" + day + "天" + phase + " " + Clock(time) + "，按 " + key + " 读取。",
                "Saved before sleeping: day " + day + ", " + phase + ", " + Clock(time) + ". " + key + " loads it.",
                "Сохранено перед сном: день " + day + ", " + phase + ", " + Clock(time) + ". " + key + " загружает это сохранение.");
        }

        internal static string SleepNotSaved(string reason)
        {
            return T("睡前没有存档：", "No save before sleeping: ", "Сохранение перед сном не сделано: ") + reason;
        }

        internal static string SleepFailed()
        {
            return T("睡前存档失败，详见 Player.log。", "The save before sleeping failed, see Player.log.", "Сохранение перед сном не удалось, подробности в Player.log.");
        }

        // With F5 Quick Save Plus running, it handles F5 saves and its own settings of the same names apply.
        private static string PlusNote()
        {
            return T("同时启用 F5 存档加强版时，按加强版中的这项设置。",
                " With F5 Quick Save Plus enabled, its setting of the same name applies.",
                " Если включён F5 Quick Save Plus, действует его одноимённая настройка.");
        }

        // 1.8.0: the question before the limit is lowered (SaveSettingsUi).
        internal static string LowerLimitTitle()
        {
            return T("调低 F5 存档的保留数量", "Lower the number of F5 saves kept", "Уменьшить число хранимых сохранений F5");
        }

        internal static string LowerLimitLine1(int limit)
        {
            return T("每个存档位的 F5 存档上限将改为 " + limit + " 份。", "The limit of F5 saves per slot will be " + limit + ".", "Предел сохранений F5 в каждом слоте станет " + limit + ".");
        }

        internal static string LowerLimitLine2()
        {
            return T("已有的 F5 存档不会因此被删除。", "Existing F5 saves are not deleted because of this.", "Уже имеющиеся сохранения F5 из-за этого не удаляются.");
        }

        internal static string LowerLimitLine3()
        {
            return T("之后每存一份新的 F5 存档，只删除最旧的一份。", "Each new F5 save then deletes only the oldest one.", "Каждое новое сохранение F5 затем удаляет только самое старое.");
        }

        internal static string ConfirmButton()
        {
            return T("确认", "Confirm", "Подтвердить");
        }

        internal static string CancelButton()
        {
            return T("取消", "Cancel", "Отмена");
        }

        // 1.8.0: the second line under an F5 save on the Load Game screen (SaveDetails, from pull request #1).
        internal static string DetailState(int state)
        {
            switch (state)
            {
                case (int)PlayerStore.StoreState.MORNING: return T("开店前", "Before opening", "До открытия");
                case (int)PlayerStore.StoreState.OPEN: return T("营业中", "Shop open", "Магазин открыт");
                case (int)PlayerStore.StoreState.CLOSED: return T("打烊后", "After closing", "После закрытия");
                default: return T("店铺状态未知", "Shop state unknown", "Состояние магазина неизвестно");
            }
        }

        internal static string DetailWaiting(int waiting)
        {
            return T("排队 " + waiting + " 人", "Waiting: " + waiting, "В очереди: " + waiting);
        }

        internal static string DetailWentOut(bool wentOut)
        {
            return wentOut ? T("已外出", "Went out tonight", "Уже выходил сегодня") : T("未外出", "Not out tonight", "Ещё не выходил");
        }

        // Added after the day and money of a save on the Load Game screen.
        internal static string SaveKind(bool quick)
        {
            if (quick)
                return " | F5";
            return T(" | 自动", " | Auto", " | Авто");
        }
    }
}
