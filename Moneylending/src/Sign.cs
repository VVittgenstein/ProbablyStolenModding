using System;

namespace Moneylending
{
    // The Moneylending Neon Sign, an item of this mod. Its picture is Textures\Items\moneylending_sign.png, which the
    // game's ModLoader loads into this mod's sprites under the file name. The game's own signs work from the display
    // case: the Expedition Sign (value 50, no display case type, handed over when expeditions are unlocked) is what
    // lets hirelings come, and a category sign brings a seller of its kind with a 75% chance. This one is handed over
    // the same way when Moneylending is bought, and in the display case it raises the daily chance of a borrower from
    // 40% to the 75% of the category signs.
    internal static class Sign
    {
        internal const string Id = "moneylending_sign";
        private const string SpriteKey = "moneylending_sign";
        private const int Value = 50;
        internal const int BorrowerChance = 75;
        private const string GivenKey = "sign_given";

        // ModItemDirectory raises this once per launch, when the store's item directories start. Items saved in a
        // slot are rebuilt from their identifier through the directories, so the sign has to be registered by then.
        internal static void OnItemDirectoryInit(ModItemDirectory directory)
        {
            try
            {
                if (directory != null)
                    directory.Add(Id, () => Create(directory));
            }
            catch (Exception e)
            {
                MoneylendingMod.Warn("registering the neon sign failed: " + e);
            }
        }

        // Built like the game's signs: name, short description, value and the important tag, which keeps an item
        // out of repossession and out of the game's random item picks.
        private static GameItem Create(ModItemDirectory directory)
        {
            GameItem item = directory.Empty().SetSpriteAndShapeFromMod(MoneylendingMod.ModId, SpriteKey);
            item.name = Text.SignName();
            item.shortDescription = Text.SignDescription();
            item.unitValue = Value;
            item.EnableTag("IMPORTANT_TAG", false);
            return item;
        }

        internal static bool InDisplayCase()
        {
            return Game.InRun && ShowcaseHelper.IsShowcaseContainItemById(Id);
        }

        // Onto the back counter (EmporiumEntry.backInvinvElement), where the game puts the items of its own network
        // entries; GraphUtils.EmporiumTryAdd falls back to the store's other inventories and destroys the item when
        // all of them are full. The sign then counts as not given and comes with the next load.
        internal static bool Give()
        {
            if (!Game.InRun || !DirectoryMaster.Has<GameItem>(Id))
                return false;
            GameItem item = DirectoryMaster.Item(Id, true);
            GraphUtils.EmporiumTryAdd(EmporiumEntry.Instance.backInvinvElement, item);
            bool placed = Game.IsInStore(item);
            if (placed)
                ModHelper.SetModDataBool(MoneylendingMod.ModId, GivenKey, true);
            else
                MoneylendingMod.Warn("no room in the store for the neon sign");
            MoneylendingMod.Log("neon sign " + (placed ? "given" : "not placed") + " on day " + Game.Day);
            return placed;
        }

        // Runs that bought Moneylending before the sign existed (1.0.0) get it once, on their next load.
        internal static void GiveIfMissing()
        {
            if (!Unlock.IsUnlocked() || ModHelper.GetModDataBool(MoneylendingMod.ModId, GivenKey, false))
                return;
            if (Give())
                Game.Notify(Text.SignGiven());
        }
    }
}
