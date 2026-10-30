using MapGeneration;
using Mirror;
using PlayerRoles.Ragdolls;
using PlayerRoles.RoleAssign;
using RelativePositioning;
using UnityEngine;

namespace PlayerRoles.PlayableScps.Scp3114
{

public static class Scp3114InitialRagdollSpawner
{
	private static bool _ragdollsSpawned;

	[RuntimeInitializeOnLoadMethod]
	private static void Init()
	{
		CustomNetworkManager.OnClientReady += OnBeforeSpawn;
		PlayerRoleManager.OnServerRoleSet += OnServerRoleSet;
	}

	private static void OnServerRoleSet(ReferenceHub userHub, RoleTypeId newRole, RoleChangeReason reason)
	{
		if (newRole == RoleTypeId.Scp3114 || newRole == RoleTypeId.Scp3114Dressed)
		{
			ServerSpawnRagdolls(userHub);
		}
	}

	private static void OnBeforeSpawn()
	{
		if (NetworkServer.active)
		{
			_ragdollsSpawned = false;
		}
	}

	private static void ServerSpawnRagdolls(ReferenceHub nicknameSourceHub)
	{
		if (_ragdollsSpawned || !TryFindSpawnRoom(out var room))
			return;
		if (!TryResolveStock173Anchor(room, out var anchor, out var yaw)
			|| !TryGetSupportedFloor(room, anchor, out var anchorFloor))
		{
			Debug.LogWarning("[SCP-3114] Initial bodies not spawned: stock173 anchor/floor could not be validated.");
			return;
		}
		Quaternion facing = Quaternion.Euler(0f, yaw, 0f);
		Vector3 forward = facing * Vector3.forward;
		Vector3 right = facing * Vector3.right;
		// Relative to the actual old role's spawn anchor, not the recovered newer room coordinates.
		Vector3 first = anchor + forward * .9f - right * .7f;
		Vector3 second = anchor + forward * .9f + right * .7f;
		if (!TryGetSupportedFloor(room, first, out var firstFloor)
			|| !TryGetSupportedFloor(room, second, out var secondFloor)
			|| Mathf.Abs(firstFloor.y - anchorFloor.y) > .2f
			|| Mathf.Abs(secondFloor.y - anchorFloor.y) > .2f
			|| !HasBodyClearance(firstFloor) || !HasBodyClearance(secondFloor)
			|| !HasSupportedApproach(room, anchorFloor, firstFloor)
			|| !HasSupportedApproach(room, anchorFloor, secondFloor))
		{
			Debug.LogWarning("[SCP-3114] Initial bodies not spawned: local chamber slots need a supported, clear approach.");
			return;
		}
		// Retain the stock anchor's root clearance while letting normal ragdoll physics settle the bodies.
		float rootClearance = Mathf.Clamp(anchor.y - anchorFloor.y, .65f, 1f);
		first = firstFloor + Vector3.up * rootClearance;
		second = secondFloor + Vector3.up * rootClearance;
		if (new RelativePosition(first).WaypointId == 0 || new RelativePosition(second).WaypointId == 0
			|| !RoleTypeId.Scientist.TryGetRoleTemplate<HumanRole>(out _)
			|| !RoleTypeId.ClassD.TryGetRoleTemplate<HumanRole>(out _))
			return;
		_ragdollsSpawned = true;
		Debug.Log("[SCP-3114] Initial body anchor=" + anchor.ToString("F3")
			+ " floor=" + anchorFloor.ToString("F3") + " slots=" + first.ToString("F3") + "/" + second.ToString("F3"));
		ServerSpawnRagdoll(RoleTypeId.Scientist, first, facing * Quaternion.Euler(25.5f, 0f, -9.4f), nicknameSourceHub);
		ServerSpawnRagdoll(RoleTypeId.ClassD, second, facing * Quaternion.Euler(22.2f, 0f, 11.8f), nicknameSourceHub);
	}

