namespace GunsmithExpansion
{
    // The lines and short texts of T-013 (final-strategy 332, 358): the Chinese is the final text of
    // review/2026-11-21/t013-dialogues.md, word for word without the 「」 marks the draft quotes its lines with; {位置} and the
    // like became {0}. The English is the Agent's translation (current/T-013 2026-11-22 04:15, the Agent's arrangement 4); the
    // Russian, French and Japanese are looked up by the English (Text.Pick, data/texts-*.tsv). Arrays by level are indexed by
    // the favor level, 0 to 4; a line for a range of levels is repeated.
    internal static class LincolnTexts
    {
        internal sealed class Line
        {
            internal readonly string Zh;
            internal readonly string En;

            internal Line(string zh, string en)
            {
                Zh = zh;
                En = en;
            }

            internal string Get()
            {
                return Text.Pick(Zh, En);
            }

            internal string Format(params object[] args)
            {
                return string.Format(Get(), args);
            }
        }

        private static Line L(string zh, string en)
        {
            return new Line(zh, en);
        }

        // Section 0: names and short texts.
        internal static readonly Line Yuri = L("尤里·林肯", "Yuri Lincoln");
        internal static readonly Line Ava = L("艾娃·林肯", "Ava Lincoln");
        internal static readonly Line ShopName = L("林肯军火行", "Lincoln Arms");
        internal static readonly Line CardName = L("林肯军火行名片", "Lincoln Arms Business Card");
        internal static readonly Line CardDescription = L("货运区后街四号窗的通行名片。双击查看卡面。",
            "Pass card for window four on the back street of the freight district. Double-click to look at the card.");
        internal static readonly Line CardDescriptionPhone = L("货运区后街四号窗的通行名片，专线电话 3955。双击查看卡面。",
            "Pass card for window four on the back street of the freight district, direct line 3955. Double-click to look at the card.");
        internal static readonly Line PartsBoxName = L("退休枪匠的配件箱", "Retired Gunsmith's Parts Box");
        internal static readonly Line PartsBoxDescription = L("退休枪匠打包封存的一箱闲置改件，内含 4 件随机枪械配件。双击拆封取出。",
            "A sealed box of spare parts the retired gunsmith packed up, with 4 random gun mods inside. Double-click to open it and take them out.");
        internal static readonly Line GunBoxName = L("退休枪匠的{0}箱", "Retired Gunsmith's Box ({0})");
        internal static readonly Line GunBoxDescription = L("退休枪匠从旧藏里摸出的一把{0}，成色随机。双击拆封取出。",
            "A gun ({0}) the retired gunsmith dug out of his old collection, in random condition. Double-click to open it and take it out.");
        internal static readonly Line MaterialBoxName = L("退休枪匠的材料箱", "Retired Gunsmith's Materials Box");
        internal static readonly Line MaterialBoxDescription = L("退休枪匠从工作台下搜罗的一箱修枪耗材，内含 6 份随机维修材料。双击拆封取出。",
            "A box of repair supplies the retired gunsmith scraped together from under his workbench, with 6 random repair materials inside. Double-click to open it and take them out.");

        internal static readonly Line[] StatusLine =
        {
            L("林肯军火行 · 好感度 {0}（Lv.0 新客 · 下一档 10）", "Lincoln Arms · Favor {0} (Lv.0 new customer · next level at 10)"),
            L("林肯军火行 · 好感度 {0}（Lv.1 常客 · 电话 3955 已开通 · 下一档 30）", "Lincoln Arms · Favor {0} (Lv.1 regular · phone 3955 open · next level at 30)"),
            L("林肯军火行 · 好感度 {0}（Lv.2 熟客 · 全店 95 折 · 下一档 60）", "Lincoln Arms · Favor {0} (Lv.2 familiar face · 5% off everything · next level at 60)"),
            L("林肯军火行 · 好感度 {0}（Lv.3 密友 · 全店 92 折 · 下一档 100）", "Lincoln Arms · Favor {0} (Lv.3 close friend · 8% off everything · next level at 100)"),
            L("林肯军火行 · 好感度 {0}（Lv.4 搭档 · 全店 90 折 · 已满级）", "Lincoln Arms · Favor {0} (Lv.4 partner · 10% off everything · top level)"),
        };

        // Index 1 to 4; 0 is not notified.
        internal static readonly Line[] LevelNotice =
        {
            null,
            L("林肯军火行好感度达到 Lv.1：已开通订货专线 3955，解锁新一批商品。",
                "Lincoln Arms favor reached Lv.1: the order line 3955 is open, and a new batch of goods is unlocked."),
            L("林肯军火行好感度达到 Lv.2：全店与电话享 95 折，解锁军规货源与极佳成色枪械。",
                "Lincoln Arms favor reached Lv.2: 5% off in the shop and on the phone; military-grade goods and guns in excellent condition are unlocked."),
            L("林肯军火行好感度达到 Lv.3：全店与电话享 92 折，解锁全新成色枪械与高阶配件。",
                "Lincoln Arms favor reached Lv.3: 8% off in the shop and on the phone; new guns and high-end gun mods are unlocked."),
            L("林肯军火行好感度达到 Lv.4：全店与电话享 90 折，全部压箱底货源已开放。",
                "Lincoln Arms favor reached Lv.4: 10% off in the shop and on the phone; every last bit of the reserved stock is open."),
        };

