using System.Collections.Generic;
using Mirror;
using PlayerRoles.Ragdolls;
using PlayerRoles.PlayableScps.Subroutines;
using UnityEngine;

namespace PlayerRoles.PlayableScps.Scp3114
{

public class Scp3114RagdollToBonesConverter : SubroutineBase
{
	[SerializeField]
	private GameObject[] _boneParts = System.Array.Empty<GameObject>();

	private DynamicRagdoll _syncRagdoll;

	private static bool _cacheSet;

	private static string[] _cachedNames;

	private static GameObject[] _cachedTemplates;

	private static readonly List<Transform> ReplacementTransforms = new List<Transform>();

	private static readonly List<Rigidbody> ReplacementRbs = new List<Rigidbody>();

	public static void ConvertExisting(DynamicRagdoll ragdoll)
	{
		if (!_cacheSet)
		{
			if (!TryPrepCache())
			{
				return;
			}
			_cacheSet = true;
		}
        EnsureLinkedTransforms(ragdoll);
		Transform transform = ragdoll.transform;
		int childCount = transform.childCount;
		for (int i = 0; i < childCount; i++)
		{
			Object.Destroy(transform.GetChild(i).gameObject);
		}
		ReplacementRbs.Clear();
		ReplacementTransforms.Clear();
		Transform[] linkedRigidbodiesTransforms = ragdoll.LinkedRigidbodiesTransforms;
		int num = linkedRigidbodiesTransforms.Length;
		for (int j = 0; j < num; j++)
		{
			string text = linkedRigidbodiesTransforms[j].name.ToLowerInvariant();
			for (int k = 0; k < _cachedNames.Length; k++)
			{
				if (BoneKey(text) == BoneKey(_cachedNames[k]))
				{
					ProcessNameMatch(ragdoll, linkedRigidbodiesTransforms[j], _cachedTemplates[k]);
					break;
				}
			}
		}
		ragdoll.Hitboxes = new HitboxData[0];
		ragdoll.LinkedRigidbodies = ReplacementRbs.ToArray();
		ragdoll.LinkedRigidbodiesTransforms = ReplacementTransforms.ToArray();
		if (ragdoll.TryGetComponent<Collider>(out var component))
		{
			component.enabled = false;
		}
		if (ragdoll.TryGetComponent<Rigidbody>(out var component2))
		{
			component2.isKinematic = true;
		}
	}

	public static void ServerConvertNew(Scp3114Role scp, DynamicRagdoll ragdoll)
	{
		if (scp.SubroutineModule.TryGetSubroutine<Scp3114RagdollToBonesConverter>(out var subroutine))
		{
			RagdollData info = ragdoll.Info;
			ragdoll.NetworkInfo = new RagdollData(info.OwnerHub, new Scp3114DamageHandler(ragdoll, isStarting: false), info.RoleType, info.StartPosition, info.StartRotation, info.Nickname, info.CreationTime);
			subroutine._syncRagdoll = ragdoll;
			subroutine.ServerSendRpc(toAll: true);
		}
	}

	private static bool TryPrepCache()
	{
		if (!RoleTypeId.Scp3114.TryGetRoleTemplate<Scp3114Role>(out var result))
		{
			return false;
		}
		if (!result.SubroutineModule.TryGetSubroutine<Scp3114RagdollToBonesConverter>(out var subroutine))
		{
			return false;
		}
		_cachedTemplates = subroutine._boneParts;
		_cachedNames = new string[_cachedTemplates.Length];
		for (int i = 0; i < _cachedTemplates.Length; i++)
		{
			_cachedNames[i] = _cachedTemplates[i].name.ToLowerInvariant();
		}
		return true;
	}

    // The old recovered human prefabs retain all eleven Rigidbody references,
    // but their parallel Transform array is empty. Rebuild only when3114 needs
    // that array; leave the original ragdoll assets and other roles untouched.
    internal static void EnsureLinkedTransforms(DynamicRagdoll ragdoll)
    {
        Rigidbody[] bodies = ragdoll.LinkedRigidbodies;
        if (bodies == null || bodies.Length == 0)
            bodies = System.Array.FindAll(ragdoll.GetComponentsInChildren<Rigidbody>(true),
                rb => rb != null && rb.transform != ragdoll.transform);
        Transform[] transforms = ragdoll.LinkedRigidbodiesTransforms;
        bool complete = transforms != null && transforms.Length == bodies.Length;
        if (complete)
            for (int i = 0; i < bodies.Length; i++)
                if (bodies[i] == null || transforms[i] != bodies[i].transform) { complete = false; break; }
        if (complete) return;
        var validBodies = new List<Rigidbody>();
        var validTransforms = new List<Transform>();
        foreach (Rigidbody body in bodies)
            if (body != null) { validBodies.Add(body); validTransforms.Add(body.transform); }
        ragdoll.LinkedRigidbodies = validBodies.ToArray();
        ragdoll.LinkedRigidbodiesTransforms = validTransforms.ToArray();
    }

    internal static string BoneKey(string bone)
    {
        string name = bone.ToLowerInvariant().Replace("mixamorig:", "").Replace("_", "").Replace(" ", "").Replace("bip01", "").Replace("bip001", "");
        string side = name.Contains("left") || name.EndsWith(".l") ? "l" : name.Contains("right") || name.EndsWith(".r") ? "r" : name.StartsWith("l") && !name.StartsWith("leg") ? "l" : name.StartsWith("r") ? "r" : "";
        if (name.Contains("hips") || name.Contains("pelvis")) return "hips";
        if (name.Contains("head")) return "head";
        if (name.Contains("forearm") || name.Contains("lowerarm")) return "forearm" + side;
        if (name.Contains("arm")) return "arm" + side;
        if (name.Contains("thigh") || name.Contains("upleg")) return "thigh" + side;
        if (name.Contains("leg") || name.Contains("calf")) return "leg" + side;
        if (name.Contains("spine") || name.Contains("chest")) return "spine";
        return name;
    }

	private static void ProcessNameMatch(DynamicRagdoll ragdoll, Transform ragdollPart, GameObject matchedBone)
	{
		Transform transform = matchedBone.transform;
		GameObject obj = Object.Instantiate(matchedBone, ragdoll.transform);
		obj.SetActive(value: true);
		Rigidbody component = obj.GetComponent<Rigidbody>();
		Transform transform2 = obj.transform;
		ReplacementRbs.Add(component);
		ReplacementTransforms.Add(transform2);
		transform2.SetPositionAndRotation(ragdollPart.TransformPoint(transform.localPosition), ragdollPart.rotation * transform.localRotation);
		if (ragdollPart.TryGetComponent<Rigidbody>(out var component2))
		{
			component.linearVelocity = component2.linearVelocity;
			component.angularVelocity = component2.angularVelocity;
		}
	}

	public override void ServerWriteRpc(NetworkWriter writer)
	{
		base.ServerWriteRpc(writer);
		writer.WriteNetworkBehaviour(_syncRagdoll);
	}

	public override void ClientProcessRpc(NetworkReader reader)
	{
		base.ClientProcessRpc(reader);
		_syncRagdoll = reader.ReadNetworkBehaviour<DynamicRagdoll>();
		if (!(_syncRagdoll == null))
		{
			ConvertExisting(_syncRagdoll);
		}
	}
}


}
