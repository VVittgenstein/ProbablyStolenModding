using System;
using System.Collections.Generic;

namespace GunsmithExpansion
{
    // Registers the catalog's guns and gun mods. ModItemDirectory raises OnModItemDirectoryInit once per launch, when
    // the store's item directories start; items saved in a slot are rebuilt from their identifier through the
    // directories, so everything has to be registered by then. Pictures are Textures\Items\<identifier>.png, which the
    // game's ModLoader loads into this mod's sprites; SetSpriteAndShapeFromMod builds the grid shape from their alpha.
    internal static class Items
    {
        internal static void OnItemDirectoryInit(ModItemDirectory directory)
        {
            if (directory == null)
                return;
            int guns = 0, mods = 0;
            AddOn2077.Load();
            var allGuns = new List<GunDef>(Catalog.Guns);
            allGuns.AddRange(AddOn2077.Guns);
            foreach (GunDef def in allGuns)
            {
                GunDef d = def;
                try
                {
                    directory.Add(d.Id, () => CreateGun(directory, d));
                    guns++;
                }
                catch (Exception e)
                {
                    GunsmithMod.Warn("registering " + d.Id + " failed: " + e.Message);
                }
            }
            foreach (ModDef def in Catalog.Mods)
            {
                ModDef d = def;
                try
                {
                    directory.Add(d.Id, () => CreateMod(directory, d));
                    mods++;
                }
                catch (Exception e)
                {
                    GunsmithMod.Warn("registering " + d.Id + " failed: " + e.Message);
                }
            }
            GunsmithMod.Log("registered " + guns + " guns and " + mods + " gun mods");
            Production.Register(directory);
            ReedSuppressor.Register(directory);
            Ammo.Register(directory);
            Reloader.Register(directory);
            Certificates.Register(directory);
            GunsmithSign.Register(directory);
            Cabinets.Register(directory);
            DeliveryBoxes.Register(directory);
            ChipBox.Register(directory);
            CorpLink.Apply();
        }

        // Built in the order of the game's GunsItemDirectory.CreateSMG: mod window and its slots, picture, name,
        // maker, description, value, contraband level, sounds, item types, stats, enabled slots, condition.
        // Since 0.2.8 a slot is open only when some gun mod of the mod fits the gun there (src/Fittings.cs, VVittgenstein
        // 2026-11-01 23:15); the special slots stay open on long guns and pistols for the game's internal parts
        // (springs, trigger group and the like fit any gun), and revolvers get one for the revolver grip.
        private static GameItem CreateGun(ModItemDirectory directory, GunDef d)
        {
            string[] open;
            if (!Fittings.Slots.TryGetValue(d.Id, out open))
                open = new string[0];
            int specials = d.Slots == Slots.Long ? 3 : d.Slots == Slots.Pistol ? 2 : Array.IndexOf(open, "special") >= 0 ? 1 : 0;
            bool modded = d.Slots != Slots.None || specials > 0;
            PixelWindow window = null;
            GameSlotInventory stock = null, rail = null, magazine = null, under = null, barrel = null;
            GameSlotInventory special1 = null, special2 = null, special3 = null;
            if (modded)
            {
                var w = GunModHelper.CreateGunModWindow(true, true, 5);
                window = w.Item1;
                stock = w.Item2;
                rail = w.Item3;
                magazine = w.Item4;
                under = w.Item5;
                barrel = w.Item6;
                special1 = w.Item7;
                special2 = w.Rest.Item1;
                special3 = w.Rest.Item2;
            }
            GameItem item = directory.Empty().SetSpriteAndShapeFromMod(d.AddOn ? AddOn2077.ModId : GunsmithMod.ModId, d.Id);
            if (window != null)
                item = item.SetContentWindow(window);
            item.name = Text.Pick(d.NameZh, d.NameEn);
            if (!string.IsNullOrEmpty(d.Maker))
                item.EnableManufacturer(d.Maker);
            item.shortDescription = Text.GunDescription(d);
            item.unitValue = d.Value;
            ContrabandHelper.InitContrabandItem(item, d.Contraband);
            if (d.Heavy)
                AudioHelper.InitHeavyGun(item);
            else
                AudioHelper.InitGun(item);
            item.SetGameItemType("WEAPON");
            item.SetGameItemType("FIREARM");
            item.SetGameItemType(d.Line);
            if (d.Antique)
                item.EnableTag(GunsItemDirectory.ANTIQUE_FIREARM_TAG, false);
            int[] s = d.Stats;
            GunHelper.InitGun(item, s[0], s[1], s[2], s[3], s[4], s[5], d.Caliber, d.Repairability, modded);
            if (modded)
            {
                if (Array.IndexOf(open, "stock") >= 0)
                    GunModHelper.EnableStockSlot(stock);
                else
                    Close(window, stock, "mech_gun_tag_stock");
                if (Array.IndexOf(open, "rail") >= 0)
                    GunModHelper.EnableRailSlot(rail);
                else
                    Close(window, rail, "mech_gun_tag_rail");
                if (Array.IndexOf(open, "barrel") >= 0)
                    GunModHelper.EnableBarrelSlot(barrel);
                else
                    Close(window, barrel, "mech_gun_tag_barrel");
                if (Array.IndexOf(open, "magazine") >= 0)
                    GunModHelper.EnableMagazineSlot(magazine, null);
                else
                    Close(window, magazine, "mech_gun_tag_magazine");
                if (Array.IndexOf(open, "under") >= 0)
                    GunModHelper.EnableUnderbarrelSlot(under);
                else
                    Close(window, under, "mech_gun_tag_underbarrel");
                GameSlotInventory[] special = { special1, special2, special3 };
                for (int i = 0; i < special.Length; i++)
                {
                    if (i < specials)
                        GunModHelper.EnableSpecialSlot(special[i]);
                    else
                        Close(window, special[i], "mech_gun_tag_special" + (i + 1));
                }
            }
            GunHelper.SetEquipmentCondition(item, (ItemFeatureList.EquipmentCondition)2);
            return item;
        }

