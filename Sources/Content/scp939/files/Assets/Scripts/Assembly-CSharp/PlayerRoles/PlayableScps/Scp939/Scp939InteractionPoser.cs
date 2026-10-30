using System.Collections.Generic;
using UnityEngine;

namespace PlayerRoles.PlayableScps.Scp939
{
    /// <summary>Что именно 939 сейчас делает с партнёром. Значения идут по сети — не перенумеровывать.</summary>
    public enum Scp939InteractionKind : byte
    {
        None = 0,
        Hug = 1,
        HandHold = 2,
        Date = 3,
        Gift = 4,
        Pet = 5,
        Eat = 6,
        Rage = 7,
        // 8 — была «мимикрия» (повтор фразы игрока), убрана 2026-09-28 по решению пользователя.
        // Номер не переиспользовать: значения идут по сети.
        Say = 9,
        Ui = 10,
        Line = 11,
        Sit = 12,
        Calm = 13,
        Affection = 14
    }

    /// <summary>
    /// Процедурные позы взаимодействия 939: доворачивает кости после аниматора, в LateUpdate.
    ///
    /// Почему не обычный анимационный клип. Аватар 939 — Humanoid, и любой импортированный клип
    /// проходит через мышечный ретаргет. Риг 939 сильно нечеловеческий, а узел «939Rig» в префабе
    /// вдобавок отмасштабирован в 20.19 раза, поэтому ретаргет отдавал bodyPosition около
    /// (130, 995, -39) и разносил модель в ~16 раз. Ни масштаб экспорта из Blender, ни globalScale
    /// импорта, ни флаги Root Transform на это не влияли — правится только пересборкой аватара,
    /// а она сломала бы все штатные анимации 939. Повороты костей от масштаба узлов не зависят.
    ///
    /// Поза считается КАЖДЫЙ КАДР от реального положения партнёра, а не по заранее записанным точкам:
    /// иначе руки смыкаются в воздухе рядом с игроком, и объятие со стороны не читается.
    /// </summary>
    // Позже обычных LateUpdate: модели 14.x сами двигают таз после аниматора (Human14CharacterModel),
    // и поза «сидя» должна ложиться поверх этой правки, а не наоборот.
    [DefaultExecutionOrder(500)]
    public class Scp939InteractionPoser : MonoBehaviour
    {
        // Объятие: куда уходят её шея и голова относительно головы партнёра (м). Поля, а не
        // константы, — подбираются вживую по кадру от первого лица.
        // Живая настройка позы объятия (меняется из execute_code без перезапуска Play).
        public static float HugLean = 0.12f;
        public static float HoldGiverOut = 0.26f;
        public static float HoldGiverFwd = 0.08f;
        public static float HoldGiverUp = 0.86f;
        public static float HoldHandHeight = 0.98f;
        public static float HoldForward = 0.10f;
        public static float HugStomachUp = 0.78f;
        public static float HugStomachFwd = 0.02f;
        public static float HugChestUp = 1.32f;
        public static float HugChestFwd = 0.03f;
        // Проверено с двух сторон в Gate B 08.10: обе кисти обхватывают спину, локти согнуты.
        public static float HugGiverFwd = 0.50f;
        public static float HugGiverSide = 0.17f;
        public static float HugGiverUp = 1.52f;
        public static float HugGiverDrop = 1.38f;

        public static float HugNeckSide = 0.22f;
        public static float HugNeckDrop = 0.05f;
        public static float HugNeckWeight = 0f;
        public static float HugHeadSide = 0.26f;
        public static float HugHeadPast = 0.35f;
        public static float HugHeadDrop = 0.10f;

        /// <summary>Высота сиденья стула от пола (Chair_LP1 в комплекте свидания).</summary>
        private const float SeatHeight = 0.50f;

        /// <summary>Время плавного входа в позу и выхода из неё.</summary>
        private const float BlendTime = 0.45f;

        /// <summary>
        /// Доля роста игрока (до камеры), на которой находится его грудь. Все смещения задаются
        /// долями от измеренного роста, а не метрами: мир этого проекта заметно мельче настоящего —
        /// у 939 голова на 0.69 над ногами, у игрока камера на 0.63, — и «метровые» цели улетали
        /// выше головы, отчего 939 вздёргивала лапы вверх вместо объятия.
        /// </summary>
        private const float PartnerChestFraction = 0.75f;

        /// <summary>
        /// Доля роста игрока, на которой висит его опущенная кисть. Ниже середины намеренно: у игрока
        /// в первом лице нет своей модели рук, и если брать точку выше, лапа 939 оказывается прямо
        /// перед камерой и занимает пол-экрана.
        /// </summary>
        private const float PartnerHandFraction = 0.46f;

        private readonly Dictionary<string, Transform> _bones = new Dictionary<string, Transform>();
        private ReferenceHub _partner;
        private Scp939InteractionKind _kind;
        private Transform _root;
        private Animator _animator;
        private float _height = 1f;
        private float _weight;
        private bool _active;
        private bool _ready;

        /// <summary>Когда разовая поза должна закончиться сама. Отрицательное — поза удерживаемая.</summary>
        private float _endsAt = -1f;

        /// <summary>Позируем гладящего (человека), а не 939. У них разные риги.</summary>
        private bool _isGiver;

        /// <summary>Поза по точке в мире, а не по партнёру: ужин (тарелка) и сидение (стул).</summary>
        private bool _pointMode;
        private Vector3 _point;
        private Vector3 _facing;

        /// <summary>
        /// Начинает (или переключает) позу взаимодействия с партнёром.
        ///
        /// <paramref name="duration"/> = 0 — поза удерживается, пока её не завершат явно (объятие,
        /// держание за руку). Больше нуля — разовая поза, которая сама закончится через это время
        /// (поглаживание): держать её кнопкой незачем, а забыть выключить — легко.
        /// </summary>
        public static void Begin(Animator animator, Transform modelRoot, ReferenceHub partner,
                                 Scp939InteractionKind kind, float duration = 0f)
        {
            if (animator == null || modelRoot == null || partner == null || kind == Scp939InteractionKind.None)
            {
                return;
            }

            Scp939InteractionPoser poser = Get(animator);
            poser.Setup(animator, modelRoot);
            poser._endsAt = duration > 0f ? Time.time + duration : -1f;
            poser._partner = partner;
            poser._kind = kind;
            // Флаг обязательно сбрасывать: компонент остаётся на модели, а модели берутся из пула и
            // переиспользуются. Один раз побывав «гладящим», модель иначе навсегда осталась бы им и
            // позировалась по чужому ригу — то есть внешне никак.
            poser._isGiver = false;
            poser._pointMode = false;
            poser._active = true;
            poser.enabled = true;
        }

        /// <summary>
        /// Поза для того, кто гладит: рука игрока тянется к голове 939.
        ///
        /// Отдельным входом, потому что человеческая модель на ДРУГОМ риге — Mixamo
        /// (mixamorig:RightArm / RightForeArm / RightHand), а не Rigify с костями DEF-*, как у 939.
        /// Сам IK общий, меняются только имена костей.
        ///
        /// Важно: свою модель локальный игрок не видит (она скрывается для владельца), так что в
        /// первом лице эта рука не появится — её увидят другие игроки и камера со стороны.
        /// </summary>
        public static void BeginGiver(Animator animator, Transform modelRoot, ReferenceHub target,
                                      Scp939InteractionKind kind, float duration)
        {
            if (animator == null || modelRoot == null || target == null)
            {
                return;
            }

            Scp939InteractionPoser poser = Get(animator);
            poser.Setup(animator, modelRoot);
            poser._endsAt = duration > 0f ? Time.time + duration : -1f;
            poser._partner = target;
            poser._kind = kind;
            poser._isGiver = true;
            poser._pointMode = false;
            poser._active = true;
            poser.enabled = true;
        }

        /// <summary>
        /// 939 склоняется к точке — к своей тарелке за ужином — и «жуёт»: голова ритмично
        /// опускается и поднимается. Разовая поза на <paramref name="duration"/> секунд.
        /// </summary>
        public static void BeginLean(Animator animator, Transform modelRoot, Vector3 point, float duration)
        {
            if (animator == null || modelRoot == null)
            {
                return;
            }
            Scp939InteractionPoser poser = Get(animator);
            poser.Setup(animator, modelRoot);
            poser._endsAt = Time.time + Mathf.Max(0.5f, duration);
            poser._partner = null;
            poser._kind = Scp939InteractionKind.Eat;
            poser._isGiver = false;
            poser._pointMode = true;
            poser._point = point;
            poser._active = true;
            poser.enabled = true;
        }

        /// <summary>
        /// Человек садится на стул: таз опускается на сиденье, бёдра вперёд, голени вниз.
        /// Только для humanoid-моделей (люди 14.x и старые Mixamo через аватар). Удерживаемая поза —
        /// снимается <see cref="End"/> в конце свидания.
        /// </summary>
        public static void BeginSit(Animator animator, Transform modelRoot, Vector3 chair, Vector3 facing)
        {
            if (animator == null || modelRoot == null)
            {
                return;
            }
            Scp939InteractionPoser poser = Get(animator);
            poser.Setup(animator, modelRoot);
            poser._endsAt = -1f;
            poser._partner = null;
            poser._kind = Scp939InteractionKind.Sit;
            poser._isGiver = true;
            poser._pointMode = true;
            poser._point = chair;
            facing.y = 0f;
            poser._facing = facing.sqrMagnitude > 1e-4f ? facing.normalized : modelRoot.forward;
            poser._active = true;
            poser.enabled = true;
        }

