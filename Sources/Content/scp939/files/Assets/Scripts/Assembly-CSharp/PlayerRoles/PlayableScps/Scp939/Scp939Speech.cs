using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PlayerRoles.PlayableScps.Scp939
{
    /// <summary>
    /// Реплика SCP-939 на экране.
    ///
    /// Отдельно от штатных подсказок и броадкастов: те принадлежат серверу и оформлены под системные
    /// сообщения, а здесь нужна именно «речь» — своим цветом, под меню действий и с плавным уходом.
    /// Канвас строится кодом, чтобы не заводить префаб и не трогать сцену (как и меню действий).
    ///
    /// По лору 939 говорит чужими голосами, поэтому реплики намеренно короткие и обрывочные.
    /// </summary>
    public class Scp939Speech : MonoBehaviour
    {
        private const float FadeTime = 0.5f;

        /// <summary>Высота реплики над нижним краем экрана: выше полосы здоровья, ниже ника игрока.</summary>
        private const float SpeechBottomOffset = 165f;

        private static Scp939Speech _instance;

        private TMP_Text _text;
        private CanvasGroup _group;
        private float _hideAt = -1f;
        private float _alpha;

        private struct Queued
        {
            public string Line;
            public float Seconds;
            public System.Action OnStart;
        }

        private readonly System.Collections.Generic.Queue<Queued> _queue =
            new System.Collections.Generic.Queue<Queued>();

        /// <summary>Показывает реплику на указанное время. Очередь сбрасывается: новое действие важнее.</summary>
        public static void Say(string line, float seconds = 4.5f)
        {
            if (string.IsNullOrEmpty(line))
            {
                return;
            }

            Ensure();
            _instance._queue.Clear();
            _instance.Show(line, seconds);
        }

        /// <summary>
        /// Реплика после текущей. Нужна для «второй фразы» — например, реакции на новый уровень
        /// отношений: сказанная сразу, она затирала бы реплику на сам подарок. <paramref name="onStart"/>
        /// вызывается в момент показа — туда кладётся озвучка, чтобы голос не обгонял текст.
        /// </summary>
        public static void Enqueue(string line, float seconds, System.Action onStart)
        {
            if (string.IsNullOrEmpty(line))
            {
                return;
            }

            Ensure();
            if (!_instance.Busy)
            {
                _instance.Show(line, seconds);
                if (onStart != null)
                {
                    onStart();
                }
                return;
            }
            _instance._queue.Enqueue(new Queued { Line = line, Seconds = seconds, OnStart = onStart });
        }

        private bool Busy => _hideAt > 0f && Time.time < _hideAt;

        private void Show(string line, float seconds)
        {
            _text.text = line;
            _hideAt = Time.time + seconds;
            enabled = true;
        }

        private static void Ensure()
        {
            if (_instance != null)
            {
                return;
            }

            var go = new GameObject("SCP939 Speech");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<Scp939Speech>();

            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(go.transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Ниже затемнения (900), но выше игрового HUD.
            canvas.sortingOrder = 600;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            var textGo = new GameObject("Line", typeof(TextMeshProUGUI));
            textGo.transform.SetParent(canvasGo.transform, false);

            var rect = textGo.GetComponent<RectTransform>();
            // Привязка к НИЖНЕМУ краю, а не к центру: по центру реплика налезала на ник игрока,
            // который рисуется при наведении. Отсчёт от низа заодно не зависит от высоты экрана.
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, SpeechBottomOffset);
            rect.sizeDelta = new Vector2(900f, 90f);

            _instance._text = textGo.GetComponent<TextMeshProUGUI>();
            _instance._text.fontSize = 32f;
            _instance._text.color = new Color(1f, 0.86f, 0.90f);
            _instance._text.fontStyle = FontStyles.Bold;
            _instance._text.alignment = TextAlignmentOptions.Center;
            _instance._text.raycastTarget = false;
            if (TMP_Settings.defaultFontAsset != null)
            {
                _instance._text.font = TMP_Settings.defaultFontAsset;
            }

            ApplyOutline(_instance._text);

            // Прозрачностью управляет CanvasGroup, а не цвет текста: цвет гасит только «лицо»
            // буквы, а обводка и тень живут в МАТЕРИАЛЕ и продолжали бы висеть на экране
            // самостоятельным контуром после того, как сама реплика уже погасла.
            _instance._group = canvasGo.AddComponent<CanvasGroup>();
            _instance._group.alpha = 0f;
            _instance._group.interactable = false;
            _instance._group.blocksRaycasts = false;
        }

        /// <summary>
        /// Чёрная обводка и мягкая тень под текстом.
        ///
        /// Без них реплика терялась: она светлая, а фон в игре бывает каким угодно — в белой
        /// комнате башни розовый текст на светлой стене почти не читался. Рич-текст TMP тега
        /// обводки не имеет, поэтому это делается через МАТЕРИАЛ, и обязательно через
        /// <see cref="TMP_Text.fontMaterial"/> — это копия на конкретный текст. Трогать
        /// sharedMaterial нельзя: обводка ушла бы на все тексты того же шрифта.
        /// </summary>
        private static void ApplyOutline(TMP_Text text)
        {
            Material mat = text.fontMaterial;

            if (mat.HasProperty(ShaderUtilities.ID_OutlineWidth))
            {
                mat.EnableKeyword(ShaderUtilities.Keyword_Outline);
                mat.SetColor(ShaderUtilities.ID_OutlineColor, new Color(0.05f, 0.02f, 0.03f, 1f));
                // 0.38, а не «аккуратные» 0.22: ширина здесь в долях SDF-градиента шрифта, и при
                // 0.22 контур на этом шрифте (LiberationSans SDF) физически есть, но на экране
                // неразличим — проверено кадром на белой стене башни.
                mat.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.38f);
                // Лёгкое утолщение самой буквы: контур съедает её изнутри, без этого текст худеет.
                mat.SetFloat(ShaderUtilities.ID_FaceDilate, 0.10f);
            }

            // Тень отдельным слоем: обводка отделяет буквы от фона, а тень — от светлых пятен
            // вроде окна или свечей, на которых чёрный контур сам по себе теряется.
            if (mat.HasProperty(ShaderUtilities.ID_UnderlayOffsetX))
            {
                mat.EnableKeyword(ShaderUtilities.Keyword_Underlay);
                mat.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(0f, 0f, 0f, 0.85f));
                mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0.4f);
                mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -0.4f);
                mat.SetFloat(ShaderUtilities.ID_UnderlayDilate, 0.15f);
                mat.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0.30f);
            }

            // Без этого обводке и тени просто негде нарисоваться: TMP считает запас вокруг буквы
            // ОДИН раз при генерации меша, и свойства материала, выставленные позже, обрезаются
            // по старому запасу — на экране остаётся ровно тот же текст, что и без обводки.
            text.UpdateMeshPadding();
        }

        private void Update()
        {
            if (_text == null || _group == null)
            {
                enabled = false;
                return;
            }

            if (!Busy && _queue.Count > 0)
            {
                Queued next = _queue.Dequeue();
                Show(next.Line, next.Seconds);
                if (next.OnStart != null)
                {
                    next.OnStart();
                }
            }

            // Скрытая режиссёром реплика продолжает «идти» по времени — просто не рисуется, чтобы
            // очередь и озвучка оставались в такте с тем, что видно у других.
            bool visible = Busy;
            _alpha = Mathf.MoveTowards(_alpha, visible ? 1f : 0f, Time.deltaTime / FadeTime);
            // Автосъёмка: реплики 939 — только в кадрах от первого лица (кинокамера со стороны выключена).
            bool sideShot = Scp939ModCompatibility.Filming && Scp939ModCompatibility.CameraActive;
            _group.alpha = Scp939Ui.SpeechVisible && !sideShot ? _alpha : 0f;

            if (!visible && _alpha <= 0f && _queue.Count == 0)
            {
                enabled = false;
            }
        }
    }
}
