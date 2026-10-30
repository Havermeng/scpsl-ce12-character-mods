using System;
using AudioPooling;
using PlayerRoles.FirstPersonControl;
using PlayerRoles.PlayableScps.Subroutines;
using PlayerStatsSystem;
using UnityEngine;

namespace PlayerRoles.PlayableScps.Scp3114
{

public class Scp3114Slap : Scp3114AttackAbilityBase<Scp3114Role>
{
	private const float HitHumeShieldReward = 175f;

	[SerializeField]
	private AudioClip[] _swingClips = System.Array.Empty<AudioClip>();

	[SerializeField]
	private float _swingSoundRange = 12f;

	private HumeShieldStat _humeShield;

	private Scp3114Strangle _strangle;

	public override float DamageAmount => 20f;

	protected override float AttackDelay => 0.3f;

	protected override float BaseCooldown => 0.5f;

	protected override bool CanTriggerAbility
	{
		get
		{
			if (base.CanTriggerAbility)
			{
				return !AbilityBlocked;
			}
			return false;
		}
	}

	protected override bool ServerAbilityAllowed => !AbilityBlocked;

	private bool AbilityBlocked
	{
		get
		{
			if (!_strangle.SyncTarget.HasValue)
			{
				return base.ScpRole.CurIdentity.Status != Scp3114Identity.DisguiseStatus.None;
			}
			return true;
		}
	}

	public event Action ServerOnHit;

	public event Action ServerOnKill;

	private void PlaySwingSound()
	{
		AudioSourcePoolManager.PlaySound(_swingClips.RandomItem(), base.transform, _swingSoundRange, 1f, FalloffType.Exponential, AudioMixerChannelType.NoDucking);
	}

    protected override DamageHandlerBase DamageHandler => new Scp3114DamageHandler(Owner, DamageAmount, Scp3114DamageHandler.HandlerType.Slap);

	protected override void Awake()
	{
		base.Awake();
		base.OnTriggered += PlaySwingSound;
	}

	public override void SpawnObject()
	{
		base.SpawnObject();
		GetSubroutine<Scp3114Strangle>(out _strangle);
		_humeShield = base.Owner.playerStats.GetModule<HumeShieldStat>();
	}

    protected override void OnDestructibleDamaged(IDestructible destructible)
    {
        if (!(destructible is HitboxIdentity hitbox)) return;
        if (hitbox.TargetHub.playerStats.GetModule<HealthStat>().CurValue <= 0)
        {
            ServerOnKill?.Invoke();
            _humeShield.CurValue = Mathf.Min(_humeShield.CurValue + HitHumeShieldReward, _humeShield.MaxValue);
        }
        ServerOnHit?.Invoke();
    }
}
}
