using MapGeneration;
using UnityEngine;

namespace PlayerRoles.PlayableScps.Scp939
{
    /// <summary>
    /// Место свидания с SCP-939 — площадка башни на поверхности (там же, где спавнится Tutorial).
    ///
    /// Почему башня, а не блок класса D. В камерах блока D столик стоял в тесной клетке: вокруг
    /// решётки и дверные косяки, света нет, а половина ракурсов упирается в стену. Площадка башни —
    /// ровная, открытая и высокая: свечи и гирлянда читаются на ночном небе, а вокруг видно всю
    /// поверхность. Точка взята от штатного спавна Tutorial (локальные (40, 14.08, -32.6) в комнате
    /// Outside — см. TutorialRole.prefab, _spawnpoints), потому что это единственная гарантированно
    /// ровная и проходимая площадка башни во всей сцене.
    ///
    /// Почему не правка сцены или префаба комнаты. Карта собирается процедурно из комнат, поэтому в
    /// сцене столу негде лежать. А префабы комнат в этом проекте — рипнутые ассеты, править их
    /// рискованно (см. заметки о хрупкости). Здесь же комплект — обычный локальный объект, который
    /// каждая сторона создаёт у себя одинаково: позиция комнаты детерминирована, поэтому сервер и
    /// клиенты получают его в одном и том же месте без всякой синхронизации.
    /// </summary>
    public static class Scp939DateSpot
    {
        /// <summary>Комната, в которой стоит комплект. Поверхность — это одна комната Outside.</summary>
        private const RoomName TargetRoom = RoomName.Outside;

        /// <summary>
        /// Опорная точка площадки башни в локальных координатах комнаты Outside: штатный спавн
        /// обучения. Мировые координаты у неё около (40, 1014.1, -32.6).
        /// </summary>
        private static readonly Vector3 TowerAnchorLocal = new Vector3(40f, 14.08f, -32.6f);

        /// <summary>
        /// Сколько метров вокруг опорной точки перебирать в поисках места и с каким шагом.
        ///
        /// Слепое смещение не годится: на площадке есть перила, стойки и невидимые Collision Box,
        /// а провалиться мимо края башни — это смерть при телепорте. Поэтому место ищется перебором
        /// с проверкой пола И свободного объёма, а из подходящих берётся самое просторное.
        /// </summary>
        private const float SearchRadius = 3.6f;

        private const float SearchStep = 0.9f;

        /// <summary>Полуразмер объёма, который должен быть свободен вокруг комплекта.</summary>
        private static readonly Vector3 ClearanceHalfExtents = new Vector3(1.30f, 0.60f, 1.60f);

        /// <summary>С какой высоты искать пол лучом.</summary>
        private const float FloorProbeHeight = 3.5f;

        /// <summary>
        /// Зазор над сиденьем при посадке. Ставить ровно в уровень пола нельзя: капсула
        /// контроллера оказывается наполовину в геометрии и участников проваливает вниз.
        /// </summary>
        public const float SeatClearance = 0.15f;

        private static GameObject _instance;
        private static Transform _seatPlayer;
        private static Transform _seatScp;
        private static Transform _chairPlayer;
        private static Transform _plateScp;

        /// <summary>Готов ли комплект — только тогда можно звать на свидание.</summary>
        public static bool Exists => _instance != null;

        public static Transform SeatPlayer => _seatPlayer;

        public static Transform SeatScp => _seatScp;

        /// <summary>
        /// Стул партнёра — туда садится думми (он позируется сидящим). Живой игрок стоит у стула:
        /// от первого лица своей модели он не видит, а камера на высоте сидящего упиралась бы в стол.
        /// </summary>
        public static Transform ChairPlayer => _chairPlayer;

        /// <summary>Её тарелка — к ней она склоняется за ужином.</summary>
        public static Transform PlateScp => _plateScp;

        /// <summary>
        /// Прячет съеденные фрикадельки на её тарелке. Число, а не «минус одна»: его шлёт сервер,
        /// и у клиента, подключившегося посреди ужина, тарелка всё равно совпадёт.
        /// </summary>
        public static void SetMeatballsEaten(int eaten)
        {
            if (_plateScp == null)
            {
                return;
            }
            for (int i = 0; i < 3; i++)
            {
                Transform ball = _plateScp.Find("Meatball " + i);
                if (ball != null)
                {
                    ball.gameObject.SetActive(i >= eaten);
                }
            }
        }

