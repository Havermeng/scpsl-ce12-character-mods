using System;
using System.Collections;
using System.Reflection;
using PlayerRoles.FirstPersonControl;
using UnityEngine;

namespace PlayerRoles.PlayableScps.Scp939
{
    // Optional integrations are looked up only if the recipient has installed them.
    // No dummy, cinema or Human14 framework is bundled or fabricated by this mod.
    public static class Scp939ModCompatibility
    {
        private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        private static Type FindType(string name) => typeof(ReferenceHub).Assembly.GetType(name);
        private static readonly Type Director = FindType("Cinema.CinemaDirector");
        private static readonly Type CameraType = FindType("Cinema.CinemaCamera");
        private static readonly Type MotionType = FindType("Cinema.CinemaMotionScale");
        private static readonly Type WearableType = FindType("PlayerRoles.FirstPersonControl.Thirdperson.Human14NativeWearable");
        private static readonly Type DummyUtils = FindType("NetworkManagerUtils.Dummies.DummyUtils");
        private static object Read(Type t, object o, string name) => t?.GetProperty(name,Flags)?.GetValue(o) ?? t?.GetField(name,Flags)?.GetValue(o);
        private static void Write(Type t, object o, string name, object value)
        { var p=t?.GetProperty(name,Flags); if(p?.CanWrite==true)p.SetValue(o,value);else t?.GetField(name,Flags)?.SetValue(o,value); }
        public static bool Filming => Read(Director,null,"Filming") is bool b && b;
        public static bool DirectorRunning => Read(Director,null,"IsRunning") is bool b && b;
        public static bool CameraActive => Read(CameraType,null,"IsActive") is bool b && b;
        public static ReferenceHub Host => Read(Director,null,"Host") as ReferenceHub;
        public static bool IsBuilding => Read(FindType("Building.BuildingController"),null,"IsBuilding") is bool b && b;
        public static bool IsDummy(ReferenceHub hub) => hub != null && Read(typeof(ReferenceHub),hub,"IsDummy") is bool b && b;
        public static void AimAt(ReferenceHub hub, ReferenceHub target)
        {
            if (!IsDummy(hub) || target==null)return;
            var method=DummyUtils?.GetMethod("AimAt",Flags,null,new[]{typeof(ReferenceHub),typeof(ReferenceHub)},null);
            if(method!=null)method.Invoke(null,new object[]{hub,target});
            else if(hub.roleManager.CurrentRole is IFpcRole r)r.FpcModule.MouseLook.LookAtDirection(target.PlayerCameraReference.position-hub.PlayerCameraReference.position);
        }
        public static void DummyForwardAttack(ReferenceHub hub)
        {
            var type=FindType("NetworkManagerUtils.Dummies.DummyActionCollector");
            var method=type?.GetMethod("ServerGetActions",Flags,null,new[]{typeof(ReferenceHub)},null);
            if(method?.Invoke(null,new object[]{hub}) is IEnumerable actions)
                foreach(var a in actions)
                    if(Read(a.GetType(),a,"Name") as string=="Attack (forward)" && Read(a.GetType(),a,"Action") is Action invoke){invoke();return;}
        }
        public static Vector3 Feet(ReferenceHub hub)
        {
            if(hub==null)return Vector3.zero;
            if(hub.roleManager.CurrentRole is IFpcRole f && f.FpcModule.CharacterModelInstance!=null)return f.FpcModule.CharacterModelInstance.transform.position;
            return hub.transform.position;
        }
        public sealed class OptionalMotion
        {
            private readonly UnityEngine.Component source;
            internal OptionalMotion(UnityEngine.Component value){source=value;}
            public Vector3 Heading => Read(MotionType,source,"Heading") is Vector3 v?v:Vector3.zero;
            public bool PairedWalk => Read(MotionType,source,"PairedWalk") is bool b&&b;
            public float FloorY => Read(MotionType,source,"FloorY") is float f?f:source.transform.position.y;
            public float Multiplier { get => Read(MotionType,source,"Multiplier") is float f?f:1f;set=>Write(MotionType,source,"Multiplier",value); }
        }
        public static OptionalMotion Motion(UnityEngine.Component owner)
        { var c=owner!=null&&MotionType!=null?owner.GetComponent(MotionType):null;return c!=null?new OptionalMotion(c):null; }
        public static OptionalMotion ParentMotion(UnityEngine.Component owner)
        { var c=owner!=null&&MotionType!=null?owner.GetComponentInParent(MotionType):null;return c!=null?new OptionalMotion(c):null; }
        public static UnityEngine.Component Wearable(UnityEngine.Component owner)
        { return owner!=null&&WearableType!=null?owner.GetComponentInParent(WearableType):null; }
        public static void MatchWearable(UnityEngine.Component source)
        { if(source!=null&&Read(WearableType,source,"Initialized") is bool b&&b)WearableType.GetMethod("MatchBones",Flags)?.Invoke(source,null); }
        public static bool EligibleSkin(Transform model, SkinnedMeshRenderer skin, ReferenceHub hub)
        {
            if(skin.gameObject.activeInHierarchy)return true;
            var w=Wearable(model);
            if(w!=null && Read(WearableType,w,"DefinedSets") is IEnumerable sets)
            {
                var worn=Read(hub.inventory.GetType(),hub.inventory,"SyncedBodyArmor");
                int currentArmor=worn!=null?Convert.ToInt32(worn):InventorySystem.Items.Armor.BodyArmorUtils.TryGetBodyArmor(hub.inventory,out var bodyArmor)?(int)bodyArmor.ItemTypeId:(int)ItemType.None;
                foreach(var set in sets)
                {
                    bool armor=false;
                    if(Read(set.GetType(),set,"ArmorTypes") is IEnumerable types)
                        foreach(var t in types)if(Convert.ToInt32(t)==currentArmor)armor=true;
                    if(!armor)continue;
                    if(Read(set.GetType(),set,"SimpleModelElements") is IEnumerable elements)
                        foreach(var e in elements)if(e is UnityEngine.Component c && (skin.transform==c.transform||skin.transform.IsChildOf(c.transform)))return true;
                }
                return false;
            }
            // Vanilla CE hides renderer visibility for its owner, without changing the
            // selected skin's activeSelf. Respect inactive skin/LOD branches below the root.
            for(var t=skin.transform;t!=null&&t!=model;t=t.parent)if(!t.gameObject.activeSelf)return false;
            return true;
        }
        public static Transform FindBone(Animator animator,HumanBodyBones bone)
        {
            if(animator==null)return null;
            if(animator.isHuman)return animator.GetBoneTransform(bone);
            string name=bone.ToString();
            switch(bone)
            {
                case HumanBodyBones.LeftUpperArm:name="LeftArm";break;case HumanBodyBones.RightUpperArm:name="RightArm";break;
                case HumanBodyBones.LeftLowerArm:name="LeftForeArm";break;case HumanBodyBones.RightLowerArm:name="RightForeArm";break;
                case HumanBodyBones.LeftUpperLeg:name="LeftUpLeg";break;case HumanBodyBones.RightUpperLeg:name="RightUpLeg";break;
                case HumanBodyBones.LeftLowerLeg:name="LeftLeg";break;case HumanBodyBones.RightLowerLeg:name="RightLeg";break;
                case HumanBodyBones.Chest:name="Spine2";break;
            }
            if(name.Contains("Proximal")||name.Contains("Intermediate")||name.Contains("Distal"))
                name=name.Replace("Left","LeftHand").Replace("Right","RightHand").Replace("Little","Pinky").Replace("Proximal","1").Replace("Intermediate","2").Replace("Distal","3");
            return Array.Find(animator.GetComponentsInChildren<Transform>(true),t=>t.name=="mixamorig:"+name);
        }
    }
}
