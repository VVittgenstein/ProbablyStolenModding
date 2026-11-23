using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using UnityEngine;

namespace GunsmithExpansion
{
    // What Lincoln Arms sells, by favor level and weapon permit (final-strategy 342 ② to ⑨, 349, 350 ① ②, 356):
    //   - Lv.0: guns of tier one (this mod's 32 and the game's handmade pistol, heavy handmade pistol, police handgun and revolver;
    //     permit I), in good condition; both manuals, the pistol cabinet, the chip box, the 7 repair materials, the first 2 material
    //     packs, the 5 rounds of tier one (the game's .22 and 10 mm, this mod's 12 gauge, 7.62 and .45; permit I), propellant, the
    //     chips of tier one and the blank chip of level 1, the game's basic printer chips; gun mods T1.
    //   - Lv.1: guns of tier two (this mod's 46 and the game's SMG and shotgun; permit II); the long gun cabinet, the reloader and its
    //     dies, the 5.56 rounds (permit II), the 3 printing modules, the chips of tier two and the blank chip of level 2 (permit II),
    //     the third pack; gun mods T2.
    //   - Lv.2: guns of tier three (permit III), excellent condition too; the chips of tier three and the blank chip of level 3
    //     (permit III), the fourth pack; gun mods T3; the ammunition box (349).
    //   - Lv.3: new condition too; gun mods T4. Lv.4: the regular guns only excellent or new.
    //   - Rare guns, at night only and rolled on each visit (342 ⑦): an old or antique gun of tier one (5, 8, 10, 12, 15% by level),
    //     the 2077 Add-On's tier three guns (Lv.2 6%, Lv.3 10%, Lv.4 12%), this mod's special heavy guns (Lv.3 5%, Lv.4 8%),
    //     the 2077 Add-On's special guns (Lv.4 6%); all with permit III but the old guns (permit I), the add-on's only with its switch on.
    //   - Without any permit: no guns and no live rounds (342 ⑧).
    // Daily stock, back in full every game day and shared by the shop and the phone (350 ②): a gun 1 a model, a gun mod 3, rounds
    // 6 a kind, the manuals, chips, modules, cabinets, chip box and ammunition box 1 each, the repair materials 10 each, the packs
    // without limit.
    // Prices (350 ①): what came from the retired gunsmith's phone catalog (chips of the game and of this mod, modules, cabinets,
    // chip box) keeps the phone's price, value x 1.3 x the retired supplier markup of the difficulty; the packs keep 185, 265, 355
    // and 600 on the normal difficulty; the rest is the value after condition x the retired supplier markup (1.15 on normal); then
    // the favor discount. In the shop they are the game's price features of the item (as Dr. Jackson's markup,
    // StoreClientList.CreateInventor), and the game's price of them, rounded up (LincolnPriceRoundingPatch), is what the shop and the
    // phone charge (LincolnGoods.Price).
    // The game's gun mods of the tiers are those it has (GunModDirectory.InitDirectory, read 2026-11-22): it has no holographic
    // sight and no scope, so the tiers T3 and T4 have none of the game's.
    // Agent's arrangements: propellant 6 a day as the rounds; the reloader and dies 1 each, a die only for rounds of the permits held;
    // the third and fourth packs with permits II and III as on the phone before (the first two are the ones sold without a permit,
    // 342 ⑧); the old and rare guns in the condition of their level, at Lv.4 that of Lv.3 (Lv.4 names the regular guns); "T3 and T4
    // twice as likely" at Lv.4 (342 ⑥, written for a display of random goods before every unlocked good was put out) as 6 a day of
    // those gun mods instead of 3.
    internal static class LincolnGoods
    {
        internal enum Kind
        {
            Gun,
            RareGun,
            GunMod,
            Rounds,
            Reloading,
            Material,
            Pack,
            Chip,
            Module,
            Storage,
            Book,
        }

