using PlayerRoles.PlayableScps.HumeShield;
using UnityEngine;

namespace PlayerRoles.PlayableScps.Scp3114
{

public class Scp3114HumeShieldController : DynamicHumeShieldController
{
	public override void SpawnObject()
    {
        RegenerationRate = 1f;
        RegenerationCooldown = 10f;
        base.SpawnObject();
    }

    public override float HsRegeneration => base.HsRegeneration > 0f ? HsMax * .05f : 0f;

    public override AudioClip ShieldBreakSound
	{
		get
		{
			if (!(base.Role as Scp3114Role).Disguised)
			{
				return base.ShieldBreakSound;
			}
			return null;
		}
	}
}


}
