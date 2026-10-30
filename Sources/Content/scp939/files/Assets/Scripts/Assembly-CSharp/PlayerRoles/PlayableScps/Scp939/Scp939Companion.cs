using Mirror;
using PlayerRoles.FirstPersonControl;
using RelativePositioning;
using UnityEngine;

namespace PlayerRoles.PlayableScps.Scp939
{
    /// <summary>
    /// Держит 939 рядом с игроком, пока он ведёт её за руку: она идёт сбоку и смотрит по ходу движения.
    ///
    /// Живёт только на сервере. Позиция задаётся через <c>Motor.ReceivedPosition</c>, как в штатном
    /// <see cref="CommandSystem.Commands.RemoteAdmin.Dummies.PlayerFollower"/>, — так модель считает
    /// собственную скорость и проигрывает обычную анимацию ходьбы. Прямая запись в FpcModule.Position
    /// этого не даёт: скорость остаётся нулевой и 939 «скользит» в позе покоя.
    ///
    /// Сторона выбирается один раз, в момент взятия за руку, и дальше не меняется — иначе 939
    /// перепрыгивала бы с боку на бок каждый раз, когда игрок разворачивается.
    /// </summary>
    public class Scp939Companion : MonoBehaviour
    {
        /// <summary>
        /// Насколько далеко вперёд ставится точка назначения за кадр. Это НЕ скорость (её задаёт
        /// режим движения роли), а длина «поводка»: FpcMotor отвергает точку, если
        /// (расстояние - 0.5) больше максимальной скорости роли, и откатывает позицию.
        /// </summary>
        private const float FollowSpeed = 3.5f;

        /// <summary>Дальше этого — телепорт вплотную, иначе она безнадёжно отстаёт на лифтах и лестницах.</summary>
        private const float TeleportDistance = 12f;

        /// <summary>Ближе этого 939 просто стоит и не поправляет позицию — иначе она мелко дрожит.</summary>
        private const float DeadZone = 0.08f;

        /// <summary>С какого отставания она переходит на бег.</summary>
        private const float SprintDistance = 1.6f;

        private ReferenceHub _scp;
        private ReferenceHub _partner;
        private float _side = 1f;

        public static void Attach(ReferenceHub scp, ReferenceHub partner)
        {
            if (scp == null || partner == null)
            {
                return;
            }

            Detach(scp);

            var companion = scp.gameObject.AddComponent<Scp939Companion>();
            companion._scp = scp;
            companion._partner = partner;
            companion._side = Scp939ModCompatibility.DirectorRunning ? 1f : companion.CurrentSide();
        }

        public static void Detach(ReferenceHub scp)
        {
            if (scp == null)
            {
                return;
            }
            foreach (Scp939Companion old in scp.GetComponents<Scp939Companion>())
            {
                Destroy(old);
            }
        }

        private void OnDestroy()
        {
            if (NetworkServer.active && _scp != null && _scp.roleManager.CurrentRole is IFpcRole role)
            {
                role.FpcModule.CurrentMovementState = PlayerMovementState.Walking;
                role.FpcModule.Motor.ReceivedPosition = new RelativePosition(_scp.transform.position);
            }
        }

        /// <summary>С какой стороны от игрока она стоит прямо сейчас: +1 справа, -1 слева.</summary>
        private float CurrentSide()
        {
            Vector3 rel = _scp.transform.position - _partner.transform.position;
            rel.y = 0f;
            return Vector3.Dot(PartnerRight(), rel) >= 0f ? 1f : -1f;
        }

        private Vector3 PartnerForward()
        {
            var motion=Scp939ModCompatibility.Motion(_partner);
            Vector3 forward = motion!=null && motion.Heading.sqrMagnitude>.5f ? motion.Heading : _partner.PlayerCameraReference.forward;
            forward.y = 0f;
            return forward.sqrMagnitude < 1e-6f ? Vector3.forward : forward.normalized;
        }

        private Vector3 PartnerRight()
        {
            // Cross(up, forward) в Unity — это именно правый вектор. Знак здесь важен: позу руки
            // 939 выбирает по своей стороне, и если стороны считать по-разному, она тянется дальней.
            return Vector3.Cross(Vector3.up, PartnerForward());
        }

