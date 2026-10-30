using UnityEngine;

namespace PlayerRoles.PlayableScps.Scp3114
{
    // Original selector fields and source split, scoped to the imported 3114 UI.
    public sealed class Scp3114NativeSubstringSelector : UniversalTextModifier
    {
        [SerializeField] private int _index;
        [SerializeField] private char _divisionChar;

        private TextLanguageReplacer _replacer;

        private TextLanguageReplacer Replacer
        {
            get
            {
                if (_replacer == null) _replacer = GetComponent<TextLanguageReplacer>();
                return _replacer;
            }
        }

        public string SelectedSourceText
        {
            get
            {
                string source = Replacer.GetCurrentTranslatedText();
                string[] parts = source.Split(new[] { _divisionChar });
                return _index < parts.Length ? parts[_index] : source;
            }
        }

        protected override void Awake()
        {
            base.Awake();
            Divide();
            Replacer.OnUpdated += Divide;
        }

        private void OnDestroy()
        {
            if (_replacer != null) _replacer.OnUpdated -= Divide;
        }

        private void Divide()
        {
            // UniversalTextModifier in this older engine caches a separate string
            // per component. Refresh from the source every time; when this label
            // also formats keys, apply that final step after selection regardless
            // of the components' Awake/event subscription order.
            if (TryGetComponent<Scp3114NativeKeycodeFormatter>(out var formatter))
            {
                formatter.RefreshSelectedSource();
                return;
            }
            DisplayText = Replacer.GetCurrentTranslatedText();
            DisplayText = SelectedSourceText;
        }
    }
}
