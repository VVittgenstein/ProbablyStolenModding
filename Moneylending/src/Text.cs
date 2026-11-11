using System;
using System.Collections.Generic;
using System.Globalization;

namespace Moneylending
{
    // What one kind of borrower says. Each entry is { Chinese, English, Russian }; the arguments are listed per entry.
    internal sealed class Lines
    {
        internal string[][] Situation;   // per request variant
        internal string[][] Ask;         // per request variant: {0} amount, {1} days, {2} total
        internal string[] AskFree;       // a request without interest: {0} amount, {1} days
        internal string[] Thanks;
        internal string[] Refused;
        internal string[] RaiseYes;
        internal string[] RaiseNo;
        internal string[] Gentle;        // reply to a loan without interest
        internal string[] OnTime;        // {0} amount handed over
        internal string[] Late;          // {0} amount handed over
        internal string[] Partial;       // {0} handed over now, {1} the rest, {2} days until the rest
        internal string[] Goods;         // the goods are on the counter
        internal string[] Bonus;         // {0} amount handed over, the extra included
        internal string[] DefaultLog;    // {0} name, {1} amount still owed
        internal string[] ArrestLog;     // {0} name, {1} amount still owed
        internal string[] Decline;       // answer to a loan offered at the counter when the customer does not want one
    }

    // Texts shown in the game: Chinese when the game language is Chinese, Russian when it is Russian, English otherwise.
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

