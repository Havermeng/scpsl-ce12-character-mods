using System.Collections.Generic;
using System.Text;
using Interactables.Interobjects.DoorUtils;
using InventorySystem;
using InventorySystem.Items;
using InventorySystem.Items.Pickups;
using Mirror;
using PlayerRoles.FirstPersonControl;
using PlayerRoles.PlayableScps.HUDs;
using PlayerRoles.PlayableScps.HumeShield;
using PlayerRoles.PlayableScps.Subroutines;
using PlayerStatsSystem;
using UnityEngine;

namespace PlayerRoles.PlayableScps.Scp3114
{

public class Scp3114Role : FpcStandardScp, IHumeShieldedRole, IInventoryRole, ISubroutinedScpRole, IHudScp, ICustomNicknameDisplayRole, IDamageHandlerProcessingRole, IHitmarkerPreventer, ISpawnableScp
{

	private static readonly ItemCategory[] BlacklistedCategories = new ItemCategory[1]
	{
		ItemCategory.SCPItem
	};

    private static readonly ItemType[] BlacklistedItems = BuildBlacklistedItems();

    private static ItemType[] BuildBlacklistedItems()
    {
        var result = new List<ItemType>();
        foreach (string name in new[] { "KeycardO5", "KeycardFacilityManager", "Jailbird", "MicroHID", "ParticleDisruptor", "ParticleDisruptorV14", "SahurBat", "BanHammer" })
            if (System.Enum.TryParse(name, out ItemType value)) result.Add(value);
        return result.ToArray();
    }

	private Scp3114Identity _identity;

	private Scp3114DamageProcessor _damageProcessor;

	private static bool EnableSpawning => GameCore.ConfigFile.ServerConfig.GetBool("scp3114_allow_natural_spawn", Scp3114PackageSettings.NaturalSpawn);

	[field: SerializeField]
	public HumeShieldModuleBase HumeShieldModule { get; private set; }

	[field: SerializeField]
	public SubroutineManagerModule SubroutineModule { get; private set; }

	[field: SerializeField]
	public ScpHudBase HudPrefab { get; private set; }

	public Scp3114Identity.StolenIdentity CurIdentity
    {
        get
        {
            if (_identity == null) _identity = GetComponentInChildren<Scp3114Identity>(true);
            return _identity.CurIdentity;
        }
    }

	public bool Disguised
	{
		get
		{
			return CurIdentity.Status == Scp3114Identity.DisguiseStatus.Active;
		}
		set
		{
			if (value != Disguised && NetworkServer.active)
			{
				CurIdentity.Status = (value ? Scp3114Identity.DisguiseStatus.Active : Scp3114Identity.DisguiseStatus.None);
				_identity.ServerResendIdentity();
			}
		}
	}

	public bool SkeletonIdle => CurIdentity.Status == Scp3114Identity.DisguiseStatus.None;

	public bool RespectPreferences => EnableSpawning;

	public bool AllowFallback => EnableSpawning;

	public Color NicknameColor => _identity.NicknameColor;

	private void Awake()
    {
        SubroutineModule.TryGetSubroutine<Scp3114Identity>(out _identity);
        SubroutineModule.TryGetSubroutine<Scp3114DamageProcessor>(out _damageProcessor);
    }

	public bool CanPickupItem(ItemPickupBase itemPickup)
	{
		ItemType typeId = itemPickup.Info.ItemId;
		if (System.Array.IndexOf(BlacklistedItems, typeId) >= 0)
		{
			return false;
		}
		if (!InventoryItemLoader.TryGetItem<ItemBase>(typeId, out var result))
		{
			return false;
		}
		return System.Array.IndexOf(BlacklistedCategories, result.Category) < 0;
	}

	public void WriteNickname(StringBuilder sb)
	{
		_identity.WriteNickname(sb);
	}

	public DamageHandlerBase ProcessDamageHandler(DamageHandlerBase dhb)
	{
		return _damageProcessor.ProcessDamageHandler(dhb);
	}

	public bool TryPreventHitmarker(AttackerDamageHandler adh)
	{
		if (!Disguised)
		{
			return false;
		}
		RoleTypeId role = adh.Attacker.Role;
		RoleTypeId stolenRole = CurIdentity.StolenRole;
		return !HitboxIdentity.CheckFriendlyFire(role, stolenRole, ignoreConfig: false);
	}

	public bool AllowDisarming(ReferenceHub detainer)
	{
		if (CurIdentity.Status != Scp3114Identity.DisguiseStatus.Active)
		{
			return false;
		}
		if (CurIdentity.StolenRole.GetFaction() == detainer.GetFaction())
		{
			return false;
		}
		return true;
	}

	public bool AllowUndisarming(ReferenceHub releaser)
	{
		return releaser.IsHuman();
	}

	public float GetSpawnChance(List<RoleTypeId> alreadySpawned)
	{
		return EnableSpawning ? 1 : 0;
	}

	private bool TryGetCurrentItem(out ItemBase ib)
	{
		if (!Disguised || !TryGetOwner(out var hub))
		{
			ib = null;
			return false;
		}
		ib = hub.inventory.CurInstance;
		return true;
	}
}


}
