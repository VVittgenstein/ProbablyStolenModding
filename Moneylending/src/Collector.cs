using System;
using UnityEngine;

namespace Moneylending
{
    // Collecting a defaulted loan for a fee. Sawyer's Crew does it while Sawyer lives. Once the player has shot him dead
    // (the game 1.0.0.12, StoreClientData.isSawyerDead), Mr. Leung, an independent debt collector, comes instead
    // (current/archive/T-012, current/T-037). His fee, his chance and the night's result are those of Sawyer's Crew
    // (VVittgenstein 2026-11-09: "沿用现行数值"); the visitor, his words and the records name the one who was paid.
    //
    // A loan's collector fields keep their names from before 1.6.0 (Loan.sawyerDay, sawyerOffered, sawyerHiredDay,
    // sawyerFee), so older saves are read as they were; they hold the visit of either collector, and Loan.collector
    // says whose offer it was.
    internal static class Collector
    {
        // Loan.collector. A loan offered or hired before 1.6.0 has no value and was Sawyer's.
        internal const string Sawyer = "sawyer";
        internal const string Leung = "leung";

        // Textures/Items/moneylending_collector.png, the portrait adopted on 2026-11-20 (current/archive/T-036).
        internal const string Portrait = "moneylending_collector";

        // The visit day and two more days for a collector who was not received (as since 1.0.0).
        internal const int WindowDays = 3;

        // Loan.legacy. A loan that defaulted after Sawyer's death in an earlier version of the mod has no visit day;
        // it gets one visit from Mr. Leung, one such loan a day, the earliest default first, from the day after the
        // save was first loaded with this version (VVittgenstein 2026-11-09: "允许，按这个安排处理旧欠款").
        internal const int LegacyNone = 0;
        internal const int LegacyWaiting = 1;
        internal const int LegacyScheduled = 2;

        internal static bool IsLeung(Loan loan)
        {
            return loan.collector == Leung;
        }

        // A defaulted loan that a collector may still be hired for: a kind of borrower who can be found, not an Aug,
        // money still owed, no offer made yet and nobody hired.
        internal static bool Eligible(Loan loan)
        {
            if (loan.status != (int)LoanStatus.Defaulted || loan.wasAug || loan.Outstanding <= 0 || loan.sawyerOffered || loan.sawyerHiredDay >= 0)
                return false;
            Profile p = Profiles.Get(loan.profile);
            return p != null && p.SawyerCan;
        }

        // First day of the loan's visit window. Saves from before 1.6.0 have no value: the window then starts the day
        // after the default, as it always did.
        internal static int WindowStart(Loan loan)
        {
            return loan.collectorFrom >= 0 ? loan.collectorFrom : loan.closedDay + 1;
        }

        internal static bool DueOn(Loan loan, int day)
        {
            return loan.sawyerDay == day && Eligible(loan);
        }

        // An eligible loan whose visit is still to come, today included, or a historical loan waiting for its visit.
        internal static bool Pending(Loan loan, int day)
        {
            if (!Eligible(loan))
                return false;
            if (loan.legacy == LegacyWaiting)
                return true;
            return loan.sawyerDay >= day && loan.sawyerDay < WindowStart(loan) + WindowDays;
        }

        // Mr. Leung's opening says one or several by the defaulted loans still pending that he can be hired for, the one
        // he comes for included (final-strategy/probably-stolen-moneylending.md, "索耶死后的独立讨债人": "开场单复数按
        // 当前待处理且仍可委托讨债的违约借款计数，包含当前这一笔"), at the first visit and at later ones.
        internal static int PendingCount(int day)
        {
            int n = 0;
            foreach (Loan loan in Book.S.loans)
            {
                if (Pending(loan, day))
                    n++;
            }
            return n;
        }

        // The first visit day of a loan that defaulted tonight (the collector is chosen on the morning of the visit).
        internal static void Schedule(Loan loan, int day)
        {
            loan.sawyerDay = day;
            loan.collectorFrom = day;
        }

