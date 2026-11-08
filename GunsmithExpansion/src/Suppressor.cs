namespace GunsmithExpansion
{
    // The reed core makeshift suppressor (final-strategy 42): a hydroponic reed filter dragged onto a small bottle
    // (the bottle printer's small bottle, the shell of the game's own makeshift suppressor, which it makes from the
    // bottle and scrap metal) gives a makeshift suppressor with a filter core. Like the game's one it is a barrel mod
    // of the makeshift type, so it suppresses at the makeshift level (GunHelper.UpdateCurrentGunStats), fits every
    // gun with an open barrel slot and has the game's picture; its penalties are smaller. Agent's decisions: the
    // stats (style -30, reliability -15, recoil control +12, ergonomics -20, against the game's -50, -35, +10, -30)
    // and the name. Value 25: the game's makeshift suppressor (15) with the value of its scrap metal core (10) swapped
    // for the reed filter's (20) (VVittgenstein 2026-11-02, current/005 quote 50). From 0.6.2 it fits the guns the game's makeshift
    // suppressor fits and looks like it when fitted: it takes that item's fitting limit and drawing functions, on this
    // mod's guns the eighth batch's pictures, on the game's guns the game's picture and places (final-strategy 104; until
    // then it drew nothing on any gun).
    internal static class ReedSuppressor
    {
        internal const string Id = "gse_suppressor_reed";
        internal const int Value = 25;
        private static readonly int[] Stats = { 0, -30, -15, 12, -20, 0 };

        internal static void Register(ModItemDirectory directory)
        {
            directory.Add(Id, () => Create(directory));
        }

        private static GameItem Create(ModItemDirectory directory)
        {
            GameItem item = directory.Empty().SetSpriteAndShape("Items/items_mod", "bottle_item");
            item.name = Text.Pick("芦苇芯简易消音器", "Reed Core Makeshift Suppressor");
            item.shortDescription = Text.Pick("小瓶子做外壳、水培芦苇滤芯做芯的简易消音器。比废金属做芯的结实，消音效果相同。",
                "A makeshift suppressor of a small bottle with a hydroponic reed filter for a core. Sturdier than one with scrap metal inside, and as quiet.");
            item.unitValue = Value;
            GunModHelper.InitGunModBarrel(item, true);
            GunModHelper.InitGunMod(item, Stats[0], Stats[1], Stats[2], Stats[3], Stats[4], Stats[5]);
            GunModHelper.InitGunModBaseStats(item, Items.Base(Stats[0]), Items.Base(Stats[1]), Items.Base(Stats[2]), Items.Base(Stats[3]),
                Items.Base(Stats[4]), Items.Base(Stats[5]));
            item.SetGameItemType("MAKESHIFT");
            GunModHelper.AllowOnlyWeapon(Fittings.GameMods[VanillaMods.MakeshiftSuppressor], item);
            GameItem game = DirectoryMaster.Item(VanillaMods.MakeshiftSuppressor, false);
            item.parentItemSpriteModifierForItemFunc = game.parentItemSpriteModifierForItemFunc;
            item.parentItemShapeModifierForItemFunc = game.parentItemShapeModifierForItemFunc;
            return item;
        }

        internal static bool Applies(GameItem source, GameItem target)
        {
            return source != null && target != null && source.IsID("hydroreed_filter") && target.IsID("small_bottled_water") && !source.forceDisableUse
                && GeneralHelper.IsItemEffectivelyOwned(source) && GeneralHelper.IsItemEffectivelyOwned(target) && target.parentInventory != null;
        }

        internal static void Make(GameItem filter, GameItem bottle)
        {
            GameInventory parent = bottle.parentInventory;
            filter.Destroy();
            bottle.Destroy();
            GraphUtils.EmporiumTryAdd(parent, DirectoryMaster.Item(Id, true));
            if (OverlayHandler.current != null)
                OverlayHandler.current.UpdateInventoryStateUI();
            GunsmithMod.Log("reed filter and small bottle made into a reed core makeshift suppressor");
        }

        internal static string UseLine(GameItem bottle)
        {
            return string.Format(Text.Pick("<b>装进</b>{0}，做成{1}", "<b>Fit</b> into {0} to make a {1}"), "<u>" + bottle.GetDisplayName(true) + "</u>",
                DirectoryMaster.Item(Id, false).name);
        }
    }
}
