namespace GunsmithExpansion
{
    // Which mod slots a gun opens. Long guns get the slots of the game's SMG and shotgun (stock, rail, barrel,
    // magazine, underbarrel, three special slots); pistols lose the stock and one special slot; revolvers, antiques,
    // the heavy machine gun and the grenade launchers take no mods, like the game's revolver.
    internal enum Slots
    {
        None,
        Pistol,
        Long,
    }

    // One gun of the catalog. Stats are firepower, style, reliability, recoil control, ergonomics and accuracy, in
    // the order of GunHelper.InitGun.
    internal sealed class GunDef
    {
        internal readonly string Id;
        internal readonly string Code;
        internal readonly string Tier;
        internal readonly string ClassName;
        internal readonly string NameZh;
        internal readonly string NameEn;
        internal readonly string Maker;
        internal readonly int Value;
        internal readonly int Contraband;
        internal readonly string Line;
        internal readonly int Caliber;
        internal readonly int Repairability;
        internal readonly Slots Slots;
        internal readonly bool Heavy;
        internal readonly bool Antique;
        internal readonly bool Suppressed;
        internal readonly int[] Stats;
        internal readonly string Size;
        // One of the 2077 Add-On's guns (src/AddOn2077.cs): its picture is in that mod.
        internal readonly bool AddOn;
        // The add-on's guns bring their descriptions in guns.tsv; the catalog's are in Catalog.GunTexts (Text.GunDescription).
        internal string DescZh;
        internal string DescEn;
        internal string DescRu;

        internal GunDef(string id, string code, string tier, string className, string nameZh, string nameEn, string maker,
            int value, int contraband, string line, int caliber, int repairability, Slots slots, bool heavy, bool antique,
            bool suppressed, int[] stats, string size, bool addOn = false)
        {
            AddOn = addOn;
            Id = id;
            Code = code;
            Tier = tier;
            ClassName = className;
            NameZh = nameZh;
            NameEn = nameEn;
            Maker = maker;
            Value = value;
            Contraband = contraband;
            Line = line;
            Caliber = caliber;
            Repairability = repairability;
            Slots = slots;
            Heavy = heavy;
            Antique = antique;
            Suppressed = suppressed;
            Stats = stats;
            Size = size;
        }
    }

    // One gun mod of the catalog. Slot is rail, barrel, barrel_suppressor, under, stock, magazine or special; a
    // non-zero caliber limits the mod to the catalog's guns of that caliber.
    internal sealed class ModDef
    {
        internal readonly string Id;
        internal readonly string Code;
        internal readonly string Category;
        internal readonly string NameZh;
        internal readonly string NameEn;
        internal readonly string Slot;
        internal readonly int Value;
        internal readonly int Caliber;
        internal readonly int[] Stats;
        internal readonly string Size;

        internal ModDef(string id, string code, string category, string nameZh, string nameEn, string slot, int value,
            int caliber, int[] stats, string size)
        {
            Id = id;
            Code = code;
            Category = category;
            NameZh = nameZh;
            NameEn = nameEn;
            Slot = slot;
            Value = value;
            Caliber = caliber;
            Stats = stats;
            Size = size;
        }
    }
}
