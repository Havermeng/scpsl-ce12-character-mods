using System.Collections.Generic;
using UnityEngine;

namespace PlayerRoles.PlayableScps.Scp939
{
    /// <summary>Одна реплика SCP-939: ключ (он же имя файла озвучки) и текст на двух языках.</summary>
    public struct Scp939Line
    {
        public string Key;
        public string Ru;
        public string En;

        public string Text(bool russian)
        {
            return russian ? Ru : En;
        }
    }

    /// <summary>
    /// Все реплики SCP-939 в одном месте.
    ///
    /// У каждой реплики есть КЛЮЧ. По сети ходит только он: текст каждый клиент берёт на своём языке,
    /// а озвучка лежит файлом с тем же именем (StreamingAssets/Scp939Voice/&lt;язык&gt;/&lt;ключ&gt;.wav,
    /// свои записи — в папке custom, они важнее сгенерированных). Поэтому ключи менять нельзя без
    /// перегенерации озвучки: Tools/Scp939Voice/generate_voice.py разбирает ИМЕННО вызовы L(...)
    /// из этого файла — формат строк L("ключ", "ru", "en") держать одной строкой.
    ///
    /// Тон — сухой и абсурдный: она слепой хищник-мимик, который очень старается быть милым.
    /// Шутка почти всегда в том, что она спокойно говорит жуткое.
    /// </summary>
    public static class Scp939Lines
    {
        private static readonly Dictionary<string, Scp939Line> All = new Dictionary<string, Scp939Line>();
        private static readonly Dictionary<string, List<string>> Categories = new Dictionary<string, List<string>>();

