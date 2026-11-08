using System;
using System.Collections.Generic;
using System.Globalization;

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

        // No step yet: the first ones will be those of the release after 0.7.0, for saves that have the record, as new Step("<version>-<what>", true, <method>).
        private static readonly Step[] Steps = { };

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
                bool heldMod = ModHelper.GetModData(GunsmithMod.ModId, FirstKey, version) == BeforeRecords;
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
                if (told.Count > 0 && StoreUIManager.Instance != null)
                    StoreUIManager.Instance.Notify(string.Format(Text.Pick("枪匠拓展已更新到 {0}：{1}", "Gunsmith Expansion updated to {0}: {1}"), version, string.Join(" ", told.ToArray())), "default");
            }
            catch (Exception e)
            {
                GunsmithMod.Warn("save record: " + e.Message);
            }
        }
    }
}