        internal enum Pricing
        {
            Markup,
            PhoneCatalog,
            Pack,
        }

        internal sealed class Good
        {
            internal string Key;
            internal string ItemId;
            internal Kind Kind;
            internal Pricing Pricing;
            internal int Level;
            internal int Permit;
            internal int PerDay;
            internal int Tier;
            internal GunDef Gun;
            internal Phone.Pack Pack;
        }

        internal static readonly string[] GameGunsTier1 = { "handmade_pistol", "heavy_handmade_pistol", "glock_receiver", "revolver" };
        internal static readonly string[] GameGunsTier2 = { "smg", "shotgun" };
        internal static readonly string[] RoundsTier1 = { "small_pistol_ammo", "heavy_pistol_ammo", "gse_ammo_12", "gse_ammo_762", "gse_ammo_45" };
        internal const string RoundsTier2 = "gse_ammo_556";

        // 342 ⑨, and the game's gun mods of each tier that the game has.
        internal static readonly string[][] ModTiers =
        {
            new[]
            {
                "gse_a056", "gse_a078", "gse_a011", "gse_a101", "gse_a010", "gse_a018", "gse_a019", "gse_a033", "gse_a040", "gse_a054",
                "gse_a052", "gse_a058", "gse_a077", "gse_a023", "gse_a048", "gse_a057", "gse_a063", "gse_a079",
                "rds_makeshift", "laser_toy", "silencer_makeshift",
            },
            new[]
            {
                "gse_a008", "gse_a009", "gse_a074", "gse_a089", "gse_a092", "gse_a100", "gse_a012", "gse_a013", "gse_a028", "gse_a044",
                "gse_a082", "gse_a016", "gse_a059", "gse_a060", "gse_a064", "gse_a070", "gse_a035", "gse_a055", "gse_a020", "gse_a030",
                "gse_a037", "gse_a065", "gse_a073", "gse_a027", "gse_a050", "gse_a097", "gse_a090", "gse_a107", "gse_a109",
                "rds", "laser", "silencer", "internal_springs_improved",
            },
            new[]
            {
                "gse_a005", "gse_a006", "gse_a007", "gse_a025", "gse_a031", "gse_a067", "gse_a075", "gse_a086", "gse_a095", "gse_a096",
                "gse_a017", "gse_a021", "gse_a049", "gse_a087", "gse_a045", "gse_a088", "gse_a093", "gse_a103",
                "silencer2", "laser2",
            },
            new[] { "gse_a014", "gse_a046", "gse_a051", "gse_a061", "gse_a072", "gse_a080", "gse_a110" },
        };

        internal static readonly int[] OldGunPercent = { 5, 8, 10, 12, 15 };
        internal static readonly int[] AddOnTier3Percent = { 0, 0, 6, 10, 12 };
        internal static readonly int[] SpecialPercent = { 0, 0, 0, 5, 8 };
        internal static readonly int[] AddOnSpecialPercent = { 0, 0, 0, 0, 6 };

        private const string UsedKey = "lincoln_used";
        private static readonly System.Random rng = new System.Random();
        private static List<Good> all;
        private static readonly Dictionary<string, int[]> shapes = new Dictionary<string, int[]>();
        private static Dictionary<string, int> used;
        private static int usedDay = -1;

        private static string TierName(int tier)
        {
            return tier == 1 ? "一档" : tier == 2 ? "二档" : "三档";
        }