        // The visitor for a loan's visit: Sawyer while he lives, Mr. Leung after.
        internal static StoreClient CreateVisitor(int id)
        {
            return Game.SawyerDead ? Visits.CreateLeung(id) : Visits.CreateSawyer(id);
        }

        // Once per save, on the first load with this version: the loans that defaulted after Sawyer's death in an
        // earlier version (eligible, no visit day) wait for a visit from Mr. Leung, from the next day on. The mark is
        // kept in the ledger, so loading the save again does not add the visits again; a save that was not written
        // after the first load is checked again with the same result.
        internal static void CheckLegacy()
        {
            State s = Book.S;
            if (s.collectorChecked)
                return;
            s.collectorChecked = true;
            int count = 0;
            foreach (Loan loan in s.loans)
            {
                if (loan.legacy == LegacyNone && loan.sawyerDay < 0 && Eligible(loan))
                {
                    loan.legacy = LegacyWaiting;
                    count++;
                }
            }
            if (count > 0)
                s.legacyFrom = Game.Day + 1;
            Book.Save();
            MoneylendingMod.Log("collector check: " + count + " earlier unpaid loan(s) to be offered to Mr. Leung" + (count > 0 ? " from day " + s.legacyFrom : ""));
        }

        // Each morning: the next historical loan gets its visit today, once the one before it has had its visit or its
        // window has passed. Returns the loan, or null.
        internal static Loan ScheduleLegacy(int day)
        {
            State s = Book.S;
            if (s.legacyFrom < 0 || day < s.legacyFrom)
                return null;
            Loan next = null;
            foreach (Loan loan in s.loans)
            {
                if (loan.legacy == LegacyScheduled && Pending(loan, day))
                    return null;
                if (loan.legacy != LegacyWaiting)
                    continue;
                if (!Eligible(loan))
                {
                    loan.legacy = LegacyNone;
                    continue;
                }
                if (next == null || loan.closedDay < next.closedDay || (loan.closedDay == next.closedDay && loan.id < next.id))
                    next = loan;
            }
            if (next == null)
            {
                s.legacyFrom = -1;
                return null;
            }
            next.legacy = LegacyScheduled;
            Schedule(next, day);
            MoneylendingMod.Log("earlier unpaid loan #" + next.id + " (defaulted on day " + next.closedDay + ") gets its visit on day " + day);
            return next;
        }

        // Puts the portrait into the scene's SpriteDict (as GunsmithExpansion does for its customers) and returns its key,
        // or null when it cannot. A customer that F5 Quick Save Plus restores keeps the key and does not run the
        // factory, so the key is also put in when a save is read (MoneylendingMod.OnGameRead): PlayerStore.LoadGame runs
        // from EmporiumEntry.Start, after SpriteDict.Awake has made the scene's dictionary.
        internal static string PortraitKey()
        {
            try
            {
                SpriteDict dict = SpriteDict.Instance;
                if (dict == null || dict.spriteDictionary == null)
                    return null;
                if (dict.spriteDictionary.ContainsKey(Portrait))
                    return Portrait;
                LoadedMod mod = ModLoader.Mods.Find(m => m.Instance is MoneylendingMod);
                Sprite own;
                if (mod == null || mod.Sprites == null || !mod.Sprites.TryGetValue(Portrait, out own) || own == null)
                    return null;
                // the pivot and pixels per unit of the game's own portraits, taken from the clerk's
                Sprite model = dict.GetSprite(SpriteDict.GetClerkSpriteName());
                Vector2 pivot = model != null ? new Vector2(model.pivot.x / model.rect.width, model.pivot.y / model.rect.height) : new Vector2(0.5f, 0.5f);
                float ppu = model != null ? model.pixelsPerUnit : own.pixelsPerUnit;
                own.texture.filterMode = FilterMode.Point;
                Sprite sprite = Sprite.Create(own.texture, own.rect, pivot, ppu);
                sprite.name = Portrait;
                dict.spriteDictionary[Portrait] = sprite;
                return Portrait;
            }
            catch (Exception e)
            {
                MoneylendingMod.Warn("the portrait " + Portrait + " was not set: " + e.Message);
                return null;
            }
        }
    }
}
