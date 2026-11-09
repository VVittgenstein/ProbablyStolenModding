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
    // and long-term compatibility is not promised (VVittgenstein: the update notes must say so). The declared prerequisite
    // permits the game's ModReflect broker to check access. With CorpPresence absent this integration stays inactive.
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
        private static Type windowType;
        private static Type uiType;
        private static bool pagerFailed;

        internal static void Apply()
        {
            if (tried)
                return;
            tried = true;
            if (!ModHelper.IsModRunning(CorpAssembly))
                return;
            try
            {
                Link();
            }
            catch (Exception e)
            {
                GunsmithMod.Warn("the link with CorpPresence is off: " + e.Message);
            }
        }

        private static Type NeedType(string name)
        {
            Type type = ModReflect.FindType(name);
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

        private static void Link()
        {
            Type meridian = NeedType("CorpPresence.MeridianCo");
            Type entry = NeedType("CorpPresence.CatalogEntry");
            Type crates = NeedType("CorpPresence.Crates");
            windowType = NeedType("CorpPresence.UI.CompanyWindow");
            uiType = NeedType("CorpPresence.UI.Ui");
            MethodBase catalog = ModReflect.Target(meridian, "get_Catalog");
            MethodBase shell = ModReflect.Target(crates, "CreateShell");
            MethodBase order = ModReflect.Target(windowType, "DrawEquipmentOrder");

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
                object e = ModReflect.New(entry);
                ModReflect.Set(e, "Id", "@" + d.Id); // the @ and the page make MeridianCo.MakeNew build it factory new, as its own guns
                ModReflect.Set(e, "Page", "制式");
                ModReflect.Set(e, "MinTier", TierOf(d));
                ModReflect.Set(e, "Label", d.NameZh);
                ModReflect.Set(e, "LabelEn", d.NameEn);
                entries.Add(e);
            }

            var old = ModReflect.GetStatic<Array>(meridian, "AllEntries");
            Array merged = Array.CreateInstance(entry, old.Length + entries.Count);
            Array.Copy(old, merged, old.Length);
            for (int i = 0; i < entries.Count; i++)
                merged.SetValue(entries[i], old.Length + i);
            mergedEntries = merged;
            ownEntries = ModReflect.GetStatic(meridian, "Entries");
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
                object theme = ModReflect.GetStatic(windowType, "_theme");
                var buttonStyle = ModReflect.Get<GUIStyle>(theme, "BtnStyle");
                int total = gunPages.Length + 1;
                page = Math.Max(0, Math.Min(page, total - 1));
                if (PageButton(new Rect(__0.xMax - 150f, __0.y - 40f, 32f, 26f), "‹", buttonStyle) && page > 0)
                    page--;
                GUI.Label(new Rect(__0.xMax - 116f, __0.y - 40f, 80f, 26f), (page + 1) + "/" + total, ModReflect.Get<GUIStyle>(theme, "Center"));
                if (PageButton(new Rect(__0.xMax - 34f, __0.y - 40f, 32f, 26f), "›", buttonStyle) && page < total - 1)
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

        private static bool PageButton(Rect rect, string text, GUIStyle style)
        {
            return (bool)ModReflect.CallStatic(uiType, "Button", new[] { typeof(Rect), typeof(string), typeof(GUIStyle) }, rect, text, style);
        }
    }
}
