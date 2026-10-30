using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using AudioPooling;
using Mirror;
using PlayerRoles.FirstPersonControl;
using PlayerRoles.PlayableScps.Subroutines;
using PlayerRoles.Spectating;
using PlayerStatsSystem;
using RelativePositioning;
using UnityEngine;
using Utils.Networking;

namespace PlayerRoles.PlayableScps.Scp3114
{
    public abstract class Scp3114AttackAbilityBase<T> : ScpKeySubroutine<T> where T : PlayerRoleBase, IFpcRole
    {
        [SerializeField]
        private float _detectionRadius = .65f;

        [SerializeField]
        private float _detectionOffset = .85f;

        [SerializeField]
        private AudioClip _killSound;

        [SerializeField]
        private AudioClip[] _hitClipsHuman = Array.Empty<AudioClip>();

        [SerializeField]
        private AudioClip[] _hitClipsObjects = Array.Empty<AudioClip>();

        private bool _attackTriggered;

        private AttackResult _syncAttack;

        private readonly Stopwatch _delaySw = new Stopwatch();

        private readonly TolerantAbilityCooldown _clientCooldown = new TolerantAbilityCooldown(0.2f);

        private readonly TolerantAbilityCooldown _serverCooldown = new TolerantAbilityCooldown(0.2f);

        private static readonly HashSet<ReferenceHub> TargettedPlayers = new HashSet<ReferenceHub>();

        /// <summary>Кого удар думми уже задел в этом замахе — по одному разу на человека.</summary>
        private static readonly HashSet<ReferenceHub> DummyHitHubs = new HashSet<ReferenceHub>();

        private static readonly HashSet<FpcBacktracker> BacktrackedPlayers = new HashSet<FpcBacktracker>();

        private static readonly Collider[] DetectionsNonAlloc = new Collider[128];

        private static readonly CachedLayerMask DetectionMask = new CachedLayerMask("Hitbox", "Glass");

        private static readonly CachedLayerMask BlockerMask = new CachedLayerMask("Locker", "Default", "Door");

        private const int DetectionsNumber = 128;

        public TolerantAbilityCooldown Cooldown
        {
            get
            {
                if (!base.Owner.isLocalPlayer && NetworkServer.active)
                {
                    return _serverCooldown;
                }
                return _clientCooldown;
            }
        }

        public abstract float DamageAmount { get; }

        protected abstract DamageHandlerBase DamageHandler { get; }

        protected virtual float SoundRange => 13f;

        protected virtual float AttackDelay => 0f;

        protected virtual float BaseCooldown => 1f;

        protected virtual bool SelfRepeating => true;

        protected virtual bool CanTriggerAbility => _clientCooldown.IsReady;

        protected virtual bool ServerAbilityAllowed => true;

        protected override ActionName TargetKey => ActionName.Shoot;

        protected override bool KeyPressable
        {
            get
            {
                if (!base.KeyPressable)
                    return false;
                return InventorySystem.GUI.InventoryGuiController.ItemsSafeForInteraction;
            }
        }

        private Transform PlyCam => base.Owner.PlayerCameraReference;

        private Vector3 OverlapSphereOrigin => PlyCam.position + PlyCam.forward * _detectionOffset;

        public event Action<AttackResult> OnAttacked
        {
            [CompilerGenerated]
            add
            {
                Action<AttackResult> action = _onAttacked;
                Action<AttackResult> action2;
                do
                {
                    action2 = action;
                    Action<AttackResult> value2 = (Action<AttackResult>)Delegate.Combine(action2, value);
                    action = Interlocked.CompareExchange(ref _onAttacked, value2, action2);
                }
                while (action != action2);
            }
            [CompilerGenerated]
            remove
            {
                Action<AttackResult> action = _onAttacked;
                Action<AttackResult> action2;
                do
                {
                    action2 = action;
                    Action<AttackResult> value2 = (Action<AttackResult>)Delegate.Remove(action2, value);
                    action = Interlocked.CompareExchange(ref _onAttacked, value2, action2);
                }
                while (action != action2);
            }
        }