        /// <summary>
        /// Модели берутся из пула: при смене роли модель выключается и позже достаётся другому
        /// игроку. Без сброса поза «оживала» у следующей 939 — с чужим партнёром и старым весом.
        /// </summary>
        private void OnDisable()
        {
            _active = false;
            _weight = 0f;
            _partner = null;
            _kind = Scp939InteractionKind.None;
            _endsAt = -1f;
            _pointMode = false;
        }

        /// <summary>Завершает позу — вес плавно уходит в 0, после чего компонент отключается.</summary>
        public static void End(Animator animator)
        {
            if (animator == null)
            {
                return;
            }
            var poser = animator.GetComponent<Scp939InteractionPoser>();
            if (poser != null)
            {
                poser._active = false;
                foreach(var pair in poser._gripRest)if(pair.Key!=null)pair.Key.localRotation=pair.Value;
            }
        }

        private static Scp939InteractionPoser Get(Animator animator)
        {
            var poser = animator.GetComponent<Scp939InteractionPoser>();
            return poser != null ? poser : animator.gameObject.AddComponent<Scp939InteractionPoser>();
        }

        private void Setup(Animator animator, Transform modelRoot)
        {
            _root = modelRoot;
            _animator = animator;

            if (_ready)
            {
                return;
            }
            foreach (Transform t in animator.GetComponentsInChildren<Transform>(true))
            {
                if (!_bones.ContainsKey(t.name))
                {
                    _bones[t.name] = t;
                }
            }

            // Рост меряется один раз, до первого доворота костей, — потом поза его исказит.
            // Имя кости головы зависит от рига: у 939 это Rigify (DEF-Head), у старых людей Mixamo,
            // а у моделей 14.x — своё, поэтому у них голова берётся из humanoid-аватара.
            // Пока проверялся только DEF-Head, для человека рост падал в 1.0, и точка сцепления рук
            // у него считалась почти на 20 см выше, чем у неё, — ладони не встречались.
            Transform head;
            if (!_bones.TryGetValue("DEF-Head", out head) && !_bones.TryGetValue("mixamorig:Head", out head))
            {
                head = HumanBone(HumanBodyBones.Head);
            }
            _height = head != null
                ? Mathf.Max(0.2f, head.position.y - modelRoot.position.y)
                : 1f;

            _ready = true;
        }

        /// <summary>
        /// Кость по humanoid-аватару. Нужна для человеческих моделей 14.x: у них риг называется
        /// не как у старых (mixamorig:*), поэтому поиск по имени на них молча не находил ничего —
        /// рука партнёра просто не двигалась, и «поглаживание» со стороны выглядело несработавшим.
        /// </summary>
        private Transform HumanBone(HumanBodyBones bone)
        {
            return Scp939ModCompatibility.FindBone(_animator,bone);
        }

