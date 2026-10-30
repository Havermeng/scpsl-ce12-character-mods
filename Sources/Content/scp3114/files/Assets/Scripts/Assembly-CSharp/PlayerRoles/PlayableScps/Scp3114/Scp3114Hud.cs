using InventorySystem;
using InventorySystem.GUI;
using InventorySystem.Items;
using PlayerRoles.PlayableScps.HUDs;
using UnityEngine;
using UnityEngine.UI;

namespace PlayerRoles.PlayableScps.Scp3114
{
    // Current retail Scp3114Hud, native Init RVA5F9920 / Update RVA5FA6A0.
    // References/layout are imported from resources.assets:72115.
    public class Scp3114Hud : ViewmodelScpHud
    {
        [SerializeField] private GameObject _hudRoot;
        [SerializeField] private ScpWarningHud _warning;
        [SerializeField] private Scp3114NativeAbilityHud _strangleCooldown;
        [SerializeField] private Scp3114NativeAbilityHud _strangleTimer;
        [SerializeField] private Scp3114NativeAbilityHud _identityCooldown;
        [SerializeField] private LoadingCircleHud _stealSkinBar;
        [SerializeField] private RawImage _avatarImage;
        // Role IDs differ between v12, local Human14 aliases and the donor.
        // Override only this HUD with the donor's complete *_Icon textures.
        [SerializeField] private Texture[] _nativeHumanAvatars;
        // Shared inspection UI is presented with the donor's style only for this role.
        [SerializeField] private TMPro.TMP_FontAsset _inspectionFont;
        [SerializeField] private Material _inspectionMaterial;
        [SerializeField] private TextAsset _nativeKeyNames;
        private Scp3114NativeKeyNameTable _keyNames;
        private Scp3114Strangle _strangle;
        private Scp3114Identity _identity;
        private Scp3114Disguise _disguise;
        private Scp3114Role _scpRole;
        private bool _wasLocalPlayer;