        [CompilerGenerated]
        private Action<AttackResult> _onAttacked;

        public event Action OnTriggered
        {
            [CompilerGenerated]
            add
            {
                Action action = _onTriggered;
                Action action2;
                do
                {
                    action2 = action;
                    Action value2 = (Action)Delegate.Combine(action2, value);
                    action = Interlocked.CompareExchange(ref _onTriggered, value2, action2);
                }
                while (action != action2);
            }
            [CompilerGenerated]
            remove
            {
                Action action = _onTriggered;
                Action action2;
                do
                {
                    action2 = action;
                    Action value2 = (Action)Delegate.Remove(action2, value);
                    action = Interlocked.CompareExchange(ref _onTriggered, value2, action2);
                }
                while (action != action2);
            }
        }

        [CompilerGenerated]
        private Action _onTriggered;

        private void ServerPerformAttack()
        {
            ReferenceHub primaryTarget = FindPrimaryTarget();
            int num = Physics.OverlapSphereNonAlloc(OverlapSphereOrigin, _detectionRadius, DetectionsNonAlloc, DetectionMask);
            _syncAttack = AttackResult.None;
            DummyHitHubs.Clear();
            for (int i = 0; i < num; i++)
            {
                if (!DetectionsNonAlloc[i].TryGetComponent<IDestructible>(out var component))
                {
                    continue;
                }
                if (Physics.Linecast(PlyCam.position, component.CenterOfMass, BlockerMask))
                {
                    continue;
                }
                // Отбор по TargettedPlayers — это проверка клиентской заявки: настоящий игрок
                // перечисляет в команде, кого он бьёт, и сервер бьёт только их. У ДУММИ клиента
                // нет, список всегда пуст, поэтому такая проверка отменяла ему любой удар по
                // живым игрокам (по дверям и прочим разрушаемым объектам удары при этом шли —
                // отсюда и ощущение «атака работает через раз»). Для думми проверку пропускаем.
                if (component is HitboxIdentity hitboxIdentity
                    && !Scp3114Compatibility.IsDummy(base.Owner)
                    && !TargettedPlayers.Remove(hitboxIdentity.TargetHub))
                {
                    continue;
                }
                // У игрока несколько хитбоксов (голова, корпус, конечности), и все они попадают в
                // сферу. У настоящего игрока дубли отсекает тот же TargettedPlayers.Remove — он
                // срабатывает один раз на человека. У думми этой проверки нет, и один удар бил по
                // КАЖДОМУ хитбоксу: замер — 3 × 40 урона в одном кадре, человек умирал с одного
                // когтя. Для думми считаем каждого человека один раз и не бьём самого себя.
                if (component is HitboxIdentity dummyTarget && Scp3114Compatibility.IsDummy(base.Owner)
                    && (dummyTarget.TargetHub == base.Owner || !DummyHitHubs.Add(dummyTarget.TargetHub)))
                {
                    continue;
                }
                if (component is HitboxIdentity candidate && candidate.TargetHub != primaryTarget) continue;
                if (!component.Damage(DamageAmount, DamageHandler, component.CenterOfMass))
                {
                    continue;
                }

                OnDestructibleDamaged(component);
                _syncAttack |= AttackResult.AttackedObject;

                if (component is HitboxIdentity hitboxIdentity2)
                {
                    _syncAttack |= AttackResult.AttackedHuman;
                    if (!(hitboxIdentity2.TargetHub.playerStats.GetModule<HealthStat>().CurValue > 0f))
                    {
                        _syncAttack |= AttackResult.KilledHuman;
                    }
                }
            }
            ServerSendRpc(toAll: true);
        }

