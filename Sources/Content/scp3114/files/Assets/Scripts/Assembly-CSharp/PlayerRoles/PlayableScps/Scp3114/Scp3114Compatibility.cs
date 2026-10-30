using System;
using System.Collections.Generic;
using PlayerRoles.Spectating;
using UnityEngine;

namespace PlayerRoles.PlayableScps.Scp3114
{
    // Small adapters for collection and point-of-view API changes. Gameplay remains in the original subroutines.
    internal static class Scp3114Compatibility
    {
        // Legacy Facility MeshColliders may expose only one face to a ray.
        // Treat either direction as blocked without changing the global backface policy.
        public static bool IsLineOfSightBlocked(Vector3 from, Vector3 to, int layerMask)
        {
            return Physics.Linecast(from, to, layerMask) || Physics.Linecast(to, from, layerMask);
        }

        private static readonly System.Reflection.PropertyInfo DummyProperty = typeof(ReferenceHub).GetProperty("IsDummy");
        public static bool IsDummy(ReferenceHub hub) => hub != null && DummyProperty != null && (bool)DummyProperty.GetValue(hub);

        public static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        public static bool IsFinite(Vector3 value) => IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        public static bool IsFinite(Quaternion value) => IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z) && IsFinite(value.w);

        public static bool TryGetPovHub(out ReferenceHub hub)
        {
            if (SpectatorTargetTracker.TryGetTrackedPlayer(out hub)) return true;
            return ReferenceHub.TryGetLocalHub(out hub);
        }

        public static TValue GetOrAdd<TKey, TValue>(this Dictionary<TKey, TValue> source, TKey key, Func<TValue> factory)
        {
            if (!source.TryGetValue(key, out TValue value)) source[key] = value = factory();
            return value;
        }

        public static TValue GetOrAddNew<TKey, TValue>(this Dictionary<TKey, TValue> source, TKey key) where TValue : new()
        {
            return source.GetOrAdd(key, () => new TValue());
        }

        public static void ForEachValue<TKey, TValue>(this Dictionary<TKey, TValue> source, Action<TValue> action)
        {
            foreach (TValue value in source.Values) action(value);
        }

        public static void ShuffleList<T>(this IList<T> list, System.Random random)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                T temp = list[i]; list[i] = list[j]; list[j] = temp;
            }
        }
    }
}
