using InventorySystem.Items;
using UnityEngine;

namespace PlayerRoles.PlayableScps.Scp939
{
    /// <summary>
    /// Показывает собственную руку игрока от первого лица во время взаимодействия с SCP-939.
    ///
    /// Зачем понадобилось отдельное решение. Своя модель персонажа для владельца скрыта, а руки от
    /// первого лица в этой игре рисует НЕ она, а общий риг <see cref="SharedHandsController"/>,
    /// который живёт на слое Viewmodel и обычно прилипает к вьюмодели предмета. С пустыми руками
    /// вьюмодели нет — риг выключается, а камера вьюмодели обнуляет маску отрисовки. Поэтому просто
    /// «позировать руку» недостаточно: её сначала некому нарисовать.
    ///
    /// Кости здесь названы третьим способом: не DEF-* (939) и не mixamorig:* (человек в третьем
    /// лице), а arm.R / forearm.R / hand.R. Причём hand.* — это кость ЗАПЯСТЬЯ, и её родитель —
    /// Armature, а не предплечье: рука и кисть анимируются независимо, поэтому доворота руки
    /// недостаточно, запястье надо ставить отдельно.
    /// </summary>
    public class Scp939FirstPersonHand : MonoBehaviour
    {
        /// <summary>Слой, на котором рисуются вьюмодели.</summary>
        private const int ViewmodelLayer = 10;

        private const float BlendTime = 0.35f;

        // Посадка рига. Эти три числа не подобраны на глаз: они сняты с настоящих вьюмоделей
        // предметов (AK, SCP-500, дезинтегратор — у всех одно и то же), потому что штатно риг
        // прилипает именно к трансформу аниматора вьюмодели. Разворот на 180° обязателен: риг
        // авторен «лицом назад», и без него руки уходят в объектив, закрывая весь экран.
        private static readonly Vector3 RigOffset = new Vector3(0f, -0.488f, 0.474f);
        private static readonly Quaternion RigRotation = Quaternion.Euler(0f, 180f, 0f);
        private const float RigScale = 0.4f;

        /// <summary>
        /// Куда тянуть ладонь — в долях длины руки, в системе координат камеры.
        /// x — вбок наружу, y — вниз, z — вперёд. Плюс амплитуда покачивания и доворот ладони.
        /// </summary>
        private struct Gesture
        {
            public float Side;
            public float Down;
            public float Forward;
            public float Stroke;
            public float PalmTwist;

            /// <summary>Вести ладонь на реальную голову 939, а не в фиксированную точку кадра.</summary>
            public bool AimAtHead;
        }

        // Поглаживание: ладонь ведём НА ГОЛОВУ 939 и покачиваем. Фиксированная точка кадра здесь
        // не годится — 939 то пригибается, то встаёт в полный рост, и рука оставалась в углу
        // экрана, пока её голова была в центре.
        //
        // Forward держим БОЛЬШИМ (кадр 2026-09-04): при 0.80 кисть оказывалась в 0.58 от камеры
        // вьюмодели, и предплечье занимало половину экрана. Дальше вытянутой руки IK всё равно не
        // уводит — цепь просто распрямляется, зато рука в кадре становится нормального размера.
        private static Gesture PetGesture = new Gesture
        // Side/Down держим МАЛЕНЬКИМИ: при наведении на голову любое заметное смещение уводит
        // ладонь с неё же (замер: при Side 0.06 / Down 0.10 кисть уезжала на 0.13 экрана вбок-вниз
        // от головы). Down = 0.16 подобран замером 2026-09-28: при 0.02 ладонь висела над макушкой
        // (кисть на экране 0.56 при макушке 0.44 — жалоба «рука выше головы»), при 0.22 уходила под
        // подбородок (0.08) — зависимость сильно нелинейная, шаг правки держать мелким.
        { Side = 0.06f, Down = 0.16f, Forward = 1.15f, Stroke = 0.10f, PalmTwist = 60f, AimAtHead = true };

