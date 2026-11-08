using System;

namespace F5ForceSave
{
    // "客户需求高亮 Client Demand Highlight" (Steam Workshop item 3828938065) switches its display with F5 by default, the
    // same default as the save key, so one press saves and also switches the highlight. Players reported this as the
    // highlight failing. While that mod runs and its key equals the save key, the message of a save key press says so.
    // Its key is read from its row in the Key Binds section of the game options (the row's source is the mod's ID), so
    // nothing is said once either key has been changed. Without such a row its key is taken to be F5.
    // "Always Highlight" (Steam Workshop item 3829191625) opens its settings with F7 by default, the same default as the
    // key that loads the last F5 save. Its page says the key can be changed in its own settings; the mod is not installed
    // here, so whether it has a row in the Key Binds section has not been checked. Without a row its key is taken to be F7.
    internal static class KeyConflict
    {
        private const ulong HighlightWorkshopId = 3828938065UL;
        private const ulong AlwaysHighlightWorkshopId = 3829191625UL;

        internal static bool WithHighlight(UnityEngine.KeyCode key)
        {
            return With(HighlightWorkshopId, UnityEngine.KeyCode.F5, key);
        }

        internal static bool WithAlwaysHighlight(UnityEngine.KeyCode key)
        {
            return With(AlwaysHighlightWorkshopId, UnityEngine.KeyCode.F7, key);
        }

        private static bool With(ulong workshopId, UnityEngine.KeyCode defaultKey, UnityEngine.KeyCode key)
        {
            try
            {
                string id = RunningModId(workshopId);
                if (id == null)
                    return false;
                bool hasRow = false;
                foreach (GameOptionRegistry.Section section in GameOptionRegistry.Sections)
                {
                    foreach (GameOptionRegistry.Entry entry in section.entries)
                    {
                        if (entry == null || entry.kind != GameOptionRegistry.Kind.KeyBind || entry.source != id || entry.getKey == null)
                            continue;
                        hasRow = true;
                        if (entry.getKey() == key)
                            return true;
                    }
                }
                return !hasRow && key == defaultKey;
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning(F5ForceSaveMod.LogPrefix + "checking another mod's key failed: " + e);
                return false;
            }
        }

        private static string RunningModId(ulong workshopId)
        {
            if (ModLoader.Mods == null)
                return null;
            foreach (LoadedMod mod in ModLoader.Mods)
            {
                if (mod != null && mod.Instance != null && mod.Manifest != null
                    && (mod.WorkshopId == workshopId || mod.ShadowedWorkshopId == workshopId))
                    return mod.Manifest.ID;
            }
            return null;
        }
    }

    // Shows a message and the sentence about a key conflict. ReputationUIManager.Notify has one text, and the notification grows
    // with it without a width limit, so a line wider than the screen runs off both sides. On the 1920 units wide CanvasScreen the
    // bar is the text's preferred width plus 40, at scale 2, which leaves 920 for the text. The two stay on one line unless that
    // line is too wide.
    internal static class Notice
    {
        private const float WidestLine = 900f;

        internal static string Show(StoreUIManager ui, string message, string conflict)
        {
            string line = Text.SameLine(message, conflict);
            ui.Notify(line, "default");
            if (!TooWide())
                return line;
            string lines = message + "\n" + conflict;
            ui.Notify(lines, "default");
            return lines;
        }

        private static bool TooWide()
        {
            try
            {
                ReputationUIManager rep = ReputationUIManager.Instance;
                return rep != null && rep.reputationNotifierTMP != null && rep.reputationNotifierTMP.preferredWidth > WidestLine;
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning(F5ForceSaveMod.LogPrefix + "measuring the message failed: " + e);
                return false;
            }
        }
    }
}
