using System;
using System.Collections.Generic;

namespace F5ForceSave
{
    // 1.8.0: key combinations such as Ctrl+F5. A key bind of the game holds one key: ModHelper.AddGameOptionKeyBind takes a
    // Func<KeyCode> and an Action<KeyCode>, and KeyBindHelper.PollCapture takes the first key pressed, so a modifier pressed
    // first would itself become the key. Each of the three keys therefore has a modifier of its own (None, Ctrl, Shift or
    // Alt): an entry of the mod configuration ("save_key_modifier", "load_key_modifier" and "f5_load_key_modifier" in
    // ModConfigs\F5ForceSave.json), a row under the key's row in the Key Binds section of the game options, and an entry in
    // this mod's settings window. A key acts only while exactly its modifier is held; with None, only while none of Ctrl,
    // Shift and Alt is held, so F5 and Ctrl+F5 can be given to different actions. A modifier key used as the key itself does
    // not count as held.
    internal enum KeyModifier
    {
        None,
        Ctrl,
        Shift,
        Alt,
    }

    internal sealed class ModifierSetting
    {
        private const string Section = "按键 / Key / Клавиши";
        // The values in the configuration file, in the order of KeyModifier.
        private static readonly string[] Names = { "None", "Ctrl", "Shift", "Alt" };

        private readonly string configKey;
        private readonly Func<string> settingLabel;
        private readonly Func<string> optionLabel;
        private ConfigEntry<string> entry;

        internal KeyModifier Current = KeyModifier.None;

        internal ModifierSetting(string configKey, Func<string> settingLabel, Func<string> optionLabel)
        {
            this.configKey = configKey;
            this.settingLabel = settingLabel;
            this.optionLabel = optionLabel;
        }

        internal void Load()
        {
            ModConfig config = ModHelper.GetConfig(F5ForceSaveMod.ModId);
            entry = config.Define(configKey, Names[0], Section, settingLabel, (string[])Names.Clone(), Text.ModifierSettingTooltip);
            Apply(entry.Value);
            entry.OnChanged += name =>
            {
                Apply(name);
                UnityEngine.Debug.Log(F5ForceSaveMod.LogPrefix + configKey + " set to " + Current);
            };
            config.Save();
            UnityEngine.Debug.Log(F5ForceSaveMod.LogPrefix + configKey + " " + Current);
        }

        private void Apply(string name)
        {
            int index = Array.IndexOf(Names, name);
            if (index >= 0)
            {
                Current = (KeyModifier)index;
                return;
            }
            Current = KeyModifier.None;
            UnityEngine.Debug.LogWarning(F5ForceSaveMod.LogPrefix + "'" + name + "' is not a modifier this mod accepts (None, Ctrl, Shift, Alt); " + configKey + " is None");
        }

        private void Set(int index)
        {
            if (index < 0 || index >= Names.Length)
                return;
            if (entry != null)
                entry.Set(Names[index]); // OnChanged applies it; the game writes the file
            else
                Current = (KeyModifier)index;
        }

        // A row of the Key Binds section, added right after the key's own row.
        internal void AddOption()
        {
            ModHelper.AddGameOptionDropdown(F5ForceSaveMod.ModId, GameOptionRegistry.SECTION_KEYBIND, configKey, optionLabel,
                Text.ModifierChoices, () => (int)Current, Set);
        }

        internal void RemoveOption()
        {
            ModHelper.RemoveGameOption(F5ForceSaveMod.ModId, configKey);
        }

        // The key as messages name it: the game's name of the key (KeyBindHelper.KeyName), after "Ctrl+" and the like.
        internal string Name(UnityEngine.KeyCode key)
        {
            string name = KeyBindHelper.KeyName(key);
            return Current == KeyModifier.None ? name : Names[(int)Current] + "+" + name;
        }

        internal bool Pressed(UnityEngine.KeyCode key)
        {
            return UnityEngine.Input.GetKeyDown(key) && Holds(Current, key);
        }

        internal static bool Holds(KeyModifier wanted, UnityEngine.KeyCode key)
        {
            bool ctrl = Held(UnityEngine.KeyCode.LeftControl, UnityEngine.KeyCode.RightControl, key);
            bool shift = Held(UnityEngine.KeyCode.LeftShift, UnityEngine.KeyCode.RightShift, key);
            bool alt = Held(UnityEngine.KeyCode.LeftAlt, UnityEngine.KeyCode.RightAlt, key)
                || (key != UnityEngine.KeyCode.AltGr && UnityEngine.Input.GetKey(UnityEngine.KeyCode.AltGr));
            switch (wanted)
            {
                case KeyModifier.Ctrl: return ctrl && !shift && !alt;
                case KeyModifier.Shift: return shift && !ctrl && !alt;
                case KeyModifier.Alt: return alt && !ctrl && !shift;
                default: return !ctrl && !shift && !alt;
            }
        }

        private static bool Held(UnityEngine.KeyCode left, UnityEngine.KeyCode right, UnityEngine.KeyCode key)
        {
            if (key == left || key == right)
                return false;
            return UnityEngine.Input.GetKey(left) || UnityEngine.Input.GetKey(right);
        }
    }
}
