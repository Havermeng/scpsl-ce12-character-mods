using PlayerRoles.FirstPersonControl;
using UnityEngine;

namespace PlayerRoles.PlayableScps.Scp3114
{
    // 12.0.2 has no 14.x chase-theme bus. Drive the original stems from the same threat state.
    public class Scp3114ChaseThemeProvider : MonoBehaviour
    {
        [SerializeField] private AudioClip _baselineClip;
        [SerializeField] private AudioClip _drumsClip;
        [SerializeField] private float _audibleDistance = 20f;
        private Scp3114Role _role;
        private Scp3114StrangleAudio _strangle;
        private AudioSource _baseline, _drums;
        private void Awake()
        {
            _role = GetComponentInParent<Scp3114Role>();
            if (_role != null) _role.SubroutineModule.TryGetSubroutine(out _strangle);
            _baseline = gameObject.AddComponent<AudioSource>();
            _drums = gameObject.AddComponent<AudioSource>();
            _baseline.loop = _drums.loop = true;
            _baseline.playOnAwake = _drums.playOnAwake = false;
            _baseline.spatialBlend = _drums.spatialBlend = 0;
            _baseline.volume = _drums.volume = 0;
        }
        private void Update()
        {
            float weight = 0;
            bool strangled = _strangle != null && _strangle.LocallyStrangled;
            if (_role != null && !_role.Pooled && Scp3114Compatibility.TryGetPovHub(out var pov) && pov.roleManager.CurrentRole is HumanRole human)
            {
                float distance = Vector3.Distance(human.CameraPosition, _role.CameraPosition);
                if (strangled) weight = 1;
                else if (_role.SkeletonIdle && distance < _audibleDistance && !Physics.Linecast(human.CameraPosition, _role.CameraPosition, FpcStateProcessor.Mask))
                    weight = Mathf.Clamp01(1 - distance / _audibleDistance);
            }
            UpdateStem(_baseline, _baselineClip, weight * (strangled ? .3f : .65f));
            UpdateStem(_drums, _drumsClip, weight * .65f);
        }
        private static void UpdateStem(AudioSource source, AudioClip clip, float target)
        {
            if (clip == null) return;
            source.clip = clip;
            source.volume = Mathf.MoveTowards(source.volume, target, Time.deltaTime * 2f);
            if (!source.isPlaying && source.volume > .001f) source.Play();
            else if (source.isPlaying && source.volume <= .001f) source.Stop();
        }
        private void OnDisable()
        {
            if (_baseline != null) _baseline.Stop();
            if (_drums != null) _drums.Stop();
        }
    }
}
