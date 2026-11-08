using System;
using System.Collections.Generic;

namespace Moneylending
{
    internal enum Outcome
    {
        Undecided = 0,
        OnTime = 1,
        Partial = 2,
        Late = 3,
        Goods = 4,
        Default = 5,
        Arrested = 6,
        Bonus = 7,
    }

    internal enum LoanStatus
    {
        Active = 0,
        Repaid = 1,
        Defaulted = 2,
        Arrested = 3,
        Collected = 4,
    }

    internal enum DebtStatus
    {
        Active = 0,
        PaidOff = 1,
        Seized = 2,
    }

    // A loan the player made. The borrower is remembered by the name and sprite shown at the time, since the
    // game's generic customers are created anew on every visit and have no identity of their own.
    public sealed class Loan
    {
        public int id;
        public string profile;
        public string name;
        public string sprite;
        public string faction;
        public bool atCounter;
        public bool wasAug;
        public int variant;
        public int principal;
        public int rate;
        public bool raised;
        public bool interestFree;
        public int total;
        public int paid;
        public int dayLent;
        public int dueDay;
        public int visitDay = -1;
        public int lastVisitDay = -1;
        public bool partialDone;
        public int outcome;
        public int status;
        public int closedDay = -1;
        public bool hasCollateral;
        public string collateralName;
        public int collateralValue;
        public int collateralCount;
        public bool collateralKept;
        public int sawyerDay = -1;
        public bool sawyerOffered;
        public int sawyerHiredDay = -1;
        public int sawyerFee;
        public int recovered;

        internal int Outstanding
        {
            get { return Math.Max(0, total - paid); }
        }

        internal bool IsActive
        {
            get { return status == (int)LoanStatus.Active; }
        }

        internal Outcome Result
        {
            get { return (Outcome)outcome; }
        }
    }

    // Money the player borrowed: "shark" (the Loan Shark, one payment after a week) or "bank" (the First Bank's
    // loan clerk, weekly installments).
    public sealed class Debt
    {
        public int id;
        public string lender;
        public string sprite;
        public int principal;
        public int total;
        public int installment;
        public int installmentsLeft;
        public int dayBorrowed;
        public int dueDay;
        public int lastVisitDay = -1;
        public int paid;
        public int seizedValue;
        public int status;
        public int closedDay = -1;

        internal bool IsActive
        {
            get { return status == (int)DebtStatus.Active; }
        }

        internal int Outstanding
        {
            get { return Math.Max(0, total - paid); }
        }

        // The amount asked at the next visit: the whole rest for the Loan Shark, one installment for the bank
        // (the last installment takes whatever rounding left over).
        internal int AmountDue
        {
            get { return lender == Debts.Bank && installmentsLeft > 1 ? Math.Min(installment, Outstanding) : Outstanding; }
        }
    }

    // The borrower who asks for a loan today. It is kept in the ledger so that a borrower recreated from the
    // waiting line (F5 Quick Save, or a reload) asks for the same loan with the same face.
    public sealed class Request
    {
        public int day = -1;
        public string profile;
        public string name;
        public string sprite;
        public int variant;
        public int amount;
        public int rate;
        public int term;
        public string collateralId;
        public int collateralValue;
        public bool answered;
    }

    public sealed class State
    {
        public int nextId = 1;
        public List<Loan> loans = new List<Loan>();
        public List<Debt> debts = new List<Debt>();
        public Request request = new Request();
        public int lent;
        public int interestEarned;
        public int lost;
        public int recovered;
        public int sawyerFees;
        public int lastBorrowerDay = -1000;
        public int lastSharkOfferDay = -1000;
        public int lastBankOfferDay = -1000;
    }

    // The ledger is kept in PlayerStore.modData (written into the save slot with the rest of the store) as one
    // string. It is read again whenever PlayerStore.Instance changes, which happens for every new game and every
    // loaded save; written after every change, so a save made at any time holds the current state.
    internal static class Book
    {
        private const string Key = "ledger";
        private static State state;
        private static object owner;

        internal static State S
        {
            get
            {
                object store = PlayerStore.HasInstance ? PlayerStore.Instance : null;
                if (state == null || !ReferenceEquals(owner, store))
                    Load();
                return state;
            }
        }

        // PlayerStore.Instance would create an empty store at the main menu, so it is only read when one exists.
        internal static void Load()
        {
            owner = PlayerStore.HasInstance ? PlayerStore.Instance : null;
            State loaded = null;
            string text = owner != null ? ModHelper.GetModData(MoneylendingMod.ModId, Key, null) : null;
            if (!string.IsNullOrEmpty(text))
            {
                try
                {
                    loaded = Codec.Decode(text);
                }
                catch (Exception e)
                {
                    MoneylendingMod.Warn("the ledger in this save could not be read and starts empty: " + e.Message);
                }
            }
            state = loaded ?? new State();
            if (state.loans == null) state.loans = new List<Loan>();
            if (state.debts == null) state.debts = new List<Debt>();
            if (state.request == null) state.request = new Request();
        }

        internal static void Save()
        {
            if (!Game.InRun)
                return;
            ModHelper.SetModData(MoneylendingMod.ModId, Key, Codec.Encode(S));
        }

        internal static int NextId()
        {
            return S.nextId++;
        }

        internal static Loan FindLoan(int id)
        {
            foreach (Loan loan in S.loans)
            {
                if (loan.id == id)
                    return loan;
            }
            return null;
        }

        internal static Debt FindDebt(int id)
        {
            foreach (Debt debt in S.debts)
            {
                if (debt.id == id)
                    return debt;
            }
            return null;
        }

        internal static Debt ActiveDebt(string lender)
        {
            foreach (Debt debt in S.debts)
            {
                if (debt.IsActive && debt.lender == lender)
                    return debt;
            }
            return null;
        }

        internal static int ActiveLoanCount()
        {
            int count = 0;
            foreach (Loan loan in S.loans)
            {
                if (loan.IsActive)
                    count++;
            }
            return count;
        }
    }
}
