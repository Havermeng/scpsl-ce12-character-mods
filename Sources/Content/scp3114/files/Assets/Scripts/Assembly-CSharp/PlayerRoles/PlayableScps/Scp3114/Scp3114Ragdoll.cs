using System.Collections.Generic;
using System.Runtime.InteropServices;
using Mirror;
using NorthwoodLib.Pools;
using PlayerRoles.Ragdolls;
using PlayerStatsSystem;
using UnityEngine;

namespace PlayerRoles.PlayableScps.Scp3114
{

public class Scp3114Ragdoll : DynamicRagdoll
{
	[SyncVar]
	private RoleTypeId _disguiseRole;

	[SerializeField]
	private float _revealDelay = .5f;

	[SerializeField]
	private float _revealDuration = 1.5f;

	[SerializeField]
	private Renderer _ownRenderer;

	private bool _playingAnimation;

	private float _revealElapsed;

	private Transform[] _trackedBones;
    private Transform[] _sourceBones;

	private Material[] _humanMaterials;

	private Transform _humanRagdollRoot;

	private static readonly int ProgressHash = Shader.PropertyToID("_Progress");

	private static readonly int FadeHash = Shader.PropertyToID("_Fade");

	protected override void Start()
	{
		base.Start();
		if (Info.Handler is DisruptorDamageHandler || !_disguiseRole.TryGetRoleTemplate<HumanRole>(out var result) || !(result.Ragdoll.ServerInstantiateSelf(Info.OwnerHub, _disguiseRole) is DynamicRagdoll dynamicRagdoll))
		{
			return;
		}
		dynamicRagdoll.Info = Info;
        Scp3114RagdollToBonesConverter.EnsureLinkedTransforms(dynamicRagdoll);
        Scp3114RagdollToBonesConverter.EnsureLinkedTransforms(this);
        _ownRenderer.sharedMaterial = new Material(_ownRenderer.sharedMaterial);
        _ownRenderer.sharedMaterial.SetFloat(FadeHash, 0);
        List<Transform> tracked = new List<Transform>();
        List<Transform> sources = new List<Transform>();
        foreach (Transform humanBone in dynamicRagdoll.LinkedRigidbodiesTransforms)
        {
            string targetKey = Scp3114RagdollToBonesConverter.BoneKey(humanBone.name);
            foreach (Transform skeletonBone in LinkedRigidbodiesTransforms)
            {
                if (Scp3114RagdollToBonesConverter.BoneKey(skeletonBone.name) != targetKey) continue;
                tracked.Add(humanBone); sources.Add(skeletonBone); break;
            }
        }
        _trackedBones = tracked.ToArray();
        _sourceBones = sources.ToArray();
		_playingAnimation = true;
		_humanRagdollRoot = dynamicRagdoll.transform;
		_humanRagdollRoot.SetParent(transform, false);
        _humanRagdollRoot.localPosition = Vector3.zero;
        _humanRagdollRoot.localRotation = Quaternion.identity;
        _humanRagdollRoot.localScale = Vector3.one;
		List<SkinnedMeshRenderer> list = ListPool<SkinnedMeshRenderer>.Shared.Rent();
		dynamicRagdoll.GetComponentsInChildren(includeInactive: true, list);
		_humanMaterials = new Material[list.Count];
		for (int i = 0; i < _humanMaterials.Length; i++)
		{
			SkinnedMeshRenderer skinnedMeshRenderer = list[i];
			Material material = (skinnedMeshRenderer.sharedMaterial = new Material(Scp3114MaterialTemplates.GetVariantTemplate(skinnedMeshRenderer.sharedMaterial, Scp3114MaterialType.Reveal)));
			material.SetFloat(ProgressHash, 0f);
			_humanMaterials[i] = material;
			skinnedMeshRenderer.sharedMaterial = material;
		}
		ListPool<SkinnedMeshRenderer>.Shared.Return(list);
		dynamicRagdoll.FreezeRagdoll();
		dynamicRagdoll.gameObject.ForEachComponentInChildren(delegate(Component comp)
		{
			// This visual shell is already frozen. Keep its kinematic bodies
            // and joints until destroying the whole shell; Unity 6 rejects
            // removing a Rigidbody while a CharacterJoint depends on it.
			if (!(comp is Transform) && !(comp is Renderer) && !(comp is Rigidbody) && !(comp is Joint))
			{
				Object.Destroy(comp);
			}
		}, includeInactive: true);
	}

	protected override void Update()
	{
		base.Update();
		if (!_playingAnimation)
		{
			return;
		}
		for (int i = 0; i < _trackedBones.Length; i++)
		{
			Transform transform = _sourceBones[i];
			_trackedBones[i].SetPositionAndRotation(transform.position, transform.rotation);
		}
		if (_revealDelay > 0f)
		{
			_revealDelay -= Time.deltaTime;
			return;
		}
		if (_revealElapsed > _revealDuration)
		{
			_playingAnimation = false;
			Object.Destroy(_humanRagdollRoot.gameObject);
			return;
		}
		_revealElapsed += Time.deltaTime;
		float progress = Mathf.Clamp01(_revealElapsed / _revealDuration);
		_ownRenderer.sharedMaterial.SetFloat(FadeHash, progress);
		_humanMaterials.ForEach(delegate(Material x)
		{
			x.SetFloat(ProgressHash, progress);
		});
	}

	[RuntimeInitializeOnLoadMethod]
	private static void Init()
	{
		RagdollManager.ServerOnRagdollCreated += ServerOnRagdollCreated;
	}

	private static void ServerOnRagdollCreated(ReferenceHub owner, BasicRagdoll ragdoll)
	{
		if (ragdoll is Scp3114Ragdoll scp3114Ragdoll)
		{
			scp3114Ragdoll._disguiseRole = RoleTypeId.None;
			if (!(owner == null) && owner.roleManager.CurrentRole is Scp3114Role { Disguised: not false } scp3114Role)
			{
				scp3114Ragdoll._disguiseRole = scp3114Role.CurIdentity.StolenRole;
			}
		}
	}

}
}
