using System;
using System.Collections.Generic;

namespace Moneylending
{
    // Loan terms, the decision how a loan ends, and the bookkeeping when money changes hands.
    internal static class Lending
    {
        internal const int MaxActiveLoans = 8;
        internal const int GraceDays = 2;

        // Pledges (VVittgenstein 2026-11-06, after suigintou's Steam comment): worth 80% to 110% of the loan. Worth 80% they take 5 off the
        // chance that the borrower does not repay, worth 110% they take 20, in proportion between. The chance never goes below 5.
        internal const int MinPledgeShare = 80;
        internal const int MaxPledgeShare = 110;
        internal const int MinDefaultChance = 5;

        internal static int Total(int amount, int rate)
        {
            return amount + (int)Math.Round(amount * rate / 500.0) * 5;
        }

        internal static int DaysUntilSunday(int day)
        {
            int days = (7 - day % 7) % 7;
            return days == 0 ? 7 : days;
        }

        // ---- Requests -----------------------------------------------------------------------------------------

        internal static Profile PickProfile()
        {
            int total = 0;
            foreach (Profile p in Profiles.All)
                total += Weight(p);
            int roll = RNG.GetRandomInt(1, Math.Max(1, total));
            foreach (Profile p in Profiles.All)
            {
                roll -= Weight(p);
                if (roll <= 0)
                    return p;
            }
            return Profiles.Get(Profiles.Scav);
        }

        // People whose faction distrusts the store (reputation -40 or lower) do not come to borrow.
        private static int Weight(Profile p)
        {
            return Game.HasReputation(p.Faction) && Game.Reputation(p.Faction) <= -40 ? 0 : p.VisitWeight;
        }

        // The borrower who comes today. It is created once per day and kept in the ledger, so a borrower
        // recreated from the waiting line asks for the same loan.
        internal static Request TodayRequest()
        {
            State s = Book.S;
            int day = Game.Day;
            if (s.request == null || s.request.day != day || Profiles.Get(s.request.profile) == null)
            {
                Profile p = PickProfile();
                s.request = NewRequest(p, day, p.DisplayName(), p.Sprite(), true);
                Book.Save();
            }
            return s.request;
        }

        internal static Request NewRequest(Profile p, int day, string name, string sprite, bool mayPledge)
        {
            var r = new Request();
            r.day = day;
            r.profile = p.Id;
            r.name = name;
            r.sprite = sprite;
            int amount = RNG.GetRandomInt(p.MinAmount, p.MaxAmount) + day * p.AmountPerTenDays / 10;
            r.amount = Game.RoundTo(amount, amount >= 300 ? 50 : 10);
            // The range is the player's (Settings.RateRange), Profile.MinRate to MaxRate unless changed.
            int minRate, maxRate;
            Settings.RateRange(p, out minRate, out maxRate);
            r.rate = p.FreeChance > 0 && RNG.Roll(p.FreeChance) ? 0 : RNG.GetRandomInt(minRate, maxRate);
            r.term = p.MaxTerm == 0 ? DaysUntilSunday(day) : RNG.GetRandomInt(p.MinTerm, p.MaxTerm);

            bool pledge = mayPledge && p.CollateralItems != null && p.CollateralItems.Length > 0
                && (p.CollateralChance >= 100 || RNG.Roll(p.Id == Profiles.Merchant ? 33 : p.CollateralChance));
            if (p.Id == Profiles.Merchant)
                r.variant = pledge ? 2 : RNG.GetRandomInt(0, 1);
            else
                r.variant = RNG.GetRandomInt(0, 2);
            if (pledge)
            {
                int value;
                List<string> items = Items.PickPledge(p.CollateralItems, r.amount, RNG.GetRandomInt(MinPledgeShare, MaxPledgeShare), out value);
                if (items.Count > 0)
                {
                    r.collateralId = string.Join(",", items.ToArray());
                    r.collateralValue = value;
                }
            }
            return r;
        }

        // ---- Making a loan ------------------------------------------------------------------------------------