        // Держание за руку: кисть сбоку и чуть ниже центра, неподвижна. Опускать сильнее нельзя —
        // при Down = 0.70 рука уходила целиком за нижний край кадра.
        private static readonly Gesture HoldGesture = new Gesture
        { Side = 0.58f, Down = 0.30f, Forward = 0.40f, Stroke = 0f, PalmTwist = 0f };

        // Объятие (2026-09-28, «объятия выглядят странно»): руки больше НЕ симметричны. Раньше обе
        // кисти висели на уровне её плеч по краям кадра — выглядело, будто держишь её за плечи на
        // вытянутых руках. Руки от первого лица рисуются ПОВЕРХ мира, поэтому «спрятать» их за её
        // спиной нельзя — они лежат на ней: ладонями к ней, тыльной стороной к камере (PalmTwist 110;
        // при отрицательном ладони смотрели в камеру, будто протягиваешь ей что-то), ближе к краям
        // кадра. Её голова при этом лежит у тебя на правом плече (Poser). Подобрано по кадрам 2026-09-28.
        private static Gesture HugGestureRight = new Gesture
        { Side = 0.40f, Down = 0.25f, Forward = 1.05f, Stroke = 0f, PalmTwist = 110f };

        private static Gesture HugGestureLeft = new Gesture
        { Side = 0.42f, Down = 0.05f, Forward = 1.05f, Stroke = 0f, PalmTwist = 110f };

        private static Scp939FirstPersonHand _instance;

        private ReferenceHub _target;
        private Transform _targetHead;
        private Camera _viewmodelCamera;
        private Light _fillLight;
        private Light _faceLight;

        /// <summary>Аниматор, снятый с рига на время позы: у пустых рук он остаётся от предмета.</summary>
        private RuntimeAnimatorController _savedController;
        private int _savedCullingMask;
        private bool _hijacked;
        private float _endsAt = -1f;

        /// <summary>До какого момента ждём, что риг освободится от вьюмодели предмета.</summary>
        private float _waitUntil = -1f;
        private float _weight;
        private Scp939InteractionKind _kind = Scp939InteractionKind.Pet;

        // Замеры бинд-позы рига по сторонам: 0 — правая, 1 — левая.
        private readonly float[] _forearmLength = new float[2];

        /// <summary>
        /// Длина руки, замеренная ОДИН раз в бинд-позе.
        ///
        /// Считать её каждый кадр нельзя: вылет жеста задаётся в долях этой длины, а кисть мы сами
        /// же и двигаем — получается положительная обратная связь. При Forward &gt; 1 кисть за
        /// десяток кадров улетала на миллионы единиц и дальше сваливалась в NaN (замер 2026-09-04:
        /// позиция (270540, -331032, 2771518) при Forward = 1.15).
        /// </summary>
        private readonly float[] _armReach = new float[2];
        private readonly Vector3[] _forearmToHand = new Vector3[2];
        private readonly Quaternion[] _handLocal = new Quaternion[2];

        // Сама бинд-поза: к ней возвращаем кости, если расчёт хоть раз дал «не число».
        private readonly Vector3[] _bindLocalPos = new Vector3[2];
        private readonly Quaternion[] _bindLocalRot = new Quaternion[2];
        private readonly Quaternion[] _bindUpperRot = new Quaternion[2];
        private readonly Quaternion[] _bindLowerRot = new Quaternion[2];
        private readonly bool[] _bindSaved = new bool[2];

        /// <summary>
        /// Показать руку в жесте под действие. duration = 0 — держать до Hide().
        ///
        /// Риг может быть занят вьюмоделью предмета, и раньше в этом случае показ просто ОТМЕНЯЛСЯ —
        /// молча и навсегда. Это и была жалоба «при поглаживании не видно руки»: сервер убирает
        /// предмет из рук в тот же момент, но у клиента ещё доигрывается анимация убирания, и на
        /// кадре запроса риг всё ещё чужой. Теперь показ не отменяется, а ЖДЁТ освобождения рига
        /// до <see cref="ShowGraceTime"/> секунд.
        /// </summary>
        public static void Show(ReferenceHub target, float duration, Scp939InteractionKind kind)
        {
            if (target == null)
            {
                return;
            }

            // В режиме строительства риг рук держит чертёж и карандаш — туда не лезем.
            if (Scp939ModCompatibility.IsBuilding)
            {
                return;
            }

            var hands = SharedHandsController.Singleton;
            if (hands == null || hands.Hands == null)
            {
                return;
            }

            if (_instance == null)
            {
                var go = new GameObject("SCP939 First Person Hand");
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<Scp939FirstPersonHand>();
            }

            _instance._target = target;
            _instance._targetHead = null;
            _instance._kind = kind;
            _instance._endsAt = duration > 0f ? Time.time + duration : -1f;
            _instance._waitUntil = Time.time + ShowGraceTime;
            _instance.enabled = true;

            // Если риг свободен — начинаем сразу, иначе этим займётся LateUpdate.
            if (RigFree(hands))
            {
                _instance.Hijack(hands);
            }
        }

