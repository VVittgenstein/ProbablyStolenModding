using System;
using System.Collections.Generic;

namespace Moneylending
{
    // The ledger (L): loans, debts and totals, and the button that offers a loan to the customer at the counter.
    internal static class LedgerWindow
    {
        private const string Id = "moneylending_ledger";
        private const int ClosedShown = 12;
        private static StoreClient asked;

        internal static void Toggle()
        {
            CustomUIManager ui = CustomUIManager.Instance;
            if (ui == null)
                return;
            if (ui.IsOpen(Id))
                ui.CloseWindow(Id);
            else
                Open();
        }

        private static void Close()
        {
            CustomUIManager ui = CustomUIManager.Instance;
            if (ui != null && ui.IsOpen(Id))
                ui.CloseWindow(Id);
        }

        private static void Open()
        {
            State s = Book.S;
            int day = Game.Day;
            CustomUIBuilder b = CustomUIManager.Window(Id, Text.LedgerTitle());
            b.SetSize(760f, 560f).Center().SetCloseOnEscape(true);

            StoreClient client = Game.Current();
            string reason = OfferBlocked(client);
            b.AddButton(reason ?? Text.LedgerOffer(client.displayName), () =>
            {
                Close();
                StoreClient current = Game.Current();
                if (OfferBlocked(current) != null)
                    return;
                asked = current;
                Visits.OfferAtCounter(current);
            });
            if (reason != null)
                b.Last.SetInteractable(false);
            b.AddSeparator();

            b.BeginScrollFlexible(0f);
            b.AddHeader(Text.LedgerLoans());
            var active = new List<Loan>();
            var closed = new List<Loan>();
            foreach (Loan loan in s.loans)
                (loan.IsActive ? active : closed).Add(loan);
            active.Sort((x, y) => x.dueDay.CompareTo(y.dueDay));
            closed.Sort((x, y) => y.closedDay.CompareTo(x.closedDay));
            if (active.Count == 0 && closed.Count == 0)
                b.AddLabel(Text.LedgerNone());
            foreach (Loan loan in active)
                b.AddLabel(Text.LedgerLoanLine(loan, day));
            for (int i = 0; i < closed.Count && i < ClosedShown; i++)
                b.AddLabel(Text.LedgerLoanLine(closed[i], day));

            b.AddSpace(8f);
            b.AddHeader(Text.LedgerDebts());
            if (s.debts.Count == 0)
                b.AddLabel(Text.LedgerNone());
            for (int i = s.debts.Count - 1; i >= 0 && i >= s.debts.Count - ClosedShown; i--)
                b.AddLabel(Text.LedgerDebtLine(s.debts[i], day));

            b.AddSpace(8f);
            b.AddHeader(Text.LedgerTotals());
            b.AddLabel(Text.LedgerTotalsLine(s));
            b.End();

            b.AddSeparator();
            b.AddButton(Text.LedgerClose(), Close);
            b.Show();
        }

        // Null when a loan can be offered now; otherwise the text shown on the disabled button.
        private static string OfferBlocked(StoreClient client)
        {
            if (client == null)
                return Text.LedgerNoCustomer();
            if (ReferenceEquals(client, asked) || client.sourceModId == MoneylendingMod.ModId)
                return Text.LedgerOffered();
            DialogUIManager dialog = DialogUIManager.Instance;
            if (dialog == null || ConversationRunning(dialog))
                return Text.LedgerBusy();
            return null;
        }

        // A customer's conversation is still going when its line offers choices, has a next line, or has an effect
        // still to run (sellers put their goods on the counter at the end of a line). A last line that has played
        // out can stay current while the customer waits for the deal; offering a loan then replaces nothing.
        private static bool ConversationRunning(DialogUIManager dialog)
        {
            Dialogue d = dialog.GetCurrentDialog();
            if (d == null)
                return false;
            return d.choices.Count > 0 || d.nextDialogue != null || (d.endAction != null && !d.isEndActionExecuted);
        }
    }
}