        private ReferenceHub FindPrimaryTarget()
        {
            ReferenceHub best = null;
            float bestDot = -1f;
            foreach (ReferenceHub candidate in ReferenceHub.AllHubs)
            {
                if (candidate == Owner || !(candidate.roleManager.CurrentRole is HumanRole human)) continue;
                if (!HitboxIdentity.IsEnemy(Owner, candidate)) continue;
                if ((human.FpcModule.Position - ScpRole.FpcModule.Position).sqrMagnitude > 9f) continue;
                float dot = human.GetDot(PlyCam);
                if (dot <= bestDot) continue;
                if (Physics.Linecast(PlyCam.position, human.CameraPosition, BlockerMask)) continue;
                bestDot = dot;
                best = candidate;
            }
            return best;
        }

        protected virtual void OnDestructibleDamaged(IDestructible dest)
        {
        }

        public override void ClientWriteCmd(NetworkWriter writer)
        {
            base.ClientWriteCmd(writer);
            if (_attackTriggered)
            {
                RelativePositionSerialization.WriteRelativePosition(writer, default(RelativePosition));
                return;
            }
            
            Vector3 position = base.ScpRole.FpcModule.Position;
            float num = _detectionOffset + _detectionRadius;
            float num2 = num * num;
            
            RelativePosition selfRelative = new RelativePosition(position);
            RelativePositionSerialization.WriteRelativePosition(writer, selfRelative);
            writer.WriteLowPrecisionQuaternion(new LowPrecisionQuaternion(PlyCam.rotation));

            int writtenTargets = 0;
            foreach (ReferenceHub allHub in ReferenceHub.AllHubs)
            {
                if (allHub.roleManager.CurrentRole is HumanRole humanRole)
                {
                    Vector3 position2 = humanRole.FpcModule.Position;
                    if (!((position2 - position).sqrMagnitude > num2))
                    {
                        ReferenceHubReaderWriter.WriteReferenceHub(writer, allHub);
                        RelativePositionSerialization.WriteRelativePosition(writer, new RelativePosition(position2));
                        writtenTargets++;
                    }
                }
            }
        }

        public override void ServerProcessCmd(NetworkReader reader)
        {
            base.ServerProcessCmd(reader);
            if (!ServerAbilityAllowed || Role.Pooled) return;
            RelativePosition relativePosition = RelativePositionSerialization.ReadRelativePosition(reader);
            
            if (relativePosition.WaypointId == 0)
            {
                _attackTriggered = true;
                ServerSendRpc(toAll: true);
            }
            else
            {
                if (!_serverCooldown.TolerantIsReady && !base.Owner.isLocalPlayer)
                {
                    return;
                }
                
                _attackTriggered = false;
                Vector3 position = relativePosition.Position;
                Quaternion value = reader.ReadLowPrecisionQuaternion().Value;
                
                if (!Scp3114Compatibility.IsFinite(position) || !Scp3114Compatibility.IsFinite(value)) return;
                ReferenceHub.TryGetHostHub(out ReferenceHub hostHub);
                bool restoreHostHitboxes = false;
                try
                {
                    BacktrackedPlayers.Add(new FpcBacktracker(base.Owner, position, value));
                    while (reader.Remaining > 0)
                    {
                        ReferenceHub referenceHub = reader.ReadReferenceHub();
                        Vector3 claimedPosition = reader.ReadRelativePosition().Position;
                        if (!Scp3114Compatibility.IsFinite(claimedPosition)) return;
                        if (referenceHub == null || !(referenceHub.roleManager.CurrentRole is HumanRole)) continue;
                        BacktrackedPlayers.Add(new FpcBacktracker(referenceHub, claimedPosition));
                        TargettedPlayers.Add(referenceHub);
                    }
                    restoreHostHitboxes = !base.Owner.isLocalPlayer && HitboxIdentity.SetOwnHitboxes(hostHub, true);
                    ServerPerformAttack();
                    _serverCooldown.Trigger(BaseCooldown);
                }
                finally
                {
                    if (restoreHostHitboxes) HitboxIdentity.SetOwnHitboxes(hostHub, false);
                    foreach (FpcBacktracker backtracker in BacktrackedPlayers) backtracker.RestorePosition();
                    BacktrackedPlayers.Clear();
                    TargettedPlayers.Clear();
                }
                ServerSendRpc(toAll: true);
            }
        }

