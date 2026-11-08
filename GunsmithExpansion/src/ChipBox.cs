using System;

namespace GunsmithExpansion
{
    // The chip box: the player's own storage for printer chips (VVittgenstein 2026-11-05 16:53, current/008 quote 5 point 2: the game has
    // none, and the picture of batch 14 becomes this). The game's only code that reads
    // PRINTER_CHIP_TAG is the printer's chip slot, and none of its containers (ContainerItemDirectory and the
    // AllowOnlyTaggedItem callers, read 2026-11-05) is made for chips.
    //   - 5 x 5 cells inside; a chip takes 1 x 1, so 25 chips.
    //   - It takes what carries PRINTER_CHIP_TAG (the game's chips, this mod's gun chips and reloading dies) and this mod's
    //     blank chip, and only the store's own, as the toolbox takes only owned tools.
    // Agent's decisions: value 100 (the game's fanny pack holds 3 x 3 of anything at 50, its satchel 6 x 3 at 150, about 5.6
    // to 8.3 a cell, and 25 cells of chips only come lower); ordered by phone from the retired gunsmith, in the cabinets' group.
    internal static class ChipBox
    {
        internal const string Id = "gse_box_chip";
        internal const int Value = 100;
        private const int Size = 5;

        internal static bool Registered()
        {
            return ModHelper.ModFileExists(GunsmithMod.ModId, "Textures/Items/" + Id + ".png");
        }

        internal static void Register(ModItemDirectory directory)
        {
            if (!Registered())
                return;
            directory.Add(Id, () => Create(directory));
            GunsmithMod.Log("registered the chip box");
        }

        private static GameItem Create(ModItemDirectory directory)
        {
            var made = DirectoryUtils.CreateInventoryWindow(Size, Size, true);
            PixelWindow window = made.Item1;
            GameInventory inside = made.Item2;
            GameItem box = directory.Empty().SetContentWindow(window).SetSpriteAndShapeFromMod(GunsmithMod.ModId, Id);
            box.name = Text.Pick("芯片盒", "Chip Box");
            box.shortDescription = Text.Pick("收纳3D打印机芯片的盒子，放得下 25 片，只能放芯片。", "A box for 3D printer chips. It holds 25 and takes nothing else.");
            box.unitValue = Value;
            box.EnableTag("IMPORTANT_TAG", false);
            box.EnableTag("CONTAINER_TAG", false);
            box.SetGameItemType("STORAGE");
            ContainerHelper.AddContainerTooltipTag(box);
            AudioHelper.InitPlasticContainer(box);
            window.SetTitle(box.name);
            window.Validate();
            inside.mayInventoryAddItemFunc = (Func<GameItem, GameInventory, bool>)Delegate.Combine(inside.mayInventoryAddItemFunc,
                new Func<GameItem, GameInventory, bool>((item, target) => Accepts(item)));
            // The game lets the player's own hoppers into a container before asking the container (GameGridInventory.MayHaveValidInventorySlot,
            // HopperHelper.MayBypassGridRules) unless it has this tag, as the game's module containers do (ContainerHelper.AllowOnlyTaggedItemsOr;
            // VVittgenstein 2026-11-07 20:16, current/T-006 quote 21).
            inside.EnableTag("NO_HOPPER_INVENTORY_TAG");
            return box;
        }

        private static bool Accepts(GameItem item)
        {
            return item != null && GeneralHelper.IsItemOwned(item) && (item.IsTag("PRINTER_CHIP_TAG") || Production.BlankLevel(item) > 0);
        }
    }
}