        // Section 1: Yuri's first visit to the player's store.
        internal static readonly Line IntroFirst = L("新开的铺子？我在街对面看了你两天，手里的活还算干净。",
            "A new shop? I've been watching you from across the street for two days. Your work is clean enough.");
        internal static readonly Line IntroSecond = L("我叫尤里·林肯。货运区后街四号窗归我管，整枪、改件、芯片、修枪耗材，只要你证照齐全，我那里都有现货。",
            "My name is Yuri Lincoln. I run window four on the back street of the freight district. Whole guns, parts, chips, repair supplies: as long as your permits are in order, I have it all in stock.");
        internal static readonly Line IntroReply = L("夜里过去看看。", "I'll come by tonight.");
        internal static readonly Line IntroCardOnCounter = L("夜里见", "See you tonight.");
        internal static readonly Line IntroCardElsewhere = L("你前柜台堆满了，名片给你放到 {0} 了。夜里见。",
            "Your front counter is full, so I put the card in {0}. See you tonight.");
        internal static readonly Line IntroNoRoom = L("你这店里连放张卡片的空儿都没有。腾个地方出来，我等着。",
            "There isn't room in here even for a card. Clear a spot, I'll wait.");
        internal static readonly Line Cleared = L("腾好了。", "There's room now.");

        // Section 2: Yuri's deliveries (index 1 to 4; 0 is not used, the phone opens at Lv.1).
        internal static readonly Line[] YuriDelivered =
        {
            null,
            L("你在电话里报的货，按时送到了，自己开箱点一下。", "The goods you ordered on the phone, right on time. Open the box and check them."),
            L("给你挑了批品相稳妥的。箱子在台面上", "I picked you a solid lot. The box is on the counter."),
            L("专线的货到了。外头巡逻队查得紧，好东西早点收进后库。", "Your order from the line is here. The patrols are strict out there; get the good stuff into the back early."),
            L("点名要的尖货，我亲自押过来的，箱子放这儿了。", "The prime goods you asked for by name. I escorted them myself; the box is right here."),
        };

        internal static readonly Line[] YuriDeliveredElsewhere =
        {
            null,
            L("你在电话里报的货送到了。前柜台没空位，给你放到 {0} 了。", "The goods you ordered on the phone are here. There was no room on the front counter, so they're in {0}."),
            L("你要的货我带来了。前头台面摆不下，替你搁在 {0} 了，抽空清点一下。", "I brought what you asked for. There was no room up front, so I left it in {0}. Count it when you have a moment."),
            L("专线的货到了。前柜台堆得太满，箱子直接给你挪去 {0} 了，早点收好。", "Your order from the line is here. The front counter is piled too high, so I moved the box to {0}. Put it away soon."),
            L("老搭档的货我亲自押来了。看你柜台忙，直接给你放进 {0} 了，一件不少。", "I brought my partner's order myself. Your counter looks busy, so it went straight into {0}. Nothing is missing."),
        };

        internal static readonly Line[] YuriNoRoom =
        {
            null,
            L("货带到了，可你店里连放箱子的空位都没有。先腾块地方，我等着。", "I've brought the goods, but there's no room for the box anywhere in your shop. Clear a space first, I'll wait."),
            L("货带到了，你这儿塞得没处下脚。腾个空位出来吧。", "The goods are here, but this place is packed to the door. Make some room."),
            L("生意不错，屋里都堆满了。箱子我先拎着，你挪块空地出来。", "Business is good, the place is full. I'll hold the box; you clear a spot."),
            L("满屋子都是货啊，老搭档。你先腾个位置，我等你。", "Goods everywhere, partner. Make a spot first, I'll wait for you."),
        };

        internal static readonly Line[] YuriDeliveryBye =
        {
            null,
            L("走了，缺货再打给我。", "I'm off. Call me when you run short."),
            L("我回后街了，有需要再联系。", "Back to the back street. Call if you need anything."),
            L("回见，铺子里还留着新货。", "See you. There's new stock waiting at the shop."),
            L("回见，老搭档。", "See you, partner."),
        };

        // Section 3: Ava's deliveries. Her introduction before the first delivery she brings to a player who never met her: [0] at
        // Lv.1, [1] from Lv.2.
        internal static readonly Line[][] AvaIntro =
        {
            new[]
            {
                L("喂，你还没见过我吧？我是艾娃·林肯，是尤里·林肯的女儿。", "Hey, you haven't met me yet, have you? I'm Ava Lincoln, Yuri Lincoln's daughter."),
                L("我爸今天歇班，把送货的活儿甩给我了。", "My dad is off today, so he dumped the deliveries on me."),
            },
            new[]
            {
                L("嘿，哥，头一回见面吧？我是尤里·林肯的女儿艾娃！", "Hey, bro, first time we meet, right? I'm Ava, Yuri Lincoln's daughter!"),
                L("常听我爸提起你的铺子。他今天歇班偷懒，差使我来给你送货啦！", "My dad talks about your shop all the time. He's taking it easy today, so he sent me with your delivery!"),
            },
        };

        internal static readonly Line[] AvaDelivered =
        {
            null,
            L("喂，开门收货啊。你订的包裹我顺路给你捎过来了，拆开核对一下。", "Hey, open up for your delivery. I brought the package you ordered on my way; open it and check."),
            L("嘿，哥，你的件到啦！我爸在家歇着差使我跑腿，搁台面上啦！", "Hey, bro, your package is here! My dad is resting at home and sent me running. It's on the counter!"),
            L("嘿，哥，快来看看这批货合不合心意！我刚从后街仓库直接提出来的，连封条都是我亲手贴的，放这儿啦！", "Hey, bro, come and see if this lot suits you! Straight from the back street warehouse, and I put the seal on myself. I'm leaving it here!"),
            L("嘿，哥，专程给你跑一趟！你的货我爸非让我亲手交到你柜台上不可！", "Hey, bro, I made a special trip for you! My dad insisted I hand your goods over at your counter myself!"),
        };

        internal static readonly Line[] AvaDeliveredElsewhere =
        {
            null,
            L("喂，开门收货啊。你前柜台摆得满满当当的，箱子我塞到 {0} 去了。", "Hey, open up for your delivery. Your front counter is crammed full, so I stuck the box in {0}."),
            L("嘿，哥，你的件！看你前柜台堆满了家伙，我干脆帮你把箱子搬到 {0} 了，省得挡你做生意！", "Hey, bro, your package! Your front counter is full of hardware, so I just moved the box to {0} for you. Wouldn't want it in the way of business!"),
            L("嘿，哥，送货上门！你这柜台比我家库房还挤，包裹我熟门熟路给你搁 {0} 里了，记得去拆啊！", "Hey, bro, delivery! Your counter is more crowded than our storeroom, so I put the package in {0}; I know my way around. Don't forget to open it!"),
            L("嘿，哥，专程给你跑一趟！前头没空地儿，我帮你放进 {0} 了！", "Hey, bro, I made a special trip for you! There was no room up front, so I put it in {0} for you!"),
        };

