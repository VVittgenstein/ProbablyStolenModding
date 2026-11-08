using System;
using System.Collections;
using System.Globalization;

namespace F5ForceSave
{
    // The load key, F8 unless the player changes it. It is kept like the save key (see SaveKey): an entry of the
    // game's mod configuration ("load_key" in ModConfigs\F5ForceSave.json), a row in the Key Binds section of the game
    // options, where the game refuses a key that another row uses, and an entry in this mod's settings window.
    internal static class LoadKey
    {
        internal const string ConfigKey = "load_key";
        internal const string OptionId = "load_key";
        private const string Section = "按键 / Key / Клавиши";
        private static ConfigEntry<string> entry;

        internal static UnityEngine.KeyCode Current = UnityEngine.KeyCode.F8;

        internal static void Load()
        {
            ModConfig config = ModHelper.GetConfig(F5ForceSaveMod.ModId);
            entry = config.Define(ConfigKey, UnityEngine.KeyCode.F8.ToString(), Section, Text.LoadKeySettingLabel, SaveKey.Choices(), Text.KeySettingTooltip);
            Apply(entry.Value);
            entry.OnChanged += name =>
            {
                Apply(name);
                UnityEngine.Debug.Log(F5ForceSaveMod.LogPrefix + "load key set to " + Current);
            };
            config.Save();
            UnityEngine.Debug.Log(F5ForceSaveMod.LogPrefix + "load key " + Current + " (" + config.FilePath + ", entry " + ConfigKey + ")");
        }

        private static void Apply(string name)
        {
            UnityEngine.KeyCode key;
            if (SaveKey.TryParse(name, out key))
            {
                Current = key;
                return;
            }
            Current = UnityEngine.KeyCode.F8;
            UnityEngine.Debug.LogWarning(F5ForceSaveMod.LogPrefix + "'" + name + "' is not a key this mod accepts; the load key is F8");
        }

        private static void Set(UnityEngine.KeyCode key)
        {
            if (!SaveKey.Bindable(key))
                return;
            if (entry != null)
                entry.Set(key.ToString()); // OnChanged applies it; the game writes the file
            else
                Current = key;
        }

        // Added after the save key row (see SaveKey.AddOption).
        internal static void AddOption()
        {
            GameOptionRegistry.EnsureVanilla();
            ModHelper.AddGameOptionKeyBind(F5ForceSaveMod.ModId, GameOptionRegistry.SECTION_KEYBIND, OptionId, Text.LoadKeyOptionLabel, () => Current, Set);
        }

        internal static void RemoveOption()
        {
            ModHelper.RemoveGameOption(F5ForceSaveMod.ModId, OptionId);
        }
    }

    // The key that loads the last F5 save of the current slot, F7 unless the player changes it. The load key always takes
    // the newest save, which after a night is the game's own morning save with the night's result in it; this key takes
    // the save the player made with F5. It is kept like the load key: "f5_load_key" in ModConfigs\F5ForceSave.json, a row in the
    // Key Binds section of the game options, and an entry in this mod's settings window.
    internal static class F5LoadKey
    {
        internal const string ConfigKey = "f5_load_key";
        internal const string OptionId = "f5_load_key";
        private const string Section = "按键 / Key / Клавиши";
        private static ConfigEntry<string> entry;

        internal static UnityEngine.KeyCode Current = UnityEngine.KeyCode.F7;

        internal static void Load()
        {
            ModConfig config = ModHelper.GetConfig(F5ForceSaveMod.ModId);
            entry = config.Define(ConfigKey, UnityEngine.KeyCode.F7.ToString(), Section, Text.F5LoadKeySettingLabel, SaveKey.Choices(), Text.KeySettingTooltip);
            Apply(entry.Value);
            entry.OnChanged += name =>
            {
                Apply(name);
                UnityEngine.Debug.Log(F5ForceSaveMod.LogPrefix + "F5 save load key set to " + Current);
            };
            config.Save();
            UnityEngine.Debug.Log(F5ForceSaveMod.LogPrefix + "F5 save load key " + Current + " (" + config.FilePath + ", entry " + ConfigKey + ")");
        }

        private static void Apply(string name)
        {
            UnityEngine.KeyCode key;
            if (SaveKey.TryParse(name, out key))
            {
                Current = key;
                return;
            }
            Current = UnityEngine.KeyCode.F7;
            UnityEngine.Debug.LogWarning(F5ForceSaveMod.LogPrefix + "'" + name + "' is not a key this mod accepts; the F5 save load key is F7");
        }