        private void Update()
        {
            if (!NetworkServer.active || _scp == null || _partner == null)
            {
                Destroy(this);
                return;
            }

            var scpRole = _scp.roleManager.CurrentRole as IFpcRole;
            if (scpRole == null || !(_partner.roleManager.CurrentRole is IFpcRole))
            {
                Destroy(this);
                return;
            }

            // Упреждение: целимся туда, где партнёр будет через долю секунды. Без него на бегу она отставала почти
            // на метр, и рука, держащая его ладонь, вытягивалась вперёд горизонтально (кадры 2026-09-30).
            Vector3 partnerVelocity = ((IFpcRole)_partner.roleManager.CurrentRole).FpcModule.Motor.Velocity;
            partnerVelocity.y = 0f;
            Vector3 wanted = _partner.transform.position + PartnerRight() * (_side * (Scp939ModCompatibility.Motion(_partner)!=null?.78f:Scp939Interactions.WalkSideOffset))
                             + partnerVelocity * 0.08f;
            Vector3 current = _scp.transform.position;
            float distance = Vector3.Distance(current, wanted);

            if (distance > TeleportDistance)
            {
                // 12.0.2 не имеет однопараметрической перегрузки; нулевая дельта сохраняет поворот.
                scpRole.FpcModule.ServerOverridePosition(wanted, Vector3.zero);
                return;
            }

            var cinemaSpeed=Scp939ModCompatibility.Motion(_scp);
            if(cinemaSpeed!=null && Scp939ModCompatibility.Motion(_partner)!=null)
            {
                Vector3 error=wanted-current;error.y=0;
                Vector3 velocity=partnerVelocity+error*5f;
                velocity=Vector3.ClampMagnitude(velocity,2f);
                scpRole.FpcModule.CurrentMovementState=PlayerMovementState.Walking;
                cinemaSpeed.Multiplier=Mathf.Clamp(velocity.magnitude/Mathf.Max(.1f,scpRole.FpcModule.MaxMovementSpeed),.01f,.65f);
                scpRole.FpcModule.Motor.ReceivedPosition=new RelativePosition(current+velocity);
                Vector3 facing=partnerVelocity.sqrMagnitude>.03f?partnerVelocity:PartnerForward();
                scpRole.FpcModule.MouseLook.CurrentHorizontal=Mathf.Atan2(facing.x,facing.z)*Mathf.Rad2Deg;
                return;
            }

            // Скорость задаёт не шаг ниже, а режим движения: мотор нормализует направление и идёт
            // со скоростью роли. Шагом (4.3) она не успевала за бегущим человеком (5.4), отставала
            // и рвала руку — поэтому, отстав, переходит на бег (7.2), а рядом снова идёт шагом.
            scpRole.FpcModule.CurrentMovementState = distance > SprintDistance || partnerVelocity.magnitude > 4.8f
                ? PlayerMovementState.Sprinting
                : PlayerMovementState.Walking;

            // Мёртвая зона. Без неё 939 каждый кадр подправляет позицию на доли сантиметра, мотор
            // на каждую такую поправку отвечает своим шагом — и со стороны это выглядит как дрожь.
            // Порог заметно больше точности позиционирования, чтобы у неё было право просто стоять.
            if (distance > DeadZone)
            {
                // БЕЗ Time.deltaTime: мотор трактует (ReceivedPosition - Position) как скорость и сам
                // домножает её на deltaTime. Шаг за кадр здесь давал движение в десятки раз медленнее.
                // На подходе скорость гасим, иначе она перелетает цель и дёргается вокруг неё.
                Vector3 dir = (wanted - current).normalized;
                float speed = Mathf.Min(FollowSpeed, Mathf.Max(0.5f, (distance - DeadZone) * 4f));
                scpRole.FpcModule.Motor.ReceivedPosition = new RelativePosition(current + dir * speed);
            }
            else
            {
                // Стоим ровно: цель совпадает с текущей позицией, мотору нечего исправлять.
                scpRole.FpcModule.Motor.ReceivedPosition = new RelativePosition(current);
            }

            // Смотрит по ходу движения пары, а не на партнёра — иначе идёт боком.
            scpRole.FpcModule.MouseLook.LookAtDirection(PartnerForward());
        }
    }
}