        /// <summary>
        /// Цепочка «плечо → предплечье → кисть» у человека, независимо от того, как названы кости.
        /// Сначала humanoid-аватар (модели 14.x), потом имена Mixamo (старые модели).
        /// </summary>
        private bool TryHumanArm(bool right, out Transform upper, out Transform lower, out Transform hand)
        {
            upper = HumanBone(right ? HumanBodyBones.RightUpperArm : HumanBodyBones.LeftUpperArm);
            lower = HumanBone(right ? HumanBodyBones.RightLowerArm : HumanBodyBones.LeftLowerArm);
            hand = HumanBone(right ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
            if (upper != null && lower != null && hand != null)
            {
                return true;
            }

            string prefix = right ? "mixamorig:Right" : "mixamorig:Left";
            return _bones.TryGetValue(prefix + "Arm", out upper) &&
                   _bones.TryGetValue(prefix + "ForeArm", out lower) &&
                   _bones.TryGetValue(prefix + "Hand", out hand);
        }

        /// <summary>Кисть человека нужной стороны — по аватару или по имени.</summary>
        private Transform HumanHand(bool right)
        {
            Transform bone = HumanBone(right ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
            if (bone != null)
            {
                return bone;
            }
            _bones.TryGetValue(right ? "mixamorig:RightHand" : "mixamorig:LeftHand", out bone);
            return bone;
        }

        private void LateUpdate()
        {
            if (!_ready)
            {
                enabled = false;
                return;
            }

            // Партнёр мог выйти из игры или сменить роль прямо посреди объятия.
            if (_active && !_pointMode && (_partner == null || _partner.roleManager.CurrentRole == null))
            {
                _active = false;
            }

            // Разовая поза заканчивается сама.
            if (_active && _endsAt > 0f && Time.time >= _endsAt)
            {
                _active = false;
                _endsAt = -1f;
            }

            _weight = Mathf.MoveTowards(_weight, _active ? 1f : 0f, Time.deltaTime / BlendTime);
            if (_weight <= 0f)
            {
                if (!_active)
                {
                    _partner = null;
                    _kind = Scp939InteractionKind.None;
                    enabled = false;
                }
                return;
            }

            if (_pointMode)
            {
                if (_kind == Scp939InteractionKind.Eat)
                {
                    PoseLean(_weight);
                }
                else if (_kind == Scp939InteractionKind.Sit)
                {
                    PoseSit(_weight);
                }
                SyncDuplicateRig();
                return;
            }

            if (_partner == null)
            {
                return;
            }

            switch (_kind)
            {
                case Scp939InteractionKind.Hug:
                    if (_isGiver)
                    {
                        PoseHugGiver(_weight);
                    }
                    else
                    {
                        PoseHug(_weight);
                    }
                    break;
                case Scp939InteractionKind.HandHold:
                    if (_isGiver)
                    {
                        PoseHandHoldGiver(_weight);
                    }
                    else
                    {
                        PoseHandHold(_weight);
                    }
                    break;
                case Scp939InteractionKind.Pet:
                    if (_isGiver)
                    {
                        PosePetGiver(_weight);
                    }
                    else
                    {
                        PosePet(_weight);
                    }
                    break;
            }

            if (_isGiver)
            {
                SyncDuplicateRig();
            }
        }

        /// <summary>
        /// Точка, в которой сцепляются кисти при держании за руку: между участниками, на высоте
        /// опущенной руки. Считается одинаково для обоих, поэтому их ладони встречаются, а не тянутся
        /// каждая в своё место.
        /// </summary>
        private static Vector3 ClaspPoint(Vector3 scpFeet, Vector3 humanFeet, float humanHeight)
        {
            Vector3 mid = (scpFeet + humanFeet) * 0.5f;
            // Высота опущенной руки у человека ростом 1.8 м — ~0.8 м от пола. Раньше считалось долей «роста до
            // камеры» (0.63 от центра капсулы) и выходило 0.3 м — у колена; мир здесь нормального масштаба.
            return new Vector3(mid.x, humanFeet.y + HoldHandHeight, mid.z);
        }

        /// <summary>
        /// Ноги партнёра — корень его модели, а НЕ transform хаба: хаб находится в центре капсулы,
        /// почти на метр выше пола, и смешивать эти две базы в одном расчёте нельзя.
        /// </summary>
        private Vector3 PartnerFeet()
        {
            var fpc = _partner.roleManager.CurrentRole as PlayerRoles.FirstPersonControl.IFpcRole;
            if (fpc != null && fpc.FpcModule.CharacterModelInstance != null)
            {
                return fpc.FpcModule.CharacterModelInstance.transform.position;
            }
            return _partner.transform.position;
        }

        /// <summary>Рост партнёра до камеры — опора для его собственных пропорций.</summary>
        private float PartnerHeight()
        {
            return Mathf.Max(0.2f, _partner.PlayerCameraReference.position.y - _partner.transform.position.y);
        }

        /// <summary>Объятие: привстать, склониться к партнёру и сомкнуть руки у него за спиной.</summary>
        private void PoseHug(float weight)
        {
            if (_root == null)
            {
                return;
            }

            float h = _height;
            Vector3 feet = _root.position;
            Vector3 partnerFeet = _partner.transform.position;
            Vector3 chest = partnerFeet + Vector3.up * (PartnerHeight() * PartnerChestFraction);
            Vector3 partnerHead = _partner.PlayerCameraReference.position;

            Vector3 toPartner = chest - feet;
            toPartner.y = 0f;
            if (toPartner.sqrMagnitude < 1e-6f)
            {
                return;
            }
            toPartner.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, toPartner);

            // Корпус выпрямляется почти вертикально. Наклон «к партнёру» намеренно минимальный:
            // 939 и игрок здесь примерно одного роста (0.69 против 0.63 до камеры), и наклон
            // вперёд загонял её грудь прямо в камеру обнимаемого — от первого лица это выглядело
            // как утыкание в текстуру, а не объятие.
            // Стоит прямо, грудью к партнёру, чуть подавшись к нему (как на обычном объятии).
            AimDir("DEF-Stomach", "DEF-Chest", Vector3.up + toPartner * HugLean, weight);
            AimDir("DEF-Chest", "DEF-Neck", Vector3.up + toPartner * HugLean * 1.5f, weight);
            // Голова ложится партнёру НА ПРАВОЕ ПЛЕЧО и уходит за него (2026-09-28). Прежний вариант
            // «голова выше и чуть вбок» от первого лица давал её грудь во весь экран — пользователь
            // назвал объятие странным. Так, как обнимают на самом деле: её щека у твоего уха, а в
            // кадре — её шея и плечо сбоку и комната за ней. -right у 939 = правая сторона партнёра.
            Aim("DEF-Chest", "DEF-Neck", partnerHead - right * HugNeckSide - Vector3.up * HugNeckDrop, weight * HugNeckWeight);
            Aim("DEF-Neck", "DEF-Head", partnerHead + toPartner * HugHeadPast - right * HugHeadSide - Vector3.up * HugHeadDrop, weight);

            // Кисти ставятся ВПЛОТНУЮ к спине партнёра, по разные стороны от позвоночника.
            // Радиус тела — от роста партнёра, чтобы не зависеть от масштаба мира.
            float bodyRadius = PartnerHeight() * 0.16f;
            Vector3 back = chest + toPartner * (bodyRadius * 1.05f);
            Vector3 handL = back - right * (bodyRadius * 0.45f) + Vector3.up * (h * 0.05f);
            Vector3 handR = back + right * (bodyRadius * 0.45f) - Vector3.up * (h * 0.09f);

            // Локти разводятся наружу и вниз — иначе решение IK складывает руку внутрь, сквозь тело.
            Vector3 poleL = (-right - Vector3.up * 0.6f).normalized;
            Vector3 poleR = (right - Vector3.up * 0.6f).normalized;

            // Левая рука обхватывает сверху, правая — снизу по пояснице, чтобы не пересекались.
            AimIk("DEF-UpperArm.L", "DEF-LowerArm.L", "DEF-Hand.L", handL, poleL, weight);
            AimIk("DEF-UpperArm.R", "DEF-LowerArm.R", "DEF-Hand.R", handR, poleR, weight);

            // Кисти доворачиваются вдоль спины, к позвоночнику, — пальцы ложатся на партнёра.


            // Отдельно доворачиваем сами ладони: без этого их поворот остаётся произвольным и когти
            // торчат врастопырку. Пальцы ведём поперёк спины и чуть вниз — ладонь ложится на партнёра.
            Aim("DEF-Hand.L", "DEF-Finger.B.L.001", back + right * (bodyRadius * 0.9f) - Vector3.up * (h * 0.04f), weight);
            Aim("DEF-Hand.R", "DEF-Finger.B.R.001", back - right * (bodyRadius * 0.9f) - Vector3.up * (h * 0.04f), weight);
        }

        /// <summary>
        /// Объятие со стороны человека: обе руки обхватывают 939 за плечи и верх спины. Раньше герой стоял
        /// с опущенными руками, и со стороны это выглядело как «она на нём висит» (кадры 2026-09-29).
        /// Цели считаются от её реальных костей, поэтому руки следуют за её наклоном.
        /// </summary>
        private void PoseHugGiver(float weight)
        {
            if (_root == null)
            {
                return;
            }

            Vector3 center;
            Transform chestBone;
            if (TryFindPartnerBone("DEF-Chest", out chestBone))
            {
                center = chestBone.position;
            }
            else
            {
                center = _partner.transform.position + Vector3.up * (PartnerHeight() * 0.6f);
            }

            Vector3 toScp = center - _root.position;
            toScp.y = 0f;
            if (toScp.sqrMagnitude < 1e-6f)
            {
                return;
            }
            toScp.Normalize();
            Vector3 side = Vector3.Cross(Vector3.up, toScp);   // вправо от героя, если он смотрит на неё

            // Цели — в ЕГО осях (от его ног): кисти сходятся у неё за спиной на досягаемой глубине, одна выше,
            // другая ниже. Раньше цель бралась от её грудной кости + вглубь — дальше длины руки, и IK вытягивал
            // обе руки прямо вперёд, «как зомби» (замер 2026-09-30: кисти на 1.3 м высоты, руки в струну).
            Vector3 feet = _root.position;
            Vector3 handRight = feet + toScp * HugGiverFwd + side * HugGiverSide + Vector3.up * HugGiverUp;
            Vector3 handLeft = feet + toScp * HugGiverFwd - side * HugGiverSide + Vector3.up * HugGiverDrop;

            Transform upper, lower, hand;
            if (TryHumanArm(true, out upper, out lower, out hand))
            {
                SolveIk(upper, lower, hand, handRight, (side * 1.0f - Vector3.up * 0.35f - toScp * 0.2f).normalized, weight);
                PointFingers(hand, true, -side + toScp * 0.3f, weight);
            }
            if (TryHumanArm(false, out upper, out lower, out hand))
            {
                SolveIk(upper, lower, hand, handLeft, (-side * 1.0f - Vector3.up * 0.35f - toScp * 0.2f).normalized, weight);
                PointFingers(hand, false, side + toScp * 0.3f, weight);
            }
        }

        private List<KeyValuePair<Transform, Transform>> _duplicatePairs;

        /// <summary>
        /// У моделей 14.x кисти — отдельные меши («hand sub»), скиннутые на ДУБЛИКАТ скелета (кости с суффиксом
        /// «__H14S…»). Аниматор ведёт оба скелета, а наш IK — только основной: рукава обнимали, а голые кисти
        /// оставались у бедра (кадры 2026-09-30). Копируем позу основного скелета в дубликат по имени.
        /// </summary>
        public void SyncDuplicateRig()
        {
            if (_animator == null)
            {
                return;
            }
            if (_duplicatePairs == null)
            {
                _duplicatePairs = new List<KeyValuePair<Transform, Transform>>();
                var main = new Dictionary<string, Transform>();
                Transform[] all = _animator.GetComponentsInChildren<Transform>(true);
                foreach (Transform t in all)
                {
                    if (!t.name.Contains("__H14S") && !main.ContainsKey(t.name))
                    {
                        main[t.name] = t;
                    }
                }
                foreach (Transform t in all)
                {
                    int cut = t.name.IndexOf("__H14S", System.StringComparison.Ordinal);
                    Transform source;
                    if (cut > 0 && main.TryGetValue(t.name.Substring(0, cut), out source))
                    {
                        _duplicatePairs.Add(new KeyValuePair<Transform, Transform>(source, t));
                    }
                }
            }
            foreach (var pair in _duplicatePairs)
            {
                if (pair.Key != null && pair.Value != null)
                {
                    pair.Value.SetPositionAndRotation(pair.Key.position, pair.Key.rotation);
                }
            }
        }

        /// <summary>Повернуть кисть человека так, чтобы пальцы смотрели в заданную сторону (кратчайшим поворотом).</summary>
        private void PointFingers(Transform hand, bool right, Vector3 dir, float weight)
        {
            Transform middle = HumanBone(right ? HumanBodyBones.RightMiddleProximal : HumanBodyBones.LeftMiddleProximal);
            if (middle == null || dir.sqrMagnitude < 1e-6f)
            {
                return;
            }
            Vector3 current = middle.position - hand.position;
            if (current.sqrMagnitude < 1e-8f)
            {
                return;
            }
            Quaternion delta = Quaternion.FromToRotation(current, dir.normalized);
            hand.rotation = Quaternion.Slerp(hand.rotation, delta * hand.rotation, weight);
        }

        /// <summary>
        /// Поглаживание: 939 подставляет голову под ладонь игрока и утыкается в неё.
        ///
        /// Позируется только ОНА: у игрока в первом лице нет своей модели рук, поэтому всё, что видно,
        /// — её движение навстречу. Голова наводится не в камеру, а чуть ниже и перед ней, туда, где
        /// была бы вытянутая ладонь.
        /// </summary>
        private void PosePet(float weight)
        {
            if (_root == null)
            {
                return;
            }

            float h = _height;
            Vector3 feet = _root.position;
            Transform camera = _partner.PlayerCameraReference;
            float ph = PartnerHeight();

            Vector3 toPartner = _partner.transform.position - feet;
            toPartner.y = 0f;
            if (toPartner.sqrMagnitude < 1e-6f)
            {
                return;
            }
            toPartner.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, toPartner);

            // Keep the native quadruped stance; a small neck dip invites a crown stroke.
            if(Scp939ModCompatibility.DirectorRunning)
                {
                    Transform neck;
                    if(_bones.TryGetValue("DEF-Neck",out neck))
                        neck.rotation=Quaternion.AngleAxis(4f*weight,Vector3.Cross(Vector3.up,_root.forward))*neck.rotation;
                }
            else
                AimDir("DEF-Neck", "DEF-Head", toPartner-Vector3.up*.12f, weight*.25f);

        }

        /// <summary>Ужин: корпус подаётся к тарелке, голова опускается к ней и «жуёт».</summary>
        private void PoseLean(float weight)
        {
            if (_root == null)
            {
                return;
            }

            float h = _height;
            Vector3 feet = _root.position;
            Vector3 toPlate = _point - feet;
            toPlate.y = 0f;
            if (toPlate.sqrMagnitude < 1e-6f)
            {
                return;
            }
            toPlate.Normalize();

            float chew = Mathf.Sin(Time.time * 7.5f) * 0.5f + 0.5f;
            Aim("DEF-Stomach", "DEF-Chest", feet + Vector3.up * (h * 0.52f) + toPlate * (h * 0.14f), weight);
            Aim("DEF-Chest", "DEF-Neck", feet + Vector3.up * (h * 0.78f) + toPlate * (h * 0.26f), weight);
            Aim("DEF-Neck", "DEF-Head", _point + Vector3.up * (0.06f + 0.07f * chew), weight);
        }

