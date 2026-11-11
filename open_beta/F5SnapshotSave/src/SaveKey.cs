using System;
using System.Collections.Generic;

namespace F5SnapshotSave
{
    // The save key, F5 unless the player changes it (the same handling as in F5ForceSave 1.3.0, with this mod's own
    // value). The value is an entry of the game's mod configuration (ModHelper.GetConfig), which the game keeps in
    // <persistentDataPath>\ModConfigs\F5SnapshotSave.json as the UnityEngine.KeyCode name under "save_key". Two
    // parts of the game's own UI change it: a row that this mod adds to the Key Binds section of the game options,
    // where the game waits for the next key press and refuses a key that another row uses, and this mod's settings
    // window in the Mods panel, a list of the accepted names. The file can also be edited while the game is closed;
    // the game replaces a name that is not in the list with F5 when it reads the file.
    internal static class SaveKey
    {
        internal const string ConfigKey = "save_key";
        internal const string OptionId = "save_key";
        private const string Section = "按键 / Key / Клавиши";
        private static ConfigEntry<string> entry;

        internal static UnityEngine.KeyCode Current = UnityEngine.KeyCode.F5;

        internal static void Load()
        {
            ModConfig config = ModHelper.GetConfig(F5SnapshotSaveMod.ModId);
            entry = config.Define(ConfigKey, UnityEngine.KeyCode.F5.ToString(), Section, Text.KeySettingLabel, Choices(), Text.KeySettingTooltip);
            Apply(entry.Value);
            entry.OnChanged += name =>
            {
                Apply(name);
                UnityEngine.Debug.Log(F5SnapshotSaveMod.LogPrefix + "save key set to " + Current);
            };
            // Written at every start, so the file exists and shows the key in effect for players who edit it by hand.
            config.Save();
            UnityEngine.Debug.Log(F5SnapshotSaveMod.LogPrefix + "save key " + Current + " (" + config.FilePath + ", entry " + ConfigKey + ")");
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
            UnityEngine.Debug.LogWarning(F5SnapshotSaveMod.LogPrefix + "'" + name + "' is not a key this mod accepts; the save key is F5");
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

        // The game's name for the key, as the Key Binds rows show it (F5, Left Shift, Numpad 0).
        internal static string Name()
        {
            return KeyBindHelper.KeyName(Current);
        }

        // The game's own options are registered first (GameOptionRegistry.EnsureVanilla, otherwise run when the
        // options window first opens), so the Key Binds section stays after the other sections and this row comes
        // after the game's own key rows.
        internal static void AddOption()
        {
            GameOptionRegistry.EnsureVanilla();
            ModHelper.AddGameOptionKeyBind(F5SnapshotSaveMod.ModId, GameOptionRegistry.SECTION_KEYBIND, OptionId, Text.KeyOptionLabel, () => Current, Set);
        }

        internal static void RemoveOption()
        {
            ModHelper.RemoveGameOption(F5SnapshotSaveMod.ModId, OptionId);
        }
    }
}
