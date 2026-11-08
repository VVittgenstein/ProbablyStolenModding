using System;
using System.Collections.Generic;
using System.Text;

namespace Moneylending
{
    // Lenders who come to the player: the Loan Shark on a rent day when the register cannot cover the rent,
    // and the First Bank's loan clerk for a store in good standing with the Upper Levels.
    internal static class Debts
    {
        internal const string Shark = "shark";
        internal const string Bank = "bank";

        internal const int SharkRate = 30;
        internal const int SharkDays = 7;
        internal const int BankRate = 75;          // yearly, the rate the game's loan clerk quotes
        internal const int BankWeeks = 4;
        internal const int BankSmall = 1000;
        internal const int BankLarge = 2000;
        internal const int BankReputation = 20;    // Upper Levels reputation needed for an offer
        internal const int BankOfferGap = 14;
        internal const int BankOfferChance = 25;

        // What the store must pay on a rent day: rent, a mortgage installment, or nothing once it is bought.
        internal static int RentDue()
        {
            PlayerStore store = PlayerStore.Instance;
            if (store.IsPropertyPaid)
                return 0;
            return store.isPayingMortgage ? store.mortgageWeeklyPayment : store.GetRentValue();
        }

        internal static bool IsRentDay()
        {
            return PlayerStore.Instance.GetRentDayCounter() == 0;
        }

        // ---- Morning and night ---------------------------------------------------------------------------------

        internal static void OnMorning(StoreClientManager manager, int day)
        {
            State s = Book.S;
            foreach (Debt debt in s.debts)
            {
                if (debt.IsActive && debt.dueDay <= day)
                    manager.AddClient(CreateCollector(debt.id));
            }

            if (IsRentDay())
            {
                int due = RentDue();
                if (due > 0 && Game.Cash < due && Book.ActiveDebt(Shark) == null && s.lastSharkOfferDay != day)
                {
                    s.lastSharkOfferDay = day;
                    manager.AddNextClient(CreateSharkOffer());
                }
            }
            else if (Book.ActiveDebt(Bank) == null && Game.Reputation(Profiles.UpperLevel) >= BankReputation
                     && day - s.lastBankOfferDay >= BankOfferGap && RNG.Roll(BankOfferChance))
            {
                s.lastBankOfferDay = day;
                Clients.Insert(manager, CreateBankOffer());
            }
        }

        // A collector who was not served (the store closed first) comes back the next day.
        internal static void OnNight(int day)
        {
            foreach (Debt debt in Book.S.debts)
            {
                if (debt.IsActive && debt.dueDay <= day && debt.lastVisitDay != day)
                    debt.dueDay = day + 1;
            }
        }

        // ---- The Loan Shark's offer ------------------------------------------------------------------------------

        internal static StoreClient CreateSharkOffer()
        {
            StoreClient client = SharkClient(Clients.SharkOffer);
            client.OnArrival = () => client.mainDialogue = SharkOfferDialogue(client);
            client.mainDialogue.SetText(client.displayName, LocHelper.GetLocalizedDialogue("dialog_loan_shark_intro_1", new object[0]));
            client.CompleteClientCreation(false);
            return client;
        }

        private static StoreClient SharkClient(string identifier)
        {
            StoreClient client = Clients.Create(identifier, Text.SharkName(), SpriteDict.GetLoanSharkSpriteName(), Profiles.BlackMarket, StoreClientFactionSetup.InitBlackMarket);
            client.dismissable = false;
            return client;
        }

        private static Dialogue SharkOfferDialogue(StoreClient client)
        {
            string name = client.displayName;
            var first = new Dialogue().SetText(name, LocHelper.GetLocalizedDialogue("dialog_loan_shark_intro_1", new object[0]));
            if (Book.ActiveDebt(Shark) != null)
            {
                first.SetEndAction(Clients.Leave);
                return first;
            }
            int shortfall = Math.Max(0, RentDue() - Game.Cash);
            int amount = Math.Max(200, (int)Math.Ceiling((shortfall + 100) / 50.0) * 50);
            int total = Lending.Total(amount, SharkRate);
            Dialogue offer = first.NextDialogue().SetText(name, Text.SharkRentDay())
                .NextDialogue().SetText(name, Text.SharkOffer(amount, total, SharkDays));

            Dialogue take = new Dialogue().SetText("PLAYER", Text.SharkTake(amount));
            take.SetNextDialogue(new Dialogue().SetText(name, Text.SharkTaken(SharkDays))
                .NextDialogue().SetText(name, LocHelper.GetLocalizedDialogue("dialog_loan_shark2_yes_3", new object[0])).SetEndAction(Clients.Leave));
            offer.AddChoice(DirectoryMaster.Item("$", true), "take", "", take, () => Borrow(Shark, amount, total, SharkDays, 1, null));

            Dialogue no = new Dialogue().SetText("PLAYER", Text.SharkNo());
            no.SetNextDialogue(new Dialogue().SetText(name, Text.SharkRefused()).SetEndAction(Clients.Leave));
            offer.AddChoice(DirectoryMaster.Item("$", true), "no", "", no, null);
            return first;
        }

