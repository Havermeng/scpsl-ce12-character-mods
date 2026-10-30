using System;
using UnityEngine;
using UnityEngine.UI;

namespace PlayerRoles.PlayableScps.Scp939
{
    /// <summary>
    /// Затемнение экрана на время переноса к столу.
    ///
    /// Своё, а не штатное: игровые фейды в этом проекте завязаны на смену роли и загрузку сцены, а
    /// здесь надо скрыть именно телепорт, оставив игрока в той же роли. Канвас строится кодом, чтобы
    /// не заводить префаб и не трогать сцену.
    /// </summary>
    public class Scp939ScreenFade : MonoBehaviour
    {
        private static Scp939ScreenFade _instance;

        private Image _image;
        private float _target;
        private float _speed = 2f;
        private Action _onFullyDark;
        private bool _pendingCallback;
        private Color _tint = Color.black;
        private float _alpha;

        /// <summary>Затемнить, выполнить действие в самой тёмной точке, затем осветлить.</summary>
        public static void Blink(float darkSeconds, Action atDarkest)
        {
            Ensure();
            _instance._tint = Color.black;
            _instance._onFullyDark = atDarkest;
            _instance._pendingCallback = true;
            _instance._speed = 1f / Mathf.Max(0.05f, darkSeconds);
            _instance._target = 1f;
        }

        /// <summary>
        /// Короткая цветная вспышка — красная, когда 939 бросается в ярости. Тот же слой, что и
        /// затемнение: цвет меняется, механика «туда и обратно» общая.
        /// </summary>
        public static void Flash(Color color, float seconds)
        {
            Ensure();
            _instance._tint = new Color(color.r, color.g, color.b, 0.55f);
            _instance._onFullyDark = null;
            _instance._pendingCallback = true;
            _instance._speed = 1f / Mathf.Max(0.05f, seconds * 0.35f);
            _instance._target = 1f;
        }

        private static void Ensure()
        {
            if (_instance != null)
            {
                return;
            }

            var go = new GameObject("SCP939 Screen Fade");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<Scp939ScreenFade>();

            var canvasGo = new GameObject("Canvas", typeof(Canvas));
            canvasGo.transform.SetParent(go.transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Выше меню действий (500), чтобы затемнение перекрывало вообще всё.
            canvas.sortingOrder = 900;

            var imageGo = new GameObject("Black", typeof(Image));
            imageGo.transform.SetParent(canvasGo.transform, false);
            var rect = imageGo.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            _instance._image = imageGo.GetComponent<Image>();
            _instance._image.color = new Color(0f, 0f, 0f, 0f);
            _instance._image.raycastTarget = false;
        }

        private void Update()
        {
            if (_image == null)
            {
                return;
            }

            // Своё значение, а не альфа картинки: у вспышки альфа домножена на силу оттенка,
            // и читать её обратно значило бы каждый кадр «откатывать» прогресс.
            float a = Mathf.MoveTowards(_alpha, _target, Time.deltaTime * _speed);
            _alpha = a;

            if (_pendingCallback && a >= 0.999f)
            {
                // Телепорт делаем ровно в тот кадр, когда экран полностью чёрный.
                _pendingCallback = false;
                _target = 0f;
                Action cb = _onFullyDark;
                _onFullyDark = null;
                if (cb != null)
                {
                    cb();
                }
            }

            _image.color = new Color(_tint.r, _tint.g, _tint.b, a * _tint.a);
        }
    }
}