        // Every good there can be, in the order they are put out and listed. Built once, after the items are registered.
        internal static List<Good> All()
        {
            if (all != null)
                return all;
            var list = new List<Good>();
            for (int tier = 1; tier <= 3; tier++)
            {
                foreach (GunDef d in Catalog.Guns)
                {
                    if (d.Tier == TierName(tier) && d.ClassName != "老式与古董")
                        list.Add(new Good { Key = d.Id, ItemId = d.Id, Kind = Kind.Gun, Pricing = Pricing.Markup, Level = tier - 1, Permit = tier, PerDay = 1, Tier = tier, Gun = d });
                }
                foreach (string id in tier == 1 ? GameGunsTier1 : tier == 2 ? GameGunsTier2 : new string[0])
                    list.Add(new Good { Key = id, ItemId = id, Kind = Kind.Gun, Pricing = Pricing.Markup, Level = tier - 1, Permit = tier, PerDay = 1, Tier = tier });
            }
            list.Add(Simple(Cabinets.SmallId, Kind.Storage, Pricing.PhoneCatalog, 0, 0, 1));
            list.Add(Simple(Cabinets.LargeId, Kind.Storage, Pricing.PhoneCatalog, 1, 0, 1));
            list.Add(Simple(ChipBox.Id, Kind.Storage, Pricing.PhoneCatalog, 0, 0, 1));
            list.Add(Simple(LincolnItems.AmmoBoxId, Kind.Storage, Pricing.Markup, 2, 0, 1));
            for (int t = 0; t < ModTiers.Length; t++)
            {
                foreach (string id in ModTiers[t])
                {
                    Good g = Simple(id, Kind.GunMod, Pricing.Markup, t, 0, 3);
                    g.Tier = t + 1;
                    list.Add(g);
                }
            }
            foreach (string id in RoundsTier1)
                list.Add(Simple(id, Kind.Rounds, Pricing.Markup, 0, 1, 6));
            list.Add(Simple(RoundsTier2, Kind.Rounds, Pricing.Markup, 1, 2, 6));
            list.Add(Simple(Reloader.PropellantId, Kind.Reloading, Pricing.Markup, 0, 1, 6));
            list.Add(Simple(Reloader.Id, Kind.Reloading, Pricing.Markup, 1, 0, 1));
            foreach (Reloader.Die die in Reloader.Dies)
                list.Add(Simple(die.Id, Kind.Reloading, Pricing.Markup, 1, Math.Max(0, die.Contraband), 1));
            for (int tier = 1; tier <= 3; tier++)
            {
                list.Add(Simple(Production.BlankIdOf(tier), Kind.Chip, Pricing.Markup, tier - 1, tier == 1 ? 0 : tier, 1));
                foreach (GunDef d in Catalog.Guns)
                {
                    if (d.Tier == TierName(tier) && Production.HasChip(d))
                        list.Add(Simple(Production.ChipId(d), Kind.Chip, Pricing.PhoneCatalog, tier - 1, tier == 1 ? 0 : tier, 1));
                }
            }
            foreach (string id in Phone.AttachmentChips)
                list.Add(Simple(id, Kind.Chip, Pricing.PhoneCatalog, 0, 0, 1));
            foreach (string id in Phone.WeaponChips)
                list.Add(Simple(id, Kind.Chip, Pricing.PhoneCatalog, 0, 0, 1));
            foreach (string id in Phone.PermitIIWeaponChips)
                list.Add(Simple(id, Kind.Chip, Pricing.PhoneCatalog, 1, 2, 1));
            foreach (string id in Phone.Modules)
                list.Add(Simple(id, Kind.Module, Pricing.PhoneCatalog, 1, 0, 1));
            foreach (string id in RepairGoods.Materials)
                list.Add(Simple(id, Kind.Material, Pricing.Markup, 0, 0, 10));
            int[] packLevel = { 0, 0, 1, 2 }, packPermit = { 0, 0, 2, 3 };
            for (int i = 0; i < Phone.MaterialPacks.Length; i++)
            {
                Phone.Pack p = Phone.MaterialPacks[i];
                list.Add(new Good { Key = "pack:" + p.Id, ItemId = p.Id, Kind = Kind.Pack, Pricing = Pricing.Pack, Level = packLevel[Math.Min(i, 3)], Permit = packPermit[Math.Min(i, 3)], PerDay = 0, Pack = p });
            }
            list.Add(Simple(Manual.GunsmithingManual.ItemId, Kind.Book, Pricing.Markup, 0, 0, 1));
            list.Add(Simple(Manual.FirearmsCatalog.ItemId, Kind.Book, Pricing.Markup, 0, 0, 1));
            all = list;
            return all;
        }