        internal static readonly Line[] AvaNoRoom =
        {
            null,
            L("喂，你这店里塞得连个放箱子的缝儿都没了。快腾块地方出来，我赶时间呢。", "Hey, there isn't even a gap for a box in this place. Clear a spot quick, I'm in a hurry."),
            L("嘿，哥，你这店里塞得也太满了吧！快腾个空位出来，箱子沉着呢。", "Hey, bro, your shop is way too full! Make some room quick, this box is heavy."),
            L("嘿，哥，生意这么火，满屋子连落箱子的地儿都没啦！快挪一小块空位给我。", "Hey, bro, business is booming, there's nowhere left to set a box down! Free up a little space for me."),
            L("嘿，哥，你这铺子都快堆成军火库啦！快帮我腾个地方，我把箱子给你放好。", "Hey, bro, your place is turning into an armory! Make room for me and I'll set the box down properly."),
        };

        internal static readonly Line[] AvaDeliveryBye =
        {
            null,
            L("货交清了，我先走了啊。", "Everything's delivered. I'm off."),
            L("那我先撤啦，哥，忙你的！", "I'll get going then, bro. Back to work with you!"),
            L("走啦哥，夜里得空来后街铺子转转啊！", "Off I go, bro. Drop by the back street shop some night!"),
            L("回见啦哥，有事随时打专线喊我！", "See you, bro! Call me on the line anytime!"),
        };

        // Section 4: the shop at night with Yuri. The two lines of the first visit at a level, each said once (4-1).
        internal static readonly Line[][] YuriLevelVisit =
        {
            new[]
            {
                L("找到地方了？守时的人在这行活得长。", "Found the place? People who keep time live long in this trade."),
                L("架子上全是现货。看中哪件直接拿到台面上结账，不讲价。", "Everything on the shelves is in stock. Take what you want to the counter and pay. No haggling."),
            },
            new[]
            {
                L("这阵子看你出货挺稳，是个正经做买卖的。", "You've been moving goods steadily. You run an honest business."),
                L("二档货和一批实用改件给你挂出来了。白天急用单件，打 3955 找我。", "I've put out Restricted II goods and a batch of practical parts for you. If you need a single piece urgently in the daytime, call me at 3955."),
            },
            new[]
            {
                L("外头不少佣兵都在提你的铺子。你卖出去的枪越多，咱们这行的路子就越宽。", "Plenty of mercenaries out there talk about your shop. The more guns you sell, the wider the road for our trade."),
                L("从今晚起，军规货和极佳成色的家伙向你开放，账面给你让五分利。", "From tonight, military-grade goods and guns in excellent condition are open to you, and I'll take five percent off your bill."),
            },
            new[]
            {
                L("老主顾了。站里能把枪械买卖做到这个份上的人不多。", "You're a regular now. Not many on this station run a gun trade the way you do."),
                L("原厂未拆封的新枪和高阶瞄具都给你摆出来了，以后按九二折算。", "Factory-sealed new guns and high-end sights are out for you. From now on it's eight percent off."),
            },
            new[]
            {
                L("到今天，林肯军火行对你没有任何保留了，老搭档。", "As of today, Lincoln Arms holds nothing back from you, partner."),
                L("全店九折。外头严查的重武器和稀罕孤品，只要进了我的库房，头一个挑的人就是你。", "Ten percent off everything. The heavy weapons they hunt for outside and the one-of-a-kind pieces: once they're in my storeroom, you get first pick."),
            },
        };

        // 4-2: one of these when the player comes in.
        internal static readonly Line[][] YuriGreeting =
        {
            new[]
            {
                L("来了。架子上有现货", "You're here. The shelves are stocked."),
                L("新面孔，规矩你懂，不讲价。", "New face. You know the rule: no haggling."),
            },
            new[]
            {
                L("又见面了。新一批二档货刚上墙，自己看。", "Here again. A new batch of Restricted II goods just went up on the wall. Have a look."),
                L("来得正好，刚拆了几箱改件和消音器。", "Good timing. I just unpacked a few crates of parts and suppressors."),
                L("随便看。白天走不开的话，打电话也是一样。", "Look around. If you can't get away in the daytime, a phone call works too."),
            },
            new[]
            {
                L("熟客来了。刚擦出来几把品相干净的家伙。", "A familiar face. I just cleaned up a few pieces in fine shape."),
                L("你要的军规货今天备齐了。", "The military-grade goods you want are all in today."),
                L("看你气色不错，最近店里单子没少接吧。", "You look well. Plenty of orders at your shop lately, I take it."),
            },
            new[]
            {
                L("老朋友来了。今天到了几把原厂油封的新枪，自己看。", "Old friend. A few new guns came in today, still in factory grease. Take a look."),
                L("歇口气，看上什么尽管往台面上拿。", "Catch your breath. Whatever you like, take it to the counter."),
                L("最近巡逻队在查货运通道，好东西趁早备下。", "The patrols are checking the freight passages lately. Stock up on the good stuff while you can."),
            },
            new[]
            {
                L("来了，老搭档。整面墙最好的货都在这儿了。", "Here you are, partner. The best goods are all on this wall."),
                L("就猜到你今晚会过来。里间刚清出一批尖货，随便挑。", "I figured you'd come tonight. I just cleared a batch of prime goods out of the back room. Take your pick."),
                L("自己人不用客套。", "No need for formalities between us."),
            },
        };

