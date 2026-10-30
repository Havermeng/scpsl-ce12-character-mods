using Mirror;
using UnityEngine;

namespace PlayerRoles.PlayableScps.Scp3114
{
    // Narrow client registration for this role's existing network corpse prefab.
    // Does not alter any other prefab, asset id, spawn message or death behaviour.
    internal static class Scp3114RagdollRegistration
    {
        private static string _lastError;

        [RuntimeInitializeOnLoadMethod]
        private static void Init()
        {
            CustomNetworkManager.OnClientStarted += OnClientStarted;
            CustomNetworkManager.OnClientReady += RegisterPrefab;
            PlayerRoleLoader.OnLoaded += RegisterPrefab;
        }

        private static void OnClientStarted()
        {
            // NetworkManager replaces this delegate before OnClientStarted, and
            // Shutdown clears it. Reattach for each ordinary connection attempt.
            NetworkClient.OnConnectedEvent -= RegisterPrefab;
            NetworkClient.OnConnectedEvent += RegisterPrefab;
            RegisterPrefab();
        }

        private static void RegisterPrefab()
        {
            if (!NetworkClient.isConnected) return;
            if (!PlayerRoleLoader.TryGetRoleTemplate<Scp3114Role>(RoleTypeId.Scp3114, out var role))
                return; // Role loading may be deferred until PoolManager is ready.

            var ragdoll = role.Ragdoll;
            // NetworkBehaviour.netIdentity is populated by Awake on instances.
            // A loaded prefab asset has not run Awake; query its actual component.
            if (ragdoll == null || !ragdoll.TryGetComponent<NetworkIdentity>(out var identity))
            {
                ReportError("SCP-3114 role template has no network ragdoll prefab.");
                return;
            }
            if (identity.assetId == 0 || identity.sceneId != 0)
            {
                ReportError("SCP-3114 ragdoll requires a nonzero persisted assetId and sceneId0; assetId="
                    + identity.assetId + ", sceneId=" + identity.sceneId + ".");
                return;
            }

            GameObject prefab = identity.gameObject;
            if (NetworkClient.prefabs.TryGetValue(identity.assetId, out var existing))
            {
                if (existing == prefab) return;
                if (existing != null)
                {
                    ReportError("SCP-3114 ragdoll assetId=" + identity.assetId
                        + " conflicts with an already registered different prefab: " + existing.name + ".");
                    return;
                }
                // Mirror's replacement warning dereferences existing.name, so
                // remove only this stale/destroyed entry before normal registration.
                NetworkClient.prefabs.Remove(identity.assetId);
            }

            NetworkClient.RegisterPrefab(prefab);
            if (!NetworkClient.GetPrefab(identity.assetId, out var registered) || registered != prefab)
            {
                ReportError("SCP-3114 ragdoll registration failed for assetId=" + identity.assetId + ".");
                return;
            }
            _lastError = null;
            Debug.Log("[SCP3114 Ragdoll] Registered client prefab '" + prefab.name
                + "' assetId=" + identity.assetId + ".");
        }

        private static void ReportError(string message)
        {
            if (_lastError == message) return;
            _lastError = message;
            Debug.LogError("[SCP3114 Ragdoll] " + message);
        }
    }
}