        static Scp939Lines()
        {
            // ---------------------------------------------------------------- поглаживание
            L("pet.0", "«Ой. Приятно. Только никому не говори.»", "\"...more. Don't take the hand away.\"");
            L("pet.1", "«Продолжай. Я сегодня ещё никого не съела.»", "\"Warm. You're not afraid of me?\"");
            L("pet.2", "«Ты гладишь SCP. По-моему, это нарушение.»", "\"The last one who petted me lives inside me now. Joking. Probably.\"");
            L("pet.3", "«Мурр. Всё, забудь. Этого не было.»", "\"I'm blind, but you sound very handsome.\"");
            L("pet.4", "«Ещё чуть-чуть, и я перестану кусаться. Может быть.»", "\"Once more and I'll purr. It will sound... unpleasant.\"");
            L("pet.5", "«Записывайте в отчёт: человек меня гладит. Мне понравилось.»", "\"No one has touched me in so long. Without a taser.\"");

            // Слишком часто гладить нельзя: это первая ступенька к её ярости.
            L("petspam.0", "«Это уже пятый раз. Я пёс, что ли? Я — SCP.»", "\"That's the fifth time. I'm running out of fur. I don't have fur.\"");
            L("petspam.1", "«Руку убрал. У тебя их две, я считала.»", "\"Hand. Away. While it's still yours.\"");
            L("petspam.2", "«Стоп. Лимит нежности на сегодня исчерпан.»", "\"I am not a dog. I am worse.\"");

            // ---------------------------------------------------------------- объятие
            L("hug.0", "«Ты меня обнимаешь или пульс проверяешь?»", "\"Are you hugging me... or checking for a pulse?\"");
            L("hug.1", "«Тепло. Непривычно. Обычно меня держат в смирительной рубашке.»", "\"Your heart is beating fast. Tasty beat.\"");
            L("hug.2", "«Не отпускай, пока я не досчитаю до трёх. Я считаю медленно.»", "\"Don't move. I'm memorising how you sound.\"");
            L("hug.3", "«Обнимашки — это как прыжок, только без укуса.»", "\"No one has hugged me before. Usually they just restrain me.\"");
            L("hugend.0", "«Уже всё? Я только вошла во вкус.»", "\"Already? I only just started not eating you.\"");
            L("hugend.1", "«Ладно. Но я всё равно запомнила, где у тебя рёбра.»", "\"Fine. But I remember where your ribs are.\"");

            // ---------------------------------------------------------------- за руку
            L("hold.0", "«Мы гуляем? Ходят слухи, что я здесь собака.»", "\"Lead. I can't see where, but I'll follow.\"");
            L("hold.1", "«Держусь за руку. Если что-то хрустнет — это не я, это когти.»", "\"If security sees us, I don't know you.\"");
            L("hold.2", "«Ведёшь меня по комплексу за ручку. Романтика уровня Кетер.»", "\"Are we walking? I've only ever walked through vents.\"");
            L("hold.3", "«Стой, я же слепая. Ты хоть смотришь, куда идёшь?»", "\"Your hand is small. Convenient. I mean — cute.\"");
            L("holdend.0", "«Отпустил? Ну и ладно. Сама дойду. По запаху.»", "\"Letting go? Fine. I'll find you by smell anyway.\"");
            L("holdend.1", "«Рука была потная? Не отвечай.»", "\"You let go. I'll remember that.\"");

            // ---------------------------------------------------------------- свидание
            L("date.0", "«Ого, тут ветер. Мы на крыше? Уровень доступа повышен.»", "\"There's wind here. A real one. Are we up high?\"");
            L("date.1", "«Свечи? Я слепая. Но пахнет как на первом свидании: горелым.»", "\"Candles? I'm blind. But it smells romantic. And slightly like a fire.\"");
            L("date.2", "«Ресторан! Первый раз ем там, где еда не убегает.»", "\"Is this a restaurant? First time I eat where the food doesn't run.\"");
            L("date.3", "«Тихо. Ни сирен, ни MTF. Подозрительно хорошее свидание.»", "\"Quiet. No sirens, no MTF. Only you. Suspicious.\"");
            L("date.4", "«Я надела юбку. Не спрашивай зачем. Я её даже не вижу.»", "\"I wore a skirt. No idea why. I can't see it.\"");
            L("dateend.0", "«Уже уходим? Поставлю пять звёзд. Шесть, если ты заплатишь.»", "\"Leaving already? I'll remember this evening. And your address.\"");
            L("dateend.1", "«В следующий раз плачу я. Только скажи, чем.»", "\"Next time it's my treat. You won't even know who.\"");
            L("dateend.2", "«Проводишь до камеры? Там уютно. И запирается снаружи.»", "\"Will you walk me to my cell? It's cosy. And locked.\"");

            // ---------------------------------------------------------------- ужин
            L("eat.0", "«Спагетти. Длинные, как очередь на патч 15.0.»", "\"Spaghetti. Long, like... no, I won't say it. Tasty.\"");
            L("eat.1", "«Фрикадельки. Из кого?»", "\"Meatballs. Made of whom?\"");
            L("eat.2", "«Вкусно. Хотя твоя рука пахнет лучше.»", "\"Tasty. But your hand smells better.\"");
            L("eat.3", "«Как в мультике про собак. Только я съем и собаку.»", "\"Like that cartoon with the dogs. Except I'd eat the dog too.\"");
            L("eatempty.0", "«Тарелка пустая. Твоя — нет. Дай сюда.»", "\"My plate is empty. Yours isn't.\"");

            // ---------------------------------------------------------------- ярость
            L("rage.0", "«ВСЁ.»", "\"THAT'S IT.\"");
            L("rage.1", "«Я ДАЛА ТЕБЕ ШАНС. ДВА ШАНСА. ВТОРОЙ БЫЛ БОНУС.»", "\"I GAVE YOU A CHANCE. TWO CHANCES.\"");
            L("rage.2", "«ТЫ ПРИНЁС ЭТО НА СВИДАНИЕ?!»", "\"YOU BROUGHT THAT TO A DATE?!\"");
            L("rage.3", "«БЕГИ. Я ДАМ ТЕБЕ ТРИ СЕКУНДЫ. ОДНУ.»", "\"RUN. I'LL GIVE YOU THREE SECONDS. ONE.\"");
            L("calm.0", "«...прости. Иногда я кусаюсь. Это часть меня.»", "\"...sorry. I bite sometimes. It's part of who I am.\"");
            L("calm.1", "«Всё, я спокойна. Где твоя вторая рука?»", "\"Okay, I'm calm. Where's your other arm?\"");
            L("calm.2", "«Давай сделаем вид, что этого не было. Я уже сделала.»", "\"Start over? Can you even start over?\"");

            // ---------------------------------------------------------------- симпатия
            L("love.up.1", "«Я выучила, как ты дышишь. Это комплимент.»", "\"I've learned how you breathe.\"");
            L("love.up.2", "«Ты мне нравишься. Ты до сих пор жив — это о многом говорит.»", "\"I like you. You're still alive, that says a lot.\"");
            L("love.up.3", "«Мы встречаемся? Я скажу остальным 939. Нас много.»", "\"Are we dating? I'll tell the other 939s. There are many of us.\"");
            L("love.up.4", "«Я бы тебя не съела. Даже если бы очень хотелось. А мне хочется.»", "\"I would never eat you. Even if I really wanted to. And I do.\"");
            L("love.up.5", "«Всё. Ты мой. Я уже позвонила твоей маме. Твоим голосом.»", "\"That's it. You're mine. I already called your mom. In your voice.\"");
            L("love.down.0", "«Ты мне нравишься чуть меньше. Примерно на одну руку.»", "\"I like you a bit less now. About one arm less.\"");

            // ---------------------------------------------------------------- подарки по настрою
            L("mood.loved.0", "«Это мне? Сохраню. Внутри себя. Не спрашивай где.»", "\"...sweet. More. Please.\"");
            L("mood.loved.1", "«Мне ещё никто ничего не дарил. Только пули.»", "\"For me? I'll remember your voice. Literally, I can do that.\"");
            L("mood.loved.2", "«Ты лучший игрок на сервере. Я проверила: ты один.»", "\"No one ever gave me anything except bullets in the back.\"");
            L("mood.loved.3", "«Беру и не отдам. Ты же не возражаешь? Вот и умница.»", "\"I'll hide this. Inside me. Don't ask how.\"");
            L("mood.liked.0", "«Пахнет незнакомо, но приятно. Как ты.»", "\"Smells strange, but nice. Like you.\"");
            L("mood.liked.1", "«Ты добрый. Подозрительно добрый.»", "\"You're kind. Suspiciously kind.\"");
            L("mood.liked.2", "«Оно теперь моё? Всё, что мне дают, — моё. Даже руки.»", "\"Is it mine now? Everything I'm given is mine. Even hands.\"");
            L("mood.liked.3", "«Другие у меня только отбирают. И стреляют.»", "\"The others give me nothing. They only take. And shoot.\"");
            L("mood.neutral.0", "«Оно молчит. Как мой прошлый парень. После.»", "\"It's silent. Like my last boyfriend. Afterwards.\"");
            L("mood.neutral.1", "«Не пахнет едой. Значит, это не подарок.»", "\"Doesn't smell like food. So it's not a gift.\"");
            L("mood.neutral.2", "«Оставлю. Может, потом съем.»", "\"I'll keep it. Maybe I'll eat it later.\"");
            L("mood.neutral.3", "«Не знаю зачем. Положу к остальным костям.»", "\"No idea what it's for. I'll put it with the other bones.\"");
            L("mood.wary.0", "«Оно гудит. Убери, пока я не решила, что гудишь ты.»", "\"It hums. Take it away before I decide it's you humming.\"");
            L("mood.wary.1", "«Пахнет людьми и железом. То есть охраной.»", "\"Smells of people and iron. So — security.\"");
            L("mood.wary.2", "«Ты даришь ЭТО девушке? Серьёзно?»", "\"You give THIS to a girl? Seriously?\"");
            L("mood.wary.3", "«Нет. Забери. И подумай над своим поведением.»", "\"No. Take it back. And think about what you've done.\"");
            L("mood.scared.0", "«Это кусается! Здесь кусаюсь только я!»", "\"It bites! I'm the only one who bites here!\"");
            L("mood.scared.1", "«Убери. Убери. Или я уберу тебя.»", "\"Put it away. Put it away. Or I'll put YOU away.\"");
            L("mood.scared.2", "«Красный флаг. Я его не вижу, но чувствую.»", "\"That's a red flag. I can't see it, but I can feel it.\"");
            L("mood.scared.3", "«Я слышала такое. Потом было больно. Всем.»", "\"I've heard one of those. Then it hurt. Everyone.\"");

            // ---------------------------------------------------------------- подарки поштучно
            // Она слепа: вещи узнаёт по запаху, весу и звуку — и говорит именно об этом.
            L("gift.KeycardJanitor", "«Карта уборщика. Открывает двери и швабру.»", "\"Smells of bleach and a wet rag. I never touched the one who carried it. Almost.\"");
            L("gift.KeycardScientist", "«Это чтобы я могла к тебе заходить?»", "\"So I can come visit you?\"");
            L("gift.KeycardResearchCoordinator", "«Координатор исследований. Тот, что спорил, как меня назвать.»", "\"This one opens more doors. Behind them they argued what to call me.\"");
            L("gift.KeycardZoneManager", "«Менеджер зоны. Щёлкал у моей двери и уходил. Ты тоже уйдёшь?»", "\"One like this clicked at my door. Will you come and leave too?\"");
            L("gift.KeycardGuard", "«Карта охраны. Эти ходят по двое. Вкуснее по одному.»", "\"Smells of a belt and gun oil. These walk in pairs. Tastier one at a time.\"");
            L("gift.KeycardContainmentEngineer", "«Инженер содержания. Один раз со мной заговорил. Один раз.»", "\"This one fixed the locks. And spoke to me once. Once.\"");
            L("gift.KeycardNTFOfficer", "«Карта офицера MTF. Их шаги я слышу раньше голоса.»", "\"These come from above, in masks. I know their steps before their voices.\"");
            L("gift.KeycardNTFLieutenant", "«Лейтенант MTF. Голос ровный, ему не страшно. Обидно.»", "\"He commands the others. Steady voice, not afraid. That's worse.\"");
            L("gift.KeycardNTFCommander", "«Командир MTF. Когда он говорит «начали», начинается плохое.»", "\"When he says 'begin', bad things begin.\"");
            L("gift.KeycardFacilityManager", "«Директор комплекса. Такой рукой меня и вписали в отчёт.»", "\"Smells of an office and other people's decisions. A hand like that wrote me down.\"");
            L("gift.KeycardChaosInsurgency", "«Карта Хаоса. Пахнет дымом и свободой. Возьми меня с собой.»", "\"Smells of smoke and street. Outside. Have you been outside? Take me.\"");
            L("gift.KeycardO5", "«Карта О5. От неё так тихо, что страшно. Ты точно человек?»", "\"I've never heard this one. It's so quiet it's frightening.\"");
            L("gift.Ammo12gauge", "«Двенадцатый калибр. Толстые, как твои пальцы.»", "\"Thick, like fingers. Like YOUR fingers.\"");
            L("gift.Ammo556x45", "«Пять-пятьдесят шесть. Тонкие и злые. Их сыпят горстями, когда спешат меня убить.»", "\"Thin and angry. They pour these out when they're in a hurry to kill me.\"");
            L("gift.Ammo44cal", "«Сорок четвёртый. Тяжёлые, по одному. Бьют наверняка. Романтика.»", "\"Heavy, one at a time. These are for making sure. Romantic.\"");
            L("gift.Ammo762x39", "«Семь-шестьдесят два. Длинные. От них у стен потом долго звенит.»", "\"Long ones. The walls ring for a long time after them.\"");
            L("gift.Ammo9x19", "«Девять миллиметров. Звенит красиво. Но я знаю, куда их потом кладут.»", "\"Small change, rings nicely. But I know where they end up. In me.\"");
            L("gift.Meat", "«Мясо! Чьё? Ладно, неважно.»", "\"Meat! You brought me meat. Whose? Okay. Doesn't matter.\"");
            L("gift.SCP207", "«О, кола. Мог бы сразу с неё начать.»", "\"Oh, cola. You should have started with that.\"");
            L("gift.SCP330", "«Конфеты? Брать надо одну. Я возьму две. И руку.»", "\"It crunches. Give me more, I won't take two. I'll take the hand.\"");
            L("gift.Medkit", "«Спасибо. А себе оставил?»", "\"Thanks. Did you keep one for yourself?\"");
            L("gift.SCP500", "«Таблетка от всего. Кроме меня.»", "\"Rattles like a handful of pebbles. Cures everything. Except me.\"");
            L("gift.Painkillers", "«Обезболивающее. Тебе больно? Будет.»", "\"Bitter. People eat this when it hurts. Does it hurt? It will.\"");
            L("gift.Adrenaline", "«Адреналин? Ты собрался бежать. От меня?»", "\"Cold, with a needle. You take that before you run. From me?\"");
            L("gift.Coin", "«Это весь твой бюджет на свидание?»", "\"Is that your whole budget for the date?\"");
            L("gift.SCP268", "«Шапка-невидимка? Зачем? Я тебя и так не вижу.»", "\"An invisibility hat? Why? I can't see you anyway.\"");
            L("gift.SCP1853", "«В голове стало тихо. Кажется, теперь я умею считать до четырёх.»", "\"My head went quiet. Thanks. I think I can count to four now.\"");
            L("gift.SCP018", "«Оно прыгает само. Не бросай, я ловлю только людей.»", "\"It jumps by itself. Don't drop it here, I won't catch it. I only catch people.\"");
            L("gift.SCP2176", "«Стекло. Разобьёшь — погаснет свет. Мне всё равно, я и так в темноте.»", "\"Glass. When it breaks the lights go out. I don't care, I'm in the dark anyway.\"");
            L("gift.SCP244a", "«Тянет холодом. Я люблю холод, но не такой.»", "\"It breathes cold. I like cold, but not this kind.\"");
            L("gift.SCP244b", "«Этот холод гуще. У меня от него немеют лапы.»", "\"This cold is thicker. It makes my paws go numb.\"");
            L("gift.SCP1576", "«Оттуда зовут голосом того, кого уже нет. Это мой приём. Верни.»", "\"It calls in the voice of someone who's gone. That's MY trick. Give it back.\"");
            L("gift.Radio", "«Чужие голоса в ящике. Я умею так же. Сказать голосом твоего начальника?»", "\"Strangers' voices in there. I can do that too. Want me to talk like your boss?\"");
            L("gift.Flashlight", "«Ты серьёзно? Я же слепая.»", "\"Seriously? You know I am blind.\"");
            L("gift.ArmorLight", "«Лёгкая броня. Боишься, что порвут? Не бойся. Я аккуратно.»", "\"You're afraid of being torn. Don't be. I'll be gentle.\"");
            L("gift.ArmorCombat", "«Пластины. Ты идёшь туда, где бьют. Бить буду я.»", "\"Plates. You're going somewhere they hit. I'll be the one hitting.\"");
            L("gift.ArmorHeavy", "«Тяжёлая. В такой не убежишь. Хорошо.»", "\"Heavy. You can't run in that. Good.\"");
            L("gift.MicroHID", "«Оно воет! Убери, у меня звенит в голове!»", "\"It howls! Take it away, my head is ringing!\"");
            L("gift.Jailbird", "«Ржавая, с зазубринами. Этим не защищаются — этим добивают.»", "\"Rusted, with notches. You don't defend with that. You finish.\"");
            L("gift.SahurBat", "«Тук-тук-тук. Это бита или у тебя сердце?»", "\"Tung-tung-tung. Someone is knocking wood. I don't like it.\"");
            L("gift.BanHammer", "«Бан-молот! Им кого-то стёрли. Даже из списка серверов.»", "\"Someone was erased with it. Completely. Even from the server list.\"");
            L("gift.GrenadeHE", "«Ты на свидание пришёл или на диверсию?»", "\"Did you come for a date or a sabotage mission?\"");
            L("gift.GrenadeFlash", "«Светошумовая. Свет мне не нужен. А вот шум — да.»", "\"It hisses before it blinds you. Already am. Thanks.\"");
            L("gift.GunCOM15", "«Пистолет. Компактный. Как мои надежды на нормальное свидание.»", "\"Small, but it bites. Like me, but without the charm.\"");
            L("gift.GunCOM18", "«Пистолет девушке? Это красный флаг.»", "\"The same, only quieter. Quieter doesn't mean kinder.\"");
            L("gift.GunCom45", "«Ещё пистолет. Ты идёшь на свидание или на войну?»", "\"Smells of powder. You came to a date with this?\"");
            L("gift.GunE11SR", "«Винтовка. Это кусается! Здесь кусаюсь только я!»", "\"That's what they cornered me with. No. No.\"");
            L("gift.GunCrossvec", "«Кроссвек. Стреляет быстро. Как и моё терпение.»", "\"It chatters fast, so fast. There's no hiding from that.\"");
            L("gift.GunFSP9", "«ПП FSP-9. Компактный. Как твой шанс уйти отсюда целым.»", "\"You don't aim one of those. You just spray.\"");
            L("gift.GunLogicer", "«Логикер. Тяжёлый. Как и наши отношения.»", "\"Heavy and angry. Those come when they want me dead. So — always.\"");
            L("gift.GunRevolver", "«Револьвер. Один патрон на двоих. Романтика.»", "\"It clicks before it fires. I flinch at that click.\"");
            L("gift.GunAK", "«АК. Классика. У меня от него звенит в ушах.»", "\"Long, with a strap. Carried by the ones who come in ranks.\"");
            L("gift.GunShotgun", "«Дробовик. Я его слышала. Мне не понравилось.»", "\"It tears things apart. I've heard it. I do the same, but with love.\"");
            L("gift.ParticleDisruptor", "«Дезинтегратор. Убери! Я не хочу стать пылью на свидании.»", "\"It hums from the inside. It leaves no bones. What would I eat?\"");
            L("gift.ParticleDisruptorV14", "«Дезинтегратор новой модели. Ещё и красивый. Убери!»", "\"This one is newer. Hums higher, angrier. Still — take it away.\"");
        }