        // 4-3: one of these after 22 seconds without a new line.
        internal static readonly Line[][] YuriIdle =
        {
            new[]
            {
                L("好铁不等人，看准了型号就下手。", "Good steel doesn't wait. Once you've settled on a model, take it."),
                L("修枪耗材、打印芯片和整枪都有现货，缺什么自己挑。", "Repair supplies, printer chips and whole guns are all in stock. Pick what you need."),
            },
            new[]
            {
                L("自己压子弹比买现成的省钱，复装器材你可以留心看看。", "Loading your own rounds costs less than buying them. Keep an eye on the reloading gear."),
                L("这站上最近不太平，冲锋枪和霰弹枪走得很快。", "Things are rough on the station lately. Submachine guns and shotguns are going fast."),
            },
            new[]
            {
                L("越是精密的步枪，越挑手艺。好在你那边名声立住了。", "The finer the rifle, the more it asks of the gunsmith. Good thing your name is made."),
                L("我女儿偶尔会替我看一天店，她嘴快，账算得不含糊。", "My daughter minds the shop for me now and then. Quick tongue, but her accounts are exact."),
            },
            new[]
            {
                L("有人问我为什么把好枪分给下层区的铺子。手里有铁的人，才有资格谈规矩。", "Someone asked me why I give good guns to a shop in the lower levels. Only those holding steel get a say in the rules."),
                L("那几支高倍镜刚校准过，配精确步枪正好。", "Those high-power scopes were just zeroed. Just right for a precision rifle."),
            },
            new[]
            {
                L("干了半辈子这行，见惯了拿货跑路的蠢货，像你这么稳的搭档少见。", "Half a lifetime in this trade, I've seen plenty of fools run off with the goods. A partner as steady as you is rare."),
                L("要是瞧见外头没见过的孤品，别声张，那是留给你的。", "If you see a one-of-a-kind piece you've never seen out there, keep quiet. That one is for you."),
            },
        };

        // 4-4 and 5-4: what is said at the counter, by level.
        internal sealed class CounterLines
        {
            internal Line Gun, RareGun, Part, Withdrawn, Paid, Bye;
        }

        internal static readonly CounterLines[] YuriCounter =
        {
            new CounterLines
            {
                Gun = L("底子干净的实用货，拿回去转手不愁买家。", "A clean, practical piece. You won't lack buyers when you sell it on."),
                RareGun = L("算你眼尖，这种老家伙流出来的可不多。", "Sharp eye. Not many of these old pieces get out."),
                Part = L("做工扎实的基础件，够你应付日常单子了。", "Solid basic parts. Enough for your everyday orders."),
                Withdrawn = L("嫌贵，还是证照没办齐？自己掂量。", "Too pricey, or are your permits not in order? Your call."),
                Paid = L("银货两讫，东西归你了。", "Paid in full. It's yours."),
                Bye = L("夜深了，回店路上避开巡逻岗。", "It's late. Avoid the patrol posts on your way back."),
            },
            new CounterLines
            {
                Gun = L("二档里的硬通货，佣兵和护卫最认这种。", "Hard currency among Restricted II. Mercenaries and guards trust these most."),
                RareGun = L("这种老枪现在不好找了，懂行的人会出好价。", "Old guns like this are hard to find now. People who know will pay well."),
                Part = L("消音器和制退器最近很紧俏，备着不亏。", "Suppressors and muzzle brakes are in demand lately. Worth keeping a few."),
                Withdrawn = L("改主意了？架子上的货今晚都在，慢慢挑。", "Changed your mind? Everything on the shelves is here all night. Take your time."),
                Paid = L("账清了，合作愉快。", "We're square. Pleasure doing business."),
                Bye = L("东西收进背囊再走，这片眼杂。", "Pack it in your bag before you go. Lots of eyes around here."),
            },
            new CounterLines
            {
                Gun = L("成色挑过了，拿去交定制单绰绰有余。", "Hand-picked condition. More than good enough for a custom order."),
                RareGun = L("好眼光。这种来路特殊的家伙，离了这扇窗我可不认账。", "Good eye. A piece with a history like this: once it leaves this window, I never saw it."),
                Part = L("军规光学件，装上以后准头和卖相都上得去。", "Military-grade optics. Mount them and both the aim and the looks go up."),
                Withdrawn = L("不急，挑到合手的再结。", "No rush. Pay when you find the right one."),
                Paid = L("点好货，收妥了。", "Goods counted, payment received."),
                Bye = L("好枪配行家，回去赚一笔吧。", "Good guns for a good gunsmith. Go and make some money."),
            },
            new CounterLines
            {
                Gun = L("原厂出来的成色，膛线都是崭新的。", "Factory condition. The rifling is brand new."),
                RareGun = L("重家伙。治安部查起来的时候，它从来没在我这儿出现过。", "Heavy piece. If Security comes asking, it was never here."),
                Part = L("高阶镜子，上层区那帮挑剔的买家就认这个。", "High-end glass. The picky buyers of the upper levels want exactly this."),
                Withdrawn = L("没挑中顺手的？明晚过来，库房每天都补新货。", "Nothing that suits you? Come tomorrow night; the storeroom restocks every day."),
                Paid = L("痛快，和你做生意省心。", "Done. Doing business with you is easy."),
                Bye = L("路上留神，替我向你的主顾问好。", "Watch yourself out there. Give my regards to your customers."),
            },
            new CounterLines
            {
                Gun = L("顶格成色，摆进柜子就是镇店货。", "Top condition. Put it in your display case and it will anchor the shop."),
                RareGun = L("压箱底的狠货都让你摸出来了，收稳了。", "You've dug out the heaviest piece I keep. Hold on to it."),
                Part = L("全套顶配，需要多少尽管搬。", "Top grade all round. Take as much as you need."),
                Withdrawn = L("眼光越来越挑了？没关系，库房随时为你开着。", "Getting pickier? No matter; the storeroom is always open for you."),
                Paid = L("清账。拿去让这站上的人见识见识什么叫好枪。", "Settled. Go and show this station what a good gun is."),
                Bye = L("慢走，老搭档。", "Take care, partner."),
            },
        };