        /// <summary>Сколько ждать, пока вьюмодель предмета отпустит общий риг рук.</summary>
        private const float ShowGraceTime = 2f;

        /// <summary>
        /// Свободен ли общий риг, то есть пусты ли руки игрока.
        ///
        /// Смотрим на ИНВЕНТАРЬ, а не на <c>runtimeAnimatorController</c> рига, как было раньше.
        /// Замер в игре 2026-09-07: после того как игрок хоть раз доставал предмет, контроллер
        /// остаётся висеть на риге НАВСЕГДА — предмет уже убран (CurItem = None), а проверка
        /// «риг занят» продолжает срабатывать. Именно поэтому рука переставала показываться после
        /// первого же подобранного предмета: у чистого хоста без лута всё работало, у игрока с
        /// вещами — нет.
        /// </summary>
        private static bool RigFree(SharedHandsController hands)
        {
            if (hands == null || hands.Hands == null)
            {
                return false;
            }

            ReferenceHub local;
            if (ReferenceHub.TryGetLocalHub(out local) && local.inventory != null)
            {
                return local.inventory.CurInstance == null;
            }

            return hands.Hands.runtimeAnimatorController == null;
        }

        public static void Hide()
        {
            if (_instance != null)
            {
                _instance._target = null;
            }
        }

        private void Hijack(SharedHandsController hands)
        {
            if (_hijacked)
            {
                return;
            }

            hands.Hands.gameObject.SetActive(true);

            // Снимаем чужой аниматор на время нашей позы: он остаётся на риге от последнего
            // предмета и продолжает крутить его анимацию по пустым рукам. Возвращаем в Restore.
            _savedController = hands.Hands.runtimeAnimatorController;
            hands.Hands.runtimeAnimatorController = null;

            // Сами меши кистей, перчаток и рукавов лежат выключенными: нужный набор включается
            // отдельно, под роль. Без этого вызова риг активен, но нарисовать нечего.
            ReferenceHub local;
            if (ReferenceHub.TryGetLocalHub(out local) && local.roleManager.CurrentRole != null)
            {
                SharedHandsController.SetRoleGloves(local.roleManager.CurrentRole.RoleTypeId);
            }

            // Камера вьюмодели включена всегда, но без предмета её маска обнулена — вернём ей слой.
            Transform workspace = hands.Hands.transform.parent;
            _viewmodelCamera = workspace != null && workspace.parent != null
                ? workspace.parent.GetComponent<Camera>()
                : null;

            if (_viewmodelCamera != null)
            {
                _savedCullingMask = _viewmodelCamera.cullingMask;
                _viewmodelCamera.cullingMask |= 1 << ViewmodelLayer;
                EnsureFillLight();
            }
            EnsureFaceLight();

            ApplyRigPlacement(hands.Hands.transform);
            _hijacked = true;
        }

