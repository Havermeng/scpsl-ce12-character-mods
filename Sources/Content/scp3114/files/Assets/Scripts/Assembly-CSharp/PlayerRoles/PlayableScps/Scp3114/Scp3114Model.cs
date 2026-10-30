using System;
using PlayerRoles.FirstPersonControl;
using PlayerRoles.FirstPersonControl.Thirdperson;
using PlayerRoles.FirstPersonControl.Thirdperson.Subcontrollers;
using UnityEngine;

namespace PlayerRoles.PlayableScps.Scp3114
{

public class Scp3114Model : AnimatedCharacterModel, ISubcontrollerRpcRedirector
{
	[Serializable]
	public struct HumanoidBone
	{
		public HumanBodyBones BoneType;

		public Transform Transform;
	}

	private static readonly int HashGrounded = Animator.StringToHash("Grounded");

	private static readonly int HashStrangling = Animator.StringToHash("Strangling");

	private static readonly int HashDanceVariant = Animator.StringToHash("DanceVariant");

	private static readonly int HashSlapTrigger = Animator.StringToHash("SlapTrigger");

	private static readonly int HashSlapMirror = Animator.StringToHash("MirrorSlap");

	private static readonly int HashStealing = Animator.StringToHash("Stealing");

	private static readonly int HashReveal = Animator.StringToHash("Reveal");

	private Scp3114Strangle _strangle;

	private Scp3114Slap _slap;

	private Scp3114Dance _dance;

	private Scp3114FakeModelManager _fakeModel;

	private bool _hadIdentity;

	[SerializeField]
	private int _strangleLayer = -1;

	[SerializeField]
	private int _danceLayer = -1;

	[SerializeField]
	private float _layerWeightAdjustSpeed = 8f;

	[SerializeField]
	private ParticleSystem _revealParticles;

	[SerializeField]
	private GameObject _skeletonFormItemRoot;

	[field: SerializeField]
	public HumanoidBone[] HumanoidBones { get; private set; } = System.Array.Empty<HumanoidBone>();

	protected override bool FootstepPlayable
	{
		get
		{
			if (base.FootstepPlayable)
			{
				return ScpRole.SkeletonIdle;
			}
			return false;
		}
	}

	protected override bool LandingFootstepPlayable
	{
		get
		{
			if (base.LandingFootstepPlayable)
			{
				return ScpRole.SkeletonIdle;
			}
			return false;
		}
	}

	public AnimatedCharacterModel RpcTarget => _fakeModel.RpcTarget;

	private Scp3114Role ScpRole => base.Role as Scp3114Role;

	public override void SpawnObject()
	{
		base.SpawnObject();
		ScpRole.SubroutineModule.TryGetSubroutine<Scp3114FakeModelManager>(out _fakeModel);
		ScpRole.SubroutineModule.TryGetSubroutine<Scp3114Strangle>(out _strangle);
		ScpRole.SubroutineModule.TryGetSubroutine<Scp3114Dance>(out _dance);
		ScpRole.SubroutineModule.TryGetSubroutine<Scp3114Slap>(out _slap);
		_slap.OnTriggered += OnSlapTriggered;
		ScpRole.CurIdentity.OnStatusChanged += OnIdentityChanged;
	}

	public void OnPlayerMove()
	{
		_fakeModel.OnPlayerMove();
	}

	public override void ResetObject()
	{
		base.ResetObject();
		_hadIdentity = false;
		_slap.OnTriggered -= OnSlapTriggered;
		ScpRole.CurIdentity.OnStatusChanged -= OnIdentityChanged;
	}

	protected override void Update()
	{
		base.Update();
		if (!base.Pooled)
		{
			base.Animator.SetBool(HashGrounded, ScpRole.FpcModule.IsGrounded || ScpRole.ActiveTime < .3f);
            bool hasValue = _strangle.SyncTarget.HasValue;
			base.Animator.SetBool(HashStrangling, hasValue);
            ResolveLayers();
			base.Animator.SetFloat(HashDanceVariant, _dance.DanceVariant);
			base.Animator.SetBool(HashStealing, ScpRole.CurIdentity.Status == Scp3114Identity.DisguiseStatus.Equipping);
			AdjustWeight(_strangleLayer, hasValue);
			AdjustWeight(_danceLayer, _dance.IsDancing);
		}
	}

    private void ResolveLayers()
    {
        if (_strangleLayer >= 0 && _danceLayer >= 0) return;
        for (int i = 0; i < Animator.layerCount; i++)
        {
            string layer = Animator.GetLayerName(i);
            if (layer.IndexOf("strang", StringComparison.OrdinalIgnoreCase) >= 0) _strangleLayer = i;
            if (layer.IndexOf("dance", StringComparison.OrdinalIgnoreCase) >= 0) _danceLayer = i;
        }
    }

	private void AdjustWeight(int layerId, bool isActive)
	{
		int layerIndex = layerId;
        if (layerIndex < 0 || layerIndex >= Animator.layerCount) return;
		float layerWeight = base.Animator.GetLayerWeight(layerIndex);
		float num = Time.deltaTime * _layerWeightAdjustSpeed;
		float value = (isActive ? (layerWeight + num) : (layerWeight - num));
		base.Animator.SetLayerWeight(layerIndex, Mathf.Clamp01(value));
	}

	private void OnIdentityChanged()
	{
		if (_skeletonFormItemRoot != null) _skeletonFormItemRoot.SetActive(ScpRole.SkeletonIdle);
		switch (ScpRole.CurIdentity.Status)
		{
		case Scp3114Identity.DisguiseStatus.Active:
			_hadIdentity = true;
			break;
		case Scp3114Identity.DisguiseStatus.None:
			if (_hadIdentity)
			{
				_hadIdentity = false;
				if (_revealParticles != null) _revealParticles.Play(withChildren: true);
				base.Animator.SetTrigger(HashReveal);
			}
			break;
		}
	}

	private void OnSlapTriggered()
	{
		base.Animator.SetBool(HashSlapMirror, !base.Animator.GetBool(HashSlapMirror));
		base.Animator.SetTrigger(HashSlapTrigger);
	}
}


}
