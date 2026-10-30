using System.Collections.Generic;
using Mirror;
using PlayerRoles.PlayableScps.Subroutines;
using PlayerRoles.Ragdolls;
using PlayerRoles.Spectating;
using UnityEngine;
using Utils.Networking;

namespace PlayerRoles.PlayableScps.Scp3114
{
    // Native RagdollIndicatorsBase behavior, scoped to 3114 because v12 does
    // not contain that newer common base.
    public class Scp3114Indicators : ScpStandardSubroutine<Scp3114Role>
    {
        private readonly struct Indicator
        {
            public readonly GameObject Instance;
            private readonly CanvasGroup _group;

            public Indicator(GameObject instance)
            {
                Instance = instance;
                _group = instance.GetComponentInChildren<CanvasGroup>();
                SetAlpha(0f);
            }

            public void SetAlpha(float alpha)
            {
                _group.alpha = Mathf.Clamp01(alpha);
            }
        }

        private enum ListSyncRpcType : byte { FullResync, Add, Remove }

        // Exact retail resources.assets:69896 fields. Template carries the
        // authored world-space size, icon, labels and key binding.
        [SerializeField] private float _showDelay = .16f;
        [SerializeField] private float _fullOpacityDistance = 4f;
        [SerializeField] private float _visibleDistance = 6.5f;
        [SerializeField] private GameObject _indicatorTemplate;
        [SerializeField] private Vector3 _posOffset = new Vector3(0f, .1f, 0f);

        private readonly Dictionary<BasicRagdoll, Indicator> _indicatorInstances = new Dictionary<BasicRagdoll, Indicator>();
        private readonly HashSet<BasicRagdoll> _availableRagdolls = new HashSet<BasicRagdoll>();
        // v12 lacks RagdollManager.AllRagdolls. Its events maintain the same
        // collection after a single snapshot when this pooled role spawns.
        private readonly HashSet<BasicRagdoll> _allRagdolls = new HashSet<BasicRagdoll>();
        private ListSyncRpcType _rpcType;
        private uint _syncRagdoll;

        private bool ValidateRagdoll(BasicRagdoll ragdoll)
        {
            if (ragdoll == null || ScpRole.Disguised || !ragdoll.Info.RoleType.TryGetRoleTemplate<HumanRole>(out _))
                return false;
            return !(ragdoll.Info.Handler is Scp3114DamageHandler used)
                || used.Subtype != Scp3114DamageHandler.HandlerType.SkinSteal;
        }

        private void Update()
        {
            if (NetworkServer.active && !ServerCheckNew())
                ServerRevalidateOld();
        }

        private bool ServerCheckNew()
        {
            foreach (BasicRagdoll ragdoll in _allRagdolls)
            {
                if (ValidateRagdoll(ragdoll) && ragdoll.Info.ExistenceTime > _showDelay && _availableRagdolls.Add(ragdoll))
                {
                    ServerSendRpc(ListSyncRpcType.Add, ragdoll);
                    return true;
                }
            }
            return false;
        }

        private void ServerRevalidateOld()
        {
            BasicRagdoll invalid = null;
            bool found = false;
            foreach (BasicRagdoll ragdoll in _availableRagdolls)
            {
                if (ValidateRagdoll(ragdoll)) continue;
                invalid = ragdoll;
                found = true;
                break;
            }
            if (!found) return;
            _availableRagdolls.Remove(invalid);
            ServerSendRpc(ListSyncRpcType.Remove, invalid);
        }

        private void UpdateIndicators()
        {
            if (Role == null || Role.Pooled || Owner == null
                || (!Owner.isLocalPlayer && !SpectatorNetworking.IsLocallySpectated(Owner)))
                return;
            Transform camera = MainCameraController.CurrentCamera;
            if (camera == null) return;
            Vector3 cameraPosition = camera.position;
            foreach (BasicRagdoll ragdoll in _availableRagdolls)
            {
                if (ragdoll != null)
                    RefreshIndicator(cameraPosition, camera, ragdoll);
            }
        }

        private void RefreshIndicator(Vector3 cameraPosition, Transform camera, BasicRagdoll ragdoll)
        {
            Transform center = ragdoll.CenterPoint;
            if (center == null) return;
            Vector3 ragdollPosition = center.position;
            float distanceSquared = (ragdollPosition - cameraPosition).sqrMagnitude;
            bool outsideRange = distanceSquared > _visibleDistance * _visibleDistance;
            if (!_indicatorInstances.TryGetValue(ragdoll, out Indicator indicator))
            {
                if (outsideRange || _indicatorTemplate == null) return;
                indicator = new Indicator(Instantiate(_indicatorTemplate));
                indicator.Instance.SetActive(HideHUDController.IsHUDVisible);
                _indicatorInstances.Add(ragdoll, indicator);
            }
            if (outsideRange)
            {
                indicator.SetAlpha(0f);
                return;
            }
            indicator.SetAlpha(Mathf.InverseLerp(_visibleDistance, _fullOpacityDistance, Mathf.Sqrt(distanceSquared)));
            Transform marker = indicator.Instance.transform;
            marker.position = ragdollPosition + _posOffset;
            marker.forward = camera.forward;
        }

