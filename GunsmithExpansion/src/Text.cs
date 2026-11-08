using System;
using System.Collections.Generic;
using HarmonyLib;

namespace GunsmithExpansion
{
    // Chinese texts when the game runs in Chinese, Russian when it runs in Russian (src/TextRu.cs, looked up by the
    // English text; an English text without a Russian one stays English), English otherwise. The Russian texts were
    // added in 0.5.13 (VVittgenstein 2026-11-03, current/005 quotes 60 and 61), as in the other mods of this project.
    internal static class Text
    {
        private static string LanguageCode()
        {
            UnityEngine.Localization.Locale locale = ModHelper.GetCurrentLocale();
            return locale != null ? locale.Identifier.Code : null;
        }

        internal static bool Chinese()
        {
            string code = LanguageCode();
            return code != null && code.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool Russian()
        {
            string code = LanguageCode();
            return code != null && code.StartsWith("ru", StringComparison.OrdinalIgnoreCase);
        }

        internal static string Pick(string zh, string en)
        {
            string code = LanguageCode();
            if (code != null && code.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
                return zh;
            string ru;
            if (en != null && code != null && code.StartsWith("ru", StringComparison.OrdinalIgnoreCase) && TextRu.Texts.TryGetValue(en, out ru))
                return ru;
            return en;
        }

        private static readonly Dictionary<string, string> ClassNamesEn = new Dictionary<string, string>
        {
            { "手枪", "Pistols" }, { "左轮（含左轮卡宾）", "Revolvers and revolver carbines" }, { "小口径步枪", "Small-bore rifles" },
            { "老式与古董", "Old and antique guns" }, { "冲锋枪", "Submachine guns" }, { "霰弹枪", "Shotguns" }, { "手枪口径卡宾", "Pistol-caliber carbines" },
            { "狩猎步枪", "Hunting rifles" }, { "杠杆步枪", "Lever-action rifles" }, { "中间威力步枪", "Intermediate rifles" },
            { "全威力步枪", "Full-power rifles" }, { "精确步枪", "Precision rifles" }, { "机枪", "Machine guns" },
            { "反器材步枪", "Anti-materiel rifles" }, { "重机枪", "Heavy machine guns" }, { "榴弹发射器", "Grenade launchers" },
        };

        // A class of the catalog (final-strategy 9) in the game's language.
        internal static string ClassName(string zh)
        {
            string en;
            return Pick(zh, ClassNamesEn.TryGetValue(zh, out en) ? en : zh);
        }

        // The game's current stat tags (GunHelper.UpdateCurrentGunStats) by the names its tooltips use.
        internal static string StatName(string tag)
        {
            switch (tag)
            {
                case "GUN_CURRENT_FIREPOWER_TAG": return Pick("火力", "firepower");
                case "GUN_CURRENT_STYLE_TAG": return Pick("风格", "style");
                case "GUN_CURRENT_RELIABILITY_TAG": return Pick("可靠性", "reliability");
                case "GUN_CURRENT_RECOIL_TAG": return Pick("后坐力控制", "recoil control");
                case "GUN_CURRENT_ERGONOMICS_TAG": return Pick("人机工效", "ergonomics");
                case "GUN_CURRENT_ACCURACY_TAG": return Pick("精准度", "accuracy");
                default: return tag;
            }
        }

        // The catalog's tiers by the game's restriction levels (type_restricted_1 to 3; the special guns are contraband
        // level 4, the game's critical level) (final-strategy 103).
        internal static string TierName(string zh)
        {
            switch (zh)
            {
                case "一档": return Pick("一级管制", "Restricted I");
                case "二档": return Pick("二级管制", "Restricted II");
                case "三档": return Pick("三级管制", "Restricted III");
                case "特殊": return Pick("高危级违禁", "critical contraband");
                default: return zh;
            }
        }

        internal static string GunDescription(string className)
        {
            string[] pair;
            return Catalog.GunDescriptions.TryGetValue(className, out pair) ? Pick(pair[0], pair[1]) : "";
        }

        // One description for each gun (item 77; VVittgenstein 2026-11-05 and 2026-11-06, current/008 quotes 20 B, 25, 29): the gun's own text, or its class's while it has none.
        internal static string GunDescription(GunDef d)
        {
            string[] pair;
            if (d.AddOn && !string.IsNullOrEmpty(d.DescEn))
                return Chinese() ? d.DescZh : Russian() && !string.IsNullOrEmpty(d.DescRu) ? d.DescRu : d.DescEn;
            return Catalog.GunTexts.TryGetValue(d.Id, out pair) ? Pick(pair[0], pair[1]) : GunDescription(d.ClassName);
        }

        internal static string ModDescription(string category)
        {
            string[] pair;
            return Catalog.ModDescriptions.TryGetValue(category, out pair) ? Pick(pair[0], pair[1]) : "";
        }

        // Fault names of final-strategy 15 and 17.
        internal static string FaultName(Fault fault)
        {
            switch (fault)
            {
                case Fault.Screws: return Pick("螺丝松脱或缺失", "Loose or missing screws");
                case Fault.Wiring: return Pick("线路故障", "Wiring fault");
                case Fault.Rust: return Pick("锈蚀积垢", "Rust and fouling");
                case Fault.PartWear: return Pick("零件磨损", "Worn parts");
                case Fault.Weld: return Pick("焊点开裂", "Cracked weld");
                default: return Pick("枪管变形", "Bent barrel");
            }
        }

        internal static string FaultStateName(FaultState state)
        {
            switch (state)
            {
                case FaultState.Emergency: return Pick("已应急修复", "emergency repair");
                case FaultState.Fixed: return Pick("已修复", "repaired");
                default: return Pick("待修", "needs repair");
            }
        }

        // How a customer names a fault they know about.
        internal static string FaultComplaint(Fault fault)
        {
            switch (fault)
            {
                case Fault.Screws: return Pick("螺丝松了几颗，还掉了一颗", "a few screws are loose and one is gone");
                case Fault.Wiring: return Pick("电路时好时坏", "the electrics keep cutting out");
                case Fault.Rust: return Pick("锈得厉害，里面也脏", "it is rusty and dirty inside");
                case Fault.PartWear: return Pick("零件磨得厉害", "the parts are badly worn");
                case Fault.Weld: return Pick("有个焊点裂了", "a weld has cracked");
                default: return Pick("枪管弯了", "the barrel is bent");
            }
        }

        internal static string RepairGreeting(int variant)
        {
            return variant == 0 ? Pick("嘿，我有把枪要修。", "I have a gun that needs fixing.") : Pick("听说你这儿能修枪？", "I hear you fix guns here?");
        }

        // What the repair customer says after the faults: only the repair (current/008 quote 18 point A), or the repair and an improvement
        // with the budget for both.
        internal static string RepairOnlyRequest()
        {
            return Pick("修好就行，别的不用动。", " Just fix it and leave the rest alone.");
        }

        internal static string UpgradeRequest(int budget)
        {
            return string.Format(Pick("修好之后顺便帮我优化一下，修理和配件一共 {0} 信用点。", " While you're at it, improve it a little. Repairs and parts together, I can spend {0} credits."), budget);
        }

        // A customer with more than one gun (item 81; VVittgenstein, current/008 quote 25).
        internal static string RepairGreetingMany()
        {
            return Pick("我有几把枪要修。", "I have a few guns that need fixing.");
        }

        internal static string RepairReturnGreeting(bool many)
        {
            return many
                ? Pick("又是我。上次修得不错，这几把也拜托你了。", "Me again. You did good work last time, so here are a few more.")
                : Pick("又是我。上次修得不错，这把也拜托你了。", "Me again. You did good work last time, so here's another one.");
        }

        internal static string GunDone()
        {
            return Pick("这把好了。", "That one's done.");
        }

        internal static string SharedBudget()
        {
            return Pick("这几把一起算。", " That's for all of them.");
        }

        // What he has left for the guns still to come, the gun just paid taken off.
        internal static string UpgradeLeft(int budget)
        {
            return string.Format(Pick("修好之后也顺便优化一下，修理和配件还能花 {0} 信用点。", " Improve this one too. Repairs and parts together, I can still spend {0} credits."), budget);
        }

        // Which of his guns he is talking about.
        internal enum GunTurn
        {
            Only,
            First,
            Another,
            Third,
            Last,
        }

        private static string RequestFormat(GunTurn turn)
        {
            switch (turn)
            {
                case GunTurn.First: return Pick("先看这把{0}，{1}。帮我看看吧。", "Start with this {0}: {1}. Can you take a look?");
                case GunTurn.Another: return Pick("还有一把{0}，{1}。帮我看看吧。", "There's also this {0}: {1}. Can you take a look?");
                case GunTurn.Third: return Pick("第三把是{0}，{1}。帮我看看吧。", "The third one is this {0}: {1}. Can you take a look?");
                case GunTurn.Last: return Pick("最后一把是{0}，{1}。帮我看看吧。", "The last one is this {0}: {1}. Can you take a look?");
                default: return Pick("是这把{0}，{1}。帮我看看吧。", "It's this {0}: {1}. Can you take a look?");
            }
        }

        // The repair customer's request (final-strategy 2 and 17: the customer names the gun and the faults they know).
        internal static string RepairRequest(string gunName, System.Collections.Generic.Dictionary<Fault, FaultState> faults, GunTurn turn = GunTurn.Only)
        {
            var known = new System.Collections.Generic.List<string>();
            foreach (System.Collections.Generic.KeyValuePair<Fault, FaultState> kv in faults)
            {
                if (kv.Value == FaultState.Known)
                    known.Add(FaultComplaint(kv.Key));
            }
            return string.Format(RequestFormat(turn), gunName, string.Join(Pick("，", ", "), known.ToArray()));
        }

        // Caliber families added by this mod; the game names 22, 10, 45 and 12 itself and shows other numbers as is.
        internal static string CaliberName(int caliber)
        {
            switch (caliber)
            {
                case 556: return Pick("中间威力步枪弹", "intermediate rifle");
                case 762: return Pick("全威力步枪弹", "full-power rifle");
                case 50: return Pick("重型弹", "heavy .50");
                case 40: return Pick("40 毫米榴弹", "40 mm grenade");
                default: return null;
            }
        }
    }

    // Lines of this mod's tooltips cut to fit the game's tooltip (VVittgenstein 2026-11-04 11:2x, current/006 quote 35 point 1:
    // the fault lines were badly laid out). GameItemElement.GetTooltipBasic builds the tooltip 40 characters wide and
    // breaks a longer line at a space (RichTextBuilder.FindBestSlice); a Chinese line has no space, and in 0.6.0 a
    // long one that began with spaces came out as an empty line followed by the text from the left edge. In Chinese the
    // game's limit is 26 characters of any kind (RichTextBuilder.Build: UISettings.GetCharWidthScale is 2 for zh, ja and
    // ko, and then the width is 40 x 0.66), 40 in the other languages. Here a line is cut before it reaches Width, every
    // character counting 1.5 in Chinese (at most 25 on a line) and 1 otherwise; a continued line keeps the indent plus
    // two spaces. Closing punctuation and the unit 毫升 stay with what comes before them, opening punctuation with what
    // comes after.
    internal static class Lines
    {
        internal const float Width = 38f;
        private const string Closing = "，。；：、）！？”』」%,.;:!?)毫升";
        private const string Opening = "（“『「(";

        internal static bool IsWide(char c)
        {
            return (c >= '⺀' && c <= '鿿') || (c >= '가' && c <= '힯') || (c >= '豈' && c <= '﫿')
                || (c >= '︰' && c <= '﹏') || (c >= '＀' && c <= '｠') || (c >= '￠' && c <= '￦')
                || c == '“' || c == '”';
        }

        internal static float Measure(string s)
        {
            if (Text.Chinese())
                return s.Length * 1.5f;
            float w = 0f;
            foreach (char c in s)
                w += IsWide(c) ? 1.5f : 1f;
            return w;
        }

        // Words: a run of narrow characters between spaces, or one wide character; each word knows whether a space
        // came before it.
        private static List<KeyValuePair<string, bool>> Words(string text)
        {
            var words = new List<KeyValuePair<string, bool>>();
            var sb = new System.Text.StringBuilder();
            bool spaceBefore = false, pendingSpace = false;
            Action flush = () =>
            {
                if (sb.Length > 0)
                {
                    words.Add(new KeyValuePair<string, bool>(sb.ToString(), spaceBefore));
                    sb.Length = 0;
                }
            };
            foreach (char c in text)
            {
                if (c == ' ')
                {
                    flush();
                    pendingSpace = true;
                    continue;
                }
                bool wide = IsWide(c);
                if (wide || (sb.Length > 0 && IsWide(sb[sb.Length - 1])))
                    flush();
                if (sb.Length == 0)
                {
                    spaceBefore = pendingSpace;
                    pendingSpace = false;
                }
                sb.Append(c);
            }
            flush();
            // punctuation joins its neighbour
            for (int i = words.Count - 1; i > 0; i--)
            {
                string w = words[i].Key;
                if (w.Length == 1 && Closing.IndexOf(w[0]) >= 0 && !words[i].Value)
                {
                    words[i - 1] = new KeyValuePair<string, bool>(words[i - 1].Key + w, words[i - 1].Value);
                    words.RemoveAt(i);
                }
            }
            for (int i = 0; i < words.Count - 1; i++)
            {
                string w = words[i].Key;
                if (w.Length >= 1 && Opening.IndexOf(w[w.Length - 1]) >= 0 && !words[i + 1].Value)
                {
                    words[i] = new KeyValuePair<string, bool>(w + words[i + 1].Key, words[i].Value);
                    words.RemoveAt(i + 1);
                    i--;
                }
            }
            return words;
        }

        // A line is broken after a comma, semicolon or colon when that keeps at least half the width on it, else
        // before the word that does not fit; spaces at the start of the text (the game's list lines begin with one)
        // are kept as part of the indent.
        private const string BreakAfter = "，；：、,;:";

        private static string Join(string prefix, List<KeyValuePair<string, bool>> words)
        {
            var sb = new System.Text.StringBuilder(prefix);
            for (int i = 0; i < words.Count; i++)
            {
                if (i > 0 && words[i].Value)
                    sb.Append(' ');
                sb.Append(words[i].Key);
            }
            return sb.ToString();
        }

        internal static List<string> Wrap(string indent, string text)
        {
            var lines = new List<string>();
            text = text ?? "";
            int lead = 0;
            while (lead < text.Length && text[lead] == ' ')
                lead++;
            indent += new string(' ', lead);
            string continued = indent + "  ";
            string prefix = indent;
            var line = new List<KeyValuePair<string, bool>>();
            foreach (KeyValuePair<string, bool> word in Words(text.Substring(lead)))
            {
                line.Add(word);
                while (line.Count > 1 && Measure(Join(prefix, line)) > Width)
                {
                    KeyValuePair<string, bool> last = line[line.Count - 1];
                    line.RemoveAt(line.Count - 1);
                    int cut = line.Count;
                    for (int k = line.Count - 1; k >= 1; k--)
                    {
                        string w = line[k - 1].Key;
                        if (BreakAfter.IndexOf(w[w.Length - 1]) >= 0)
                        {
                            if (Measure(Join(prefix, line.GetRange(0, k))) >= Width / 2)
                                cut = k;
                            break;
                        }
                    }
                    lines.Add(Join(prefix, line.GetRange(0, cut)));
                    List<KeyValuePair<string, bool>> rest = line.GetRange(cut, line.Count - cut);
                    rest.Add(last);
                    line = rest;
                    prefix = continued;
                }
            }
            if (line.Count > 0)
                lines.Add(Join(prefix, line));
            return lines;
        }

        internal static void Add(RichTextBuilder builder, string indent, string text, RenderHandler.ColorPalette color)
        {
            foreach (string line in Wrap(indent, text))
                builder.AddLine(line, true, color);
        }
    }

    [HarmonyPatch(typeof(GunHelper), nameof(GunHelper.GetCaliberName))]
    internal static class CaliberNamePatch
    {
        private static void Postfix(int caliber, ref string __result)
        {
            string name = Text.CaliberName(caliber);
            if (name != null)
                __result = name;
        }
    }
}
