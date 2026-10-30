using System;
using System.Collections.Generic;
using Mirror;
using PlayerRoles.PlayableScps.Scp049;
using PlayerRoles.Ragdolls;
using PlayerRoles.PlayableScps.Subroutines;
using UnityEngine;

namespace PlayerRoles.PlayableScps.Scp3114
{

public class Scp3114Disguise : RagdollAbilityBase<Scp3114Role>
{
	// Presentation uses the same range, linked-body distance and eligibility as
	// the client request. This query never begins or completes an ability.
	public bool TryGetAvailableCorpse(out BasicRagdoll corpse)
	{
		corpse = null;
		if (!ScpRole.SkeletonIdle || !Cooldown.IsReady || IsInProgress) return false;
		Transform camera = Owner.PlayerCameraReference;
		if (!Physics.Raycast(camera.position, camera.forward, out var hit, RangeSqr)) return false;
		var body = hit.transform.GetComponentInParent<DynamicRagdoll>();
		if (body == null || !AnyValidateBegin(body, out _)) return false;
		foreach (Rigidbody bone in body.LinkedRigidbodies)
			if (bone != null && (bone.position - ScpRole.FpcModule.Position).sqrMagnitude < RangeSqr)
			{
				corpse = body;
				return true;
			}
		return false;
	}

	public readonly AbilityCooldown Cooldown = new AbilityCooldown();

	private readonly Dictionary<BasicRagdoll, byte> _prevUnitIds = new Dictionary<BasicRagdoll, byte>();

	[SerializeField]
	private AudioSource _equipSkinSound;

	protected override float RangeSqr => 3.5f;

	protected override float Duration => 5f;

	public event Action<Scp3114HudTranslation> OnClientError;

	public override void SpawnObject()
	{
		base.SpawnObject();
		RagdollManager.ServerOnRagdollCreated += ServerOnRagdollCreated;
	}

	public override void ResetObject()
	{
		base.ResetObject();
		Cooldown.Clear();
		RagdollManager.ServerOnRagdollCreated -= ServerOnRagdollCreated;
	}

	public override void ClientProcessRpc(NetworkReader reader)
	{
		base.ClientProcessRpc(reader);
		Cooldown.ReadCooldown(reader);
	}

	public override void ServerWriteRpc(NetworkWriter writer)
	{
		base.ServerWriteRpc(writer);
		Cooldown.WriteCooldown(writer);
	}

	protected override void OnKeyDown()
	{
		base.OnKeyDown();
		if (Cooldown.IsReady)
		{
			ClientTryStart();
		}
	}

	protected override void OnKeyUp()
	{
		base.OnKeyUp();
		ClientTryCancel();
	}

	protected override void OnProgressSet()
	{
		base.OnProgressSet();
		Scp3114Identity.StolenIdentity curIdentity = base.ScpRole.CurIdentity;
		if (base.IsInProgress)
		{
			_equipSkinSound.Play();
			curIdentity.Ragdoll = base.CurRagdoll;
			curIdentity.UnitNameId = (byte)(_prevUnitIds.TryGetValue(base.CurRagdoll, out var value) ? value : 0);
			curIdentity.Status = Scp3114Identity.DisguiseStatus.Equipping;
		}
		else if (curIdentity.Status == Scp3114Identity.DisguiseStatus.Equipping)
		{
			_equipSkinSound.Stop();
			curIdentity.Status = Scp3114Identity.DisguiseStatus.None;
			if (NetworkServer.active)
			{
				Cooldown.Trigger(Duration);
				ServerSendRpc(toAll: true);
			}
		}
	}

	protected override void ServerComplete()
	{
		if (Cooldown.IsReady && AnyValidateBegin(CurRagdoll, out _))
		{
			base.ScpRole.Disguised = true;

			if (!(base.CurRagdoll == null) && base.CurRagdoll is DynamicRagdoll ragdoll)
			{
				Scp3114RagdollToBonesConverter.ServerConvertNew(base.ScpRole, ragdoll);
			}
		}
	}

	protected override byte ServerValidateBegin(BasicRagdoll ragdoll)
	{
        Scp3114HudTranslation error = Scp3114HudTranslation.RagdollErrorAlreadyDisguised;
        if (!Cooldown.IsReady || !AnyValidateBegin(ragdoll, out error))
            return (byte)(error == Scp3114HudTranslation.IdentityStolenWarning ? Scp3114HudTranslation.RagdollErrorAlreadyDisguised : error);
        return 0;
	}

	protected override bool ClientValidateBegin(BasicRagdoll raycastedRagdoll)
	{
		if (AnyValidateBegin(raycastedRagdoll, out var error))
		{
			return base.ClientValidateBegin(raycastedRagdoll);
		}
		this.OnClientError?.Invoke(error);
		return false;
	}

	protected override bool ServerValidateAny()
    {
        return CurRagdoll != null && base.ServerValidateAny() && AnyValidateBegin(CurRagdoll, out _);
    }

    private bool AnyValidateBegin(BasicRagdoll rg, out Scp3114HudTranslation error)
    {
        if (rg == null) { error = Scp3114HudTranslation.RagdollErrorNotHuman; return false; }
        foreach (ReferenceHub hub in ReferenceHub.AllHubs)
        {
            if (hub == Owner || !(hub.roleManager.CurrentRole is Scp3114Role other)) continue;
            if (other.CurIdentity.Ragdoll == rg && other.CurIdentity.Status == Scp3114Identity.DisguiseStatus.Equipping)
            { error = Scp3114HudTranslation.RagdollErrorPreviouslyUsed; return false; }
        }
		if (!rg.Info.RoleType.TryGetRoleTemplate<HumanRole>(out var _))
		{
			error = Scp3114HudTranslation.RagdollErrorNotHuman;
			return false;
		}
		if (rg.Info.Handler is Scp3114DamageHandler { Subtype: Scp3114DamageHandler.HandlerType.SkinSteal })
		{
			error = Scp3114HudTranslation.RagdollErrorPreviouslyUsed;
			return false;
		}
		if (base.ScpRole.Disguised)
		{
			error = Scp3114HudTranslation.RagdollErrorAlreadyDisguised;
			return false;
		}
		error = Scp3114HudTranslation.IdentityStolenWarning;
		return true;
	}

	private void ServerOnRagdollCreated(ReferenceHub owner, BasicRagdoll ragdoll)
	{
		if (!(owner == null) && owner.roleManager.CurrentRole is HumanRole humanRole)
		{
			_prevUnitIds[ragdoll] = humanRole.UnitNameId;
		}
	}
}


}
