using PlayerRoles.PlayableScps.Subroutines;
using PlayerStatsSystem;
using UnityEngine;

namespace PlayerRoles.PlayableScps.Scp3114
{

public class Scp3114DamageProcessor : SubroutineBase, IDamageHandlerProcessingRole
{
	[SerializeField]
	private float _explosionDamageMultiplier = .8f;

	[SerializeField]
	private float _disguisedHumeShieldDamageMultiplier = 1f;

	[SerializeField]
	private float _disguisedBaseHealthDamageMultiplier = 1f;

	private Scp3114Role _scpRole;

	private void DisableHitboxMultipliers(DamageHandlerBase handler)
	{
		if (handler is StandardDamageHandler standardDamageHandler)
		{
			standardDamageHandler.Hitbox = HitboxType.Body;
		}
	}

	private void ApplyExplosionDamageReduction(ExplosionDamageHandler explosionHandler)
	{
		explosionHandler.ApplyDamageMultiplier(_explosionDamageMultiplier);
	}

	private void ApplyDisguiseDamageReduction(FirearmDamageHandler firearmHandler)
	{
		float num = ((_scpRole.HumeShieldModule.HsCurrent > 0f) ? _disguisedHumeShieldDamageMultiplier : _disguisedBaseHealthDamageMultiplier);
		firearmHandler.ApplyDamageMultiplier(num);
	}

	protected override void Awake()
	{
		base.Awake();
		_scpRole = base.Role as Scp3114Role;
	}

	public DamageHandlerBase ProcessDamageHandler(DamageHandlerBase handler)
	{
		DisableHitboxMultipliers(handler);
		if (!(handler is ExplosionDamageHandler explosionHandler))
		{
			if (handler is FirearmDamageHandler firearmHandler && _scpRole.Disguised)
			{
				ApplyDisguiseDamageReduction(firearmHandler);
			}
		}
		else
		{
			ApplyExplosionDamageReduction(explosionHandler);
		}
		return handler;
	}
}


}