        private void OnHudToggled(bool state)
        {
            foreach (Indicator indicator in _indicatorInstances.Values)
                indicator.Instance.SetActive(state);
        }

        private void OnSpectatorTargetChanged()
        {
            foreach (Indicator indicator in _indicatorInstances.Values)
                Destroy(indicator.Instance);
            _indicatorInstances.Clear();
        }

        public override void SpawnObject()
        {
            base.SpawnObject();
            foreach (BasicRagdoll ragdoll in Object.FindObjectsByType<BasicRagdoll>(FindObjectsSortMode.None))
                _allRagdolls.Add(ragdoll);
            PlayerRoleManager.OnRoleChanged += OnRoleChanged;
            RagdollManager.OnRagdollSpawned += OnRagdollSpawned;
            RagdollManager.OnRagdollRemoved += OnRagdollRemoved;
            HideHUDController.ToggleHUD += OnHudToggled;
            MainCameraController.OnUpdated += UpdateIndicators;
            SpectatorTargetTracker.OnTargetChanged += OnSpectatorTargetChanged;
        }

        public override void ResetObject()
        {
            base.ResetObject();
            _availableRagdolls.Clear();
            _allRagdolls.Clear();
            PlayerRoleManager.OnRoleChanged -= OnRoleChanged;
            RagdollManager.OnRagdollSpawned -= OnRagdollSpawned;
            RagdollManager.OnRagdollRemoved -= OnRagdollRemoved;
            HideHUDController.ToggleHUD -= OnHudToggled;
            MainCameraController.OnUpdated -= UpdateIndicators;
            SpectatorTargetTracker.OnTargetChanged -= OnSpectatorTargetChanged;
            OnSpectatorTargetChanged();
        }

        public override void ServerWriteRpc(NetworkWriter writer)
        {
            base.ServerWriteRpc(writer);
            writer.WriteByte((byte)_rpcType);
            if (_rpcType != ListSyncRpcType.FullResync)
            {
                writer.WriteUInt(_syncRagdoll);
                return;
            }
            foreach (BasicRagdoll ragdoll in _availableRagdolls)
                writer.WriteUInt(ragdoll.netId);
        }

        public override void ClientProcessRpc(NetworkReader reader)
        {
            base.ClientProcessRpc(reader);
            ListSyncRpcType rpcType = (ListSyncRpcType)reader.ReadByte();
            switch (rpcType)
            {
                case ListSyncRpcType.Add:
                case ListSyncRpcType.Remove:
                    ClientProcessRpcSingularNetId(reader.ReadUInt(), rpcType);
                    return;
                case ListSyncRpcType.FullResync:
                    break;
                default:
                    return;
            }
            while (_availableRagdolls.Count > 0)
            {
                BasicRagdoll first = null;
                foreach (BasicRagdoll ragdoll in _availableRagdolls) { first = ragdoll; break; }
                ClientRemoveRagdoll(first);
            }
            while (reader.Remaining > 0)
                ClientProcessRpcSingularNetId(reader.ReadUInt(), ListSyncRpcType.Add);
        }

        private void ClientProcessRpcSingularNetId(uint netId, ListSyncRpcType rpcType)
        {
            if (!NetworkUtils.SpawnedNetIds.TryGetValue(netId, out NetworkIdentity identity)
                || !identity.TryGetComponent(out BasicRagdoll ragdoll))
                return;
            if (rpcType == ListSyncRpcType.Add)
                _availableRagdolls.Add(ragdoll);
            else
                ClientRemoveRagdoll(ragdoll);
        }

        private void OnRoleChanged(ReferenceHub hub, PlayerRoleBase oldRole, PlayerRoleBase newRole)
        {
            if (NetworkServer.active && newRole is SpectatorRole)
            {
                _rpcType = ListSyncRpcType.FullResync;
                base.ServerSendRpc(hub);
            }
        }

        private void ServerSendRpc(ListSyncRpcType rpcType, BasicRagdoll ragdoll)
        {
            _rpcType = rpcType;
            _syncRagdoll = ragdoll == null ? 0 : ragdoll.netId;
            ServerSendRpc(hub => hub == Owner || hub.roleManager.CurrentRole is SpectatorRole);
        }

        private void ClientRemoveRagdoll(BasicRagdoll ragdoll)
        {
            _availableRagdolls.Remove(ragdoll);
            if (_indicatorInstances.TryGetValue(ragdoll, out Indicator indicator))
            {
                Destroy(indicator.Instance);
                _indicatorInstances.Remove(ragdoll);
            }
        }

        private void OnRagdollSpawned(BasicRagdoll ragdoll)
        {
            _allRagdolls.Add(ragdoll);
        }

        private void OnRagdollRemoved(BasicRagdoll ragdoll)
        {
            _allRagdolls.Remove(ragdoll);
            ClientRemoveRagdoll(ragdoll);
        }
    }
}
