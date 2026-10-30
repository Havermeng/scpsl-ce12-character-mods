using System;
using System.Collections.Generic;
using System.Linq;
using PlayerRoles.FirstPersonControl;
using PlayerRoles.PlayableScps.Scp939;
using RemoteAdmin;
using UnityEngine;

namespace CommandSystem.Commands.RemoteAdmin
{
    /// <summary>
    /// Режиссёрский пульт свидания с 939 — для съёмки дублей.
    ///
    /// Меню по E годится, чтобы играть, но не чтобы снимать: нельзя выбрать конкретную реплику,
    /// нельзя дёрнуть действие, стоя с камерой в стороне, нельзя повторить дубль один в один.
    /// Здесь всё то же самое запускается одной строкой из Remote Admin, а действия идут через ту же
    /// серверную точку <see cref="Scp939Interactions.ServerAct"/>, что и меню, — поведение одинаковое.
    ///
    /// Вместо номера 939 можно писать «*»: возьмётся ближайшая к тебе 939. Партнёр по умолчанию —
    /// ты сам, иначе номер игрока или думми.
    /// </summary>
    public class Scp939DateCommand : ICommand, IUsageProvider
    {
        public string Command { get; } = "939";

        public string[] Aliases { get; } = new string[] { "scp939", "date939" };

        public string Description { get; } = "Режиссёрский пульт свидания с SCP-939 (реплики, действия, ярость, симпатия, интерфейс).";

        public string[] Usage { get; } = new string[] { "действие", "939 (номер или *)", "..." };

        private const string Help =
            "939 <pet|hug|unhug|hold|unhold|date|enddate|eat|rage|calm> <939|*> [партнёр]\n" +
            "939 gift <939|*> <ItemType> [партнёр]   — подарок «из воздуха»\n" +
            "939 line <939|*> <ключ>                 — реплика по ключу (список: 939 lines [категория])\n" +
            "939 say <939|*> <текст>                  — свой текст на экран\n" +
            "939 love <939|*> <0-100>                 — выставить симпатию\n" +
            "939 ragehits <n>                         — ударов за ярость (по умолчанию 2, 0 — насмерть)\n" +
            "939 ui <prompt|speech|hearts|hud|voice|all> <on|off>, 939 ui reload — интерфейс и озвучка";

