using System;

[assembly: System.Reflection.AssemblyTitle("Moneylending")]
[assembly: System.Reflection.AssemblyVersion("1.5.1.0")]

namespace Moneylending
{
    // Entry point found by the game's ModLoader, which loads mods once per launch and applies the Harmony patches
    // in this assembly (Harmony ID "mod.<manifest ID>") before calling Init and OnEnable.
    public sealed class MoneylendingMod : IMod
    {
        internal const string ModId = "Moneylending";
        internal const string LogPrefix = "[Moneylending] ";
        private static UnityEngine.GameObject host;

        public void Init(ModManifest manifest)
        {
            UnityEngine.Debug.Log(LogPrefix + "v" + manifest.ModVersion + " loaded");
        }

        public void OnEnable()
        {
            // The mod runs with the default price and rates if its settings cannot be set up.
            try
            {
                Settings.Define();
            }
            catch (Exception e)
            {
                Warn("setting up the settings failed: " + e);
            }
            Clients.RegisterStatic();
            // F5 Quick Save Plus stores this mod's visitors exactly only with the adapter (F5SnapshotSpec); without it
            // they are handled as objects it does not know.
            try
            {
                F5SnapshotSpec.Register();
            }
            catch (Exception e)
            {
                Warn("registering the adapter for F5 Quick Save Plus failed: " + e);
            }

            ModHook.OnGameLoadedInit -= OnGameLoading;
            ModHook.OnGameLoadedInit += OnGameLoading;
            ModHook.OnGameLoadedEarly -= OnGameRead;
            ModHook.OnGameLoadedEarly += OnGameRead;
            ModHook.OnNewGameStarted -= OnNewGame;
            ModHook.OnNewGameStarted += OnNewGame;
            ModHook.OnGameLoadedLate -= OnGameLoaded;
            ModHook.OnGameLoadedLate += OnGameLoaded;
            ModHook.OnGenerateCustomerVeryLate -= Daily.OnGenerateCustomers;
            ModHook.OnGenerateCustomerVeryLate += Daily.OnGenerateCustomers;
            ModHook.OnGoingSleepLate -= Daily.OnNight;
            ModHook.OnGoingSleepLate += Daily.OnNight;
            ModHook.OnCreateTooltipLate -= Pledges.OnTooltip;
            ModHook.OnCreateTooltipLate += Pledges.OnTooltip;
            ModHook.OnModItemDirectoryInit -= Sign.OnItemDirectoryInit;
            ModHook.OnModItemDirectoryInit += Sign.OnItemDirectoryInit;

            if (host == null)
            {
                host = new UnityEngine.GameObject("Moneylending");
                UnityEngine.Object.DontDestroyOnLoad(host);
                host.AddComponent<Hotkey>();
            }
        }

        // Called when the game quits (ModLoader.OnApplicationQuit) and when it stops the running mod
        // (ModLoader.StopRunningMod: a changed trust verdict or the Steam guard).
        public void OnDisable()
        {
            ModHook.OnGameLoadedInit -= OnGameLoading;
            ModHook.OnGameLoadedEarly -= OnGameRead;
            ModHook.OnNewGameStarted -= OnNewGame;
            ModHook.OnGameLoadedLate -= OnGameLoaded;
            ModHook.OnGenerateCustomerVeryLate -= Daily.OnGenerateCustomers;
            ModHook.OnGoingSleepLate -= Daily.OnNight;
            ModHook.OnCreateTooltipLate -= Pledges.OnTooltip;
            ModHook.OnModItemDirectoryInit -= Sign.OnItemDirectoryInit;
            try
            {
                F5SnapshotSpec.Unregister();
            }
            catch (Exception e)
            {
                Warn("removing the adapter for F5 Quick Save Plus failed: " + e);
            }
            if (host != null)
            {
                UnityEngine.Object.Destroy(host);
                host = null;
            }
        }

        // PlayerStore.LoadGame, before the slot file is read into the store.
        private static void OnGameLoading()
        {
            try
            {
                Unlock.Register();
            }
            catch (Exception e)
            {
                Warn("registering the network entry failed: " + e);
            }
        }

        // A new run, before its first save.
        private static void OnNewGame(string scenarioId)
        {
            try
            {
                Unlock.Register();
                Book.Load();
            }
            catch (Exception e)
            {
                Warn("starting the ledger for a new run failed: " + e);
            }
        }

        // PlayerStore.LoadGame has read the slot file into the store (ES3.LoadInto, PlayerStore.modData with the ledger
        // included) and fires OnGameLoadedEarly before it reads the inventories and fires OnGameLoadedLate. The ledger is
        // read here, before any mod's OnGameLoadedLate handler, whatever the order in which the mods were loaded:
        // F5 Quick Save Plus reads its snapshot in its OnGameLoadedLate handler,
        // finds the loans, debts and today's request of the restored visitors in the ledger by key (F5SnapshotSpec.Find),
        // and later recreates customers by identifier. Visitors queued for a later day are also stored by identifier, so the
        // per-loan identifiers have to exist before any queue is rebuilt. A load from inside a run can keep the same
        // PlayerStore object (PlayerStore.ResetData clears it only on the way to the main menu), so Book.S would not notice
        // the new save on its own; the ledger is read explicitly on every load.
        private static void OnGameRead()
        {
            try
            {
                Book.Load();
                Clients.RegisterRecords();
            }
            catch (Exception e)
            {
                Warn("reading the ledger failed: " + e);
            }
        }