        /// <summary>
        /// Подсветка для руки.
        ///
        /// Штатно вьюмодель освещается вместе с предметом, а с пустыми руками её не освещает никто:
        /// на кадре 2026-09-04 рука была чёрным силуэтом даже в ярко освещённой белой комнате.
        /// Маска источника — ТОЛЬКО слой вьюмодели, поэтому на саму сцену он не влияет.
        /// </summary>
        private void EnsureFillLight()
        {
            // This film shot uses the original body skin and the room lights.
            // Keep ordinary gameplay lighting, but avoid extra pools on floor/ceiling.
            if (Scp939ModCompatibility.Filming && (Scp939InteractionPoser.CinemaSoftPet ||
                (Scp939ModCompatibility.Host != null && Scp939ModCompatibility.Motion(Scp939ModCompatibility.Host) is Scp939ModCompatibility.OptionalMotion motion && motion.PairedWalk))) return;
            if (_fillLight != null)
            {
                return;
            }

            var go = new GameObject("SCP939 Hand Fill Light");
            go.transform.SetParent(_viewmodelCamera.transform, false);
            go.transform.localPosition = new Vector3(0.25f, 0.15f, 0.15f);

            _fillLight = go.AddComponent<Light>();
            _fillLight.type = LightType.Point;
            _fillLight.color = new Color(1f, 0.96f, 0.92f);
            _fillLight.intensity = 2.4f;
            _fillLight.range = 4f;
            _fillLight.shadows = LightShadows.None;
            _fillLight.cullingMask = 1 << ViewmodelLayer;
        }

        /// <summary>
        /// Мягкий тёплый свет от камеры на саму 939, пока длится ласка или объятие. В упор, да ещё в
        /// тёмном коридоре, её модель была сплошным чёрным пятном на весь экран (кадр пользователя
        /// 2026-09-28). Маска — всё, КРОМЕ вьюмодели: у рук своя подсветка. Малая дальность — светит
        /// только то, что вплотную к камере, окружение почти не трогает.
        /// </summary>
        private void EnsureFaceLight()
        {
            // This film shot uses the original body skin and the room lights.
            // Keep ordinary gameplay lighting, but avoid extra pools on floor/ceiling.
            if (Scp939ModCompatibility.Filming && (Scp939InteractionPoser.CinemaSoftPet ||
                (Scp939ModCompatibility.Host != null && Scp939ModCompatibility.Motion(Scp939ModCompatibility.Host) is Scp939ModCompatibility.OptionalMotion motion && motion.PairedWalk))) return;
            if (_faceLight != null)
            {
                return;
            }
            Camera main = Camera.main;
            if (main == null)
            {
                return;
            }

            // Film may prewarm this same light before the menu to avoid a light pop.
            var prepared = main.transform.Find("SCP939 Face Fill Light");
            if (Scp939ModCompatibility.Filming && prepared != null)
            {
                _faceLight = prepared.GetComponent<Light>();
                if (_faceLight != null) return;
            }
            var go = new GameObject("SCP939 Face Fill Light");
            go.transform.SetParent(main.transform, false);
            go.transform.localPosition = new Vector3(0f, 0.12f, 0.05f);

            _faceLight = go.AddComponent<Light>();
            _faceLight.type = LightType.Point;
            _faceLight.color = new Color(1f, 0.88f, 0.84f);
            _faceLight.intensity = 1.6f;
            _faceLight.range = 2.6f;
            _faceLight.shadows = LightShadows.None;
            _faceLight.cullingMask = ~(1 << ViewmodelLayer);
        }

        private static void ApplyRigPlacement(Transform rig)
        {
            rig.localPosition = RigOffset;
            rig.localRotation = RigRotation;
            rig.localScale = Vector3.one * RigScale;
        }

        private void Restore()
        {
            var hands = SharedHandsController.Singleton;
            if (hands != null && hands.Hands != null)
            {
                if (_savedController != null)
                {
                    hands.Hands.runtimeAnimatorController = _savedController;
                    _savedController = null;
                }
                hands.Hands.gameObject.SetActive(false);
            }

            if (_fillLight != null)
            {
                Destroy(_fillLight.gameObject);
                _fillLight = null;
            }
            if (_faceLight != null)
            {
                Destroy(_faceLight.gameObject);
                _faceLight = null;
            }

            if (_viewmodelCamera != null)
            {
                _viewmodelCamera.cullingMask = _savedCullingMask;
                _viewmodelCamera = null;
            }

            _hijacked = false;
        }