        // A slot of the game's gun mod window that stays closed on this gun: it takes nothing and its panel is hidden.
        // Without this the slot showed in the window and took any gun mod allowed on the gun, without the stat update
        // that the game's Enable*Slot hooks run (found 2026-11-02; the pistols' unused slots had it since 0.1.0).
        // GunModHelper.CreateGunModWindow gives each slot's panel the slot's label key as its role.
        private static void Close(PixelWindow window, GameSlotInventory slot, string role)
        {
            slot.mayInventoryAddItemFunc = (mod, inventory) => false;
            var row = window.childElement as MachineryPanelRow;
            MachineryPanel panel = row != null ? row.GetPanel(role) : null;
            if (panel != null)
                panel.SetHidden(true);
        }

        // Built like the game's GunModDirectory entries: slot initialisation, then the stat bonuses; the base stats
        // are 30% of the bonuses, the ratio of the game's red dot sight (25 and 10 accuracy, 20 and 5 style) rounded.
        private static GameItem CreateMod(ModItemDirectory directory, ModDef d)
        {
            GameItem item = directory.Empty().SetSpriteAndShapeFromMod(GunsmithMod.ModId, d.Id);
            item.name = Text.Pick(d.NameZh, d.NameEn);
            item.shortDescription = Text.ModDescription(d.Category);
            // the magazines say their calibre, which the gun's tooltip shows too (current/008 quote 1 point 4); a space between the two sentences but in Chinese
            if (d.Caliber != 0)
                item.shortDescription += (Text.Chinese() ? "" : " ") + string.Format(Text.Pick("适用口径：{0}。", "Caliber: {0}."), GunHelper.GetCaliberName(d.Caliber));
            item.unitValue = d.Value;
            switch (d.Slot)
            {
                case "rail":
                    GunModHelper.InitGunModRail(item);
                    break;
                case "barrel":
                    GunModHelper.InitGunModBarrel(item, false);
                    break;
                case "barrel_suppressor":
                    GunModHelper.InitGunModBarrel(item, true);
                    break;
                case "under":
                    GunModHelper.InitGunModUnderRail(item);
                    break;
                case "stock":
                    GunModHelper.InitGunModStock(item);
                    break;
                case "magazine":
                    GunModHelper.InitGunModMagazine(item);
                    break;
                default:
                    GunModHelper.InitGunModSpecial(item);
                    break;
            }
            int[] s = d.Stats;
            GunModHelper.InitGunMod(item, s[0], s[1], s[2], s[3], s[4], s[5]);
            GunModHelper.InitGunModBaseStats(item, Base(s[0]), Base(s[1]), Base(s[2]), Base(s[3]), Base(s[4]), Base(s[5]));
            // the guns this mod fits (src/Fittings.cs), the mod's and the game's; before 0.2.8 only magazines were
            // limited, by caliber
            GunModHelper.AllowOnlyWeapon(Fittings.Guns[d.Id], item);
            Mounting.Enable(item, d);
            return item;
        }

        internal static int Base(int bonus)
        {
            return (int)Math.Round(bonus * 0.3, MidpointRounding.AwayFromZero);
        }
    }
}
