using System;
using System.Collections.Generic;
using HarmonyLib;

namespace GunsmithExpansion
{
    // The ammunition reloader (final-strategy 36; picture current/005 U83; VVittgenstein 2026-11-02 quote 49). The machine is
    // built by the game's 3D printer code (MachinePrinter.Printer: battery slot, module bay, chip slot, input and output
    // trays; at the end of each cycle, while powered, it starts when the chip's materials and machine tags are there,
    // and hands out the batch when the work is done), with its own picture, name and value. A reloading die in the chip
    // slot sets the ammunition; each batch takes one metal ingot (bullets and cases) and one can of propellant, and makes
    // as many rounds as the game's gun part chip makes parts: 2 to 5 by the purity of the ingot (the die carries the
    // gun part chip tag, so MachinePrinter.CaptureIngotQuality and FinishPrinting count it by GUN_PART_COUNT_BY_PURITY).
    // The dies ask for the reloader's machine tag, so the game's printers do not run them, and the reloader starts
    // nothing but a die. Families: .22 and 10 mm (the game's rounds), .45, 12 gauge, intermediate and full-power rifle
    // rounds; the design draft lists heavy rounds and 40 mm grenades as ordered or black market only, so they have no
    // die. The retired gunsmith sells the reloader, a die and propellant from the second firearm permit on; the game's
    // lower level chemist sells propellant (the game has no propellant of its own; final-strategy 42 has it come from
    // chemistry). Values follow the game's (VVittgenstein 2026-11-02, current/005 quote 50): the reloader is worth the game's
    // printer (350), the machine it is built as; a die is worth 12 rounds of its ammunition, as the game's gun part chip
    // (300) is worth 12 gun parts (25), four batches of three; propellant is worth 10, so that a batch of the game's
    // 10 mm rounds at fair purity (3 rounds, 90) is worth 1.5 times its materials (an ingot, 50, and the propellant),
    // as a batch of the gun part chip (3 parts, 75, from an ingot) is. Work 24: a batch a night (final-strategy 36, and the machine's
    // description), the game's printer doing 24 a night without a module that changes its speed (MachinePrinter.Printer, InitProgressTypeMachine; done once the
    // progress reaches the work, IsProgressTypeMachineFinished). Until 0.7 it was 30, as the gun part chip, which took two nights (found 2026-11-07 by
    // current/T-007). Agent's decisions: one die per visit of a family the permit covers, the propellant counts and chance.
    internal static class Reloader
    {
        internal const string Id = "gse_reloader";
        internal const string PropellantId = "gse_propellant";
        internal const string OffPicture = "gse_reloader_off";
        internal const string OnPicture = "gse_reloader_on";
        internal const string IdentityTag = "GSE_RELOADER_TAG";
        internal const string DieTag = "GSE_RELOAD_DIE_TAG";
        internal const string MachineTag = "GSE Ammo Reloader";
        internal const int Value = 350;
        internal const int RoundsPerDieValue = 12;
        internal const int PropellantValue = 10;
        internal const int Work = 24;
        internal const int GunsmithPropellant = 3;
        internal const int ChemistPercent = 50;
        private static readonly Random rng = new Random();
        private static readonly List<Die> registered = new List<Die>();

        // A die of the game's .22 or 10 mm rounds (value 20 and 30, contraband level 1) or of a family of this mod.
        internal sealed class Die
        {
            internal string Id;
            internal string Ammo;
            internal int GameRoundValue;

            private GunsmithExpansion.Ammo.Family Family
            {
                get { return Array.Find(GunsmithExpansion.Ammo.Families, x => x.Id == Ammo); }
            }

            internal int RoundValue
            {
                get { return Family != null ? Family.Value : GameRoundValue; }
            }

            internal int Contraband
            {
                get { return Family != null ? Family.Contraband : 1; }
            }

            internal int Value
            {
                get { return (int)(Math.Round(RoundValue * RoundsPerDieValue / 10.0, MidpointRounding.AwayFromZero) * 10); }
            }
        }