        public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
        {
            if (!sender.CheckPermission(PlayerPermissions.FacilityManagement, out response))
            {
                return false;
            }

            if (arguments.Count == 0)
            {
                response = Help;
                return false;
            }

            string action = arguments.At(0).ToLowerInvariant();
            ReferenceHub self = (sender as PlayerCommandSender)?.ReferenceHub;

            switch (action)
            {
                case "help":
                    response = Help;
                    return true;
                case "lines":
                    return ListLines(arguments, out response);
                case "ui":
                    return SetUi(arguments, out response);
                case "ragehits":
                    int hits;
                    if (arguments.Count < 2 || !int.TryParse(arguments.At(1), out hits))
                    {
                        response = "Ударов за ярость сейчас: " + Scp939Rage.MaxHits + " (0 — без ограничений).";
                        return false;
                    }
                    Scp939Rage.MaxHits = Mathf.Max(0, hits);
                    response = "Ударов за ярость: " + (Scp939Rage.MaxHits == 0 ? "без ограничений" : Scp939Rage.MaxHits.ToString());
                    return true;
            }

            if (arguments.Count < 2)
            {
                response = "Укажи 939: номер игрока или *.\n" + Help;
                return false;
            }

            ReferenceHub scp = ResolveScp(arguments.At(1), self);
            if (scp == null)
            {
                response = "SCP-939 не найдена (" + arguments.At(1) + ").";
                return false;
            }

            string error;
            switch (action)
            {
                case "line":
                    if (arguments.Count < 3 || !Scp939Interactions.ServerSayLine(scp, arguments.At(2)))
                    {
                        response = "Нет такой реплики. Список: 939 lines";
                        return false;
                    }
                    response = "Сказала «" + arguments.At(2) + "».";
                    return true;

                case "say":
                    if (arguments.Count < 3)
                    {
                        response = "Что сказать?";
                        return false;
                    }
                    Scp939Interactions.ServerSayText(scp, string.Join(" ", arguments.Skip(2)));
                    response = "Готово.";
                    return true;

                case "love":
                    float value;
                    if (arguments.Count < 3 || !float.TryParse(arguments.At(2), out value))
                    {
                        response = "Симпатия: 939 love <939> <0-100>. Сейчас: " + Mathf.RoundToInt(Scp939Affection.Get(scp));
                        return false;
                    }
                    Scp939Interactions.ServerSetAffection(scp, value);
                    response = "Симпатия = " + Mathf.RoundToInt(Scp939Affection.Get(scp));
                    return true;

                case "calm":
                    Scp939Interactions.Calm(scp);
                    response = "Успокаивается.";
                    return true;


            }

            ReferenceHub partner = self;
            int partnerArg = action == "gift" ? 3 : 2;
            if (arguments.Count > partnerArg)
            {
                partner = ResolvePlayer(arguments.At(partnerArg));
                if (partner == null)
                {
                    response = "Партнёр не найден (" + arguments.At(partnerArg) + ").";
                    return false;
                }
            }
            if (partner == null)
            {
                response = "Укажи партнёра: консоль сервера сама партнёром быть не может.";
                return false;
            }

            if (action == "rage")
            {
                Scp939Interactions.BeginRage(scp, partner, null);
                response = "Она в ярости.";
                return true;
            }

            if (action == "gift")
            {
                ItemType type;
                if (arguments.Count < 3 || !Enum.TryParse(arguments.At(2), true, out type))
                {
                    response = "Какой предмет? Например: 939 gift * SCP207";
                    return false;
                }
                bool okGift = Scp939Interactions.ServerAct(scp, partner, Scp939InteractionKind.Gift, true, self, 0, type, out error);
                response = okGift ? "Подарено: " + type : "Не вышло: " + error;
                return okGift;
            }

            Scp939InteractionKind kind;
            bool start = true;
            switch (action)
            {
                case "pet":
                    kind = Scp939InteractionKind.Pet;
                    break;
                case "hug":
                    kind = Scp939InteractionKind.Hug;
                    break;
                case "unhug":
                    kind = Scp939InteractionKind.Hug;
                    start = false;
                    break;
                case "hold":
                    kind = Scp939InteractionKind.HandHold;
                    break;
                case "unhold":
                    kind = Scp939InteractionKind.HandHold;
                    start = false;
                    break;
                case "date":
                    kind = Scp939InteractionKind.Date;
                    break;
                case "enddate":
                    kind = Scp939InteractionKind.Date;
                    start = false;
                    break;
                case "eat":
                    kind = Scp939InteractionKind.Eat;
                    break;
                default:
                    response = "Неизвестное действие «" + action + "».\n" + Help;
                    return false;
            }

            bool ok = Scp939Interactions.ServerAct(scp, partner, kind, start, self, 0, null, out error);
            response = ok ? "Готово: " + action + "." : "Не вышло: " + error;
            return ok;
        }

        /// <summary>«*» — ближайшая к отправителю 939, иначе номер игрока.</summary>
        private static ReferenceHub ResolveScp(string arg, ReferenceHub self)
        {
            if (arg == "*")
            {
                ReferenceHub best = null;
                float bestSqr = float.MaxValue;
                Vector3 from = self != null ? self.transform.position : Vector3.zero;
                foreach (ReferenceHub hub in ReferenceHub.AllHubs)
                {
                    if (!Scp939Interactions.IsInteractable(hub))
                    {
                        continue;
                    }
                    float sqr = (hub.transform.position - from).sqrMagnitude;
                    if (sqr < bestSqr)
                    {
                        bestSqr = sqr;
                        best = hub;
                    }
                }
                return best;
            }

            ReferenceHub scp = ResolvePlayer(arg);
            return Scp939Interactions.IsInteractable(scp) ? scp : null;
        }

