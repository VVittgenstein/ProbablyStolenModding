[assembly: System.Reflection.AssemblyTitle("GunsmithExpansion")]
[assembly: System.Reflection.AssemblyVersion("0.7.6.0")]

namespace GunsmithExpansion
{
    // Entry point found by the game's ModLoader, which loads mods once per launch and applies the Harmony patches
    // in this assembly (Harmony ID "mod.<manifest ID>") before calling Init and OnEnable.
    public sealed class GunsmithMod : IMod
    {
        internal const string ModId = "GunsmithExpansion";
        internal const string LogPrefix = "[GunsmithExpansion] ";
        internal static string Version = "0.7.6";

        public void Init(ModManifest manifest)
        {
            Version = manifest.ModVersion;
            UnityEngine.Debug.Log(LogPrefix + "v" + manifest.ModVersion + " loaded");
        }

        public void OnEnable()
        {
            ModHook.OnModItemDirectoryInit -= Items.OnItemDirectoryInit;
            ModHook.OnModItemDirectoryInit += Items.OnItemDirectoryInit;
            ModHook.OnGameLoadedLate -= SaveRecord.OnGameLoaded;
            ModHook.OnGameLoadedLate += SaveRecord.OnGameLoaded;
            ModHook.OnGameLoadedLate -= Phone.OnGameLoaded;
            ModHook.OnGameLoadedLate += Phone.OnGameLoaded;
            ModHook.OnGameLoadedLate -= GameGunLevels.OnGameLoaded;
            ModHook.OnGameLoadedLate += GameGunLevels.OnGameLoaded;
            ModHook.OnGameLoadedLate -= Cabinets.OnGameLoaded;
            ModHook.OnGameLoadedLate += Cabinets.OnGameLoaded;
            ModHook.OnNetworkUpgradeUnlocked -= RetiredGunsmithService.OnUpgradeUnlocked;
            ModHook.OnNetworkUpgradeUnlocked += RetiredGunsmithService.OnUpgradeUnlocked;
            ModHook.OnNetworkUpgradeUnlocked -= ManualServiceHook.OnUpgradeUnlocked;
            ModHook.OnNetworkUpgradeUnlocked += ManualServiceHook.OnUpgradeUnlocked;
            ModHook.OnNewGameStarted -= GunsmithStartSignHook.OnNewGameStarted;
            ModHook.OnNewGameStarted += GunsmithStartSignHook.OnNewGameStarted;
            ModHook.OnNewGameStarted -= ManualStartHook.OnNewGameStarted;
            ModHook.OnNewGameStarted += ManualStartHook.OnNewGameStarted;
            Cabinets.DefineSettings();
            AddOn2077.DefineSettings();
            Phone.Register();
            Certificates.RegisterClients();
            GunsmithSign.RegisterClient();
            Reservations.Register();
            Contracts.Register();
            NewCustomers.Register();
            F5SnapshotSpec.Register();
        }

        // Called when the game quits (ModLoader.OnApplicationQuit) and when it stops the mod while it runs because of the trust list
        // (ModLoader.StopRunningMod); after its Steam guard trips the game stops the mod without calling this.
        public void OnDisable()
        {
            ReservationPickupLayout.Disable();
            ModHook.OnModItemDirectoryInit -= Items.OnItemDirectoryInit;
            ModHook.OnGameLoadedLate -= SaveRecord.OnGameLoaded;
            ModHook.OnGameLoadedLate -= Phone.OnGameLoaded;
            ModHook.OnGameLoadedLate -= GameGunLevels.OnGameLoaded;
            ModHook.OnGameLoadedLate -= Cabinets.OnGameLoaded;
            ModHook.OnNetworkUpgradeUnlocked -= RetiredGunsmithService.OnUpgradeUnlocked;
            ModHook.OnNetworkUpgradeUnlocked -= ManualServiceHook.OnUpgradeUnlocked;
            ModHook.OnNewGameStarted -= GunsmithStartSignHook.OnNewGameStarted;
            ModHook.OnNewGameStarted -= ManualStartHook.OnNewGameStarted;
        }

        internal static void Log(string s)
        {
            UnityEngine.Debug.Log(LogPrefix + s);
        }

        internal static void Warn(string s)
        {
            UnityEngine.Debug.LogWarning(LogPrefix + s);
        }
    }
}