        internal static readonly Die[] Dies =
        {
            new Die { Id = "gse_die_22", Ammo = "small_pistol_ammo", GameRoundValue = 20 },
            new Die { Id = "gse_die_10", Ammo = "heavy_pistol_ammo", GameRoundValue = 30 },
            new Die { Id = "gse_die_45", Ammo = "gse_ammo_45" },
            new Die { Id = "gse_die_12", Ammo = "gse_ammo_12" },
            new Die { Id = "gse_die_556", Ammo = "gse_ammo_556" },
            new Die { Id = "gse_die_762", Ammo = "gse_ammo_762" },
        };

        internal static IList<Die> Registered
        {
            get { return registered; }
        }

        internal static void Register(ModItemDirectory directory)
        {
            if (!ModHelper.ModFileExists(GunsmithMod.ModId, "Textures/Items/" + OffPicture + ".png")
                || !ModHelper.ModFileExists(GunsmithMod.ModId, "Textures/Items/" + PropellantId + ".png"))
            {
                GunsmithMod.Warn("the reloader's pictures are missing; no reloader");
                return;
            }
            Chemistry.Register();
            directory.Add(Id, CreateReloader);
            directory.Add(PropellantId, () => CreatePropellant(directory));
            foreach (Die die in Dies)
            {
                Die d = die;
                Ammo.Family f = Array.Find(Ammo.Families, x => x.Id == d.Ammo);
                if (f != null && !Ammo.IsRegistered(f))
                    continue;
                directory.Add(d.Id, () => CreateDie(directory, d));
                registered.Add(d);
            }
            MachineProgressHelper.SetMachineTagDisplayName(MachineTag, () => Text.Pick("复装机", "Ammo reloader"));
            GunsmithMod.Log("registered the reloader, propellant and " + registered.Count + " reloading dies");
        }

        private static GameItem CreateReloader()
        {
            GameItem machine;
            PrinterOutputPatch.KeepGameSize = true;
            try
            {
                machine = MachinePrinter.Printer();
            }
            finally
            {
                PrinterOutputPatch.KeepGameSize = false;
            }
            machine.EnableTag(IdentityTag, false);
            machine.EnableTag(MachineTag, false);
            machine.SetSpriteAndShapeFromMod(GunsmithMod.ModId, OffPicture);
            machine.name = Text.Pick("复装机", "Ammo Reloader");
            machine.shortDescription = Text.Pick(
                "复装弹药的机器。芯片槽放复装模具，再放入金属锭与发射药，通电后自动开工，夜里做完一批。每批用 1 个金属锭与 1 罐发射药，产量随金属锭纯度为 2 至 5 份；发射药纯度不低于 80% 时多 1 份，低于 45% 时少 1 份。",
                "A machine that reloads ammunition. Put a reloading die in the chip slot and metal ingots and propellant in the tray; once powered it starts on its own and finishes a batch overnight. Each batch takes 1 metal ingot and 1 can of propellant and makes 2 to 5 rounds by the ingot's purity; propellant of at least 80% purity adds 1 round, below 45% takes 1 away.");
            machine.unitValue = Value;
            return machine;
        }

        private static GameItem CreatePropellant(ModItemDirectory directory)
        {
            GameItem item = directory.Empty().SetSpriteAndShapeFromMod(GunsmithMod.ModId, PropellantId);
            item.name = Text.Pick("发射药", "Propellant");
            item.shortDescription = Text.Pick("一罐无烟发射药，复装机的耗材：每批复装用 1 罐。化学制剂成型机可用硝化棉制成：植物质与酸在 40°C 至 90°C 之间反应，生成硝化棉和水；韧棉织物放进搅拌机可得到植物质。",
                "A can of smokeless propellant for the ammo reloader: each batch takes 1 can. The chem finisher makes it from nitrocellulose: plant matter and acid react between 40°C and 90°C into nitrocellulose and water; kotton fabric goes into the blender as plant matter.");
            item.SetValue(PropellantValue);
            Chemistry.InitPropellant(item);
            return item;
        }

