using System;
using System.Collections.Generic;

namespace Moneylending
{
    // Borrowers asking for a loan, borrowers coming back, and Sawyer offering to collect.
    // Choices follow the game's own loan dialogues: a choice's label is the player's line, its effect runs in
    // the choice action, and the replies after it are ordinary lines (the label itself is never played, so it
    // carries no actions).
    internal static class Visits
    {
        // ---- Asking for a loan ------------------------------------------------------------------------------

        internal static StoreClient CreateBorrower()
        {
            Request r = Lending.TodayRequest();
            Profile p = Profiles.Get(r.profile);
            StoreClient client = Clients.Create(Clients.Borrower, p, r.name, r.sprite);
            client.OnArrival = () => client.mainDialogue = BorrowerDialogue(client, p, r);
            client.mainDialogue.SetText(r.name, Text.F(p.Lines.Situation[r.variant]));
            client.CompleteClientCreation(false);
            return client;
        }

        private static Dialogue BorrowerDialogue(StoreClient client, Profile p, Request r)
        {
            var first = new Dialogue().SetText(r.name, Text.F(p.Lines.Situation[r.variant]));
            if (r.answered)
            {
                first.SetEndAction(Clients.Leave);
                return first;
            }
            Dialogue last = first.NextDialogue().SetText(r.name, AskLine(p, r));
            // The pledges are put on the counter as the borrower arrives, like a seller's goods (not the player's
            // yet; they go with the borrower if no loan is made). An item without room on the counter is left out.
            var pledges = new List<GameItem>();
            var names = new List<string>();
            foreach (string id in Items.SplitIds(r.collateralId))
            {
                GameItem pledge = Items.Spawn(id);
                if (pledge == null || !PlayerStore.Instance.TryAddDirectSellingItemToTable(pledge, false, false, true, 0))
                    continue;
                pledges.Add(pledge);
                names.Add(pledge.name);
            }
            if (pledges.Count > 0)
                last = last.NextDialogue().SetText(r.name, Text.PledgeOffer(Text.ItemList(names), pledges.Count));
            AddRequestChoices(last, client, p, r, pledges, null);
            return first;
        }

        // A loan the player offers to the customer at the counter (from the ledger). The customer keeps their
        // own business afterwards; only a borrower who came for the loan leaves when it is settled.
        // The trade tablet that the game opens for a buying customer covers the choice bubbles, so it is put away
        // while the loan is discussed and opened again after the last line.
        internal static void OfferAtCounter(StoreClient client)
        {
            DialogUIManager dialog = DialogUIManager.Instance;
            if (client == null || dialog == null)
                return;
            NegociationUIManager tablet = NegociationUIManager.Instance;
            bool tabletWasOpen = tablet != null && tablet.IsOpen;
            if (tabletWasOpen)
                tablet.CloseUI();
            Action after = () =>
            {
                NegociationUIManager t = NegociationUIManager.Instance;
                if (tabletWasOpen && t != null && !t.IsOpen && Game.Current() == client)
                    t.OpenUI();
            };

            string reply;
            Profile p = Profiles.ForCustomer(client, out reply);
            var offer = new Dialogue().SetText("PLAYER", Text.OfferLoan());
            Dialogue answer = offer.NextDialogue();
            if (p == null || !RNG.Roll(p.OfferAccept))
            {
                answer.SetText(client.displayName, reply ?? Text.F(p != null ? p.Lines.Decline : null)).SetEndAction(after);
            }
            else
            {
                Request r = Lending.NewRequest(p, Game.Day, client.displayName, client.GetSprite(), false);
                if (p.Id == Profiles.Merchant || p.Id == Profiles.UpperBroke)
                    r.variant = RNG.GetRandomInt(0, 1);
                answer.SetText(client.displayName, Text.AcceptOffer());
                Dialogue situation = answer.NextDialogue().SetText(client.displayName, Text.F(p.Lines.Situation[r.variant]));
                Dialogue ask = situation.NextDialogue().SetText(client.displayName, AskLine(p, r));
                AddRequestChoices(ask, client, p, r, null, after);
            }
            dialog.StartDialogue(offer, true);
        }

        private static string AskLine(Profile p, Request r)
        {
            if (r.rate == 0 && p.Lines.AskFree != null)
                return Text.F(p.Lines.AskFree, r.amount, r.term);
            return Text.F(p.Lines.Ask[r.variant], r.amount, r.term, Lending.Total(r.amount, r.rate));
        }

