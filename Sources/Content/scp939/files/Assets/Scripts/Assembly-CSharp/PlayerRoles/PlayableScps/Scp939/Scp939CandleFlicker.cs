using UnityEngine;

namespace PlayerRoles.PlayableScps.Scp939
{
    /// <summary>
    /// Дрожание пламени свечи. Ровный свет от свечи выглядит как лампочка, а весь смысл свечей —
    /// в живом неровном пятне на лицах.
    ///
    /// Считается от <see cref="Time.time"/> без состояния, поэтому все свечи в комнате мерцают
    /// вразнобой (сдвиг берётся из позиции) и не требуют синхронизации по сети.
    /// </summary>
    [RequireComponent(typeof(Light))]
    public class Scp939CandleFlicker : MonoBehaviour
    {
        private const float MinScale = 0.78f;
        private const float MaxScale = 1.12f;
        private const float Speed = 6.5f;

        private Light _light;
        private float _baseIntensity;
        private float _offset;

        private void Awake()
        {
            _light = GetComponent<Light>();
            _baseIntensity = _light.intensity;
            _offset = Mathf.Abs(transform.position.x * 13.7f + transform.position.z * 7.3f) % 100f;
        }

        private void Update()
        {
            // Две несоразмерные синусоиды дают неповторяющееся дрожание без генератора случайных чисел.
            float t = Time.time * Speed + _offset;
            float n = (Mathf.Sin(t) * 0.6f + Mathf.Sin(t * 2.37f) * 0.4f) * 0.5f + 0.5f;
            _light.intensity = _baseIntensity * Mathf.Lerp(MinScale, MaxScale, n);
        }
    }
}
