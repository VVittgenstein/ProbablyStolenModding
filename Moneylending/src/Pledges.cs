using System;
using System.Collections.Generic;

namespace Moneylending
{
    // Pledges and I.O.U. notes are ordinary game items carrying a tag with the loan's number. Item tags are
    // written into the save with the item, so they are found again after a reload. A pledge whose loan was
    // not repaid keeps the tag with the negative number: it is the player's to keep or sell.
    internal static class Pledges
    {
        internal const string PledgeTag = "MONEYLENDING_PLEDGE";
        internal const string IouTag = "MONEYLENDING_IOU";

        internal static void Mark(GameItem item, int loanId)
        {
            SetTag(item, PledgeTag, loanId);
        }

        private static void SetTag(GameItem item, string tag, int value)
        {
            item.EnableTag(tag, false);
            item.ModifyTag(tag, s => s.SetInt(value), false);
        }

        private static int GetTag(GameItem item, string tag)
        {
            return item != null && item.IsTag(tag) ? item.GetTagReadonly(tag).GetInt() : 0;
        }

        // The pledges of a loan the player still has.
        internal static List<GameItem> FindAll(int loanId)
        {
            var found = new List<GameItem>();
            if (!Game.InRun)
                return found;
            foreach (GameItem item in PlayerStore.Instance.FindAllItem(true))
            {
                if (GetTag(item, PledgeTag) == loanId)
                    found.Add(item);
            }
            return found;
        }

        internal static GameItem FindIou(int loanId)
        {
            return FindTagged(IouTag, loanId);
        }

        private static GameItem FindTagged(string tag, int value)
        {
            if (!Game.InRun)
                return null;
            foreach (GameItem item in PlayerStore.Instance.FindAllItem(true))
            {
                if (GetTag(item, tag) == value)
                    return item;
            }
            return null;
        }

        // False when the player no longer has any of the pledges (sold or thrown away before the loan ended).
        internal static bool Forfeit(Loan loan, out string names, out int value)
        {
            var itemNames = new List<string>();
            value = 0;
            foreach (GameItem item in FindAll(loan.id))
            {
                SetTag(item, PledgeTag, -loan.id);
                itemNames.Add(item.name);
                value += (int)Math.Max(0, item.unitValue);
            }
            names = Text.ItemList(itemNames);
            return itemNames.Count > 0;
        }

        // The borrower's I.O.U., written like Georgio's (a handnote in his handwriting font) and put on the
        // counter as the player's own item.
        internal static void GiveIou(Loan loan)
        {
            try
            {
                Game.PutOnCounter(() =>
                {
                    GameItem note = PreBuiltItemHelper.CreateHandnote(Text.IouBody(loan.name, loan.total, loan.dueDay), Text.IouTitle(), Text.IouSign(loan.name), 2);
                    if (note != null)
                        SetTag(note, IouTag, loan.id);
                    return note;
                });
            }
            catch (Exception e)
            {
                MoneylendingMod.Warn("the I.O.U. for loan #" + loan.id + " could not be written: " + e.Message);
            }
        }

        // The borrower takes the note back when the loan is repaid, wherever the player keeps it.
        internal static void TakeBackIou(Loan loan)
        {
            GiveBack(FindIou(loan.id));
        }

        // An item leaves the store with the customer at the counter: it is put on the counter as not the player's,
        // and the game removes it when the customer leaves (PlayerStore.RemoveNotOwnedItemFromTable), as it does
        // with every customer's unsold goods, so the player sees it go. False when neither counter has room; the
        // item then stays where it is.
        internal static bool GiveBack(GameItem item)
        {
            EmporiumEntry entry = EmporiumEntry.Instance;
            if (item == null || entry == null)
                return false;
            if (!OnCounter(entry, item))
                ModHelper.MoveItemToFrontCounter(item);
            if (!OnCounter(entry, item))
                ModHelper.MoveItemToBackCounter(item);
            if (!OnCounter(entry, item))
                return false;
            GeneralHelper.SetItemOwned(item, false);
            return true;
        }

        internal static bool OnCounter(EmporiumEntry entry, GameItem item)
        {
            return entry.frontInvinvElement.childItems.Contains(item) || entry.backInvinvElement.childItems.Contains(item);
        }

        // Directly on the store shelves or on a counter, not inside a container. Lenders take only such goods; whether
        // moving an item out of a closed container works in the game's grid code was not tested.
        internal static bool OnShelfOrCounter(EmporiumEntry entry, GameItem item)
        {
            if (item == null || item.parents == null)
                return false;
            foreach (GraphNodeStorage parent in item.parents)
            {
                if (ReferenceEquals(parent, entry.invElement) || ReferenceEquals(parent, entry.frontInvinvElement) || ReferenceEquals(parent, entry.backInvinvElement))
                    return true;
            }
            return false;
        }

        internal static bool IsActivePledge(GameItem item)
        {
            return item != null && item.IsTag(PledgeTag) && GetTag(item, PledgeTag) > 0;
        }

        // A pledge that could not be handed back stays with the player as an ordinary item.
        internal static void Unmark(GameItem item)
        {
            if (item != null && item.IsTag(PledgeTag))
                SetTag(item, PledgeTag, 0);
        }

        internal static void OnTooltip(RichTextBuilder builder, GameItem item)
        {
            if (builder == null || item == null || !item.IsTag(PledgeTag))
                return;
            int id = GetTag(item, PledgeTag);
            Loan loan = id != 0 ? Book.FindLoan(Math.Abs(id)) : null;
            if (loan == null)
                return;
            builder.AddLine(id > 0 && loan.IsActive ? Text.PledgeTooltip(loan.name, loan.id, loan.Outstanding) : Text.ForfeitTooltip(loan.name));
        }
    }
}
