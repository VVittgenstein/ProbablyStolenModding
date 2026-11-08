using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Moneylending
{
    // The saved form of the ledger: one record per line ("S" totals, "R" today's request, "L" a loan, "D" a debt),
    // each field written as name=value and separated by '|'. Unknown names are skipped and missing ones keep their
    // defaults, so later versions can add fields. UnityEngine.JsonUtility was tried first: in the game it wrote the
    // numbers of the ledger but left out the lists of loans and debts.
    internal static class Codec
    {
        private const string Header = "moneylending 1";

        internal static string Encode(State s)
        {
            var sb = new StringBuilder();
            sb.Append(Header).Append('\n');

            var f = new Fields();
            f.Put("nextId", s.nextId); f.Put("lent", s.lent); f.Put("interestEarned", s.interestEarned); f.Put("lost", s.lost);
            f.Put("recovered", s.recovered); f.Put("sawyerFees", s.sawyerFees); f.Put("lastBorrowerDay", s.lastBorrowerDay);
            f.Put("lastSharkOfferDay", s.lastSharkOfferDay); f.Put("lastBankOfferDay", s.lastBankOfferDay);
            sb.Append(f.Line("S")).Append('\n');

            if (s.request != null)
            {
                Request r = s.request;
                f = new Fields();
                f.Put("day", r.day); f.Put("profile", r.profile); f.Put("name", r.name); f.Put("sprite", r.sprite); f.Put("variant", r.variant);
                f.Put("amount", r.amount); f.Put("rate", r.rate); f.Put("term", r.term); f.Put("collateralId", r.collateralId);
                f.Put("collateralValue", r.collateralValue); f.Put("answered", r.answered);
                sb.Append(f.Line("R")).Append('\n');
            }

            foreach (Loan l in s.loans)
            {
                f = new Fields();
                f.Put("id", l.id); f.Put("profile", l.profile); f.Put("name", l.name); f.Put("sprite", l.sprite); f.Put("faction", l.faction);
                f.Put("atCounter", l.atCounter); f.Put("wasAug", l.wasAug); f.Put("variant", l.variant); f.Put("principal", l.principal);
                f.Put("rate", l.rate); f.Put("raised", l.raised); f.Put("interestFree", l.interestFree); f.Put("total", l.total); f.Put("paid", l.paid);
                f.Put("dayLent", l.dayLent); f.Put("dueDay", l.dueDay); f.Put("visitDay", l.visitDay); f.Put("lastVisitDay", l.lastVisitDay);
                f.Put("partialDone", l.partialDone); f.Put("outcome", l.outcome); f.Put("status", l.status); f.Put("closedDay", l.closedDay);
                f.Put("hasCollateral", l.hasCollateral); f.Put("collateralName", l.collateralName); f.Put("collateralValue", l.collateralValue);
                f.Put("collateralCount", l.collateralCount); f.Put("collateralKept", l.collateralKept); f.Put("sawyerDay", l.sawyerDay); f.Put("sawyerOffered", l.sawyerOffered);
                f.Put("sawyerHiredDay", l.sawyerHiredDay); f.Put("sawyerFee", l.sawyerFee); f.Put("recovered", l.recovered);
                sb.Append(f.Line("L")).Append('\n');
            }

            foreach (Debt d in s.debts)
            {
                f = new Fields();
                f.Put("id", d.id); f.Put("lender", d.lender); f.Put("sprite", d.sprite); f.Put("principal", d.principal); f.Put("total", d.total);
                f.Put("installment", d.installment); f.Put("installmentsLeft", d.installmentsLeft); f.Put("dayBorrowed", d.dayBorrowed);
                f.Put("dueDay", d.dueDay); f.Put("lastVisitDay", d.lastVisitDay); f.Put("paid", d.paid); f.Put("seizedValue", d.seizedValue);
                f.Put("status", d.status); f.Put("closedDay", d.closedDay);
                sb.Append(f.Line("D")).Append('\n');
            }
            return sb.ToString();
        }

        internal static State Decode(string text)
        {
            string[] lines = text.Split('\n');
            if (lines.Length == 0 || lines[0].Trim() != Header)
                throw new FormatException("unknown ledger format");
            var s = new State();
            for (int i = 1; i < lines.Length; i++)
            {
                string tag;
                Fields f = Fields.Parse(lines[i], out tag);
                if (f == null)
                    continue;
                switch (tag)
                {
                    case "S":
                        s.nextId = f.Int("nextId", s.nextId); s.lent = f.Int("lent", 0); s.interestEarned = f.Int("interestEarned", 0);
                        s.lost = f.Int("lost", 0); s.recovered = f.Int("recovered", 0); s.sawyerFees = f.Int("sawyerFees", 0);
                        s.lastBorrowerDay = f.Int("lastBorrowerDay", s.lastBorrowerDay); s.lastSharkOfferDay = f.Int("lastSharkOfferDay", s.lastSharkOfferDay);
                        s.lastBankOfferDay = f.Int("lastBankOfferDay", s.lastBankOfferDay);
                        break;
                    case "R":
                        var r = new Request();
                        r.day = f.Int("day", -1); r.profile = f.Str("profile"); r.name = f.Str("name"); r.sprite = f.Str("sprite");
                        r.variant = f.Int("variant", 0); r.amount = f.Int("amount", 0); r.rate = f.Int("rate", 0); r.term = f.Int("term", 1);
                        r.collateralId = f.Str("collateralId"); r.collateralValue = f.Int("collateralValue", 0); r.answered = f.Bool("answered");
                        s.request = r;
                        break;
                    case "L":
                        var l = new Loan();
                        l.id = f.Int("id", 0); l.profile = f.Str("profile"); l.name = f.Str("name"); l.sprite = f.Str("sprite"); l.faction = f.Str("faction");
                        l.atCounter = f.Bool("atCounter"); l.wasAug = f.Bool("wasAug"); l.variant = f.Int("variant", 0); l.principal = f.Int("principal", 0);
                        l.rate = f.Int("rate", 0); l.raised = f.Bool("raised"); l.interestFree = f.Bool("interestFree"); l.total = f.Int("total", 0);
                        l.paid = f.Int("paid", 0); l.dayLent = f.Int("dayLent", 0); l.dueDay = f.Int("dueDay", 0); l.visitDay = f.Int("visitDay", -1);
                        l.lastVisitDay = f.Int("lastVisitDay", -1); l.partialDone = f.Bool("partialDone"); l.outcome = f.Int("outcome", 0);
                        l.status = f.Int("status", 0); l.closedDay = f.Int("closedDay", -1); l.hasCollateral = f.Bool("hasCollateral");
                        l.collateralName = f.Str("collateralName"); l.collateralValue = f.Int("collateralValue", 0); l.collateralKept = f.Bool("collateralKept");
                        l.collateralCount = f.Int("collateralCount", l.hasCollateral ? 1 : 0);
                        l.sawyerDay = f.Int("sawyerDay", -1); l.sawyerOffered = f.Bool("sawyerOffered"); l.sawyerHiredDay = f.Int("sawyerHiredDay", -1);
                        l.sawyerFee = f.Int("sawyerFee", 0); l.recovered = f.Int("recovered", 0);
                        if (l.id > 0)
                            s.loans.Add(l);
                        break;
                    case "D":
                        var d = new Debt();
                        d.id = f.Int("id", 0); d.lender = f.Str("lender"); d.sprite = f.Str("sprite"); d.principal = f.Int("principal", 0);
                        d.total = f.Int("total", 0); d.installment = f.Int("installment", 0); d.installmentsLeft = f.Int("installmentsLeft", 0);
                        d.dayBorrowed = f.Int("dayBorrowed", 0); d.dueDay = f.Int("dueDay", 0); d.lastVisitDay = f.Int("lastVisitDay", -1);
                        d.paid = f.Int("paid", 0); d.seizedValue = f.Int("seizedValue", 0); d.status = f.Int("status", 0); d.closedDay = f.Int("closedDay", -1);
                        if (d.id > 0)
                            s.debts.Add(d);
                        break;
                }
            }
            return s;
        }

        // One record. Values are escaped so that '|', '=', '%' and line breaks inside names cannot break the line.
        private sealed class Fields
        {
            private readonly List<KeyValuePair<string, string>> pairs = new List<KeyValuePair<string, string>>();
            private Dictionary<string, string> map;

            internal void Put(string key, int value)
            {
                pairs.Add(new KeyValuePair<string, string>(key, value.ToString(CultureInfo.InvariantCulture)));
            }

            internal void Put(string key, bool value)
            {
                pairs.Add(new KeyValuePair<string, string>(key, value ? "1" : "0"));
            }

            internal void Put(string key, string value)
            {
                if (value != null)
                    pairs.Add(new KeyValuePair<string, string>(key, value));
            }

            internal string Line(string tag)
            {
                var sb = new StringBuilder(tag);
                foreach (KeyValuePair<string, string> p in pairs)
                    sb.Append('|').Append(p.Key).Append('=').Append(Escape(p.Value));
                return sb.ToString();
            }

            internal static Fields Parse(string line, out string tag)
            {
                tag = null;
                line = line.TrimEnd('\r');
                if (line.Length == 0)
                    return null;
                string[] parts = line.Split('|');
                tag = parts[0];
                var f = new Fields { map = new Dictionary<string, string>() };
                for (int i = 1; i < parts.Length; i++)
                {
                    int eq = parts[i].IndexOf('=');
                    if (eq > 0)
                        f.map[parts[i].Substring(0, eq)] = Unescape(parts[i].Substring(eq + 1));
                }
                return f;
            }

            internal int Int(string key, int fallback)
            {
                string v;
                int n;
                return map.TryGetValue(key, out v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) ? n : fallback;
            }

            internal bool Bool(string key)
            {
                string v;
                return map.TryGetValue(key, out v) && v == "1";
            }

            internal string Str(string key)
            {
                string v;
                return map.TryGetValue(key, out v) ? v : null;
            }

            private static string Escape(string value)
            {
                var sb = new StringBuilder(value.Length);
                foreach (char c in value)
                {
                    switch (c)
                    {
                        case '%': sb.Append("%25"); break;
                        case '|': sb.Append("%7C"); break;
                        case '=': sb.Append("%3D"); break;
                        case '\n': sb.Append("%0A"); break;
                        case '\r': sb.Append("%0D"); break;
                        default: sb.Append(c); break;
                    }
                }
                return sb.ToString();
            }

            private static string Unescape(string value)
            {
                if (value.IndexOf('%') < 0)
                    return value;
                var sb = new StringBuilder(value.Length);
                for (int i = 0; i < value.Length; i++)
                {
                    if (value[i] == '%' && i + 2 < value.Length)
                    {
                        string code = value.Substring(i + 1, 2);
                        string c = code == "25" ? "%" : code == "7C" ? "|" : code == "3D" ? "=" : code == "0A" ? "\n" : code == "0D" ? "\r" : null;
                        if (c != null)
                        {
                            sb.Append(c);
                            i += 2;
                            continue;
                        }
                    }
                    sb.Append(value[i]);
                }
                return sb.ToString();
            }
        }
    }
}