        private static void L(string key, string ru, string en)
        {
            All[key] = new Scp939Line { Key = key, Ru = ru, En = en };

            // Категория — всё до последней точки: «pet.3» -> «pet», «mood.loved.0» -> «mood.loved».
            int dot = key.LastIndexOf('.');
            string category = dot > 0 ? key.Substring(0, dot) : key;
            List<string> list;
            if (!Categories.TryGetValue(category, out list))
            {
                list = new List<string>();
                Categories[category] = list;
            }
            list.Add(key);
        }

        public static bool TryGet(string key, out Scp939Line line)
        {
            if (string.IsNullOrEmpty(key))
            {
                line = default(Scp939Line);
                return false;
            }
            return All.TryGetValue(key, out line);
        }

        public static bool Exists(string key)
        {
            return !string.IsNullOrEmpty(key) && All.ContainsKey(key);
        }

        /// <summary>Ключ из категории по номеру варианта. null, если категории нет.</summary>
        public static string Pick(string category, int variant)
        {
            List<string> list;
            if (!Categories.TryGetValue(category, out list) || list.Count == 0)
            {
                return null;
            }
            return list[Mathf.Abs(variant) % list.Count];
        }

        public static int Count(string category)
        {
            List<string> list;
            return Categories.TryGetValue(category, out list) ? list.Count : 0;
        }

        public static IEnumerable<string> Keys => All.Keys;

        public static IEnumerable<string> CategoryNames => Categories.Keys;
    }
}
