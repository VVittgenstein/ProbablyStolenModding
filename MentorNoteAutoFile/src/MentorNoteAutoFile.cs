using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;

[assembly: System.Reflection.AssemblyTitle("MentorNoteAutoFile")]
[assembly: System.Reflection.AssemblyVersion("1.1.0.0")]

namespace MentorNoteAutoFile
{
    // Entry point found by the game's ModLoader. The loader applies the Harmony patches in this
    // assembly (Harmony ID "mod.<manifest ID>") before calling Init and OnEnable.
    public sealed class MentorNoteAutoFileMod : IMod
    {
        internal const string LogPrefix = "[MentorNoteAutoFile] ";

        public void Init(ModManifest manifest)
        {
            UnityEngine.Debug.Log(LogPrefix + "v" + manifest.ModVersion + " loaded");
        }

        public void OnEnable()
        {
        }

        public void OnDisable()
        {
        }
    }

    // A mentor note being dragged, with the place it was dragged from.
    internal sealed class DraggedNote
    {
        internal GameItem Note;
        internal GameInventory Source;
        internal GameItem SourceManual;

        internal static DraggedNote Capture(GameItem item)
        {
            if (item == null || item.identifier != NoteRouting.NoteId)
                return null;
            GameInventory source = item.parentInventory;
            return new DraggedNote
            {
                Note = item,
                Source = source,
                SourceManual = source != null ? source.GetParentItem() : null,
            };
        }
    }

    // Single-item drag. ItemMouseDragHandler.EndDrag drops the dragged item into lastInventory (the grid
    // under the cursor, set only when no item is under the cursor) or onto lastItem (the item under the
    // cursor). The prefix retargets the drop to a mentor's manual lying directly in that space, so the
    // game's own "drop onto a container" branch files the note; the postfix files a note that still
    // landed next to a manual.
    [HarmonyPatch(typeof(ItemMouseDragHandler), "EndDrag")]
    internal static class EndDragPatch
    {
        private static void Prefix(GameItemElement ___currentItem, ref GameItem ___lastItem, ref GameInventory ___lastInventory, out DraggedNote __state)
        {
            __state = null;
            try
            {
                __state = DraggedNote.Capture(___currentItem);
                if (__state == null)
                    return;

                DropLog.Info("drop of " + DropLog.Item(___currentItem) + " from " + DropLog.Inv(__state.Source)
                    + "; under cursor: item " + DropLog.Item(___lastItem) + ", grid " + DropLog.Inv(___lastInventory));
                GameItem manual = NoteRouting.FindTargetManual(__state, ___lastItem, ___lastInventory);
                if (manual == null)
                    return;

                ___lastItem = manual;
                ___lastInventory = null;
                DropLog.Info("drop retargeted to " + DropLog.Item(manual));
            }
            catch (Exception e)
            {
                DropLog.Warn("drag prefix left the drop to the game: " + e);
            }
        }

        private static void Postfix(DraggedNote __state)
        {
            NoteRouting.FileAfterDrop(__state);
        }
    }

    // Group drag of band-selected items. ItemMultiSelectHandler.EndGroupDrag moves each selected item into
    // the grid or container under the cursor; the postfix then files mentor notes that landed in a space
    // directly holding a mentor's manual.
    [HarmonyPatch(typeof(ItemMultiSelectHandler), "EndGroupDrag")]
    internal static class EndGroupDragPatch
    {
        private static void Prefix(List<GameItemElement> ___selectedItems, out List<DraggedNote> __state)
        {
            __state = null;
            try
            {
                if (___selectedItems == null)
                    return;
                foreach (GameItemElement element in ___selectedItems)
                {
                    DraggedNote captured = DraggedNote.Capture(element);
                    if (captured == null)
                        continue;
                    if (__state == null)
                        __state = new List<DraggedNote>();
                    __state.Add(captured);
                }
                if (__state != null)
                    DropLog.Info("group drop with " + __state.Count + " mentor note(s) among " + ___selectedItems.Count + " item(s)");
            }
            catch (Exception e)
            {
                DropLog.Warn("group prefix failed: " + e);
            }
        }

        private static void Postfix(List<DraggedNote> __state)
        {
            if (__state == null)
                return;
            foreach (DraggedNote dragged in __state)
                NoteRouting.FileAfterDrop(dragged);
        }
    }

    internal static class NoteRouting
    {
        // Item identifiers from MiscItemDirectory: TutorialPage (Mentor's Note) and TutorialBook (its manual).
        internal const string NoteId = "tutorial_page";
        internal const string ManualId = "tutorial_book";

        internal static GameItem FindTargetManual(DraggedNote dragged, GameItem hoveredItem, GameInventory hoveredInventory)
        {
            List<GameItem> spaceItems = ItemsInDropSpace(dragged.Note, hoveredItem, hoveredInventory);
            if (spaceItems == null)
                return null;
            DropLog.Info("drop space holds: " + DropLog.Ids(spaceItems));
            return FindManual(spaceItems, dragged);
        }

