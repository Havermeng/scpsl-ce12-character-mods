using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PlayerRoles.PlayableScps.Scp939
{
    /// <summary>
    /// Симпатия SCP-939 к игрокам — шкала «отношений» из симулятора свиданий.
    ///
    /// Значение одно на 939 (а не на пару): в съёмке почти всегда один ухажёр, а шкала на пару
    /// усложнила бы и команды режиссёра, и интерфейс. Живёт на сервере; клиенты узнают значение из
    /// поля <see cref="Scp939InteractionBroadcast.Affection"/> и только рисуют его.
    ///
    /// Уровни по 20 очков. Переход через порог — повод для отдельной реплики, это и есть «прогресс»
    /// свидания, который видно в кадре.
    /// </summary>
    public static class Scp939Affection
    {
        public const float Max = 100f;

        /// <summary>С чего начинается знакомство: не ноль, чтобы первый же плохой подарок не бесил её.</summary>
        public const float Initial = 12f;

        private static readonly Dictionary<ReferenceHub, float> Values = new Dictionary<ReferenceHub, float>();

        public static void Clear()
        {
            Values.Clear();
        }

        public static void Forget(ReferenceHub scp)
        {
            if (scp != null)
            {
                Values.Remove(scp);
            }
        }

        public static float Get(ReferenceHub scp)
        {
            float value;
            return scp != null && Values.TryGetValue(scp, out value) ? value : Initial;
        }

        public static void Set(ReferenceHub scp, float value)
        {
            if (scp != null)
            {
                Values[scp] = Mathf.Clamp(value, 0f, Max);
            }
        }

        /// <summary>Меняет симпатию и возвращает, на сколько уровней она сдвинулась (+1, -1, 0).</summary>
        public static int Change(ReferenceHub scp, float delta)
        {
            if (!Scp939ModSettings.Enabled("dialogue")) return 0;
            float before = Get(scp);
            float after = Mathf.Clamp(before + delta, 0f, Max);
            Set(scp, after);
            return LevelOf(after) - LevelOf(before);
        }

        public static int LevelOf(float value)
        {
            return Mathf.Clamp(Mathf.FloorToInt(value / 20f), 0, 5);
        }

        public static string Title(int level, bool russian)
        {
            switch (level)
            {
                case 0:
                    return russian ? "Незнакомец" : "Stranger";
                case 1:
                    return russian ? "Знакомый" : "Acquaintance";
                case 2:
                    return russian ? "Друг" : "Friend";
                case 3:
                    return russian ? "Встречаетесь" : "Dating";
                case 4:
                    return russian ? "Всё серьёзно" : "It's serious";
                default:
                    return russian ? "Ты теперь её" : "She owns you now";
            }
        }
    }

    /// <summary>
    /// Что из интерфейса мода показывать. Меняется командой режиссёра <c>939 ui</c> и приходит всем
    /// клиентам сообщением, поэтому хост-съёмщик выключает всё одной строкой.
    ///
    /// <see cref="FollowHud"/>: элементы прячутся вместе со штатным HUD (клавиша скрытия
    /// интерфейса) — раньше подсказка, меню и реплики оставались в кадре, когда HUD уже скрыт.
    /// </summary>
    public static class Scp939Ui
    {
        public static bool RelationshipRevealed { get; set; } = true;

        public const byte FlagPrompt = 1;
        public const byte FlagSpeech = 2;
        public const byte FlagHearts = 4;
        public const byte FlagFollowHud = 8;
        public const byte FlagVoice = 16;

        // Озвучка реплик по умолчанию ВЫКЛЮЧЕНА (решение пользователя 2026-09-28): ролик идёт под его
        // закадровый голос, а 939 «говорит» субтитрами. Включить обратно: 939 ui voice on.
        public static byte Flags = (byte)(FlagPrompt | FlagSpeech | FlagHearts | FlagFollowHud | (Scp939ModSettings.Enabled("voice") ? FlagVoice : 0));

        private static bool Has(byte flag)
        {
            return (Flags & flag) != 0;
        }

        private static bool HudAllows => !Has(FlagFollowHud) || HideHUDController.IsHUDVisible;

        // В кадрах со стороны (автосъёмка) подсказка под прицелом бессмысленна — прицела там нет.
        public static bool PromptVisible => Has(FlagPrompt) && HudAllows && !Scp939ModCompatibility.CameraActive;

        // Субтитры 939 остаются и в кинокадрах (при съёмке HUD прячется вместе с кинокамерой, а реплики
        // нужны всегда); подсказка, шкала симпатии и меню — только от первого лица.
        public static bool SpeechVisible => Scp939ModSettings.Enabled("dialogue") && Has(FlagSpeech) && (HudAllows || Scp939ModCompatibility.Filming);

        public static bool HeartsVisible => Scp939ModSettings.Enabled("dialogue") && RelationshipRevealed && Has(FlagHearts) && HudAllows && !Scp939ModCompatibility.CameraActive;

        public static bool VoiceEnabled => Scp939ModSettings.Enabled("voice") && Has(FlagVoice);
    }

    /// <summary>
    /// Шкала симпатии на экране: сверху по центру, имя 939, уровень отношений и полоса.
    /// Появляется при изменении и сама гаснет — постоянно висящая плашка мешала бы кадру.
    /// Канвас кодом, как у меню и реплик.
    /// </summary>
    public class Scp939AffectionHud : MonoBehaviour
    {
        private const float ShowSeconds = 5f;
        private const float FadeTime = 0.4f;

        private static Scp939AffectionHud _instance;

        private CanvasGroup _group;
        private TMP_Text _title;
        private TMP_Text _delta;
        private Image _fill;
        private float _hideAt = -1f;
        private float _shown;
        private float _target;
        private float _alpha;
        private float _deltaUntil;

        public static void Show(string name, float value, float delta, bool russian)
        {
            if (!Scp939Ui.RelationshipRevealed) { Hide(); return; }
            Ensure();
            int level = Scp939Affection.LevelOf(value);
            _instance._title.text = "<color=#FF6B85>" + name + "</color>   " +
                                    Scp939Affection.Title(level, russian) + "   <size=18><color=#B9AEB3>" +
                                    Mathf.RoundToInt(value) + " / 100</color></size>";
            // A newly revealed film HUD starts at the real pre-action value,
            // rather than manufacturing a 0-to-target gain on first creation.
            if (Scp939ModCompatibility.Filming && _instance._alpha <= .001f)
            {
                _instance._shown = Mathf.Clamp01((value - delta) / Scp939Affection.Max);
                _instance._fill.rectTransform.anchorMax = new Vector2(_instance._shown, 1f);
            }
            _instance._target = value / Scp939Affection.Max;
            if (Mathf.Abs(delta) >= 0.5f)
            {
                _instance._delta.text = delta > 0f
                    ? "<color=#8CFF9E>+" + Mathf.RoundToInt(delta) + "</color>"
                    : "<color=#FF5A5A>" + Mathf.RoundToInt(delta) + "</color>";
                _instance._deltaUntil = Time.time + 2.2f;
            }
            _instance._hideAt = Time.time + ShowSeconds;
            _instance.enabled = true;
        }

        public static void Hide()
        {
            if (_instance != null)
            {
                _instance._hideAt = -1f;
            }
        }

        private static void Ensure()
        {
            if (_instance != null)
            {
                return;
            }

            var go = new GameObject("SCP939 Affection HUD");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<Scp939AffectionHud>();

            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(go.transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 550;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            _instance._group = canvasGo.AddComponent<CanvasGroup>();
            _instance._group.alpha = 0f;
            _instance._group.blocksRaycasts = false;
            _instance._group.interactable = false;

            var panel = new GameObject("Panel", typeof(Image));
            panel.transform.SetParent(canvasGo.transform, false);
            var rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -38f);
            rect.sizeDelta = new Vector2(560f, 74f);
            var bg = panel.GetComponent<Image>();
            bg.color = new Color(0.05f, 0.04f, 0.06f, 0.78f);
            bg.raycastTarget = false;

            _instance._title = MakeText(panel.transform, 22f, TextAlignmentOptions.Center);
            var titleRect = _instance._title.rectTransform;
            titleRect.anchorMin = new Vector2(0f, 0.45f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.offsetMin = new Vector2(12f, 0f);
            titleRect.offsetMax = new Vector2(-12f, -4f);

            var track = new GameObject("Track", typeof(Image));
            track.transform.SetParent(panel.transform, false);
            var trackRect = track.GetComponent<RectTransform>();
            trackRect.anchorMin = new Vector2(0f, 0f);
            trackRect.anchorMax = new Vector2(1f, 0.45f);
            trackRect.offsetMin = new Vector2(22f, 12f);
            trackRect.offsetMax = new Vector2(-22f, -4f);
            var trackImage = track.GetComponent<Image>();
            trackImage.color = new Color(1f, 0.42f, 0.52f, 0.18f);
            trackImage.raycastTarget = false;

            var fill = new GameObject("Fill", typeof(Image));
            fill.transform.SetParent(track.transform, false);
            var fillRect = fill.GetComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = new Vector2(0f, 1f);
            fillRect.pivot = new Vector2(0f, 0.5f);
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            _instance._fill = fill.GetComponent<Image>();
            _instance._fill.color = new Color(1f, 0.42f, 0.52f, 0.95f);
            _instance._fill.raycastTarget = false;

            // Деления уровней: пять отрезков по 20 — так «переход на новый уровень» видно глазом.
            for (int i = 1; i < 5; i++)
            {
                var tick = new GameObject("Tick " + i, typeof(Image));
                tick.transform.SetParent(track.transform, false);
                var tickRect = tick.GetComponent<RectTransform>();
                tickRect.anchorMin = new Vector2(i * 0.2f, 0f);
                tickRect.anchorMax = new Vector2(i * 0.2f, 1f);
                tickRect.sizeDelta = new Vector2(2f, 0f);
                var tickImage = tick.GetComponent<Image>();
                tickImage.color = new Color(0.05f, 0.04f, 0.06f, 0.9f);
                tickImage.raycastTarget = false;
            }

            _instance._delta = MakeText(panel.transform, 26f, TextAlignmentOptions.Left);
            var deltaRect = _instance._delta.rectTransform;
            deltaRect.anchorMin = deltaRect.anchorMax = new Vector2(1f, 0.5f);
            deltaRect.pivot = new Vector2(0f, 0.5f);
            deltaRect.anchoredPosition = new Vector2(14f, 0f);
            deltaRect.sizeDelta = new Vector2(120f, 40f);
        }

        private static TMP_Text MakeText(Transform parent, float size, TextAlignmentOptions align)
        {
            var go = new GameObject("Text", typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<TextMeshProUGUI>();
            text.fontSize = size;
            text.color = Color.white;
            text.alignment = align;
            text.richText = true;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            if (TMP_Settings.defaultFontAsset != null)
            {
                text.font = TMP_Settings.defaultFontAsset;
            }
            return text;
        }

        private void Update()
        {
            if (_group == null)
            {
                enabled = false;
                return;
            }

            bool visible = _hideAt > 0f && Time.time < _hideAt && Scp939Ui.HeartsVisible;
            _alpha = Mathf.MoveTowards(_alpha, visible ? 1f : 0f, Time.deltaTime / FadeTime);
            _group.alpha = _alpha;

            // Полоса доезжает плавно — рывок в один кадр глазом не читается как «выросло».
            _shown = Mathf.MoveTowards(_shown, _target, Time.deltaTime * 0.6f);
            var rect = _fill.rectTransform;
            rect.anchorMax = new Vector2(Mathf.Clamp01(_shown), 1f);

            _delta.gameObject.SetActive(Time.time < _deltaUntil);

            if (!visible && _alpha <= 0f && Mathf.Approximately(_shown, _target))
            {
                enabled = false;
            }
        }
    }
}
