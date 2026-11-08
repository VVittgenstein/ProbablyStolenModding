[assembly: System.Reflection.AssemblyTitle("GunsmithExpansion2077")]
[assembly: System.Reflection.AssemblyVersion("0.7.0.0")]

namespace GunsmithExpansion2077
{
    // The game's ModLoader takes a mod only with a listed DLL that has a class implementing IMod (ModLoader.IsWellFormed). This
    // class does nothing else: Gunsmith Expansion reads this mod's guns.tsv and pictures (its src/AddOn2077.cs). The DLL does not
    // reference GunsmithExpansion.dll, because the game's mod guide says a mod is refused when a listed DLL references an assembly
    // that is neither the game's nor listed by the same mod.
    public sealed class AddOnMod : IMod
    {
        public void Init(ModManifest manifest)
        {
            UnityEngine.Debug.Log("[GunsmithExpansion2077] v" + manifest.ModVersion + " loaded");
        }

        public void OnEnable()
        {
        }

        public void OnDisable()
        {
        }
    }
}