        private void LateUpdate()
        {
            var hands = SharedHandsController.Singleton;

            // Риг занят вьюмоделью предмета. Если мы ещё не начали — ждём (игрок как раз убирает
            // предмет), а если уже держали руку и игрок что-то достал — уступаем: драться с
            // анимацией предмета нельзя.
            if (_target != null && !RigFree(hands))
            {
                if (_hijacked)
                {
                    _target = null;
                    _weight = 0f;
                    Restore();
                    enabled = false;
                    return;
                }

                if (Time.time > _waitUntil)
                {
                    Debug.Log("[939] рука не показана: риг занят предметом дольше " + ShowGraceTime + " с");
                    _target = null;
                    enabled = false;
                }
                return;
            }

            // Риг освободился, а мы ещё не захватили его — начинаем сейчас.
            if (_target != null && !_hijacked && hands != null)
            {
                Hijack(hands);
            }

            bool wanted = _target != null && (_endsAt < 0f || Time.time < _endsAt);
            _weight = Mathf.MoveTowards(_weight, wanted ? 1f : 0f, Time.deltaTime / BlendTime);

            if (!wanted && _weight <= 0f)
            {
                if (_hijacked)
                {
                    Restore();
                }
                enabled = false;
                return;
            }

            if (hands == null || hands.Hands == null || !_hijacked || _viewmodelCamera == null)
            {
                return;
            }

            // Маску возвращаем КАЖДЫЙ кадр: система вьюмоделей сбрасывает её в ноль, пока в руках
            // ничего нет, и одной установки при включении не хватает — рука остаётся невидимой.
            _viewmodelCamera.cullingMask |= 1 << ViewmodelLayer;

            // Позицию рига каждый кадр править не нужно — он вложен в рабочую область под камерой
            // вьюмодели и едет за головой сам. Достаточно держать заданную посадку.
            Transform rig = hands.Hands.transform;
            ApplyRigPlacement(rig);

            Gesture g = _kind == Scp939InteractionKind.Hug ? HugGestureRight
                      : _kind == Scp939InteractionKind.HandHold ? HoldGesture
                      : PetGesture;

            PoseSide(rig, _weight, true, g);

            // Левую руку в объятии тоже выводим вперёд, в остальных случаях — убираем вниз.
            // Без этого она остаётся в бинд-позе, а в ней предплечье лежит поперёк объектива
            // (замер: кисть в −0.11, то есть ПОЗАДИ камеры) — на экране это выглядело как вторая
            // гигантская лапа сверху слева.
            if (_kind == Scp939InteractionKind.Hug)
            {
                PoseSide(rig, _weight, false, HugGestureLeft);
            }
            else
            {
                Gesture tuck = new Gesture { Side = 0.45f, Down = 1.00f, Forward = 0.30f };
                PoseSide(rig, _weight, false, tuck);
            }
        }

