using System;
using System.Collections.Generic;
using UnityEngine;

namespace PlayerRoles.PlayableScps.Scp3114
{
    // Scoped current-client key formatting. Native Format/GetBestVersion/TranslateKey
    // evidence is retained in proof/fidelity_20261022/ui_reference/key_format_audit.
    public sealed class Scp3114NativeKeycodeFormatter : UniversalTextModifier
    {
        [Serializable]
        private struct Key
        {
            public ActionName Action;
            public int FormatIndex;
            public string AdditionalFormat;
        }

        [SerializeField] private Key[] _keys;
        [SerializeField] private int _maxChars;
        [SerializeField] private string _missingFormat;
        [SerializeField] private TextAsset _nativeKeyNames;

        private TextLanguageReplacer _cachedtlr;
        private bool _cacheSet;
        private Scp3114NativeKeyNameTable _keyNameTable;

        private TextLanguageReplacer TextLangReplacer
        {
            get
            {
                if (!_cacheSet)
                {
                    _cachedtlr = GetComponent<TextLanguageReplacer>();
                    _cacheSet = true;
                }
                return _cachedtlr;
            }
        }

        protected override void Awake()
        {
            base.Awake();
            Format();
            TextLangReplacer.OnUpdated += Format;
        }

        private void OnDestroy()
        {
            if (_cachedtlr != null) _cachedtlr.OnUpdated -= Format;
        }

        private void Format()
        {
            if (_keys == null || _keys.Length == 0) return;
            int maxIndex = 0;
            for (int i = 0; i < _keys.Length; i++) maxIndex = Mathf.Max(maxIndex, _keys[i].FormatIndex);
            var formatArgs = new string[maxIndex + 1];
            for (int i = 0; i < _keys.Length; i++)
            {
                var key = _keys[i];
                string keyName = GetDisplayName(NewInput.GetKey(key.Action), _maxChars);
                if (!string.IsNullOrEmpty(key.AdditionalFormat)) keyName = string.Format(key.AdditionalFormat, keyName);
                formatArgs[key.FormatIndex] = keyName;
            }

            var tlr = TextLangReplacer;
            // This older engine caches each modifier's last string separately. Read
            // the TLR's unformatted source on every refresh, as the native modifier
            // reads the newly translated label, rather than formatting stale output.
            var selector = GetComponent<Scp3114NativeSubstringSelector>();
            string format = tlr.Missing ? _missingFormat : selector != null ? selector.SelectedSourceText : tlr.GetCurrentTranslatedText();
            DisplayText = format;
            DisplayText = string.Format(format, formatArgs);
        }

        internal void RefreshSelectedSource()
        {
            Format();
        }

        public string GetDisplayName(KeyCode keyCode, int maxCharacters)
        {
            if (_keyNameTable == null) _keyNameTable = new Scp3114NativeKeyNameTable(_nativeKeyNames);
            return new Scp3114NativeReadableKeyCode(keyCode, _keyNameTable, TranslationReader.TranslationDirectoryName).GetBestVersion(maxCharacters);
        }
    }

    internal sealed class Scp3114NativeKeyNameTable
    {
        [Serializable]
        private sealed class Locale
        {
            public string name;
            public string[] rows;
        }

        [Serializable]
        private sealed class Alias
        {
            public string from;
            public string to;
        }

        [Serializable]
        private sealed class Data
        {
            public int schemaVersion;
            public string fallbackLocale;
            public Locale[] locales;
            public Alias[] aliases;
        }

        private readonly Dictionary<string, string[]> _locales = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly string _fallback;

        public Scp3114NativeKeyNameTable(TextAsset source)
        {
            if (source == null) throw new InvalidOperationException("SCP-3114 native KeyCodes data is not bound");
            var data = JsonUtility.FromJson<Data>(source.text);
            if (data == null || data.schemaVersion != 1 || data.locales == null || data.aliases == null)
                throw new InvalidOperationException("Invalid SCP-3114 native KeyCodes data");
            foreach (var locale in data.locales)
            {
                if (locale.rows == null || locale.rows.Length != 19) throw new InvalidOperationException("Incomplete native KeyCodes locale: " + locale.name);
                _locales.Add(locale.name, locale.rows);
            }
            foreach (var alias in data.aliases) _aliases.Add(alias.from, alias.to);
            _fallback = data.fallbackLocale;
            if (string.IsNullOrEmpty(_fallback) || !_locales.ContainsKey(_fallback)) throw new InvalidOperationException("Native KeyCodes fallback is absent");
        }

        public string Get(string locale, int row, string englishNormal)
        {
            locale = locale ?? _fallback;
            if (_aliases.TryGetValue(locale, out var alias)) locale = alias;
            if (_locales.TryGetValue(locale, out var rows) && !string.IsNullOrWhiteSpace(rows[row])) return rows[row];
            string fallback = _locales[_fallback][row];
            return string.IsNullOrWhiteSpace(fallback) ? englishNormal : fallback;
        }
    }

