using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace F5ForceSave
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
            if (!SaveSettings.ExcludeManualOnCopy || QuickHistory.HandledByPlus()) return true;
            __result = -1;
            try
            {
                if (!ready) throw new InvalidOperationException("the original copier's input calls no longer match");
                SavePreviewData preview = SaveUIManager.GetPreview(slotId);
                current = Build(slotId, preview, SaveFiles.LoadHistory(slotId),
                    (file, day, time) =>
                    {
                        var record = SaveDetails.Read(file, day);
                        // An unreadable file cannot be established as automatic.
                        return !record.Readable || record.Type == SaveDetails.Kind.Quick;
                    });
                if (current != null)
                {
                    UnityEngine.Debug.Log(F5ForceSaveMod.LogPrefix + "copy exclusion active: slot " + slotId + ", source " + current.Source + ", automatic history " + current.History.Count);
                    return true;
                }
                UnityEngine.Debug.LogWarning(F5ForceSaveMod.LogPrefix + "copy skipped: no readable automatic save or no slot preview; the source slot is unchanged");
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning(F5ForceSaveMod.LogPrefix + "copy without manual saves failed: " + e.Message);
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
            Func<string, int, DateTime, bool> isExcluded)
        {
            if (preview == null) return null;
            var autos = new List<SaveHistoryEntry>();
            foreach (SaveHistoryEntry entry in history ?? new List<SaveHistoryEntry>())
                if (entry != null && !isExcluded(entry.file, entry.dayCount, entry.saveDateTime)) autos.Add(entry);
            autos.Sort((a, b) => b.saveDateTime.CompareTo(a.saveDateTime));
            var plan = new Plan { Slot = slotId, Source = SaveFiles.Slot(slotId), Preview = preview.CloneForSlot(slotId), History = autos };
            if (!isExcluded(plan.Source, preview.dayCount, preview.lastSaveDateTime)) return plan;
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

        private static string SourceFile(int slotId) { return current != null && current.Slot == slotId ? current.Source : SaveFiles.Slot(slotId); }
        private static SavePreviewData Preview(string key, SavePreviewData fallback)
        {
            return current != null ? current.Preview : SaveFiles.IndexLoad<SavePreviewData>(key, fallback);
        }
        private static List<SaveHistoryEntry> History(int slotId)
        {
            return current != null && current.Slot == slotId ? current.History : SaveFiles.LoadHistory(slotId);
        }

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
            // Replace operands only, preserving branch labels and exception blocks.
            codes[sourceIndex].operand = AccessTools.Method(typeof(CopyHistory), nameof(SourceFile));
            codes[previewIndex].operand = AccessTools.Method(typeof(CopyHistory), nameof(Preview));
            codes[historyIndex].operand = AccessTools.Method(typeof(CopyHistory), nameof(History));
            return codes;
        }
    }
}
