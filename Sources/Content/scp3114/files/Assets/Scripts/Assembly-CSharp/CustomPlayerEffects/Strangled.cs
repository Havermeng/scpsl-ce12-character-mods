using CursorManagement;
using InventorySystem.Items;
using Mirror;
using PlayerRoles.FirstPersonControl;
using PlayerRoles.PlayableScps.Scp3114;
using PlayerStatsSystem;
using UnityEngine;

namespace CustomPlayerEffects
{

public class Strangled : StatusEffectBase, ISoundtrackMutingEffect, IMovementInputOverride, IStaminaModifier, IMovementSpeedModifier, ICursorOverride, IInteractionBlocker
{
	private bool _hasCache;

	private bool _overrideRegistered;

	private double _startTime;

	private float _remainingLineOfSightTolerance;

	private ReferenceHub _cachedHub;

	private Scp3114Strangle.StrangleTarget _strangleTarget;

	private const float MaxMovementSpeed = 5f;

	private const float LookLerp = 20f;

	private const float StartDamageRate = 5f;

	private const float DamageRatePerSec = 5f;

	private const float LineOfSightToleranceSeconds = 0.5f;

	public bool MuteSoundtrack => base.IsEnabled;

	public bool MovementOverrideActive => base.IsEnabled;

	public Vector3 MovementOverrideDirection { get; private set; }

	public bool StaminaModifierActive => base.IsEnabled;

	public bool SprintingDisabled => base.IsEnabled;
    public float StaminaUsageMultiplier => 1f;
    public float StaminaRegenMultiplier => 1f;

	public bool MovementModifierActive => base.IsEnabled;

	public float MovementSpeedMultiplier => 1f;

	public float MovementSpeedLimit { get; private set; }

	public CursorOverrideMode CursorOverride => CursorOverrideMode.NoOverride;

	public bool LockMovement => base.IsEnabled;

	public override bool AllowEnabling => !SpawnProtected.CheckPlayer(base.Hub);

	private float DamageRate
	{
		get
		{
			double elapsedSeconds = ElapsedSeconds;
			if (elapsedSeconds < 0.0)
			{
				return 0f;
			}
			double num = 5.0 * elapsedSeconds;
			return (float)(5.0 + num);
		}
	}

	public float EstimatedTimeToKill
	{
		get
		{
			HealthStat module = base.Hub.playerStats.GetModule<HealthStat>();
			AhpStat module2 = base.Hub.playerStats.GetModule<AhpStat>();
			float num = module.CurValue + module2.CurValue;
			float damageRate = DamageRate;
			float num2 = damageRate * damageRate;
			return (0f - damageRate + Mathf.Sqrt(num2 + 10f * num)) / 5f;
		}
	}

	public double ElapsedSeconds => NetworkTime.time - _startTime;

	public BlockedInteraction BlockedInteractions => BlockedInteraction.ItemUsage | BlockedInteraction.OpenInventory;

	public bool CanBeCleared => !base.IsEnabled;

	protected override void Enabled()
	{
		base.Enabled();
		_startTime = NetworkTime.time;
		_remainingLineOfSightTolerance = 0.5f;
		MovementSpeedLimit = 0f;
		MovementOverrideDirection = Vector3.zero;
		base.Hub.interCoordinator.AddBlocker(this);
        if (IsLocalPlayer) CursorManager.Register(this);
		if (base.Hub.roleManager.CurrentRole is IFpcRole fpcRole)
		{
			fpcRole.FpcModule.Motor.RegisterMovementLock(this);
			_overrideRegistered = true;
		}
	}

	protected override void Disabled()
	{
		base.Disabled();
        Hub.interCoordinator.RemoveBlocker(this);
        CursorManager.Unregister(this);
        _hasCache = false;
		if (base.Hub.roleManager.CurrentRole is IFpcRole fpcRole && _overrideRegistered)
		{
			fpcRole.FpcModule.Motor.UnregisterMovementLock(this);
            _overrideRegistered = false;
		}
	}