        private static ReferenceHub ResolvePlayer(string arg)
        {
            int id;
            ReferenceHub hub;
            if (int.TryParse(arg.TrimEnd('.'), out id) && ReferenceHub.TryGetHub(id, out hub) &&
                hub.roleManager.CurrentRole is IFpcRole)
            {
                return hub;
            }
            // По нику: удобно для думми с говорящими именами («Dummy 2»).
            foreach (ReferenceHub candidate in ReferenceHub.AllHubs)
            {
                if (candidate != null && candidate.nicknameSync != null &&
                    string.Equals(candidate.nicknameSync.MyNick, arg, StringComparison.OrdinalIgnoreCase) &&
                    candidate.roleManager.CurrentRole is IFpcRole)
                {
                    return candidate;
                }
            }
            return null;
        }

        private static bool ListLines(ArraySegment<string> arguments, out string response)
        {
            string filter = arguments.Count > 1 ? arguments.At(1) : null;
            IEnumerable<string> keys = Scp939Lines.Keys;
            if (!string.IsNullOrEmpty(filter))
            {
                keys = keys.Where(k => k.StartsWith(filter, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                response = "Категории: " + string.Join(", ", Scp939Lines.CategoryNames.OrderBy(c => c)) +
                           "\nКлючи категории: 939 lines <категория>";
                return true;
            }

            var lines = new List<string>();
            foreach (string key in keys.OrderBy(k => k))
            {
                Scp939Line line;
                Scp939Lines.TryGet(key, out line);
                lines.Add(key + " — " + line.Ru);
            }
            response = lines.Count > 0 ? string.Join("\n", lines) : "Ничего не найдено.";
            return lines.Count > 0;
        }

        private static bool SetUi(ArraySegment<string> arguments, out string response)
        {
            if (arguments.Count >= 2 && arguments.At(1).Equals("reload", StringComparison.OrdinalIgnoreCase))
            {
                Scp939Interactions.ServerSetUi(Scp939Ui.Flags, true);
                response = "Озвучка перечитана с диска.";
                return true;
            }

            if (arguments.Count < 3)
            {
                response = "939 ui <prompt|speech|hearts|hud|voice|all> <on|off>. Флаги сейчас: " + Describe(Scp939Ui.Flags);
                return false;
            }

            byte flag;
            switch (arguments.At(1).ToLowerInvariant())
            {
                case "prompt":
                    flag = Scp939Ui.FlagPrompt;
                    break;
                case "speech":
                    flag = Scp939Ui.FlagSpeech;
                    break;
                case "hearts":
                    flag = Scp939Ui.FlagHearts;
                    break;
                case "hud":
                    flag = Scp939Ui.FlagFollowHud;
                    break;
                case "voice":
                    flag = Scp939Ui.FlagVoice;
                    break;
                case "all":
                    flag = Scp939Ui.FlagPrompt | Scp939Ui.FlagSpeech | Scp939Ui.FlagHearts;
                    break;
                default:
                    response = "Неизвестный элемент интерфейса.";
                    return false;
            }

            bool on = arguments.At(2).Equals("on", StringComparison.OrdinalIgnoreCase) || arguments.At(2) == "1";
            byte flags = on ? (byte)(Scp939Ui.Flags | flag) : (byte)(Scp939Ui.Flags & ~flag);
            Scp939Interactions.ServerSetUi(flags, false);
            response = "Интерфейс: " + Describe(flags);
            return true;
        }

        private static string Describe(byte flags)
        {
            var parts = new List<string>();
            parts.Add("prompt=" + ((flags & Scp939Ui.FlagPrompt) != 0 ? "on" : "off"));
            parts.Add("speech=" + ((flags & Scp939Ui.FlagSpeech) != 0 ? "on" : "off"));
            parts.Add("hearts=" + ((flags & Scp939Ui.FlagHearts) != 0 ? "on" : "off"));
            parts.Add("hud=" + ((flags & Scp939Ui.FlagFollowHud) != 0 ? "on" : "off"));
            parts.Add("voice=" + ((flags & Scp939Ui.FlagVoice) != 0 ? "on" : "off"));
            return string.Join(", ", parts);
        }
    }
}
