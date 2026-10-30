using PlayerRoles.FirstPersonControl;
using UnityEngine;
using PlayerRoles.PlayableScps.Subroutines;

namespace PlayerRoles.PlayableScps.Scp3114
{

public class Scp3114MovementModule : FirstPersonMovementModule, IStaminaModifier
{
	private Scp3114Role _scpRole;

	private float _skeletonWalkSpeed;

	private float _skeletonSprintSpeed;

	protected override FpcStateProcessor NewStateProcessor => new FpcStateProcessor(base.Hub, this,
        GameCore.ConfigFile.ServerConfig.GetFloat("stamina_balance_use", .05f),
        GameCore.ConfigFile.ServerConfig.GetFloat("stamina_balance_immunity", 3f),
        new AnimationCurve(new Keyframe(0, 0), new Keyframe(1, 0), new Keyframe(4.11f, .126f)));

	public bool StaminaModifierActive => _scpRole.SkeletonIdle;

	public float StaminaUsageMultiplier => 1.8f;
    public float StaminaRegenMultiplier => 1f;
    public bool SprintingDisabled => false;

	private void Awake()
	{
		_scpRole = GetComponent<Scp3114Role>();
		_skeletonWalkSpeed = WalkSpeed;
		_skeletonSprintSpeed = SprintSpeed;
		_scpRole.CurIdentity.OnStatusChanged += OnStatusChanged;
	}

	private void OnStatusChanged()
	{
		switch (_scpRole.CurIdentity.Status)
		{
		case Scp3114Identity.DisguiseStatus.Active:
		{
			if (_scpRole.CurIdentity.StolenRole.TryGetRoleTemplate<HumanRole>(out var result))
			{
				WalkSpeed = result.FpcModule.WalkSpeed;
				SprintSpeed = result.FpcModule.SprintSpeed;
			}
			break;
		}
		case Scp3114Identity.DisguiseStatus.None:
			WalkSpeed = _skeletonWalkSpeed;
			SprintSpeed = _skeletonSprintSpeed;
			break;
		}
	}
}


}