        // Pays the borrower and writes the loan into the ledger. The pledges, when there are any, already lie on
        // the counter (not owned); they become the player's to hold, marked with the loan's number.
        internal static Loan Make(StoreClient client, Profile p, Request r, int rate, bool raised, bool interestFree, bool atCounter, List<GameItem> pledges)
        {
            PlayerStore store = PlayerStore.Instance;
            if (store.playerCash < r.amount)
            {
                MoneylendingMod.Warn("not enough cash for the loan of " + r.amount);
                return null;
            }
            store.ModCash(-r.amount, true);

            var loan = new Loan();
            loan.id = Book.NextId();
            loan.profile = p.Id;
            loan.name = r.name;
            loan.sprite = r.sprite;
            loan.faction = p.Faction;
            loan.atCounter = atCounter;
            loan.wasAug = client != null && client.isAug;
            loan.variant = r.variant;
            loan.principal = r.amount;
            loan.rate = rate;
            loan.raised = raised;
            loan.interestFree = interestFree;
            loan.total = Total(r.amount, rate);
            loan.dayLent = Game.Day;
            loan.dueDay = loan.dayLent + Math.Max(1, r.term);

            // The pledges were put on the counter when the borrower arrived.
            if (pledges != null)
            {
                var names = new List<string>();
                foreach (GameItem pledge in pledges)
                {
                    GeneralHelper.SetItemOwned(pledge, true);
                    Pledges.Mark(pledge, loan.id);
                    if (!Game.IsInStore(pledge))
                        continue;
                    names.Add(pledge.name);
                    loan.collateralValue += (int)Math.Max(0, pledge.unitValue);
                    loan.collateralCount++;
                }
                loan.hasCollateral = loan.collateralCount > 0;
                if (loan.hasCollateral)
                    loan.collateralName = Text.ItemList(names);
            }

            // A customer at the counter who came to buy can spend what they borrowed in the store.
            if (atCounter && client != null && client.useClientBudget)
                client.ModBudget(r.amount);

            State s = Book.S;
            s.loans.Add(loan);
            s.lent += loan.principal;
            r.answered = true;
            Clients.RegisterLoan(loan.id);
            Pledges.GiveIou(loan);

            if (interestFree && p.RepOnGentle != 0)
                Game.ModReputation(p.Faction, p.RepOnGentle);
            else if (raised && p.RepOnRaise != 0)
                Game.ModReputation(p.Faction, p.RepOnRaise);
            else if (p.RepOnLend != 0)
                Game.ModReputation(p.Faction, p.RepOnLend);
            string waived = interestFree && r.rate > 0 ? CreditWaived(p, Total(r.amount, r.rate) - r.amount) : null;

            Book.Save();
            // One notification: a second one right after it would replace it on screen.
            Game.Notify(Text.Lent(loan.principal, loan.name) + waived);
            MoneylendingMod.Log("loan #" + loan.id + " (" + p.Id + ") " + loan.principal + " at " + rate + "%, total " + loan.total + ", due day " + loan.dueDay + (loan.hasCollateral ? ", pledge " + loan.collateralName : ""));
            return loan;
        }

        // Interest the player gives up earns reputation with the borrower's faction: one point per 20 Credits, at most
        // 5 for one loan. The game's own trades go through the same StoreReputation.ModReputation(double), which keeps
        // fractions and applies the difficulty and the perks (a trade earns its value / 400). The static
        // ModReputation(string, int, bool) is not used here: it takes whole points, and each Lower Level point it
        // gives also adds 40 to Wild's favor, which would pay back more than the interest given up.
        internal const int WaiveCreditsPerRep = 20;
        internal const double WaiveRepCap = 5.0;

        internal static double WaiveReputation(int forgone)
        {
            return Math.Min(WaiveRepCap, forgone / (double)WaiveCreditsPerRep);
        }

