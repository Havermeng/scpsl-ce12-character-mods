using Mirror;
using PlayerRoles.FirstPersonControl;
using RelativePositioning;
using UnityEngine;

namespace PlayerRoles.PlayableScps.Scp939
{
    /// <summary>
    /// «Режим босса»: неудачный подарок или слишком настойчивые ласки выводят 939 из себя, и
    /// несколько секунд она гонится за обидчиком и бьёт когтями. Потом остывает и извиняется.
    ///
    /// Удар — штатный: через действие думми «Attack (forward)», то есть тот же путь, что у когтей
    /// живого игрока (урон, кулдаун, хитрег). Поэтому ярость работает только у 939-думми —
    /// живым игроком за 939 никто, кроме него самого, не управляет.
    ///
    /// Живёт на сервере, на объекте 939.
    /// </summary>
    [DefaultExecutionOrder(-190)]
    public class Scp939Rage : MonoBehaviour
    {
        public const float DefaultDuration = 5.5f;

        /// <summary>С какой дистанции когти достают: захват 1.0 + смещение 0.8 у штатной атаки.</summary>
        private const float StrikeRange = 1.7f;

        /// <summary>На каком расстоянии от цели она останавливается: капсулы не должны слипаться.</summary>
        private const float StopDistance = 1.0f;

        private const float SwingInterval = 1.1f;

        /// <summary>
        /// Сколько ударов за одну ярость. Два когтя по 40 — человек со 100 HP выживает: на дубле
        /// хозяин свидания не должен умирать от каждого неудачного подарка. 0 — без ограничений
        /// (команда <c>939 ragehits</c>).
        /// </summary>
        public static int MaxHits = 2;

        private int _hits;

        private ReferenceHub _scp;
        private ReferenceHub _target;
        private float _until;
        private float _nextSwing;
        private System.Action<ReferenceHub> _onEnd;

        /// <summary>За кем она гонится.</summary>
        public ReferenceHub Target => _target;

        public static bool IsRaging(ReferenceHub scp)
        {
            return scp != null && scp.GetComponent<Scp939Rage>() != null;
        }

        public static void Begin(ReferenceHub scp, ReferenceHub target, float duration, System.Action<ReferenceHub> onEnd)
        {
            if (!NetworkServer.active || scp == null || target == null)
            {
                return;
            }

            Scp939Rage rage = scp.GetComponent<Scp939Rage>();
            if (rage == null)
            {
                rage = scp.gameObject.AddComponent<Scp939Rage>();
            }
            rage._scp = scp;
            rage._target = target;
            rage._until = Time.time + duration;
            // Первый удар не сразу: сначала реплика и рывок — иначе зритель не успевает понять, за что.
            rage._nextSwing = Time.time + 0.6f;
            rage._onEnd = onEnd;
            rage._hits = 0;
        }

        /// <summary>Прервать ярость без реплики «прости» — например, если 939 сменила роль.</summary>
        public static void Cancel(ReferenceHub scp)
        {
            if (scp == null)
            {
                return;
            }
            Scp939Rage rage = scp.GetComponent<Scp939Rage>();
            if (rage != null)
            {
                rage._onEnd = null;
                Destroy(rage);
            }
        }

        private void Update()
        {
            var self = _scp != null ? _scp.roleManager.CurrentRole as IFpcRole : null;
            if (!NetworkServer.active || self == null || _target == null ||
                !(_target.roleManager.CurrentRole is IFpcRole) ||
                !Scp939Interactions.IsInteractable(_scp.roleManager.CurrentRole.RoleTypeId))
            {
                _onEnd = null;
                Destroy(this);
                return;
            }

            if (Time.time >= _until)
            {
                Destroy(this);
                return;
            }

            if (!Scp939ModCompatibility.IsDummy(_scp))
            {
                return;   // живым игроком не управляем — только реплики и шкала
            }

            Vector3 position = _scp.transform.position;
            Vector3 delta = _target.transform.position - position;
            delta.y = 0f;
            float distance = delta.magnitude;

            // Бежит в полную силу: в ярости она обязана догнать, иначе вся сцена — прогулка.
            self.FpcModule.CurrentMovementState = PlayerMovementState.Sprinting;
            if (distance > StopDistance)
            {
                Vector3 step = delta / distance * Mathf.Min(0.25f, distance - StopDistance);
                self.FpcModule.Motor.ReceivedPosition = new RelativePosition(position + step);
            }
            else
            {
                self.FpcModule.Motor.ReceivedPosition = new RelativePosition(position);
            }
            Scp939ModCompatibility.AimAt(_scp, _target);

            if (distance <= StrikeRange && Time.time >= _nextSwing && (MaxHits <= 0 || _hits < MaxHits))
            {
                _hits++;
                _nextSwing = Time.time + SwingInterval;
                Strike();
            }
        }

        private void Strike()
        {
            Scp939ModCompatibility.DummyForwardAttack(_scp);
        }

        private void OnDestroy()
        {
            if (NetworkServer.active && _scp != null && _scp.roleManager.CurrentRole is IFpcRole role)
            {
                role.FpcModule.CurrentMovementState = PlayerMovementState.Walking;
                role.FpcModule.Motor.ReceivedPosition = new RelativePosition(_scp.transform.position);
            }

            System.Action<ReferenceHub> onEnd = _onEnd;
            _onEnd = null;
            if (onEnd != null && _scp != null)
            {
                onEnd(_scp);
            }
        }
    }
}
