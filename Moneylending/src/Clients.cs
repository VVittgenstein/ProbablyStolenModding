using System;
using System.Collections.Generic;

namespace Moneylending
{
    // Every visitor of this mod has an identifier in StoreClientListDict.storeClientDict, so the game (and
    // F5 Quick Save, which stores waiting customers by identifier) can recreate it. Visitors that belong to one
    // loan or debt carry its number in the identifier and rebuild themselves from the ledger.
    internal static class Clients
    {
        internal const string Borrower = "moneylending_borrower";
        internal const string RepayPrefix = "moneylending_repay_";
        internal const string SawyerPrefix = "moneylending_sawyer_";
        internal const string SharkOffer = "moneylending_shark_offer";
        internal const string BankOffer = "moneylending_bank_offer";
        internal const string CollectPrefix = "moneylending_collect_";

        internal static void RegisterStatic()
        {
            Dictionary<string, Func<StoreClient>> dict = StoreClientListDict.storeClientDict;
            dict[Borrower] = Visits.CreateBorrower;
            dict[SharkOffer] = Debts.CreateSharkOffer;
            dict[BankOffer] = Debts.CreateBankOffer;
        }

        internal static void RegisterRecords()
        {
            foreach (Loan loan in Book.S.loans)
                RegisterLoan(loan.id);
            foreach (Debt debt in Book.S.debts)
                RegisterDebt(debt.id);
        }

        internal static void RegisterLoan(int id)
        {
            Dictionary<string, Func<StoreClient>> dict = StoreClientListDict.storeClientDict;
            dict[RepayPrefix + Text.N(id)] = () => Visits.CreateRepay(id);
            dict[SawyerPrefix + Text.N(id)] = () => Visits.CreateSawyer(id);
        }

        internal static void RegisterDebt(int id)
        {
            StoreClientListDict.storeClientDict[CollectPrefix + Text.N(id)] = () => Debts.CreateCollector(id);
        }

        // A dialogue visitor in the style of the game's own (Georgio's loan, the Loan Shark): it cannot be shot
        // or reported, so a loan never loses its borrower halfway through a visit.
        internal static StoreClient Create(string identifier, string name, string sprite, string faction, Action<StoreClient> setup)
        {
            var client = new StoreClient();
            client.identifier = identifier;
            client.displayName = name;
            client.spriteName = sprite;
            if (setup != null)
                setup(client);
            else if (faction != null)
                client.clientFaction = faction;
            client.clientIntent = StoreClient.ClientIntent.DIALOGUE;
            client.nonShootable = true;
            client.arrestable = false;
            client.isBargainAvailable = false;
            client.CanReturn = false;
            ModHelper.SetClientSource(client, MoneylendingMod.ModId);
            return client;
        }

        internal static StoreClient Create(string identifier, Profile profile, string name, string sprite)
        {
            return Create(identifier, name, sprite, profile.Faction, profile.Setup);
        }

        // Returned when a record no longer exists (a stale identifier in the waiting line): says nothing and goes.
        internal static StoreClient Nobody(string identifier)
        {
            StoreClient client = Create(identifier, LocHelper.GetLocalizedClientName("name_scavenger"), SpriteDict.GetRandomScavSpriteName(), Profiles.LowerLevel, null);
            client.mainDialogue.SetText(client.displayName, "...").SetEndAction(Leave);
            client.CompleteClientCreation(false);
            return client;
        }

        internal static void Leave()
        {
            DialogUIManager dialog = DialogUIManager.Instance;
            if (dialog != null)
                dialog.CurrentClientLeave(1f);
        }

        // Puts a visitor among the customers already queued for today, at a random place.
        internal static void Insert(StoreClientManager manager, StoreClient client)
        {
            List<StoreClient> stack = manager.clientStack;
            stack.Insert(RNG.GetRandomInt(0, stack.Count), client);
        }
    }
}
