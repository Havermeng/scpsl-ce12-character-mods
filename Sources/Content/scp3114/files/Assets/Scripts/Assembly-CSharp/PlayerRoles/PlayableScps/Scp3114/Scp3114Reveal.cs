using System;
using Mirror;
using PlayerRoles.PlayableScps.Subroutines;
using UnityEngine;

namespace PlayerRoles.PlayableScps.Scp3114
{

public class Scp3114Reveal : ScpKeySubroutine<Scp3114Role>
{
	private const float HoldDuration = 0.65f;

	public const ActionName RevealKey = ActionName.Reload;

	private float _holdTimer;

	protected override ActionName TargetKey => ActionName.Reload;

	protected override bool KeyPressable
	{
		get
		{
			if (base.KeyPressable && base.ScpRole.Disguised)
			{
				return _holdTimer < 0.65f;
			}
			return false;
		}
	}

	public static event Action OnRevealFail;

	protected override void Update()
	{
		base.Update();
		if (base.Role.IsLocalPlayer || (NetworkServer.active && Owner != null && Scp3114Compatibility.IsDummy(Owner)))
		{
			if (IsKeyHeld && base.ScpRole.Disguised)
			{
				_holdTimer += Time.deltaTime;
			}
			else
			{
				_holdTimer = 0f;
			}
		}
	}

	protected override void OnKeyUp()
	{
		base.OnKeyUp();
		if (base.ScpRole.Disguised)
		{
			if (_holdTimer >= 0.65f)
			{
				ClientSendCmd();
			}
			else
			{
				Scp3114Reveal.OnRevealFail?.Invoke();
			}
		}
	}

	public override void ServerProcessCmd(NetworkReader reader)
	{
        base.ServerProcessCmd(reader);
        if (base.ScpRole.Disguised)
            base.ScpRole.Disguised = false;
	}
}


}