        // Section 5: the shop at night with Ava. Her first night with the player (5-1): [never met, Lv.0 or 1], [met on a
        // delivery, Lv.1], [never met, from Lv.2], [met, from Lv.2].
        internal static readonly Line[] AvaFirstNightNeverMetLow =
        {
            L("喂，找尤里·林肯？我爸今天歇班，我是他女儿艾娃，今晚归我看店。", "Hey, looking for Yuri Lincoln? My dad is off today. I'm his daughter Ava, and tonight the shop is mine."),
            L("店里的型号和价码我都清楚，看中什么放台面上就行。", "I know every model and price in here. Just put whatever you like on the counter."),
        };

        internal static readonly Line[] AvaFirstNightMetLow =
        {
            L("喂，真找过来啦？今晚也是我盯铺子。", "Hey, you actually found the place? I'm minding the shop tonight too."),
            L("现货都在架子上，自己慢慢挑吧。", "Everything in stock is on the shelves. Take your time."),
        };

        internal static readonly Line[] AvaFirstNightNeverMetHigh =
        {
            L("嘿，哥，找我爸呀？他今天休假去了，我是他女儿艾娃，常听他提起你！", "Hey, bro, looking for my dad? He's taken the day off. I'm his daughter Ava; he talks about you a lot!"),
            L("今晚这扇窗归我当家，你的权限和折扣我都调好啦，架子上的货随便挑！", "Tonight this window is mine to run. Your access and your discount are all set; pick anything off the shelves!"),
        };

        internal static readonly Line[] AvaFirstNightMetHigh =
        {
            L("嘿，哥，在后街铺子里撞见你啦！", "Hey, bro, fancy running into you at the back street shop!"),
            L("我爸今天歇班，今晚归我看店，架子上的货随便挑！", "My dad is off today, so tonight I'm minding the shop. Pick anything off the shelves!"),
        };

        // The first night she sees the player after a new level (index 1 to 4).
        internal static readonly Line[] AvaLevelVisit =
        {
            null,
            L("喂，我爸交代过了，二档货和 3955 专线都给你开了。白天犯懒不想跑夜路就打电话吧。", "Hey, my dad told me: Restricted II goods and the 3955 line are open for you. If you don't feel like coming out at night, just call in the daytime."),
            L("嘿，哥，你现在可是咱们家的熟客啦！军规货和折扣我都给你备好了，快挑挑看！", "Hey, bro, you're one of our regulars now! Your military-grade goods and your discount are ready. Go and pick something!"),
            L("嘿，哥！我爸昨晚盘账还夸你出货厉害呢，原厂新枪和高倍镜全给你挂上啦！", "Hey, bro! My dad was praising your sales when he went over the books last night. Factory-new guns and high-power scopes are all up for you!"),
            L("嘿，哥，我爸把里间暗格的钥匙都留给我了，交代说只要你来，压箱底的货随便你挑！", "Hey, bro, my dad left me the key to the hidden compartment in the back. He said whenever you come, you can pick anything he's been holding back!"),
        };

        internal static readonly Line[][] AvaGreeting =
        {
            new[]
            {
                L("喂，买东西？我爸不在，今晚我替他收账。", "Hey, buying? My dad's out; I'm taking the money for him tonight."),
                L("这么晚过来进货？架子上有现货，自己看吧。", "Stocking up this late? The shelves are full. Have a look."),
                L("随便看吧。先说好，我爸定的价，一分都不抹零啊。", "Look around. Just so you know, my dad sets the prices, and I don't round anything down."),
            },
            new[]
            {
                L("喂，又来了啊。新到的二档货和改件我都码上去了，自己看吧。", "Hey, back again. I've stacked up the new Restricted II goods and parts. Have a look."),
                L("来得挺巧，我刚把消音器和芯片清点完，缺什么自己拿。", "Good timing, I just finished counting the suppressors and chips. Take what you need."),
                L("今晚还是我看店。挑好了搁台面上，我给你算账。", "Me minding the shop again tonight. Put what you pick on the counter and I'll ring it up."),
            },
            new[]
            {
                L("嘿，哥，晚上好呀！我爸在家歇着呢，刚到的军规货我都给你摆齐啦！", "Hey, bro, good evening! My dad is resting at home; I've laid out all the new military-grade goods for you!"),
                L("嘿，哥，外头风凉吧？今天有几把极佳成色的好家伙，赶紧看看！", "Hey, bro, chilly out there? We've got a few beauties in excellent condition today. Go and take a look!"),
                L("嘿，哥，看到你就痛快，比外头那些磨磨唧唧的买家省心多啦！", "Hey, bro, always a pleasure to see you. You're way easier than those dithering buyers out there!"),
            },
            new[]
            {
                L("嘿，哥，来了就别客气！原厂新枪和高倍镜都在架子上，随便挑！", "Hey, bro, make yourself at home! Factory-new guns and high-power scopes are on the shelves. Pick away!"),
                L("嘿，哥！我爸又偷懒歇班，好在有我守着铺子，今天有好东西哦！", "Hey, bro! My dad is taking it easy again, good thing I'm minding the shop. We've got good stuff today!"),
                L("嘿，哥，来得正好！刚从后库搬出来几箱新货，快看看合不合心意！", "Hey, bro, perfect timing! I just hauled a few crates of new stock out of the back. See if anything suits you!"),
            },
            new[]
            {
                L("嘿，哥，你可算来啦！里间最好的成色和尖货我都给你端出来了！", "Hey, bro, finally! I brought out the best pieces and the prime goods from the back for you!"),
                L("嘿，哥，自家人不用寒暄，看上哪把狠家伙直接往台面上搬！", "Hey, bro, family doesn't need small talk. See a heavy piece you like? Straight to the counter with it!"),
                L("嘿，哥！今晚整面墙的货随你挑，连暗格里的私货都给你留着呢！", "Hey, bro! The whole wall is yours to pick from tonight, and I've even kept the private stock from the hidden compartment for you!"),
            },
        };

