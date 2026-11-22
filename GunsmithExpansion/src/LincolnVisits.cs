using System;

namespace GunsmithExpansion
{
    // Yuri Lincoln's first visit to the player's store (final-strategy 306, 333, 343; the dialogue draft's section 1): on day 3, or
    // the day after loading a save past it, again the next day until he is received. He says his two lines, the player answers, and he
    // leaves his business card where the phone couriers leave a box (DeliveryBoxes: the front counter, else the counter top, the
    // inventory or the display case, which he names); with no room anywhere he waits until the player has made some. Once the card is
    // handed over, Lincoln Arms is on the night map (Lincoln.SetIntroduced). He brings no goods (343).
    internal static class LincolnVisits
    {
        internal const string IntroId = "gseLincolnIntro";

        internal static void Register()
        {
            StoreClientListDict.storeClientDict[IntroId] = CreateIntro;
        }

        private static StoreClient CreateIntro()
        {
            var c = new StoreClient();
            c.identifier = IntroId;
            c.displayName = Lincoln.DealerName();
            c.spriteName = Certificates.PortraitKey(Lincoln.DealerPortrait) ?? SpriteDict.GetRandomLowerLevelSpriteName();
            c.clientFaction = "FACTION_LOWER_LEVEL";
            c.isBargainAvailable = false;
            c.nonShootable = true;
            // as the phone couriers (Phone.CreateCourier): a visit without a sale
            c.clientIntent = (StoreClient.ClientIntent)2;
            c.isRegularSellDisable = true;
            string name = c.displayName;
            Dialogue second = c.mainDialogue.SetText(name, LincolnTexts.IntroFirst.Get()).NextDialogue().SetText(name, LincolnTexts.IntroSecond.Get());
            Dialogue reply = new Dialogue().SetText("PLAYER", LincolnTexts.IntroReply.Get());
            second.AddChoice(DirectoryMaster.Item("$", true), "", "", reply, () => GiveCard(c, reply.NextDialogue()));
            c.allDoneDialogue = new Dialogue().SetText(name, LincolnTexts.IntroCardOnCounter.Get()).SetEndAction(Leave);
            c.CompleteClientCreation(false);
            return c;
        }

        // As Phone.HandOver: the line is set by what happened; without room he asks for some and the choice tries again.
        private static void GiveCard(StoreClient client, Dialogue line)
        {
            string name = client.displayName;
            if (line.choices != null)
                line.choices.Clear();
            if (Lincoln.Introduced())
            {
                line.SetText(name, LincolnTexts.IntroCardOnCounter.Get()).SetEndAction(Leave);
                return;
            }
            string elsewhere = null;
            GameItem card = LincolnItems.NewCard();
            bool placed = card != null && DeliveryBoxes.PlaceItem(card, ref elsewhere);
            if (card != null && !placed)
            {
                card.Destroy();
                line.SetText(name, LincolnTexts.IntroNoRoom.Get());
                line.SetEndAction(null);
                Dialogue cleared = new Dialogue().SetText("PLAYER", LincolnTexts.Cleared.Get());
                line.AddChoice(DirectoryMaster.Item("$", true), "", "", cleared, () => GiveCard(client, cleared.NextDialogue()));
                return;
            }
            if (card == null)
                GunsmithMod.Warn("Lincoln Arms business card not made (its picture " + LincolnItems.CardId + " is not there); the shop opens without it");
            Lincoln.SetIntroduced();
            line.SetText(name, elsewhere != null ? LincolnTexts.IntroCardElsewhere.Format(elsewhere) : LincolnTexts.IntroCardOnCounter.Get())
                .SetEndAction(Leave);
        }

        private static void Leave()
        {
            if (DialogUIManager.Instance != null)
                DialogUIManager.Instance.CurrentClientLeave(1f);
        }
    }
}
