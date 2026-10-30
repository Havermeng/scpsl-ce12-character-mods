using AudioPooling;
using UnityEngine;

namespace PlayerRoles.PlayableScps.Scp939
{
    /// <summary>
    /// Мелкие звуки свидания, синтезированные кодом (как звуки строительства): подходящих клипов в
    /// рипнутом проекте нет, а тащить файлы ради пары эффектов незачем.
    /// </summary>
    public static class Scp939Sfx
    {
        private const int SampleRate = 44100;

        private static AudioClip _slurp;

        /// <summary>Втягивание спагетти: шипящий шум с тоном, ползущим вверх, и мокрый щелчок в конце.</summary>
        public static void Slurp(Vector3 position)
        {
            if (_slurp == null)
            {
                _slurp = MakeSlurp();
            }
            if (AudioSourcePoolManager.PlaySound(_slurp, position, 14f, 0.8f,
                    FalloffType.Exponential, AudioMixerChannelType.DefaultSfx) == null)
            {
                AudioSource.PlayClipAtPoint(_slurp, position, 0.8f);
            }
        }

        private static AudioClip _tick;
        private static AudioClip _confirm;
        private static AudioClip _back;

        /// <summary>Переход по пунктам меню — короткий мягкий «тик».</summary>
        public static void UiTick()
        {
            Play2D(_tick ?? (_tick = MakeBlip(1320f, 1320f, 0.035f, 0.18f)), 0.35f);
        }

        /// <summary>Выбор пункта — две ноты вверх, «динь-динь».</summary>
        public static void UiConfirm()
        {
            Play2D(_confirm ?? (_confirm = MakeTwoNotes(880f, 1320f)), 0.4f);
        }

        /// <summary>Назад — нота вниз.</summary>
        public static void UiBack()
        {
            Play2D(_back ?? (_back = MakeBlip(900f, 600f, 0.09f, 0.25f)), 0.35f);
        }

        private static void Play2D(AudioClip clip, float volume)
        {
            Camera cam = Camera.main;
            Vector3 at = cam != null ? cam.transform.position : Vector3.zero;
            if (AudioSourcePoolManager.PlaySound(clip, at, 10f, volume,
                    FalloffType.Linear, AudioMixerChannelType.Interface, 0f) == null)
            {
                AudioSource.PlayClipAtPoint(clip, at, volume);
            }
        }

        /// <summary>Синус со скользящей частотой и мягкой огибающей — «пузырёк» интерфейса.</summary>
        private static AudioClip MakeBlip(float fromHz, float toHz, float seconds, float gain)
        {
            int length = (int)(SampleRate * seconds);
            var data = new float[length];
            float phase = 0f;
            for (int i = 0; i < length; i++)
            {
                float t = i / (float)length;
                float freq = Mathf.Lerp(fromHz, toHz, t);
                phase += 2f * Mathf.PI * freq / SampleRate;
                float env = Mathf.Sin(Mathf.PI * t);
                data[i] = Mathf.Sin(phase) * env * gain;
            }
            AudioClip clip = AudioClip.Create("939 ui", length, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip MakeTwoNotes(float first, float second)
        {
            int half = (int)(SampleRate * 0.07f);
            var data = new float[half * 2];
            for (int n = 0; n < 2; n++)
            {
                float freq = n == 0 ? first : second;
                for (int i = 0; i < half; i++)
                {
                    float t = i / (float)half;
                    float env = Mathf.Clamp01(t * 12f) * (1f - t);
                    data[n * half + i] = (Mathf.Sin(2f * Mathf.PI * freq * i / SampleRate) +
                                          0.3f * Mathf.Sin(4f * Mathf.PI * freq * i / SampleRate)) * env * 0.22f;
                }
            }
            AudioClip clip = AudioClip.Create("939 ui confirm", data.Length, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip MakeSlurp()
        {
            int length = (int)(SampleRate * 0.95f);
            var data = new float[length];
            var random = new System.Random(207);
            float band = 0f;
            float low = 0f;
            float phase = 0f;
            for (int i = 0; i < length; i++)
            {
                float t = i / (float)SampleRate;
                float noise = (float)(random.NextDouble() * 2.0 - 1.0);

                // Полоса шума, «сужающаяся» кверху: так звучит воздух через губы.
                low += (noise - low) * 0.08f;
                band = noise - low;

                float freq = Mathf.Lerp(260f, 900f, t / 0.8f);
                phase += 2f * Mathf.PI * freq / SampleRate;
                float tone = Mathf.Sin(phase) * 0.25f;

                float env = Mathf.Clamp01(t * 10f) * Mathf.Clamp01((0.8f - t) * 6f);
                float pop = t > 0.8f && t < 0.84f ? Mathf.Sin((t - 0.8f) * 2f * Mathf.PI * 180f) * (0.84f - t) * 20f : 0f;
                data[i] = (band * 0.55f + tone) * env * 0.6f + pop * 0.5f;
            }
            AudioClip clip = AudioClip.Create("939 slurp", length, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
