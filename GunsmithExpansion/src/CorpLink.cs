using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace GunsmithExpansion
{
    // The link with the workshop mod CorpPresence (公司增强, item 3828913695, aamobe; VVittgenstein 2026-11-05 13:05 and 13:59,
    // current/008 quote 3 point 24, quote 4 point 24). When that mod is enabled, and only then:
    //   - Meridian Ballistics' order form also lists this mod's guns of the maker meridian, in tiers one to three, on
    //     pages after its own first page. final-strategy 11 keeps the special tier to what sellers bring, so it is not listed.
    //     A gun opens at the licence of its tier. Price and weekly limit are that mod's rule for an order; they are to be
    //     settled with groups two to four of the round (round-items.md).
    //   - Its delivery crates are as wide as this mod's widest gun, 17 cells instead of 12, or the longer guns would not
    //     fit. Height stays 10.
    //   - Its factory refurbishing makes a new gun of the same identifier (MeridianCo.MakeRefurbished), which clears this
    //     mod's faults, as they sit on the gun. VVittgenstein wants that kept; there is no code for it here.
    // None of that mod's code or pictures is copied; its source is public to read and its README does not allow passing on
    // changed copies, so everything is done from outside, at run time. Its catalog and windows are internal, with no
    // interface for other mods, so this rests on names that may change: what is missing is logged and the link stays off,
    // and long-term compatibility is not promised (VVittgenstein: the update notes must say so). Read against its version 0.9.7compat,
    // and the two mods have not yet been run together.
    // The patches go on when the item directory is built: every mod is loaded by then, and no saved crate has been
    // read yet, so a crate holding a long gun comes back at its full width. The game's own [HarmonyPatch] pass runs before
    // that mod may have loaded, so it cannot be used for these.
    internal static class CorpLink
    {
        private const string CorpAssembly = "CorpPresence";
        private const int PerPage = 8; // as the pages of that mod's parts
        private const int CrateWidth = 12;
        private const int CrateHeight = 10;

        private static bool tried;
        private static object ownEntries;
        private static object mergedEntries;
        private static Array[] gunPages;
        private static int page;
        private static int crateWidth = CrateWidth;
        private static FieldInfo themeField;
        private static FieldInfo buttonStyleField;
        private static FieldInfo centerStyleField;
        private static MethodInfo uiButton;
        private static bool pagerFailed;

        internal static void Apply()
        {
            if (tried)
                return;
            tried = true;
            Assembly corp = Array.Find(AppDomain.CurrentDomain.GetAssemblies(), a => a.GetName().Name == CorpAssembly);
            if (corp == null)
                return;
            try
            {
                Link(corp);
            }
            catch (Exception e)
            {
                GunsmithMod.Warn("the link with CorpPresence is off: " + e.Message);
            }
        }

        private static Type NeedType(Assembly corp, string name)
        {
            Type type = corp.GetType(name);
            if (type == null)
                throw new MissingMemberException(name);
            return type;
        }

        private static T Need<T>(T member, string name) where T : class
        {
            if (member == null)
                throw new MissingMemberException(name);
            return member;
        }

        private static void Link(Assembly corp)
        {
            Type meridian = NeedType(corp, "CorpPresence.MeridianCo");
            Type entry = NeedType(corp, "CorpPresence.CatalogEntry");
            Type crates = NeedType(corp, "CorpPresence.Crates");
            Type window = NeedType(corp, "CorpPresence.UI.CompanyWindow");
            Type ui = NeedType(corp, "CorpPresence.UI.Ui");
            Type theme = NeedType(corp, "CorpPresence.UI.Theme");
            FieldInfo all = Need(AccessTools.Field(meridian, "AllEntries"), "MeridianCo.AllEntries");
            FieldInfo own = Need(AccessTools.Field(meridian, "Entries"), "MeridianCo.Entries");
            MethodInfo catalog = Need(AccessTools.PropertyGetter(meridian, "Catalog"), "MeridianCo.Catalog");
            MethodInfo shell = Need(AccessTools.Method(crates, "CreateShell"), "Crates.CreateShell");
            MethodInfo order = Need(AccessTools.Method(window, "DrawEquipmentOrder"), "CompanyWindow.DrawEquipmentOrder");
            themeField = Need(AccessTools.Field(window, "_theme"), "CompanyWindow._theme");
            uiButton = Need(AccessTools.Method(ui, "Button", new[] { typeof(Rect), typeof(string), typeof(GUIStyle) }), "Ui.Button");
            buttonStyleField = Need(AccessTools.Field(theme, "BtnStyle"), "Theme.BtnStyle");
            centerStyleField = Need(AccessTools.Field(theme, "Center"), "Theme.Center");
            FieldInfo id = Need(AccessTools.Field(entry, "Id"), "CatalogEntry.Id");
            FieldInfo pageName = Need(AccessTools.Field(entry, "Page"), "CatalogEntry.Page");
            FieldInfo minTier = Need(AccessTools.Field(entry, "MinTier"), "CatalogEntry.MinTier");
            FieldInfo label = Need(AccessTools.Field(entry, "Label"), "CatalogEntry.Label");
            FieldInfo labelEn = Need(AccessTools.Field(entry, "LabelEn"), "CatalogEntry.LabelEn");

            var guns = new List<GunDef>();
            foreach (GunDef d in Catalog.Guns)
            {
                crateWidth = Math.Max(crateWidth, WidthOf(d));
                if (d.Maker == "meridian" && TierOf(d) > 0)
                    guns.Add(d);
            }
            guns.Sort((a, b) => TierOf(a) != TierOf(b) ? TierOf(a) - TierOf(b) : a.Value - b.Value);
            var entries = new List<object>();
            foreach (GunDef d in guns)
            {
                object e = Activator.CreateInstance(entry);
                id.SetValue(e, "@" + d.Id); // the @ and the page make MeridianCo.MakeNew build it factory new, as its own guns
                pageName.SetValue(e, "制式");
                minTier.SetValue(e, TierOf(d));
                label.SetValue(e, d.NameZh);
                labelEn.SetValue(e, d.NameEn);
                entries.Add(e);
            }

            var old = (Array)all.GetValue(null);
            Array merged = Array.CreateInstance(entry, old.Length + entries.Count);
            Array.Copy(old, merged, old.Length);
            for (int i = 0; i < entries.Count; i++)
                merged.SetValue(entries[i], old.Length + i);
            mergedEntries = merged;
            ownEntries = own.GetValue(null);
            gunPages = new Array[(entries.Count + PerPage - 1) / PerPage];
            for (int p = 0; p < gunPages.Length; p++)
            {
                int count = Math.Min(PerPage, entries.Count - p * PerPage);
                gunPages[p] = Array.CreateInstance(entry, count);
                for (int i = 0; i < count; i++)
                    gunPages[p].SetValue(entries[p * PerPage + i], i);
            }

            // Under the Harmony ID the game's ModLoader gives this mod ("mod." and the manifest ID): when the game stops the mod while it runs
            // (ModLoader.StopRunningMod, for the trust list or its Steam guard) or quits, it takes off the patches of that ID only (UnpatchAll).
            var harmony = new Harmony("mod." + GunsmithMod.ModId);
            harmony.Patch(catalog, postfix: new HarmonyMethod(AccessTools.Method(typeof(CorpLink), nameof(CatalogPostfix))));
            harmony.Patch(shell, postfix: new HarmonyMethod(AccessTools.Method(typeof(CorpLink), nameof(ShellPostfix))));
            harmony.Patch(order, prefix: new HarmonyMethod(AccessTools.Method(typeof(CorpLink), nameof(OrderPrefix))));
            GunsmithMod.Log("linked with CorpPresence: " + entries.Count + " guns on the Meridian form, crates " + crateWidth + " x " + CrateHeight);
        }

        private static int TierOf(GunDef d)
        {
            return d.Tier == "一档" ? 1 : d.Tier == "二档" ? 2 : d.Tier == "三档" ? 3 : 0;
        }

        private static int WidthOf(GunDef d)
        {
            int width;
            string size = d.Size ?? "";
            int x = size.IndexOf('x');
            return x > 0 && int.TryParse(size.Substring(0, x), out width) ? width : 0;
        }

        // MakeOrdered finds what was ordered in the company's Catalog. (A patch, as its field is read-only and Mono may
        // have compiled the array into the getter.)
        private static void CatalogPostfix(ref object __result)
        {
            if (mergedEntries != null)
                __result = mergedEntries;
        }

        private static void ShellPostfix(GameItem __result)
        {
            if (__result == null)
                return;
            foreach (GameInventory inside in GraphUtils.FindChildrenType<GameInventory>(__result))
            {
                var grid = inside as GameGridInventory;
                if (grid == null)
                    continue;
                grid.SetShape(crateWidth, CrateHeight);
                if (__result.contentWindow != null)
                    __result.contentWindow.Validate();
                return;
            }
        }

        // Its first page has no pager and shows every entry it is given, 8 fit; the buttons stand where its parts page has them.
        private static void OrderPrefix(Rect __0, ref object __1)
        {
            if (gunPages == null || pagerFailed || !ReferenceEquals(__1, ownEntries))
                return;
            try
            {
                object theme = themeField.GetValue(null);
                var buttonStyle = (GUIStyle)buttonStyleField.GetValue(theme);
                int total = gunPages.Length + 1;
                page = Math.Max(0, Math.Min(page, total - 1));
                if ((bool)uiButton.Invoke(null, new object[] { new Rect(__0.xMax - 150f, __0.y - 40f, 32f, 26f), "‹", buttonStyle }) && page > 0)
                    page--;
                GUI.Label(new Rect(__0.xMax - 116f, __0.y - 40f, 80f, 26f), (page + 1) + "/" + total, (GUIStyle)centerStyleField.GetValue(theme));
                if ((bool)uiButton.Invoke(null, new object[] { new Rect(__0.xMax - 34f, __0.y - 40f, 32f, 26f), "›", buttonStyle }) && page < total - 1)
                    page++;
                if (page > 0)
                    __1 = gunPages[page - 1];
            }
            catch (Exception e)
            {
                pagerFailed = true;
                GunsmithMod.Warn("the Meridian form's pages are off: " + e.Message);
            }
        }
    }
}