        internal static string N(long value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        internal static string F(string[] texts, params object[] args)
        {
            if (texts == null)
                return "";
            string format = Chinese() ? texts[0] : Russian() && texts.Length > 2 ? texts[2] : texts[1];
            return args == null || args.Length == 0 ? format : string.Format(CultureInfo.InvariantCulture, format, args);
        }

        private static string T(string zh, string en, string ru)
        {
            return Chinese() ? zh : Russian() ? ru : en;
        }

        // ---- Borrowers -------------------------------------------------------------------------------------

        internal static readonly Dictionary<string, Lines> Borrowers = new Dictionary<string, Lines>
        {
            {
                Profiles.Scav, new Lines
                {
                    Situation = new[]
                    {
                        new[] { "废弃区外围早被搜刮干净了。我想往深处走，可得先置办一盏像样的灯和几个滤芯。", "Anything near the edge of the Abandoned Sections is picked clean. I want to go deeper, but I need a proper lamp and filters first.", "С окраин Заброшенных секций всё уже растащили. Хочу забраться поглубже, но сначала нужен нормальный фонарь и пара фильтров." },
                        new[] { "我的撬棍昨天断了。没有工具，什么也捡不回来。", "My crowbar snapped yesterday. No tools, no haul.", "Вчера сломался лом. Без инструментов никакой добычи." },
                        new[] { "有个隔间一直封着，还没人进去过。我得买点切割用的燃气才进得去。", "There's a sealed section nobody has opened yet. I need cutting gas to get in.", "Есть запечатанная секция, её ещё никто не вскрывал. Чтобы попасть внутрь, нужен газ для резака." },
                    },
                    Ask = new[]
                    {
                        new[] { "借我{0}信用点行吗？{1}天后还你{2}。", "Lend me {0} Credits? In {1} days I'll pay you back {2}.", "Одолжишь {0} Кр.? Через {1} дн. верну {2} Кр." },
                        new[] { "有{0}信用点我就能重新开工。{1}天后我带{2}回来还你。", "{0} Credits would get me back to work. I'll bring you {2} in {1} days.", "С {0} Кр. я снова смогу работать. Через {1} дн. принесу тебе {2} Кр." },
                        new[] { "借我{0}信用点。{1}天后还你{2}，拿我这趟的收获担保。", "Lend me {0} Credits. In {1} days you get {2}, I swear on my haul.", "Одолжи {0} Кр. Через {1} дн. получишь {2} Кр., клянусь своей добычей." },
                    },
                    Thanks = new[] { "你不会后悔的。等我回来。", "You won't regret it. I'll be back.", "Не пожалеешь. Жди обратно." },
                    Refused = new[] { "行吧。那我就带着手头这点家伙下去。", "Fine. I'll go down with what I've got.", "Ладно. Пойду вниз с тем, что есть." },
                    RaiseYes = new[] { "这也太狠了……行吧，我也没别的办法。", "That's steep... Fine, I've got no other choice.", "Ну и аппетиты... Ладно, выбора у меня нет." },
                    RaiseNo = new[] { "这个利息，我还不如死在下面。算了吧。", "At that rate I'd rather not come back up. Forget it.", "С такими процентами проще там внизу и сгинуть. Забудь." },
                    Gentle = new[] { "不要利息？……行，这份人情我记下了。", "No interest? ...All right. I owe you one.", "Без процентов? ...Ладно. Я твой должник." },
                    OnTime = new[] { "我活着回来了。这是你的{0}信用点。", "Made it back in one piece. Here's your {0} Credits.", "Вернуться удалось без единой царапины. Вот твои {0} Кр." },
                    Late = new[] { "抱歉，这趟比我想的久。这是欠你的{0}信用点。", "Sorry, the run took longer than I thought. Here's the {0} Credits I owe you.", "Извини, вылазка затянулась. Вот мой долг: {0} Кр." },
                    Goods = new[] { "这趟收获不错，可没人肯给拾荒客付现钱。用这些东西抵债行吗？", "The haul was good, but nobody pays scavs in cash. Will these do instead?", "Добыча хорошая, но помоечникам наличными никто не платит. Возьмёшь вот этим?" },
                    DefaultLog = new[] { "{0}没有从废弃区回来。{1}信用点仍未还上。", "{0} has not come back from the Abandoned Sections. {1} Credits are still unpaid.", "{0} не возвращается из Заброшенных секций. Долг {1} Кр. не погашен." },
                    Decline = new[] { "借钱？在这下面欠人钱？不了。", "Borrow? And owe someone down here? No thanks.", "Занять? И ходить в должниках здесь, внизу? Нет уж." },
                }
            },
            {
                Profiles.Caretaker, new Lines
                {
                    Situation = new[]
                    {
                        new[] { "我的孩子已经烧了三天。诊所要先付钱才肯给药。", "My child has had a fever for three days. The clinic wants Credits up front for the medicine.", "У моего ребёнка третий день жар. В клинике требуют деньги вперёд, иначе лекарства не дадут." },
                        new[] { "家里的滤水器坏了，我们不能一直买瓶装水。", "The water filter at home broke, and we can't keep buying bottled water.", "Дома сломался фильтр для воды, а покупать воду в бутылках нам не по карману." },
                        new[] { "房租又涨了，孩子们还得吃饭。", "Our rent went up again, and the kids still need to eat.", "Аренда опять выросла, а детей всё равно надо кормить." },
                    },
                    Ask = new[]
                    {
                        new[] { "能借我{0}信用点吗？{1}天后发工钱，我还你{2}。", "Could you lend me {0} Credits? I get paid in {1} days, and I'll return {2}.", "Не одолжите {0} Кр.? Через {1} дн. мне заплатят, и я верну {2} Кр." },
                        new[] { "有{0}信用点就能修好。{1}天后我能还你{2}。", "{0} Credits would fix it. I can pay back {2} in {1} days.", "{0} Кр. хватит на ремонт. Через {1} дн. я смогу вернуть {2} Кр." },
                        new[] { "只要{0}信用点，让我们撑过这几天。{1}天后还你{2}。", "Just {0} Credits to get us through. {2} back in {1} days.", "Всего {0} Кр., чтобы продержаться эти дни. Через {1} дн. верну {2} Кр." },
                    },
                    Thanks = new[] { "谢谢你。我一定会还的。", "Thank you. I'll pay you back, I promise.", "Спасибо вам. Я обязательно верну, обещаю." },
                    Refused = new[] { "我明白。大家日子都不好过。", "I understand. Everyone's struggling.", "Понимаю. Всем сейчас тяжело." },
                    RaiseYes = new[] { "……好吧。孩子等不了。", "...All right. My child can't wait.", "...Хорошо. Ребёнок ждать не может." },
                    RaiseNo = new[] { "我还不起这么多。我再去问问别人。", "I could never pay that back. I'll ask someone else.", "Столько мне никогда не вернуть. Спрошу у кого-нибудь ещё." },
                    Gentle = new[] { "不要利息？我……谢谢你。我不会忘记的。", "Without interest? I... Thank you. I won't forget this.", "Без процентов? Я... Спасибо вам. Я этого не забуду." },
                    OnTime = new[] { "这是{0}信用点。多亏了你，家里总算缓过来了。", "Here are the {0} Credits. Thanks to you, we're back on our feet.", "Вот {0} Кр. Благодаря вам мы снова на ногах." },
                    Late = new[] { "抱歉拖了这么久，工头一直扣着我们的工钱。这是{0}信用点。", "I'm sorry it took so long. The shift boss held our pay. Here are the {0} Credits.", "Простите, что так долго: бригадир задерживал нам зарплату. Вот {0} Кр." },
                    Partial = new[] { "我现在只有{0}信用点。剩下的{1}，{2}天后带来。", "I only have {0} Credits right now. I'll bring the other {1} in {2} days.", "Сейчас у меня есть только {0} Кр. Остальные {1} Кр. принесу через {2} дн." },
                    DefaultLog = new[] { "{0}一家搬离了这个区。{1}信用点仍未还上。", "{0}'s family has moved out of the sector. {1} Credits are still unpaid.", "Семья заёмщика {0} переехала из этого сектора. Долг {1} Кр. не погашен." },
                    Decline = new[] { "谢谢你，不过我们暂时还撑得住。", "That's kind, but we're managing for now.", "Спасибо за заботу, но пока мы справляемся." },
                }
            },
            {
                Profiles.Worker, new Lines
                {
                    Situation = new[]
                    {
                        new[] { "合作社周日才发工钱，可我的防尘面罩撑不到那天了。", "The co-op pays us on Sunday, but my dust mask won't last until then.", "Кооператив платит по воскресеньям, а мой респиратор до воскресенья не дотянет." },
                        new[] { "一班干十二个小时，我的靴子都快磨穿了。", "Twelve-hour shifts, and my boots are falling apart.", "Смены по двенадцать часов, ботинки совсем развалились." },
                        new[] { "工头说钻头坏了要扣我工钱，根本不是我弄坏的。", "The foreman docked my pay for a broken drill. It wasn't even my fault.", "Бригадир удержал у меня из зарплаты за сломанный бур, а я тут вообще ни при чём." },
                    },
                    Ask = new[]
                    {
                        new[] { "借我{0}信用点到发薪日？周日还你{2}。", "Lend me {0} Credits until payday? I'll pay back {2} on Sunday.", "Одолжишь {0} Кр. до получки? В воскресенье верну {2} Кр." },
                        new[] { "借{0}信用点买双新靴子。周日发工钱就还你{2}。", "{0} Credits for new boots. {2} back on payday, Sunday.", "{0} Кр. на новые ботинки. {2} Кр. верну в воскресенье, в день получки." },
                        new[] { "能借我{0}信用点吗？周日我带{2}来还。", "Can you spare {0} Credits? On Sunday I'll bring {2}.", "Можешь одолжить {0} Кр.? В воскресенье принесу {2} Кр." },
                    },
                    Thanks = new[] { "谢了。周日见。", "Thanks. See you on Sunday.", "Спасибо. До воскресенья." },
                    Refused = new[] { "也行。我再拿胶带缠一缠。", "Fair enough. I'll tape them up again.", "Ну ладно. Обмотаю их ещё раз изолентой." },
                    RaiseYes = new[] { "这都够一个班的工钱了……成交。", "That's a whole shift's pay... Deal.", "Это же плата за целую смену... Договорились." },
                    RaiseNo = new[] { "不行。我可不想给你白干一个班。", "No way. I'm not working a shift for you.", "Нет уж. Отрабатывать на тебя целую смену я не собираюсь." },
                    Gentle = new[] { "不要利息？矿上的兄弟们会知道你是什么样的人。", "No interest? The crew will hear what kind of person you are.", "Без процентов? Ребята на шахте узнают, что вы за человек." },
                    OnTime = new[] { "发工钱了。这是你的{0}信用点。", "Payday. Here are your {0} Credits.", "Получка пришла. Вот твои {0} Кр." },
                    Late = new[] { "合作社又晚发了工钱，抱歉。这是{0}信用点。", "The co-op paid late again. Sorry. Here are the {0} Credits.", "Кооператив опять задержал зарплату, извини. Вот {0} Кр." },
                    Partial = new[] { "这周只发了一半工钱。先给你{0}信用点，剩下的{1}，{2}天后给。", "They only paid us half this week. Here are {0} Credits; the other {1} in {2} days.", "На этой неделе заплатили только половину. Вот {0} Кр., остальные {1} Кр. через {2} дн." },
                    DefaultLog = new[] { "{0}在小行星带出了事故，没法再干活。{1}信用点仍未还上。", "{0} was injured in an accident in the asteroid belt and can't work. {1} Credits are still unpaid.", "После несчастного случая в поясе астероидов заёмщик {0} не может работать. Долг {1} Кр. не погашен." },
                    Decline = new[] { "快发工钱了，我没事。", "Payday's close. I'm fine.", "Скоро получка, справлюсь." },
                }
            },
            {
                Profiles.Merchant, new Lines
                {
                    Situation = new[]
                    {
                        new[] { "你好啊，同行。明天有艘补给船靠港，我一个人吃不下整批货。", "Hello, fellow merchant. A supply ship docks tomorrow, and I can't cover the whole lot.", "Здравствуйте, коллега. Завтра причаливает корабль снабжения, а выкупить всю партию в одиночку мне не по силам." },
                        new[] { "有个买家拖着货款，我的供货商却不肯等。", "One of my buyers is late paying me, and my own supplier won't wait.", "Один покупатель задерживает оплату, а мой поставщик ждать не хочет." },
                        new[] { "我接了上层区的一笔订单，就差本钱。", "I have an order from the Upper Levels and only lack the capital.", "У меня заказ с Верхних уровней, не хватает только оборотных средств." },
                    },
                    Ask = new[]
                    {
                        new[] { "借我{0}信用点，{1}天后货卖出去，还你{2}。", "Lend me {0} Credits, and in {1} days, when the goods are sold, I'll repay {2}.", "Одолжите {0} Кр., и через {1} дн., когда товар будет продан, я верну {2} Кр." },
                        new[] { "借{0}信用点帮我周转一下，{1}天后还你{2}。", "{0} Credits would bridge the gap. {2} back in {1} days.", "{0} Кр. помогут закрыть дыру. Через {1} дн. верну {2} Кр." },
                        new[] { "借我{0}信用点。{1}天后你拿到{2}，我拿回我的东西。", "Lend me {0} Credits. In {1} days you get {2}, and I get my goods back.", "Одолжите {0} Кр. Через {1} дн. вы получите {2} Кр., а я свой товар." },
                    },
                    Thanks = new[] { "合作愉快。", "A pleasure doing business.", "Приятно иметь с вами дело." },
                    Refused = new[] { "可惜。下次吧。", "Pity. Another time, perhaps.", "Жаль. Может, в другой раз." },
                    RaiseYes = new[] { "你可真会做生意。成交。", "You drive a hard bargain. Agreed.", "Умеете вы торговаться. По рукам." },
                    RaiseNo = new[] { "那我去找别人谈。", "Then I'll take my business elsewhere.", "Тогда поищу другого партнёра." },
                    Gentle = new[] { "不要利息？做生意的人很少这样。我记住了。", "No interest? Few in business would do that. I won't forget it.", "Без процентов? В нашем деле так мало кто поступает. Я этого не забуду." },
                    OnTime = new[] { "说好的，{0}信用点。", "As agreed: {0} Credits.", "Как договаривались: {0} Кр." },
                    Late = new[] { "抱歉来晚了。{0}信用点，说好的数。", "My apologies for the delay. {0} Credits, as agreed.", "Прошу прощения за задержку. {0} Кр., как договаривались." },
                    Partial = new[] { "还有一半货压在仓库里。先给你{0}信用点，剩下的{1}，{2}天后给。", "Half the lot is still in the warehouse. Here are {0} Credits; the remaining {1} in {2} days.", "Половина партии ещё лежит на складе. Вот {0} Кр., остальные {1} Кр. через {2} дн." },
                    DefaultLog = new[] { "{0}的货在港口被扣了。{1}信用点仍未还上。", "{0}'s shipment was seized at the docks. {1} Credits are still unpaid.", "Груз заёмщика {0} арестован в порту. Долг {1} Кр. не погашен." },
                    Decline = new[] { "我的账目没问题，谢谢。", "My books are in order, thank you.", "С моими счетами всё в порядке, спасибо." },
                }
            },
            {
                Profiles.UpperBroke, new Lines
                {
                    Situation = new[]
                    {
                        new[] { "我在上面身家尽丧啊。一个强敌、一步错棋，满盘皆输。", "Lost everything up top. One rival, one bad deal, and poof.", "Наверху всё потеряно. Один соперник, одна неудачная сделка, и всё." },
                        new[] { "上面有个讨债的在找我。我得在他找到我家人之前把钱还上。", "A debt collector from up top is looking for me. I have to pay him before he finds my family.", "Меня ищет коллектор сверху. Надо расплатиться, пока он не добрался до моей семьи." },
                        new[] { "只要做成一笔好买卖，我就能翻身。我只差本钱。", "One good deal and I'm back on my feet. I just need the stake.", "Одна удачная сделка, и я снова на ногах. Не хватает только начального капитала." },
                    },
                    Ask = new[]
                    {
                        new[] { "我的合伙人{1}天内会把我那份结给我。在那之前借我{0}信用点，到时还你{2}。", "My partners will pay out my share within {1} days. Until then, lend me {0} Credits and I'll repay {2}.", "Мои партнёры выплатят мою долю в течение {1} дн. А пока одолжите мне {0} Кр., и я верну {2} Кр." },
                        new[] { "借{0}信用点，{1}天后还你{2}。", "{0} Credits. {2} back in {1} days.", "{0} Кр. Через {1} дн. верну {2} Кр." },
                        new[] { "借我{0}信用点，{1}天后你拿到{2}。", "Lend me {0} Credits; in {1} days you get {2}.", "Одолжите {0} Кр., через {1} дн. получите {2} Кр." },
                    },
                    Thanks = new[] { "你救了我。我会记得是谁帮了我。", "You've saved me. I'll remember who helped.", "Вы меня спасли. Я запомню, кто помог." },
                    Refused = new[] { "也是。谁会帮一个落魄的人呢。", "Of course. Why would anyone help a ruined man?", "Ну конечно. Кто станет помогать тому, кто всё потерял?" },
                    RaiseYes = new[] { "简直是抢劫。可我现在也没资格讨价还价。", "Robbery. But I'm hardly in a position to argue.", "Грабёж. Но спорить мне сейчас не с руки." },
                    RaiseNo = new[] { "我是破产了，还没到走投无路的地步。", "I'm broke, not desperate.", "Денег нет, но не до такой же степени." },
                    OnTime = new[] { "说到做到：{0}信用点。", "As promised: {0} Credits.", "Как обещано: {0} Кр." },
                    Late = new[] { "抱歉来晚了，我的合伙人办事太慢。{0}信用点，说好的数。", "Forgive the delay; my partners are slow. {0} Credits, as promised.", "Простите за задержку, мои партнёры не торопятся. {0} Кр., как обещано." },
                    Bonus = new[] { "上面的事情有转机了。{0}信用点，里面多出来的算是谢礼。", "Things turned around up top. {0} Credits; the extra is my thanks.", "Наверху дела пошли на лад. Вот {0} Кр., сверх долга моя благодарность." },
                    DefaultLog = new[] { "{0}为了躲债离开了空间站。{1}信用点仍未还上。", "{0} has left the station to escape their creditors. {1} Credits are still unpaid.", "Скрываясь от кредиторов, заёмщик {0} покинул станцию. Долг {1} Кр. не погашен." },
                    Decline = new[] { "我不想再欠任何人了。", "I'd rather not owe anyone again.", "Не хочу больше ходить в должниках." },
                }
            },
            {
                Profiles.Thief, new Lines
                {
                    Situation = new[]
                    {
                        new[] { "我在上层区接了个活儿，得先置办点家伙。", "I've got a job lined up in the Upper Levels. Need gear first.", "Наклёвывается дело на Верхних уровнях. Сначала нужно снаряжение." },
                        new[] { "最近风声太紧，我得躲几天。", "There's too much heat on me. I need to lie low for a few days.", "Слишком много на меня сейчас внимания. Надо залечь на дно на пару дней." },
                        new[] { "有个销赃的欠我钱，最近却没了动静。", "A fence owes me, but he's gone quiet.", "Скупщик краденого должен мне денег, а сам куда-то пропал." },
                    },
                    Ask = new[]
                    {
                        new[] { "借我{0}信用点，{1}天后还你{2}。别多问。", "Lend me {0} Credits. In {1} days you get {2}. Don't ask questions.", "Одолжи {0} Кр. Через {1} дн. получишь {2} Кр. Лишних вопросов не задавай." },
                        new[] { "借{0}信用点让我避避风头，{1}天后还你{2}。", "{0} Credits keeps me off the street. {2} back in {1} days.", "{0} Кр., чтобы не светиться на улицах. Через {1} дн. верну {2} Кр." },
                        new[] { "先借我{0}信用点。{1}天后，不管怎样你都会拿到{2}。", "Front me {0} Credits. You'll get {2} in {1} days, one way or another.", "Дай вперёд {0} Кр. Через {1} дн. так или иначе получишь {2} Кр." },
                    },
                    Thanks = new[] { "聪明。你那份少不了。", "Smart. You'll get your cut.", "Умно. Своё получишь." },
                    Refused = new[] { "那是你的损失。", "Your loss.", "Тебе же хуже." },
                    RaiseYes = new[] { "够贪。我喜欢。成交。", "Greedy. I like that. Deal.", "Жадность. Это мне нравится. Договорились." },
                    RaiseNo = new[] { "连高利贷都比你便宜。", "Even the Loan Shark is cheaper than you.", "Даже нелегальный ростовщик берёт меньше." },
                    OnTime = new[] { "活儿干得很顺。这是你的{0}信用点。", "The job went fine. Here are your {0} Credits.", "Дело прошло гладко. Вот твои {0} Кр." },
                    Late = new[] { "前几天得躲一躲。这是{0}信用点。", "Had to lie low for a while. Here are the {0} Credits.", "Пришлось на время залечь на дно. Вот {0} Кр." },
                    Goods = new[] { "现钱最近不好出手。拿这些抵吧，只多不少。别让人看见。", "Cash is hard to move right now. Take these instead; they're worth more. Don't let anyone see them.", "С наличными сейчас туго. Возьми лучше вот это, оно стоит больше. И никому не показывай." },
                    DefaultLog = new[] { "{0}在黑市上销声匿迹了。{1}信用点仍未还上。", "{0} has vanished from the Black Market. {1} Credits are still unpaid.", "Заёмщик {0} бесследно исчез с Чёрного рынка. Долг {1} Кр. не погашен." },
                    ArrestLog = new[] { "{0}在上层区作案时被治安部抓了。{1}信用点收不回来了。", "Security arrested {0} during a break-in in the Upper Levels. The {1} Credits will not be repaid.", "Служба безопасности арестовала заёмщика {0} при краже со взломом на Верхних уровнях. Долг {1} Кр. уже не вернуть." },
                    Decline = new[] { "我从不欠别人。都是别人欠我。", "I don't owe people. People owe me.", "В долг я не беру. Это мне все должны." },
                }
            },
            {
                Profiles.Rev, new Lines
                {
                    Situation = new[]
                    {
                        new[] { "我们小组需要医疗物资，下层的同志们受了伤。", "Our cell needs medical supplies. Comrades in the lower sectors are hurt.", "Нашей ячейке нужны медикаменты: товарищи в нижних секторах ранены." },
                        new[] { "上层区又抬高了水价。我们在组织一条补给线。", "The Upper Levels raised the water price again. We're organizing a supply line.", "Верхние уровни снова подняли цену на воду. Мы налаживаем канал снабжения." },
                        new[] { "我们有几个人被抓了，他们的家人需要钱。", "Some of our people were arrested. Their families need Credits.", "Нескольких наших арестовали. Их семьям нужны деньги." },
                    },
                    Ask = new[]
                    {
                        new[] { "借我们{0}信用点。{1}天后还你{2}。革命军说话算话。", "Lend us {0} Credits. In {1} days we repay {2}. The Revolution keeps its word.", "Одолжи нам {0} Кр. Через {1} дн. вернём {2} Кр. Революция держит слово." },
                        new[] { "现在借{0}信用点，{1}天后还你{2}。", "{0} Credits now, {2} back in {1} days.", "{0} Кр. сейчас, а через {1} дн. вернём {2} Кр." },
                        new[] { "借{0}信用点，{1}天后还你{2}。", "{0} Credits. We'll repay {2} in {1} days.", "{0} Кр. Через {1} дн. вернём {2} Кр." },
                    },
                    AskFree = new[] { "借我们{0}信用点，{1}天后如数奉还。我们付不起利息，可革命军说话算话。", "Lend us {0} Credits, and in {1} days we pay it all back. We can't pay interest, but the Revolution keeps its word.", "Одолжи нам {0} Кр., и через {1} дн. мы вернём всё до последнего кредита. Платить проценты нам нечем, но Революция держит слово." },
                    Thanks = new[] { "你帮的忙比你想的要大。", "You're helping more than you know.", "Твоя помощь значит больше, чем тебе кажется." },
                    Refused = new[] { "我们会另想办法的，一直都是这样。", "We'll find another way. We always do.", "Найдём другой способ. Мы всегда находим." },
                    RaiseYes = new[] { "从斗争里赚钱……算了，我们需要这笔钱。", "Profiting from the struggle... Fine. We need it.", "Наживаться на борьбе... Ладно. Деньги нам нужны." },
                    RaiseNo = new[] { "你说话跟上层区那帮人一个样。", "You sound like the Upper Levels.", "Говоришь прямо как Верхние уровни." },
                    Gentle = new[] { "革命会记住你的，同志。", "The Revolution will remember this, comrade.", "Революция этого не забудет, товарищ." },
                    OnTime = new[] { "革命军说话算话。{0}信用点。", "The Revolution keeps its word. {0} Credits.", "Революция держит слово. {0} Кр." },
                    Late = new[] { "一次突袭出了岔子，我们只好四散躲开。这是{0}信用点。", "A raid went wrong and we had to scatter. Here are the {0} Credits.", "Налёт пошёл не по плану, пришлось разбежаться. Вот {0} Кр." },
                    Goods = new[] { "这些是从上层区仓库里解放出来的，应该够抵债了。", "We liberated these from an Upper Levels warehouse. They should cover it.", "Это мы освободили со склада на Верхних уровнях. Должно покрыть долг." },
                    DefaultLog = new[] { "治安部突袭了{0}所在的小组，之后再没人见过他们。{1}信用点收不回来了。", "Security raided {0}'s cell. No one from it has been seen since. The {1} Credits will not be repaid.", "Служба безопасности накрыла ячейку заёмщика {0}. С тех пор никого из неё не видели. Долг {1} Кр. уже не вернуть." },
                    Decline = new[] { "我们小组还撑得住，留给更需要的人吧。", "The cell is managing. Keep it for someone who needs it.", "Ячейка справляется. Прибереги для тех, кому нужнее." },
                }
            },
            {
                Profiles.Tourist, new Lines
                {
                    Situation = new[]
                    {
                        new[] { "我的卡在这下面用不了，兑换处又关门了。", "My card doesn't work down here, and the exchange office is closed.", "Моя карта здесь, внизу, не работает, а обменный пункт закрыт." },
                        new[] { "我的钱包在电梯里被人偷了。所有人都叫我别下来。", "Someone took my wallet in the lift. Everyone said not to come down here.", "В лифте у меня стащили кошелёк. Все говорили, что сюда спускаться не стоит." },
                        new[] { "我看中了一件特别地道的纪念品，可是对方只收现钱。", "I found the most authentic souvenir, but they only take cash.", "Тут продают самый настоящий сувенир, но только за наличные." },
                    },
                    Ask = new[]
                    {
                        new[] { "能借我{0}信用点吗？我的穿梭机{1}天后才走，走之前还你{2}。", "Could you lend me {0} Credits? My shuttle leaves in {1} days; I'll repay {2} before then.", "Не одолжите {0} Кр.? Мой шаттл улетает через {1} дн., до отлёта верну {2} Кр." },
                        new[] { "只要{0}信用点让我撑一撑。{1}天后还你{2}，我保证。", "Just {0} Credits to get by. {2} back in {1} days, I promise.", "Всего {0} Кр., чтобы продержаться. Через {1} дн. верну {2} Кр., обещаю." },
                        new[] { "能借我{0}信用点吗？{1}天后还你{2}。", "Could you lend me {0} Credits? I'll pay back {2} in {1} days.", "Не одолжите {0} Кр.? Через {1} дн. верну {2} Кр." },
                    },
                    Thanks = new[] { "你真是救星！这地方果然原汁原味。", "You're a lifesaver! This place really is authentic.", "Вы меня просто спасли! Здесь и правда всё по-настоящему." },
                    Refused = new[] { "哦，好吧。我再想想办法。", "Oh. Okay. I'll figure something out.", "А. Ладно. Что-нибудь придумаю." },
                    RaiseYes = new[] { "这下面都是这样的吗？好吧……行。", "Is that normal down here? Well... Okay.", "Здесь, внизу, так принято? Ну... хорошо." },
                    RaiseNo = new[] { "这也太多了。不用了，谢谢。", "That's way too much. No thanks.", "Это слишком много. Нет, спасибо." },
                    OnTime = new[] { "这是你的{0}信用点。这趟旅行太棒了！", "Here are your {0} Credits. Best trip ever!", "Вот ваши {0} Кр. Лучшая поездка в жизни!" },
                    Bonus = new[] { "这是{0}信用点，多出来的是小费。太值了！", "Here are {0} Credits; the extra is a tip. Totally worth it!", "Вот {0} Кр., сверх долга чаевые. Оно того стоило!" },
                    DefaultLog = new[] { "{0}乘坐的穿梭机已经离开了空间站。{1}信用点收不回来了。", "{0}'s shuttle has left the station. The {1} Credits will not be repaid.", "Шаттл заёмщика {0} уже улетел со станции. Долг {1} Кр. уже не вернуть." },
                    Decline = new[] { "哦，我不缺钱，不过谢谢！", "Oh, I'm fine, but thanks!", "О, у меня всё в порядке, но спасибо!" },
                }
            },
        };

        // Answers of customers who never borrow, to a loan offered at the counter.
        internal static string DeclineRich() { return T("在这下面借钱？还是算了。", "Borrow money down here? I think not.", "Брать в долг здесь, внизу? Ну уж нет."); }
        internal static string DeclineSecurity() { return T("店主借钱给治安官？说话小心点。", "A shopkeeper lending to an officer? Careful what you offer.", "Владелец магазина даёт в долг сотруднику Службы безопасности? Осторожнее с такими предложениями."); }
        internal static string DeclineOther() { return T("不用了，谢谢。", "No, thank you.", "Нет, спасибо."); }
        internal static string DeclineCash() { return T("你自己的钱都不够借吧。", "You don't have that kind of money to lend.", "У вас самих не хватит денег, чтобы давать в долг."); }

        internal static string AcceptOffer() { return T("其实……确实需要。", "Actually... I do.", "Вообще-то... да, нужен."); }
        internal static string PledgeOffer(string items, int count)
        {
            return string.Format(CultureInfo.InvariantCulture, count > 1
                ? T("这些东西先押在你这里：{0}。", "Keep these as a guarantee: {0}.", "Вот залог: {0}.")
                : T("这件{0}先押在你这里。", "Keep this {0} as a guarantee.", "Вот залог: {0}."), items);
        }

        // Item names for one line: the same name twice is written once with a count.
        internal static string ItemList(List<string> names)
        {
            var parts = new List<string>();
            var seen = new List<string>();
            foreach (string name in names)
            {
                if (seen.Contains(name))
                    continue;
                seen.Add(name);
                int count = 0;
                foreach (string other in names)
                {
                    if (other == name)
                        count++;
                }
                parts.Add(count > 1 ? name + " ×" + count.ToString(CultureInfo.InvariantCulture) : name);
            }
            return string.Join(T("、", ", ", ", "), parts.ToArray());
        }
        internal static string AugLostLog(string name, int owed)
        {
            return string.Format(CultureInfo.InvariantCulture, T("再也没有人见过{0}。{1}信用点收不回来了。", "No one has seen {0} again. The {1} Credits will not be repaid.", "Заёмщика {0} больше никто не видел. Долг {1} Кр. уже не вернуть."), name, owed);
        }

        // ---- Request choices and the player's lines -------------------------------------------------------

        internal static string ChoiceLend(int amount) { return string.Format(CultureInfo.InvariantCulture, T("借给你{0}信用点。", "Here are {0} Credits.", "Вот {0} Кр."), amount); }
        internal static string ChoiceRaise(int rate, int total) { return string.Format(CultureInfo.InvariantCulture, T("利息要{0}%，你得还{1}。", "Only at {0}%: you pay back {1}.", "Только под {0}%: вернуть нужно будет {1} Кр."), rate, total); }
        internal static string ChoiceGentle() { return T("拿去吧，不要利息。", "Take it. No interest.", "Берите, без процентов."); }
        internal static string ChoiceTalkInterest() { return T("利息再谈谈。", "Let's talk about the interest.", "Давайте обсудим проценты."); }
        internal static string TalkInterestReply() { return T("你说。", "Go on.", "Я слушаю."); }
        internal static string ChoiceBackToRequest() { return T("算了，还是刚才说的。", "Never mind. Back to what you said.", "Ладно, вернёмся к вашему предложению."); }
        internal static string WaivedInterest(string faction, double gained)
        {
            return string.Format(CultureInfo.InvariantCulture, T("没有收利息：{0}声望 +{1:0.0}。", " No interest charged: {0} reputation +{1:0.0}.", " Без процентов: репутация ({0}) +{1:0.0}."), faction, gained);
        }
        internal static string ChoiceRefuse() { return T("抱歉，今天不行。", "Sorry, not today.", "Извините, сегодня никак."); }
        internal static string OfferLoan() { return T("需要借点钱吗？", "Do you need a loan?", "Вам нужен заём?"); }

        // ---- IOU note and pledges ----------------------------------------------------------------------------

        internal static string IouTitle() { return T("欠条", "I.O.U.", "Расписка"); }
        internal static string IouBody(string name, int total, int dueDay)
        {
            return string.Format(CultureInfo.InvariantCulture, T("{0}欠店主{1}信用点，第{2}天到期。", "{0} owes the Shopkeeper {1} Credits, due on day {2}.", "Заёмщик {0} должен владельцу магазина {1} Кр. Срок возврата: день {2}."), name, total, dueDay);
        }
        internal static string IouSign(string name) { return name; }

        internal static string PledgeTooltip(string name, int id, int total)
        {
            return string.Format(CultureInfo.InvariantCulture, T("{0}的抵押物（借款 #{1}）。还清{2}信用点后要归还。", "Pledged by {0} (loan #{1}). Given back when the {2} Credits are repaid.", "Залог заёмщика {0} (заём #{1}). Возвращается после выплаты {2} Кр."), name, id, total);
        }
        internal static string ForfeitTooltip(string name)
        {
            return string.Format(CultureInfo.InvariantCulture, T("{0}没有还钱，这件抵押物归你了。", "{0} did not repay; this pledge is yours.", "Заёмщик {0} не вернул долг, этот залог теперь ваш."), name);
        }
        internal static string PledgeReturned(string item, string name)
        {
            return string.Format(CultureInfo.InvariantCulture, T("{0}已还给{1}。", "{0} returned to {1}.", "Залог «{0}» возвращён заёмщику {1}."), item, name);
        }
        internal static string PledgeMissingLine(string item)
        {
            return string.Format(CultureInfo.InvariantCulture, T("我的{0}呢？……那我就从欠款里扣掉它的价钱。", "Where is my {0}? ...Then I'll take its value off what I owe.", "А где мой залог, {0}? ...Тогда я вычту его стоимость из долга."), item);
        }
        internal static string PledgeMissingNote(string name, int value, string item)
        {
            return string.Format(CultureInfo.InvariantCulture, T("{0}因为{2}不见了，少还了{1}信用点。", "{0} kept back {1} Credits for the missing {2}.", "Заёмщик {0} удержал {1} Кр. за пропавший залог «{2}»."), name, value, item);
        }
        internal static string PledgePartlyMissingLine()
        {
            return T("还有一部分抵押物呢？……那我就从欠款里扣掉它的价钱。", "Where is the rest of my pledge? ...Then I'll take its value off what I owe.", "А где остальной залог? ...Тогда я вычту его стоимость из долга.");
        }
        internal static string PledgePartlyMissingNote(string name, int value)
        {
            return string.Format(CultureInfo.InvariantCulture, T("{0}因为部分抵押物不见了，少还了{1}信用点。", "{0} kept back {1} Credits for the missing part of the pledge.", "Заёмщик {0} удержал {1} Кр. за пропавшую часть залога."), name, value);
        }
        internal static string PledgeNoRoom(string item, string name, int kept)
        {
            return string.Format(CultureInfo.InvariantCulture, T("柜台上没有空位还{0}，它留在店里，{1}少还了{2}信用点。", "No room on the counter to hand back the {0}: it stays in the store, and {1} kept back {2} Credits.", "На прилавке нет места, чтобы вернуть залог «{0}»: он остаётся в магазине, а заёмщик {1} удержал {2} Кр."), item, name, kept);
        }
        internal static string PledgeKept(string item)
        {
            return string.Format(CultureInfo.InvariantCulture, T("{0}归你了。", "You keep the {0}.", "Залог «{0}» остаётся у вас."), item);
        }

        // ---- Notifications and night log ----------------------------------------------------------------------

        internal static string NotUnlocked() { return T("先在王尔德的人脉网络里解锁放贷门路。", "Unlock Moneylending in Wild's network first.", "Сначала откройте «Выдачу займов» в сети Уайлда."); }
        internal static string Lent(int amount, string name) { return string.Format(CultureInfo.InvariantCulture, T("借给{1}{0}信用点。", "Lent {0} Credits to {1}.", "Выдан заём {0} Кр., заёмщик: {1}."), amount, name); }
        internal static string Repaid(int amount, string name) { return string.Format(CultureInfo.InvariantCulture, T("{1}还了{0}信用点。", "{1} repaid {0} Credits.", "Возвращено {0} Кр., заёмщик: {1}."), amount, name); }
        internal static string RepaidGoods(string name) { return string.Format(CultureInfo.InvariantCulture, T("{0}用货物抵了债。", "{0} repaid in goods.", "Долг погашен товарами, заёмщик: {0}."), name); }
        internal static string NoShow(string name, int owed) { return string.Format(CultureInfo.InvariantCulture, T("{0}没有来还{1}信用点。", "{0} did not come to repay {1} Credits.", "Заёмщик {0} не пришёл вернуть {1} Кр."), name, owed); }

        // ---- Sawyer -----------------------------------------------------------------------------------------------

        internal static string SawyerOwes(string name, int owed)
        {
            return string.Format(CultureInfo.InvariantCulture, T("{0}欠你{1}信用点。我的伙计们能帮你要回来。", "{0} owes you {1} Credits. My Crew can get it back.", "Заёмщик {0} должен тебе {1} Кр. Моя команда может вернуть долг."), name, owed);
        }
        internal static string SawyerHire(int fee) { return string.Format(CultureInfo.InvariantCulture, T("给你{0}信用点。", "Here are {0} Credits.", "Вот {0} Кр."), fee); }
        internal static string SawyerDecline() { return T("算了，不追了。", "Let it go.", "Оставим это."); }
        internal static string SawyerHired() { return T("交给我们吧。今晚就有消息。", "Consider it done. You'll hear from us tonight.", "Считай, дело сделано. Вечером дадим знать."); }
        internal static string SawyerDeclined() { return T("随你。", "Suit yourself.", "Как знаешь."); }
        internal static string SawyerCollected(int amount, string name)
        {
            return string.Format(CultureInfo.InvariantCulture, T("索耶的伙计从{1}那里要回了{0}信用点。", "Sawyer's Crew collected {0} Credits from {1}.", "Команда Сойера взыскала {0} Кр. с заёмщика {1}."), amount, name);
        }
        internal static string SawyerFailed(string name)
        {
            return string.Format(CultureInfo.InvariantCulture, T("索耶的伙计没找到{0}。", "Sawyer's Crew could not find {0}.", "Команде Сойера не удалось найти заёмщика {0}."), name);
        }
        internal static string SawyerWordSpread() { return T("伙计们上门讨债的事在下层区传开了。", "Word of the Crew's visit spread through the Lower Levels.", "Слух о визите Команды Сойера разошёлся по Нижним уровням."); }

        // ---- Loan Shark -----------------------------------------------------------------------------------------

        internal static string SharkRentDay() { return T("今天交房租，你的收银机看着有点空啊。", "Rent day, and the register looks a little light.", "День аренды, а касса что-то пустовата."); }
        internal static string SharkOffer(int amount, int total, int days)
        {
            return string.Format(CultureInfo.InvariantCulture, T("我现在就能借你{0}信用点。{2}天后还我{1}。", "I can lend you {0} Credits right now. In {2} days you pay back {1}.", "Могу прямо сейчас одолжить тебе {0} Кр. Через {2} дн. вернёшь {1} Кр."), amount, total, days);
        }
        internal static string SharkTake(int amount) { return string.Format(CultureInfo.InvariantCulture, T("我借{0}信用点。", "I'll take the {0} Credits.", "Беру {0} Кр."), amount); }
        internal static string SharkNo() { return T("不用了，谢谢。", "No, thanks.", "Нет, спасибо."); }
        internal static string SharkTaken(int days) { return string.Format(CultureInfo.InvariantCulture, T("爽快。{0}天后见。", "Pleasure. See you in {0} days.", "Приятно иметь дело. Увидимся через {0} дн."), days); }
        internal static string SharkRefused() { return T("那就祝你好运了，朋友。", "Good luck, then, friend.", "Тогда удачи, дружище."); }
        internal static string SharkCollect(int due) { return string.Format(CultureInfo.InvariantCulture, T("日子到了，朋友。{0}信用点。", "Time's up, friend. {0} Credits.", "Время вышло, дружище. {0} Кр."), due); }
        internal static string Pay(int due) { return string.Format(CultureInfo.InvariantCulture, T("给你{0}信用点。", "Here are {0} Credits.", "Вот {0} Кр."), due); }
        internal static string CannotPay() { return T("我拿不出来。", "I don't have it.", "Мне нечем заплатить."); }
        internal static string SharkPaid() { return T("跟你做生意总是很愉快。", "Always a pleasure.", "С тобой всегда приятно иметь дело."); }
        internal static string SharkSquare() { return T("这下两清了。别让我再来一趟。", "We're square. Don't make me come back.", "Теперь мы в расчёте. Не заставляй меня приходить снова."); }
        internal static string Seized(string lender, int value, int count)
        {
            return string.Format(CultureInfo.InvariantCulture, T("{0}拿走了{2}件货物，共值{1}信用点，清单留在柜台上。", "{0} took {2} item(s) worth {1} Credits; the list is on the counter.", "{0} забрал товары ({2} шт.) на сумму {1} Кр., опись лежит на прилавке."), lender, value, count);
        }
        internal static string SeizedTitle() { return T("扣押清单", "Seizure List", "Опись изъятого"); }
        internal static string SeizedList(string lender, int value, string items)
        {
            return string.Format(CultureInfo.InvariantCulture, T("{0}拿走了以下货物，共值{1}信用点：\n{2}", "{0} took these goods, worth {1} Credits in total:\n{2}", "{0} забрал эти товары на общую сумму {1} Кр.:\n{2}"), lender, value, items);
        }

        // Two sentences of a night log line.
        internal static string Join(string first, string second)
        {
            return Chinese() ? first + second : first + " " + second;
        }
        internal static string SeizedNothing(string lender) { return string.Format(CultureInfo.InvariantCulture, T("{0}拿走了你所有的现金。", "{0} took all your cash.", "{0} забрал все ваши наличные."), lender); }

        // ---- First Bank -------------------------------------------------------------------------------------------

        internal static string BankOffer(int small, int large, int weeks, int rate)
        {
            return string.Format(CultureInfo.InvariantCulture, T("我们可以为你提供一笔经营贷款：{0}或{1}信用点，分{2}周偿还，年利率{3}%。", "We can offer you a business loan: {0} or {1} Credits, repaid in {2} weekly installments at {3}% yearly interest.", "Мы можем предложить вам кредит на развитие бизнеса: {0} или {1} Кр. с погашением раз в неделю в течение {2} нед. под {3}% годовых."), small, large, weeks, rate);
        }
        internal static string BankBorrow(int amount, int weekly)
        {
            return string.Format(CultureInfo.InvariantCulture, T("我借{0}信用点（每周还{1}）。", "I'll borrow {0} Credits ({1} per week).", "Беру {0} Кр. ({1} Кр. в неделю)."), amount, weekly);
        }
        internal static string BankNo() { return T("暂时不需要。", "Not right now.", "Пока не нужно."); }
        internal static string BankTaken() { return T("很好。我每周都会来收一期还款。", "Excellent. I'll come by every week for the installment.", "Отлично. Я буду заходить каждую неделю за очередным платежом."); }
        internal static string BankRefused() { return T("这个提议会保留一段时间。", "Our offer stands for a while.", "Наше предложение какое-то время остаётся в силе."); }
        internal static string BankCollect(int due, int left)
        {
            return left > 0
                ? string.Format(CultureInfo.InvariantCulture, T("你好。本周应还{0}信用点，之后还剩{1}期。", "Good day. This week's installment is {0} Credits; {1} more after this one.", "Добрый день. Платёж на этой неделе: {0} Кр. После него останется платежей: {1}."), due, left)
                : string.Format(CultureInfo.InvariantCulture, T("你好。这是最后一期，{0}信用点。", "Good day. This is the last installment: {0} Credits.", "Добрый день. Это последний платёж: {0} Кр."), due);
        }
        internal static string BankPaid(bool last) { return last ? T("贷款已经还清。合作愉快。", "The loan is paid off. A pleasure working with you.", "Кредит погашен. Приятно было с вами работать.") : T("谢谢。下周见。", "Thank you. See you next week.", "Спасибо. До встречи через неделю."); }
        internal static string BankSeize() { return T("那银行只能拿走与这期还款等值的货物。这件事会记入你的信用记录。", "Then the bank will take goods to the value of this installment. It will go on your record.", "Тогда банк заберёт товары на сумму этого платежа. Это попадёт в вашу кредитную историю."); }

        // ---- Network upgrade ----------------------------------------------------------------------------------------

        internal static string UnlockTitle() { return T("放贷门路", "Moneylending", "Выдача займов"); }
        internal static string UnlockBought() { return T("消息已经传开：你的店肯借钱给人。", "Word is out: your shop lends Credits.", "Слух разошёлся: ваш магазин даёт деньги в долг."); }
        internal static string UnlockDescription()
        {
            return T(
                "王尔德会放出消息，说你的店肯借钱给人。下层区、上层区、黑市和革命军的人会上门借钱，你也可以向柜台前的顾客提出借钱（按 L 打开账本）。手头紧的时候，也会有人上门借钱给你。买下后还会得到一块放贷霓虹灯牌。",
                "Wild will let it be known that your shop lends Credits. People from the Lower Levels, the Upper Levels, the Black Market and the Revolution will come asking for loans, and you can offer one to the customer at your counter (L opens the ledger). When cash runs short, lenders will come to you as well. Comes with a Moneylending Neon Sign.",
                "Уайлд пустит слух, что ваш магазин даёт деньги в долг. За займами будут приходить люди с Нижних и Верхних уровней, с Чёрного рынка и революционеры, а клиенту у прилавка заём можно предложить самому (L открывает учётную книгу). Когда наличных не хватает, займы будут предлагать и вам. В комплекте неоновая вывеска «Займы».");
        }

        // ---- Settings window (Settings) --------------------------------------------------------------------------------

        internal static string SettingCost() { return T("“放贷门路”的价格（信用点）", "Price of \"Moneylending\" (Credits)", "Цена «Выдачи займов» (Кр.)"); }
        internal static string SettingCostTip()
        {
            return string.Format(CultureInfo.InvariantCulture, T(
                "王尔德的人脉网络中“放贷门路”的基础价格，默认 {0}，可填 0 至 {1}。实际价格仍由游戏按难度和特性的倍率计算。只对还没有买下“放贷门路”的存档有效。",
                "Base price of \"Moneylending\" in Wild's network: {0} by default, 0 to {1}. The game still applies its multipliers for the difficulty and perks. Only affects runs that have not bought \"Moneylending\" yet.",
                "Базовая цена «Выдачи займов» в сети Уайлда: по умолчанию {0}, допустимо от 0 до {1}. Итоговую цену игра по-прежнему рассчитывает с учётом сложности и других множителей. Действует только в прохождениях, где «Выдача займов» ещё не куплена."),
                Unlock.DefaultCost, Settings.MaxCost);
        }
        internal static string SettingMinRate(string borrower) { return string.Format(CultureInfo.InvariantCulture, T("{0}：利息下限（%）", "{0}: lowest interest (%)", "{0}: мин. ставка (%)"), borrower); }
        internal static string SettingMaxRate(string borrower) { return string.Format(CultureInfo.InvariantCulture, T("{0}：利息上限（%）", "{0}: highest interest (%)", "{0}: макс. ставка (%)"), borrower); }
        internal static string SettingRateTip(Profile p)
        {
            string tip = string.Format(CultureInfo.InvariantCulture, T(
                "{0}提出借钱时，利息取下限与上限之间的一个整数百分比。默认 {1}% 至 {2}%，可填 0 至 {3}；下限比上限大时，两个数对调后使用。只影响之后新提出的借款，已经借出的和当天已经提出的不变。",
                "{0}: the interest proposed is a whole percentage between the lowest and the highest. {1}% to {2}% by default, 0 to {3}; if the lowest is above the highest, the two are used the other way round. Only affects loans requested from now on, not loans already made or the request already made today.",
                "{0}: ставка по займу выбирается как целое число процентов между минимальной и максимальной. По умолчанию от {1}% до {2}%, допустимо от 0 до {3}; если минимальная ставка больше максимальной, значения меняются местами. Действует только на новые просьбы о займе: уже выданные займы и просьба, уже поступившая сегодня, не меняются."),
                BorrowerName(p), p.MinRate, p.MaxRate, Settings.MaxRate);
            if (p.FreeChance > 0)
            {
                tip += string.Format(CultureInfo.InvariantCulture, T(
                    "每次请求有 {0}% 的概率不付利息，这两项只用于其余的请求。",
                    " Each request has a {0}% chance of offering no interest; these two entries apply to the others.",
                    " Каждая просьба с вероятностью {0}% приходит без процентов; эти два значения действуют на остальные."),
                    p.FreeChance);
            }
            return tip;
        }

        // The game's own name for this kind of customer in the game language, as the borrowers are named in the game; the
        // name from the configuration file's keys if the game gives none.
        internal static string BorrowerName(Profile p)
        {
            string name = null;
            try
            {
                name = p.DisplayName();
            }
            catch (Exception)
            {
            }
            return string.IsNullOrEmpty(name) || name == p.NameKey ? p.ConfigName.Replace('_', ' ') : name;
        }

        // ---- Neon sign ------------------------------------------------------------------------------------------------

        internal static string SignName() { return T("放贷霓虹灯牌", "Moneylending Neon Sign", "Неоновая вывеска «Займы»"); }
        internal static string SignDescription()
        {
            return T(
                "一块霓虹灯牌。将其放置在展示柜中，每天有人上门借钱的机会从 40% 提高到 75%。",
                "A neon sign that, when placed in the Display Case, raises the daily chance of a borrower from 40% to 75%.",
                "Неоновая вывеска. Если поставить её на витрину, шанс, что за день придёт заёмщик, вырастет с 40% до 75%.");
        }
        internal static string SignGiven()
        {
            return T(
                "你收到了一块放贷霓虹灯牌。放进展示柜后，每天有人上门借钱的机会提高到 75%。",
                "You received a Moneylending Neon Sign. In the Display Case, it raises the daily chance of a borrower to 75%.",
                "Вы получили неоновую вывеску «Займы». На витрине она повышает шанс, что за день придёт заёмщик, до 75%.");
        }

        // ---- Ledger window ---------------------------------------------------------------------------------------------

        internal static string LedgerTitle() { return T("账本", "Ledger", "Учётная книга"); }
        internal static string LedgerLoans() { return T("借出", "Loans", "Выданные займы"); }
        internal static string LedgerDebts() { return T("借入", "Debts", "Долги"); }
        internal static string LedgerTotals() { return T("统计", "Totals", "Итоги"); }
        internal static string LedgerNone() { return T("暂无", "None", "Нет"); }
        internal static string LedgerOffer(string name) { return string.Format(CultureInfo.InvariantCulture, T("向{0}提出借钱", "Offer a loan to {0}", "Предложить заём: {0}"), name); }
        internal static string LedgerNoCustomer() { return T("柜台前没有顾客", "No customer at the counter", "У прилавка нет клиента"); }
        internal static string LedgerOffered() { return T("已经问过这位顾客", "Already asked this customer", "Этому клиенту уже предлагали"); }
        internal static string LedgerBusy() { return T("等对话结束再问", "Wait for the conversation to end", "Дождитесь конца разговора"); }
        internal static string LedgerClose() { return T("关闭", "Close", "Закрыть"); }

        internal static string LedgerLoanLine(Loan loan, int day)
        {
            string head = string.Format(CultureInfo.InvariantCulture, T("#{0} {1}：借出{2}，应还{3}", "#{0} {1}: lent {2}, owes {3}", "#{0} {1}: выдано {2}, к возврату {3}"), loan.id, loan.name, loan.principal, loan.Outstanding);
            string extra = loan.hasCollateral ? string.Format(CultureInfo.InvariantCulture, T("，抵押{0}", ", pledge: {0}", ", залог: {0}"), loan.collateralName) : "";
            return head + extra + T("。", ". ", ". ") + LoanState(loan, day);
        }

        internal static string LoanState(Loan loan, int day)
        {
            switch ((LoanStatus)loan.status)
            {
                case LoanStatus.Repaid:
                    return T("已还清", "Repaid", "Возвращено");
                case LoanStatus.Arrested:
                    return T("借款人被捕", "Borrower arrested", "Заёмщик арестован");
                case LoanStatus.Collected:
                    return T("索耶的伙计已要回", "Collected by Sawyer's Crew", "Взыскано Командой Сойера");
                case LoanStatus.Defaulted:
                    return loan.sawyerHiredDay >= 0 && loan.sawyerHiredDay >= day
                        ? T("未还，索耶的伙计在追", "Unpaid; Sawyer's Crew is on it", "Не возвращено; этим занимается Команда Сойера")
                        : T("未还", "Unpaid", "Не возвращено");
            }
            int left = loan.dueDay - day;
            if (left > 0)
                return string.Format(CultureInfo.InvariantCulture, T("{0}天后到期（第{1}天）", "Due in {0} days (day {1})", "Срок через {0} дн. (день {1})"), left, loan.dueDay);
            if (left == 0)
                return T("今天到期", "Due today", "Срок сегодня");
            return string.Format(CultureInfo.InvariantCulture, T("逾期{0}天", "{0} days late", "Просрочено на {0} дн."), -left);
        }

        internal static string LedgerDebtLine(Debt debt, int day)
        {
            string lender = debt.lender == Debts.Bank ? BankName() : SharkName();
            if (debt.status == (int)DebtStatus.PaidOff)
                return string.Format(CultureInfo.InvariantCulture, T("{0}：借入{1}，已还清", "{0}: borrowed {1}, paid off", "{0}: взято {1}, погашено"), lender, debt.principal);
            if (debt.status == (int)DebtStatus.Seized)
                return string.Format(CultureInfo.InvariantCulture, T("{0}：借入{1}，以货抵债{2}", "{0}: borrowed {1}, settled with goods worth {2}", "{0}: взято {1}, погашено товарами на {2} Кр."), lender, debt.principal, debt.seizedValue);
            if (debt.lender == Debts.Bank)
                return string.Format(CultureInfo.InvariantCulture, T("{0}：借入{1}，每周还{2}，还剩{3}期，下次第{4}天", "{0}: borrowed {1}, {2} per week, {3} left, next on day {4}", "{0}: взято {1}, {2} в неделю, осталось платежей: {3}, следующий в день {4}"), lender, debt.principal, debt.installment, debt.installmentsLeft, debt.dueDay);
            return string.Format(CultureInfo.InvariantCulture, T("{0}：借入{1}，第{3}天要还{2}", "{0}: borrowed {1}, {2} due on day {3}", "{0}: взято {1}, вернуть {2} в день {3}"), lender, debt.principal, debt.Outstanding, debt.dueDay);
        }

        internal static string LedgerTotalsLine(State s)
        {
            return string.Format(CultureInfo.InvariantCulture,
                T("借出共{0}，赚到利息{1}，损失{2}，追回{3}，付给索耶的伙计{4}",
                  "Lent {0} in total, interest earned {1}, lost {2}, recovered {3}, paid to Sawyer's Crew {4}",
                  "Всего выдано {0}, получено процентов {1}, потеряно {2}, взыскано {3}, заплачено Команде Сойера {4}"),
                s.lent, s.interestEarned, s.lost, s.recovered, s.sawyerFees);
        }

        internal static string SharkName() { return LocHelper.GetLocalizedClientName("name_loan_shark"); }
        internal static string BankName() { return T("上层区第一银行", "First Bank", "Первый Банк"); }
    }
}