        internal static readonly Line[][] AvaIdle =
        {
            new[]
            {
                L("缺螺丝和零件的话，店里都有现成的。", "If you're short of screws and parts, we have them ready."),
                L("慢点挑，跑不了。", "Take your time. Nothing's going anywhere."),
            },
            new[]
            {
                L("白天要是临时差一两件芯片或者改件，打专线就行，省得半夜跑腿。", "If you're short a chip or a part or two in the daytime, just call the line. Saves you a trip at midnight."),
                L("我从小在这堆弹药箱旁边长大，擦枪验货可不比我爸慢。", "I grew up next to these ammo crates. I clean and check guns as fast as my dad does."),
            },
            new[]
            {
                L("我爸嘴上不说，其实挺佩服你那间小铺子的出货本事。", "My dad won't say it, but he really admires how much your little shop moves."),
                L("要是有中意的老枪，趁今晚还在赶紧下手啊。", "If there's an old gun you like, grab it while it's still here tonight."),
            },
            new[]
            {
                L("我爸前两天还念叨呢，说这站里能让他放心的枪匠就你一个。", "My dad was saying just the other day that you're the only gunsmith on this station he trusts."),
                L("那些高倍镜金贵着呢，我擦的时候都小心翼翼的。", "Those high-power scopes are precious. I'm extra careful when I clean them."),
            },
            new[]
            {
                L("要我说，咱们两家干脆把后街打通算了，省得你大晚上来回跑！", "If you ask me, we should just knock through the back street and join our shops. It would save you running back and forth at night!"),
            },
        };

        internal static readonly CounterLines[] AvaCounter =
        {
            new CounterLines
            {
                Gun = L("这把挺顺手的，拿回去应该好卖。", "This one handles well. It should sell easily."),
                RareGun = L("哟，角落里的老古董都被你挑出来了，眼力不错嘛。", "Oh, you found the old antique in the corner. Good eye."),
                Part = L("这几样都是修枪改枪最常用的，挑得挺准。", "These are the ones used most for repairs and mods. Good picks."),
                Withdrawn = L("怎么又放回去了？不合意就再看看别的。", "Putting them back? If they don't suit you, look at something else."),
                Paid = L("数清楚了，货归你。", "All counted. The goods are yours."),
                Bye = L("慢走啊，夜里少往巡逻队眼前凑。", "Take care. Stay out of the patrols' sight tonight."),
            },
            new CounterLines
            {
                Gun = L("这把二档货最近走得挺快，拿回去不愁压手。", "This Restricted II piece has been selling fast lately. It won't sit on your shelf."),
                RareGun = L("这种老物件店里也不常有，算你赶巧了。", "We don't often have old pieces like this. Lucky timing."),
                Part = L("配齐这一套，客人的改枪单就好交差了。", "Get this whole set and your customers' mod orders are easy to deliver."),
                Withdrawn = L("挑花眼了？没事，今晚我不急着关窗，你慢慢比对。", "Too many to choose from? It's fine, I'm in no hurry to close tonight. Compare all you like."),
                Paid = L("收妥了，账目两清。", "Received. We're square."),
                Bye = L("东西装严实了再走，回见。", "Pack it up tight before you go. See you."),
            },
            new CounterLines
            {
                Gun = L("嘿，哥，看这把的涂层多干净，摆柜子里准抢手！", "Hey, bro, look how clean the finish is on this one. It'll fly out of your display case!"),
                RareGun = L("嘘，小声点啊哥！这把可是好不容易流过来的稀罕货！", "Shh, keep it down, bro! This rare one took a lot to get here!"),
                Part = L("这批军规瞄具透光特别好，装枪上立马不一样。", "These military-grade sights let in so much light. Mount one and you'll see the difference right away."),
                Withdrawn = L("不急不急，哥你挑件最顺眼的再买！", "No rush, bro. Buy the one you like best!"),
                Paid = L("收妥啦，哥！点好你的东西！", "Got it, bro! Check your goods!"),
                Bye = L("路上小心啊哥，下次我代班还给你留好货！", "Be careful on the way, bro. Next time I'm on, I'll save you something good!"),
            },
            new CounterLines
            {
                Gun = L("原厂未拆封的新枪，连防锈油味儿都还在呢，哥！", "Factory-sealed and new, bro. You can still smell the rust oil!"),
                RareGun = L("哇，哥你真要把这个大家伙抱走呀？路上千万藏严实点！", "Wow, bro, you're really taking this big one? Keep it well hidden on the way!"),
                Part = L("高阶镜子配好枪，上层区的大买家肯定抢着掏钱！", "High-end glass on a good gun. The big buyers of the upper levels will be fighting to pay!"),
                Withdrawn = L("这几件还不合心意呀？再看看别的呗，哥！", "None of these suit you? Look at something else, bro!"),
                Paid = L("清账啦，麻利儿的！哥你现在手笔越来越大啦！", "All settled, nice and quick! You're spending bigger and bigger these days, bro!"),
                Bye = L("早点回去歇着啊哥，别光顾着赚钱熬坏了！", "Go home and rest, bro. Don't wear yourself out making money!"),
            },
            new CounterLines
            {
                Gun = L("挑得好，哥！顶配成色，拿回去镇场子准没错！", "Good pick, bro! Top condition, it'll anchor your shop for sure!"),
                RareGun = L("嘿，哥！这可是我爸藏在暗格最里头的宝贝，今天全归你啦！", "Hey, bro! This is the treasure my dad hid deepest in the compartment, and today it's all yours!"),
                Part = L("尽管拿，咱们库房管够！", "Take all you want, the storeroom has plenty!"),
                Withdrawn = L("挑剔起来啦，哥？哈哈，咱们家最好的货随时给你留着！", "Getting picky, bro? Ha, our best goods are always kept for you!"),
                Paid = L("妥啦！哥，跟你做买卖最痛快！", "Done! Bro, doing business with you is the best!"),
                Bye = L("回见啦哥！路上当心巡逻队，有事随时打专线喊我！", "See you, bro! Watch out for the patrols, and call me on the line anytime!"),
            },
        };