        /// <summary>
        /// Сидение на стуле. Таз ставится на сиденье, ноги считаются тем же двухкостным IK, что и
        /// руки: стопа — перед стулом на полу, колено выводится вперёд полюсом.
        /// </summary>
        private void PoseSit(float weight)
        {
            if (_root == null)
            {
                return;
            }

            Transform hips = HumanBone(HumanBodyBones.Hips);
            if (hips == null)
            {
                return;
            }

            float floor = _root.position.y;
            Vector3 seat = new Vector3(_point.x, floor + SeatHeight + 0.06f, _point.z) + _facing * 0.04f;
            hips.position = Vector3.Lerp(hips.position, seat, weight);

            SitLeg(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, floor, weight);
            SitLeg(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, floor, weight);
        }

        private void SitLeg(HumanBodyBones upperId, HumanBodyBones lowerId, HumanBodyBones footId, float floor, float weight)
        {
            Transform upper = HumanBone(upperId);
            Transform lower = HumanBone(lowerId);
            Transform foot = HumanBone(footId);
            if (upper == null || lower == null || foot == null)
            {
                return;
            }

            float thigh = Vector3.Distance(upper.position, lower.position);
            Vector3 target = upper.position + _facing * (thigh * 0.95f);
            target.y = floor + 0.09f;   // щиколотка чуть над полом
            Vector3 pole = (_facing + Vector3.up * 0.4f).normalized;
            SolveIk(upper, lower, foot, target, pole, weight);
        }

        /// <summary>
        /// Рука гладящего тянется к голове 939 и ложится на неё.
        ///
        /// Кости — Mixamo, потому что человеческая модель на другом риге, чем 939. Высота цели
        /// берётся от макушки партнёра: во время поглаживания 939 склоняет голову, и рука должна
        /// идти за ней, а не висеть там, где голова была бы в покое.
        /// </summary>
        public static Vector3 PetCrown(Transform head)
        {
            if(CinemaSoftPet) return head.position+head.forward*(.34f+Mathf.Sin(Time.time*2.2f)*.045f)+head.up*.075f;
            // Ordinary game target retained.
            return head.position - head.forward * .04f + head.up * .16f
                 + head.forward * (Mathf.Sin(Time.time * 2.2f) * (CinemaSoftPet?.065f:.035f));
        }

        // Shot-specific polish; do not alter the accepted first-person gesture.
        public static bool CinemaSoftPet;

        private void PosePetGiver(float weight)
        {
            if (_root == null)
            {
                return;
            }

            Transform head;
            // Голову 939 ищем по её же кости: так рука следует за реальным положением, включая наклон.
            Vector3 target;
            if (TryFindPartnerHead(out head))
            {
                target = PetCrown(head);
            }
            else
            {
                target = _partner.transform.position + Vector3.up * (PartnerHeight() * 0.75f);
            }

            Vector3 toTarget = target - _root.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < 1e-6f)
            {
                return;
            }
            toTarget.Normalize();

            // Гладим ближней рукой — той же логикой, что и держание за руку.
            if(CinemaSoftPet)
            {
                var spine=HumanBone(HumanBodyBones.Spine);
                if(spine!=null) spine.rotation=Quaternion.AngleAxis(2f*weight,Vector3.Cross(Vector3.up,toTarget))*spine.rotation;
            }
            Vector3 right = Vector3.Cross(Vector3.up, _root.forward);
            bool useRight = true; // Same right hand throughout the gesture in both views.

            Transform upper, lower, hand;
            if (!TryHumanArm(useRight, out upper, out lower, out hand))
            {
                return;
            }

            // Локоть отводим вниз и наружу, иначе IK складывает руку сквозь собственный корпус.
            Vector3 pole = (right*.25f - _root.forward*.55f + Vector3.up*.90f).normalized;
            Vector3 petFingers=toTarget;
            if(CinemaSoftPet)
            {
                // A low elbow plus a horizontal palm caused an 86-degree wrist kink.
                // Raise the elbow into the reach, and let the fingers follow the
                // forearm across the surface instead of forcing them straight ahead.
                pole=(right*.35f+Vector3.up*.85f-_root.forward*.10f).normalized;
                Quaternion originalUpper=upper.rotation, originalLower=lower.rotation;
                for(int pass=0;pass<3;pass++)
                {
                    SolveIk(upper,lower,hand,target-petFingers*.075f+Vector3.up*.025f,pole,1f);
                    petFingers=Vector3.ProjectOnPlane(hand.position-lower.position,Vector3.up).normalized;
                    if(petFingers.sqrMagnitude<.5f)petFingers=toTarget;
                }
                Quaternion posedUpper=upper.rotation,posedLower=lower.rotation;
                upper.rotation=Quaternion.Slerp(originalUpper,posedUpper,weight);
                lower.rotation=Quaternion.Slerp(originalLower,posedLower,weight);
                Vector3 forearm=(hand.position-lower.position).normalized;
                petFingers=Vector3.RotateTowards(forearm,petFingers,25f*Mathf.Deg2Rad,0f).normalized;
            }
            else
                SolveIk(upper, lower, hand, target-toTarget*.075f+Vector3.up*.025f, pole, weight);
            OrientHumanPalm(hand,true,petFingers,Vector3.up,weight);
            // An open, softly curved palm: relaxed locomotion fingers otherwise stay clenched.
            foreach(string digit in new[]{"Index","Middle","Ring","Little"})
                for(int jointIndex=0;jointIndex<2;jointIndex++)
                {
                    var first=(HumanBodyBones)System.Enum.Parse(typeof(HumanBodyBones),"Right"+digit+(jointIndex==0?"Proximal":"Intermediate"));
                    var second=(HumanBodyBones)System.Enum.Parse(typeof(HumanBodyBones),"Right"+digit+(jointIndex==0?"Intermediate":"Distal"));
                    var joint=HumanBone(first);var child=HumanBone(second);
                    if(joint==null||child==null)continue;
                    Vector3 current=child.position-joint.position;
                    if(current.sqrMagnitude<1e-8f)continue;
                    Vector3 desired=(petFingers-Vector3.up*(jointIndex==0?.04f:.14f)).normalized;
                    joint.rotation=Quaternion.Slerp(joint.rotation,Quaternion.FromToRotation(current,desired)*joint.rotation,weight);
                }
        }

        private void OrientHumanPalm(Transform hand,bool right,Vector3 fingers,Vector3 dorsal,float weight)
        {
            var middle=HumanBone(right?HumanBodyBones.RightMiddleProximal:HumanBodyBones.LeftMiddleProximal);
            var index=HumanBone(right?HumanBodyBones.RightIndexProximal:HumanBodyBones.LeftIndexProximal);
            var little=HumanBone(right?HumanBodyBones.RightLittleProximal:HumanBodyBones.LeftLittleProximal);
            if(middle==null||index==null||little==null)return;
            Vector3 along=middle.position-hand.position;
            Vector3 normal=Vector3.Cross(index.position-little.position,along)*(right?1f:-1f);
            if(along.sqrMagnitude<1e-8f||normal.sqrMagnitude<1e-8f)return;
            Quaternion delta=Quaternion.LookRotation(fingers,dorsal)*Quaternion.Inverse(Quaternion.LookRotation(along,normal));
            hand.rotation=Quaternion.Slerp(hand.rotation,delta*hand.rotation,weight);
        }

        /// <summary>
        /// Рука человека, который держит 939 за руку: тянется к той же точке сцепления, что и её.
        /// Без этой позы партнёр стоял с опущенными руками, и со стороны держание не читалось совсем.
        /// </summary>
        public static Vector3 CinemaClasp(ReferenceHub human,ReferenceHub scp)
        {
            Vector3 feet=Scp939ModCompatibility.Feet(human),other=Scp939ModCompatibility.Feet(scp);
            var motion=Scp939ModCompatibility.Motion(human);
            Vector3 fwd=motion!=null && motion.Heading.sqrMagnitude>.5f?motion.Heading:human.PlayerCameraReference.forward;
            fwd.y=0;fwd.Normalize();
            bool paired = motion != null && motion.PairedWalk;
            Vector3 point = (feet + other) * .5f;
            if (paired)
            {
                var model = ((PlayerRoles.FirstPersonControl.IFpcRole)human.roleManager.CurrentRole).FpcModule.CharacterModelInstance;
                var arm = Scp939ModCompatibility.FindBone(model.GetComponentInChildren<Animator>(), HumanBodyBones.RightUpperArm);
                point.y = arm != null ? arm.position.y - PairShoulderDrop : motion.FloorY + .94f;
                point += fwd * PairForward;
                var creatureModel = ((PlayerRoles.FirstPersonControl.IFpcRole)scp.roleManager.CurrentRole).FpcModule.CharacterModelInstance;
                var creature = creatureModel.GetComponentInChildren<Scp939InteractionPoser>();
                Transform su, sl, sh, sa, sb, sc;
                if (creature != null && creature._bones.TryGetValue("DEF-UpperArm.L", out su) &&
                    creature._bones.TryGetValue("DEF-LowerArm.L", out sl) && creature._bones.TryGetValue("DEF-Hand.L", out sh) &&
                    creature._bones.TryGetValue("DEF-Finger.A.L.001", out sa) && creature._bones.TryGetValue("DEF-Finger.B.L.001", out sb) &&
                    creature._bones.TryGetValue("DEF-Finger.C.L.001", out sc))
                {
                    Vector3 outward = Vector3.Cross(Vector3.up, fwd);
                    Quaternion grip = Quaternion.AngleAxis(-25f, outward);
                    Vector3 fingerAxis = grip * (-Vector3.up * Mathf.Cos(PairCreatureAngle * Mathf.Deg2Rad) - outward * Mathf.Sin(PairCreatureAngle * Mathf.Deg2Rad));
                    float palmLength = Vector3.Distance(sh.position, (sa.position + sb.position + sc.position) / 3f) * .55f;
                    Vector3 palmOffset = fingerAxis * palmLength;
                    Vector3 palmShift = grip * (fwd * (PairPalmGap * .5f) + outward * PairLateral + Vector3.up * PairCreatureLift);
                    Vector3 wristTarget = point + palmShift - palmOffset;
                    bool shoulderPosed = creature._pairCreatureArmFrame == Time.frameCount;
                    Vector3 upperStart = shoulderPosed ? creature._pairCreatureNativeUpper : su.position;
                    Vector3 flat = wristTarget - upperStart; flat.y = 0f;
                    Transform wrist = sh.parent;
                    float reach = Vector3.Distance(su.position, sl.position) + Vector3.Distance(sl.position, wrist.position) + Vector3.Distance(wrist.position, sh.position) - .012f;
                    if (shoulderPosed) reach = creature._pairCreatureNativeReach;
                    float minimum = upperStart.y - Mathf.Sqrt(Mathf.Max(.0001f, reach * reach - flat.sqrMagnitude)) - palmShift.y + palmOffset.y;
                    // Smooth intersection of both arms' reachable volumes, including the gait's high phase.
                    float delta = point.y - minimum;
                    point.y = (point.y + minimum + Mathf.Sqrt(delta * delta + .015f * .015f)) * .5f;
                }
                return point;
            }
            return point + Vector3.up * .94f + fwd * .08f;
        }