        // Returns the text to add to the notification of the loan, or null.
        private static string CreditWaived(Profile p, int forgone)
        {
            if (!p.CanWaive || forgone <= 0)
                return null;
            StoreReputation rep = StoreReputation.GetStoreReputation(p.Faction);
            if (rep == null)
                return null;
            double before = rep.GetReputationExact();
            rep.ModReputation(WaiveReputation(forgone));
            double gained = rep.GetReputationExact() - before;
            MoneylendingMod.Log("no interest on " + forgone + " Credits: " + p.Faction + " reputation +" + gained.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
            return Text.WaivedInterest(StoreReputation.GetFactionNameByID(p.Faction), gained);
        }

        // Chance that the borrower accepts a higher rate: like the game's bargaining, reputation with the
        // borrower's faction helps and a bad reputation counts double.
        internal static int RaiseChance(Profile p)
        {
            int rep = Game.Reputation(p.Faction);
            int chance = p.RaiseBase + (rep >= 0 ? rep / 6 : rep / 3);
            return Math.Max(5, Math.Min(95, chance));
        }

        // ---- How a loan ends ------------------------------------------------------------------------------------

        internal static int DefaultChance(Loan loan, Profile p)
        {
            int chance = p.DefaultBase;
            int rep = Game.Reputation(p.Faction);
            chance -= rep >= 0 ? rep / 6 : rep / 3;
            if (loan.raised)
                chance += p.RaiseStep / 2;
            if (loan.interestFree && p.CanWaive)
                chance -= 3;
            if (loan.hasCollateral)
                chance -= PledgeReduction(loan.collateralValue, loan.principal);
            int repaid, defaulted, collected;
            History(p.Id, loan.id, out repaid, out defaulted, out collected);
            chance += Math.Min(20, defaulted * 5);
            chance -= Math.Min(10, repaid * 2 + collected * 3);
            if (p.Id == Profiles.Rev && News.CrackdownActive())
                chance += 15;
            if (p.Id == Profiles.Thief && News.GangWar())
                chance += 10;
            if (loan.partialDone)
                chance /= 2;
            return Math.Max(MinDefaultChance, Math.Min(85, chance));
        }

        // A smaller share (a loan made before 1.4.0, or pledges that did not all fit on the counter) counts as 80%.
        internal static int PledgeReduction(int value, int principal)
        {
            int share = principal > 0 ? (int)(value * 100L / principal) : 0;
            share = Math.Max(MinPledgeShare, Math.Min(MaxPledgeShare, share));
            return 5 + (share - MinPledgeShare) / 2;
        }

        // Earlier loans to the same kind of borrower: unpaid loans make defaulting look safe, repaid loans and
        // debts collected by Sawyer's Crew make it look unwise.
        internal static void History(string profile, int exceptId, out int repaid, out int defaulted, out int collected)
        {
            repaid = 0;
            defaulted = 0;
            collected = 0;
            foreach (Loan other in Book.S.loans)
            {
                if (other.profile != profile || other.id == exceptId)
                    continue;
                switch ((LoanStatus)other.status)
                {
                    case LoanStatus.Repaid: repaid++; break;
                    case LoanStatus.Defaulted: defaulted++; break;
                    case LoanStatus.Collected: collected++; break;
                }
            }
        }

        // Decided on the night before the due day. A borrower who was an Aug never comes back.
        internal static void Decide(Loan loan, int today)
        {
            Profile p = Profiles.Get(loan.profile);
            int arrest = p != null ? p.ArrestChance * (News.SecurityHeat() ? 2 : 1) : 0;
            Outcome result;
            if (p == null || loan.wasAug)
                result = Outcome.Default;
            else if (arrest > 0 && RNG.Roll(arrest))
                result = Outcome.Arrested;
            else if (RNG.Roll(DefaultChance(loan, p)))
                result = Outcome.Default;
            else if (p.Id == Profiles.Worker && News.WorkerStrike() && !loan.partialDone)
                result = Outcome.Late;
            else
                result = PickRepayment(loan, p);

            loan.outcome = (int)result;
            int due = Math.Max(loan.dueDay, today + 1);
            switch (result)
            {
                case Outcome.Late:
                    loan.visitDay = due + RNG.GetRandomInt(1, 3);
                    break;
                case Outcome.Default:
                case Outcome.Arrested:
                    loan.visitDay = -1;
                    break;
                default:
                    loan.visitDay = due;
                    break;
            }
            MoneylendingMod.Log("loan #" + loan.id + " decided: " + result + ", visit day " + loan.visitDay);
        }

        private static Outcome PickRepayment(Loan loan, Profile p)
        {
            var options = new List<KeyValuePair<Outcome, int>>
            {
                new KeyValuePair<Outcome, int>(Outcome.OnTime, p.OnTime),
                new KeyValuePair<Outcome, int>(Outcome.Partial, loan.partialDone ? 0 : p.Partial),
                new KeyValuePair<Outcome, int>(Outcome.Late, loan.partialDone ? 0 : p.Late),
                new KeyValuePair<Outcome, int>(Outcome.Goods, p.GoodsItems != null && p.GoodsItems.Length > 0 ? p.Goods : 0),
                new KeyValuePair<Outcome, int>(Outcome.Bonus, p.Bonus),
            };
            int total = 0;
            foreach (KeyValuePair<Outcome, int> o in options)
                total += o.Value;
            if (total <= 0)
                return Outcome.OnTime;
            int roll = RNG.GetRandomInt(1, total);
            foreach (KeyValuePair<Outcome, int> o in options)
            {
                roll -= o.Value;
                if (roll <= 0)
                    return o.Key;
            }
            return Outcome.OnTime;
        }

        // ---- Money coming back ----------------------------------------------------------------------------------

        // The whole rest of the loan is paid: in cash (less the value of a pledge the player no longer has, plus
        // a bonus), or in goods worth at least the rest.
        internal static void Settle(Loan loan, int cash, bool goods)
        {
            if (!loan.IsActive)
                return;
            PlayerStore store = PlayerStore.Instance;
            int owed = loan.Outstanding;
            if (cash > 0)
                store.ModCash(cash, true);
            loan.paid = loan.total;
            loan.status = (int)LoanStatus.Repaid;
            loan.closedDay = Game.Day;
            loan.lastVisitDay = Game.Day;

            State s = Book.S;
            int shortfall = goods ? 0 : Math.Max(0, owed - cash);
            int extra = goods ? 0 : Math.Max(0, cash - owed);
            s.interestEarned += Math.Max(0, loan.total - loan.principal - shortfall) + extra;

            Profile p = Profiles.Get(loan.profile);
            if (p != null && p.RepOnRepay != 0)
                Game.ModReputation(p.Faction, p.RepOnRepay);
            Pledges.TakeBackIou(loan);
            Book.Save();
            Game.Notify(goods ? Text.RepaidGoods(loan.name) : Text.Repaid(cash, loan.name));
            MoneylendingMod.Log("loan #" + loan.id + " repaid: cash " + cash + (goods ? " plus goods" : ""));
        }

        // Part of the loan now, the rest on a later day.
        internal static void PayPart(Loan loan, int cash, int days)
        {
            if (!loan.IsActive)
                return;
            PlayerStore.Instance.ModCash(cash, true);
            loan.paid += cash;
            loan.partialDone = true;
            loan.outcome = (int)Outcome.Undecided;
            loan.lastVisitDay = Game.Day;
            loan.dueDay = Game.Day + days;
            loan.visitDay = -1;
            Book.Save();
            Game.Notify(Text.Repaid(cash, loan.name));
            MoneylendingMod.Log("loan #" + loan.id + " part payment " + cash + ", rest " + loan.Outstanding + " due day " + loan.dueDay);
        }

        // Written off after the grace days; the pledge, if any, stays with the player.
        internal static void Default(Loan loan, Profile p)
        {
            State s = Book.S;
            s.lost += loan.Outstanding;
            loan.status = (int)LoanStatus.Defaulted;
            loan.closedDay = Game.Day;
            string log;
            if (loan.wasAug)
                log = Text.AugLostLog(loan.name, loan.Outstanding);
            else
                log = Text.F(p != null ? p.Lines.DefaultLog : null, loan.name, loan.Outstanding);
            log = KeepPledges(loan, s, log);
            Game.NightLog(log, Game.Loss);
            if (p != null && p.SawyerCan && !loan.wasAug && loan.Outstanding > 0 && !Game.SawyerDead)
                loan.sawyerDay = Game.Day + 1;
        }

        internal static void Arrest(Loan loan, Profile p)
        {
            State s = Book.S;
            s.lost += loan.Outstanding;
            loan.status = (int)LoanStatus.Arrested;
            loan.closedDay = Game.Day;
            string log = Text.F(p.Lines.ArrestLog, loan.name, loan.Outstanding);
            log = KeepPledges(loan, s, log);
            Game.NightLog(log, Game.Loss);
        }

        // The pledges the player still has become the player's; the night log says which.
        private static string KeepPledges(Loan loan, State s, string log)
        {
            if (!loan.hasCollateral)
                return log;
            string names;
            int value;
            if (!Pledges.Forfeit(loan, out names, out value))
                return log;
            loan.collateralKept = true;
            s.recovered += value;
            return Text.Join(log, Text.PledgeKept(names));
        }

        internal static int SawyerFee(Loan loan)
        {
            return Math.Max(30, Game.RoundTo(loan.Outstanding / 4, 5));
        }

        internal static int SawyerChance(Loan loan)
        {
            int chance = 70 + Game.Reputation(Profiles.BlackMarket) / 5;
            if (loan.profile == Profiles.Thief)
                chance -= 20;
            return Math.Max(20, Math.Min(90, chance));
        }
    }
}