        private static Good Simple(string id, Kind kind, Pricing pricing, int level, int permit, int perDay)
        {
            return new Good { Key = id, ItemId = id, Kind = kind, Pricing = pricing, Level = level, Permit = permit, PerDay = perDay };
        }

        internal static Good Find(string key)
        {
            foreach (Good g in All())
            {
                if (g.Key == key)
                    return g;
            }
            return RareGood(key);
        }

        // Whether the game can make the item (an item of this mod is registered only when its picture is there).
        internal static bool Exists(Good g)
        {
            if (g == null)
                return false;
            if (g.Kind == Kind.Pack)
                return DeliveryBoxes.Registered(DeliveryBoxes.CartonId);
            return Probe(g.ItemId) != null;
        }

        // The cells an item takes unturned (width, height), read from known sizes or mod sprites without instantiating items;
        // falls back once to a DirectoryMaster probe for game-owned or unknown item IDs.
        internal static int[] Cells(Good g)
        {
            if (g == null)
                return null;
            return Probe(g.Kind == Kind.Pack ? DeliveryBoxes.CartonId : g.ItemId);
        }

        private static LoadedMod FindLoadedMod(string modId)
        {
            List<LoadedMod> mods = ModLoader.Mods;
            if (mods == null)
                return null;
            for (int i = 0; i < mods.Count; i++)
            {
                LoadedMod m = mods[i];
                if (m != null && m.Manifest != null && m.Manifest.ID == modId)
                    return m;
            }
            return null;
        }

        private static int[] CellsFromModSprite(string modId, string spriteKey)
        {
            LoadedMod mod = FindLoadedMod(modId);
            Sprite sprite;
            if (mod == null || mod.Sprites == null || !mod.Sprites.TryGetValue(spriteKey, out sprite) || sprite == null)
                return null;
            int gridSize = UISettings.current != null && UISettings.current.gridSize > 0 ? UISettings.current.gridSize : 16;
            Rect r = sprite.rect;
            return new[] { Math.Max(1, (int)r.width / gridSize), Math.Max(1, (int)r.height / gridSize) };
        }

        // Mirrors GameItem.SetSpriteAndShapeFromMod (width = (int)rect.width / gridSize, height = (int)rect.height / gridSize)
        // and known 1x1 printer chips/dies so arriving at Lincoln Arms does not instantiate hundreds of temporary GameItems.
        private static int[] FastCells(string id)
        {
            if (id.StartsWith("gse_chip_", StringComparison.Ordinal)
                || id.StartsWith("gse_die_", StringComparison.Ordinal)
                || id.StartsWith("printer_chip_", StringComparison.Ordinal))
                return new[] { 1, 1 };

            if (id == Cabinets.SmallId)
                return CellsFromModSprite(GunsmithMod.ModId, Cabinets.SmallId + "_" + Cabinets.StartSlots + "_0");
            if (id == Cabinets.LargeId)
                return CellsFromModSprite(GunsmithMod.ModId, Cabinets.LargeId + "_" + Cabinets.StartSlots + "_0");
            if (id == LincolnItems.AmmoBoxId)
                return CellsFromModSprite(GunsmithMod.ModId, LincolnItems.AmmoBoxClosed);
            if (id == Reloader.Id)
                return CellsFromModSprite(GunsmithMod.ModId, Reloader.OffPicture);
            if (id == Production.BlankId)
                return CellsFromModSprite(GunsmithMod.ModId, Production.BlankId + "_1");

            foreach (Ammo.Family f in Ammo.Families)
            {
                if (f.Id == id)
                    return CellsFromModSprite(GunsmithMod.ModId, f.Picture);
            }

            if (id.StartsWith("gse_", StringComparison.Ordinal))
            {
                int[] own = CellsFromModSprite(GunsmithMod.ModId, id);
                if (own != null)
                    return own;
                if (AddOn2077.Find(id) != null)
                    return CellsFromModSprite(AddOn2077.ModId, id);
            }

            return null;
        }