        private void PoseHandHoldGiver(float weight)
        {
            if (_root == null)
            {
                return;
            }

            // Рука героя — в ЕГО осях: опущена, чуть отведена в её сторону и вперёд. Так она всегда досягаема и не
            // зависит от того, что 939 на ходу отстаёт (раньше рука тянулась к её кисти и выворачивалась назад
            // горизонтально, а в беге оставалась анимация бега). 939 тянется к ЕГО кисти — у неё рука длиннее.
            Vector3 toScp = _partner.transform.position - _root.position;
            toScp.y = 0f;
            if (toScp.sqrMagnitude < 1e-6f)
            {
                return;
            }
            toScp.Normalize();
            Vector3 fwd = _root.forward;
            var staging=Scp939ModCompatibility.ParentMotion(_root);
            if(staging!=null && staging.Heading.sqrMagnitude>.5f)fwd=staging.Heading;
            fwd.y = 0f;
            fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            bool useRight = staging!=null || Vector3.Dot(right, toScp)>0f;
            Vector3 outward = useRight ? right : -right;
            var humanHub=_root.GetComponentInParent<ReferenceHub>();
            Vector3 clasp = humanHub!=null && Scp939ModCompatibility.Motion(humanHub)!=null ? CinemaClasp(humanHub,_partner) : (_root.position + PartnerFeet()) * .5f + Vector3.up * .98f;

            Transform upper, lower, hand;
            if (!TryHumanArm(useRight, out upper, out lower, out hand))
            {
                return;
            }
            if (staging != null && staging.PairedWalk)
            {
                PosePairedHuman(upper, lower, hand, useRight, humanHub, fwd, outward, weight);
                return;
            }
            Vector3 pole = (-Vector3.up*.8f-fwd*.15f+outward*.25f).normalized;   // локоть назад: рука висит вдоль тела
            Vector3 fingers=(-Vector3.up+fwd*.12f).normalized;
            SolveIk(upper, lower, hand, clasp-fingers*.065f-outward*.025f, pole, weight);
            OrientHumanPalm(hand,useRight,fingers,-outward,weight);
            if(humanHub!=null && Scp939ModCompatibility.Motion(humanHub)!=null) GripFingers(useRight,weight);
            else PointFingers(hand, useRight, -Vector3.up + fwd*.3f, weight);
        }

        private Vector3 HumanPalmCentre(Transform hand, bool right)
        {
            Transform index = HumanBone(right ? HumanBodyBones.RightIndexProximal : HumanBodyBones.LeftIndexProximal);
            Transform little = HumanBone(right ? HumanBodyBones.RightLittleProximal : HumanBodyBones.LeftLittleProximal);
            return index != null && little != null ? Vector3.Lerp(hand.position, (index.position + little.position) * .5f, .55f) : hand.position;
        }

        private const float PairShoulderDrop = .52f;
        private const float PairForward = .12f;
        private const float PairPalmYaw = 180f;
        private const float PairPalmGap = .05f;
        private const float PairLateral = .02f;
        private const float PairCreatureLift = .035f;
        private const float PairHumanAngle = 35f;
        private const float PairCreatureAngle = 30f;
        private static readonly Vector3 PairHumanCurl = new Vector3(40f, 95f, 140f);
        private static readonly Vector3 PairCreatureCurl = new Vector3(35f, 95f, 125f);
        private static readonly Vector3 PairThumb = new Vector3(.7f, .4f, .15f);
        private sealed class PairArmReference
        {
            public Vector3 Hinge;
            public Quaternion Lower, Wrist, TwistOneFrame, TwistTwoFrame;
            public Transform TwistOne, TwistTwo, UpperTwistOne, UpperTwistTwo;
            public Quaternion UpperTwistOneRest, UpperTwistTwoRest;
        }
        private PairArmReference _pairHumanReference, _pairCreatureReference;
        // Use the native reach envelope for the shared clasp even if the creature's
        // shoulder has already been posed this frame. The accepted human pose stays fixed.
        private int _pairCreatureArmFrame = -1;
        private Vector3 _pairCreatureNativeUpper;
        private float _pairCreatureNativeReach;

        private PairArmReference PairReference(Transform upper, Transform lower, Transform hand)
        {
            foreach (var skin in _animator.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var bones = skin.bones;
                int u = System.Array.IndexOf(bones, upper), l = System.Array.IndexOf(bones, lower), h = System.Array.IndexOf(bones, hand);
                if (u < 0 || l < 0 || h < 0 || skin.sharedMesh == null) continue;
                var bind = skin.sharedMesh.bindposes;
                Matrix4x4 ub = bind[u].inverse, lb = bind[l].inverse, hb = bind[h].inverse;
                Transform tip = hand.parent != lower ? hand.parent : hand;
                int tipIndex = System.Array.IndexOf(bones, tip);
                if (tipIndex < 0) continue;
                Vector3 a = ub.MultiplyPoint3x4(Vector3.zero), b = lb.MultiplyPoint3x4(Vector3.zero), c = bind[tipIndex].inverse.MultiplyPoint3x4(Vector3.zero);
                var result = new PairArmReference { Hinge = ub.inverse.MultiplyVector(Vector3.Cross((b - a).normalized, (c - b).normalized)).normalized,
                    Lower = (ub.inverse * lb).rotation, Wrist = (lb.inverse * hb).rotation };
                for (int i = 0; i < bones.Length; i++)
                {
                    if (bones[i] == null) continue;
                    if (bones[i].name == "ForearmTwist1.R") { result.TwistOne = bones[i]; result.TwistOneFrame = (lb.inverse * bind[i].inverse).rotation; }
                    if (bones[i].name == "ForearmTwist2.R") { result.TwistTwo = bones[i]; result.TwistTwoFrame = (lb.inverse * bind[i].inverse).rotation; }
                    if (bones[i].name == "ArmTwist1.R") { result.UpperTwistOne = bones[i]; int p = System.Array.IndexOf(bones, bones[i].parent); if (p >= 0) result.UpperTwistOneRest = (bind[p] * bind[i].inverse).rotation; }
                    if (bones[i].name == "ArmTwist2.R") { result.UpperTwistTwo = bones[i]; int p = System.Array.IndexOf(bones, bones[i].parent); if (p >= 0) result.UpperTwistTwoRest = (bind[p] * bind[i].inverse).rotation; }
                }
                return result;
            }
            return null;
        }

        private static Quaternion PairForearmTwist(Transform lower, Transform hand, Quaternion restWrist)
        {
            Vector3 axis = (hand.position - lower.position).normalized;
            Quaternion delta = hand.rotation * Quaternion.Inverse(lower.rotation * restWrist);
            Vector3 projected = Vector3.Project(new Vector3(delta.x, delta.y, delta.z), axis);
            Quaternion twist = new Quaternion(projected.x, projected.y, projected.z, delta.w);
            float length = Mathf.Sqrt(Quaternion.Dot(twist, twist));
            return length > 1e-5f ? new Quaternion(twist.x / length, twist.y / length, twist.z / length, twist.w / length) : Quaternion.identity;
        }

        private static void PairHumanSkin(Transform lower, Transform hand, PairArmReference reference, float weight)
        {
            if (reference == null) return;
            Quaternion twist = PairForearmTwist(lower, hand, reference.Wrist);
            if (reference.TwistOne != null)
                reference.TwistOne.rotation = Quaternion.Slerp(reference.TwistOne.rotation, Quaternion.Slerp(Quaternion.identity, twist, .25f) * lower.rotation * reference.TwistOneFrame, weight);
            if (reference.TwistTwo != null)
                reference.TwistTwo.rotation = Quaternion.Slerp(reference.TwistTwo.rotation, Quaternion.Slerp(Quaternion.identity, twist, .65f) * lower.rotation * reference.TwistTwoFrame, weight);
            if (reference.UpperTwistOne != null)
                reference.UpperTwistOne.localRotation = Quaternion.Slerp(reference.UpperTwistOne.localRotation, reference.UpperTwistOneRest, weight);
            if (reference.UpperTwistTwo != null)
                reference.UpperTwistTwo.localRotation = Quaternion.Slerp(reference.UpperTwistTwo.localRotation, reference.UpperTwistTwoRest, weight);
        }

