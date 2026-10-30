using UnityEngine;
namespace PlayerRoles.PlayableScps.Scp3114
{
    public static class Scp3114PackageSettings
    {
        public static bool NaturalSpawn
        {
            get { var value = Resources.Load<TextAsset>("SCP3114Mod/NaturalSpawn"); return value != null && value.text.Trim() == "true"; }
        }
        public static bool CanSpawnNaturally(PlayerRoles.RoleTypeId role)
        {
            if ((int)role == 42) return false;
            return (int)role != 41 || GameCore.ConfigFile.ServerConfig.GetBool("scp3114_allow_natural_spawn", NaturalSpawn);
        }
    }
}
