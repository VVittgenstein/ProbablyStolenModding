using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace GunsmithExpansion
{
    // What a save knows of this mod's versions (VVittgenstein 2026-11-06 03:40 and 04:01, current/008 quotes 23 and 24, item 79). Until 0.7 nothing recorded which version a save came from, so every change for older saves had to guess from
    // what the save holds, and item 48 could not be done at all: a save does not show whether the store ever got its pistol cabinet. From 0.7 each save keeps in
    // PlayerStore.modData:
    //   - save_version: the version that last loaded it;
    //   - save_first_version: the version of its first record; "before-0.7" for a save that held this mod's data and no record;
    //   - save_history: each version that loaded it and on which day, as "before-0.7;0.7.0@12";
    //   - save_steps_done: the update steps it has been through;
    //   - given_<what>: the one-time things the store got from 0.7 on (Given).
    // On loading, the steps it has not been through run in the order of the list, each once per save, and what they gave is told in the game's notice. A new game goes
    // through none. A save with no record and none of this mod's data is one this mod was just added to, and the steps marked ForSavesWithMod are not for it.
    // Its name is the save version record (存档版本记录; VVittgenstein 2026-11-08 02:15, current/T-006 quote 32). Updates are for saves that have it, that is from 0.7 on: a save
    // from before 0.7 has no record, so what happened in it cannot be told, and it goes through no step (VVittgenstein 2026-11-08 02:09, quote 31 point 3; 02:15, quote 32). The five 0.7.0 steps written before (the game's firearm
    // sign given back, the sign's text, the sign's size, the guns' names and texts, the blank chips' names and values) were taken out then: such a save keeps the
    // cells, names, texts and values its items were saved with (SaveManager.DecodeNodesInternal).
    // An update for a release: a new Step at the end of the list, its version in its id. The item list says for each new design what saves need
    // (review/2026-11-05/round-items.md).
    internal static class SaveRecord
    {
        internal const string BeforeRecords = "before-0.7";
        private const string VersionKey = "save_version";
        private const string FirstKey = "save_first_version";
        private const string HistoryKey = "save_history";
        private const string DoneKey = "save_steps_done";
        private const string GivenPrefix = "given_";

        private sealed class Step
        {
            internal readonly string Id;
            internal readonly bool ForSavesWithMod;
            // What to tell the player, or null.
            internal readonly Func<string> Run;

            internal Step(string id, bool forSavesWithMod, Func<string> run)
            {
                Id = id;
                ForSavesWithMod = forSavesWithMod;
                Run = run;
            }
        }

        // For saves that have the record, as new Step("<version>-<what>", true, <method>); the version of T-013's release is not set yet, so its
        // step is named after the task.
        private static readonly Step[] Steps =
        {
            new Step("t013-removed-guns", true, RemoveTakenOut),
        };

        // The guns W045, W122, W132 and the gun mod A041, taken out of the mod in T-013 (final-strategy 341 ③; data/removed.tsv): what of them is
        // in the store, in a container or mounted on a gun, with their frames and printer chips, goes, for what was paid for it or, never
        // bought, its value (a printed frame's); a phone order of their chips still on its way is paid back as the old catalog priced it; a
        // reservation of one of the three guns is called off without the reputation loss, its deposit going back to the customer. The game
        // rebuilds an item it no longer knows from the save under its identifier, with its saved value and tags (DirectoryMaster.Item:
        // ItemDirectory.CreateEmptyItem), so they are found by identifier; the mod no longer registers them.
        internal static readonly string[] TakenOutGuns = { "gse_w045", "gse_w122", "gse_w132" };
        internal const string TakenOutMod = "gse_a041";
        // the level of each gun's chips (its tier), for the old catalog's price of a chip on order (Production.ChipValueOfLevel)
        private static readonly int[] TakenOutLevels = { 3, 2, 1 };

        private static string RemoveTakenOut()
        {
            PlayerStore store = PlayerStore.Instance;
            EmporiumEntry entry = EmporiumEntry.Instance;
            if (store == null || entry == null)
                throw new InvalidOperationException("the store is not there yet");
            var ids = new HashSet<string> { TakenOutMod };
            foreach (string gun in TakenOutGuns)
            {
                ids.Add(gun);
                ids.Add("gse_frame_" + gun.Substring(4));
                ids.Add("gse_chip_" + gun.Substring(4));
            }
            long refund = 0;
            int items = 0;
            foreach (GameItem item in new List<GameItem>(entry.GetAllOwnedItems(false, true)))
            {
                if (item == null || item.identifier == null || !ids.Contains(item.identifier))
                    continue;
                long paid = item.IsTag("BOUGHT_PRICE_TAG") ? item.GetTagReadonly("BOUGHT_PRICE_TAG").GetInt() : item.unitValue;
                refund += Math.Max(0, paid) * Math.Max(1, item.unitCount);
                items += Math.Max(1, item.unitCount);
                GunsmithMod.Log("taken out: " + item.identifier + ", " + paid + " back");
                item.Destroy();
            }
            int orders = 0;
            for (int i = 0; i < TakenOutGuns.Length; i++)
            {
                int markup = Difficulty.Rules != null ? Difficulty.Rules.retiredSupplierMarkup : 15;
                int price = (int)Math.Ceiling(Production.ChipValueOfLevel(TakenOutLevels[i]) * Phone.PriceFactor * (100 + markup) / 100.0);
                int n = Phone.RemoveOrders("gse_chip_" + TakenOutGuns[i].Substring(4));
                orders += n;
                refund += (long)n * price;
            }
            int deposits;
            int reservations = Reservations.CancelGuns(new HashSet<string>(TakenOutGuns), out deposits);
            if (refund > 0)
                store.ModCash((int)Math.Min(int.MaxValue, refund), true);
            GunsmithMod.Log("taken out of the mod: " + items + " items and " + orders + " chip orders, " + refund + " credits back; "
                + reservations + " reservations called off, deposits " + deposits);
            var told = new List<string>();
            if (items + orders > 0)
                told.Add(string.Format(Text.Pick("已下架的“掠影”无托步枪、“雾港”冲锋枪、“落日”雕花左轮与“弧线”枪托（含机匣、芯片与在途的芯片订单）共 {0} 件已移除，退还 {1} 信用点。",
                    "{0} pieces of the Glimpse Bullpup Rifle, Fogport SMG, Sundown Engraved Revolver and Arcline Stock, which are no longer in the mod (with their frames, chips and chip orders on the way), were removed; {1} credits paid back."),
                    items + orders, refund));
            if (reservations > 0)
                told.Add(string.Format(Text.Pick("这三把枪的 {0} 个预订已取消，不影响声望，定金 {1} 信用点退还给顾客。",
                    "{0} reservations of these three guns were called off without any loss of reputation; the deposits of {1} credits went back to the customers."),
                    reservations, deposits));
            return told.Count > 0 ? string.Join("\n\n", told.ToArray()) : null;
        }

        internal const string UpdateWindowId = "gse_update_notice";

        internal static void ShowUpdatePopup(string version, string details)
        {
            string message = string.Format(Text.Pick("枪匠拓展已更新到 {0}：{1}", "Gunsmith Expansion updated to {0}: {1}"), version, details);
            CustomUIManager manager = CustomUIManager.Instance;
            if (manager == null)
            {
                if (StoreUIManager.Instance != null)
                    StoreUIManager.Instance.Notify(message, "default");
                return;
            }
            if (manager.IsOpen(UpdateWindowId))
                manager.CloseWindow(UpdateWindowId);
            string layer = StoreUIManager.Instance != null ? "meta" : "menu";
            CustomUIBuilder b = manager.CreateWindow(UpdateWindowId, Text.Pick("枪匠拓展", "Gunsmith Expansion"), layer);
            b.SetSize(760f, 320f).Center().SetCloseOnEscape(true);
            b.AddLabel(message, "body");
            if (b.Last != null)
            {
                if (b.Last.text != null)
                    b.Last.text.enableWordWrapping = true;
                b.Last.SetPreferredSize(700f, 180f);
            }
            b.AddSpace(8f);
            b.BeginRow(12f, TextAnchor.MiddleCenter);
            b.AddFlexibleSpace();
            b.AddButton(Text.Pick("好的。", "Okay."), () =>
            {
                if (CustomUIManager.Instance != null && CustomUIManager.Instance.IsOpen(UpdateWindowId))
                    CustomUIManager.Instance.CloseWindow(UpdateWindowId);
            }, "ok");
            if (b.Last != null)
                b.Last.SetFixedWidth(180f);
            b.AddFlexibleSpace();
            b.End();
            CustomUIWindow window = b.Show();
            if (window != null)
                window.BringToFront();
        }

        private static string Today()
        {
            try
            {
                return StoreStation.GetDayCounter().ToString(CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                return "?";
            }
        }

        // A one-time thing the store got, so that a later update can tell.
        internal static void Given(string what)
        {
            ModHelper.SetModDataBool(GunsmithMod.ModId, GivenPrefix + what, true);
        }

        internal static void OnNewGame()
        {
            string version = GunsmithMod.Version;
            ModHelper.SetModData(GunsmithMod.ModId, VersionKey, version);
            ModHelper.SetModData(GunsmithMod.ModId, FirstKey, version);
            ModHelper.SetModData(GunsmithMod.ModId, HistoryKey, version + "@" + Today());
            var done = new List<string>();
            foreach (Step step in Steps)
                done.Add(step.Id);
            ModHelper.SetModData(GunsmithMod.ModId, DoneKey, string.Join(";", done.ToArray()));
            GunsmithMod.Log("save record: new game on " + version);
        }

        // Subscribed first of the load hooks (GunsmithMod.OnEnable): the others write this mod's data, which tells a save that held it before.
        internal static void OnGameLoaded()
        {
            try
            {
                if (!PlayerStore.HasInstance)
                    return;
                string version = GunsmithMod.Version;
                string last = ModHelper.GetModData(GunsmithMod.ModId, VersionKey, null);
                if (last == null)
                {
                    string first = ModHelper.GetModDataKeys(GunsmithMod.ModId).Count > 0 ? BeforeRecords : version;
                    ModHelper.SetModData(GunsmithMod.ModId, FirstKey, first);
                    if (first == BeforeRecords)
                        ModHelper.SetModData(GunsmithMod.ModId, HistoryKey, first);
                }
                // The steps are for saves that had the record before this load (final-strategy 295): a save from before 0.7 gets its record now
                // and goes through no step, as does a save this mod was just added to. Until T-013 this read the first version and ran the
                // steps for the saves from before 0.7 only; the list was empty, so nothing ran either way.
                bool heldMod = last != null;
                var done = new List<string>((ModHelper.GetModData(GunsmithMod.ModId, DoneKey, "") ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries));
                var told = new List<string>();
                foreach (Step step in Steps)
                {
                    if (done.Contains(step.Id))
                        continue;
                    if (!step.ForSavesWithMod || heldMod)
                    {
                        try
                        {
                            string line = step.Run();
                            if (!string.IsNullOrEmpty(line))
                                told.Add(line);
                            GunsmithMod.Log("save record: update step " + step.Id + " run");
                        }
                        catch (Exception e)
                        {
                            // tried again on the next load
                            GunsmithMod.Warn("save record: update step " + step.Id + ": " + e.Message);
                            continue;
                        }
                    }
                    done.Add(step.Id);
                }
                ModHelper.SetModData(GunsmithMod.ModId, DoneKey, string.Join(";", done.ToArray()));
                if (last != version)
                {
                    string history = ModHelper.GetModData(GunsmithMod.ModId, HistoryKey, "");
                    string entry = version + "@" + Today();
                    ModHelper.SetModData(GunsmithMod.ModId, HistoryKey, string.IsNullOrEmpty(history) ? entry : history + ";" + entry);
                    ModHelper.SetModData(GunsmithMod.ModId, VersionKey, version);
                    GunsmithMod.Log("save record: was " + (last ?? "without a record") + ", now " + version + "; history " + ModHelper.GetModData(GunsmithMod.ModId, HistoryKey, ""));
                }
                if (told.Count > 0)
                    ShowUpdatePopup(version, string.Join("\n\n", told.ToArray()));
            }
            catch (Exception e)
            {
                GunsmithMod.Warn("save record: " + e.Message);
            }
        }
    }
}