        private void PoseSide(Transform rig, float weight, bool right, Gesture g)
        {
            string suffix = right ? ".R" : ".L";
            Transform upper = Find(rig, "arm" + suffix);
            Transform lower = Find(rig, "forearm" + suffix);
            Transform hand = Find(rig, "hand" + suffix);
            if (upper == null || lower == null || hand == null)
            {
                return;
            }

            // Санитар. Один NaN в этой цепочке необратим: кисть считается от собственной позиции
            // (Lerp с прошлым кадром), длина руки — от кисти, цель — от длины. Стоит любому звену
            // разъехаться, и кость улетает в «не число» насовсем — в кадре рука просто исчезает,
            // а WorldToViewportPoint отдаёт мусор вида 1e37 (поймано замером 2026-09-04).
            // Поэтому при первом же NaN возвращаем кость в запомненную позу и ждём следующий кадр.
            if (!IsFinite(hand.position) || !IsFinite(lower.position) || !IsFinite(upper.position))
            {
                ResetSide(right, upper, lower, hand);
                return;
            }

            int i = right ? 0 : 1;
            if (_forearmLength[i] <= 0f)
            {
                float length = Vector3.Distance(lower.position, hand.position);
                Vector3 toHand = lower.InverseTransformPoint(hand.position).normalized;
                if (length <= 0f || !IsFinite(toHand))
                {
                    return;   // риг ещё не в рабочем состоянии — замерим на следующем кадре
                }

                _forearmLength[i] = length;
                _armReach[i] = ArmLength(upper, lower, hand);
                _forearmToHand[i] = toHand;
                _handLocal[i] = Quaternion.Inverse(lower.rotation) * hand.rotation;
                _bindLocalPos[i] = hand.localPosition;
                _bindLocalRot[i] = hand.localRotation;
                _bindUpperRot[i] = upper.localRotation;
                _bindLowerRot[i] = lower.localRotation;
                _bindSaved[i] = true;
            }

            // Цель задаётся ОТНОСИТЕЛЬНО КАМЕРЫ, а не от плеча.
            //
            // Вьюмодель рисуется вплотную к камере отдельной камерой: целиться её рукой в реальную
            // голову 939, которая в метре, бессмысленно — рука вытягивается до предела и всё равно
            // не достаёт. Нужен жест: ладонь перед собой, как будто гладишь то, что видишь.
            // От плеча считать тоже нельзя: посадкой рига оно смещено вниз и вперёд, и ладонь
            // оказывалась вплотную к объективу.
            Transform basis = _viewmodelCamera.transform;
            float reach = _armReach[i] > 0f ? _armReach[i] : ArmLength(upper, lower, hand);
            float side = right ? g.Side : -g.Side;
            if (g.AimAtHead) side = 0f;

            // Куда «смотрит» рука. Обычно просто вперёд, но в поглаживании — на реальную голову
            // 939: камера вьюмодели стоит в позиции камеры игрока, поэтому направление на неё
            // из мира переносится в пространство вьюмодели один в один.
            Vector3 aim = basis.forward;
            Transform head;
            if (g.AimAtHead && TryFindTargetHead(out head))
            {
                Vector3 crown = Scp939InteractionPoser.PetCrown(head);
                Camera world = _target != null && Camera.main != null ? Camera.main : _viewmodelCamera;
                Vector3 viewport = world.WorldToViewportPoint(crown);
                Vector3 toHead = _viewmodelCamera.ViewportPointToRay(viewport).direction;
                if (toHead.sqrMagnitude > 1e-6f)
                {
                    aim = toHead.normalized;
                }
            }

            if (_kind == Scp939InteractionKind.HandHold && right && _target != null)
            {
                var role=_target.roleManager.CurrentRole as FirstPersonControl.IFpcRole;
                var model=role?.FpcModule.CharacterModelInstance;
                if(model!=null)
                {
                    var fingers=Find(model.transform,"DEF-Hand.L");
                    if(fingers!=null)
                    {
                        Camera world=Camera.main != null ? Camera.main : _viewmodelCamera;
                        Vector3 viewport=world.WorldToViewportPoint(fingers.position);
                        aim=_viewmodelCamera.ViewportPointToRay(viewport).direction;
                        side=0; g.Down=0;
                    }
                }
            }

            Vector3 up = Vector3.ProjectOnPlane(basis.up, aim).normalized;
            if (up.sqrMagnitude < 1e-6f)
            {
                // Смотрим строго вдоль луча на голову — «верх» вырождается. Берём любой перпендикуляр,
                // иначе дальше в базис уходят нули, а из них — вырожденный полюс и NaN в IK.
                up = Vector3.ProjectOnPlane(Vector3.up, aim).normalized;
                if (up.sqrMagnitude < 1e-6f)
                {
                    up = Vector3.Cross(aim, Vector3.right).normalized;
                }
            }
            Vector3 across = Vector3.Cross(up, aim).normalized;

            Vector3 target = basis.position
                           + aim * (reach * g.Forward)
                           + across * (reach * side)
                           - up * (reach * (g.AimAtHead ? .30f : g.Down));

            if (!IsFinite(target))
            {
                return;
            }

            if (g.Stroke > 0f && !g.AimAtHead)
            {
                // Поглаживание идёт ПОПЕРЁК головы, а не вдоль луча взгляда: движение вперёд-назад
                // от первого лица почти незаметно, а вбок читается сразу.
                target += across * (Mathf.Sin(Time.time * 2.2f) * reach * g.Stroke);
            }

            Vector3 pole = ((right ? across : -across) - up * 0.8f).normalized;
            Scp939InteractionPoser.SolveIk(upper, lower, hand, target, pole, weight);

            // Предплечье доворачиваем по запомненному направлению на кисть: целиться так можно,
            // хотя кость запястья и не является его ребёнком.
            Vector3 currentDir = lower.TransformDirection(_forearmToHand[i]);
            Vector3 wantDir = (target - lower.position).normalized;
            if (currentDir.sqrMagnitude > 1e-8f && wantDir.sqrMagnitude > 1e-8f)
            {
                Quaternion delta = Quaternion.FromToRotation(currentDir, wantDir);
                lower.rotation = Quaternion.Slerp(lower.rotation, delta * lower.rotation, weight);
            }

            // И, наконец, само запястье: оно ведёт за собой пальцы и меш кисти.
            Quaternion want = lower.rotation * _handLocal[i];
            if (Mathf.Abs(g.PalmTwist) > 0.01f && wantDir.sqrMagnitude > 1e-8f)
            {
                // Доворот вокруг оси предплечья — им разворачиваем ладонь вниз (гладить) или
                // внутрь (обнимать). Знак зеркалим для левой руки. Ось обязана быть ненулевой:
                // AngleAxis вокруг нулевого вектора возвращает кватернион из NaN.
                want = Quaternion.AngleAxis(right ? g.PalmTwist : -g.PalmTwist, wantDir) * want;
            }

            Vector3 newPos = Vector3.Lerp(hand.position, target, weight);
            if (IsFinite(newPos))
            {
                hand.position = newPos;
            }
            if (IsFinite(want))
            {
                hand.rotation = Quaternion.Slerp(hand.rotation, want, weight);
            }
        }

