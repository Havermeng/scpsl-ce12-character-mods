using System.Collections.Generic;
using System.IO;
using AudioPooling;
using PlayerRoles.FirstPersonControl;
using UnityEngine;

namespace PlayerRoles.PlayableScps.Scp939
{
    /// <summary>
    /// Голос SCP-939.
    ///
    /// Голос — милый женский (Piper, диктор Ирина), в игре тон чуть поднимается. В ярости тот же
    /// голос опускается и рвётся («мимикрия»: ниже, с призрачным вторым слоем) — контраст и есть шутка.
    ///
    /// Где лежат файлы (WAV, 16 бит или float, моно или стерео):
    /// <list type="bullet">
    /// <item>StreamingAssets/Scp939Voice/custom/&lt;ключ&gt;.wav — свои записи. Играются КАК ЕСТЬ, без
    /// обработки, и важнее сгенерированных: так можно, например, записать реплику своим голосом.</item>
    /// <item>StreamingAssets/Scp939Voice/ru|en/&lt;ключ&gt;.wav — сгенерированные
    /// (Tools/Scp939Voice/generate_voice.py), к ним применяется эффект мимикрии.</item>
    /// </list>
    /// Нет файла — играется короткое «щёлканье» (синтез кодом), чтобы реплика не была немой.
    ///
    /// StreamingAssets, а не Resources: файлы не импортируются Unity как ассеты (на сотне клипов
    /// импорт заметно тормозит редактор) и их можно менять без пересборки.
    /// </summary>
    public static class Scp939Voice
    {
        private const float HearDistance = 26f;

        private static readonly Dictionary<string, AudioClip> Cache = new Dictionary<string, AudioClip>();
        private static readonly HashSet<string> Missing = new HashSet<string>();

        private static AudioSource _current;
        private static AudioClip _chitter;

        private static string Root => Path.Combine(Application.streamingAssetsPath, "Scp939Voice");

        /// <summary>
        /// Реплика целиком: текст на экран и голос из её головы.
        /// <paramref name="queued"/> — сказать после текущей реплики, а не вместо неё.
        /// </summary>
        public static void Speak(ReferenceHub scp, string key, float seconds = 0f, bool queued = false)
        {
            Scp939Line line;
            if (!Scp939Lines.TryGet(key, out line))
            {
                return;
            }

            bool russian = Scp939Interactions.IsRussian();
            string text = line.Text(russian);

            // Сколько держать текст: по длине фразы, но не меньше, чем звучит озвучка, — иначе
            // субтитр гаснет посреди предложения.
            AudioClip clip = Scp939Ui.VoiceEnabled ? Load(key, russian) : null;
            float auto = Mathf.Clamp(2.2f + text.Length * 0.055f, 3f, 8f);
            if (clip != null)
            {
                auto = Mathf.Max(auto, clip.length + 0.8f);
            }
            float shown = Mathf.Max(seconds, auto);

            if (queued)
            {
                Scp939Speech.Enqueue(text, shown, delegate { Play(scp, key, russian); });
            }
            else
            {
                Scp939Speech.Say(text, shown);
                Play(scp, key, russian);
            }
        }

        /// <summary>Произносит реплику по ключу, без текста на экране.</summary>
        public static void Play(ReferenceHub scp, string key, bool russian)
        {
            if (!Scp939Ui.VoiceEnabled || scp == null)
            {
                return;
            }
            AudioClip clip = Load(key, russian) ?? Chitter();
            PlayClip(scp, clip, key != null && key.StartsWith("rage") ? 1f : 0.9f);
        }

        /// <summary>Играет готовый клип из головы 939. Прошлая реплика обрывается — двое её не говорят.</summary>
        public static void PlayClip(ReferenceHub scp, AudioClip clip, float volume)
        {
            if (clip == null || scp == null)
            {
                return;
            }

            if (_current != null && _current.isPlaying)
            {
                _current.Stop();
            }

            Transform at = HeadOf(scp);
            _current = AudioSourcePoolManager.PlaySound(clip, at, HearDistance, volume,
                FalloffType.Exponential, AudioMixerChannelType.DefaultSfx);
            if (_current == null)
            {
                AudioSource.PlayClipAtPoint(clip, at.position, volume);
            }
        }

        /// <summary>Сбрасывает кэш — после замены файлов озвучки не нужен перезапуск.</summary>
        public static void Reload()
        {
            Cache.Clear();
            Missing.Clear();
        }

