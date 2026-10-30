using UnityEngine;
namespace ScpCeMods
{
    public static class ModRoles
    {
        public static bool CanSpawnNaturally(PlayerRoles.RoleTypeId role)
        {
            if ((int)role == 22 || (int)role == 42) return false;
            if ((int)role != 41) return true;
            var flag = Resources.Load<TextAsset>("SCP3114Mod/NaturalSpawn");
            return GameCore.ConfigFile.ServerConfig.GetBool("scp3114_allow_natural_spawn", flag != null && flag.text.Trim() == "true");
        }
    }
}