        // Built like the game's gun part chip (ModuleDirectory.CreateCncChip with the gun part tag), with the
        // reloader's machine tag for the printing modules and a second material.
        private static GameItem CreateDie(ModItemDirectory directory, Die d)
        {
            GameItem die = directory.Empty().SetSpriteAndShape("Items/items_tool", "printer_chip6");
            GameItem round = DirectoryMaster.Item(d.Ammo, false);
            die.name = string.Format(Text.Pick("{0}复装模具", "{0} Reloading Die"), round != null ? round.name : d.Ammo);
            die.shortDescription = Text.Pick(
                "放进复装机的芯片槽，复装这种弹药。每批用 1 个金属锭与 1 罐发射药，产量随金属锭纯度为 2 至 5 份。",
                "Goes in the ammo reloader's chip slot to reload this ammunition. Each batch takes 1 metal ingot and 1 can of propellant and makes 2 to 5 rounds by the ingot's purity.");
            die.unitValue = d.Value;
            die.EnableTag("PRINTER_CHIP_TAG", false);
            die.EnableTag("PRINTER_GUN_PART_CHIP_TAG", false);
            die.EnableTag(DieTag, false);
            MachineProgressHelper.SetDualAdditionalRequiredItem(die, new ValueTuple<string, int>("metal_ingot", 1),
                new ValueTuple<string, int>(PropellantId, 1));
            MachineProgressHelper.InitProgressSourceItem(die, Work, d.Ammo, true, 3, MachineTag);
            AudioHelper.InitSmallModule(die);
            return die;
        }

        private static void Put(string id, int count)
        {
            for (int i = 0; i < count; i++)
                Sellers.Offer(id);
        }

        // The retired gunsmith, from the second firearm permit on: the reloader, a die of a family the permit covers,
        // and propellant.
        internal static void StockRetiredGunsmith(StoreClient client)
        {
            int top = Ammo.PermitTop();
            List<Die> dies = registered.FindAll(d => d.Contraband <= top);
            if (client == null || top < 2 || dies.Count == 0)
                return;
            Die pick = dies[rng.Next(dies.Count)];
            Sellers.AfterStock(client, () =>
            {
                if (PlayerStore.Instance == null)
                    return;
                Put(Id, 1);
                Put(pick.Id, 1);
                Put(PropellantId, GunsmithPropellant);
            });
        }

        // The game's lower level chemist: propellant.
        internal static void StockChemist(StoreClient client)
        {
            if (client == null || registered.Count == 0 || rng.Next(100) >= ChemistPercent)
                return;
            int count = 2 + rng.Next(3);
            Sellers.AfterStock(client, () =>
            {
                if (PlayerStore.Instance != null)
                    Put(PropellantId, count);
            });
        }
    }

    // The printer code sets the printer's pictures by name at each cycle and when a batch is done; the reloader takes
    // its own pictures instead.
    [HarmonyPatch(typeof(GameItem), nameof(GameItem.SetSpriteAndShape), new[] { typeof(string), typeof(string) })]
    internal static class ReloaderPicturePatch
    {
        private static bool Prefix(GameItem __instance, string spritePath, ref GameItem __result)
        {
            if ((spritePath != "printer_on" && spritePath != "printer_off") || __instance == null
                || !__instance.IsTag(Reloader.IdentityTag))
                return true;
            __result = __instance.SetSpriteAndShapeFromMod(GunsmithMod.ModId,
                spritePath == "printer_on" ? Reloader.OnPicture : Reloader.OffPicture);
            return false;
        }
    }

    [HarmonyPatch(typeof(MachineProgressHelper), nameof(MachineProgressHelper.CanStart))]
    internal static class ReloaderStartPatch
    {
        private static void Postfix(GameItem sourceItem, GameItem machine, ref bool __result)
        {
            if (__result && machine != null && machine.IsTag(Reloader.IdentityTag)
                && (sourceItem == null || !sourceItem.IsTag(Reloader.DieTag)))
                __result = false;
        }
    }

    [HarmonyPatch(typeof(StoreClientList), nameof(StoreClientList.CreateLowerLevelChemist), new[] { typeof(bool) })]
    internal static class ReloaderChemistPatch
    {
        private static void Postfix(StoreClient __result)
        {
            Reloader.StockChemist(__result);
        }
    }
}
