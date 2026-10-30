using System;
using System.Text;
using Mirror;
using PlayerRoles.FirstPersonControl;
using PlayerRoles.Ragdolls;
using PlayerRoles.Spectating;
using PlayerRoles.PlayableScps.Subroutines;
using UnityEngine;

namespace PlayerRoles.PlayableScps.Scp3114
{

public class Scp3114Identity : ScpStandardSubroutine<Scp3114Role>, ICustomNicknameDisplayRole
{
	public enum DisguiseStatus
	{
		None,
		Equipping,
		Active
	}

	public class StolenIdentity
	{
		private DisguiseStatus _status;

		public BasicRagdoll Ragdoll;

		public byte UnitNameId;

		public DisguiseStatus Status
		{
			get
			{
				return _status;
			}
			set
			{
				if (_status != value)
				{
					_status = value;
					this.OnStatusChanged?.Invoke();
				}
			}
		}

		public RoleTypeId StolenRole
		{
			get
			{
				if (!(Ragdoll == null))
				{
					return Ragdoll.Info.RoleType;
				}
				return RoleTypeId.None;
			}
		}

		public event Action OnStatusChanged;

		public void Reset()
		{
			_status = DisguiseStatus.None;
			Ragdoll = null;
			UnitNameId = 0;
		}
	}

	public readonly StolenIdentity CurIdentity = new StolenIdentity();

	public readonly AbilityCooldown RemainingDuration = new AbilityCooldown();

	private bool _wasDisguised;

	[SerializeField]
	private AudioSource[] _revealEffectSources = System.Array.Empty<AudioSource>();

	[SerializeField]
	private AudioSource _revealWarningSource;

	[SerializeField]
	private float _warningTimeSeconds = 15f;

	[SerializeField]
	private float _disguiseDurationSeconds = 150f;

	public Color NicknameColor
	{
		get
		{
			if (CurIdentity.Status != DisguiseStatus.Active)
			{
				return base.Role.RoleColor;
			}
			if (!CurIdentity.StolenRole.TryGetRoleTemplate<HumanRole>(out var result))
			{
				return base.Role.RoleColor;
			}
			return result.RoleColor;
		}
	}

	private void OnRagdollRemoved(BasicRagdoll ragdoll)
	{
		if (!(CurIdentity.Ragdoll != ragdoll))
		{
			CurIdentity.Status = DisguiseStatus.None;
            if (NetworkServer.active) ServerResendIdentity();
		}
	}

	private void OnPlayerAdded(ReferenceHub player)
	{
		if (NetworkServer.active)
		{
			ServerSendRpc(player);
		}
	}

	private void Update()
	{
        UpdateWarningAudio();
        if (NetworkServer.active && CurIdentity.Status == DisguiseStatus.Active && RemainingDuration.IsReady)
        {
            CurIdentity.Status = DisguiseStatus.None;
            ServerResendIdentity();
        }
	}

	private void UpdateWarningAudio()
	{
		double num = RemainingDuration.Remaining;
		bool flag = num < (double)_warningTimeSeconds && num > 0.0;
		if (_revealWarningSource.isPlaying)
		{
			if (!flag)
			{
				_revealWarningSource.Stop();
			}
		}
		else
		{
			if (!flag)
			{
				return;
			}
			_revealWarningSource.Play();
		}
		_revealWarningSource.mute = !base.Role.IsLocalPlayer && !base.Owner.IsLocallySpectated();
	}

	private void OnIdentityStatusChanged()
	{
		if (CurIdentity.Status == DisguiseStatus.Active)
		{
			_wasDisguised = true;
			if (NetworkServer.active)
				RemainingDuration.Trigger(_disguiseDurationSeconds);
			return;
		}
		RemainingDuration.Clear();
		if (_wasDisguised)
		{
			_wasDisguised = false;
			_revealEffectSources.ForEach(delegate(AudioSource x)
			{
				x.Play();
			});
		}
	}

	protected override void Awake()
	{
		base.Awake();
		CurIdentity.OnStatusChanged += OnIdentityStatusChanged;
	}

	public override void SpawnObject()
	{
		base.SpawnObject();
		ReferenceHub.OnPlayerAdded += OnPlayerAdded;
		RagdollManager.OnRagdollRemoved += OnRagdollRemoved;
	}

	public override void ResetObject()
	{
		base.ResetObject();
		_wasDisguised = false;
		RemainingDuration.Clear();
		CurIdentity.Reset();
		ReferenceHub.OnPlayerAdded -= OnPlayerAdded;
		RagdollManager.OnRagdollRemoved -= OnRagdollRemoved;
	}

	public void WriteNickname(StringBuilder sb)
	{
        if (CurIdentity.Status == DisguiseStatus.Active && CurIdentity.Ragdoll != null)
            sb.Append(CurIdentity.Ragdoll.Info.Nickname);
        else if (Owner != null)
            sb.Append(Owner.nicknameSync.DisplayName);
	}

	public void ServerResendIdentity()
	{
		ServerSendRpc(toAll: true);
	}

	public override void ServerWriteRpc(NetworkWriter writer)
	{
		base.ServerWriteRpc(writer);
		RemainingDuration.WriteCooldown(writer);
		writer.WriteNetworkBehaviour(CurIdentity.Ragdoll);
		writer.WriteByte(CurIdentity.UnitNameId);
		writer.WriteByte((byte)CurIdentity.Status);
	}

	public override void ClientProcessRpc(NetworkReader reader)
	{
		base.ClientProcessRpc(reader);
		if (!NetworkServer.active)
		{
			RemainingDuration.ReadCooldown(reader);
			CurIdentity.Ragdoll = reader.ReadNetworkBehaviour<BasicRagdoll>();
			CurIdentity.UnitNameId = reader.ReadByte();
			CurIdentity.Status = (DisguiseStatus)reader.ReadByte();
		}
	}
}


}
