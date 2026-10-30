using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PlayerRoles.PlayableScps.Scp939
{
    /// <summary>
    /// Меню действий с SCP-939 и подсказка «нажми E» под прицелом.
    ///
    /// Управление намеренно НЕ курсором мыши: в этом проекте любое переключение фокуса ввода на UI
    /// уже приводило к тому, что у игрока переставали работать WASD (та же грабля ловилась на панели
    /// Remote Admin). Поэтому выбор — колесом мыши или стрелками, подтверждение — клавишей
    /// взаимодействия (E) или Enter, назад — Backspace. Цифры НЕ используются: 1 и 2 — хоткеи оружия.
    ///
    /// Вторая версия (2026-09-28, «меню всё ещё не совершенно»): пункты разбиты по разделам,
    /// пояснение выбранного пункта вынесено в отдельную строку внизу (раньше хвост подписи тянул строку
    /// вширь), недоступные пункты не прячутся, а серые с причиной — раскладка не прыгает между
    /// состояниями. В шапке — имя, уровень отношений и полоска симпатии. Появление, подсветка и
    /// выбор анимированы и озвучены.
    ///
    /// Canvas собирается кодом, а не префабом: меню целиком живёт в скриптах и переносится вместе
    /// с ними, без правок сцены.
    /// </summary>
    public class Scp939ActionMenu : MonoBehaviour
    {
        /// <summary>Строка меню: пункт, заголовок раздела или недоступный пункт.</summary>
        public struct Entry
        {
            public readonly string Label;
            public readonly string Hint;
            public readonly System.Action Action;
            public readonly bool IsSection;
            public readonly bool IsDisabled;

            public Entry(string label, System.Action action, string hint = null)
                : this(label, action, hint, false, false)
            {
            }

            private Entry(string label, System.Action action, string hint, bool section, bool disabled)
            {
                Label = label;
                Hint = hint;
                Action = action;
                IsSection = section;
                IsDisabled = disabled;
            }

            /// <summary>Заголовок раздела — не выбирается, только делит список.</summary>
            public static Entry Section(string title)
            {
                return new Entry(title, null, null, true, false);
            }

            /// <summary>Пункт, который сейчас нельзя выбрать; <paramref name="reason"/> — почему.</summary>
            public static Entry Disabled(string label, string reason)
            {
                return new Entry(label, null, reason, false, true);
            }

            public bool Selectable => !IsSection && !IsDisabled;
        }

        /// <summary>
        /// Действие «ничего не делать»: меню не закрывается и не перерисовывается. Для пунктов-шуток.
        /// </summary>
        public static readonly System.Action Nothing = delegate { };

        private const int MaxRows = 18;
        private const float RowHeight = 34f;
        private const float SectionHeight = 24f;
        private const float SlideDistance = 60f;
        private const float AppearTime = 0.16f;

        /// <summary>Фирменный цвет 939 в интерфейсе.</summary>
        private static readonly Color Accent = new Color(1f, 0.42f, 0.52f);

        private static Scp939ActionMenu _instance;

        private readonly List<Entry> _entries = new List<Entry>();
        private readonly List<Image> _rowBacks = new List<Image>();
        private readonly List<TMP_Text> _rowTexts = new List<TMP_Text>();
        private readonly List<LayoutElement> _rowLayouts = new List<LayoutElement>();
        private readonly List<float> _rowGlow = new List<float>();

        private GameObject _root;
        private RectTransform _panelRect;
        private CanvasGroup _group;
        private TMP_Text _title;
        private TMP_Text _level;
        private TMP_Text _subtitle;
        private TMP_Text _hint;
        private TMP_Text _footer;
        private GameObject _barRoot;
        private Image _barFill;
        private int _index;
        private System.Action _onBack;
        private float _appear;
        private bool _closing;

        // Подсказка под прицелом — отдельный корень, живёт независимо от панели.
        private GameObject _promptRoot;
        private TMP_Text _promptText;

        public static bool IsOpen => _instance != null && _instance._root != null && _instance._root.activeSelf && !_instance._closing;

        /// <summary>Выбранная строка — для автосъёмки: сценарий листает меню стрелками до нужного пункта.</summary>
        public static int SelectedIndex => IsOpen ? _instance._index : -1;

        /// <summary>Индекс первого выбираемого пункта, чья подпись содержит <paramref name="labelPart"/>; -1 — нет такого.</summary>
        public static int FindEntry(string labelPart)
        {
            if (!IsOpen || string.IsNullOrEmpty(labelPart))
            {
                return -1;
            }
            for (int i = 0; i < _instance._entries.Count; i++)
            {
                Entry entry = _instance._entries[i];
                if (entry.Selectable && entry.Label != null &&
                    entry.Label.IndexOf(labelPart, System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>Сколько нажатий стрелки (со знаком: − вверх, + вниз) до пункта <paramref name="target"/>.</summary>
        public static int StepsTo(int target)
        {
            if (!IsOpen || target < 0)
            {
                return 0;
            }
            int steps = 0;
            int index = _instance._index;
            // Идём вниз по выбираемым пунктам — так же, как пошла бы стрелка, без обхода по кругу.
            int dir = target > index ? 1 : -1;
            while (index != target && Mathf.Abs(steps) < _instance._entries.Count)
            {
                int next = _instance.NextSelectable(index, dir);
                if (next == index)
                {
                    break;
                }
                index = next;
                steps += dir;
            }
            return index == target ? steps : 0;
        }

        /// <summary>
        /// Открывает меню. <paramref name="onBack"/> — что делать по Backspace: в подменю это
        /// возврат на уровень выше, в корневом меню null (тогда Backspace просто закрывает).
        /// </summary>
        public static void Open(string title, string subtitle, IList<Entry> entries, System.Action onBack = null)
        {
            Ensure();
            bool wasOpen = IsOpen;
            _instance._onBack = onBack;
            _instance.Fill(title, subtitle, entries);
            if (!wasOpen)
            {
                _instance._appear = 0f;
            }
        }

        /// <summary>
        /// Уровень отношений в шапке. value &lt; 0 — прятать полоску (например, в подменю подарков
        /// она не нужна и только отнимает место).
        /// </summary>
        public static void SetAffection(float value, string levelTitle)
        {
            Ensure();
            bool show = Scp939ModSettings.Enabled("dialogue") && Scp939Ui.RelationshipRevealed && value >= 0f;
            _instance._barRoot.SetActive(show);
            _instance._level.gameObject.SetActive(show && !string.IsNullOrEmpty(levelTitle));
            if (show)
            {
                _instance._barFill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(value / 100f), 1f);
                _instance._level.text = levelTitle + "  <color=#9A8F95>" + Mathf.RoundToInt(value) + "</color>";
            }
        }

        public static void Close()
        {
            if (_instance != null && _instance._root != null && _instance._root.activeSelf)
            {
                _instance._closing = true;
                _instance._entries.Clear();
                _instance._onBack = null;
            }
        }

        /// <summary>Подсказка под прицелом. null или пустая строка — спрятать.</summary>
        public static void SetPrompt(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                if (_instance != null && _instance._promptRoot != null)
                {
                    _instance._promptRoot.SetActive(false);
                }
                return;
            }

            Ensure();
            _instance._promptText.text = text;
            _instance._promptRoot.SetActive(true);
        }

        /// <summary>Обрабатывает ввод меню. true — нажатие израсходовано.</summary>
        public static bool HandleInput(KeyCode interactKey)
        {
            if (!IsOpen)
            {
                return false;
            }
            return _instance.HandleInputInternal(interactKey);
        }

        private bool HandleInputInternal(KeyCode interactKey)
        {
            if (_entries.Count == 0)
            {
                return false;
            }

            float wheel = Input.mouseScrollDelta.y;
            int step = 0;
            if (wheel > 0.01f || Input.GetKeyDown(KeyCode.UpArrow))
            {
                step = -1;
            }
            else if (wheel < -0.01f || Input.GetKeyDown(KeyCode.DownArrow))
            {
                step = 1;
            }

            if (step != 0)
            {
                int next = NextSelectable(_index, step);
                if (next != _index)
                {
                    _index = next;
                    Scp939Sfx.UiTick();
                    Refresh();
                }
            }

            if (Input.GetKeyDown(KeyCode.Backspace))
            {
                System.Action back = _onBack;
                Scp939Sfx.UiBack();
                if (back != null)
                {
                    back();
                }
                else
                {
                    Close();
                }
                return true;
            }

            bool confirm = Input.GetKeyDown(interactKey) ||
                           Input.GetKeyDown(KeyCode.Return) ||
                           Input.GetKeyDown(KeyCode.KeypadEnter);
            if (!confirm)
            {
                return false;
            }

            Entry entry = _entries[_index];
            if (!entry.Selectable)
            {
                return true;
            }

            Scp939Sfx.UiConfirm();
            System.Action action = entry.Action;
            if (action == Nothing)
            {
                return true;   // пункт-пустышка: меню остаётся как было
            }
            if (action == null)
            {
                Close();
                return true;
            }
            // Действие само решает, открыть ли следующее меню (подменю, «назад») — поэтому сначала
            // закрываем, а уже потом выполняем: иначе закрытие затёрло бы только что открытое подменю.
            Close();
            action();
            return true;
        }

        /// <summary>Следующая выбираемая строка в направлении <paramref name="dir"/>, по кругу.</summary>
        private int NextSelectable(int from, int dir)
        {
            int count = _entries.Count;
            for (int i = 1; i <= count; i++)
            {
                int candidate = ((from + dir * i) % count + count) % count;
                if (_entries[candidate].Selectable)
                {
                    return candidate;
                }
            }
            return from;
        }

        private static void Ensure()
        {
            if (_instance != null)
            {
                return;
            }
            var go = new GameObject("SCP939 Action Menu");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<Scp939ActionMenu>();
            _instance.Build();
        }

        // ------------------------------------------------------------------ сборка интерфейса

        private void Build()
        {
            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Выше игрового HUD, но обычным UI-слоем: перехватывать ввод меню не должно.
            canvas.sortingOrder = 500;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            BuildPanel(canvasGo.transform);
            BuildPrompt(canvasGo.transform);
        }

        private void BuildPanel(Transform parent)
        {
            _root = new GameObject("Panel", typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter), typeof(CanvasGroup));
            _root.transform.SetParent(parent, false);
            _group = _root.GetComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _group.interactable = false;

            _panelRect = _root.GetComponent<RectTransform>();
            // Справа по центру: в середине экрана меню перекрывало саму 939, внизу — её реплику.
            _panelRect.anchorMin = _panelRect.anchorMax = new Vector2(1f, 0.5f);
            _panelRect.pivot = new Vector2(1f, 0.5f);
            _panelRect.anchoredPosition = new Vector2(-64f, -20f);
            _panelRect.sizeDelta = new Vector2(520f, 0f);

            var bg = _root.GetComponent<Image>();
            bg.sprite = RoundedSprite(new Color(0.06f, 0.04f, 0.07f, 0.93f), new Color(1f, 0.42f, 0.52f, 0.60f), 2);
            bg.type = Image.Type.Sliced;
            bg.raycastTarget = false;

            var layout = _root.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(20, 20, 16, 14);
            layout.spacing = 2f;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = true;
            layout.childControlWidth = true;

            var fitter = _root.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            // Шапка: имя слева, уровень отношений справа.
            var header = new GameObject("Header", typeof(RectTransform), typeof(LayoutElement));
            header.transform.SetParent(_root.transform, false);
            header.GetComponent<LayoutElement>().preferredHeight = 36f;
            _title = MakeText(header.transform, "Title", 28f, Accent, TextAlignmentOptions.Left, 0f);
            Stretch(_title.rectTransform);
            _title.fontStyle = FontStyles.Bold;
            _level = MakeText(header.transform, "Level", 18f, new Color(1f, 0.86f, 0.9f), TextAlignmentOptions.Right, 0f);
            Stretch(_level.rectTransform);

            // Полоска симпатии под шапкой.
            _barRoot = new GameObject("Affection", typeof(Image), typeof(LayoutElement));
            _barRoot.transform.SetParent(_root.transform, false);
            _barRoot.GetComponent<LayoutElement>().preferredHeight = 6f;
            var track = _barRoot.GetComponent<Image>();
            track.color = new Color(1f, 0.42f, 0.52f, 0.16f);
            track.raycastTarget = false;
            var fill = new GameObject("Fill", typeof(Image));
            fill.transform.SetParent(_barRoot.transform, false);
            _barFill = fill.GetComponent<Image>();
            _barFill.color = Accent;
            _barFill.raycastTarget = false;
            var fillRect = _barFill.rectTransform;
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = new Vector2(0f, 1f);
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            _barRoot.SetActive(false);

            _subtitle = MakeText(_root.transform, "Subtitle", 16f, new Color(0.72f, 0.68f, 0.72f),
                                 TextAlignmentOptions.Left, 24f);

            for (int i = 0; i < MaxRows; i++)
            {
                MakeRow(_root.transform, i);
            }

            MakeSeparator(_root.transform);

            // Пояснение к выбранному пункту. Переносится на вторую строку, если длинное.
            _hint = MakeText(_root.transform, "Hint", 17f, new Color(0.86f, 0.80f, 0.84f), TextAlignmentOptions.Left, 0f);
            _hint.textWrappingMode = TextWrappingModes.Normal;
            _hint.fontStyle = FontStyles.Italic;
            var hintLayout = _hint.gameObject.AddComponent<LayoutElement>();
            hintLayout.minHeight = 24f;

            _footer = MakeText(_root.transform, "Footer", 14f, new Color(0.58f, 0.55f, 0.60f),
                               TextAlignmentOptions.Left, 20f);

            _root.SetActive(false);
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private void BuildPrompt(Transform parent)
        {
            _promptRoot = new GameObject("Prompt", typeof(Image), typeof(ContentSizeFitter), typeof(HorizontalLayoutGroup));
            _promptRoot.transform.SetParent(parent, false);

            var rect = _promptRoot.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            // Ниже прицела И ниже таблички с ником, иначе обе надписи читаются как одна каша.
            rect.anchoredPosition = new Vector2(0f, -205f);

            var bg = _promptRoot.GetComponent<Image>();
            bg.sprite = RoundedSprite(new Color(0.05f, 0.04f, 0.06f, 0.72f), new Color(1f, 0.42f, 0.52f, 0.45f), 2);
            bg.type = Image.Type.Sliced;
            bg.raycastTarget = false;

            var layout = _promptRoot.GetComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(16, 16, 8, 8);
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = false;

            var fitter = _promptRoot.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            _promptText = MakeText(_promptRoot.transform, "Text", 20f, Color.white, TextAlignmentOptions.Center, 0f);
            _promptRoot.SetActive(false);
        }

        private void MakeRow(Transform parent, int index)
        {
            var row = new GameObject("Row " + index, typeof(Image), typeof(LayoutElement));
            row.transform.SetParent(parent, false);

            var back = row.GetComponent<Image>();
            back.sprite = RoundedSprite(Color.white, new Color(0f, 0f, 0f, 0f), 0);
            back.type = Image.Type.Sliced;
            back.color = new Color(0f, 0f, 0f, 0f);
            back.raycastTarget = false;

            var element = row.GetComponent<LayoutElement>();
            element.preferredHeight = RowHeight;

            var text = MakeText(row.transform, "Label", 21f, Color.white, TextAlignmentOptions.Left, 0f);
            var rect = text.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(12f, 0f);
            rect.offsetMax = new Vector2(-10f, 0f);

            _rowBacks.Add(back);
            _rowTexts.Add(text);
            _rowLayouts.Add(element);
            _rowGlow.Add(0f);
        }

        private void MakeSeparator(Transform parent)
        {
            var go = new GameObject("Separator", typeof(Image), typeof(LayoutElement));
            go.transform.SetParent(parent, false);

            var image = go.GetComponent<Image>();
            image.color = new Color(1f, 0.42f, 0.52f, 0.28f);
            image.raycastTarget = false;
            go.GetComponent<LayoutElement>().preferredHeight = 2f;
        }

        private TMP_Text MakeText(Transform parent, string name, float size, Color color,
                                  TextAlignmentOptions align, float preferredHeight)
        {
            var go = new GameObject(name, typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);

            var text = go.GetComponent<TextMeshProUGUI>();
            text.fontSize = size;
            text.color = color;
            text.alignment = align;
            text.raycastTarget = false;
            text.richText = true;
            // По умолчанию без переносов: строка меню обязана оставаться одной строкой.
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            if (TMP_Settings.defaultFontAsset != null)
            {
                text.font = TMP_Settings.defaultFontAsset;
            }

            if (preferredHeight > 0f)
            {
                go.AddComponent<LayoutElement>().preferredHeight = preferredHeight;
            }
            return text;
        }

        /// <summary>
        /// Скруглённая подложка: обычный Image умеет только прямые углы, а готового спрайта в
        /// проекте нет. Рисуем текстуру кодом и режем её 9-slice.
        /// </summary>
        private static Sprite RoundedSprite(Color fill, Color border, int borderWidth)
        {
            const int size = 40;
            const float radius = 12f;

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Max(radius - x - 0.5f, (x + 0.5f) - (size - radius), 0f);
                    float dy = Mathf.Max(radius - y - 0.5f, (y + 0.5f) - (size - radius), 0f);
                    float d = Mathf.Sqrt(dx * dx + dy * dy) - radius;

                    Color c;
                    if (d > 0.5f)
                    {
                        c = new Color(fill.r, fill.g, fill.b, 0f);
                    }
                    else if (borderWidth > 0 && d > -borderWidth)
                    {
                        c = border;
                    }
                    else
                    {
                        c = fill;
                    }

                    if (d > -0.5f && d <= 0.5f)
                    {
                        c.a *= Mathf.Clamp01(0.5f - d);
                    }
                    tex.SetPixel(x, y, c);
                }
            }
            tex.Apply();

            var border9 = new Vector4(radius + 2f, radius + 2f, radius + 2f, radius + 2f);
            return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                                 SpriteMeshType.FullRect, border9);
        }

        // ------------------------------------------------------------------ наполнение

        private void Fill(string title, string subtitle, IList<Entry> entries)
        {
            _closing = false;
            _entries.Clear();
            for (int i = 0; i < entries.Count && i < MaxRows; i++)
            {
                _entries.Add(entries[i]);
            }

            _title.text = title;
            _subtitle.text = subtitle ?? string.Empty;
            _subtitle.gameObject.SetActive(!string.IsNullOrEmpty(subtitle));

            // Прошлый выбор сохраняем (повторить действие одной клавишей), но на выбираемой строке.
            _index = Mathf.Clamp(_index, 0, Mathf.Max(0, _entries.Count - 1));
            if (_entries.Count > 0 && !_entries[_index].Selectable)
            {
                _index = NextSelectable(_index, 1);
            }

            for (int i = 0; i < _rowTexts.Count; i++)
            {
                bool used = i < _entries.Count;
                _rowTexts[i].transform.parent.gameObject.SetActive(used);
                if (used)
                {
                    _rowLayouts[i].preferredHeight = _entries[i].IsSection ? SectionHeight : RowHeight;
                    _rowGlow[i] = i == _index ? 1f : 0f;
                }
            }

            bool russian = Scp939Interactions.IsRussian();
            // Клавиша — из настроек управления, а не зашитое «E».
            string key = new ReadableKeyCode(NewInput.GetKey(ActionName.Interact)).ShortVersion;
            _footer.text = russian
                ? "<color=#FF6B85>Колесо</color> выбор    <color=#FF6B85>" + key + "</color> выбрать    <color=#FF6B85>Backspace</color> назад"
                : "<color=#FF6B85>Wheel</color> select    <color=#FF6B85>" + key + "</color> choose    <color=#FF6B85>Backspace</color> back";

            Refresh();
            _root.SetActive(true);
        }

        private void Refresh()
        {
            for (int i = 0; i < _entries.Count && i < _rowTexts.Count; i++)
            {
                Entry entry = _entries[i];
                TMP_Text text = _rowTexts[i];

                if (entry.IsSection)
                {
                    text.fontSize = 14f;
                    text.color = new Color(1f, 0.42f, 0.52f, 0.85f);
                    text.fontStyle = FontStyles.Bold | FontStyles.UpperCase;
                    text.characterSpacing = 6f;
                    text.text = entry.Label;
                    continue;
                }

                bool on = i == _index;
                text.fontSize = 21f;
                text.fontStyle = FontStyles.Normal;
                text.characterSpacing = 0f;
                if (entry.IsDisabled)
                {
                    text.color = new Color(0.46f, 0.43f, 0.47f);
                    text.text = "    " + entry.Label;
                }
                else
                {
                    text.color = on ? Color.white : new Color(0.82f, 0.79f, 0.83f);
                    text.text = (on ? "<color=#FF6B85><b>></b></color>  " : "    ") + entry.Label;
                }
            }

            // Пояснение — отдельной строкой внизу: у выбранного пункта или у недоступного под ним.
            string hint = _entries.Count > 0 ? _entries[_index].Hint : null;
            _hint.text = string.IsNullOrEmpty(hint) ? " " : hint;
        }

        private void Update()
        {
            if (_root == null || !_root.activeSelf)
            {
                return;
            }

            // Появление: панель выезжает справа и проявляется; закрытие — в обратную сторону.
            float target = _closing ? 0f : 1f;
            _appear = Mathf.MoveTowards(_appear, target, Time.unscaledDeltaTime / AppearTime);
            float eased = 1f - (1f - _appear) * (1f - _appear);
            _group.alpha = eased;
            _panelRect.anchoredPosition = new Vector2(-64f + (1f - eased) * SlideDistance, -20f);
            if (_closing && _appear <= 0f)
            {
                _closing = false;
                _root.SetActive(false);
                return;
            }

            // Подсветка перетекает со строки на строку, а не мигает.
            for (int i = 0; i < _entries.Count && i < _rowBacks.Count; i++)
            {
                float want = i == _index && _entries[i].Selectable ? 1f : 0f;
                _rowGlow[i] = Mathf.MoveTowards(_rowGlow[i], want, Time.unscaledDeltaTime * 9f);
                _rowBacks[i].color = new Color(Accent.r, Accent.g, Accent.b, 0.24f * _rowGlow[i]);
            }
        }
    }
}
