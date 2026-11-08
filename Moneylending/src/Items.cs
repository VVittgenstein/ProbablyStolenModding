using System;
using System.Collections.Generic;

namespace Moneylending
{
    // Game items used as pledges and as goods given in repayment. Every identifier is one the game's own
    // sellers put on the counter; the values are the factories' unitValue in this build (0.46D, build 26317870).
    internal static class Items
    {
        internal sealed class Entry
        {
            internal readonly string Id;
            internal readonly int Value;

            internal Entry(string id, int value)
            {
                Id = id;
                Value = value;
            }
        }

        private static readonly Dictionary<string, int> Values = new Dictionary<string, int>
        {
            // a scavenger's tools and haul
            { "screwdriver", 25 }, { "wire_cutter", 40 }, { "welder", 80 }, { "crowbar", 75 }, { "hatchet", 55 },
            { "scrap_metal", 10 }, { "wire", 16 }, { "common_electronic", 33 }, { "printer_plastic", 12 }, { "kotton_fabric", 65 },
            // Lower Level household goods
            { "toothpaste", 15 }, { "shampoo", 30 }, { "toilet_paper", 25 }, { "paper_towel", 35 }, { "box_tampon", 100 },
            // trade goods
            { "common_medical", 40 }, { "common_chemical", 50 }, { "water_ration", 30 }, { "processed_meat", 25 },
            { "rubbing_alcohol", 35 }, { "bandage_item", 8 }, { "surgery_tool", 200 }, { "rare_electronic", 350 },
            // Upper Level luxuries
            { "skincare_cream", 500 }, { "neuroactive_perfume", 150 }, { "bottled_water_premium", 325 }, { "blue_blood_bag", 250 },
            { "portable_water_purifier", 325 }, { "water_filter_adv", 350 }, { "black_injector", 350 },
            // a thief's stock
            { "turbo_booster_adv", 300 }, { "scratcher_stack", 100 }, { "aquascan", 200 }, { "kitchen_cleaver", 65 }, { "salve", 80 },
        };

        internal static readonly string[] ScavHaul =
        {
            "screwdriver", "wire_cutter", "welder", "crowbar", "hatchet", "scrap_metal", "wire", "common_electronic", "printer_plastic", "kotton_fabric",
        };

        internal static readonly string[] MerchantPledge =
        {
            "surgery_tool", "rare_electronic", "common_chemical", "common_medical", "kotton_fabric",
        };

        internal static readonly string[] LuxuryPledge =
        {
            "skincare_cream", "neuroactive_perfume", "bottled_water_premium", "blue_blood_bag", "portable_water_purifier", "water_filter_adv", "black_injector",
        };

        internal static readonly string[] StolenGoods =
        {
            "skincare_cream", "neuroactive_perfume", "bottled_water_premium", "portable_water_purifier", "water_filter_adv",
            "turbo_booster_adv", "scratcher_stack", "aquascan", "kitchen_cleaver", "salve", "surgery_tool", "common_medical",
        };

        // What the Revolution takes in a raid on the Upper Levels (the raid event lowers household prices).
        internal static readonly string[] RaidedGoods =
        {
            "box_tampon", "paper_towel", "shampoo", "toilet_paper", "toothpaste", "common_medical", "rubbing_alcohol", "salve",
        };

        internal static int ValueOf(string id)
        {
            int value;
            return id != null && Values.TryGetValue(id, out value) ? value : 0;
        }

        internal const int MaxPledgeItems = 4;

