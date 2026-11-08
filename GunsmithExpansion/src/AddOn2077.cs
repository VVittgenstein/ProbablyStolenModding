using System;
using System.Collections.Generic;
using System.Globalization;

namespace GunsmithExpansion
{
    // The 2077 Add-On (VVittgenstein 2026-11-05 16:53, current/008 quote 5; final-strategy 174): four guns in a mod of their own,
    // GunsmithExpansion2077, which holds only guns.tsv and their pictures; the switch is in this mod's settings. This mod reads
    // the file when the store's item directories start (Items.OnItemDirectoryInit), which is later than every mod's OnEnable, so
    // the order of the mods does not matter.
    //   - None of the four takes gun mods (VVittgenstein 2026-11-05 19:19, current/008 quote 14): they are Slots.None, with no gun mod window.
    //   - With the add-on enabled its guns are always registered, so a save holding them loads whole whatever the switch says.
    //   - The switch (addon_2077_enabled, off by default) only decides whether the sellers bring them (ForSale), and it takes effect at
    //     once. Off by default is the Agent's proposal, still pending in final-strategy pending item 54.
    //   - They stay out of Catalog.Guns: the order thresholds do not change and no repair order, reservation, contract or collection
    //     order names them. They have no chips (Production.HasChip): they can only be bought, as the special tier's guns.
    //   - Without the add-on, or with it disabled, Guns is empty and everything runs as before. A save holding its guns then gets the
    //     game's placeholder items (inferred from the code, not tested: GunsmithExpansion2077/review/2026-11-05/addon-2077-research.md C.2).
    internal static class AddOn2077
    {
        internal const string ModId = "GunsmithExpansion2077";
        private const string DataFile = "guns.tsv";
        private const string EnabledKey = "addon_2077_enabled";
        private const string SettingsSection = "2077 Add-On";
        internal static readonly List<GunDef> Guns = new List<GunDef>();
        private static ConfigEntry<bool> enabled;
        private static bool loaded;

        internal static void DefineSettings()
        {
            ModConfig config = ModHelper.GetConfig(GunsmithMod.ModId);
            enabled = config.Define(EnabledKey, false, SettingsSection,
                () => Text.Pick("2077 Add-On 的枪出现在货源中", "2077 Add-On guns turn up for sale"),
                () => Text.Pick("打开后，2077 Add-On 的 4 把枪会出现在卖家的货物、板条箱和垃圾场里。这些枪在单独的 Mod 里，要另外订阅并启用它，没有它时这个开关不起作用。关闭后不再出现新的，已有的枪不受影响。默认关闭。",
                    "When on, the 4 guns of the 2077 Add-On turn up among sellers' wares, in crates and at the dumping grounds. They are in a separate mod you have to subscribe to and enable; without it this switch does nothing. When off, no new ones turn up and guns you already have are not affected. Off by default."));
            config.Save();
        }

        internal static bool Enabled()
        {
            return enabled != null && enabled.Value;
        }

        // What the sellers pick from (Supply.Pick, Supply.FillCrate, DamagedGuns.PickGun): the catalog, and the add-on's guns
        // while the switch is on.
        internal static IEnumerable<GunDef> ForSale()
        {
            foreach (GunDef d in Catalog.Guns)
                yield return d;
            if (!Enabled())
                yield break;
            foreach (GunDef d in Guns)
                yield return d;
        }

        internal static GunDef Find(string id)
        {
            foreach (GunDef d in Guns)
            {
                if (d.Id == id)
                    return d;
            }
            return null;
        }

        internal static void Load()
        {
            if (loaded)
                return;
            loaded = true;
            LoadedMod mod = ModLoader.Mods.Find(m => m.Manifest != null && m.Manifest.ID == ModId);
            if (mod == null || !mod.IsEnabled || mod.Sprites == null)
                return;
            string text = ModHelper.ReadModFileText(ModId, DataFile);
            if (text == null)
            {
                GunsmithMod.Warn(ModId + " has no " + DataFile);
                return;
            }
            foreach (KeyValuePair<int, string> problem in Read(text, id => mod.Sprites.ContainsKey(id), Guns))
                GunsmithMod.Warn(ModId + " " + DataFile + " line " + problem.Key + ": " + problem.Value);
            GunsmithMod.Log(ModId + ": read " + Guns.Count + " guns, switch " + (Enabled() ? "on" : "off"));
        }

        // Adds the file's guns that have a picture to `guns`; returns the lines it could not use, with the reason. Separate from
        // Load so that it can run outside the game (GunsmithExpansion2077/testing/scripts/test_addon2077.ps1).
        internal static List<KeyValuePair<int, string>> Read(string text, Func<string, bool> hasPicture, List<GunDef> guns)
        {
            var problems = new List<KeyValuePair<int, string>>();
            string[] lines = text.Split('\n');
            string[] header = lines[0].TrimEnd('\r').Split('\t');
            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i].TrimEnd('\r');
                if (line.Length == 0)
                    continue;
                try
                {
                    string[] cells = line.Split('\t');
                    var row = new Dictionary<string, string>();
                    for (int c = 0; c < header.Length && c < cells.Length; c++)
                        row[header[c]] = cells[c];
                    if (hasPicture(row["id"]))
                        guns.Add(Parse(row));
                    else
                        problems.Add(new KeyValuePair<int, string>(i + 1, "no picture for " + row["id"]));
                }
                catch (Exception e)
                {
                    problems.Add(new KeyValuePair<int, string>(i + 1, e.Message));
                }
            }
            return problems;
        }

        private static GunDef Parse(Dictionary<string, string> r)
        {
            string[] stats = r["stats"].Split(',');
            var s = new int[stats.Length];
            for (int i = 0; i < s.Length; i++)
                s[i] = Int(stats[i]);
            var gun = new GunDef(r["id"], r["code"], r["tier"], r["class"], r["name_zh"], r["name_en"], r["maker"], Int(r["value"]),
                Int(r["contraband"]), r["line"], Int(r["caliber"]), Int(r["repairability"]), Slots.None, r["heavy"] == "true", false, false,
                s, r["size"], true);
            // optional: an add-on built before the guns had descriptions of their own has its class's (Text.GunDescription)
            r.TryGetValue("desc_zh", out gun.DescZh);
            r.TryGetValue("desc_en", out gun.DescEn);
            r.TryGetValue("desc_ru", out gun.DescRu);
            return gun;
        }

        private static int Int(string s)
        {
            return int.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture);
        }
    }
}