        private static void Set(UnityEngine.KeyCode key)
        {
            if (!SaveKey.Bindable(key))
                return;
            if (entry != null)
                entry.Set(key.ToString()); // OnChanged applies it; the game writes the file
            else
                Current = key;
        }

        internal static string Name()
        {
            return KeyBindHelper.KeyName(Current);
        }

        // Added after the load key row.
        internal static void AddOption()
        {
            GameOptionRegistry.EnsureVanilla();
            ModHelper.AddGameOptionKeyBind(F5ForceSaveMod.ModId, GameOptionRegistry.SECTION_KEYBIND, OptionId, Text.F5LoadKeyOptionLabel, () => Current, Set);
        }

        internal static void RemoveOption()
        {
            ModHelper.RemoveGameOption(F5ForceSaveMod.ModId, OptionId);
        }
    }

    // The load key loads the newest save of the current slot, as "Load latest" on the Load Game screen does. In the
    // game that takes two steps. Leaving to the main menu (EscapeUIManager.MainMenuWithAnalytics) resets PlayerStore,
    // EmporiumEntry and StoreStation and loads the scene EmporiumMenu, whose MainMenuUIController.Awake replaces
    // NewGameData with a new one (NewGameData.HardReset). A Load button of the Load Game screen (LoadGameUI.LoadSlot)
    // then sets NewGameData.saveSlot and saveFile and starts the store scene InventoryScene, where EmporiumEntry.Start
    // loads that slot because saveSlot is not -1. The same steps are taken here in one go, without the main menu.
    // saveFile stays empty, so SaveFiles.ResolveLoadFile takes the slot file, the newest save of the slot, F5 or the
    // game's own. The game never changes Time.timeScale, so there is no pause to undo.
    //
    // 1.5.1: the steps run at the end of the frame. The key is read in the Update phase, and objects that update later
    // in that frame used to meet the emptied PlayerStore.Instance and StoreStation.Instance, which create blank ones:
    // at the dumping ground DumpingGroundLocation.Tick then threw a NullReferenceException. The game's own way to the
    // main menu also runs after the Update phase, in a coroutine.
    // 1.5.1: two static flags are cleared. Interactable.isPaused and TooltipContainer.isPaused are set while a response
    // window is open (EmporiumEntry.ShowResponseWindow, for example the window that picks a backpack before going out)
    // and cleared only when it closes. Nothing else clears Interactable.isPaused, not even going through the main menu,
    // so loading with that window open left every Interactable in the store (the door, the sleeping bag) ignoring
    // clicks until the game was restarted.
    // 1.5.2: away from the store the game's own way back runs first. Without it the mods listening to
    // ModHook.OnAfterhourLeave and OnReturningStore were told about going out and never about coming back, because
    // the load replaces the store scene while those objects stay; a mod that changes the screen while the player is
    // out keeps that change until the game restarts. The reports (CHOCOZ 2026-11-04, 断水流大大大师兄 2026-11-05) were not
    // reproduced with the mods on this computer, so whether this is their cause has not been verified.
    internal static class QuickLoad
    {
        private const string StoreScene = "InventoryScene";
        private const float GiveUpSeconds = 3f;
        private static bool pending;
        private static string pendingNote;

        internal static bool Pending
        {
            get { return pending; }
        }

        // host runs the coroutine; it has to survive the scene change.
        internal static void TryLoad(UnityEngine.MonoBehaviour host)
        {
            if (pending)
                return;
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != StoreScene || !PlayerStore.HasInstance || NewGameData.Instance == null)
                return;
            if (StoreUIManager.IsAnyTyping() || QuickSave.InProgress)
                return;

            PlayerStore store = PlayerStore.Instance;
            int slot = store.saveSlotId;
            SavePreviewData latest = slot >= 0 ? SaveUIManager.GetPreview(slot) : null;
            if (latest == null)
            {
                string message = Text.NothingToLoad();
                if (StoreUIManager.Instance != null)
                    StoreUIManager.Instance.Notify(message, "default");
                UnityEngine.Debug.Log(F5ForceSaveMod.LogPrefix + "not loaded: slot " + slot + " has no save");
                return;
            }

            UnityEngine.Debug.Log(F5ForceSaveMod.LogPrefix + "quick load: slot " + slot + ", day " + latest.dayCount + ", saved "
                + latest.lastSaveDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            pending = true;
            host.StartCoroutine(LoadAtEndOfFrame(store, slot, null));
        }