        // Section 6: the phone line 3955. Who answers: [0] Yuri Lv.1 to 2, [1] Yuri Lv.3 to 4, [2] Ava Lv.1, [3] Ava Lv.2 to 4.
        internal static int Voice(bool ava, int level)
        {
            if (ava)
                return level <= 1 ? 2 : 3;
            return level <= 2 ? 0 : 1;
        }

        internal static readonly Line[] PhoneOpen =
        {
            L("这里是林肯。报型号吧，全款不讲价，两天送到", "Lincoln here. Name the model. Full payment, no haggling, delivered in two days."),
            L("是我。专线给你留着配额，要什么？", "It's me. The line keeps a share for you. What do you need?"),
            L("喂，林肯军火行。我爸今天歇班，我接电话。全款不讲价，两天送到，要什么？", "Hey, Lincoln Arms. My dad is off today, so I'm on the phone. Full payment, no haggling, delivered in two days. What do you need?"),
            L("嘿，哥！我爸今天歇班，专线归我接！你要点啥？", "Hey, bro! My dad is off today, so the line is mine! What'll it be?"),
        };

        internal static readonly Line[] PhoneOpenQuotaUsed =
        {
            L("这里是林肯。材料包以外的指定货你这周已经调满了，急用就夜里来铺子拿。", "Lincoln here. You've had your full share of specific goods besides material packs this week. If it's urgent, come to the shop at night."),
            L("是我。这周的单件调货配额用完了，夜里直接过来挑吧；材料包的话现在还能发。", "It's me. This week's share of single items is used up; come and pick at night. Material packs I can still send now."),
            L("喂，你这周的指定货已经调满了，急用就夜里来铺子挑；材料包的话现在还能发。", "Hey, you've had your full share of specific goods this week. If it's urgent, come and pick at the shop at night. Material packs I can still send now."),
            L("嘿，哥！你这周的单件调货配额用完啦，急用别的就今晚来铺子里拿呗；材料包的话现在还能发！", "Hey, bro! You've used up this week's share of single items. If you need something else urgently, come and get it at the shop tonight. Material packs I can still send!"),
        };

        internal static readonly Line[] PhoneWhichKind =
        {
            L("要哪一类？", "Which kind?"),
            L("要哪一类？", "Which kind?"),
            L("具体要哪一类？", "Which kind exactly?"),
            L("具体要哪一类呀，哥？", "Which kind exactly, bro?"),
        };

        internal static readonly Line[] PhoneList =
        {
            L("目前能调的有这些。", "This is what I can get right now."),
            L("目前能调的有这些。", "This is what I can get right now."),
            L("能调出来的都在这儿了。", "This is everything I can get you."),
            L("现成能给你调出来的都在这儿啦，哥。", "Here's everything that's ready to send you, bro."),
        };

        internal static readonly Line PhoneMore = L("还有别的吗？", "What else have you got?");

        internal static readonly Line[] PhoneNextPage =
        {
            L("还有这些。", "There's also these."),
            L("还有这些。", "There's also these."),
            L("后面还有这些。", "There's more after these."),
            L("往后翻还有这些呢，哥。", "Turn the page, there's more, bro."),
        };

        internal static readonly Line[] PhoneLastPage =
        {
            L("这一类就这些。还要看别的吗？", "That's all of that kind. Want to see anything else?"),
            L("这一类就这些。还要看别的吗？", "That's all of that kind. Want to see anything else?"),
            L("这一类没别的了。还要看其他类吗？", "Nothing else of that kind. Want to see other kinds?"),
            L("这一类就这么多啦，哥，还要看别的吗？", "That's all of that kind, bro. Want to see anything else?"),
        };

        internal static readonly Line PhoneItemChoice = L("{0}，{1} 信用点。", "{0}, {1} credits.");

        internal static readonly Line[] PhoneOrdered =
        {
            L("款收到了，两天后送到你店里。", "Payment received. It'll be at your shop in two days."),
            L("收妥，给你挑品相好的封箱，后天准到。", "Received. I'll pick a good one and seal the box; it'll be there the day after tomorrow."),
            L("款收到了，后天给你捎过去。", "Payment received. I'll bring it over the day after tomorrow."),
            L("收到款啦，哥！我给你挑品相好的封箱，后天见！", "Got the payment, bro! I'll pick you a good one and seal the box. See you the day after tomorrow!"),
        };

        internal static readonly Line[] PhoneNoMoney =
        {
            L("账上信用点不够，凑齐了再打来。", "You don't have enough credits. Call back when you do."),
            L("账上信用点不够，凑齐了再打来。", "You don't have enough credits. Call back when you do."),
            L("你账上信用点不够啊，攒够了再打来吧。", "You don't have enough credits. Call back once you've saved up."),
            L("哎呀哥，你账上信用点差一点，周转开了再打给我吧！", "Oh, bro, you're a little short on credits. Call me once things free up!"),
        };

        internal static readonly Line PhoneBye = L("先这样。", "That's all.");

        internal static readonly Line[] PhoneHangUp =
        {
            L("行，有需要再打。", "Fine. Call again if you need anything."),
            L("回见，老搭档。", "See you, partner."),
            L("行，挂了啊。", "Okay, hanging up."),
            L("好嘞哥，那先挂啦，回见！", "Alright, bro, hanging up. See you!"),
        };

