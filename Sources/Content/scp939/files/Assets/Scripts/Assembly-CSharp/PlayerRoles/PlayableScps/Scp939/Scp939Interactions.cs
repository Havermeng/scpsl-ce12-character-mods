using System.Collections.Generic;
// ServerRemoveItem — метод расширения из InventorySystem.InventoryExtensions, а не член Inventory.
using InventorySystem;
using InventorySystem.Items;
using Mirror;
using PlayerRoles.FirstPersonControl;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PlayerRoles.PlayableScps.Scp939
{
    /// <summary>Клиент просит сервер начать или прекратить взаимодействие с 939.</summary>
    public struct Scp939InteractionRequest : NetworkMessage
    {
        public uint TargetNetId;
        public byte Kind;
        public bool Start;

        /// <summary>Серийный номер подарка в инвентаре. Осмысленно только для <see cref="Scp939InteractionKind.Gift"/>.</summary>
        public ushort ItemSerial;

        /// <summary>
        /// Кого 939 обнимает/ведёт/даёт погладить. Ноль — самого отправителя.
        ///
        /// Нужно для съёмки: сцену между 939 и думми можно снять с любого ракурса, а сцену с самим
        /// собой — только из первого лица, где не видно ни своих рук, ни лиц.
        /// </summary>
        public uint PartnerNetId;
    }

    /// <summary>Сервер сообщает всем клиентам, что 939 что-то сделала или сказала.</summary>
    public struct Scp939InteractionBroadcast : NetworkMessage
    {
        public uint ScpNetId;
        public uint PartnerNetId;
        public byte Kind;
        public bool Start;

        /// <summary>
        /// Ключ реплики (<see cref="Scp939Lines"/>). По сети идёт ключ, а не текст: текст каждый
        /// клиент берёт на своём языке, а озвучку — файлом с тем же именем.
        /// </summary>
        public string LineKey;

        /// <summary>Сказать после текущей реплики, а не вместо неё.</summary>
        public bool Queued;

        /// <summary>Свободный текст режиссёра (команда <c>939 say</c>).</summary>
        public string Text;

        /// <summary>Небольшое число для конкретного вида: флаги интерфейса, съеденные фрикадельки.</summary>
        public byte Value;

        public bool HasAffection;
        public byte Affection;
        public sbyte AffectionDelta;

        /// <summary>Реплика на смену уровня отношений — говорится второй, после основной.</summary>
        public string LevelLineKey;

    }

    /// <summary>
    /// Взаимодействия с SCP-939: ласка, объятия, прогулка за руку, подарки, свидание, ужин,
    /// мимикрия, ярость и симпатия.
    ///
    /// Клавиша взаимодействия (E) рядом с 939 открывает меню действий; в меню та же клавиша
    /// подтверждает выбранный пункт, а выбирают его колесом мыши или стрелками. Всё то же самое
    /// режиссёр может запускать командой <c>939 ...</c> из Remote Admin — для съёмки дублей.
    ///
    /// Штатная система взаимодействий (IServerInteractable) здесь не годится — она рассчитана на
    /// статичные объекты сцены с коллайдерами-интерактаблами, а цель тут другой игрок. Поэтому цель
    /// ищется поиском ближайшей 939 в конусе перед игроком: хитбоксы живут на пулящейся модели,
    /// а не на хабе, и завязываться на них хрупко.
    /// </summary>
    public static class Scp939Interactions
    {
        public static bool ShowPartnerSelection { get; set; } = true;
        public static bool CinemaSuppressGiftRage { get; set; } = false;
        // Мир НОРМАЛЬНЫЙ: замер в игре 2026-09-04 — капсула игрока 1.80, глаза 1.59 над полом.
        // transform хаба сидит в ЦЕНТРЕ капсулы, на 0.96 над полом — доли роста ниже считаются от
        // этой базы, не переводить их в метры, не перепроверив в игре.

        /// <summary>Дальность, с которой можно начать взаимодействие.</summary>
        public const float Range = 2.5f;

        /// <summary>
        /// На каком расстоянии (м, между центрами) обнимающий стоит от 939. Раньше это было 0.8 роста
        /// до камеры ≈ 0.5 м — а грудь 939 вынесена от её центра вперёд на 0.6–0.7 м, и камера
        /// оказывалась ВНУТРИ её корпуса (замер 2026-09-28: её грудь позади камеры). Статическое поле,
        /// а не константа — подбирается вживую.
        /// </summary>
        public static float HugDistance = 1.0f;

        /// <summary>Насколько обнимаемого смещает влево от её оси (см. PullTowards).</summary>
        public static float HugSideShift = 0f;

        /// <summary>Сторона, с которой 939 идёт, когда её ведут за руку.</summary>
        public static float WalkSideOffset = 0.60f;

        /// <summary>Насколько точно надо смотреть на цель (косинус половины угла конуса).</summary>
        private const float AimTolerance = 0.5f;

        /// <summary>Запас к <see cref="Range"/>: клиент и сервер видят чуть разные позиции.</summary>
        private const float ServerRangeTolerance = 0.8f;

        /// <summary>Насколько можно разойтись в объятии, прежде чем связь рвётся.</summary>
        private const float BreakDistance = 3.5f;

        /// <summary>
        /// То же для прогулки. Больше, чем у объятия: на повороте или лестнице она на секунду
        /// отстаёт, и рвать из-за этого руку нельзя — её догоняет <see cref="Scp939Companion"/>.
        /// </summary>
        private const float HandHoldBreakDistance = 14f;

        /// <summary>Сколько держится поза поглаживания.</summary>
        private const float PetDuration = 3.2f;

        /// <summary>Окно, в котором ласки считаются «подряд».</summary>
        private const float PetSpamWindow = 10f;

        /// <summary>Сколько фрикаделек на её тарелке (см. DateTableDetails.Dinner).</summary>
        private const int MeatballsOnPlate = 3;

        /// <summary>Ниже этой симпатии «пугающий» подарок доводит её до ярости.</summary>
        private const float RageAffectionThreshold = 45f;

        /// <summary>Пауза между командой «гасим экран» и самим переносом.</summary>
        private const float DateTeleportDelay = 0.55f;

        /// <summary>
        /// Метки на каждом шаге цепочки «выбор в меню → сервер → все клиенты». Выключено: включать
        /// при отладке, когда «нажал — ничего не произошло» и нужно понять, до какого шага дошло.
        /// </summary>
        public static bool VerboseLog = false;

        // =================================================================== общее состояние

        private sealed class Session
        {
            public ReferenceHub Scp;
            public ReferenceHub Partner;
            public Scp939InteractionKind Kind;
        }

        /// <summary>Идущее свидание: кто участвует и куда их вернуть.</summary>
        private sealed class DateRecord
        {
            public ReferenceHub Scp;
            public ReferenceHub Partner;
            public Vector3 ScpReturn;
            public Vector3 PartnerReturn;
            public float ScpYaw;
            public float PartnerYaw;
            public int MeatballsEaten;
        }

        /// <summary>Отложенный перенос: и посадка за стол, и возврат домой идут через него.</summary>
        private struct PendingMove
        {
            public float At;
            public ReferenceHub Hub;
            public ReferenceHub FaceTo;   // развернуть к этому игроку после переноса
            public Transform Seat;        // не null — сажаем за стол
            public Vector3 Position;      // иначе переносим в эту точку
            public float Yaw;
            public bool Sit;              // думми садится на стул, а не встаёт перед ним
        }

        /// <summary>Активные взаимодействия на сервере, ключ — 939.</summary>
        private static readonly Dictionary<ReferenceHub, Session> Sessions = new Dictionary<ReferenceHub, Session>();
        private static readonly List<DateRecord> ActiveDates = new List<DateRecord>();
        private static readonly List<PendingMove> PendingMoves = new List<PendingMove>();
        private static readonly List<ReferenceHub> Doomed = new List<ReferenceHub>();
        private static readonly Dictionary<ReferenceHub, List<float>> PetTimes = new Dictionary<ReferenceHub, List<float>>();

        private static int _lineCounter;

        // --- клиент ---

        /// <summary>Что клиент знает о сессиях: 939 (netId) → партнёр и вид. Наполняется из рассылок.</summary>
        private struct ClientSession
        {
            public uint Partner;
            public Scp939InteractionKind Kind;
        }

        private static readonly Dictionary<uint, ClientSession> ClientSessions = new Dictionary<uint, ClientSession>();

        /// <summary>Последняя известная клиенту симпатия каждой 939 — для шапки меню.</summary>
        private static readonly Dictionary<uint, float> ClientAffection = new Dictionary<uint, float>();

        /// <summary>Идущие свидания глазами клиента: пары (939, партнёр).</summary>
        private static readonly List<KeyValuePair<uint, uint>> ClientDates = new List<KeyValuePair<uint, uint>>();

        /// <summary>С кем обнимается или гуляет сам локальный игрок — для притягивания и руки в кадре.</summary>
        private static ReferenceHub _localTarget;
        private static Scp939InteractionKind _localKind;
        private static ReferenceHub _menuTarget;

        /// <summary>
        /// Кого назначили партнёром вместо себя. null — партнёр сам игрок.
        /// Живёт только на клиенте: сервер получает готовый netId в запросе.
        /// </summary>
        private static ReferenceHub _partnerOverride;

        [RuntimeInitializeOnLoadMethod]
        private static void Init()
        {
            StaticUnityMethods.OnUpdate -= Tick;
            StaticUnityMethods.OnUpdate += Tick;
            CustomNetworkManager.OnClientReady -= RegisterHandlers;
            CustomNetworkManager.OnClientReady += RegisterHandlers;
            PlayerRoleManager.OnRoleChanged -= OnRoleChanged;
            PlayerRoleManager.OnRoleChanged += OnRoleChanged;
        }

        private static void RegisterHandlers()
        {
            // БЕЗ флага «уже зарегистрировано». ReplaceHandler идемпотентен, а флаг был опасен:
            // если редактор не перезагружает домен между запусками, статик переживает сессию,
            // регистрация пропускается — и на свежих NetworkServer/NetworkClient обработчиков нет.
            NetworkServer.ReplaceHandler<Scp939InteractionRequest>(ServerProcessRequest);
            NetworkClient.ReplaceHandler<Scp939InteractionBroadcast>(ClientApply);
            Scp939GiftTransfer.Register();

            // Состояние прошлой сессии не должно протекать в новую по той же причине.
            Sessions.Clear();
            ActiveDates.Clear();
            PendingMoves.Clear();
            PetTimes.Clear();
            Scp939Affection.Clear();
            ClientSessions.Clear();
            ClientDates.Clear();
            ClientAffection.Clear();
            _localTarget = null;
            _localKind = Scp939InteractionKind.None;
            _menuTarget = null;
            _partnerOverride = null;
            Scp939ActionMenu.Close();
        }

        /// <summary>Роль, с которой можно взаимодействовать: и штатная 939, и наша одетая.</summary>
        public static bool IsInteractable(RoleTypeId role)
        {
            return role == RoleTypeId.Scp939 || role == RoleTypeId.Scp939Dressed;
        }

        public static bool IsInteractable(ReferenceHub hub)
        {
            return hub != null && hub.roleManager.CurrentRole != null && IsInteractable(hub.roleManager.CurrentRole.RoleTypeId);
        }

        private static void Tick()
        {
            if (!Scp939ModSettings.Enabled("interactions")) return;
            if (NetworkServer.active)
            {
                Scp939GiftTransfer.Tick();
                ServerTick();
            }
            if (NetworkClient.active)
            {
                ClientTick();
            }
        }

        /// <summary>
        /// Смерть, смена роли, респавн. Раньше мод их не замечал вовсе: объятие продолжало тянуть
        /// игрока к месту бывшей 939, рука от первого лица висела навсегда, а «Закончить свидание»
        /// после респавна телепортировало НОВУЮ роль в старую точку.
        /// </summary>
        private static void OnRoleChanged(ReferenceHub hub, PlayerRoleBase prevRole, PlayerRoleBase newRole)
        {
            if (hub == null)
            {
                return;
            }
            if (NetworkServer.active)
            {
                ServerOnRoleChanged(hub);
            }
            if (NetworkClient.active)
            {
                ClientOnRoleChanged(hub);
            }
        }

        // =================================================================== клиент

        private static void ClientTick()
        {
            ReferenceHub self;
            if (!ReferenceHub.TryGetLocalHub(out self) || self == null)
            {
                Scp939ActionMenu.SetPrompt(null);
                return;
            }

            // Пока игрок печатает в админке или консоли, меню клавиш не ловит: иначе E и Backspace
            // в поле ввода (например, при переименовании думми) заодно дёргали меню 939.
            if (InputBlocked())
            {
                Scp939ActionMenu.SetPrompt(null);
                return;
            }

            KeyCode interactKey = NewInput.GetKey(ActionName.Interact);

            if (Scp939ActionMenu.IsOpen)
            {
                Scp939ActionMenu.SetPrompt(null);
                Scp939ActionMenu.HandleInput(interactKey);

                // Отошёл — меню закрывается само. На свидании не закрываем: там меню — единственный
                // способ уехать из башни.
                if (Scp939ActionMenu.IsOpen && !LocalOnDate() && !StillInReach(self, _menuTarget))
                {
                    Scp939ActionMenu.Close();
                }
            }
            else
            {
                ReferenceHub aimed;
                if (TryFindTarget(self, Range, out aimed))
                {
                    Scp939ActionMenu.SetPrompt(Scp939Ui.PromptVisible ? PromptFor(aimed, interactKey) : null);
                    if (Input.GetKeyDown(interactKey))
                    {
                        OpenMenu(aimed);
                    }
                }
                else if (_localTarget != null && IsInteractable(_localTarget) && StillInReach(self, _localTarget))
                {
                    // Прогулка за руку: она идёт СБОКУ и в конус прицела не попадает — раньше, чтобы
                    // открыть меню, приходилось разворачиваться к ней. Теперь та же клавиша открывает
                    // меню той, кого ты держишь, куда бы ты ни смотрел.
                    Scp939ActionMenu.SetPrompt(Scp939Ui.PromptVisible ? PromptFor(_localTarget, interactKey) : null);
                    if (Input.GetKeyDown(interactKey))
                    {
                        OpenMenu(_localTarget);
                    }
                }
                else if (LocalOnDate())
                {
                    // На свидании меню открывается, даже если 939 не в прицеле: из башни пешком не
                    // выйти, и без этого игрок оставался бы там заперт, стоило ей отойти или умереть.
                    Scp939ActionMenu.SetPrompt(Scp939Ui.PromptVisible
                        ? Label(KeyTag(interactKey) + " свидание", KeyTag(interactKey) + " date")
                        : null);
                    if (Input.GetKeyDown(interactKey))
                    {
                        OpenDateMenu();
                    }
                }
                else
                {
                    Scp939ActionMenu.SetPrompt(null);
                }
            }

            // Притягиваем себя к 939, пока идёт объятие, — иначе обнимать некого.
            if (_localKind == Scp939InteractionKind.Hug && _localTarget != null)
            {
                if (IsInteractable(_localTarget))
                {
                    PullTowards(self, _localTarget);
                }
                else
                {
                    ResetLocalHold();
                }
            }
        }

        private static bool InputBlocked()
        {
            if (CursorManagement.CursorManager.MovementLocked)
            {
                return true;
            }
            EventSystem events = EventSystem.current;
            GameObject selected = events != null ? events.currentSelectedGameObject : null;
            if (selected == null)
            {
                return false;
            }
            var tmp = selected.GetComponent<TMP_InputField>();
            if (tmp != null && tmp.isFocused)
            {
                return true;
            }
            var legacy = selected.GetComponent<UnityEngine.UI.InputField>();
            return legacy != null && legacy.isFocused;
        }

        private static string KeyTag(KeyCode key)
        {
            return "<color=#FF6B85>[" + new ReadableKeyCode(key).ShortVersion + "]</color>";
        }

        /// <summary>Текст подсказки под прицелом: только клавиша и имя, чтобы не лезть на табличку с ником.</summary>
        private static string PromptFor(ReferenceHub scp, KeyCode key)
        {
            return KeyTag(key) + " " + NameOf(scp);
        }

        /// <summary>Как показывать 939 в интерфейсе: её ник, если он задан, иначе «SCP-939».</summary>
        public static string NameOf(ReferenceHub hub)
        {
            return hub != null && hub.nicknameSync != null && !string.IsNullOrEmpty(hub.nicknameSync.MyNick)
                ? hub.nicknameSync.MyNick
                : "SCP-939";
        }

        /// <summary>Кто сейчас партнёр в меню: назначенный думми или сам игрок.</summary>
        private static ReferenceHub EffectivePartner()
        {
            if (_partnerOverride != null && _partnerOverride.roleManager.CurrentRole is IFpcRole)
            {
                return _partnerOverride;
            }
            _partnerOverride = null;
            ReferenceHub self;
            return ReferenceHub.TryGetLocalHub(out self) ? self : null;
        }

        private static bool ClientSessionIs(ReferenceHub scp, ReferenceHub partner, Scp939InteractionKind kind)
        {
            ClientSession session;
            return scp != null && partner != null && ClientSessions.TryGetValue(scp.netId, out session) &&
                   session.Partner == partner.netId && session.Kind == kind;
        }

        private static bool ClientOnDate(ReferenceHub hub)
        {
            if (hub == null)
            {
                return false;
            }
            uint id = hub.netId;
            for (int i = 0; i < ClientDates.Count; i++)
            {
                if (ClientDates[i].Key == id || ClientDates[i].Value == id)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Свидание «с моей стороны»: сам игрок или назначенный им партнёр сидит за столом. Раньше
        /// флаг ставился только если партнёром был сам игрок, и свидание с думми нельзя было
        /// закончить — повторное «Пойти на свидание» запоминало башню как точку возврата.
        /// </summary>
        private static bool LocalOnDate()
        {
            ReferenceHub self;
            return (ReferenceHub.TryGetLocalHub(out self) && ClientOnDate(self)) || ClientOnDate(_partnerOverride);
        }

        /// <summary>
        /// Собирает и показывает меню действий для указанной 939. Пункты разбиты по разделам, а то,
        /// что сейчас недоступно, остаётся на месте серым с причиной — раскладка не прыгает.
        /// </summary>
        private static void OpenMenu(ReferenceHub scp)
        {
            _menuTarget = scp;
            ReferenceHub partner = EffectivePartner();

            bool hugging = ClientSessionIs(scp, partner, Scp939InteractionKind.Hug);
            bool holding = ClientSessionIs(scp, partner, Scp939InteractionKind.HandHold);
            bool onDate = ClientOnDate(scp) || ClientOnDate(partner);

            var entries = new List<Scp939ActionMenu.Entry>();

            entries.Add(Scp939ActionMenu.Entry.Section(Label("Нежности", "Affection")));
            // Поглаживание первым: это самое частое действие, а меню открывается на прошлом выборе.
            entries.Add(new Scp939ActionMenu.Entry(
                Label("Погладить по голове", "Pet her head"),
                delegate { Send(scp, Scp939InteractionKind.Pet, true); },
                Label("Она склонится под ладонь. Только не слишком часто — на четвёртый раз подряд она начнёт злиться.",
                      "She leans into your palm. Not too often though — the fourth time in a row annoys her.")));
            entries.Add(new Scp939ActionMenu.Entry(
                hugging ? Label("Перестать обнимать", "Stop hugging") : Label("Обнять", "Hug"),
                delegate { Send(scp, Scp939InteractionKind.Hug, !hugging); },
                hugging ? Label("Отпустить её.", "Let her go.")
                        : Label("Объятие держится, пока не отпустишь.", "The hug lasts until you let go.")));
            entries.Add(new Scp939ActionMenu.Entry(
                holding ? Label("Отпустить руку", "Let go of hand") : Label("Взять за руку", "Hold hand"),
                delegate { Send(scp, Scp939InteractionKind.HandHold, !holding); },
                holding ? Label("Она останется стоять на месте.", "She will stay where she is.")
                        : Label("Она пойдёт рядом с тобой, а отстав — догонит бегом. Меню во время прогулки — та же клавиша.",
                                "She walks beside you and jogs to catch up. Same key opens this menu mid-walk.")));

            if (Scp939ModSettings.Enabled("gifts"))
            {
            entries.Add(Scp939ActionMenu.Entry.Section(Label("Подарки", "Gifts")));
            entries.Add(new Scp939ActionMenu.Entry(
                Label("Подарить предмет...", "Give a gift..."),
                delegate { OpenGiftMenu(scp); },
                Label("Она слепая и узнаёт вещи по запаху. Еду любит, оружие — нет.",
                      "She is blind and knows things by smell. Loves food, hates weapons.")));
            }
            if (Scp939ModSettings.Enabled("dates"))
            {
            entries.Add(Scp939ActionMenu.Entry.Section(Label("Свидание", "Date")));
            if (onDate)
            {
                entries.Add(new Scp939ActionMenu.Entry(
                    Label("Поужинать", "Have dinner"),
                    delegate { Send(scp, Scp939InteractionKind.Eat, true); },
                    Label("Спагетти с фрикадельками. Фрикаделек на её тарелке три.",
                          "Spaghetti and meatballs. She has three meatballs.")));
                entries.Add(new Scp939ActionMenu.Entry(
                    Label("Закончить свидание", "End the date"),
                    delegate { SendDateEnd(scp); },
                    Label("Вернёт вас обоих туда, откуда вы пришли.", "Returns you both to where you came from.")));
            }
            else
            {
                entries.Add(new Scp939ActionMenu.Entry(
                    Label("Пойти на свидание", "Go on a date"),
                    delegate { Send(scp, Scp939InteractionKind.Date, true); },
                    Label("Ужин при свечах в башне на поверхности.", "Candlelit dinner up in the surface tower.")));
                entries.Add(Scp939ActionMenu.Entry.Disabled(
                    Label("Поужинать", "Have dinner"),
                    Label("Только на свидании.", "Only on a date.")));
            }

            }
            entries.Add(Scp939ActionMenu.Entry.Section(Label("Прочее", "Other")));
            // Пункт-шутка по просьбе пользователя: намеренно НИЧЕГО не делает, меню остаётся как было.
            entries.Add(new Scp939ActionMenu.Entry(
                Label("Жёсткий джага-джага", "Hardcore jaga jaga"),
                Scp939ActionMenu.Nothing));
            // Для съёмки: сцену между 939 и думми можно снять со стороны, а не из первого лица.
            if (ShowPartnerSelection)
            {
            entries.Add(new Scp939ActionMenu.Entry(
                Label("Партнёр: ", "Partner: ") + (_partnerOverride != null ? NameOf(_partnerOverride) : Label("я", "me")),
                delegate { OpenPartnerMenu(scp); },
                Label("Кого она обнимает и с кем идёт на свидание — можно назначить думми и снимать со стороны.",
                      "Who she interacts with — assign a dummy to film from outside.")));
            }
            entries.Add(new Scp939ActionMenu.Entry(Label("Закрыть", "Close"), null));

            Scp939ActionMenu.Open(NameOf(scp), MenuSubtitle(scp, partner, hugging, holding, onDate), entries);
            ShowAffectionInMenu(scp);
        }

        /// <summary>Симпатия этой 939 в шапке меню — по последнему значению из рассылок.</summary>
        private static void ShowAffectionInMenu(ReferenceHub scp)
        {
            float value;
            if (scp == null || !ClientAffection.TryGetValue(scp.netId, out value))
            {
                value = Scp939Affection.Initial;
            }
            Scp939ActionMenu.SetAffection(value, Scp939Affection.Title(Scp939Affection.LevelOf(value), IsRussian()));
        }

        /// <summary>
        /// Короткое меню за столиком, когда 939 не в прицеле: ужин и выход. Это аварийный выход из
        /// башни — он не должен зависеть от того, где она стоит.
        /// </summary>
        private static void OpenDateMenu()
        {
            ReferenceHub scp = DateScpOf(EffectivePartner());
            ReferenceHub self;
            if (scp == null && ReferenceHub.TryGetLocalHub(out self))
            {
                scp = DateScpOf(self);
            }
            _menuTarget = scp;

            var entries = new List<Scp939ActionMenu.Entry>();
            if (scp != null)
            {
                ReferenceHub target = scp;
                entries.Add(new Scp939ActionMenu.Entry(
                    Label("Поужинать", "Have dinner"),
                    delegate { Send(target, Scp939InteractionKind.Eat, true); }));
            }
            entries.Add(new Scp939ActionMenu.Entry(
                Label("Закончить свидание", "End the date"),
                delegate { SendDateEnd(scp); },
                Label("вернёт обоих обратно", "returns you both")));
            entries.Add(new Scp939ActionMenu.Entry(Label("Закрыть", "Close"), null));

            Scp939ActionMenu.Open(Label("Свидание", "Date"),
                Label("столик в башне", "table up in the tower"), entries);
            if (scp != null)
            {
                ShowAffectionInMenu(scp);
            }
            else
            {
                Scp939ActionMenu.SetAffection(-1f, null);
            }
        }

        private static ReferenceHub DateScpOf(ReferenceHub partner)
        {
            if (partner == null)
            {
                return null;
            }
            for (int i = 0; i < ClientDates.Count; i++)
            {
                if (ClientDates[i].Value == partner.netId)
                {
                    ReferenceHub scp;
                    return ReferenceHub.TryGetHubNetID(ClientDates[i].Key, out scp) ? scp : null;
                }
            }
            return null;
        }

        /// <summary>
        /// Просьба закончить свидание. Сервер ищет запись по партнёру, а если не нашёл — по 939, так
        /// что закончить можно и из режиссёрского меню, и после смерти одного из них.
        /// </summary>
        private static void SendDateEnd(ReferenceHub scp)
        {
            uint partner = _partnerOverride != null ? _partnerOverride.netId : 0u;
            Log("клиент просит закончить свидание");
            NetworkClient.Send(new Scp939InteractionRequest
            {
                TargetNetId = scp != null ? scp.netId : 0u,
                Kind = (byte)Scp939InteractionKind.Date,
                Start = false,
                PartnerNetId = partner
            });
        }

        /// <summary>Строка под заголовком меню: с кем и что сейчас происходит.</summary>
        private static string MenuSubtitle(ReferenceHub scp, ReferenceHub partner, bool hugging, bool holding, bool onDate)
        {
            string who = _partnerOverride != null ? NameOf(_partnerOverride) : Label("ты", "you");
            string state;
            if (onDate)
            {
                state = Label("свидание", "on a date");
            }
            else if (hugging)
            {
                state = Label("объятие", "hugging");
            }
            else if (holding)
            {
                state = Label("за руку", "holding hands");
            }
            else
            {
                state = Label("она слушает", "she is listening");
            }
            if (!ShowPartnerSelection) return state;
            return Label("Партнёр: ", "Partner: ") + who + "   ·   " + state;
        }

        /// <summary>
        /// Второй уровень меню: что подарить. Список берётся из инвентаря игрока — дарить можно ровно
        /// то, что у тебя есть. Названия — из перевода игры, а не сырые имена enum.
        /// </summary>
        private static void OpenGiftMenu(ReferenceHub scp)
        {
            ReferenceHub self;
            ReferenceHub.TryGetLocalHub(out self);
            var entries = new List<Scp939ActionMenu.Entry>();

            if (self != null && self.inventory != null)
            {
                foreach (KeyValuePair<ushort, ItemBase> pair in self.inventory.UserInventory.Items)
                {
                    if (pair.Value == null)
                    {
                        continue;
                    }
                    ushort serial = pair.Key;
                    entries.Add(new Scp939ActionMenu.Entry(
                        ItemTranslationReader.GetName(pair.Value.ItemTypeId),
                        delegate { SendGift(scp, serial); }));
                }
            }

            if (entries.Count == 0)
            {
                entries.Add(new Scp939ActionMenu.Entry(Label("(нечего дарить)", "(nothing to give)"), null));
            }

            entries.Add(new Scp939ActionMenu.Entry(Label("< Назад", "< Back"), delegate { OpenMenu(scp); }));

            Scp939ActionMenu.Open(Label("Что подарить?", "What to give?"),
                Label("оружие её пугает", "weapons scare her"),
                entries, delegate { OpenMenu(scp); });
            Scp939ActionMenu.SetAffection(-1f, null);
        }

        private static void SendGift(ReferenceHub target, ushort serial)
        {
            Log("клиент дарит предмет, serial=" + serial);
            NetworkClient.Send(new Scp939InteractionRequest
            {
                TargetNetId = target.netId,
                Kind = (byte)Scp939InteractionKind.Gift,
                Start = true,
                ItemSerial = serial
            });
        }

        /// <summary>Не отошёл ли игрок от 939, для которой открыто меню.</summary>
        private static bool StillInReach(ReferenceHub self, ReferenceHub scp)
        {
            if (self == null || !IsInteractable(scp))
            {
                return false;
            }
            float limit = Range + 1f;
            return (scp.transform.position - self.transform.position).sqrMagnitude <= limit * limit;
        }

        /// <summary>Русский ли язык выбран в игре.</summary>
        public static bool IsRussian()
        {
            string dir = TranslationReader.TranslationDirectoryName;
            if (!string.IsNullOrEmpty(dir) && dir.StartsWith("ru", System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            var culture = TranslationReader.TranslationCulture;
            return culture != null && culture.TwoLetterISOLanguageName == "ru";
        }

        /// <summary>Подпись на языке игры. Отдельный словарь ради пары строк заводить незачем.</summary>
        public static string Label(string russian, string english)
        {
            return IsRussian() ? russian : english;
        }

        private static void PullTowards(ReferenceHub self, ReferenceHub scp)
        {
            var fpc = self.roleManager.CurrentRole as IFpcRole;
            if (fpc == null)
            {
                return;
            }

            Vector3 selfPos = self.transform.position;
            Vector3 away = selfPos - scp.transform.position;
            away.y = 0f;
            if (away.sqrMagnitude < 1e-4f)
            {
                return;
            }

            // Чуть влево от её оси: её голова ложится тебе на правое плечо (Poser), и со сдвигом в
            // кадре остаётся видна комната слева, а не только её грудь во весь экран.
            Vector3 myRight = Vector3.Cross(Vector3.up, -away.normalized);
            Vector3 wanted = scp.transform.position + away.normalized * HugDistance - myRight * HugSideShift;
            wanted.y = selfPos.y;
            fpc.FpcModule.Position = Vector3.Lerp(selfPos, wanted, Time.deltaTime * 9f);
        }

        private static void Log(string message)
        {
            if (VerboseLog)
            {
                Debug.Log("[939] " + message);
            }
        }

        private static void Send(ReferenceHub target, Scp939InteractionKind kind, bool start)
        {
            uint partner = _partnerOverride != null ? _partnerOverride.netId : 0u;
            Log("клиент отправляет: " + kind + (start ? " (начать)" : " (прекратить)") +
                ", цель netId=" + target.netId + ", партнёр=" + (partner == 0u ? "я" : partner.ToString()));
            NetworkClient.Send(new Scp939InteractionRequest
            {
                TargetNetId = target.netId,
                Kind = (byte)kind,
                Start = start,
                PartnerNetId = partner
            });
        }

        /// <summary>
        /// Кандидаты в партнёры: живые игроки рядом с 939, ближние первыми. Не больше восьми — в меню
        /// десять строк, и две заняты «Со мной» и «Назад»; раньше при толпе думми «Назад» пропадало.
        /// </summary>
        private static List<ReferenceHub> PartnerCandidates(ReferenceHub scp)
        {
            var list = new List<ReferenceHub>();
            foreach (ReferenceHub hub in ReferenceHub.AllHubs)
            {
                if (hub == null || hub == scp || hub.isLocalPlayer || !(hub.roleManager.CurrentRole is IFpcRole))
                {
                    continue;
                }
                if ((hub.transform.position - scp.transform.position).sqrMagnitude > 12f * 12f)
                {
                    continue;
                }
                list.Add(hub);
            }
            Vector3 center = scp.transform.position;
            list.Sort((a, b) => (a.transform.position - center).sqrMagnitude
                .CompareTo((b.transform.position - center).sqrMagnitude));
            if (list.Count > 8)
            {
                list.RemoveRange(8, list.Count - 8);
            }
            return list;
        }

        /// <summary>Меню выбора партнёра — с кем 939 будет взаимодействовать.</summary>
        private static void OpenPartnerMenu(ReferenceHub scp)
        {
            var entries = new List<Scp939ActionMenu.Entry>
            {
                new Scp939ActionMenu.Entry(Label("Со мной", "With me"),
                    delegate { _partnerOverride = null; OpenMenu(scp); })
            };

            foreach (ReferenceHub hub in PartnerCandidates(scp))
            {
                ReferenceHub captured = hub;
                entries.Add(new Scp939ActionMenu.Entry(NameOf(hub),
                    delegate { _partnerOverride = captured; OpenMenu(scp); },
                    Scp939ModCompatibility.IsDummy(captured) ? Label("думми", "dummy") : null));
            }

            entries.Add(new Scp939ActionMenu.Entry(Label("< Назад", "< Back"), delegate { OpenMenu(scp); }));

            Scp939ActionMenu.Open(Label("С кем?", "With whom?"),
                Label("для съёмки со стороны", "to film from outside"),
                entries, delegate { OpenMenu(scp); });
            Scp939ActionMenu.SetAffection(-1f, null);
        }

        /// <summary>Ближайшая 939 перед игроком.</summary>
        public static bool TryFindTarget(ReferenceHub self, float range, out ReferenceHub target)
        {
            target = null;
            if (self == null)
            {
                return false;
            }

            Transform camera = self.PlayerCameraReference;
            Vector3 origin = camera.position;
            Vector3 forward = camera.forward;
            float bestSqr = range * range;

            foreach (ReferenceHub hub in ReferenceHub.AllHubs)
            {
                if (hub == null || hub == self || !IsInteractable(hub))
                {
                    continue;
                }

                Vector3 delta = hub.transform.position - origin;
                float sqr = delta.sqrMagnitude;
                if (sqr > bestSqr || sqr < 1e-4f)
                {
                    continue;
                }
                // Конус вместо рейкаста: 939 крупная и сгорбленная, строгий луч в центр модели
                // постоянно промахивался бы мимо.
                if (Vector3.Dot(forward, delta.normalized) < AimTolerance)
                {
                    continue;
                }

                bestSqr = sqr;
                target = hub;
            }

            return target != null;
        }

        private static void ResetLocalHold()
        {
            _localTarget = null;
            _localKind = Scp939InteractionKind.None;
            Scp939FirstPersonHand.Hide();
        }

        private static void ClientOnRoleChanged(ReferenceHub hub)
        {
            if (hub.isLocalPlayer)
            {
                ResetLocalHold();
                Scp939ActionMenu.Close();
            }
            else if (hub == _localTarget)
            {
                ResetLocalHold();
            }
            if (hub == _partnerOverride && !(hub.roleManager.CurrentRole is IFpcRole))
            {
                _partnerOverride = null;
            }
            ClientSessions.Remove(hub.netId);
        }

        /// <summary>
        /// Внешняя обёртка применения рассылки. Всё, что рисует сцену на клиенте, идёт через одну
        /// точку с перехватом исключений: сбой в любом шаге раньше тихо обрывал ВСЮ цепочку.
        /// </summary>
        private static void ClientApply(Scp939InteractionBroadcast msg)
        {
            try
            {
                ClientApplyInternal(msg);
            }
            catch (System.Exception e)
            {
                Debug.LogError("[939] сбой при применении " + (Scp939InteractionKind)msg.Kind + ": " + e);
            }
        }

        /// <summary>Выполняет шаг сцены отдельно от остальных: упавший шаг не уносит с собой другие.</summary>
        private static void Step(string what, System.Action action)
        {
            try
            {
                action();
            }
            catch (System.Exception e)
            {
                Debug.LogError("[939] шаг «" + what + "» не выполнен: " + e);
            }
        }

        private static ReferenceHub ResolveClient(uint netId)
        {
            ReferenceHub hub;
            return netId != 0u && ReferenceHub.TryGetHubNetID(netId, out hub) ? hub : null;
        }

        private static void ClientApplyInternal(Scp939InteractionBroadcast msg)
        {
            var kind = (Scp939InteractionKind)msg.Kind;
            Log("клиент применяет: " + kind + (msg.Start ? " (начать)" : " (прекратить)"));

            ReferenceHub scp = ResolveClient(msg.ScpNetId);
            ReferenceHub partner = ResolveClient(msg.PartnerNetId);

            if (msg.Start && (kind == Scp939InteractionKind.Hug || kind == Scp939InteractionKind.HandHold ||
                kind == Scp939InteractionKind.Pet || kind == Scp939InteractionKind.Date || kind == Scp939InteractionKind.Rage))
                Scp939GiftTransfer.InterruptSettledPresentation(scp);

            switch (kind)
            {
                case Scp939InteractionKind.Ui:
                    Scp939Ui.Flags = msg.Value;
                    if (!msg.Start)
                    {
                        Scp939Voice.Reload();
                    }
                    return;

                case Scp939InteractionKind.Date:
                    ClientApplyDate(msg, scp, partner);
                    break;

                case Scp939InteractionKind.Sit:
                    ClientApplySit(msg, partner);
                    break;

                case Scp939InteractionKind.Hug:
                case Scp939InteractionKind.HandHold:
                case Scp939InteractionKind.None:
                    ClientApplyHold(msg, kind, scp, partner);
                    break;

                case Scp939InteractionKind.Pet:
                    ClientApplyPet(msg, scp, partner);
                    break;

                case Scp939InteractionKind.Eat:
                    ClientApplyEat(msg, scp);
                    break;

                case Scp939InteractionKind.Rage:
                    if (msg.Start && partner != null && partner.isLocalPlayer)
                    {
                        Step("красная вспышка", delegate { Scp939ScreenFade.Flash(new Color(0.55f, 0f, 0.02f), 0.45f); });
                    }
                    SpeakStep(scp, msg.LineKey, msg.Queued);
                    break;

                case Scp939InteractionKind.Say:
                    Step("реплика режиссёра", delegate
                    {
                        Scp939Speech.Say(msg.Text, Mathf.Clamp(2.5f + msg.Text.Length * 0.06f, 3f, 9f));
                    });
                    break;

                default:
                    SpeakStep(scp, msg.LineKey, msg.Queued);
                    break;
            }

            ClientApplyAffection(msg, scp);
        }

        private static void SpeakStep(ReferenceHub scp, string key, bool queued, float seconds = 0f)
        {
            if (string.IsNullOrEmpty(key) || scp == null)
            {
                return;
            }
            Step("реплика " + key, delegate { Scp939Voice.Speak(scp, key, seconds, queued); });
        }

        private static void ClientApplyAffection(Scp939InteractionBroadcast msg, ReferenceHub scp)
        {
            if (!msg.HasAffection || scp == null)
            {
                return;
            }
            ClientAffection[scp.netId] = msg.Affection;
            Step("шкала симпатии", delegate
            {
                Scp939AffectionHud.Show(NameOf(scp), msg.Affection, msg.AffectionDelta, IsRussian());
            });
            // Реплика на новый уровень — в очередь, после реплики на само действие.
            if (!Scp939ModCompatibility.Filming || Scp939Ui.RelationshipRevealed)
                SpeakStep(scp, msg.LevelLineKey, true);
        }

        private static void ClientApplyHold(Scp939InteractionBroadcast msg, Scp939InteractionKind kind,
                                            ReferenceHub scp, ReferenceHub partner)
        {
            if (!msg.Start)
            {
                // Конец сессии разбираем ДО поиска аниматора: 939 могла умереть, модели уже нет, а
                // своё состояние (притягивание, рука в кадре) снять нужно всё равно.
                ClientSessions.Remove(msg.ScpNetId);
                if ((scp != null && scp == _localTarget) || (partner != null && partner.isLocalPlayer))
                {
                    ResetLocalHold();
                }
                if (scp != null)
                {
                    Step("снять позу 939", delegate
                    {
                        Transform root;
                        Scp939InteractionPoser.End(AnimatorOf(scp, out root));
                    });
                }
                if (partner != null)
                {
                    Step("снять позу партнёра", delegate
                    {
                        Transform root;
                        Scp939InteractionPoser.End(AnimatorOf(partner, out root));
                    });
                }
                SpeakStep(scp, msg.LineKey, false);
                return;
            }

            if (scp == null || partner == null)
            {
                return;
            }

            ClientSessions[msg.ScpNetId] = new ClientSession { Partner = msg.PartnerNetId, Kind = kind };
            SpeakStep(scp, msg.LineKey, false);

            Step("поза 939", delegate
            {
                Transform modelRoot;
                Animator animator = AnimatorOf(scp, out modelRoot);
                Scp939InteractionPoser.Begin(animator, modelRoot, partner, kind);
            });

            // Держание за руку и объятие — обоюдные: партнёр тоже тянется к ней (рука или обе руки).
            if (kind == Scp939InteractionKind.HandHold || kind == Scp939InteractionKind.Hug)
            {
                Scp939InteractionKind giverKind = kind;
                Step("поза партнёра", delegate
                {
                    Transform giverRoot;
                    Animator giverAnimator = AnimatorOf(partner, out giverRoot);
                    Scp939InteractionPoser.BeginGiver(giverAnimator, giverRoot, scp, giverKind, 0f);
                });
            }

            if (partner.isLocalPlayer)
            {
                _localTarget = scp;
                _localKind = kind;
                // Удерживаемая поза, поэтому без срока — рука снимется вместе с сессией.
                Step("рука от первого лица", delegate { Scp939FirstPersonHand.Show(scp, 0f, kind); });
            }
        }

        private static void ClientApplyPet(Scp939InteractionBroadcast msg, ReferenceHub scp, ReferenceHub petter)
        {
            if (scp == null || petter == null)
            {
                return;
            }

            // Реплика — ПЕРВЫМ шагом: она не зависит от моделей и ригов, и даже если поза где-то не
            // соберётся, игрок увидит, что действие дошло.
            SpeakStep(scp, msg.LineKey, false);

            // Слишком частая ласка — она отстраняется и ворчит, позы нет.
            if (!msg.Start)
            {
                return;
            }

            Step("поза 939", delegate
            {
                Transform modelRoot;
                Animator animator = AnimatorOf(scp, out modelRoot);
                Scp939InteractionPoser.Begin(animator, modelRoot, petter, Scp939InteractionKind.Pet, PetDuration);
            });

            // Позируем и руку гладящего: у людей другой риг, поэтому отдельный вход.
            Step("поза гладящего", delegate
            {
                Transform giverRoot;
                Animator giverAnimator = AnimatorOf(petter, out giverRoot);
                Scp939InteractionPoser.BeginGiver(giverAnimator, giverRoot, scp, Scp939InteractionKind.Pet, PetDuration);
            });

            if (petter.isLocalPlayer)
            {
                Step("рука от первого лица", delegate
                {
                    Scp939FirstPersonHand.Show(scp, PetDuration, Scp939InteractionKind.Pet);
                });
            }
        }

        /// <summary>Клиентская часть свидания: затемнение, реплика и отметка, кто сидит за столом.</summary>
        private static void ClientApplyDate(Scp939InteractionBroadcast msg, ReferenceHub scp, ReferenceHub partner)
        {
            Step("затемнение", delegate { Scp939ScreenFade.Blink(DateTeleportDelay, null); });

            ClientDates.RemoveAll(p => p.Key == msg.ScpNetId || p.Value == msg.PartnerNetId);

            if (msg.Start)
            {
                ClientDates.Add(new KeyValuePair<uint, uint>(msg.ScpNetId, msg.PartnerNetId));
                // Реплика держится дольше обычной: пока экран возвращается, читать её нечем.
                SpeakStep(scp, msg.LineKey, false, 7f);
                return;
            }

            if (partner != null)
            {
                Step("встать со стула", delegate
                {
                    Transform root;
                    Scp939InteractionPoser.End(AnimatorOf(partner, out root));
                });
            }
            SpeakStep(scp, msg.LineKey, false, 6f);
        }

        private static void ClientApplySit(Scp939InteractionBroadcast msg, ReferenceHub partner)
        {
            if (partner == null)
            {
                return;
            }
            Step("посадка на стул", delegate
            {
                Transform root;
                Animator animator = AnimatorOf(partner, out root);
                if (!msg.Start)
                {
                    Scp939InteractionPoser.End(animator);
                    return;
                }
                Transform chair = Scp939DateSpot.ChairPlayer;
                if (chair != null)
                {
                    Scp939InteractionPoser.BeginSit(animator, root, chair.position, chair.forward);
                }
            });
        }

        private static void ClientApplyEat(Scp939InteractionBroadcast msg, ReferenceHub scp)
        {
            // Фрикадельки на её тарелке пропадают по одной: у всех клиентов одинаково, число шлёт сервер.
            Step("тарелка", delegate { Scp939DateSpot.SetMeatballsEaten(msg.Value); });
            SpeakStep(scp, msg.LineKey, false);

            if (scp == null || !msg.Start)
            {
                return;
            }

            Step("поза ужина", delegate
            {
                Transform plate = Scp939DateSpot.PlateScp;
                Transform modelRoot;
                Animator animator = AnimatorOf(scp, out modelRoot);
                if (plate != null)
                {
                    Scp939InteractionPoser.BeginLean(animator, modelRoot, plate.position, 2.8f);
                }
            });
            Step("звук ужина", delegate { Scp939Sfx.Slurp(scp.transform.position); });
        }

        // =================================================================== сервер

        private static void ServerProcessRequest(NetworkConnectionToClient conn, Scp939InteractionRequest msg)
        {
            Log("сервер получил запрос: " + (Scp939InteractionKind)msg.Kind);

            ReferenceHub sender;
            if (conn == null || conn.identity == null || !ReferenceHub.TryGetHub(conn.identity.gameObject, out sender))
            {
                Log("отклонено: не удалось определить отправителя");
                return;
            }

            ReferenceHub partner = sender;
            if (msg.PartnerNetId != 0u)
            {
                ReferenceHub chosen = ResolveServer(msg.PartnerNetId);
                if (chosen == null || !(chosen.roleManager.CurrentRole is IFpcRole))
                {
                    Log("отклонено: выбранный партнёр не найден");
                    return;
                }
                partner = chosen;
            }

            ReferenceHub scp = ResolveServer(msg.TargetNetId);
            var kind = (Scp939InteractionKind)msg.Kind;

            // Окончание свидания разбираем ДО проверки 939: её к этому моменту может уже не быть
            // (умерла, сменила роль, думми удалён), а игрок обязан суметь уехать из башни.
            if (kind == Scp939InteractionKind.Date && !msg.Start)
            {
                ServerEndDate(partner, scp);
                return;
            }

            if (!IsInteractable(scp) || scp == partner)
            {
                Log("отклонено: цель не найдена или это не 939");
                return;
            }

            // Отправитель сам должен быть живым — зритель из режима наблюдателя не командует.
            if (!(sender.roleManager.CurrentRole is IFpcRole))
            {
                return;
            }

            // Дистанцию проверяем у партнёра: он, а не режиссёр, должен стоять рядом с 939. На
            // свидании — не проверяем: там сидят за столом, а ужин заказывают из-за него.
            if (msg.Start && FindDate(scp) == null)
            {
                float maxRange = Range + ServerRangeTolerance;
                float actual = Vector3.Distance(scp.transform.position, partner.transform.position);
                if (actual > maxRange)
                {
                    Log("отклонено: далеко — " + actual.ToString("0.00"));
                    return;
                }
            }

            string error;
            ServerAct(scp, partner, kind, msg.Start, sender, msg.ItemSerial, null, out error);
            if (error != null)
            {
                Log("отклонено: " + error);
                if (kind == Scp939InteractionKind.Gift)
                    conn.Send(new Scp939GiftAnimationMessage { Stage = 4, Error = error });
            }
        }

        /// <summary>
        /// Общая точка входа для меню и команд режиссёра: что сделать, с кем и кто попросил.
        /// <paramref name="giftType"/> — подарок «из воздуха» (команда), иначе берётся из инвентаря
        /// партнёра по <paramref name="itemSerial"/>.
        /// </summary>
        public static bool ServerAct(ReferenceHub scp, ReferenceHub partner, Scp939InteractionKind kind, bool start,
                                     ReferenceHub source, ushort itemSerial, ItemType? giftType, out string error)
        {
            error = null;
            if (!Scp939ModSettings.Allows(kind)) { error = "This component is disabled in SCP > Mods > Character Mod Manager."; return false; }
            if (!NetworkServer.active)
            {
                error = "сервер не запущен";
                return false;
            }
            if (!IsInteractable(scp))
            {
                error = "цель — не 939";
                return false;
            }
            if (partner == null || !(partner.roleManager.CurrentRole is IFpcRole) || partner == scp)
            {
                error = "партнёр не найден";
                return false;
            }

            if (Scp939GiftTransfer.Busy(partner) || Scp939GiftTransfer.Busy(scp))
            {
                error = "Сначала закончите передачу текущего подарка.";
                return false;
            }

            // Пока она в ярости, нежности не принимаются — только режиссёрское «успокойся».
            if (Scp939Rage.IsRaging(scp) && kind != Scp939InteractionKind.Date)
            {
                error = "она в ярости";
                return false;
            }

            switch (kind)
            {
                case Scp939InteractionKind.Pet:
                    ServerPet(scp, partner);
                    return true;

                case Scp939InteractionKind.Hug:
                case Scp939InteractionKind.HandHold:
                    return ServerHold(scp, partner, kind, start, out error);

                case Scp939InteractionKind.Gift:
                    return ServerGiveGift(scp, partner, itemSerial, giftType, out error);

                case Scp939InteractionKind.Date:
                    if (!start)
                    {
                        return ServerEndDate(partner, scp);
                    }
                    return ServerStartDate(scp, partner, out error);

                case Scp939InteractionKind.Eat:
                    return ServerEat(scp, partner, out error);

                default:
                    error = "неизвестное действие";
                    return false;
            }
        }

        /// <summary>
        /// Поглаживание. Разовое действие: сессию не заводит, но объятие/прогулку прекращает — иначе
        /// разовая поза перезаписывала удерживаемую и через 3 с гасла, а сервер считал, что вы
        /// всё ещё обнимаетесь.
        ///
        /// Ласку нельзя «фармить»: четвёртая подряд за <see cref="PetSpamWindow"/> с — она ворчит и
        /// теряет симпатию, шестая — ярость. Это первая ступенька цепочки «перестарался → получил».
        /// </summary>
        private static void ServerPet(ReferenceHub scp, ReferenceHub partner)
        {
            if (Sessions.ContainsKey(scp))
            {
                EndSession(scp, null);
            }

            List<float> times;
            if (!PetTimes.TryGetValue(scp, out times))
            {
                times = new List<float>();
                PetTimes[scp] = times;
            }
            float now = Time.time;
            times.RemoveAll(t => now - t > PetSpamWindow);
            times.Add(now);

            DummyAimAt(scp, partner);

            if (times.Count >= 6)
            {
                times.Clear();
                BeginRage(scp, partner, "petspam.2");
                return;
            }

            if (times.Count >= 4)
            {
                Broadcast(scp, partner, Scp939InteractionKind.Pet, false, Scp939Lines.Pick("petspam", _lineCounter++), -6f);
                return;
            }

            ServerFreeHands(partner);
            FacePartner(scp, partner, Scp939InteractionKind.Pet);
            Broadcast(scp, partner, Scp939InteractionKind.Pet, true, Scp939Lines.Pick("pet", _lineCounter++), 4f);
        }

        private static bool ServerHold(ReferenceHub scp, ReferenceHub partner, Scp939InteractionKind kind, bool start,
                                       out string error)
        {
            error = null;
            Session existing;
            bool has = Sessions.TryGetValue(scp, out existing);

            if (!start)
            {
                if (has && existing.Partner == partner)
                {
                    EndSession(scp, Scp939Lines.Pick(existing.Kind == Scp939InteractionKind.Hug ? "hugend" : "holdend", _lineCounter++));
                    return true;
                }
                error = "нечего прекращать";
                return false;
            }

            if (has)
            {
                if (existing.Partner != partner)
                {
                    error = "её уже кто-то обнимает";
                    return false;
                }
                if (existing.Kind == kind)
                {
                    return true;   // уже идёт — повторный запрос не перезапускает позу
                }
                EndSession(scp, null);
            }

            Sessions[scp] = new Session { Scp = scp, Partner = partner, Kind = kind };
            ServerFreeHands(partner);
            if (kind == Scp939InteractionKind.Hug)
            {
                BringDummyClose(scp, partner);
            }
            FacePartner(scp, partner, kind);

            if (kind == Scp939InteractionKind.HandHold && Scp939ModCompatibility.IsDummy(scp))
            {
                // Вести можно только думми: живую 939 сервер за игрока не двигает.
                Scp939Companion.Attach(scp, partner);
            }

            Broadcast(scp, partner, kind, true,
                Scp939Lines.Pick(kind == Scp939InteractionKind.Hug ? "hug" : "hold", _lineCounter++),
                kind == Scp939InteractionKind.Hug ? 6f : 5f);
            return true;
        }

        /// <summary>
        /// Живого игрока к ней притягивает его же клиент (<see cref="PullTowards"/>), а думми сам не
        /// подойдёт — и 939 обнимала воздух на расстоянии вытянутых рук (кадр hug_side, 1.36 м).
        /// Ставим думми туда же, куда притянуло бы игрока.
        /// </summary>
        private static void BringDummyClose(ReferenceHub scp, ReferenceHub partner)
        {
            if (!Scp939ModCompatibility.IsDummy(partner) || !(partner.roleManager.CurrentRole is IFpcRole))
            {
                return;
            }
            Vector3 away = partner.transform.position - scp.transform.position;
            away.y = 0f;
            if (away.sqrMagnitude < 1e-4f)
            {
                away = scp.transform.forward;
            }
            Vector3 target = scp.transform.position + away.normalized * HugDistance;
            target.y = partner.transform.position.y;
            ((IFpcRole)partner.roleManager.CurrentRole).FpcModule.ServerOverridePosition(target, Vector3.zero);
            SyncDummyDestination(partner, target);
        }

        /// <summary>
        /// Убирает предмет в руках партнёра перед лаской: пока в руках предмет, риг вьюмодели занят
        /// его анимацией, и собственной руки в кадре не видно.
        /// </summary>
        private static void ServerFreeHands(ReferenceHub partner)
        {
            if (partner == null || partner.inventory == null || partner.inventory.CurItem.SerialNumber == 0)
            {
                return;
            }
            partner.inventory.ServerSelectItem(0);
        }

        /// <summary>
        /// Вручение подарка. Предмет забирается, только если она его приняла. Испугавший подарок при
        /// низкой симпатии доводит её до ярости.
        /// </summary>
        private static bool ServerGiveGift(ReferenceHub scp, ReferenceHub partner, ushort serial, ItemType? giftType,
                                           out string error)
        {
            error = null;
            if (Sessions.ContainsKey(scp))
            {
                error = "Сначала завершите объятие или отпустите руку SCP-939.";
                return false;
            }
            ItemType type;
            bool fromInventory = !giftType.HasValue;

            if (fromInventory)
            {
                ItemBase item;
                if (partner.inventory == null || !partner.inventory.UserInventory.Items.TryGetValue(serial, out item) || item == null)
                {
                    error = "предмета с serial=" + serial + " нет";
                    return false;
                }
                type = item.ItemTypeId;
            }
            else
            {
                type = giftType.Value;
            }

            DummyAimAt(scp, partner);
            return Scp939GiftTransfer.TryStart(scp, partner, serial, type, fromInventory,
                delegate { CompleteGift(scp, partner, type); }, out error);
        }

        private static void CompleteGift(ReferenceHub scp, ReferenceHub partner, ItemType type)
        {
            if (!IsInteractable(scp) || partner == null) return;
            float delta = Scp939Gifts.AffectionDelta(type);
            Broadcast(scp, partner, Scp939InteractionKind.Gift, true, Scp939Gifts.LineKeyFor(type, _lineCounter++), delta);

            if (!CinemaSuppressGiftRage && Scp939Gifts.MoodFor(type) == Scp939GiftMood.Scared &&
                Scp939Affection.Get(scp) < RageAffectionThreshold)
            {
                BeginRage(scp, partner, null);
            }
        }

        /// <summary>
        /// Ярость: реплика (в очередь после реплики на подарок), красная вспышка у жертвы и погоня
        /// с когтями у 939-думми. По окончании — «прости» и немного симпатии обратно, чтобы после
        /// сцены можно было продолжать свидание, а не начинать с нуля.
        /// </summary>
        public static void BeginRage(ReferenceHub scp, ReferenceHub target, string lineKey)
        {
            if (!IsInteractable(scp) || target == null)
            {
                return;
            }
            if (Sessions.ContainsKey(scp))
            {
                EndSession(scp, null);
            }
            Scp939Companion.Detach(scp);

            Broadcast(scp, target, Scp939InteractionKind.Rage, true, lineKey ?? Scp939Lines.Pick("rage", _lineCounter++), -10f,
                queued: true);
            Scp939Rage.Begin(scp, target, Scp939Rage.DefaultDuration, OnRageEnded);
        }

        private static void OnRageEnded(ReferenceHub scp)
        {
            if (!IsInteractable(scp))
            {
                return;
            }
            if (Scp939Affection.Get(scp) < 15f)
            {
                Scp939Affection.Set(scp, 15f);
            }
            Broadcast(scp, null, Scp939InteractionKind.Calm, true, Scp939Lines.Pick("calm", _lineCounter++), 0f);
        }

        public static void Calm(ReferenceHub scp)
        {
            if (Scp939Rage.IsRaging(scp))
            {
                // Destroy компонента сам вызовет OnRageEnded с репликой «прости».
                Object.Destroy(scp.GetComponent<Scp939Rage>());
            }
        }

        // ------------------------------------------------------------------- свидание

        /// <summary>
        /// Свидание: гасим экран, переносим обоих к столу. Телепорт — на сервере, затемнение — на
        /// клиентах, поэтому сначала всем уходит команда мигнуть, а перенос — через мгновение.
        /// </summary>
        private static bool ServerStartDate(ReferenceHub scp, ReferenceHub partner, out string error)
        {
            error = null;
            if (!Scp939DateSpot.Exists && !Scp939DateSpot.TryBuildNow())
            {
                error = "место для свидания не готово (комната Outside не создана или префаб не собран: SCP/939/Build Date Table)";
                Debug.LogWarning("[939] " + error);
                return false;
            }

            if (Sessions.ContainsKey(scp))
            {
                EndSession(scp, null);
            }
            Scp939Companion.Detach(scp);

            // Уже на свидании — только пересаживаем, ТОЧКИ ВОЗВРАТА НЕ ТРОГАЕМ: раньше повторный
            // запрос перезаписывал их на башню, и пара застревала наверху.
            DateRecord rec = FindDate(partner) ?? FindDate(scp);
            bool fresh = rec == null;
            if (fresh)
            {
                rec = new DateRecord
                {
                    Scp = scp,
                    Partner = partner,
                    ScpReturn = scp.transform.position,
                    PartnerReturn = partner.transform.position,
                    ScpYaw = YawOf(scp),
                    PartnerYaw = YawOf(partner)
                };
                ActiveDates.Add(rec);
            }
            else
            {
                rec.Scp = scp;
                rec.Partner = partner;
            }

            Broadcast(scp, partner, Scp939InteractionKind.Date, true, Scp939Lines.Pick("date", _lineCounter++),
                fresh ? 15f : 0f);

            float at = Time.time + DateTeleportDelay;
            bool sit = Scp939ModCompatibility.IsDummy(partner) && Scp939DateSpot.ChairPlayer != null;
            PendingMoves.Add(new PendingMove
            {
                At = at, Hub = partner, FaceTo = scp,
                Seat = sit ? Scp939DateSpot.ChairPlayer : Scp939DateSpot.SeatPlayer, Sit = sit
            });
            PendingMoves.Add(new PendingMove { At = at, Hub = scp, FaceTo = partner, Seat = Scp939DateSpot.SeatScp });
            return true;
        }

        /// <summary>
        /// Конец свидания: гасим экран и возвращаем обоих туда, откуда позвали. Запись ищется по
        /// партнёру, затем по 939 — чтобы закончить можно было из любого меню и после смерти.
        /// </summary>
        private static bool ServerEndDate(ReferenceHub who, ReferenceHub scpHint)
        {
            DateRecord rec = FindDate(who) ?? FindDate(scpHint);
            if (rec == null)
            {
                Log("окончание свидания: активного свидания нет");
                return false;
            }
            ActiveDates.Remove(rec);

            ReferenceHub scp = rec.Scp;
            if (rec.Partner != null && Scp939ModCompatibility.IsDummy(rec.Partner))
            {
                Broadcast(null, rec.Partner, Scp939InteractionKind.Sit, false, null, 0f);
            }
            BroadcastRaw(new Scp939InteractionBroadcast
            {
                ScpNetId = scp != null ? scp.netId : 0u,
                PartnerNetId = rec.Partner != null ? rec.Partner.netId : 0u,
                Kind = (byte)Scp939InteractionKind.Date,
                Start = false,
                LineKey = IsInteractable(scp) ? Scp939Lines.Pick("dateend", _lineCounter++) : null
            });

            float at = Time.time + DateTeleportDelay;
            if (rec.Partner != null)
            {
                PendingMoves.Add(new PendingMove { At = at, Hub = rec.Partner, Position = rec.PartnerReturn, Yaw = rec.PartnerYaw });
            }
            if (IsInteractable(scp))
            {
                PendingMoves.Add(new PendingMove { At = at, Hub = scp, Position = rec.ScpReturn, Yaw = rec.ScpYaw });
            }
            return true;
        }

        /// <summary>Ужин: она склоняется к тарелке, фрикаделек становится на одну меньше.</summary>
        private static bool ServerEat(ReferenceHub scp, ReferenceHub partner, out string error)
        {
            error = null;
            DateRecord rec = FindDate(scp) ?? FindDate(partner);
            if (rec == null)
            {
                error = "ужинать можно только на свидании";
                return false;
            }

            if (rec.MeatballsEaten >= MeatballsOnPlate)
            {
                Broadcast(scp, partner, Scp939InteractionKind.Eat, false, "eatempty.0", 0f, value: (byte)rec.MeatballsEaten);
                return true;
            }

            rec.MeatballsEaten++;
            Broadcast(scp, partner, Scp939InteractionKind.Eat, true, Scp939Lines.Pick("eat", _lineCounter++), 3f,
                value: (byte)rec.MeatballsEaten);
            return true;
        }

        private static DateRecord FindDate(ReferenceHub hub)
        {
            if (hub == null)
            {
                return null;
            }
            for (int i = 0; i < ActiveDates.Count; i++)
            {
                if (ActiveDates[i].Partner == hub || ActiveDates[i].Scp == hub)
                {
                    return ActiveDates[i];
                }
            }
            return null;
        }

        /// <summary>Горизонтальный угол взгляда — им же и возвращаем, чтобы не разворачивало.</summary>
        private static float YawOf(ReferenceHub hub)
        {
            var fpc = hub != null ? hub.roleManager.CurrentRole as IFpcRole : null;
            return fpc != null ? fpc.FpcModule.MouseLook.CurrentHorizontal : 0f;
        }

        /// <summary>
        /// Разворачивает партнёра как надо для действия. Живого игрока не трогаем: у него своя мышь.
        /// Думми сам повернуться не может — ему это делает сервер.
        /// </summary>
        private static void FacePartner(ReferenceHub scp, ReferenceHub partner, Scp939InteractionKind kind)
        {
            if (partner == null || scp == null || !Scp939ModCompatibility.IsDummy(partner))
            {
                return;
            }

            if (kind == Scp939InteractionKind.HandHold)
            {
                // Идут парой — значит смотрят в одну сторону, а не друг на друга.
                var partnerRole = partner.roleManager.CurrentRole as IFpcRole;
                if (partnerRole != null)
                {
                    partnerRole.FpcModule.MouseLook.LookAtDirection(scp.transform.forward);
                }
                return;
            }

            Scp939ModCompatibility.AimAt(partner, scp);
        }

        /// <summary>
        /// Развернуть 939 к партнёру — только если она думми. Живой 939 сервер камеру не дёргает:
        /// у хоста, играющего за неё, иначе каждый кадр уводило бы взгляд.
        /// </summary>
        private static void DummyAimAt(ReferenceHub scp, ReferenceHub target)
        {
            if (scp != null && target != null && Scp939ModCompatibility.IsDummy(scp))
            {
                Scp939ModCompatibility.AimAt(scp, target);
            }
        }

        private static void ServerTickMoves()
        {
            if (PendingMoves.Count == 0)
            {
                return;
            }

            float now = Time.time;
            for (int i = PendingMoves.Count - 1; i >= 0; i--)
            {
                PendingMove move = PendingMoves[i];
                if (now < move.At)
                {
                    continue;
                }
                PendingMoves.RemoveAt(i);

                if (move.Hub == null || !(move.Hub.roleManager.CurrentRole is IFpcRole))
                {
                    continue;
                }

                if (move.Seat != null)
                {
                    Seat(move.Hub, move.Seat);
                    if (move.Sit)
                    {
                        Broadcast(null, move.Hub, Scp939InteractionKind.Sit, true, null, 0f);
                    }
                }
                else
                {
                    Teleport(move.Hub, move.Position, move.Yaw);
                }

                // Разворачиваем ДРУГ НА ДРУГА, а не по направлению стула: стул стоит как поставлен.
                if (move.FaceTo != null && Scp939ModCompatibility.IsDummy(move.Hub))
                {
                    var fpc = (IFpcRole)move.Hub.roleManager.CurrentRole;
                    Vector3 dir = move.FaceTo.transform.position - move.Hub.transform.position;
                    // Цель ещё может стоять на старом месте — тогда смотрим вдоль стула.
                    if (move.Seat != null && dir.sqrMagnitude > 16f)
                    {
                        dir = move.Seat.forward;
                    }
                    dir.y = 0f;
                    if (dir.sqrMagnitude > 1e-4f)
                    {
                        fpc.FpcModule.MouseLook.LookAtDirection(dir.normalized);
                    }
                }
            }
        }

        /// <summary>Возврат в запомненную точку: та же механика, что и посадка, но без стула.</summary>
        private static void Teleport(ReferenceHub hub, Vector3 position, float yaw)
        {
            var fpc = hub.roleManager.CurrentRole as IFpcRole;
            if (fpc == null)
            {
                return;
            }
            fpc.FpcModule.ServerOverridePosition(position, Vector3.zero);
            SyncDummyDestination(hub, position);
            fpc.FpcModule.MouseLook.CurrentHorizontal = yaw;
        }

        /// <summary>
        /// Думми после переноса надо переписать и точку назначения мотора. Иначе в ней остаётся
        /// старая цель (от ярости, прогулки, «следуй»), и если она ближе ~5 м, мотор честно ведёт
        /// думми обратно — замер: 939 после ярости так и не села за стол.
        /// </summary>
        private static void SyncDummyDestination(ReferenceHub hub, Vector3 position)
        {
            if (hub == null || !Scp939ModCompatibility.IsDummy(hub))
            {
                return;
            }
            var fpc = hub.roleManager.CurrentRole as IFpcRole;
            if (fpc != null)
            {
                fpc.FpcModule.Motor.ReceivedPosition = new RelativePositioning.RelativePosition(position);
            }
        }

        private static void Seat(ReferenceHub hub, Transform seat)
        {
            var fpc = hub.roleManager.CurrentRole as IFpcRole;
            if (fpc == null)
            {
                return;
            }
            // Высоту считаем от геометрии капсулы: transform.position персонажа — это ЦЕНТР капсулы,
            // а не ступни. Посадка «ровно в точку» роняла участников сквозь пол на сотни метров.
            Vector3 target = seat.position;
            var controller = fpc.FpcModule.CharController;
            if (controller != null)
            {
                float capsuleBottom = controller.center.y - controller.height * 0.5f;
                target.y = Scp939DateSpot.GroundY(seat.position) - capsuleBottom + Scp939DateSpot.SeatClearance;
            }

            // ServerOverridePosition, а не Position: он сообщает клиенту, что рывок легальный.
            fpc.FpcModule.ServerOverridePosition(target, Vector3.zero);
            SyncDummyDestination(hub, target);
            fpc.FpcModule.MouseLook.LookAtDirection(seat.forward);
        }

        private static void EndSession(ReferenceHub scp, string lineKey)
        {
            // Партнёра называем и при отмене: клиенту иначе не с кого снять его позу.
            Session ending;
            ReferenceHub partner = Sessions.TryGetValue(scp, out ending) ? ending.Partner : null;

            Sessions.Remove(scp);
            Scp939Companion.Detach(scp);

            BroadcastRaw(new Scp939InteractionBroadcast
            {
                ScpNetId = scp != null ? scp.netId : 0u,
                PartnerNetId = partner != null ? partner.netId : 0u,
                Kind = (byte)Scp939InteractionKind.None,
                Start = false,
                LineKey = lineKey
            });
        }

        private static void ServerOnRoleChanged(ReferenceHub hub)
        {
            // Сессии, где он 939 или партнёр.
            Doomed.Clear();
            foreach (KeyValuePair<ReferenceHub, Session> pair in Sessions)
            {
                if (pair.Key == hub || pair.Value.Partner == hub)
                {
                    Doomed.Add(pair.Key);
                }
            }
            for (int i = 0; i < Doomed.Count; i++)
            {
                EndSession(Doomed[i], null);
            }

            Scp939Rage.Cancel(hub);
            foreach (ReferenceHub other in ReferenceHub.AllHubs)
            {
                Scp939Rage rage = other != null ? other.GetComponent<Scp939Rage>() : null;
                if (rage != null && rage.Target == hub)
                {
                    Scp939Rage.Cancel(other);
                }
            }

            if (!IsInteractable(hub))
            {
                Scp939Affection.Forget(hub);
                PetTimes.Remove(hub);
            }

            PendingMoves.RemoveAll(m => m.Hub == hub);

            for (int i = ActiveDates.Count - 1; i >= 0; i--)
            {
                DateRecord rec = ActiveDates[i];
                if (rec.Partner == hub)
                {
                    // Партнёр умер или сменил роль: свидание окончено. Его НЕ телепортируем (это уже
                    // другая роль в другой точке), а 939 возвращаем на место.
                    ActiveDates.RemoveAt(i);
                    BroadcastRaw(new Scp939InteractionBroadcast
                    {
                        ScpNetId = rec.Scp != null ? rec.Scp.netId : 0u,
                        PartnerNetId = hub.netId,
                        Kind = (byte)Scp939InteractionKind.Date,
                        Start = false
                    });
                    if (IsInteractable(rec.Scp))
                    {
                        PendingMoves.Add(new PendingMove
                        {
                            At = Time.time + DateTeleportDelay, Hub = rec.Scp, Position = rec.ScpReturn, Yaw = rec.ScpYaw
                        });
                    }
                }
                else if (rec.Scp == hub)
                {
                    // 939 больше нет — партнёр всё ещё может уехать через «Закончить свидание».
                    rec.Scp = null;
                }
            }
        }

        private static void ServerTick()
        {
            ServerTickMoves();

            if (Sessions.Count == 0)
            {
                return;
            }

            Doomed.Clear();
            foreach (KeyValuePair<ReferenceHub, Session> pair in Sessions)
            {
                Session s = pair.Value;
                float limit = s.Kind == Scp939InteractionKind.HandHold ? HandHoldBreakDistance : BreakDistance;
                if (s.Scp == null || s.Partner == null || !IsInteractable(s.Scp) ||
                    !(s.Partner.roleManager.CurrentRole is IFpcRole) ||
                    (s.Scp.transform.position - s.Partner.transform.position).sqrMagnitude > limit * limit)
                {
                    Doomed.Add(pair.Key);
                    continue;
                }

                // В объятии 939 постоянно повёрнута к партнёру; на прогулке этим занимается Companion.
                if (s.Kind == Scp939InteractionKind.Hug)
                {
                    DummyAimAt(s.Scp, s.Partner);
                }
            }

            for (int i = 0; i < Doomed.Count; i++)
            {
                EndSession(Doomed[i], null);
            }
        }

        // ------------------------------------------------------------------- рассылка

        private static void Broadcast(ReferenceHub scp, ReferenceHub partner, Scp939InteractionKind kind, bool start,
                                      string lineKey, float affectionDelta, bool queued = false, byte value = 0)
        {
            var msg = new Scp939InteractionBroadcast
            {
                ScpNetId = scp != null ? scp.netId : 0u,
                PartnerNetId = partner != null ? partner.netId : 0u,
                Kind = (byte)kind,
                Start = start,
                LineKey = lineKey,
                Queued = queued,
                Value = value
            };

            if (scp != null && Mathf.Abs(affectionDelta) > 0.01f)
            {
                int levelShift = Scp939Affection.Change(scp, affectionDelta);
                FillAffection(ref msg, scp, affectionDelta, levelShift);
            }
            BroadcastRaw(msg);
        }

        private static void FillAffection(ref Scp939InteractionBroadcast msg, ReferenceHub scp, float delta, int levelShift)
        {
            float value = Scp939Affection.Get(scp);
            msg.HasAffection = true;
            msg.Affection = (byte)Mathf.RoundToInt(value);
            msg.AffectionDelta = (sbyte)Mathf.Clamp(Mathf.RoundToInt(delta), -100, 100);
            if (levelShift > 0)
            {
                msg.LevelLineKey = "love.up." + Scp939Affection.LevelOf(value);
            }
            else if (levelShift < 0)
            {
                msg.LevelLineKey = "love.down.0";
            }
        }

        private static void BroadcastRaw(Scp939InteractionBroadcast msg)
        {
            NetworkServer.SendToReady(msg);
        }

        // ------------------------------------------------------------------- для команд режиссёра

        /// <summary>Сказать реплику по ключу (команда <c>939 line</c>).</summary>
        public static bool ServerSayLine(ReferenceHub scp, string key)
        {
            if (!IsInteractable(scp) || !Scp939Lines.Exists(key))
            {
                return false;
            }
            Broadcast(scp, null, Scp939InteractionKind.Line, true, key, 0f);
            return true;
        }

        /// <summary>Свободный текст на экран (команда <c>939 say</c>).</summary>
        public static void ServerSayText(ReferenceHub scp, string text)
        {
            BroadcastRaw(new Scp939InteractionBroadcast
            {
                ScpNetId = scp != null ? scp.netId : 0u,
                Kind = (byte)Scp939InteractionKind.Say,
                Start = true,
                Text = text
            });
        }

        /// <summary>Выставить симпатию и показать шкалу (команда <c>939 love</c>).</summary>
        public static void ServerSetAffection(ReferenceHub scp, float value)
        {
            float before = Scp939Affection.Get(scp);
            Scp939Affection.Set(scp, value);
            var msg = new Scp939InteractionBroadcast
            {
                ScpNetId = scp.netId,
                Kind = (byte)Scp939InteractionKind.Affection,
                Start = true
            };
            FillAffection(ref msg, scp, value - before, 0);
            BroadcastRaw(msg);
        }

        /// <summary>Разослать настройки интерфейса мода (команда <c>939 ui</c>).</summary>
        public static void ServerSetUi(byte flags, bool reloadVoice)
        {
            Scp939Ui.Flags = flags;
            BroadcastRaw(new Scp939InteractionBroadcast
            {
                Kind = (byte)Scp939InteractionKind.Ui,
                Start = !reloadVoice,
                Value = flags
            });
        }

        // =================================================================== общее

        private static ReferenceHub ResolveServer(uint netId)
        {
            NetworkIdentity identity;
            ReferenceHub hub;
            if (netId == 0u || !NetworkServer.spawned.TryGetValue(netId, out identity) || identity == null ||
                !ReferenceHub.TryGetHub(identity.gameObject, out hub))
            {
                return null;
            }
            return hub;
        }

        /// <summary>Аниматор модели игрока и корень этой модели (он же — точка на полу под ней).</summary>
        private static Animator AnimatorOf(ReferenceHub hub, out Transform modelRoot)
        {
            modelRoot = null;
            var fpc = hub != null ? hub.roleManager.CurrentRole as IFpcRole : null;
            if (fpc == null)
            {
                return null;
            }
            var model = fpc.FpcModule.CharacterModelInstance;
            if (model == null)
            {
                return null;
            }
            modelRoot = model.transform;
            return model.GetComponentInChildren<Animator>();
        }
    }
}