        // counterEnd is null for a borrower who came for the loan (the replies end with them leaving); for a customer
        // at the counter it runs after the last reply.
        private static void AddRequestChoices(Dialogue line, StoreClient client, Profile p, Request r, List<GameItem> pledges, Action counterEnd)
        {
            string name = r.name;
            bool atCounter = counterEnd != null;
            Func<bool> canPay = () => Game.Cash >= r.amount;

            // Lend at the rate asked.
            Dialogue lend = new Dialogue().SetText("PLAYER", Text.ChoiceLend(r.amount));
            lend.SetNextDialogue(Reply(name, p.Lines.Thanks, counterEnd));
            line.AddChoice(ChoiceItem(), "lend", "", lend, () => Lending.Make(client, p, r, r.rate, false, r.rate == 0, atCounter, pledges), canPay);

            if (p.Gentle && r.rate > 0)
            {
                AddGentleChoice(line, client, p, r, pledges, counterEnd, canPay);
            }
            else if (p.CanWaive && r.rate > 0)
            {
                // A line holds three choices, so raising the rate and waiving it share a second line. Backing out of
                // it returns to this one.
                Dialogue talk = new Dialogue().SetText("PLAYER", Text.ChoiceTalkInterest());
                Dialogue ask = talk.NextDialogue().SetText(name, Text.TalkInterestReply());
                AddGentleChoice(ask, client, p, r, pledges, counterEnd, canPay);
                AddRaiseChoice(ask, client, p, r, pledges, counterEnd, canPay);
                Dialogue back = new Dialogue().SetText("PLAYER", Text.ChoiceBackToRequest());
                back.SetNextDialogue(line);
                ask.AddChoice(ChoiceItem(), "back", "", back, () => { });
                line.AddChoice(ChoiceItem(), "talk", "", talk, () => { }, canPay);
            }
            else
            {
                AddRaiseChoice(line, client, p, r, pledges, counterEnd, canPay);
            }

            // Refuse.
            Dialogue refuse = new Dialogue().SetText("PLAYER", Text.ChoiceRefuse());
            refuse.SetNextDialogue(Reply(name, p.Lines.Refused, counterEnd));
            line.AddChoice(ChoiceItem(), "refuse", "", refuse, () =>
            {
                r.answered = true;
                Book.Save();
            });
        }

        // Lend without interest.
        private static void AddGentleChoice(Dialogue line, StoreClient client, Profile p, Request r, List<GameItem> pledges, Action counterEnd, Func<bool> canPay)
        {
            bool atCounter = counterEnd != null;
            Dialogue gentle = new Dialogue().SetText("PLAYER", Text.ChoiceGentle());
            gentle.SetNextDialogue(Reply(r.name, p.Lines.Gentle, counterEnd));
            line.AddChoice(ChoiceItem(), "gentle", "", gentle, () => Lending.Make(client, p, r, 0, false, true, atCounter, pledges), canPay);
        }

        // Ask for more interest: the borrower accepts or walks away.
        private static void AddRaiseChoice(Dialogue line, StoreClient client, Profile p, Request r, List<GameItem> pledges, Action counterEnd, Func<bool> canPay)
        {
            bool atCounter = counterEnd != null;
            int raised = r.rate + p.RaiseStep;
            Dialogue raise = new Dialogue().SetText("PLAYER", Text.ChoiceRaise(raised, Lending.Total(r.amount, raised)));
            Dialogue yes = Reply(r.name, p.Lines.RaiseYes, counterEnd);
            Dialogue no = Reply(r.name, p.Lines.RaiseNo, counterEnd);
            raise.SetNextDialogue(no);
            line.AddChoice(ChoiceItem(), "raise", "", raise, () =>
            {
                if (RNG.Roll(Lending.RaiseChance(p)) && Lending.Make(client, p, r, raised, true, false, atCounter, pledges) != null)
                {
                    raise.SetNextDialogue(yes);
                }
                else
                {
                    r.answered = true;
                    Book.Save();
                }
            }, canPay);
        }

        private static Dialogue Reply(string name, string[] text, Action counterEnd)
        {
            return new Dialogue().SetText(name, Text.F(text)).SetEndAction(counterEnd ?? Clients.Leave);
        }