        [RuntimeInitializeOnLoadMethod]
        private static void Init()
        {
            // Отписка перед подпиской и сброс ссылок: если редактор не перезагрузил домен между
            // запусками, статики переживают сессию — иначе получим двойную подписку (два стола)
            // и ссылку на уничтоженный объект прошлой сессии.
            _instance = null;
            _seatPlayer = null;
            _seatScp = null;
            _chairPlayer = null;
            _plateScp = null;

            // Комнаты появляются не сразу — ждём, пока генератор карты добавит нужную.
            RoomIdentifier.OnAdded -= OnRoomAdded;
            RoomIdentifier.OnAdded += OnRoomAdded;
            RoomIdentifier.OnRemoved -= OnRoomRemoved;
            RoomIdentifier.OnRemoved += OnRoomRemoved;
        }

        private static void OnRoomAdded(RoomIdentifier room)
        {
            if (!Scp939ModSettings.Enabled("dates") || room == null || room.Name != TargetRoom || _instance != null)
            {
                return;
            }
            Build(room);
        }

        private static void OnRoomRemoved(RoomIdentifier room)
        {
            // Карта пересобирается между раундами — комплект уезжает вместе с комнатой.
            if (room != null && room.Name == TargetRoom && _instance != null)
            {
                Object.Destroy(_instance);
                _instance = null;
                _seatPlayer = null;
                _seatScp = null;
                _chairPlayer = null;
                _plateScp = null;
            }
        }

        /// <summary>Ставит комплект в комнату. Безопасно звать повторно.</summary>
        public static void Build(RoomIdentifier room)
        {
            if (_instance != null || room == null)
            {
                return;
            }

            var prefab = Resources.Load<GameObject>("Scp939DateTable");
            if (prefab == null)
            {
                Debug.LogWarning("[939] Префаб комплекта не найден. Собери его: меню SCP/939/Build Date Table.");
                return;
            }

            Vector3 local;
            float yaw;
            if (!PickSpot(room, out local, out yaw))
            {
                Debug.LogWarning("[939] На площадке башни не нашлось места под столик — ставлю в опорную точку.");
                local = TowerAnchorLocal;
                yaw = 0f;
            }

            _instance = Object.Instantiate(prefab, room.transform);
            _instance.name = "SCP939 Date Table";
            _instance.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            _instance.transform.localPosition = local;

            _seatPlayer = _instance.transform.Find("Seat Player");
            _seatScp = _instance.transform.Find("Seat Scp939");
            _chairPlayer = _instance.transform.Find("Chair A");
            _plateScp = _instance.transform.Find("Pasta B");
            AddTableCollider(_instance.transform);

            Debug.Log("[939] Столик свидания поставлен в башне: мир " +
                      _instance.transform.position.ToString("0.00") + ", поворот " + yaw.ToString("0"));
        }

        /// <summary>
        /// Коллайдер стола. Мебель в префабе без коллайдеров (примитивам их снимают, чтобы они не
        /// ловили лучи наведения), и думми проходили сквозь стол. Ставим один ящик на всю
        /// столешницу — точной формы ножек тут не нужно.
        /// </summary>
        private static void AddTableCollider(Transform root)
        {
            Transform cloth = root.Find("Tablecloth/Sheet");
            float top = cloth != null ? cloth.localPosition.y : 0.78f;
            var go = new GameObject("Table Collider");
            go.transform.SetParent(root, false);
            var box = go.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, top * 0.5f, 0f);
            box.size = new Vector3(1.56f, top, 1.16f);
        }

