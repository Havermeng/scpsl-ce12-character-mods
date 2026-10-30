using Mirror;
using UnityEngine;

namespace PlayerRoles.PlayableScps.Scp3114
{
    /// <summary>The extra role has an extra corpse prefab. Register it on each normal client.</summary>
    internal static class Scp3114DressedRegistration
    {
        [RuntimeInitializeOnLoadMethod]
        private static void Init()
        {
            CustomNetworkManager.OnClientStarted += Connected;
            CustomNetworkManager.OnClientReady += Register;
            PlayerRoleLoader.OnLoaded += Register;
        }

        private static void Connected()
        {
            NetworkClient.OnConnectedEvent -= Register;
            NetworkClient.OnConnectedEvent += Register;
            Register();
        }

        private static void Register()
        {
            if (!NetworkClient.isConnected ||
                !PlayerRoleLoader.TryGetRoleTemplate<Scp3114Role>(RoleTypeId.Scp3114Dressed, out var role)) return;
            if (role.Ragdoll == null || !role.Ragdoll.TryGetComponent<NetworkIdentity>(out var identity)
                || identity.assetId == 0 || identity.sceneId != 0)
            {
                Debug.LogError("[SCP3114 Dressed] Invalid dressed ragdoll network prefab.");
                return;
            }
            if (NetworkClient.prefabs.TryGetValue(identity.assetId, out var existing))
            {
                if (existing == identity.gameObject) return;
                if (existing != null)
                {
                    Debug.LogError("[SCP3114 Dressed] Corpse asset id conflict: " + identity.assetId);
                    return;
                }
                NetworkClient.prefabs.Remove(identity.assetId);
            }
            NetworkClient.RegisterPrefab(identity.gameObject);
        }
    }
}