        // The last save made with the save key: the slot file when that is the newest save of the slot, otherwise the history
        // file (LoadGameUI.LoadSlot takes the same file name for a row of the Load Game screen). A slot without a recorded F5
        // save stays as it is. F5 saves made before 1.4.0 are not recorded and do not count.
        internal static void TryLoadLastF5(UnityEngine.MonoBehaviour host)
        {
            if (pending)
                return;
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != StoreScene || !PlayerStore.HasInstance || NewGameData.Instance == null)
                return;
            if (StoreUIManager.IsAnyTyping() || QuickSave.InProgress)
                return;

            PlayerStore store = PlayerStore.Instance;
            int slot = store.saveSlotId;
            string conflict = KeyConflict.WithAlwaysHighlight(F5LoadKey.Current) ? Text.AlwaysHighlightConflict() : null;
            string file;
            if (slot < 0 || !QuickHistory.FindLastQuick(slot, out file))
            {
                if (StoreUIManager.Instance != null)
                {
                    if (conflict != null)
                        Notice.Show(StoreUIManager.Instance, Text.NoF5Save(), conflict);
                    else
                        StoreUIManager.Instance.Notify(Text.NoF5Save(), "default");
                }
                UnityEngine.Debug.Log(F5ForceSaveMod.LogPrefix + "not loaded: slot " + slot + " has no F5 save");
                return;
            }

            UnityEngine.Debug.Log(F5ForceSaveMod.LogPrefix + "load last F5 save: slot " + slot + ", " + (file ?? "the slot file"));
            pendingNote = conflict;
            pending = true;
            host.StartCoroutine(LoadAtEndOfFrame(store, slot, file));
        }

        // The message of a key that loads would go with the old scene, so it waits for the new one.
        internal static void OnGameLoaded()
        {
            string note = pendingNote;
            pendingNote = null;
            if (note == null)
                return;
            try
            {
                if (StoreUIManager.Instance != null)
                    StoreUIManager.Instance.Notify(note, "default");
                UnityEngine.Debug.Log(F5ForceSaveMod.LogPrefix + note);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning(F5ForceSaveMod.LogPrefix + "showing the key conflict after the load failed: " + e);
            }
        }

        // The transit map closes at once (MapUIManager.CloseUI). From a location MapUIManager.ReturnNow fades to black and
        // comes back inside that fade; while another transition runs it does nothing and that transition is waited for.
        // 1.6.1: the game 1.0.0.12 (buildid 26496118) replaced CloseUI() with CloseUI(bool restoreStoreUI = true), which
        // with true also shows again the store windows and panels hidden when the map opened. 1.6.0 was built against
        // CloseUI(), which no longer exists. true is the value the map's own close button (TransitUI.OnCloseClicked)
        // passes, and ReturnNow restores those windows as well (MapUIManager.OnReturning).
        private static bool GoBackToStore()
        {
            MapUIManager map = MapUIManager.Instance;
            if (map == null)
                return false;
            if (map.IsOutside)
            {
                map.ReturnNow();
                return true;
            }
            if (TransitUI.IsOpen)
                map.CloseUI(true);
            return false;
        }

        private static IEnumerator LoadAtEndOfFrame(PlayerStore store, int slot, string file)
        {
            yield return new UnityEngine.WaitForEndOfFrame();
            bool wait = false;
            try
            {
                wait = GoBackToStore();
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning(F5ForceSaveMod.LogPrefix + "going back to the store before loading failed; loading anyway: " + e);
            }
            if (wait)
            {
                float giveUp = UnityEngine.Time.realtimeSinceStartup + GiveUpSeconds;
                while (MapUIManager.Instance != null && MapUIManager.Instance.IsOutside && UnityEngine.Time.realtimeSinceStartup < giveUp)
                    yield return null;
                if (MapUIManager.Instance != null && MapUIManager.Instance.IsOutside)
                    UnityEngine.Debug.LogWarning(F5ForceSaveMod.LogPrefix + "still away from the store after " + GiveUpSeconds + " s; loading anyway");
                yield return new UnityEngine.WaitForEndOfFrame();
            }
            pending = false;
            Interactable.isPaused = false;
            TooltipContainer.isPaused = false;
            store.Reset();
            EmporiumEntry.ResetData();
            PlayerStore.ResetData();
            StoreStation.ResetData();
            NewGameData.HardReset();
            NewGameData.Instance.saveSlot = slot;
            NewGameData.Instance.saveFile = file;
            EmporiumEntry.sceneLoadStartTime = UnityEngine.Time.realtimeSinceStartup;
            UnityEngine.SceneManagement.SceneManager.LoadScene(StoreScene, UnityEngine.SceneManagement.LoadSceneMode.Single);
        }
    }
}