	private static bool TryResolveStock173Anchor(RoomIdentifier room, out Vector3 anchor, out float yaw)
	{
		// SpawnpointHandler is the existing public old-role API; no private field reflection.
		if (RoleTypeId.Scp173.TryGetRoleTemplate<FpcStandardScp>(out var template)
			&& template.SpawnpointHandler != null)
        {
            try
            {
                if (template.SpawnpointHandler.TryGetSpawnpoint(out anchor, out yaw)
                    && RoomIdUtils.IsWithinRoomBoundaries(room, anchor)) return true;
            }
            catch (System.NullReferenceException)
            {
                // The legacy RoomRoleSpawnpoint API dereferences RandomItem() even when its
                // generated-room list is empty. Keep the physical SP_173 fallback usable.
            }
        }
		foreach (Transform child in room.GetComponentsInChildren<Transform>(true))
		{
			if (child.name != "SP_173") continue;
			anchor = child.position;
			yaw = child.eulerAngles.y;
			return true;
		}
		anchor = Vector3.zero;
		yaw = 0f;
		return false;
	}

	private static int InitialBodyGeometryMask => PlayerRoles.FirstPersonControl.FpcStateProcessor.Mask.value
		& ~LayerMask.GetMask("Player", "Hitbox", "Ragdoll", "Pickup");

	private static bool TryGetSupportedFloor(RoomIdentifier room, Vector3 sample, out Vector3 floor)
	{
		floor = Vector3.zero;
		if (!Physics.Raycast(sample + Vector3.up * .3f, Vector3.down, out var hit, 3f,
			InitialBodyGeometryMask, QueryTriggerInteraction.Ignore)
			|| hit.normal.y < .85f || hit.point.y > sample.y + .05f
			|| hit.collider.GetComponentInParent<RoomIdentifier>() != room)
			return false;
		floor = hit.point;
		return true;
	}

	private static bool HasBodyClearance(Vector3 floor)
	{
		return !Physics.CheckCapsule(floor + Vector3.up * .35f, floor + Vector3.up * 1.5f, .25f,
			InitialBodyGeometryMask, QueryTriggerInteraction.Ignore);
	}

	private static bool HasSupportedApproach(RoomIdentifier room, Vector3 fromFloor, Vector3 toFloor)
	{
		// This is local floor continuity, not a substitute for a real motor/pathfinding walk test.
		for (int i = 1; i < 6; i++)
		{
			Vector3 sample = Vector3.Lerp(fromFloor, toFloor, i / 6f);
			if (!TryGetSupportedFloor(room, sample + Vector3.up * .8f, out var floor)
				|| Mathf.Abs(floor.y - fromFloor.y) > .2f || !HasBodyClearance(floor))
				return false;
		}
		return true;
	}

    private static bool TryFindSpawnRoom(out RoomIdentifier room)
    {
        foreach (RoomIdentifier candidate in RoomIdentifier.AllRoomIdentifiers)
        {
            if (candidate.Name == RoomName.Lcz173) { room = candidate; return true; }
        }
        room = null;
        return false;
    }

	private static void ServerSpawnRagdoll(RoleTypeId role, Vector3 pos, Quaternion rot, ReferenceHub nicknameSource)
	{
		if (role.TryGetRoleTemplate<HumanRole>(out var result))
		{
			RelativePosition relPos = new RelativePosition(pos);
			if (relPos.WaypointId != 0)
			{
				Quaternion relativeRotation = rot;
				BasicRagdoll basicRagdoll = result.Ragdoll.ServerInstantiateSelf(nicknameSource, role);
				basicRagdoll.NetworkInfo = new RagdollData(null, new Scp3114DamageHandler(basicRagdoll, isStarting: true), role, pos, relativeRotation, nicknameSource.nicknameSync.DisplayName, NetworkTime.time);
				NetworkServer.Spawn(basicRagdoll.gameObject);
			}
		}
	}
}


}