        private static Transform HeadOf(ReferenceHub scp)
        {
            var fpc = scp.roleManager.CurrentRole as IFpcRole;
            if (fpc != null && fpc.FpcModule.CharacterModelInstance != null)
            {
                foreach (Transform t in fpc.FpcModule.CharacterModelInstance.GetComponentsInChildren<Transform>())
                {
                    if (t.name == "DEF-Head")
                    {
                        return t;
                    }
                }
            }
            return scp.PlayerCameraReference != null ? scp.PlayerCameraReference : scp.transform;
        }

        private static AudioClip Load(string key, bool russian)
        {
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }

            string lang = russian ? "ru" : "en";
            string cacheKey = lang + "/" + key;
            AudioClip clip;
            if (Cache.TryGetValue(cacheKey, out clip))
            {
                return clip;
            }
            if (Missing.Contains(cacheKey))
            {
                return null;
            }

            clip = LoadWav(Path.Combine(Root, "custom", key + ".wav"), key, false);
            if (clip == null)
            {
                clip = LoadWav(Path.Combine(Root, lang, key + ".wav"), key, true);
            }
            if (clip == null && !russian)
            {
                // Английской озвучки может не быть вовсе — тогда лучше русский голос, чем щёлканье.
                clip = LoadWav(Path.Combine(Root, "ru", key + ".wav"), key, true);
            }

            if (clip == null)
            {
                Missing.Add(cacheKey);
                return null;
            }
            Cache[cacheKey] = clip;
            return clip;
        }