        // The game's choice bubbles take an item that carries the label; its own loan dialogue uses "$".
        private static GameItem ChoiceItem()
        {
            return DirectoryMaster.Item("$", true);
        }

        // ---- Coming back to repay -------------------------------------------------------------------------------

        internal static StoreClient CreateRepay(int id)
        {
            Loan loan = Book.FindLoan(id);
            Profile p = loan != null ? Profiles.Get(loan.profile) : null;
            if (loan == null || p == null || !loan.IsActive)
                return Clients.Nobody(Clients.RepayPrefix + Text.N(id));
            StoreClient client = Clients.Create(Clients.RepayPrefix + Text.N(id), loan.name, loan.sprite, loan.faction, p.Setup);
            client.OnArrival = () => client.mainDialogue = RepayDialogue(loan, p);
            client.mainDialogue.SetText(loan.name, "...");
            client.CompleteClientCreation(false);
            return client;
        }

        // Built when the borrower reaches the counter, so the pledge check sees the store as it is then. A
        // borrower sent away before handing over the money comes again the next day.
        private static Dialogue RepayDialogue(Loan loan, Profile p)
        {
            string name = loan.name;
            int owed = loan.Outstanding;
            Outcome result = loan.Result;
            var first = new Dialogue();

            if (result == Outcome.Partial)
            {
                int now = Math.Min(owed - 5, Math.Max(5, Game.RoundTo(owed * RNG.GetRandomInt(40, 60) / 100, 5)));
                int days = RNG.GetRandomInt(3, 4);
                first.SetText(name, Text.F(p.Lines.Partial, now, owed - now, days)).SetEndAction(() =>
                {
                    Lending.PayPart(loan, now, days);
                    Clients.Leave();
                });
                return first;
            }

            // A full repayment; the pledges go back, and the value of those the player no longer has is kept back.
            int deduction = 0;
            List<GameItem> pledges = loan.hasCollateral ? Pledges.FindAll(loan.id) : new List<GameItem>();
            if (loan.hasCollateral && pledges.Count < loan.collateralCount)
            {
                int held = 0;
                foreach (GameItem pledge in pledges)
                    held += (int)Math.Max(0, pledge.unitValue);
                deduction = Math.Min(owed, Math.Max(0, loan.collateralValue - held));
            }

            bool goods = result == Outcome.Goods && p.Lines.Goods != null;
            int cash = owed - deduction;
            if (goods)
            {
                List<string> items = Items.PickGoods(p.GoodsItems, cash * 115 / 100);
                bool stolen = p.GoodsStolen;
                first.SetText(name, Text.F(p.Lines.Goods)).SetStartAction(() => Items.PlaceGoods(items, stolen));
                cash = 0;
            }
            else
            {
                string[] text = result == Outcome.Late && p.Lines.Late != null ? p.Lines.Late : p.Lines.OnTime;
                if (result == Outcome.Bonus && p.Lines.Bonus != null)
                {
                    cash += Game.RoundTo(loan.principal * RNG.GetRandomInt(10, 25) / 100, 5);
                    text = p.Lines.Bonus;
                }
                first.SetText(name, Text.F(text, cash));
            }

            int paidCash = cash;
            if (deduction > 0)
            {
                first.SetEndAction(() => FinishRepay(loan, pledges, paidCash, goods, deduction));
                first.NextDialogue().SetText(name, pledges.Count > 0 ? Text.PledgePartlyMissingLine() : Text.PledgeMissingLine(loan.collateralName)).SetEndAction(Clients.Leave);
            }
            else
            {
                first.SetEndAction(() =>
                {
                    FinishRepay(loan, pledges, paidCash, goods, 0);
                    Clients.Leave();
                });
            }
            return first;
        }

        private static void FinishRepay(Loan loan, List<GameItem> pledges, int cash, bool goods, int deduction)
        {
            if (!loan.IsActive)
                return;
            var returned = new List<string>();
            var stayed = new List<string>();
            int stayedValue = 0;
            foreach (GameItem pledge in pledges)
            {
                string item = pledge.name;
                if (Pledges.GiveBack(pledge))
                {
                    returned.Add(item);
                    continue;
                }
                // No room on either counter: the pledge stays, and the borrower keeps back its value.
                stayed.Add(item);
                stayedValue += (int)Math.Max(0, pledge.unitValue);
                Pledges.Unmark(pledge);
            }
            if (returned.Count > 0)
                Game.Notify(Text.PledgeReturned(Text.ItemList(returned), loan.name));
            if (stayed.Count > 0)
            {
                int keep = goods ? 0 : Math.Min(cash, stayedValue);
                cash -= keep;
                Game.Notify(Text.PledgeNoRoom(Text.ItemList(stayed), loan.name, keep));
            }
            if (deduction > 0)
            {
                Profile p = Profiles.Get(loan.profile);
                if (p != null)
                    Game.ModReputation(p.Faction, -2);
                Game.Notify(pledges.Count > 0 ? Text.PledgePartlyMissingNote(loan.name, deduction) : Text.PledgeMissingNote(loan.name, deduction, loan.collateralName));
            }
            Lending.Settle(loan, cash, goods);
        }

