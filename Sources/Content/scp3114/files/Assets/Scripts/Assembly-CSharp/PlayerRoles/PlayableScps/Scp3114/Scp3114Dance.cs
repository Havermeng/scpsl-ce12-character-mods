using System;
using CameraShaking;
using Mirror;
using PlayerRoles.FirstPersonControl;
using PlayerRoles.Spectating;
using PlayerRoles.PlayableScps.Subroutines;
using RelativePositioning;
using UnityEngine;

namespace PlayerRoles.PlayableScps.Scp3114
{

public class Scp3114Dance : ScpStandardSubroutine<Scp3114Role>, IShakeEffect
{
	[SerializeField]
	private int _danceVariants = 3;

	[SerializeField]
	private string _secretCode = "gingerbread";

	[SerializeField]
	private float _cameraAdjustSpeed = 3f;

	[SerializeField]
	private float _cameraMaxDistance = 3f;

	[SerializeField]
	private ActionName[] _cancelKeys = { ActionName.MoveForward, ActionName.MoveBackward, ActionName.MoveLeft, ActionName.MoveRight, ActionName.Jump, ActionName.Shoot, ActionName.Zoom };

	private float _curCameraDis;

	private int _nextMatchIndex;

	private int _codeLength;

	private bool _shakeActive;

	private Transform _tr;

	private Vector3 _lastFwd;

	private double _lastRpcTime;

	private RelativePosition _serverStartPos;

	private const float MaxPositionDiffSqr = 2.5f;

	private const float CameraRadius = 0.16f;

	private const float MinDuration = 0.5f;

	private byte _nextDanceId;

	public bool IsDancing { get; private set; }

	public int DanceVariant { get; private set; }

	public bool ThirdpersonMode
	{
		get
		{
			if (!(_curCameraDis > 0f))
			{
				return IsDancing;
			}
			return true;
		}
	}

	private void Update()
	{
		if (NetworkServer.active)
		{
			UpdateServer();
		}
		UpdateCamera();
		if (!base.Role.IsLocalPlayer)
		{
			return;
		}
		if (IsDancing)
		{
			if (TryEndDancing())
			{
				ClientSendCmd();
			}
		}
		else if (TryStartDancing())
		{
			ClientSendCmd();
		}
	}

	private void UpdateServer()
	{
		if (IsDancing && !((_serverStartPos.Position - base.ScpRole.FpcModule.Position).sqrMagnitude < 2.5f))
		{
			IsDancing = false;
			ServerSendRpc(toAll: true);
		}
	}

	private void SetModelVisibility(bool b)
	{
		base.ScpRole.FpcModule.CharacterModelInstance.SetVisibility(b);
	}

	private void UpdateCamera()
	{
		_lastFwd = _tr.forward;
		if (!IsDancing)
		{
			if (!(_curCameraDis <= 0f))
			{
				_curCameraDis -= Time.deltaTime * _cameraAdjustSpeed;
				if (!(_curCameraDis > 0f))
				{
					SetModelVisibility(!base.Owner.isLocalPlayer && !base.Owner.IsLocallySpectated());
					_curCameraDis = 0f;
				}
			}
		}
		else
		{
			_curCameraDis += Time.deltaTime * _cameraAdjustSpeed;
			float num = _cameraMaxDistance;
			if (Physics.Raycast(base.ScpRole.CameraPosition, -_lastFwd, out var hitInfo, num + 0.16f, FpcStateProcessor.Mask))
			{
				num = hitInfo.distance - 0.16f;
			}
			_curCameraDis = Math.Min(num, _curCameraDis);
		}
	}

	private bool TryStartDancing()
	{
		string inputString = Input.inputString;
		for (int i = 0; i < inputString.Length; i++)
		{
			if (char.ToLowerInvariant(inputString[i]) != _secretCode[_nextMatchIndex])
			{
				_nextMatchIndex = 0;
			}
			else if (++_nextMatchIndex == _codeLength)
			{
				_nextMatchIndex = 0;
				return true;
			}
		}
		return false;
	}

	private bool TryEndDancing()
	{
		if (NetworkTime.time < _lastRpcTime + 0.5)
		{
			return false;
		}
		ActionName[] cancelKeys = _cancelKeys;
		for (int i = 0; i < cancelKeys.Length; i++)
		{
			if (Input.GetKey(NewInput.GetKey(cancelKeys[i])))
			{
				return true;
			}
		}
		return false;
	}

	protected override void Awake()
	{
		base.Awake();
		_tr = base.transform;
		_codeLength = _secretCode.Length;
	}

	public override void SpawnObject()
	{
		base.SpawnObject();
		if (!_shakeActive)
		{
			CameraShakeController.AddEffect(this);
			_shakeActive = true;
		}
	}

	public override void ResetObject()
	{
		base.ResetObject();
		IsDancing = false;
		_nextMatchIndex = 0;
		_lastRpcTime = 0.0;
		_curCameraDis = 0f;
	}

	public override void ClientWriteCmd(NetworkWriter writer)
	{
		base.ClientWriteCmd(writer);
		writer.WriteBool(!IsDancing);
	}

	public override void ServerProcessCmd(NetworkReader reader)
	{
		base.ServerProcessCmd(reader);
		if (reader.ReadBool())
		{
			if (!base.ScpRole.SkeletonIdle)
			{
				return;
			}
			_nextDanceId = (byte)UnityEngine.Random.Range(0, 255);
			IsDancing = true;
			_serverStartPos = new RelativePosition(base.ScpRole.FpcModule.Position);

		}
		else
		{
			IsDancing = false;
		}
		ServerSendRpc(toAll: true);
	}

	public override void ClientProcessRpc(NetworkReader reader)
	{
		base.ClientProcessRpc(reader);
		IsDancing = reader.ReadBool();
		DanceVariant = reader.ReadByte() % Math.Max(1, _danceVariants);
		_lastRpcTime = NetworkTime.time;
		if (IsDancing)
		{
			SetModelVisibility(b: true);
		}
	}

	public override void ServerWriteRpc(NetworkWriter writer)
	{
		base.ServerWriteRpc(writer);
		writer.WriteBool(IsDancing);
		writer.WriteByte(_nextDanceId);
	}

	public bool GetEffect(ReferenceHub ply, out ShakeEffectValues shakeValues)
	{
		shakeValues = ShakeEffectValues.None;
		if (base.Role.Pooled)
		{
			_shakeActive = false;
			return false;
		}
		if (base.Owner.isLocalPlayer || base.Owner.IsLocallySpectated())
		{
			Vector3? rootCameraPositionOffset = _lastFwd * (0f - _curCameraDis);
			shakeValues = new ShakeEffectValues(null, null, rootCameraPositionOffset);
		}
		return true;
	}
}


}
