using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace PlayerRoles.PlayableScps.Scp3114
{
    // Old 12.x models use a real generic Mixamo rig without an Avatar. Resolve those real bones,
    // preserving the modern Humanoid route where an actual valid Avatar exists.
    public static class Scp3114BoneResolver
    {
        public static Dictionary<string, Transform> CreateLookup(Transform root)
        {
            var result = new Dictionary<string, Transform>(StringComparer.Ordinal);
            foreach (Transform bone in root.GetComponentsInChildren<Transform>(true))
            {
                string key = Normalize(bone.name);
                if (!result.ContainsKey(key)) result.Add(key, bone);
            }
            return result;
        }

        public static Transform Resolve(Animator animator, Dictionary<string, Transform> lookup, HumanBodyBones bone)
        {
            if (animator != null && animator.avatar != null && animator.avatar.isValid && animator.avatar.isHuman)
            {
                Transform mapped = animator.GetBoneTransform(bone);
                if (mapped != null) return mapped;
            }
            string enumName = bone.ToString();
            if (lookup.TryGetValue(Normalize(enumName), out Transform direct)) return direct;
            string side = enumName.StartsWith("Left", StringComparison.Ordinal) ? "Left" : enumName.StartsWith("Right", StringComparison.Ordinal) ? "Right" : "";
            string part = side.Length == 0 ? enumName : enumName.Substring(side.Length);
            string shortSide = side == "Left" ? "L" : side == "Right" ? "R" : "";
            string[] names;
            switch (part)
            {
                case "Hips": names = new[] { "Hips", "Pelvis" }; break;
                case "Spine": names = new[] { "Spine", "Spine0" }; break;
                case "Chest": names = new[] { "Spine1", "Chest" }; break;
                case "UpperChest": names = new[] { "Spine2", "UpperChest" }; break;
                case "Neck": names = new[] { "Neck", "Neck1" }; break;
                case "Head": names = new[] { "Head" }; break;
                case "Shoulder": names = new[] { side + "Shoulder", shortSide + "Clavicle", side + "Clavicle" }; break;
                case "UpperArm": names = new[] { side + "Arm", shortSide + "UpperArm", side + "UpperArm" }; break;
                case "LowerArm": names = new[] { side + "ForeArm", shortSide + "Forearm", side + "LowerArm" }; break;
                case "Hand": names = new[] { side + "Hand", shortSide + "Hand" }; break;
                case "UpperLeg": names = new[] { side + "UpLeg", shortSide + "Thigh", side + "Thigh" }; break;
                case "LowerLeg": names = new[] { side + "Leg", shortSide + "Calf", side + "Calf" }; break;
                case "Foot": names = new[] { side + "Foot", shortSide + "Foot" }; break;
                case "Toes": names = new[] { side + "ToeBase", shortSide + "Toe0", side + "Toes" }; break;
                default:
                    names = FingerAliases(side, shortSide, part);
                    break;
            }
            foreach (string name in names)
                if (lookup.TryGetValue(Normalize(name), out Transform found)) return found;
            return null;
        }

        private static string[] FingerAliases(string side, string shortSide, string part)
        {
            string finger = null;
            int fingerId = -1;
            string[] fingers = { "Thumb", "Index", "Middle", "Ring", "Little" };
            for (int i = 0; i < fingers.Length; i++)
                if (part.StartsWith(fingers[i], StringComparison.Ordinal)) { finger = fingers[i]; fingerId = i; break; }
            if (finger == null) return Array.Empty<string>();
            int joint = part.EndsWith("Proximal", StringComparison.Ordinal) ? 1 : part.EndsWith("Intermediate", StringComparison.Ordinal) ? 2 : part.EndsWith("Distal", StringComparison.Ordinal) ? 3 : 0;
            if (joint == 0) return Array.Empty<string>();
            string mixamoFinger = finger == "Little" ? "Pinky" : finger;
            string bipJoint = joint == 1 ? "" : (joint - 1).ToString();
            return new[] { side + "Hand" + mixamoFinger + joint, shortSide + "Finger" + fingerId + bipJoint, side + "Finger" + fingerId + bipJoint };
        }

        private static string Normalize(string name)
        {
            int colon = name.LastIndexOf(':');
            if (colon >= 0) name = name.Substring(colon + 1);
            name = name.ToLowerInvariant().Replace("bip001", "").Replace("bip01", "").Replace("mixamorig", "");
            var builder = new StringBuilder(name.Length);
            foreach (char c in name) if (char.IsLetterOrDigit(c)) builder.Append(c);
            return builder.ToString();
        }

        public static Dictionary<Transform, Quaternion> BindRotations(Transform modelRoot)
        {
            var result = new Dictionary<Transform, Quaternion>();
            foreach (SkinnedMeshRenderer renderer in modelRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (renderer.sharedMesh == null) continue;
                Matrix4x4[] poses;
                try { poses = renderer.sharedMesh.bindposes; }
                catch (UnityException) { continue; } // Some legacy meshes disallow CPU reads; the caller retains the actual current rest frame.
                Transform[] bones = renderer.bones;
                for (int i = 0; i < bones.Length && i < poses.Length; i++)
                {
                    if (bones[i] == null || result.ContainsKey(bones[i])) continue;
                    Quaternion rotation = (renderer.localToWorldMatrix * poses[i].inverse).rotation;
                    if (Scp3114Compatibility.IsFinite(rotation)) result.Add(bones[i], rotation);
                }
            }
            return result;
        }

        public static int Depth(Transform bone)
        {
            int result = 0;
            for (Transform parent = bone.parent; parent != null; parent = parent.parent) result++;
            return result;
        }
    }
}