        // ---- Sawyer ---------------------------------------------------------------------------------------------

        internal static StoreClient CreateSawyer(int id)
        {
            Loan loan = Book.FindLoan(id);
            if (loan == null || loan.status != (int)LoanStatus.Defaulted || loan.sawyerHiredDay >= 0)
                return Clients.Nobody(Clients.SawyerPrefix + Text.N(id));
            StoreClient client = Clients.Create(Clients.SawyerPrefix + Text.N(id), LocHelper.GetLocalizedClientName("name_sawyer"), "sawyer", Profiles.BlackMarket, StoreClientFactionSetup.InitBlackMarket);
            client.OnArrival = () => client.mainDialogue = SawyerDialogue(client, loan);
            client.mainDialogue.SetText(client.displayName, "...");
            client.CompleteClientCreation(false);
            return client;
        }

        private static Dialogue SawyerDialogue(StoreClient client, Loan loan)
        {
            loan.sawyerOffered = true;
            Book.Save();
            string name = client.displayName;
            int fee = Lending.SawyerFee(loan);
            var first = new Dialogue().SetText(name, LocHelper.GetLocalizedDialogue("dialog_sawyer_butcher_1", new object[] { loan.name }));
            Dialogue offer = first.NextDialogue().SetText(name, Text.SawyerOwes(loan.name, loan.Outstanding))
                .NextDialogue().SetText(name, LocHelper.GetLocalizedDialogue("dialog_sawyer_butcher_3", new object[] { fee }));

            Dialogue hire = new Dialogue().SetText("PLAYER", Text.SawyerHire(fee));
            hire.SetNextDialogue(new Dialogue().SetText(name, Text.SawyerHired()).SetEndAction(Clients.Leave));
            offer.AddChoice(ChoiceItem(), "hire", "", hire, () =>
            {
                PlayerStore store = PlayerStore.Instance;
                if (store.playerCash < fee || loan.sawyerHiredDay >= 0)
                    return;
                store.ModCash(-fee, true);
                loan.sawyerHiredDay = Game.Day;
                loan.sawyerFee = fee;
                Book.S.sawyerFees += fee;
                Book.Save();
                MoneylendingMod.Log("Sawyer hired for loan #" + loan.id + ", fee " + fee);
            }, () => Game.Cash >= fee);

            Dialogue decline = new Dialogue().SetText("PLAYER", Text.SawyerDecline());
            decline.SetNextDialogue(new Dialogue().SetText(name, Text.SawyerDeclined()).SetEndAction(Clients.Leave));
            offer.AddChoice(ChoiceItem(), "decline", "", decline, null);
            return first;
        }

        // Resolved on the night of the day the Crew was paid.
        internal static void ResolveSawyer(Loan loan)
        {
            Profile p = Profiles.Get(loan.profile);
            if (RNG.Roll(Lending.SawyerChance(loan)))
            {
                int amount = loan.Outstanding;
                PlayerStore.Instance.ModCash(amount, false);
                Book.S.recovered += amount;
                loan.recovered = amount;
                loan.paid = loan.total;
                loan.status = (int)LoanStatus.Collected;
                string log = Text.SawyerCollected(amount, loan.name);
                if (p != null && p.SawyerRepCost > 0 && Game.HasReputation(p.Faction))
                {
                    StoreReputation.ModReputation(p.Faction, -p.SawyerRepCost, false);
                    if (p.Faction == Profiles.LowerLevel)
                        log = Text.Join(log, Text.SawyerWordSpread());
                }
                Game.NightLog(log, Game.GoodNews);
            }
            else
            {
                Game.NightLog(Text.SawyerFailed(loan.name), Game.Warning);
            }
        }
    }
}