        /// <summary>
        /// Ищет место на площадке башни: перебирает точки вокруг опорной, для каждой требует пол
        /// под ней и свободный объём вокруг, а из подходящих берёт ту, где просторнее всего и
        /// ближе к центру площадки. Заодно подбирает поворот — стол разворачивается так, чтобы за
        /// спинами сидящих было больше открытого пространства, а не стена.
        /// </summary>
        private static bool PickSpot(RoomIdentifier room, out Vector3 local, out float yaw)
        {
            local = TowerAnchorLocal;
            yaw = 0f;

            float bestScore = float.NegativeInfinity;
            bool found = false;

            for (float dx = -SearchRadius; dx <= SearchRadius + 0.01f; dx += SearchStep)
            {
                for (float dz = -SearchRadius; dz <= SearchRadius + 0.01f; dz += SearchStep)
                {
                    Vector3 candidateLocal = TowerAnchorLocal + new Vector3(dx, 0f, dz);
                    Vector3 world = room.transform.TransformPoint(candidateLocal);

                    RaycastHit hit;
                    if (!Physics.Raycast(world + Vector3.up * FloorProbeHeight, Vector3.down, out hit,
                                         FloorProbeHeight * 2f, ~0, QueryTriggerInteraction.Ignore))
                    {
                        continue;   // пола нет — это край башни, туда нельзя
                    }

                    // Пол должен быть ИМЕННО площадкой башни, а не землёй далеко внизу: иначе
                    // комплект уедет с башни на поверхность, а участники — вместе с ним.
                    if (Mathf.Abs(hit.point.y - world.y) > 1.5f)
                    {
                        continue;
                    }

                    Vector3 center = hit.point + Vector3.up * (ClearanceHalfExtents.y + 0.08f);
                    if (Physics.CheckBox(center, ClearanceHalfExtents, Quaternion.identity, ~0,
                                         QueryTriggerInteraction.Ignore))
                    {
                        continue;   // тесно: перила, стойка или стена
                    }

                    // Из свободных точек берём самую просторную, при равенстве — ближе к опорной.
                    float openness = Openness(hit.point + Vector3.up * 0.6f);
                    float score = openness - 0.35f * new Vector2(dx, dz).magnitude;
                    if (score <= bestScore)
                    {
                        continue;
                    }

                    bestScore = score;
                    local = room.transform.InverseTransformPoint(hit.point);
                    yaw = BestYaw(hit.point + Vector3.up * 0.6f) - room.transform.rotation.eulerAngles.y;
                    found = true;
                }
            }

            return found;
        }

        /// <summary>Средняя дистанция до преград по кругу — насколько точка «на открытом месте».</summary>
        private static float Openness(Vector3 point)
        {
            const float maxDistance = 6f;
            float sum = 0f;
            for (int i = 0; i < 8; i++)
            {
                sum += RayDistance(point, Quaternion.Euler(0f, i * 45f, 0f) * Vector3.forward, maxDistance);
            }
            return sum / 8f;
        }

        /// <summary>
        /// Куда развернуть комплект: вдоль самого длинного свободного направления. Стулья стоят по
        /// оси Z префаба, поэтому «длинная» сторона должна смотреть туда, где больше места.
        /// </summary>
        private static float BestYaw(Vector3 point)
        {
            float bestYaw = 0f;
            float best = -1f;
            for (int i = 0; i < 8; i++)
            {
                float angle = i * 45f;
                Vector3 dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                // Нужна пара противоположных направлений: сидят-то друг напротив друга.
                float span = RayDistance(point, dir, 6f) + RayDistance(point, -dir, 6f);
                if (span > best)
                {
                    best = span;
                    bestYaw = angle;
                }
            }
            return bestYaw;
        }

        private static float RayDistance(Vector3 origin, Vector3 dir, float max)
        {
            RaycastHit hit;
            return Physics.Raycast(origin, dir, out hit, max, ~0, QueryTriggerInteraction.Ignore)
                ? hit.distance
                : max;
        }

        /// <summary>
        /// Уровень пола под точкой. Если луч не находит опоры, возвращает высоту самой точки —
        /// это хуже, чем реальный пол, но не роняет участника в бездну.
        /// </summary>
        public static float GroundY(Vector3 point)
        {
            // Именно RaycastAll с отсевом игроков. Обычный луч цепляет капсулу того, кого уже
            // посадили за стол (замер 2026-09-04: он давал «пол» на 1.77 выше настоящего), и
            // второго участника посадило бы ему на голову — на стол или в воздух.
            RaycastHit[] hits = Physics.RaycastAll(point + Vector3.up * FloorProbeHeight, Vector3.down,
                                                   FloorProbeHeight * 2f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity;
            for (int i = 0; i < hits.Length; i++)
            {
                ReferenceHub hub;
                if (ReferenceHub.TryGetHub(hits[i].collider.gameObject, out hub))
                {
                    continue;   // это игрок, а не пол
                }
                if (hits[i].point.y > best)
                {
                    best = hits[i].point.y;
                }
            }
            return best > float.NegativeInfinity ? best : point.y;
        }

        /// <summary>Находит комнату и ставит комплект, если генератор уже отработал до подписки.</summary>
        public static bool TryBuildNow()
        {
            if (_instance != null)
            {
                return true;
            }
            foreach (RoomIdentifier room in RoomIdentifier.AllRoomIdentifiers)
            {
                if (room != null && room.Name == TargetRoom)
                {
                    Build(room);
                    return _instance != null;
                }
            }
            return false;
        }
    }
}