        /// <summary>Возвращает кости стороны в позу, снятую при первом позировании.</summary>
        private void ResetSide(bool right, Transform upper, Transform lower, Transform hand)
        {
            int i = right ? 0 : 1;
            if (!_bindSaved[i])
            {
                return;
            }
            upper.localRotation = _bindUpperRot[i];
            lower.localRotation = _bindLowerRot[i];
            hand.localPosition = _bindLocalPos[i];
            hand.localRotation = _bindLocalRot[i];
        }

        private static bool IsFinite(Vector3 v)
        {
            return !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z) &&
                   !float.IsInfinity(v.x) && !float.IsInfinity(v.y) && !float.IsInfinity(v.z);
        }

        private static bool IsFinite(Quaternion q)
        {
            return !float.IsNaN(q.x) && !float.IsNaN(q.y) && !float.IsNaN(q.z) && !float.IsNaN(q.w) &&
                   (q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w) > 1e-6f;
        }

        /// <summary>
        /// Кость головы того, к кому тянемся. У 939 риг Rigify (DEF-Head), у человека — Mixamo,
        /// поэтому проверяем оба имени. Найденное запоминаем: перебор иерархии каждый кадр не нужен.
        /// </summary>
        private bool TryFindTargetHead(out Transform head)
        {
            if (_targetHead != null)
            {
                head = _targetHead;
                return true;
            }

            head = null;
            if (_target == null || _target.roleManager.CurrentRole == null)
            {
                return false;
            }

            var fpc = _target.roleManager.CurrentRole as FirstPersonControl.IFpcRole;
            if (fpc == null || fpc.FpcModule.CharacterModelInstance == null)
            {
                return false;
            }

            foreach (Transform t in fpc.FpcModule.CharacterModelInstance.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "DEF-Head" || t.name == "mixamorig:Head")
                {
                    _targetHead = t;
                    head = t;
                    return true;
                }
            }
            return false;
        }

        /// <summary>Длина руки рига — по ней задаётся вылет жеста, чтобы не зависеть от масштаба.</summary>
        private static float ArmLength(Transform upper, Transform lower, Transform hand)
        {
            return Vector3.Distance(upper.position, lower.position) + Vector3.Distance(lower.position, hand.position);
        }

        private static Transform Find(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == name)
                {
                    return t;
                }
            }
            return null;
        }
    }
}