        // After the game has placed the note: a note that moved into a space directly holding a mentor's
        // manual is moved on into that manual with the same call the game uses for group drops.
        internal static void FileAfterDrop(DraggedNote dragged)
        {
            if (dragged == null)
                return;
            try
            {
                GameItem note = dragged.Note;
                GameItemElement element = note as GameItemElement;
                if (element != null && element.IsDestroyed())
                    return;

                GameInventory landed = note.parentInventory;
                if (landed == null || landed == dragged.Source)
                {
                    DropLog.Info("after drop " + DropLog.Item(note) + " is in " + DropLog.Inv(landed));
                    return;
                }
                if (!GeneralHelper.MayPlayerInsertInto(landed))
                    return;

                List<GameItem> spaceItems = landed.childItems;
                DropLog.Info("after drop " + DropLog.Item(note) + " landed in " + DropLog.Inv(landed) + ", which holds: " + DropLog.Ids(spaceItems));
                GameItem manual = FindManual(spaceItems, dragged);
                if (manual == null)
                    return;

                int moved = GraphUtils.TryAcceptAll(manual, note, -1);
                DropLog.Info("moved " + moved + " into " + DropLog.Item(manual) + "; note is now in " + DropLog.Inv(note.parentInventory));
            }
            catch (Exception e)
            {
                DropLog.Warn("after-drop filing failed: " + e);
            }
        }

        private static GameItem FindManual(List<GameItem> spaceItems, DraggedNote dragged)
        {
            if (spaceItems == null)
                return null;
            foreach (GameItem candidate in spaceItems)
            {
                if (candidate == null || candidate == dragged.Note || candidate.identifier != ManualId)
                    continue;
                // A note dragged out of a manual is not sent back into that same manual.
                if (candidate == dragged.SourceManual)
                {
                    DropLog.Info("skipped " + DropLog.Item(candidate) + ": the note came from it");
                    continue;
                }

                bool mayInsert = GeneralHelper.MayPlayerInsertInto(candidate);
                bool mayHold = mayInsert && candidate.MayHaveValidInventorySlot(dragged.Note);
                SlotMarker slot = mayHold ? candidate.TryFindOneValidInventorySlot(dragged.Note) : null;
                int transfer = slot != null ? slot.numTransfer : -1;
                DropLog.Info("checked " + DropLog.Item(candidate) + ": mayInsert=" + mayInsert + " mayHold=" + mayHold + " slotTransfer=" + transfer);
                if (transfer > 0)
                    return candidate;
            }
            return null;
        }

        // Items lying directly in the space the note is dropped into, or null when the drop stays with the game.
        private static List<GameItem> ItemsInDropSpace(GameItem note, GameItem hoveredItem, GameInventory hoveredInventory)
        {
            if (hoveredItem == null)
            {
                // Empty cell of a grid.
                if (hoveredInventory == null || !GeneralHelper.MayPlayerInsertInto(hoveredInventory))
                    return null;
                return hoveredInventory.childItems;
            }

            // Onto a manual: the game already files the note there.
            if (hoveredItem.identifier == ManualId)
                return null;

            // Onto a container that takes the note: the space is the container's content.
            if (GeneralHelper.MayPlayerInsertInto(hoveredItem) && hoveredItem.MayHaveValidInventorySlot(note))
                return GraphUtils.FindChildrenType<GameItem>(hoveredItem, null, null);

            // The game stacks the note on, or uses it on, the item under the cursor.
            if (hoveredItem.MayStack(note) || (note.MayTarget(hoveredItem) && note.CanTarget(hoveredItem)))
                return null;

            // Over an item that does not take the note: the space is the grid holding that item.
            GameInventory around = hoveredItem.parentInventory;
            if (around == null || !GeneralHelper.MayPlayerInsertInto(around))
                return null;
            return around.childItems;
        }
    }

    // Diagnostic lines in Player.log, written only while a mentor note is dropped.
    internal static class DropLog
    {
        internal static void Info(string message)
        {
            UnityEngine.Debug.Log(MentorNoteAutoFileMod.LogPrefix + message);
        }

        internal static void Warn(string message)
        {
            UnityEngine.Debug.LogWarning(MentorNoteAutoFileMod.LogPrefix + message);
        }

        internal static string Item(GameItem item)
        {
            return item == null ? "none" : item.identifier + " '" + item.name + "'";
        }

        internal static string Inv(GameInventory inventory)
        {
            if (inventory == null)
                return "none";
            GameItem owner = inventory.GetParentItem();
            List<GameItem> items = inventory.childItems;
            return inventory.GetType().Name + " of " + (owner == null ? "store" : owner.identifier)
                + " (" + (items == null ? 0 : items.Count) + " items)";
        }

        internal static string Ids(List<GameItem> items)
        {
            if (items == null || items.Count == 0)
                return "nothing";
            StringBuilder text = new StringBuilder();
            foreach (GameItem item in items)
            {
                if (text.Length > 0)
                    text.Append(", ");
                text.Append(item == null ? "null" : item.identifier);
            }
            return text.ToString();
        }
    }
}