        // Position-only IK leaves arbitrary humeral roll. Match the rig's native elbow
        // hinge before pronating the forearm, keeping the accepted hand world pose.
        private static void PairElbowFrame(Transform upper, Transform lower, Transform hand,
            Vector3 nativeHinge, Quaternion nativeLower)
        {
            if (nativeHinge.sqrMagnitude < .5f) return;
            Vector3 end = hand.position, elbow = lower.position;
            Quaternion palm = hand.rotation;
            Vector3 axis = (elbow - upper.position).normalized;
            Vector3 plane = Vector3.Cross(axis, end - elbow);
            if (plane.sqrMagnitude < 1e-8f) return;
            float roll = Vector3.SignedAngle(upper.TransformDirection(nativeHinge), plane, axis);
            upper.rotation = Quaternion.AngleAxis(roll, axis) * upper.rotation;
            lower.localRotation = nativeLower;
            SolveIk(upper, lower, hand, end, elbow - upper.position, 1f);
            hand.rotation = palm;
        }

        private void PosePairedHuman(Transform upper, Transform lower, Transform hand, bool right,
            ReferenceHub human, Vector3 forward, Vector3 outward, float weight)
        {
            // Opposed palms and inward-sloping metacarpals form one clasp beneath the hips.
            Quaternion grip = Quaternion.AngleAxis(-25f, outward);
            Vector3 dorsal = grip * (Quaternion.AngleAxis(PairPalmYaw, Vector3.up) * forward);
            Vector3 target = CinemaClasp(human, _partner) + grip * (-forward * (PairPalmGap * .5f) - outward * PairLateral);
            Vector3 relaxed = grip * (-Vector3.up * Mathf.Cos(PairHumanAngle * Mathf.Deg2Rad) + outward * Mathf.Sin(PairHumanAngle * Mathf.Deg2Rad));
            Vector3 fingers = relaxed;
            Vector3 pole = (-Vector3.up - forward * .18f + outward * .05f).normalized;
            Quaternion originalUpper = upper.rotation, originalLower = lower.rotation, originalHand = hand.rotation;
            Quaternion neutralWrist = Quaternion.Inverse(lower.rotation) * hand.rotation;
            if (_pairHumanReference == null) _pairHumanReference = PairReference(upper, lower, hand);
            for (int pass = 0; pass < 3; pass++)
            {
                OrientHumanPalm(hand, right, fingers, dorsal, 1f);
                Vector3 offset = HumanPalmCentre(hand, right) - hand.position;
                Vector3 wristTarget = target - offset;
                SolveIk(upper, lower, hand, wristTarget, pole, 1f);
                OrientHumanPalm(hand, right, fingers, dorsal, 1f);
            }
            if (_pairHumanReference != null)
                PairElbowFrame(upper, lower, hand, _pairHumanReference.Hinge, _pairHumanReference.Lower);
            Quaternion posedUpper = upper.rotation, posedLower = lower.rotation, posedHand = hand.rotation;
            upper.rotation = Quaternion.Slerp(originalUpper, posedUpper, weight);
            lower.rotation = Quaternion.Slerp(originalLower, posedLower, weight);
            hand.rotation = Quaternion.Slerp(originalHand, posedHand, weight);
            PairHumanSkin(lower, hand, _pairHumanReference, weight);
            PosePairedHumanDigits(hand, right, weight);
        }

        private void PosePairedCreature(string upperName, string lowerName, string handName, bool right,
            ReferenceHub scp, Vector3 forward, Vector3 toPartner, float weight)
        {
            Transform upper, lower, hand, a, b, c;
            string side = right ? "R" : "L";
            if (!_bones.TryGetValue(upperName, out upper) || !_bones.TryGetValue(lowerName, out lower) ||
                !_bones.TryGetValue(handName, out hand) || !_bones.TryGetValue("DEF-Finger.A." + side + ".001", out a) ||
                !_bones.TryGetValue("DEF-Finger.B." + side + ".001", out b) || !_bones.TryGetValue("DEF-Finger.C." + side + ".001", out c)) return;
            Vector3 outward = Vector3.Cross(Vector3.up, forward).normalized;
            Quaternion grip = Quaternion.AngleAxis(-25f, outward);
            Vector3 dorsal = grip * (Quaternion.AngleAxis(PairPalmYaw, Vector3.up) * forward);
            Transform nativeWrist = hand.parent;
            _pairCreatureArmFrame = Time.frameCount;
            _pairCreatureNativeUpper = upper.position;
            _pairCreatureNativeReach = Vector3.Distance(upper.position, lower.position) +
                Vector3.Distance(lower.position, nativeWrist.position) + Vector3.Distance(nativeWrist.position, hand.position) - .012f;
            Vector3 target = CinemaClasp(_partner, scp) + grip * (forward * (PairPalmGap * .5f) + outward * PairLateral + Vector3.up * PairCreatureLift);
            Vector3 relaxed = grip * (-Vector3.up * Mathf.Cos(PairCreatureAngle * Mathf.Deg2Rad) - outward * Mathf.Sin(PairCreatureAngle * Mathf.Deg2Rad));
            Vector3 fingers = relaxed;
            Vector3 pole = (-Vector3.up - forward * .70f - outward * .50f).normalized;
            Quaternion ou = upper.rotation, ol = lower.rotation, oh = hand.rotation;
            Quaternion neutralWrist = Quaternion.Inverse(lower.rotation) * hand.rotation;
            Transform wrist = hand.parent;
            bool hasWrist = wrist != null && wrist != lower && wrist.IsChildOf(lower);
            Quaternion ow = hasWrist ? wrist.rotation : Quaternion.identity;
            // Protract the clavicle slightly and let the upper arm join the gesture.
            // Rotate the authored chain; never translate/scale a joint or move the clasp.
            Transform shoulder = upper.parent;
            if (shoulder != null && shoulder.name.StartsWith("DEF-Shoulder."))
            {
                Vector3 clavicle = upper.position - shoulder.position;
                Vector3 engaged = (clavicle + forward * .06f - Vector3.up * .06f).normalized * clavicle.magnitude;
                Quaternion participation = Quaternion.RotateTowards(Quaternion.identity, Quaternion.FromToRotation(clavicle, engaged), 28f);
                shoulder.rotation = Quaternion.Slerp(shoulder.rotation, participation * shoulder.rotation, weight);
            }
            if (_pairCreatureReference == null) _pairCreatureReference = PairReference(upper, lower, hand);
            for (int pass = 0; pass < 3; pass++)
            {
                Vector3 along = b.position - hand.position;
                Vector3 normal = Vector3.Cross(a.position - c.position, along) * (right ? 1f : -1f);
                if (along.sqrMagnitude > 1e-8f && normal.sqrMagnitude > 1e-8f)
                    hand.rotation = Quaternion.LookRotation(fingers, -dorsal) * Quaternion.Inverse(Quaternion.LookRotation(along, normal)) * hand.rotation;
                Vector3 centre = Vector3.Lerp(hand.position, (a.position + b.position + c.position) / 3f, .55f);
                SolveIk(upper, lower, hand, target - (centre - hand.position), pole, 1f);
            }
            Vector3 finalAlong = b.position - hand.position;
            Vector3 finalNormal = Vector3.Cross(a.position - c.position, finalAlong) * (right ? 1f : -1f);
            if (finalAlong.sqrMagnitude > 1e-8f && finalNormal.sqrMagnitude > 1e-8f)
                hand.rotation = Quaternion.LookRotation(fingers, -dorsal) * Quaternion.Inverse(Quaternion.LookRotation(finalAlong, finalNormal)) * hand.rotation;
            if (_pairCreatureReference != null)
            {
                PairElbowFrame(upper, lower, hand, _pairCreatureReference.Hinge, _pairCreatureReference.Lower);
                Quaternion twist = PairForearmTwist(lower, hand, _pairCreatureReference.Wrist);
                Quaternion wristFrame = hasWrist ? twist * wrist.rotation : Quaternion.identity, palmFrame = hand.rotation;
                lower.rotation = Quaternion.Slerp(Quaternion.identity, twist, hasWrist ? .25f : 1f) * lower.rotation;
                if (hasWrist) wrist.rotation = wristFrame;
                hand.rotation = palmFrame;
            }
            Quaternion pu = upper.rotation, pl = lower.rotation, ph = hand.rotation, pw = hasWrist ? wrist.rotation : Quaternion.identity;
            upper.rotation = Quaternion.Slerp(ou, pu, weight); lower.rotation = Quaternion.Slerp(ol, pl, weight);
            if (hasWrist) wrist.rotation = Quaternion.Slerp(ow, pw, weight);
            hand.rotation = Quaternion.Slerp(oh, ph, weight);
            // Absolute directions replace additive curls from an already flexed animation.
            for (int digitIndex = 0; digitIndex < 3; digitIndex++)
                for (int i = 1; i <= 3; i++)
                {
                    string digit = digitIndex == 0 ? "A" : digitIndex == 1 ? "B" : "C";
                    Transform joint, child;
                    if (!_bones.TryGetValue("DEF-Finger." + digit + "." + side + ".00" + i, out joint)) continue;
                    _bones.TryGetValue("DEF-Finger." + digit + "." + side + ".00" + (i + 1), out child);
                    Vector3 alongBone = child != null ? child.position - joint.position : joint.up;
                    float bend = PairCreatureCurl[i - 1] * Mathf.Deg2Rad;
                    Vector3 splay = grip * forward * ((digitIndex - 1) * .035f);
                    Vector3 wanted = (fingers * Mathf.Cos(bend) + dorsal * Mathf.Sin(bend) + splay).normalized;
                    AimDigit(joint, alongBone, wanted, weight);
                }
        }