        private static int[] Probe(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;
            int[] cells;
            if (shapes.TryGetValue(id, out cells))
                return cells;
            if (!DirectoryMaster.Has<GameItem>(id))
            {
                shapes[id] = null;
                return null;
            }
            cells = FastCells(id);
            if (cells != null)
            {
                shapes[id] = cells;
                return cells;
            }
            GameItem probe = null;
            try
            {
                probe = DirectoryMaster.Item(id, false);
                GridShape s = probe != null ? probe.modifiedShape ?? probe.shape : null;
                cells = s != null ? new[] { Math.Max(1, s.width), Math.Max(1, s.height) } : null;
            }
            catch (Exception)
            {
                cells = null;
            }
            if (probe != null)
                probe.Destroy();
            shapes[id] = cells;
            return cells;
        }

        // The regular goods of the level and the permit held, in order.
        internal static List<Good> Unlocked(int level, int permit)
        {
            var list = new List<Good>();
            foreach (Good g in All())
            {
                if (g.Level <= level && g.Permit <= permit && Exists(g))
                    list.Add(g);
            }
            return list;
        }

        // A rare gun is a good of its own, made when it turns up: one a model a day.
        private static Good RareGood(string id)
        {
            GunDef d = null;
            foreach (GunDef c in Catalog.Guns)
            {
                if (c.Id == id)
                    d = c;
            }
            if (d == null)
                d = AddOn2077.Find(id);
            if (d == null)
                return null;
            return new Good { Key = d.Id, ItemId = d.Id, Kind = Kind.RareGun, Pricing = Pricing.Markup, PerDay = 1, Gun = d, Tier = d.Tier == "特殊" ? 4 : 3 };
        }

        // The rare guns of one visit (342 ④ ⑤ ⑥ ⑦): each group rolled on its own, one gun of a group that comes up.
        internal static List<Good> RollRare(int level, int permit)
        {
            var found = new List<Good>();
            if (permit >= 1)
                TryRare(found, OldGunPercent[level], d => d.Tier == "一档" && d.ClassName == "老式与古董", Catalog.Guns);
            if (permit >= 3)
            {
                TryRare(found, SpecialPercent[level], d => d.Tier == "特殊", Catalog.Guns);
                if (AddOn2077.Enabled())
                {
                    TryRare(found, AddOnTier3Percent[level], d => d.Tier == "三档", AddOn2077.Guns);
                    TryRare(found, AddOnSpecialPercent[level], d => d.Tier == "特殊", AddOn2077.Guns);
                }
            }
            return found;
        }

        private static void TryRare(List<Good> found, int percent, Func<GunDef, bool> fits, IEnumerable<GunDef> guns)
        {
            if (percent <= 0 || rng.Next(100) >= percent)
                return;
            var pool = new List<GunDef>();
            foreach (GunDef d in guns)
            {
                if (fits(d))
                    pool.Add(d);
            }
            while (pool.Count > 0)
            {
                GunDef d = pool[rng.Next(pool.Count)];
                Good g = RareGood(d.Id);
                if (g != null && Exists(g) && Left(g) > 0)
                {
                    found.Add(g);
                    return;
                }
                pool.Remove(d);
            }
        }

        internal static bool IsRegularGun(Good g)
        {
            return g != null && g.Kind == Kind.Gun;
        }

        // How many a day (350 ②), the gun mods of T3 and T4 twice at Lv.4 (Agent's arrangement, see above).
        internal static int PerDay(Good g)
        {
            if (g.Kind == Kind.GunMod && g.Tier >= 3 && Lincoln.Level() >= 4)
                return g.PerDay * 2;
            return g.PerDay;
        }