        // Section 7: the retired gunsmith, his number and the blind boxes on 4867.
        internal static readonly Line RetiredNumber = L("对了，我退下来以前攒了不少改件、旧枪和修枪零碎，搁家里也是落灰。我的号码是 4867，你要是缺东西使，打电话给我，老头子按箱打包卖给你。",
            "By the way, before I retired I piled up plenty of parts, old guns and repair odds and ends, and they're just gathering dust at home. My number is 4867. If you're short of something, give me a call and the old man will sell it to you by the box.");
        internal static readonly Line RetiredOpen = L("又怎么了，小子？", "What is it now, kid?");
        internal static readonly Line RetiredOpenWeekDone = L("又怎么了，小子？这周已经给你翻了三箱了，我这把老骨头折腾不动，下周再打来吧。",
            "What is it now, kid? I've already dug out three boxes for you this week. These old bones can't take any more. Call next week.");
        internal static readonly Line RetiredWeekDoneReply = L("行，您歇着，下周再说。", "Alright, take it easy. Next week, then.");
        internal static readonly Line RetiredHangUpShort = L("挂了。", "Hanging up.");
        internal static readonly Line RetiredAskSpare = L("你这里还有没有不用的东西。", "Got anything you're not using?");
        internal static readonly Line RetiredSpareReply = L("翻翻倒是有不少压箱底的旧货。你想淘换配件，还是整把的家伙？",
            "I could dig up plenty of old stuff from the bottom of my boxes. Are you after parts, or whole guns?");
        internal static readonly Line RetiredParts = L("配件。", "Parts.");
        internal static readonly Line RetiredPartsQuote = L("抽屉里还有一堆杂七杂八的改件，好坏都有，四个一箱打包 {0} 信用点卖给你，要吗？",
            "There's a drawer full of odds and ends, good and bad. Four to a box, {0} credits for the lot. Want it?");
        internal static readonly Line RetiredPartsYes = L("要了，钱给你打过去。", "I'll take it. Sending the money now.");
        internal static readonly Line RetiredPartsDone = L("行，给你封好箱，两天后捎过去。", "Fine. I'll seal a box and bring it over in two days.");
        internal static readonly Line RetiredPartsNoMoney = L("你小子兜里钱都不够，凑齐了再打来。", "You don't even have the money, kid. Call back when you do.");
        internal static readonly Line RetiredPartsNo = L("算了，我再想想。", "Never mind, let me think about it.");
        internal static readonly Line RetiredPartsNoReply = L("磨磨蹭蹭的。还有别的事吗？", "Dawdling, as always. Anything else?");
        internal static readonly Line RetiredGuns = L("枪械。", "Guns.");
        internal static readonly Line RetiredGunsNoPermit = L("你小子连张武器许可证都没办下来，整枪我可不敢塞给你，先去把证办了再来。",
            "You haven't even got a weapon permit, kid. I won't slip you a whole gun. Go and get your permit first.");
        internal static readonly Line RetiredGunsNoPermitBack = L("好吧，那看看别的。", "Alright, something else then.");
        internal static readonly Line RetiredGunsAsk = L("按你现在的证，我柜子里能匀给你的旧枪有这几类，具体摸到哪把、什么成色全看运气，挑一类吧。",
            "With the permit you have, these are the kinds of old guns I can spare from my cabinet. Which one you get and in what condition is down to luck. Pick a kind.");
        internal static readonly Line RetiredGunsChoice = L("{0}盲盒，{1} 信用点。", "{0} blind box, {1} credits.");
        internal static readonly Line RetiredGunsMore = L("还有别的类别吗？", "Any other kinds?");
        internal static readonly Line RetiredGunsNo = L("先不买枪了。", "No guns for now.");
        internal static readonly Line RetiredGunsDone = L("行，给你从{0}堆里摸一把装箱，两天后送到。", "Fine. I'll fish one out of the {0} pile and box it. It'll be there in two days.");
        internal static readonly Line RetiredGunsNoMoney = L("账上钱不够啊小子，攒够了再来淘换。", "Not enough money, kid. Save up and come back.");
        internal static readonly Line RetiredBack = L("我再看看别的。", "Let me look at something else.");
        internal static readonly Line RetiredAskMaterials = L("我修枪缺点材料。", "I'm short of repair materials.");
        internal static readonly Line RetiredMaterialsQuote = L("工作台底下还剩些螺丝、零件、酒精和擦枪料，六个一箱打包 {0} 信用点，给你匀一箱？",
            "There are still some screws, parts, alcohol and cleaning stuff under the workbench. Six to a box, {0} credits. Want one?");
        internal static readonly Line RetiredMaterialsYes = L("行，给我来一箱。", "Sure, one box.");
        internal static readonly Line RetiredMaterialsDone = L("收到了，装好箱两天后给你送过去。", "Got it. I'll pack the box and send it over in two days.");
        internal static readonly Line RetiredMaterialsNoMoney = L("这么点材料钱都差？先去挣两单再说吧。", "You can't even cover a few materials? Go and earn some first.");
        internal static readonly Line RetiredMaterialsNo = L("先不用了。", "Not now.");
        internal static readonly Line RetiredMaterialsNoReply = L("那还有别的事吗？", "Anything else, then?");
        internal static readonly Line RetiredBye = L("没事了，回头再聊。", "Nothing else. Talk later.");
        internal static readonly Line RetiredHangUp = L("少惹麻烦，挂了。", "Stay out of trouble. Hanging up.");

        // 7-2: the courier who brings the boxes.
        internal static readonly Line BoxDelivered = L("你在电话里跟老爷子淘换的箱子，给你送来了，自己拆封吧。",
            "Here's the box you got from the old man on the phone. Open it yourself.");
        internal static readonly Line BoxDeliveredElsewhere = L("你在电话里跟老爷子淘换的箱子送到了。前柜台没空位，给你放到 {0} 了。",
            "The box you got from the old man on the phone is here. There was no room on the front counter, so it's in {0}.");
        internal static readonly Line BoxNoRoom = L("箱子到了，可你店里哪儿都塞不下。你腾个地方，我等着。",
            "The box is here, but there's nowhere to put it in your shop. Clear a space; I'll wait.");
        internal static readonly Line BoxBye = L("回头见。", "See you.");
    }
}