        // The items of a pledge: at most four, with a total as close as possible to the target share of the loan and inside
        // the allowed shares whenever a set reaches that (four items may not be worth enough for a very large loan). A tie
        // goes to fewer items, then to a random set among equals.
        internal static List<string> PickPledge(string[] ids, int amount, int sharePercent, out int total)
        {
            var pool = new List<Entry>();
            foreach (string id in ids)
            {
                int value = ValueOf(id);
                if (value > 0 && DirectoryMaster.Has<GameItem>(id))
                    pool.Add(new Entry(id, value));
            }
            int low = amount * Lending.MinPledgeShare / 100;
            int high = amount * Lending.MaxPledgeShare / 100;
            int target = amount * sharePercent / 100;

            var current = new List<Entry>();
            var best = new List<Entry>();
            int bestSum = 0;
            bool bestInside = false;
            int ties = 0;
            Action<int, int> search = null;
            search = (start, sum) =>
            {
                if (current.Count > 0)
                {
                    bool inside = sum >= low && sum <= high;
                    int distance = Math.Abs(sum - target);
                    int bestDistance = Math.Abs(bestSum - target);
                    bool better = best.Count == 0 || (inside && !bestInside)
                        || (inside == bestInside && (distance < bestDistance || (distance == bestDistance && current.Count < best.Count)));
                    bool tie = !better && inside == bestInside && distance == bestDistance && current.Count == best.Count;
                    if (better)
                        ties = 1;
                    else if (tie)
                        ties++;
                    if (better || (tie && RNG.GetRandomInt(1, ties) == 1))
                    {
                        best = new List<Entry>(current);
                        bestSum = sum;
                        bestInside = inside;
                    }
                }
                if (current.Count == MaxPledgeItems)
                    return;
                for (int i = start; i < pool.Count; i++)
                {
                    current.Add(pool[i]);
                    search(i, sum + pool[i].Value);
                    current.RemoveAt(current.Count - 1);
                }
            };
            search(0, 0);

            total = bestSum;
            var picked = new List<string>();
            foreach (Entry e in best)
                picked.Add(e.Id);
            return picked;
        }

        // A request keeps the identifiers of its pledge in one text, separated by commas. Requests saved before 1.4.0 hold one.
        internal static string[] SplitIds(string ids)
        {
            return string.IsNullOrEmpty(ids) ? new string[0] : ids.Split(',');
        }

        internal static GameItem Spawn(string id)
        {
            if (string.IsNullOrEmpty(id) || !DirectoryMaster.Has<GameItem>(id))
                return null;
            return DirectoryMaster.Item(id, false);
        }

        // A random set of goods worth at least the target, at most six items.
        internal static List<string> PickGoods(string[] ids, int target)
        {
            var picked = new List<string>();
            var pool = new List<Entry>();
            foreach (string id in ids)
            {
                int value = ValueOf(id);
                if (value > 0 && DirectoryMaster.Has<GameItem>(id))
                    pool.Add(new Entry(id, value));
            }
            if (pool.Count == 0)
                return picked;
            pool.Sort((a, b) => b.Value.CompareTo(a.Value));
            int remaining = target;
            while (remaining > 0 && picked.Count < 6)
            {
                var fitting = new List<Entry>();
                foreach (Entry e in pool)
                {
                    if (e.Value <= remaining)
                        fitting.Add(e);
                }
                Entry chosen;
                if (picked.Count == 5 || fitting.Count == 0)
                {
                    // Close the gap with the cheapest item that covers it, or the most valuable one.
                    chosen = pool[0];
                    foreach (Entry e in pool)
                    {
                        if (e.Value >= remaining && e.Value < chosen.Value)
                            chosen = e;
                    }
                }
                else
                {
                    chosen = fitting[RNG.GetRandomInt(0, Math.Min(fitting.Count, 3) - 1)];
                }
                picked.Add(chosen.Id);
                remaining -= chosen.Value;
            }
            return picked;
        }

        // Owned goods on the counter, or in the store inventory when the counter is full; stolen goods are marked the
        // way the game's thieves' goods are (only once the game has introduced stolen items, on day 13). An item the
        // counter refused is destroyed by the game at the end of the frame, so the inventory gets a fresh one.
        internal static void PlaceGoods(List<string> ids, bool stolen)
        {
            PlayerStore store = PlayerStore.Instance;
            foreach (string id in ids)
            {
                GameItem item = Spawn(id);
                if (item == null || store.TryAddDirectSellingItemToTable(item, true, stolen, true, 0))
                    continue;
                GameItem copy = Spawn(id);
                if (copy == null)
                    continue;
                GeneralHelper.SetItemOwned(copy, true);
                if (stolen && store.isStolenItemIntroduced)
                    StolenHelper.InitStolenItem(copy, 0);
                ModHelper.MoveItemToInventory(copy);
            }
        }
    }
}