        private static void LoadUsed()
        {
            int today = Lincoln.Day();
            if (used != null && usedDay == today)
                return;
            used = new Dictionary<string, int>();
            usedDay = today;
            string text = ModHelper.GetModData(GunsmithMod.ModId, UsedKey, "") ?? "";
            int bar = text.IndexOf('|');
            int day;
            if (bar <= 0 || !int.TryParse(text.Substring(0, bar), NumberStyles.Integer, CultureInfo.InvariantCulture, out day) || day != today)
                return;
            foreach (string entry in text.Substring(bar + 1).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = entry.LastIndexOf('=');
                int n;
                if (eq > 0 && int.TryParse(entry.Substring(eq + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out n))
                    used[entry.Substring(0, eq)] = n;
            }
        }

        private static void SaveUsed()
        {
            var parts = new List<string>();
            foreach (KeyValuePair<string, int> kv in used)
                parts.Add(kv.Key + "=" + kv.Value.ToString(CultureInfo.InvariantCulture));
            ModHelper.SetModData(GunsmithMod.ModId, UsedKey, usedDay.ToString(CultureInfo.InvariantCulture) + "|" + string.Join(";", parts.ToArray()));
        }

        // A save loaded on another day, or the day turning, starts from the full stock.
        internal static void Forget()
        {
            used = null;
            usedDay = -1;
        }

        internal static int Left(Good g)
        {
            if (g == null)
                return 0;
            int perDay = PerDay(g);
            if (perDay <= 0)
                return int.MaxValue;
            LoadUsed();
            int n;
            used.TryGetValue(g.Key, out n);
            return Math.Max(0, perDay - n);
        }

        internal static void Take(Good g, int count)
        {
            if (g == null || PerDay(g) <= 0 || count <= 0)
                return;
            LoadUsed();
            int n;
            used.TryGetValue(g.Key, out n);
            used[g.Key] = n + count;
            SaveUsed();
        }

        // A gun's condition at the shop (342 ② to ⑥) and the conditions the phone offers.
        internal static ItemFeatureList.EquipmentCondition RollCondition(Good g, int level)
        {
            int roll = rng.Next(100);
            if (level >= 4 && g.Kind == Kind.Gun)
                return roll < 50 ? ItemFeatureList.EquipmentCondition.Excellent : ItemFeatureList.EquipmentCondition.New;
            if (level >= 3)
                return roll < 30 ? ItemFeatureList.EquipmentCondition.Good : roll < 70 ? ItemFeatureList.EquipmentCondition.Excellent : ItemFeatureList.EquipmentCondition.New;
            if (level >= 2)
                return roll < 50 ? ItemFeatureList.EquipmentCondition.Good : ItemFeatureList.EquipmentCondition.Excellent;
            return ItemFeatureList.EquipmentCondition.Good;
        }

        internal static List<ItemFeatureList.EquipmentCondition> PhoneConditions(int level)
        {
            var list = new List<ItemFeatureList.EquipmentCondition> { ItemFeatureList.EquipmentCondition.Good };
            if (level >= 2)
                list.Add(ItemFeatureList.EquipmentCondition.Excellent);
            if (level >= 3)
                list.Add(ItemFeatureList.EquipmentCondition.New);
            return list;
        }

        internal static bool IsGun(Good g)
        {
            return g != null && (g.Kind == Kind.Gun || g.Kind == Kind.RareGun);
        }

        // The item of a good, not owned, with its condition; a pack in its labelled carton. null when it cannot be made.
        internal static GameItem Make(Good g, ItemFeatureList.EquipmentCondition condition)
        {
            if (g == null)
                return null;
            if (g.Kind == Kind.Pack)
                return MakePack(g);
            GameItem item = DirectoryMaster.Item(g.ItemId, true);
            if (item == null)
                return null;
            if (IsGun(g))
                GunHelper.SetEquipmentCondition(item, condition);
            GeneralHelper.SetItemOwned(item, false);
            return item;
        }

        private static GameItem MakePack(Good g)
        {
            GameItem box = DeliveryBoxes.FilledPack(g.Pack);
            if (box == null)
                return null;
            GeneralHelper.SetItemOwned(box, false);
            DeliveryBoxes.SetContentsOwned(box, false);
            // The carton is worth what the pack costs less what is in it, so that the pack costs its price (GameItem.GetNegociatedValue
            // adds the contents); its contents keep their own values.
            long inside = DeliveryBoxes.ContentsValue(box);
            box.SetValue(Math.Max(0, Phone.PackPrice(g.Pack) - inside));
            return box;
        }

        internal const string MarkupFeature = "gseLincolnMarkup";
        internal const string CatalogFeature = "gseLincolnCatalog";
        internal const string DiscountFeature = "gseLincolnDiscount";

        internal static int Markup()
        {
            return Difficulty.Rules != null ? Difficulty.Rules.retiredSupplierMarkup : 15;
        }

        // The price features of the shop on an item of it (as Dr. Jackson's, ItemFeatureList.InventorMarkup): the supplier markup in
        // the market stage, which the game adds to the item's other market changes, and the phone's 1.3 and the discount in the final
        // stage, which the game multiplies.
        internal static void AddPriceFeatures(GameItem item, Good g, int discount)
        {
            if (item == null || g == null || GeneralHelper.IsItemOwned(item))
                return;
            if (g.Pricing != Pricing.Pack && !item.IsAlreadyContainFeatureWithId(MarkupFeature) && Markup() != 0)
                item.AddItemFeature(Feature(MarkupFeature, ItemFeature.ValueStage.Market, Markup()));
            if (g.Pricing == Pricing.PhoneCatalog && !item.IsAlreadyContainFeatureWithId(CatalogFeature))
                item.AddItemFeature(Feature(CatalogFeature, ItemFeature.ValueStage.Final, (int)Math.Round((Phone.PriceFactor - 1) * 100)));
            if (discount > 0 && !item.IsAlreadyContainFeatureWithId(DiscountFeature))
                item.AddItemFeature(Feature(DiscountFeature, ItemFeature.ValueStage.Final, -discount));
        }

        private static ItemFeature Feature(string id, ItemFeature.ValueStage stage, int percent)
        {
            var f = new ItemFeature((ItemFeature.FeatureType)6);
            f.identifier = id;
            f.category = id;
            f.valueStage = stage;
            string text = LocHelper.GetLocalizedMechanic(percent < 0 ? "item_feature_discount" : "item_feature_supplier_markup", new object[0]);
            f.SetPublicDisplay(text);
            f.SetActualDisplay(text);
            f.isFeatureMatch = true;
            f.SetPreExposedValueMod(percent, true);
            return f;
        }

        // What a good costs now (the phone, and what the shop shows): its item made, the shop's features added and the game's price
        // read (PlayerStore.BuyItem pays GameItem.GetNegociatedValue). -1 when it cannot be made.
        internal static int Price(Good g, ItemFeatureList.EquipmentCondition condition)
        {
            GameItem item = Make(g, condition);
            if (item == null)
                return -1;
            try
            {
                AddPriceFeatures(item, g, Lincoln.Discount());
                return (int)Math.Max(0, item.GetNegociatedValue());
            }
            finally
            {
                item.Destroy();
            }
        }

        internal static bool HasShopPrice(GameItem item)
        {
            return item.IsAlreadyContainFeatureWithId(MarkupFeature) || item.IsAlreadyContainFeatureWithId(CatalogFeature)
                || item.IsAlreadyContainFeatureWithId(DiscountFeature);
        }

        // 350 ①: the prices are rounded up ("向上取整": value 50 sells for 58 on normal), and the goods of the old phone catalog keep their
        // prices (Math.Ceiling in Phone.Price before T-013). The game rounds a price to the nearest credit, a half to the even one
        // (GameItem.ComposeStagedValue, Math.Round): for an item of the shop the same sum is read again here, as
        // GameItem.GetNegociatedValue and AccumulateFeatureStages read it (IL read 2026-11-22), and rounded up. One rounding at the
        // end, after the discount (Agent's arrangement: 350 does not say how a discounted price is rounded).
        internal static long RoundedUp(GameItem item, long rounded)
        {
            long innate = 0, market = 0;
            double final = 1;
            if (item.itemFeatures != null)
            {
                foreach (ItemFeature f in item.itemFeatures)
                {
                    if (f == null || f.isDisabled)
                        continue;
                    ItemFeature.ValueStage stage = f.GetValueStage();
                    if (stage == ItemFeature.ValueStage.Market)
                        market += f.GetCurrentValueMod();
                    else if (stage == ItemFeature.ValueStage.Final)
                        final *= Math.Max(0, 1 + f.GetCurrentValueMod() / 100.0);
                    else
                        innate += f.GetCurrentValueMod();
                }
            }
            long total = item.unitValue + item.GetComplexeItemValue();
            double sum = ((Math.Max(0, total * (1 + innate / 100.0)) + WaterHelper.GetContainerPrice(item)) * Math.Max(0, 1 + market / 100.0)
                + item.GetItemMiscValue(true, true, true, false, true)) * final;
            long up = (long)Math.Ceiling(sum - 1e-9);
            // A sum read as the game reads it rounds up to the game's price or to 1 more; anything else keeps the game's price.
            return up >= rounded && up - rounded <= 1 ? up : rounded;
        }

        internal static string Name(Good g, ItemFeatureList.EquipmentCondition condition)
        {
            if (g.Kind == Kind.Pack)
                return Phone.PackName(g.Pack);
            GameItem blueprint = DirectoryMaster.Item(g.ItemId, false);
            string name = blueprint != null ? blueprint.name : g.ItemId;
            if (blueprint != null)
                blueprint.Destroy();
            if (!IsGun(g) || condition == ItemFeatureList.EquipmentCondition.Good)
                return name;
            return string.Format(Text.Pick("{0}（{1}）", "{0} ({1})"), name, ConditionName(condition));
        }

        internal static string ConditionName(ItemFeatureList.EquipmentCondition condition)
        {
            switch (condition)
            {
                case ItemFeatureList.EquipmentCondition.New: return Text.Pick("全新", "new");
                case ItemFeatureList.EquipmentCondition.Excellent: return Text.Pick("极佳", "excellent");
                case ItemFeatureList.EquipmentCondition.Fair: return Text.Pick("一般", "fair");
                case ItemFeatureList.EquipmentCondition.Poor: return Text.Pick("较差", "poor");
                default: return Text.Pick("良好", "good");
            }
        }
    }

    // The rounding up of the shop's prices (LincolnGoods.RoundedUp), wherever the game reads the price of an item of the shop: the trade
    // window (NegociationUIManager.BuildBuying) and the payment (PlayerStore.BuyItem) both read GameItem.GetNegociatedValue.
    [HarmonyPatch(typeof(GameItem), nameof(GameItem.GetNegociatedValue))]
    internal static class LincolnPriceRoundingPatch
    {
        private static void Postfix(GameItem __instance, ref long __result)
        {
            try
            {
                if (__instance != null && !GeneralHelper.IsItemOwned(__instance) && LincolnGoods.HasShopPrice(__instance))
                    __result = LincolnGoods.RoundedUp(__instance, __result);
            }
            catch (Exception e)
            {
                GunsmithMod.Warn("Lincoln Arms price: " + e.Message);
            }
        }
    }
}
