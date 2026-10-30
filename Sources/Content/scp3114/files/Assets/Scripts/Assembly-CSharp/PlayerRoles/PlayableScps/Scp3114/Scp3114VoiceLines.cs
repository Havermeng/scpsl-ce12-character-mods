using System;
using System.Collections.Generic;
using MapGeneration;
using Mirror;
using PlayerRoles.PlayableScps.Subroutines;
using UnityEngine;

namespace PlayerRoles.PlayableScps.Scp3114
{

public class Scp3114VoiceLines : ScpStandardSubroutine<Scp3114Role>
{
	[Serializable]
	private class VoiceLinesDefinition
	{
		public VoiceLinesName Label;

		public AudioClip[] RandomClips = Array.Empty<AudioClip>();

		public float MinIdleTime;

		public float MaxDuration;

		[Range(0f, 1f)]
		public float Chance;

		private double _lastUse;

        private float _lastClipDuration;

		private List<int> _order;

		private int _lastIndex;

		public float LastUseElapsedSeconds => (float)(NetworkTime.time - _lastUse - _lastClipDuration);

		public bool TryDrawNext(out ushort clipId)
		{
			if (UnityEngine.Random.value > Chance)
			{
				clipId = 0;
				return false;
			}
			_lastUse = NetworkTime.time;
			_lastIndex++;
			clipId = (ushort)_lastIndex;
            AudioClip playedClip = GetClip(clipId);
            _lastClipDuration = playedClip != null ? (MaxDuration > 0f ? Mathf.Min(playedClip.length, MaxDuration) : playedClip.length) : Mathf.Max(0f, MaxDuration);
			return true;
		}

		public AudioClip GetClip(int index)
		{
			return RandomClips == null || RandomClips.Length == 0 ? null : RandomClips[_order[index % _order.Count]];
		}

		public void Init()
		{
			_lastUse = 0.0;
			if (_order == null)
			{
				_order = new List<int>(RandomClips.Length);
			}
			_order.Add(0);
		}

		public void Randomize(int seed)
		{
			_order.Clear();
			for (int i = 0; i < RandomClips.Length; i++)
			{
				_order.Add(i);
			}
			_order.ShuffleList(new System.Random(seed));
		}
	}

	private enum VoiceLinesName
	{
		KillSlap,
		KillStrangle,
		Slap,
		RandomIdle,
		Reveal,
		EquipStart,
		StartStrangle
	}

	private static float VoiceVolume => Mathf.Clamp01(PlayerPrefsSl.Get("Scp3114VoiceVolume", 1f));

	[SerializeField]
	private VoiceLinesDefinition[] _voiceLines = Array.Empty<VoiceLinesDefinition>();

	[SerializeField]
	private AudioSource _source;

	[SerializeField]
	private float _idleCycleTime = 18f;

	private float _idleRemaining;

	private byte _syncName;

	private ushort _syncId;

	private bool _hasDisguise;

	private bool _randomized;

	protected override void Awake()
	{
		base.Awake();
		_voiceLines.ForEach(delegate(VoiceLinesDefinition x)
		{
			x.Init();
		});
		base.ScpRole.CurIdentity.OnStatusChanged += OnStatusChanged;
		GetSubroutine<Scp3114Slap>(out var sr);
		sr.ServerOnHit += delegate
		{
			ServerPlayConditionally(VoiceLinesName.Slap);
		};
		sr.ServerOnKill += delegate
		{
			ServerPlayConditionally(VoiceLinesName.KillSlap);
		};
		GetSubroutine<Scp3114Strangle>(out var sr2);
		sr2.ServerOnBegin += delegate
		{
			ServerPlayConditionally(VoiceLinesName.StartStrangle);
		};
		sr2.ServerOnKill += delegate
		{
			ServerPlayConditionally(VoiceLinesName.KillStrangle);
		};
	}

	private void OnStatusChanged()
	{
		switch (base.ScpRole.CurIdentity.Status)
		{
		case Scp3114Identity.DisguiseStatus.Active:
			_hasDisguise = true;
			_idleRemaining = _idleCycleTime;
			break;
		case Scp3114Identity.DisguiseStatus.Equipping:
			ServerPlayConditionally(VoiceLinesName.EquipStart);
			break;
		case Scp3114Identity.DisguiseStatus.None:
			if (_hasDisguise)
			{
				_hasDisguise = false;
				ServerPlayConditionally(VoiceLinesName.Reveal);
			}
			break;
		}
	}

	private void Update()
	{
		if (!_randomized)
		{
			RandomizeWhenReady();
		}
		if (NetworkServer.active && !_hasDisguise)
		{
			_idleRemaining -= Time.deltaTime;
			if (!(_idleRemaining > 0f))
			{
				_idleRemaining = _idleCycleTime;
				ServerPlayConditionally(VoiceLinesName.RandomIdle);
			}
		}
	}

	private void RandomizeWhenReady()
	{
		if (SeedSynchronizer.MapGenerated)
		{
			int seed = SeedSynchronizer.Seed + (int)base.Owner.netId;
			_voiceLines.ForEach(delegate(VoiceLinesDefinition x)
			{
				x.Randomize(seed);
			});
			_randomized = true;
		}
	}

	private void ServerPlayConditionally(VoiceLinesName lineToPlay)
	{
		if (!NetworkServer.active)
		{
			return;
		}
		VoiceLinesDefinition voiceLinesDefinition = null;
		float num = float.PositiveInfinity;
		VoiceLinesDefinition[] voiceLines = _voiceLines;
		foreach (VoiceLinesDefinition voiceLinesDefinition2 in voiceLines)
		{
			num = Mathf.Min(num, voiceLinesDefinition2.LastUseElapsedSeconds);
			if (voiceLinesDefinition2.Label == lineToPlay)
			{
				voiceLinesDefinition = voiceLinesDefinition2;
			}
		}
		bool killPriority = lineToPlay == VoiceLinesName.KillSlap || lineToPlay == VoiceLinesName.KillStrangle;
        if (voiceLinesDefinition != null && (killPriority || !(voiceLinesDefinition.MinIdleTime > num)) && voiceLinesDefinition.TryDrawNext(out var clipId))
		{
			_syncName = (byte)lineToPlay;
			_syncId = clipId;
			ServerSendRpc(toAll: true);
		}
	}

	public override void ServerWriteRpc(NetworkWriter writer)
	{
		base.ServerWriteRpc(writer);
		writer.WriteByte(_syncName);
		writer.WriteUShort(_syncId);
	}

	public override void ClientProcessRpc(NetworkReader reader)
	{
		base.ClientProcessRpc(reader);
		_syncName = reader.ReadByte();
		_syncId = reader.ReadUShort();
		VoiceLinesDefinition[] voiceLines = _voiceLines;
		foreach (VoiceLinesDefinition voiceLinesDefinition in voiceLines)
		{
			if ((byte)voiceLinesDefinition.Label == _syncName)
			{
				if (_source.isPlaying)
				{
					_source.Stop();
				}
				_source.PlayOneShot(voiceLinesDefinition.GetClip(_syncId), VoiceVolume);
				break;
			}
		}
	}

	public override void SpawnObject()
	{
		base.SpawnObject();
		_randomized = false;
		if (NetworkServer.active)
		{
			_hasDisguise = false;
			_idleRemaining = _idleCycleTime;
		}
	}
}


}