        // ---- The First Bank's offer --------------------------------------------------------------------------------

        internal static StoreClient CreateBankOffer()
        {
            string sprite = SpriteDict.GetRandomUpperSpriteName();
            StoreClient client = BankClient(Clients.BankOffer, sprite);
            client.OnArrival = () => client.mainDialogue = BankOfferDialogue(client, sprite);
            client.mainDialogue.SetText(client.displayName, LocHelper.GetLocalizedDialogue("dialog_loan_clerk_1", new object[0]));
            client.CompleteClientCreation(false);
            return client;
        }

        private static StoreClient BankClient(string identifier, string sprite)
        {
            StoreClient client = Clients.Create(identifier, LocHelper.GetLocalizedClientName("name_loan_clerk"), sprite, "NONE", null);
            client.AddTag("SPEAKS_FORMAL");
            client.bubbleColor = "upper";
            return client;
        }

        private static Dialogue BankOfferDialogue(StoreClient client, string sprite)
        {
            string name = client.displayName;
            var first = new Dialogue().SetText(name, LocHelper.GetLocalizedDialogue("dialog_loan_clerk_1", new object[0]));
            if (Book.ActiveDebt(Bank) != null)
            {
                first.SetEndAction(Clients.Leave);
                return first;
            }
            Dialogue offer = first.NextDialogue().SetText(name, LocHelper.GetLocalizedDialogue("dialog_loan_clerk_2", new object[0]))
                .NextDialogue().SetText(name, LocHelper.GetLocalizedDialogue("dialog_loan_clerk_3", new object[0]))
                .NextDialogue().SetText(name, Text.BankOffer(BankSmall, BankLarge, BankWeeks, BankRate));

            AddBankChoice(offer, name, BankSmall, sprite);
            AddBankChoice(offer, name, BankLarge, sprite);
            Dialogue no = new Dialogue().SetText("PLAYER", Text.BankNo());
            no.SetNextDialogue(new Dialogue().SetText(name, Text.BankRefused()).SetEndAction(Clients.Leave));
            offer.AddChoice(DirectoryMaster.Item("$", true), "no", "", no, null);
            return first;
        }

        private static void AddBankChoice(Dialogue offer, string name, int amount, string sprite)
        {
            int weekly = PlayerStore.CalculateWeeklyPayment(amount, BankRate, BankWeeks);
            Dialogue take = new Dialogue().SetText("PLAYER", Text.BankBorrow(amount, weekly));
            take.SetNextDialogue(new Dialogue().SetText(name, Text.BankTaken()).SetEndAction(Clients.Leave));
            offer.AddChoice(DirectoryMaster.Item("$", true), "borrow", "", take, () => Borrow(Bank, amount, weekly * BankWeeks, 7, BankWeeks, sprite));
        }

        private static void Borrow(string lender, int amount, int total, int days, int installments, string sprite)
        {
            if (Book.ActiveDebt(lender) != null)
                return;
            var debt = new Debt();
            debt.id = Book.NextId();
            debt.lender = lender;
            debt.principal = amount;
            debt.total = total;
            debt.installment = installments > 1 ? (int)Math.Ceiling(total / (double)installments) : total;
            debt.installmentsLeft = installments;
            debt.dayBorrowed = Game.Day;
            debt.dueDay = Game.Day + days;
            debt.sprite = sprite;
            Book.S.debts.Add(debt);
            Clients.RegisterDebt(debt.id);
            PlayerStore.Instance.ModCash(amount, true);
            Book.Save();
            MoneylendingMod.Log("borrowed " + amount + " from " + lender + ", total " + total + ", first payment day " + debt.dueDay);
        }

        // ---- Collecting --------------------------------------------------------------------------------------------

        internal static StoreClient CreateCollector(int id)
        {
            Debt debt = Book.FindDebt(id);
            if (debt == null || !debt.IsActive)
                return Clients.Nobody(Clients.CollectPrefix + Text.N(id));
            StoreClient client = debt.lender == Bank
                ? BankClient(Clients.CollectPrefix + Text.N(id), debt.sprite ?? SpriteDict.GetRandomUpperSpriteName())
                : SharkClient(Clients.CollectPrefix + Text.N(id));
            client.dismissable = false;
            client.OnArrival = () => client.mainDialogue = CollectDialogue(client, debt);
            client.mainDialogue.SetText(client.displayName, "...");
            client.CompleteClientCreation(false);
            return client;
        }