        // Put pronation into the forearm instead of winding the hand around the wrist.
        // Rotating around lower->hand preserves the solved endpoint and limb lengths.
        private static void AlignForearmRoll(Transform lower, Transform hand, Quaternion neutralWrist)
        {
            Vector3 axis = (hand.position - lower.position).normalized;
            Quaternion wanted = hand.rotation;
            Quaternion delta = wanted * Quaternion.Inverse(lower.rotation * neutralWrist);
            Vector3 projected = Vector3.Project(new Vector3(delta.x, delta.y, delta.z), axis);
            Quaternion twist = new Quaternion(projected.x, projected.y, projected.z, delta.w);
            float length = Mathf.Sqrt(Quaternion.Dot(twist, twist));
            if (length > 1e-5f)
            {
                twist = new Quaternion(twist.x / length, twist.y / length, twist.z / length, twist.w / length);
                lower.rotation = twist * lower.rotation;
                hand.rotation = wanted;
            }
        }

        private static void AimDigit(Transform joint, Vector3 alongBone, Vector3 wanted, float weight)
        {
            if (alongBone.sqrMagnitude < 1e-8f || wanted.sqrMagnitude < 1e-8f) return;
            joint.rotation = Quaternion.Slerp(joint.rotation,
                Quaternion.FromToRotation(alongBone, wanted.normalized) * joint.rotation, weight);
        }

        private void PosePairedHumanDigits(Transform hand, bool right, float weight)
        {
            var middle = HumanBone(right ? HumanBodyBones.RightMiddleProximal : HumanBodyBones.LeftMiddleProximal);
            var index = HumanBone(right ? HumanBodyBones.RightIndexProximal : HumanBodyBones.LeftIndexProximal);
            var little = HumanBone(right ? HumanBodyBones.RightLittleProximal : HumanBodyBones.LeftLittleProximal);
            if (middle == null || index == null || little == null) return;
            Vector3 along = (middle.position - hand.position).normalized;
            Vector3 across = (index.position - little.position).normalized;
            Vector3 palm = -Vector3.Cross(across, along).normalized * (right ? 1f : -1f);
            string side = right ? "Right" : "Left";
            foreach (string finger in new[] { "Index", "Middle", "Ring", "Little" })
                for (int i = 0; i < 3; i++)
                {
                    var id = (HumanBodyBones)System.Enum.Parse(typeof(HumanBodyBones), side + finger + (i == 0 ? "Proximal" : i == 1 ? "Intermediate" : "Distal"));
                    var joint = HumanBone(id); if (joint == null) continue;
                    Transform child = i < 2 ? HumanBone((HumanBodyBones)System.Enum.Parse(typeof(HumanBodyBones), side + finger + (i == 0 ? "Intermediate" : "Distal"))) : null;
                    Vector3 current = child != null ? child.position - joint.position : joint.up;
                    float bend = PairHumanCurl[i] * Mathf.Deg2Rad;
                    AimDigit(joint, current, along * Mathf.Cos(bend) + palm * Mathf.Sin(bend), weight);
                }
            // The thumb opposes the four fingers instead of preserving the hooked idle pose.
            Vector3 centre = HumanPalmCentre(hand, right);
            var thumbRoot = HumanBone(right ? HumanBodyBones.RightThumbProximal : HumanBodyBones.LeftThumbProximal);
            if (thumbRoot == null) return;
            Vector3 thumbDirection = (along * PairThumb.x + across * PairThumb.y + palm * PairThumb.z).normalized;
            for (int i = 0; i < 3; i++)
            {
                var joint = HumanBone((HumanBodyBones)System.Enum.Parse(typeof(HumanBodyBones), side + "Thumb" + (i == 0 ? "Proximal" : i == 1 ? "Intermediate" : "Distal")));
                var child = i < 2 ? HumanBone((HumanBodyBones)System.Enum.Parse(typeof(HumanBodyBones), side + "Thumb" + (i == 0 ? "Intermediate" : "Distal"))) : null;
                if (joint == null) continue;
                Vector3 direction = Vector3.Slerp(thumbDirection, along, i * .12f).normalized;
                Vector3 current = child != null ? child.position - joint.position : joint.up;
                AimDigit(joint, current, direction, weight);
            }
        }

        private readonly Dictionary<Transform,Quaternion> _gripRest=new Dictionary<Transform,Quaternion>();
        private void GripFingers(bool right,float weight)
        {
            var index=HumanBone(right?HumanBodyBones.RightIndexProximal:HumanBodyBones.LeftIndexProximal);
            var little=HumanBone(right?HumanBodyBones.RightLittleProximal:HumanBodyBones.LeftLittleProximal);
            if(index==null||little==null)return;
            Vector3 axis=(index.position-little.position).normalized;
            string side=right?"Right":"Left";
            foreach(string finger in new[]{"Index","Middle","Ring","Little"})
                for(int n=0;n<3;n++)
                {
                    var bone=(HumanBodyBones)System.Enum.Parse(typeof(HumanBodyBones),side+finger+(n==0?"Proximal":n==1?"Intermediate":"Distal"));
                    var joint=HumanBone(bone);if(joint==null)continue;
                    if(!_gripRest.TryGetValue(joint,out var rest)){rest=joint.localRotation;_gripRest[joint]=rest;}
                    joint.localRotation=rest;
                    joint.rotation=Quaternion.AngleAxis(-(n==0?18f:34f)*weight,axis)*joint.rotation;
                }
        }

        /// <summary>Какая рука человека сейчас ближе к точке — правая или левая.</summary>
        private bool NearerHandIsRight(Vector3 point)
        {
            Transform r = HumanHand(true);
            Transform l = HumanHand(false);
            if (r == null)
            {
                return false;
            }
            if (l == null)
            {
                return true;
            }
            return Vector3.Distance(r.position, point) <= Vector3.Distance(l.position, point);
        }

