using System;
using System.Diagnostics;
using PlayerRoles.PlayableScps.Subroutines;
using UnityEngine;
using UnityEngine.UI;

namespace PlayerRoles.PlayableScps.Scp3114
{
    // Current retail AbilityHud schema and behavior, scoped to the 3114 port.
    // Native evidence: metadata_audit/native_focus/AbilityHud.txt (RVA 6DAED0+).
    // The old engine supplies AbilityCooldown in place of the newer IAbilityCooldown.
    [Serializable]
    public class Scp3114NativeAbilityHud
    {
        [Serializable]
        private class GraphicColor
        {
            public Color StartingColor;
            public Graphic Graphic;

            private GraphicColor(Graphic graphic)
            {
                Graphic = graphic;
                StartingColor = graphic.color;
            }
        }

        [SerializeField] private GameObject _parent;
        [SerializeField] private CanvasGroup _fader;
        [SerializeField] private Image _durationCircle;
        [SerializeField] private Image _cooldownCircle;
        [SerializeField] private bool _inverseDuration;
        [SerializeField] private bool _inverseCooldown;
        [SerializeField] private bool _showDurationAtCooldown;
        [SerializeField] private Vector3 _startScale = Vector3.one;
        [SerializeField] private float _flashDuration;
        [SerializeField] private float _flashSpeed;
        [SerializeField] private GraphicColor[] _graphics;

        private readonly Stopwatch _flashStopwatch = new Stopwatch();
        private float _flashIdleTime;
        private AbilityCooldown _cooldown;
        private AbilityCooldown _duration;
        private RectTransform _rt;
        private bool _hasDuration;
        private bool _hasFader;
        private readonly Stopwatch _fullStopwatch = new Stopwatch();

        private const float MinFullTime = 0.3f;
        private const float FadeSpeed = 8.5f;

        public void Setup(AbilityCooldown cd, AbilityCooldown duration)
        {
            _cooldown = cd;
            _duration = duration;
            _rt = _cooldownCircle.rectTransform;
            _hasDuration = _duration != null;
            _hasFader = _fader != null;
            bool visible = UpdateVisibility();
            if (_hasFader)
                _fader.alpha = visible ? 1f : 0f;
            else
                _parent.SetActive(visible);
        }

        public void Update(bool forceHidden = false)
        {
            bool visible = !forceHidden && UpdateVisibility();
            if (_hasFader)
                _fader.alpha = Mathf.Clamp01(_fader.alpha + Time.deltaTime * (visible ? FadeSpeed : -FadeSpeed));
            else
                _parent.SetActive(visible);
        }

        private bool UpdateVisibility()
        {
            bool visible = UpdateCooldown();
            if (_hasDuration && UpdateDuration())
                visible = true;
            return visible;
        }

        private bool UpdateCooldown()
        {
            float fill = FillCircle(_cooldown, _cooldownCircle, _inverseCooldown);
            _rt.localScale = Vector3.Lerp(_startScale, Vector3.one, fill);
            return !_cooldown.IsReady;
        }

        private bool UpdateDuration()
        {
            if (_duration.IsReady)
            {
                _durationCircle.enabled = false;
                return _fullStopwatch.IsRunning && _fullStopwatch.Elapsed.TotalSeconds < MinFullTime;
            }

            _durationCircle.enabled = _showDurationAtCooldown || _cooldown.IsReady;
            FillCircle(_duration, _durationCircle, !_inverseDuration);
            _fullStopwatch.Restart();
            return true;
        }

        private float FillCircle(AbilityCooldown cd, Image circle, bool inverse)
        {
            float fill = cd.Readiness;
            if (inverse)
                fill = 1f - fill;
            circle.fillAmount = fill;
            return fill;
        }

        public void LateUpdate()
        {
            foreach (GraphicColor graphic in _graphics)
                UpdateFlash(graphic.Graphic, _flashStopwatch, graphic.StartingColor, ref _flashIdleTime);
        }

        public void FlashAbility()
        {
            _flashStopwatch.Restart();
        }

        private void UpdateFlash(Graphic targetGraphic, Stopwatch sw, Color normalColor, ref float idleTime)
        {
            Color color;
            if (sw.IsRunning && sw.Elapsed.TotalSeconds < _flashDuration)
            {
                float phase = (Time.timeSinceLevelLoad - idleTime) * _flashSpeed * Mathf.PI;
                color = Color.Lerp(normalColor, Color.red, Mathf.Abs(Mathf.Sin(phase)));
            }
            else
            {
                color = Color.Lerp(targetGraphic.color, normalColor, Time.deltaTime * _flashSpeed);
                idleTime = Time.timeSinceLevelLoad;
            }

            targetGraphic.color = new Color(color.r, color.g, color.b, targetGraphic.color.a);
        }
    }
}
