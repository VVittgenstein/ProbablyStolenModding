using System;
using System.Collections.Generic;

namespace GunsmithExpansion
{
    // The phone pages of this mod whose choices are paged: the lists of 3955 (LincolnPhone) and the kinds of the retired
    // gunsmith's gun boxes on 4867 (BlindBoxes). A page holds as many choices as fit the game area (PhoneChoices.PageCapacity) and
    // is filled again when that number has changed by the time the game shows it (PhoneChoiceLayoutPatch, Phone.PrepareChoicePage).
    // Until T-013 this was the retired gunsmith's catalog alone (Phone.ShowMenu); the catalog went to Lincoln Arms (final-strategy 300).
    internal static class PhoneMenus
    {
        // One choice of a list: the player's line and what picking it does with that line (it sets the reply on line.NextDialogue()).
        // MakeLabel gives the line when it is first needed: a good's price is read by making its item (LincolnGoods.Price), so only the
        // goods of the pages shown are made.
        internal sealed class Entry
        {
            private string label;
            internal Func<string> MakeLabel;
            internal Action<Dialogue> Pick;
            internal Func<bool> Enabled;

            internal string Label
            {
                get
                {
                    if (label == null)
                        label = MakeLabel != null ? MakeLabel() : "";
                    return label;
                }
                set { label = value; }
            }
        }

        private sealed class Page
        {
            internal Func<int> Capacity;
            internal Action Fill;
            internal int Count;
        }

        private static readonly Dictionary<Dialogue, Page> pages = new Dictionary<Dialogue, Page>();

        // At the start of each call: the pages of the calls before are not shown again.
        internal static void Clear()
        {
            pages.Clear();
        }

        internal static bool IsPage(Dialogue page)
        {
            return page != null && pages.ContainsKey(page);
        }

        internal static void Prepare(Dialogue page)
        {
            Page p;
            if (page == null || !pages.TryGetValue(page, out p) || p.Capacity == null || p.Fill == null)
                return;
            if (p.Capacity() == p.Count)
                return;
            foreach (var choice in page.choices)
            {
                if (choice.Item1 != null)
                    choice.Item1.Destroy();
            }
            page.choices.Clear();
            p.Fill();
        }

        internal static Dialogue Line(string text)
        {
            return new Dialogue().SetText("PLAYER", text);
        }

        // A choice with the game's money icon, as the game's phone suppliers have.
        internal static void AddChoice(Dialogue page, string label, Action<Dialogue> pick, Func<bool> enabled = null)
        {
            Dialogue line = Line(label);
            page.AddChoice(DirectoryMaster.Item("$", true), "", "", line, () => pick(line), enabled);
        }

        // Entries from start, as many as fit, then "more" when more entries follow (or always, when moreAlways) and "bye". onMore gets
        // the player's line and the first entry of the next page, -1 after the last page.
        internal static void ShowList(Dialogue page, List<Entry> entries, int start, string moreLabel, Action<Dialogue, int> onMore,
            bool moreAlways, string byeLabel, Action<Dialogue> onBye)
        {
            var labels = new List<string>();
            for (int i = start; i < entries.Count && labels.Count < Phone.ChoiceBubbles - 2; i++)
                labels.Add(entries[i].Label);
            Action fill = null;
            fill = () =>
            {
                Phone.EnsureChoiceBubbles();
                int perPage = PhoneChoices.PageCapacity(labels);
                for (int i = start; i < entries.Count && i < start + perPage; i++)
                {
                    Entry e = entries[i];
                    AddChoice(page, e.Label, e.Pick, e.Enabled);
                }
                int next = start + perPage;
                if (onMore != null && (next < entries.Count || moreAlways))
                    AddChoice(page, moreLabel, line => onMore(line, next < entries.Count ? next : -1));
                if (onBye != null)
                    AddChoice(page, byeLabel, onBye);
                pages[page] = new Page { Capacity = () => PhoneChoices.PageCapacity(labels), Fill = fill, Count = perPage };
            };
            fill();
        }

        internal static void EndCall()
        {
            if (PhoneUIManager.Instance != null)
                PhoneUIManager.Instance.OnPhoneDialogEnd(true);
        }
    }
}