        public override void ServerWriteRpc(NetworkWriter writer)
        {
            base.ServerWriteRpc(writer);
            if (!_attackTriggered)
            {
                writer.WriteByte((byte)_syncAttack);
            }
        }

        public override void ClientProcessRpc(NetworkReader reader)
        {
            base.ClientProcessRpc(reader);
            
            if (reader.Position >= reader.Capacity)
            {
                if (!base.Owner.isLocalPlayer)
                {
                    _clientCooldown.Trigger(BaseCooldown);
                    this._onTriggered?.Invoke();
                }
                return;
            }
            
            _syncAttack = (AttackResult)reader.ReadByte();
            this._onAttacked?.Invoke(_syncAttack);
            
            if (_syncAttack != AttackResult.None && (base.Owner.isLocalPlayer || SpectatorNetworking.IsLocallySpectated(base.Owner)))
            {
                Hitmarker.PlayHitmarker(1f);
            }
            
            if (HasFlagFast(AttackResult.KilledHuman) && _killSound != null)
            {
                AudioSourcePoolManager.PlaySound(_killSound, base.transform, SoundRange);
            }
            else if (HasFlagFast(AttackResult.AttackedHuman) && _hitClipsHuman.Length != 0)
            {
                AudioSourcePoolManager.PlaySound(_hitClipsHuman.RandomItem(), base.transform, SoundRange);
            }
            else if (HasFlagFast(AttackResult.AttackedObject) && _hitClipsObjects.Length != 0)
            {
                AudioSourcePoolManager.PlaySound(_hitClipsObjects.RandomItem(), base.transform, SoundRange);
            }
        }

        public override void ResetObject()
        {
            base.ResetObject();
            _attackTriggered = false;
            _delaySw.Reset();
            _clientCooldown.Clear();
            _serverCooldown.Clear();
            TargettedPlayers.Clear();
            BacktrackedPlayers.Clear();
        }

        protected override void Update()
        {
            base.Update();

            if (base.Owner == null)
                return;

            // Дамми не является локальным игроком, а сама атака выдаётся именно в OnClientUpdate:
            // при SelfRepeating (значение по умолчанию у всех SCP) OnKeyDown выходит сразу и удара
            // не наносит. Из-за этой проверки у дамми вставала IsKeyHeld, но атака не происходила
            // вообще никогда. На сервере пускаем дамми по тому же пути: ClientSendCmd для него
            // замкнут на ServerProcessCmd в SubroutineBase, так что дальше всё идёт как у игрока.
            if (base.Owner.isLocalPlayer || (NetworkServer.active && Scp3114Compatibility.IsDummy(base.Owner)))
                OnClientUpdate();
        }

        protected virtual void OnClientUpdate()
        {
            if (_attackTriggered)
            {
                if (_delaySw.Elapsed.TotalSeconds >= AttackDelay)
                {
                    _attackTriggered = false;
                    ClientSendCmd();
                }
                return;
            }

            // ISIL: обе точки вызова передают константу true — замах шлётся всегда,
            // реальный удар уходит вторым пакетом после AttackDelay (для зомби — следующий кадр).
            if (SelfRepeating && IsKeyHeld && CanTriggerAbility)
                ClientPerformAttack();
        }

        protected override void OnKeyDown()
        {
            base.OnKeyDown();
            
            if (_attackTriggered)
                return;
            
            if (SelfRepeating)
                return;
            
            if (!CanTriggerAbility)
                return;
            
            ClientPerformAttack();
        }

        protected virtual void ClientPerformAttack(bool attackTriggered = true)
        {
            _attackTriggered = attackTriggered;
            _delaySw.Restart();
            _clientCooldown.Trigger(BaseCooldown);
            base.ClientSendCmd();
            this._onTriggered?.Invoke();
        }

        private bool HasFlagFast(AttackResult flag)
        {
            return (_syncAttack & flag) == flag;
        }

        private void OnDrawGizmosSelected()
        {
            if (base.Owner == null)
                return;
            
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(OverlapSphereOrigin, _detectionRadius);
        }
    }
}