        private static AudioClip LoadWav(string path, string key, bool mimicEffect)
        {
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                int rate;
                float[] samples = Scp939Wav.ReadMono(File.ReadAllBytes(path), out rate);
                if (samples == null || samples.Length == 0)
                {
                    return null;
                }
                if (mimicEffect)
                {
                    // Обычные реплики — милый голос чуть выше, ярость — тот же голос, но низкий и
                    // рваный: контраст «милашка → чудовище» и есть шутка.
                    samples = key.StartsWith("rage")
                        ? Scp939Wav.Mimicry(samples, 0.80f, 2.6f)
                        : Scp939Wav.Cute(samples);
                }
                AudioClip clip = AudioClip.Create("939 " + key, samples.Length, 1, rate, false);
                clip.SetData(samples, 0);
                return clip;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[939] не удалось прочитать озвучку " + path + ": " + e.Message);
                return null;
            }
        }

        /// <summary>
        /// Запасной звук, когда озвучки нет: серия влажных щелчков с низким рыком. Лучше, чем
        /// тишина, и по-своему в образе — 939 щёлкает, пока подбирает, чьим голосом ответить.
        /// </summary>
        private static AudioClip Chitter()
        {
            if (_chitter != null)
            {
                return _chitter;
            }

            const int rate = 44100;
            int length = (int)(rate * 0.9f);
            var data = new float[length];
            var random = new System.Random(939);
            float lp = 0f;
            for (int i = 0; i < length; i++)
            {
                float t = i / (float)rate;
                float env = Mathf.Clamp01(t * 12f) * Mathf.Clamp01((0.9f - t) * 4f);
                float growl = Mathf.Sin(2f * Mathf.PI * (62f + 8f * Mathf.Sin(t * 17f)) * t) * 0.35f;
                float noise = (float)(random.NextDouble() * 2.0 - 1.0);
                lp += (noise - lp) * 0.18f;
                // Щелчки: короткие импульсы шума с неровным шагом.
                float phase = (t * 11f + 0.3f * Mathf.Sin(t * 5f)) % 1f;
                float click = phase < 0.06f ? lp * 2.2f * (1f - phase / 0.06f) : 0f;
                data[i] = (growl * 0.5f + click) * env * 0.7f;
            }
            _chitter = AudioClip.Create("939 chitter", length, 1, rate, false);
            _chitter.SetData(data, 0);
            return _chitter;
        }
    }

    /// <summary>Чтение WAV и обработка голоса. Отдельно, потому что этим же пользуется мимикрия микрофона.</summary>
    public static class Scp939Wav
    {
        /// <summary>PCM 8/16/24/32 бит и float 32, любое число каналов → моно float.</summary>
        public static float[] ReadMono(byte[] bytes, out int sampleRate)
        {
            sampleRate = 22050;
            if (bytes.Length < 44 || bytes[0] != 'R' || bytes[1] != 'I' || bytes[2] != 'F' || bytes[3] != 'F')
            {
                return null;
            }

            int format = 1, channels = 1, bits = 16;
            int pos = 12;
            while (pos + 8 <= bytes.Length)
            {
                string id = System.Text.Encoding.ASCII.GetString(bytes, pos, 4);
                int size = System.BitConverter.ToInt32(bytes, pos + 4);
                int body = pos + 8;
                if (id == "fmt ")
                {
                    format = System.BitConverter.ToInt16(bytes, body);
                    channels = System.BitConverter.ToInt16(bytes, body + 2);
                    sampleRate = System.BitConverter.ToInt32(bytes, body + 4);
                    bits = System.BitConverter.ToInt16(bytes, body + 14);
                    if (format == -2 && size >= 26)
                    {
                        // WAVE_FORMAT_EXTENSIBLE: настоящий формат — в первых байтах подтипа.
                        format = System.BitConverter.ToInt16(bytes, body + 24);
                    }
                }
                else if (id == "data")
                {
                    size = Mathf.Min(size, bytes.Length - body);
                    int step = bits / 8;
                    int frames = size / (step * channels);
                    var result = new float[frames];
                    for (int f = 0; f < frames; f++)
                    {
                        float sum = 0f;
                        for (int c = 0; c < channels; c++)
                        {
                            int at = body + (f * channels + c) * step;
                            sum += Sample(bytes, at, bits, format);
                        }
                        result[f] = sum / channels;
                    }
                    return result;
                }
                pos = body + size + (size & 1);
            }
            return null;
        }

        private static float Sample(byte[] b, int at, int bits, int format)
        {
            switch (bits)
            {
                case 8:
                    return (b[at] - 128) / 128f;
                case 16:
                    return System.BitConverter.ToInt16(b, at) / 32768f;
                case 24:
                    return ((b[at] | (b[at + 1] << 8) | ((sbyte)b[at + 2] << 16))) / 8388608f;
                default:
                    return format == 3 ? System.BitConverter.ToSingle(b, at) : System.BitConverter.ToInt32(b, at) / 2147483648f;
            }
        }

        /// <summary>
        /// «Мимикрия»: голос ниже на <paramref name="pitch"/>, поверх — второй, ещё чуть ниже и с
        /// задержкой, и мягкое насыщение. Одинокий синтезированный диктор звучит как навигатор;
        /// два почти совпадающих голоса дают ощущение, что кто-то говорит чужим ртом.
        /// </summary>
        public static float[] Mimicry(float[] source, float pitch, float drive)
        {
            float[] main = Resample(source, pitch);
            float[] ghost = Resample(source, pitch * 0.94f);
            int delay = 700;
            int length = Mathf.Max(main.Length, ghost.Length + delay);
            var result = new float[length];
            float norm = (float)System.Math.Tanh(drive);
            float peak = 0f;
            for (int i = 0; i < length; i++)
            {
                float a = i < main.Length ? main[i] : 0f;
                int g = i - delay;
                float b = g >= 0 && g < ghost.Length ? ghost[g] : 0f;
                float mixed = a + b * 0.33f;
                float shaped = (float)System.Math.Tanh(mixed * drive) / norm;
                result[i] = shaped;
                peak = Mathf.Max(peak, Mathf.Abs(shaped));
            }
            if (peak > 0.001f)
            {
                float gain = 0.92f / peak;
                for (int i = 0; i < length; i++)
                {
                    result[i] *= gain;
                }
            }
            return result;
        }

        /// <summary>
        /// Милый голос: тон выше на 12% (генератор заранее замедляет речь, чтобы темп остался
        /// естественным) и ровная громкость. Без второго голоса и насыщения — только чистота.
        /// </summary>
        public static float[] Cute(float[] source)
        {
            float[] result = Resample(source, 1.12f);
            float peak = 0f;
            for (int i = 0; i < result.Length; i++)
            {
                peak = Mathf.Max(peak, Mathf.Abs(result[i]));
            }
            if (peak > 0.001f)
            {
                float gain = 0.9f / peak;
                for (int i = 0; i < result.Length; i++)
                {
                    result[i] *= gain;
                }
            }
            return result;
        }

        /// <summary>Передискретизация: pitch &lt; 1 — ниже и медленнее, как у плёнки на малой скорости.</summary>
        public static float[] Resample(float[] source, float pitch)
        {
            int length = Mathf.Max(1, (int)(source.Length / pitch));
            var result = new float[length];
            for (int i = 0; i < length; i++)
            {
                float src = i * pitch;
                int i0 = (int)src;
                int i1 = Mathf.Min(i0 + 1, source.Length - 1);
                float frac = src - i0;
                result[i] = i0 < source.Length ? Mathf.Lerp(source[i0], source[i1], frac) : 0f;
            }
            return result;
        }
    }
}
