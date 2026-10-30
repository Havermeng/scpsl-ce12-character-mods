using System;
using UnityEngine;

namespace PlayerRoles.PlayableScps.Scp939
{
    public sealed class Scp939GiftArmMeshes : ScriptableObject
    {
        [Serializable] public struct Entry { public Mesh Source, Arm; }
        public Entry[] Entries = Array.Empty<Entry>();
        private static Scp939GiftArmMeshes _instance;
        public static Mesh Find(Mesh source)
        {
            if (_instance == null) _instance = Resources.Load<Scp939GiftArmMeshes>("Scp939GiftArmMeshes");
            if (_instance != null) foreach (var entry in _instance.Entries) if (entry.Source == source) return entry.Arm;
            return null;
        }
    }
}