        // PlayerStore and the inventories have been read from the slot file. The ledger is not read again here: that
        // would replace Book.S, and the loans, debts and request that F5 Quick Save Plus may already have found for its
        // restored visitors (if its handler ran first) would no longer be the ledger's, so their repayments would not be
        // recorded.
        private static void OnGameLoaded()
        {
            try
            {
                Unlock.Register();
                Log("ledger loaded: " + Book.S.loans.Count + " loan(s), " + Book.S.debts.Count + " debt(s)");
                Sign.GiveIfMissing();
            }
            catch (Exception e)
            {
                Warn("setting up the loaded run failed: " + e);
            }
        }

        internal static void Log(string message)
        {
            UnityEngine.Debug.Log(LogPrefix + message);
        }

        internal static void Warn(string message)
        {
            UnityEngine.Debug.LogWarning(LogPrefix + message);
        }
    }

    internal static class Game
    {
        internal const string GoodNews = "5c6f46";
        internal const string Warning = "cc7755";
        internal const string Loss = "9c3738";

        // PlayerStore.Instance and StoreStation.Instance create an empty instance when none exists (also at the
        // main menu); EmporiumEntry.Instance is set only while the store scene of a run is loaded.
        internal static bool InRun
        {
            get { return EmporiumEntry.Instance != null && PlayerStore.HasInstance; }
        }

        internal static int Day
        {
            get { return StoreStation.GetDayCounter(); }
        }

        internal static StoreClient Current()
        {
            PlayerStore store = PlayerStore.Instance;
            return store != null && store.currentClientInstance != null ? store.currentClientInstance.GetClientBlueprint() : null;
        }

        internal static int Cash
        {
            get { return PlayerStore.Instance != null ? PlayerStore.Instance.playerCash : 0; }
        }

        // The game 1.0.0.12 (buildid 26496118) lets the player shoot Sawyer dead (StoreClientListStory.SetupSawyer); the
        // game then sets StoreClientData.isSawyerDead and stops sending him. 1.4.1: this mod stops sending him too
        // (VVittgenstein 2026-11-07).
        internal static bool SawyerDead
        {
            get
            {
                PlayerStore store = PlayerStore.Instance;
                return store != null && store.storeClientData != null && store.storeClientData.isSawyerDead;
            }
        }

        internal static int Reputation(string faction)
        {
            if (string.IsNullOrEmpty(faction))
                return 0;
            StoreReputation r = StoreReputation.GetStoreReputation(faction);
            return r != null ? r.GetReputation() : 0;
        }

        internal static bool HasReputation(string faction)
        {
            return !string.IsNullOrEmpty(faction) && StoreReputation.GetStoreReputation(faction) != null;
        }

        internal static void ModReputation(string faction, int value)
        {
            if (value != 0 && HasReputation(faction))
                StoreReputation.ModReputation(faction, value, true);
        }

        internal static void Notify(string message)
        {
            StoreUIManager ui = StoreUIManager.Instance;
            if (ui != null)
                ui.Notify(message, "default");
            MoneylendingMod.Log(message);
        }

        internal static void NightLog(string text, string colour)
        {
            PlayerStore store = PlayerStore.Instance;
            if (store == null)
                return;
            if (colour != null)
                store.AddNightLog(text, colour);
            else
                store.AddNightLog(text);
            MoneylendingMod.Log("night log: " + text);
        }

        internal static int RoundTo(int value, int step)
        {
            return Math.Max(step, (int)Math.Round(value / (double)step) * step);
        }

        internal static bool IsInStore(GameItem item)
        {
            return item != null && Game.InRun && PlayerStore.Instance.FindAllItem(true).Contains(item);
        }

        // A new item of the player's on the counter, or in the store's inventory when the counter has no room. An
        // item that PlayerStore.TryAddDirectSellingItemToTable could not place is destroyed by the game at the end
        // of the frame ("Destroyed unplaced items"), wherever it is by then; moving that same item into the inventory
        // left a broken tile there (seen in game). So the inventory gets a second copy from the same factory.
        internal static bool PutOnCounter(Func<GameItem> create)
        {
            if (!InRun)
                return false;
            GameItem item = create();
            if (item == null)
                return false;
            if (PlayerStore.Instance.TryAddDirectSellingItemToTable(item, true, false, true, 0))
                return true;
            GameItem copy = create();
            if (copy == null)
                return false;
            GeneralHelper.SetItemOwned(copy, true);
            ModHelper.MoveItemToInventory(copy);
            if (IsInStore(copy))
                return true;
            MoneylendingMod.Warn("no room in the store for " + copy.identifier);
            return false;
        }
    }

    // L opens or closes the ledger while a run is loaded, the store is shown and no text field is focused.
    public sealed class Hotkey : UnityEngine.MonoBehaviour
    {
        private void Update()
        {
            if (!UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.L))
                return;
            try
            {
                if (!Game.InRun || ModHelper.IsOutsideStore() || StoreUIManager.IsAnyTyping())
                    return;
                if (!Unlock.IsUnlocked())
                {
                    Game.Notify(Text.NotUnlocked());
                    return;
                }
                LedgerWindow.Toggle();
            }
            catch (Exception e)
            {
                MoneylendingMod.Warn("the ledger key failed: " + e);
            }
        }
    }
}