	protected override void OnEffectUpdate()
	{
		base.OnEffectUpdate();
		if (base.IsLocalPlayer || NetworkServer.active)
		{
			UpdateInputOverride();
		}
		if (NetworkServer.active && !ServerUpdate())
		{
			DisableEffect();
		}
	}

	private bool ServerUpdate()
	{
		if (!TryUpdateAttacker(out var attacker))
		{
			return false;
		}
		Vector3 position = base.Hub.PlayerCameraReference.position;
		Vector3 position2 = attacker.PlayerCameraReference.position;
		if (!Scp3114Compatibility.IsLineOfSightBlocked(position, position2, FpcStateProcessor.Mask))
		{
			_remainingLineOfSightTolerance = 0.5f;
		}
		else
		{
			_remainingLineOfSightTolerance -= Time.deltaTime;
			if (_remainingLineOfSightTolerance < 0f)
			{
				return false;
			}
		}
		base.Hub.inventory.ServerSelectItem(0);
		return base.Hub.playerStats.DealDamage(new Scp3114DamageHandler(attacker, DamageRate * Time.deltaTime, Scp3114DamageHandler.HandlerType.Strangulation));
	}

	private void UpdateInputOverride()
	{
		if (TryUpdateAttacker(out var attacker) && base.Hub.roleManager.CurrentRole is IFpcRole { FpcModule: var fpcModule } fpcRole)
		{
			// Old FPC positions are capsule centres; keep the victim's view
            // on the attacker at eye height instead of forcing it at the pelvis.
            if (base.IsLocalPlayer)
            {
                Vector3 lookPoint = attacker.PlayerCameraReference.position;
                // The native grab lowers the animated skull below the logical
                // standing camera. Follow the visible head, so it remains in view.
                if (attacker.roleManager.CurrentRole is Scp3114Role scp &&
                    scp.FpcModule.CharacterModelInstance is Scp3114Model model)
                    foreach (var bone in model.HumanoidBones)
                        if (bone.BoneType == HumanBodyBones.Head && bone.Transform != null)
                        { lookPoint = bone.Transform.position; break; }
                fpcRole.LookAtPoint(lookPoint, Time.deltaTime * 20f);
            }
			Vector3 vector = _strangleTarget.TargetPosition.Position - fpcModule.Position;
			float num = vector.MagnitudeIgnoreY();
			if (num > .001f)
			{
				MovementOverrideDirection = vector / num;
				MovementSpeedLimit = Mathf.Min(5f, num / Time.deltaTime);
			}
            else
            {
                MovementOverrideDirection = Vector3.zero;
                MovementSpeedLimit = 0f;
            }
		}
	}

	public bool TryUpdateAttacker(out ReferenceHub attacker)
	{
		if (_hasCache && _cachedHub != null && CheckPlayer(_cachedHub))
		{
			attacker = _cachedHub;
			return true;
		}
		foreach (ReferenceHub allHub in ReferenceHub.AllHubs)
		{
			if (CheckPlayer(allHub))
			{
				attacker = allHub;
				_hasCache = true;
				return true;
			}
		}
		_hasCache = false;
		attacker = null;
		return false;
		bool CheckPlayer(ReferenceHub hub)
		{
			if (!(hub.roleManager.CurrentRole is Scp3114Role scp3114Role))
			{
				return false;
			}
			if (!scp3114Role.SubroutineModule.TryGetSubroutine<Scp3114Strangle>(out var subroutine))
			{
				return false;
			}
			if (!subroutine.SyncTarget.HasValue)
			{
				return false;
			}
			if (subroutine.SyncTarget.Value.Target != base.Hub)
			{
				return false;
			}
			_cachedHub = hub;
			_strangleTarget = subroutine.SyncTarget.Value;
			return true;
		}
	}

	private void OnDestroy()
	{
		if (base.Hub.roleManager.CurrentRole is IFpcRole fpcRole && _overrideRegistered)
		{
			fpcRole.FpcModule.Motor.UnregisterMovementLock(this);
            _overrideRegistered = false;
		}
	}
}

}
