using System.Collections.Generic;
using UnityEngine;
namespace PlayerRoles.PlayableScps.Scp939
{
    public static class Scp939ModSettings
    {
        private static readonly Dictionary<string,bool> Cache = new Dictionary<string,bool>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] private static void Reset() { Cache.Clear(); }
        public static bool Enabled(string component)
        {
            if (!Cache.TryGetValue(component,out var enabled)) { enabled = Resources.Load<TextAsset>("SCP939Mod/"+component) != null; Cache[component]=enabled; }
            return enabled;
        }
        public static bool Allows(Scp939InteractionKind kind)
        {
            if (!Enabled("interactions")) return false;
            if (kind==Scp939InteractionKind.Gift) return Enabled("gifts");
            if (kind==Scp939InteractionKind.Date || kind==Scp939InteractionKind.Eat || kind==Scp939InteractionKind.Sit) return Enabled("dates");
            if (kind==Scp939InteractionKind.Say || kind==Scp939InteractionKind.Line || kind==Scp939InteractionKind.Affection) return Enabled("dialogue");
            return true;
        }
    }
}
