using System;

namespace Moneylending
{
    // Morning: visitors whose day it is join the customers generated for the day (StoreClientManager.GenerateClient,
    // the last hook, so the game's own duplicate filter does not touch them). Night: the ledger moves on by one day
    // and writes what happened into the morning report.
    internal static class Daily
    {
        private const int BorrowerChance = 40;

        internal static void OnGenerateCustomers(StoreClientManager manager)
        {
            try
            {
                if (!Game.InRun || manager == null || !Unlock.IsUnlocked())
                    return;
                int day = Game.Day;
                State s = Book.S;

                foreach (Loan loan in s.loans)
                {
                    if (loan.IsActive && loan.visitDay == day && loan.lastVisitDay != day && Comes(loan.Result))
                        Clients.Insert(manager, Visits.CreateRepay(loan.id));
                    else if (loan.status == (int)LoanStatus.Defaulted && loan.sawyerDay == day && !loan.sawyerOffered && loan.sawyerHiredDay < 0
                        && !Game.SawyerDead)
                        Clients.Insert(manager, Visits.CreateSawyer(loan.id));
                }

                Debts.OnMorning(manager, day);

                int chance = Sign.InDisplayCase() ? Sign.BorrowerChance : BorrowerChance;
                if (s.lastBorrowerDay != day && Book.ActiveLoanCount() < Lending.MaxActiveLoans && RNG.Roll(chance))
                {
                    s.lastBorrowerDay = day;
                    Clients.Insert(manager, Visits.CreateBorrower());
                }
                Book.Save();
            }
            catch (Exception e)
            {
                MoneylendingMod.Warn("adding today's visitors failed: " + e);
            }
        }

        private static bool Comes(Outcome result)
        {
            return result == Outcome.OnTime || result == Outcome.Partial || result == Outcome.Late || result == Outcome.Goods || result == Outcome.Bonus;
        }

        // StoreStation.dayCounter is still the day that ended; the game saves after this, at the start of the next day.
        internal static void OnNight()
        {
            try
            {
                if (!Game.InRun)
                    return;
                int day = Game.Day;
                State s = Book.S;

                foreach (Loan loan in s.loans)
                {
                    if (loan.status == (int)LoanStatus.Defaulted)
                    {
                        if (loan.sawyerHiredDay == day)
                            Visits.ResolveSawyer(loan);
                        else if (loan.sawyerDay == day && !loan.sawyerOffered && loan.sawyerHiredDay < 0 && loan.sawyerDay - loan.closedDay < 3)
                            loan.sawyerDay = day + 1;
                        continue;
                    }
                    if (!loan.IsActive)
                        continue;

                    Profile p = Profiles.Get(loan.profile);
                    Outcome result = loan.Result;

                    // A borrower who was due today but did not get to the counter comes again tomorrow.
                    if (loan.visitDay == day && loan.lastVisitDay != day && Comes(result))
                        loan.visitDay = day + 1;

                    if (loan.dueDay == day && (result == Outcome.Late || result == Outcome.Default))
                        Game.NightLog(Text.NoShow(loan.name, loan.Outstanding), Game.Warning);

                    if (result == Outcome.Arrested && loan.dueDay <= day && p != null)
                        Lending.Arrest(loan, p);
                    else if (result == Outcome.Default && day >= loan.dueDay + Lending.GraceDays)
                        Lending.Default(loan, p);
                    else if (result == Outcome.Undecided && loan.dueDay <= day + 1)
                        Lending.Decide(loan, day);
                }

                Debts.OnNight(day);
                Book.Save();
            }
            catch (Exception e)
            {
                MoneylendingMod.Warn("the nightly ledger update failed: " + e);
            }
        }
    }
}