        /// <summary>Кость партнёра по имени — чтобы тянуться к её реальному положению, а не к расчётному.</summary>
        private bool TryFindPartnerBone(string boneName, out Transform bone)
        {
            bone = null;
            var fpc = _partner.roleManager.CurrentRole as PlayerRoles.FirstPersonControl.IFpcRole;
            if (fpc == null || fpc.FpcModule.CharacterModelInstance == null)
            {
                return false;
            }
            foreach (Transform t in fpc.FpcModule.CharacterModelInstance.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == boneName)
                {
                    bone = t;
                    return true;
                }
            }
            return false;
        }

        private bool TryFindPartnerHead(out Transform head)
        {
            return TryFindPartnerBone("DEF-Head", out head);
        }

        /// <summary>Держание за руку: ближняя к партнёру рука опускается к его кисти.</summary>
        private void PoseHandHold(float weight)
        {
            if (_root == null)
            {
                return;
            }

            float h = _height;
            Vector3 feet = _root.position;

            Vector3 toPartner = _partner.transform.position - feet;
            toPartner.y = 0f;
            if (toPartner.sqrMagnitude < 1e-6f)
            {
                return;
            }
            toPartner.Normalize();

            // Партнёр идёт сбоку, поэтому руку выбираем по стороне, с которой он находится.
            // Внимание: Cross(up, forward) — это ПРАВЫЙ вектор, а не левый. На этом уже была ошибка:
            // 939 тянулась к партнёру дальней рукой через себя.
            Vector3 forward = _root.forward;
            bool onRight = Vector3.Dot(Vector3.Cross(Vector3.up, forward), toPartner) > 0f;
            string upper  = onRight ? "DEF-UpperArm.R"     : "DEF-UpperArm.L";
            string lower  = onRight ? "DEF-LowerArm.R"     : "DEF-LowerArm.L";
            string hand   = onRight ? "DEF-Hand.R"         : "DEF-Hand.L";
            string finger = onRight ? "DEF-Finger.B.R.001" : "DEF-Finger.B.L.001";

            // Корпус выпрямляется во весь рост — идти рядом, сгорбившись по-звериному, выглядит так,
            // будто она просто крадётся мимо, а не идёт с тобой за руку. Ставится ДО рук: они считают
            // положение плеча, а его задаёт корпус.
            // Native body gait retained during hand-holding.

            // Preserve native neck orientation during paired walking.

            // Точка, где встречаются кисти. Обе базы — КОРНИ МОДЕЛЕЙ (пол): раньше её ноги брались
            // от корня модели, а ноги партнёра от хаба (центр капсулы, ~0.96 над полом), и точка
            // уезжала так далеко, что рука партнёра до неё физически не дотягивалась.
            Vector3 partnerFeet = PartnerFeet();
            Vector3 partnerHand = ClaspPoint(feet, partnerFeet, PartnerHeight());
            var scpHub=_root.GetComponentInParent<ReferenceHub>();
            partnerHand = scpHub!=null && Scp939ModCompatibility.Motion(_partner)!=null ? CinemaClasp(_partner,scpHub) : (feet + partnerFeet) * .5f + Vector3.up * .98f;
            var pairedMotion = Scp939ModCompatibility.Motion(_partner);
            if (pairedMotion != null && pairedMotion.PairedWalk)
            {
                PosePairedCreature(upper, lower, hand, onRight, scpHub, forward, toPartner, weight);
                return;
            }
            Vector3 pole = (-Vector3.up*.65f-forward*.15f-toPartner*.25f).normalized;   // локоть назад: рука висит вдоль тела
            // Концом цепи берём саму кисть, а не запястье: иначе ладонь проскакивает мимо руки
            // партнёра на длину кисти и «держания» не видно.
            AimIk(upper, lower, hand, partnerHand, pole, weight);

            // IK ставит кисть в нужную точку, но её собственный поворот при этом остаётся любым —
            // в кадре получались растопыренные когти, торчащие вбок. Доворачиваем ладонь так, чтобы
            // пальцы смотрели вниз, вдоль руки партнёра, как при настоящем рукопожатии.
            Aim(hand, finger, partnerHand - Vector3.up * (h * 0.16f), weight);
            if(Scp939ModCompatibility.Motion(_partner)!=null)
            {
                string sideName=onRight?"R":"L";
                Transform palm,a,b,c;
                if(_bones.TryGetValue(hand,out palm) &&
                   _bones.TryGetValue("DEF-Finger.A."+sideName+".001",out a) &&
                   _bones.TryGetValue("DEF-Finger.B."+sideName+".001",out b) &&
                   _bones.TryGetValue("DEF-Finger.C."+sideName+".001",out c))
                {
                    Vector3 along=b.position-palm.position;
                    Vector3 normal=Vector3.Cross(a.position-c.position,along)*(onRight?1f:-1f);
                    if(along.sqrMagnitude>1e-8f && normal.sqrMagnitude>1e-8f)
                    {
                        Quaternion delta=Quaternion.LookRotation(-Vector3.up,-toPartner)*Quaternion.Inverse(Quaternion.LookRotation(along,normal));
                        palm.rotation=Quaternion.Slerp(palm.rotation,delta*palm.rotation,weight);
                    }
                    Vector3 curlAxis=Vector3.Cross(-Vector3.up,toPartner).normalized;
                    foreach(string digit in new[]{"A","B","C"})
                        for(int jointIndex=1;jointIndex<=3;jointIndex++)
                        {
                            Transform joint;
                            if(!_bones.TryGetValue("DEF-Finger."+digit+"."+sideName+".00"+jointIndex,out joint))continue;
                            Quaternion rest;
                            if(!_gripRest.TryGetValue(joint,out rest)){rest=joint.localRotation;_gripRest[joint]=rest;}
                            joint.localRotation=rest;
                            joint.rotation=Quaternion.AngleAxis((jointIndex==1?20f:jointIndex==2?35f:25f)*weight,curlAxis)*joint.rotation;
                        }
                }
            }
        }

        /// <summary>
        /// Двухкостный IK: ставит кисть ИМЕННО в заданную точку, а не просто разворачивает руку в её
        /// сторону.
        ///
        /// Разворота недостаточно: рука фиксированной длины, и если целиться в точку у спины партнёра,
        /// кисть проскакивает её насквозь и вылезает с другой стороны. Замер до этой правки: цель в
        /// 0.12 от груди партнёра — кисть в 0.28. Из-за этого объятие выглядело как размахивание
        /// лапами, а держание за руку не читалось вовсе.
        ///
        /// Локоть ищется по теореме косинусов, сторона сгиба задаётся вектором <paramref name="pole"/>.
        /// </summary>
        private void AimIk(string upperName, string lowerName, string endName,
                           Vector3 target, Vector3 pole, float weight)
        {
            Transform upper, lower, end;
            if (weight <= 0f || !_bones.TryGetValue(upperName, out upper) ||
                !_bones.TryGetValue(lowerName, out lower) || !_bones.TryGetValue(endName, out end))
            {
                return;
            }
            SolveIk(upper, lower, end, target, pole, weight);
        }

        /// <summary>
        /// Тот же двухкостный IK, но по готовым костям — чтобы им могли пользоваться и другие риги
        /// (например, руки от первого лица), у которых свои имена костей и свой словарь.
        /// </summary>
        public static void SolveIk(Transform upper, Transform lower, Transform end,
                                   Vector3 target, Vector3 pole, float weight)
        {
            if (upper == null || lower == null || end == null || weight <= 0f)
            {
                return;
            }

            // «Не число» на входе означает, что риг уже разъехался: считать по нему нельзя, иначе
            // NaN расползается по всей цепи и кость исчезает из кадра навсегда.
            if (!Finite(upper.position) || !Finite(lower.position) || !Finite(end.position) ||
                !Finite(target) || !Finite(pole))
            {
                return;
            }

            // У 939 предплечье из ДВУХ костей (LowerArm 0.18 + Wrist 0.16): промежуточные кости между lower и end
            // выпрямляем по оси предплечья, иначе вторая половина висела под углом из анимации — «вывернутые
            // запястья» (замер 2026-09-30: изгиб 46–58°).
            bool lowerIsAncestor = false;
            int depth = 0;
            for (Transform c = end.parent; c != null && depth < 4; c = c.parent, depth++)
            {
                if (c == lower)
                {
                    lowerIsAncestor = true;
                    break;
                }
            }
            for (Transform t = end.parent; lowerIsAncestor && t != null && t != lower; t = t.parent)
            {
                Transform parent = t.parent;
                Transform child = null;
                for (Transform c = end; c != null; c = c.parent)
                {
                    if (c.parent == t)
                    {
                        child = c;
                        break;
                    }
                }
                if (parent == null || child == null)
                {
                    break;
                }
                Vector3 along = t.position - parent.position;
                Vector3 current = child.position - t.position;
                if (along.sqrMagnitude > 1e-8f && current.sqrMagnitude > 1e-8f)
                {
                    t.rotation = Quaternion.Slerp(t.rotation, Quaternion.FromToRotation(current, along) * t.rotation, weight);
                }
            }

            Vector3 root = upper.position;
            float a = Vector3.Distance(root, lower.position);
            float b = Vector3.Distance(lower.position, end.position);
            if (a < 1e-5f || b < 1e-5f)
            {
                return;
            }

            Vector3 toTarget = target - root;
            float reach = toTarget.magnitude;
            if (reach < 1e-5f)
            {
                return;
            }

            // За пределами досягаемости рука просто вытягивается — иначе решение не существует.
            float clamped = Mathf.Clamp(reach, Mathf.Abs(a - b) + 1e-4f, a + b - 1e-4f);
            Vector3 dir = toTarget / reach;

            float cos = (a * a + clamped * clamped - b * b) / (2f * a * clamped);
            float angle = Mathf.Acos(Mathf.Clamp(cos, -1f, 1f)) * Mathf.Rad2Deg;

            Vector3 axis = Vector3.Cross(dir, pole);
            if (axis.sqrMagnitude < 1e-6f)
            {
                // Полюс совпал с направлением на цель — берём любую перпендикулярную ось.
                axis = Vector3.Cross(dir, Vector3.up);
                if (axis.sqrMagnitude < 1e-6f)
                {
                    axis = Vector3.Cross(dir, Vector3.right);
                }
            }
            axis.Normalize();

            Vector3 elbow = root + (Quaternion.AngleAxis(angle, axis) * dir) * a;

            AimBone(upper, lower, elbow, weight);
            AimBone(lower, end, target, weight);
        }

        /// <summary>Конечно ли значение — защита от NaN, который необратимо ломает позу.</summary>
        private static bool Finite(Vector3 v)
        {
            return !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z) &&
                   !float.IsInfinity(v.x) && !float.IsInfinity(v.y) && !float.IsInfinity(v.z);
        }

        /// <summary>Доворот кости по готовым трансформам — общая часть для всех ригов.</summary>
        public static void AimBone(Transform bone, Transform child, Vector3 worldTarget, float weight)
        {
            if (bone == null || child == null || weight <= 0f)
            {
                return;
            }
            if (!Finite(bone.position) || !Finite(child.position) || !Finite(worldTarget))
            {
                return;
            }

            Vector3 current = child.position - bone.position;
            Vector3 desired = worldTarget - bone.position;
            if (current.sqrMagnitude < 1e-8f || desired.sqrMagnitude < 1e-8f)
            {
                return;
            }

            Quaternion delta = Quaternion.FromToRotation(current, desired);
            bone.rotation = Quaternion.Slerp(bone.rotation, delta * bone.rotation, Mathf.Clamp01(weight));
        }

        /// <summary>Доворачивает кость так, чтобы её «дочерняя» кость смотрела в мировую точку.</summary>
        /// <summary>
        /// Навести кость по НАПРАВЛЕНИЮ (от её собственного положения). Абсолютные высоты целей тут не годятся:
        /// цель «на 0.78 роста» оказывалась НИЖЕ самой кости живота, и 939 складывалась пополам (кадры 2026-09-30).
        /// </summary>
        private void AimDir(string boneName, string childName, Vector3 dir, float weight)
        {
            Transform bone;
            if (!_bones.TryGetValue(boneName, out bone))
            {
                return;
            }
            Aim(boneName, childName, bone.position + dir.normalized, weight);
        }

        private void Aim(string boneName, string childName, Vector3 worldTarget, float weight)
        {
            Transform bone, child;
            if (weight <= 0f || !_bones.TryGetValue(boneName, out bone) || !_bones.TryGetValue(childName, out child))
            {
                return;
            }

            Vector3 current = child.position - bone.position;
            Vector3 desired = worldTarget - bone.position;
            if (current.sqrMagnitude < 1e-8f || desired.sqrMagnitude < 1e-8f)
            {
                return;
            }

            Quaternion delta = Quaternion.FromToRotation(current, desired);
            bone.rotation = Quaternion.Slerp(bone.rotation, delta * bone.rotation, Mathf.Clamp01(weight));
        }
    }
}