        private Texture CurAvatar
        {
            get
            {
                if (_scpRole.CurIdentity.Status == Scp3114Identity.DisguiseStatus.Active &&
                    PlayerRoleLoader.AllRoles.TryGetValue(_identity.CurIdentity.StolenRole, out var role) &&
                    role is IAvatarRole avatarRole)
                {
                    string name = _identity.CurIdentity.StolenRole.ToString();
                    int family = name.Contains("ClassD") ? 0 : name.Contains("Scientist") ? 1
                        : name.Contains("Guard") ? 2 : name.StartsWith("Ntf") ? 3
                        : name.StartsWith("Chaos") ? 4 : name.Contains("Tutorial") ? 5 : -1;
                    if (family >= 0 && _nativeHumanAvatars != null && family < _nativeHumanAvatars.Length)
                        return _nativeHumanAvatars[family];
                    return avatarRole.RoleAvatar;
                }
                return _scpRole.RoleAvatar;
            }
        }
        internal override void Init(ReferenceHub hub)
        {
            base.Init(hub);
            _scpRole = hub.roleManager.CurrentRole as Scp3114Role;
            if (_scpRole == null) return;
            _scpRole.SubroutineModule.TryGetSubroutine(out _identity);
            _scpRole.SubroutineModule.TryGetSubroutine(out _disguise);
            _scpRole.SubroutineModule.TryGetSubroutine(out _strangle);
            _strangleCooldown.Setup(_strangle.Cooldown, null);
            _strangleTimer.Setup(_strangle.StrangleTimer, null);
            _identityCooldown.Setup(_disguise.Cooldown, _identity.RemainingDuration);
            _wasLocalPlayer = hub.isLocalPlayer;
            if (!_wasLocalPlayer) return;
            RagdollInspector.Set3114Presentation(_inspectionFont, _inspectionMaterial);
            _identity.CurIdentity.OnStatusChanged += OnIdentityChanged;
            _disguise.OnErrorReceived += OnDisguiseErrorReceived;
            _disguise.OnClientError += PlayWarning;
            _strangle.OnAttemptedWhileDisguised += OnAttackWhileDisguised;
            Inventory.OnCurrentItemChanged += OnCurrentItemChanged;
            Scp3114Reveal.OnRevealFail += OnRevealFail;
            if (_scpRole.VoiceModule is Scp3114VoiceModule voice)
                voice.OnHintTriggered += OnVcHintTriggered;
        }
        internal override void OnDied()
        {
            if (_hudRoot != null) _hudRoot.SetActive(false);
        }
        protected override void Update()
        {
            base.Update();
            if (_scpRole == null || _scpRole.Pooled || _disguise == null) return;
            _stealSkinBar.Apply(_disguise.ProgressStatus);
            _strangleCooldown.Update();
            _strangleTimer.Update();
            _identityCooldown.Update();
            _avatarImage.texture = CurAvatar;
            if (_wasLocalPlayer && _scpRole.Disguised && InventoryGuiController.ItemsSafeForInteraction &&
                Input.GetKey(NewInput.GetKey(ActionName.Shoot)))
                PlayWarning(Scp3114HudTranslation.UnableToAttackWarning, ActionName.Reload);
        }
        private void OnIdentityChanged()
        {
            if (_scpRole.CurIdentity.Status != Scp3114Identity.DisguiseStatus.Active) return;
            if (PlayerRoleLoader.AllRoles.TryGetValue(_identity.CurIdentity.StolenRole, out var role))
            {
                var name = "<color=#" + ColorUtility.ToHtmlStringRGB(role.RoleColor) + ">" + role.RoleName + "</color>";
                PlayWarning(Scp3114HudTranslation.IdentityStolenWarning, name);
            }
        }
        private void OnDisguiseErrorReceived(byte errorCode) { PlayWarning((Scp3114HudTranslation)errorCode); }
        private void OnAttackWhileDisguised() { PlayWarning(Scp3114HudTranslation.UnableToAttackWarning, ActionName.Reload); }
        private void OnRevealFail() { PlayWarning(Scp3114HudTranslation.RevealKeyHoldLongerWarning, ActionName.Reload); }
        private void OnVcHintTriggered() { PlayWarning(Scp3114HudTranslation.ProximityVoiceHint, ActionName.AltVoiceChat); }
        private void OnCurrentItemChanged(ReferenceHub player, ItemIdentifier previous, ItemIdentifier current)
        {
            if (player != null && player.isLocalPlayer && previous.TypeId == ItemType.None && current.TypeId != ItemType.None)
                PlayWarning(Scp3114HudTranslation.EquipItemWarning);
        }
        private void PlayWarning(Scp3114HudTranslation key)
        {
            if (_warning != null) _warning.SetText(Translations.TryGet(key, out var value) ? value : key.ToString());
        }
        private void PlayWarning(Scp3114HudTranslation key, string formatArg)
        {
            var value = Translations.TryGet(key, out var text) ? text : key + " - {0}";
            if (_warning != null) _warning.SetText(string.Format(value, formatArg));
        }
        private void PlayWarning(Scp3114HudTranslation key, ActionName action)
        {
            if (_keyNames == null) _keyNames = new Scp3114NativeKeyNameTable(_nativeKeyNames);
            string keyName = new Scp3114NativeReadableKeyCode(NewInput.GetKey(action), _keyNames,
                TranslationReader.TranslationDirectoryName).NormalVersion;
            PlayWarning(key, "<color=red>" + keyName + "</color>");
        }
        protected override void OnDestroy()
        {
            if (_wasLocalPlayer)
            {
                if (_identity != null) _identity.CurIdentity.OnStatusChanged -= OnIdentityChanged;
                if (_disguise != null)
                {
                    _disguise.OnErrorReceived -= OnDisguiseErrorReceived;
                    _disguise.OnClientError -= PlayWarning;
                }
                if (_strangle != null) _strangle.OnAttemptedWhileDisguised -= OnAttackWhileDisguised;
                Inventory.OnCurrentItemChanged -= OnCurrentItemChanged;
                Scp3114Reveal.OnRevealFail -= OnRevealFail;
                if (_scpRole != null && _scpRole.VoiceModule is Scp3114VoiceModule voice)
                    voice.OnHintTriggered -= OnVcHintTriggered;
            }
            base.OnDestroy();
        }
    }
}
