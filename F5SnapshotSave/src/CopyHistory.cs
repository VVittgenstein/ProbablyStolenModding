using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace F5SnapshotSave
{
    // Adapt three inputs of the original copier. It still creates/registers the
    // slot, copies the bytes, retries I/O and records history in its usual way.
    [HarmonyPatch(typeof(SaveFiles), nameof(SaveFiles.DuplicateSlot))]
    internal static class CopyHistory
    {
        internal sealed class Plan
        {
            internal int Slot;
            internal string Source;
            internal SavePreviewData Preview;
            internal List<SaveHistoryEntry> History;
        }
        private static Plan current;
        private static bool ready;

        private static bool Prefix(int slotId, ref int __result, out Plan __state)
        {
            __state = current;
            current = null;
            if (!SaveSettings.ExcludeManualOnCopy || !QuickHistory.Handles()) return true;
            __result = -1;
            try
            {
                if (!ready) throw new InvalidOperationException("the original copier's input calls no longer match");
                SavePreviewData preview = SaveUIManager.GetPreview(slotId);
                // 0.7.0, as F5ForceSave 1.8.0: an F5 save is told by the mark in its save time (QuickMark), or for one made before 1.8.0 by the
                // list of earlier versions; the pull request read the save files, which needs the files capability.
                HashSet<long> legacy = QuickHistory.LoadLegacy();
                current = Build(slotId, preview, SaveFiles.LoadHistory(slotId), time => QuickHistory.IsQuick(time, legacy));
                if (current != null)
                {
                    UnityEngine.Debug.Log(F5SnapshotSaveMod.LogPrefix + "copy exclusion active: slot " + slotId + ", source " + current.Source + ", automatic history " + current.History.Count);
                    return true;
                }
                UnityEngine.Debug.LogWarning(F5SnapshotSaveMod.LogPrefix + "copy skipped: no readable automatic save or no slot preview; the source slot is unchanged");
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning(F5SnapshotSaveMod.LogPrefix + "copy without manual saves failed: " + e.Message);
            }
            // OnDuplicateClicked displays the game's existing copy-failed dialog.
            return false;
        }

        private static Exception Finalizer(Plan __state, Exception __exception)
        {
            current = __state;
            return __exception;
        }

        internal static Plan Build(int slotId, SavePreviewData preview, List<SaveHistoryEntry> history,
            Func<DateTime, bool> isExcluded)
        {
            if (preview == null) return null;
            var autos = new List<SaveHistoryEntry>();
            foreach (SaveHistoryEntry entry in history ?? new List<SaveHistoryEntry>())
                if (entry != null && !isExcluded(entry.saveDateTime)) autos.Add(entry);
            autos.Sort((a, b) => b.saveDateTime.CompareTo(a.saveDateTime));
            var plan = new Plan { Slot = slotId, Source = SaveFiles.Slot(slotId), Preview = preview.CloneForSlot(slotId), History = autos };
            if (!isExcluded(preview.lastSaveDateTime)) return plan;
            if (autos.Count == 0) return null;
            SaveHistoryEntry latestAuto = autos[0];
            plan.Source = latestAuto.file;
            plan.Preview.dayCount = latestAuto.dayCount;
            plan.Preview.credit = latestAuto.credit;
            plan.Preview.lastSaveDateTime = latestAuto.saveDateTime;
            plan.Preview.version = latestAuto.version;
            autos.RemoveAt(0);
            return plan;
        }

        // Called right after the game's three calls: while this mod's copy is in progress (set by Prefix for this slot),
        // the plan's value replaces the game's value; otherwise the game's value passes through unchanged.
        private static string SourceFile(string slotFile) { return current != null ? current.Source : slotFile; }
        private static SavePreviewData Preview(SavePreviewData indexed) { return current != null ? current.Preview : indexed; }
        private static List<SaveHistoryEntry> History(List<SaveHistoryEntry> loaded) { return current != null ? current.History : loaded; }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            MethodInfo slot = AccessTools.Method(typeof(SaveFiles), nameof(SaveFiles.Slot));
            MethodInfo preview = AccessTools.Method(typeof(SaveFiles), nameof(SaveFiles.IndexLoad), generics: new[] { typeof(SavePreviewData) });
            MethodInfo history = AccessTools.Method(typeof(SaveFiles), nameof(SaveFiles.LoadHistory));
            int sourceIndex = -1, previewIndex = -1, historyIndex = -1, previewCount = 0, historyCount = 0;
            for (int i = 0; i < codes.Count; i++)
            {
                if (sourceIndex < 0 && codes[i].Calls(slot)) sourceIndex = i;
                if (codes[i].Calls(preview)) { previewIndex = i; previewCount++; }
                if (codes[i].Calls(history)) { historyIndex = i; historyCount++; }
            }
            ready = sourceIndex >= 0 && sourceIndex < previewIndex && previewIndex < historyIndex && previewCount == 1 && historyCount == 1;
            if (!ready) return codes;
            // F5ForceSave and F5 Quick Save Plus both patch this method, and Harmony applies their transpilers one after the
            // other. Each keeps the game's calls, which the other looks for, and inserts its own call right after each one
            // (nothing is inserted before a call, so branch labels and exception blocks stay where they are). Inserting
            // from the last position keeps the earlier positions valid.
            codes.Insert(historyIndex + 1, new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(CopyHistory), nameof(History))));
            codes.Insert(previewIndex + 1, new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(CopyHistory), nameof(Preview))));
            codes.Insert(sourceIndex + 1, new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(CopyHistory), nameof(SourceFile))));
            return codes;
        }
    }
}