        private static Dialogue CollectDialogue(StoreClient client, Debt debt)
        {
            debt.lastVisitDay = Game.Day;
            Book.Save();
            string name = client.displayName;
            bool bank = debt.lender == Bank;
            int due = debt.AmountDue;
            var line = new Dialogue().SetText(name, bank ? Text.BankCollect(due, debt.installmentsLeft - 1) : Text.SharkCollect(due));

            Dialogue pay = new Dialogue().SetText("PLAYER", Text.Pay(due));
            Dialogue paidReply = new Dialogue().SetText(name, bank ? Text.BankPaid(debt.installmentsLeft <= 1) : Text.SharkPaid()).SetEndAction(Clients.Leave);
            pay.SetNextDialogue(paidReply);
            line.AddChoice(DirectoryMaster.Item("$", true), "pay", "", pay, () => Pay(debt, due), () => Game.Cash >= due);

            Dialogue cannot = new Dialogue().SetText("PLAYER", Text.CannotPay());
            Dialogue seizeReply;
            if (bank)
            {
                seizeReply = new Dialogue().SetText(name, Text.BankSeize()).SetEndAction(Clients.Leave);
            }
            else
            {
                seizeReply = new Dialogue().SetText(name, LocHelper.GetLocalizedDialogue("dialog_loan_shark_collect_repo_3", new object[0]));
                seizeReply.NextDialogue().SetText(name, Text.SharkSquare()).SetEndAction(Clients.Leave);
            }
            cannot.SetNextDialogue(seizeReply);
            line.AddChoice(DirectoryMaster.Item("$", true), "cannot", "", cannot, () => Seize(debt, due), () => Game.Cash < due);
            return line;
        }

        private static void Pay(Debt debt, int due)
        {
            if (!debt.IsActive || Game.Cash < due)
                return;
            PlayerStore.Instance.PayMoney(due, true);
            Advance(debt, due);
            Book.Save();
            MoneylendingMod.Log("paid " + due + " to " + debt.lender + ", left " + debt.Outstanding);
        }

        // Takes all the cash, then goods for the rest, chosen the way the game's Loan Shark repossesses goods when
        // a mortgage payment is missed (PlayerStore.GetAllRepoEligibleItems and FindRepoItems), and leaves a list of
        // what was taken on the counter, as he does. The lender's faction remembers.
        private static void Seize(Debt debt, int due)
        {
            if (!debt.IsActive)
                return;
            PlayerStore store = PlayerStore.Instance;
            int cash = Math.Min(due, store.playerCash);
            if (cash > 0)
                store.PayMoney(cash, true);
            int shortfall = due - cash;
            // Only goods directly on the shelves or the counters, and no borrower's pledge.
            var candidates = new List<GameItem>();
            EmporiumEntry entry = EmporiumEntry.Instance;
            if (shortfall > 0 && entry != null)
            {
                List<GameItem> eligible = store.GetAllRepoEligibleItems();
                if (eligible != null)
                {
                    foreach (GameItem item in eligible)
                    {
                        if (Pledges.OnShelfOrCounter(entry, item) && !Pledges.IsActivePledge(item))
                            candidates.Add(item);
                    }
                }
            }
            List<GameItem> chosen = candidates.Count > 0 ? store.FindRepoItems(candidates, shortfall) : null;
            var items = new List<GameItem>();
            long value = 0;
            var names = new StringBuilder();
            if (chosen != null)
            {
                foreach (GameItem item in chosen)
                {
                    long worth = Math.Max(0, item.GetCurrentValue(false, true, true, false, 0));
                    string name = item.name;
                    if (!Pledges.GiveBack(item))
                        continue;
                    items.Add(item);
                    value += worth;
                    names.Append(name).Append('\n');
                }
            }
            debt.seizedValue += (int)value;
            string lender = debt.lender == Bank ? Text.BankName() : Text.SharkName();
            if (items.Count > 0)
            {
                string list = Text.SeizedList(lender, (int)value, names.ToString().TrimEnd('\n'));
                Game.PutOnCounter(() => PreBuiltItemHelper.CreatePaperWithText(list, Text.SeizedTitle()));
                Game.Notify(Text.Seized(lender, (int)value, items.Count));
            }
            else
            {
                Game.Notify(Text.SeizedNothing(lender));
            }
            if (debt.lender == Bank)
            {
                Game.ModReputation(Profiles.UpperLevel, -10);
                Advance(debt, due);
            }
            else
            {
                Game.ModReputation(Profiles.BlackMarket, -10);
                debt.paid = debt.total;
                debt.status = (int)DebtStatus.Seized;
                debt.closedDay = Game.Day;
            }
            Book.Save();
            MoneylendingMod.Log("seized from the player for " + debt.lender + ": cash " + cash + ", goods " + value + " (" + items.Count + " item(s))");
        }

        private static void Advance(Debt debt, int amount)
        {
            debt.paid += amount;
            debt.installmentsLeft = Math.Max(0, debt.installmentsLeft - 1);
            if (debt.Outstanding <= 0 || debt.installmentsLeft == 0)
            {
                debt.paid = debt.total;
                debt.status = debt.seizedValue > 0 ? (int)DebtStatus.Seized : (int)DebtStatus.PaidOff;
                debt.closedDay = Game.Day;
            }
            else
            {
                debt.dueDay = Game.Day + 7;
            }
        }
    }
}