    internal readonly struct Scp3114NativeReadableKeyCode
    {
        public readonly string NormalVersion;
        public readonly string ShortVersion;

        public Scp3114NativeReadableKeyCode(KeyCode keyCode, Scp3114NativeKeyNameTable translations, string locale)
        {
            GetReadableForm(keyCode, out string normal, out string shortName);
            if (TryGetTranslationRow(keyCode, out int row))
            {
                string translated = translations.Get(locale, row, normal);
                int button = (int)keyCode - (int)KeyCode.Mouse0;
                normal = button >= 3 && button <= 6 ? string.Format(translated, normal[normal.Length - 1].ToString()) : translated;
            }
            NormalVersion = normal;
            ShortVersion = shortName;
        }

        public string GetBestVersion(int maxCharacters)
        {
            return NormalVersion.Length <= maxCharacters ? NormalVersion : ShortVersion;
        }

        private static bool TryGetTranslationRow(KeyCode code, out int row)
        {
            switch (code)
            {
                case KeyCode.UpArrow: row = 2; break;
                case KeyCode.DownArrow: row = 3; break;
                case KeyCode.LeftArrow: row = 4; break;
                case KeyCode.RightArrow: row = 5; break;
                case KeyCode.LeftShift: row = 6; break;
                case KeyCode.RightShift: row = 7; break;
                case KeyCode.LeftControl: row = 8; break;
                case KeyCode.RightControl: row = 9; break;
                case KeyCode.LeftAlt: row = 10; break;
                case KeyCode.RightAlt: row = 11; break;
                case KeyCode.Tab: row = 12; break;
                case KeyCode.Space: row = 13; break;
                case KeyCode.Return: row = 14; break;
                case KeyCode.Mouse0: row = 15; break;
                case KeyCode.Mouse1: row = 16; break;
                case KeyCode.Mouse2: row = 17; break;
                case KeyCode.Mouse3:
                case KeyCode.Mouse4:
                case KeyCode.Mouse5:
                case KeyCode.Mouse6: row = 18; break;
                default: row = -1; return false;
            }
            return true;
        }

        private static void GetReadableForm(KeyCode code, out string normal, out string shortName)
        {
            switch (code)
            {
                case KeyCode.Escape: normal = "Escape"; shortName = "Esc"; break;
                case KeyCode.BackQuote: normal = "~"; shortName = "~"; break;
                case KeyCode.Backspace: normal = "Backspace"; shortName = "BKSP"; break;
                case KeyCode.Insert: normal = "Insert"; shortName = "Ins"; break;
                case KeyCode.Delete: normal = "Delete"; shortName = "Del"; break;
                case KeyCode.PageUp: normal = "Page Up"; shortName = "PgUp"; break;
                case KeyCode.PageDown: normal = "Page Down"; shortName = "PgDn"; break;
                case KeyCode.UpArrow: normal = "Up Arrow"; shortName = "▲"; break;
                case KeyCode.DownArrow: normal = "Down Arrow"; shortName = "▼"; break;
                case KeyCode.LeftArrow: normal = "Left Arrow"; shortName = "◄"; break;
                case KeyCode.RightArrow: normal = "Right Arrow"; shortName = "►"; break;
                case KeyCode.LeftShift: normal = "Left Shift"; shortName = "Left ⇧"; break;
                case KeyCode.RightShift: normal = "Right Shift"; shortName = "Right ⇧"; break;
                case KeyCode.LeftControl: normal = "Left Control"; shortName = "L Ctrl"; break;
                case KeyCode.RightControl: normal = "Right Control"; shortName = "R Ctrl"; break;
                case KeyCode.LeftAlt: normal = "Left Alt"; shortName = "L Alt"; break;
                case KeyCode.RightAlt: normal = "Right Alt"; shortName = "R Alt"; break;
                case KeyCode.Mouse0: normal = "Left Mouse Button"; shortName = "LMB"; break;
                case KeyCode.Mouse1: normal = "Right Mouse Button"; shortName = "RMB"; break;
                case KeyCode.Mouse2: normal = "Middle Mouse Button"; shortName = "MMB"; break;
                case KeyCode.Mouse3:
                case KeyCode.Mouse4:
                case KeyCode.Mouse5:
                case KeyCode.Mouse6:
                    normal = code.ToString().Insert(5, " Button ");
                    shortName = "MB" + normal[normal.Length - 1];
                    break;
                case KeyCode.Alpha0:
                case KeyCode.Alpha1:
                case KeyCode.Alpha2:
                case KeyCode.Alpha3:
                case KeyCode.Alpha4:
                case KeyCode.Alpha5:
                case KeyCode.Alpha6:
                case KeyCode.Alpha7:
                case KeyCode.Alpha8:
                case KeyCode.Alpha9:
                    normal = ((int)code - (int)KeyCode.Alpha0).ToString();
                    shortName = normal;
                    break;
                case KeyCode.Keypad0:
                case KeyCode.Keypad1:
                case KeyCode.Keypad2:
                case KeyCode.Keypad3:
                case KeyCode.Keypad4:
                case KeyCode.Keypad5:
                case KeyCode.Keypad6:
                case KeyCode.Keypad7:
                case KeyCode.Keypad8:
                case KeyCode.Keypad9:
                    int number = (int)code - (int)KeyCode.Keypad0;
                    normal = "Numpad " + number;
                    shortName = "Num " + number;
                    break;
                default: normal = code.ToString(); shortName = normal; break;
            }
        }
    }
}
