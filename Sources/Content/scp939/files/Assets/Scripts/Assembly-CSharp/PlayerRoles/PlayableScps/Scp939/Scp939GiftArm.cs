using System;
using System.Collections.Generic;
using UnityEngine;
using PlayerRoles.FirstPersonControl.Thirdperson;

namespace PlayerRoles.PlayableScps.Scp939
{
    // A gift owns only this chain, after the normal Animator/skin drivers have run.
    // All frames are measured from the actual rig/bindposes, including 939's extra wrist.
    public sealed class Scp939GiftArm
    {
        public readonly Animator Animator;
        public readonly Transform Shoulder, Upper, Lower, Wrist, Hand;
        public readonly bool Creature;
        public readonly float Reach;
        private readonly Transform[] _digits;
        private readonly Transform[] _children;
        private readonly int[] _digitIndex, _segment;
        private readonly Vector3 _palmForward, _palmNormal, _palmCentre;
        private Vector3 _hinge;
        private Quaternion _lowerRest, _wristRest;
        private readonly List<Transform> _owned = new List<Transform>();
        private Quaternion[] _base;
        private Quaternion[] _before;
        private bool _posed;
        private readonly List<Transform> _twists = new List<Transform>();
        private readonly List<Quaternion> _twistFrames = new List<Quaternion>();
        private readonly List<float> _twistWeights = new List<float>();
        private readonly List<KeyValuePair<Transform, Transform>> _duplicates = new List<KeyValuePair<Transform, Transform>>();
        private readonly UnityEngine.Component _wearable;

        public bool Valid => Upper != null && Lower != null && Hand != null && Reach > .1f && _base != null && _digits != null;
        public Vector3 Palm => Hand.TransformPoint(_palmCentre);
        public Vector3 PalmNormal => Hand.TransformDirection(_palmNormal).normalized;
        public Quaternion PalmFrame => Quaternion.LookRotation(Hand.TransformDirection(_palmForward), PalmNormal);
        public float ElbowDegrees => Vector3.Angle(Upper.position - Lower.position, Hand.position - Lower.position);

        public Scp939GiftArm(Animator animator, bool creature)
        {
            Animator = animator; Creature = creature;
            if (animator == null) return;
            _wearable = Scp939ModCompatibility.Wearable(animator);
            var map = new Dictionary<string, Transform>();
            foreach (Transform t in animator.GetComponentsInChildren<Transform>(true))
                if (!map.ContainsKey(t.name)) map.Add(t.name, t);
            Transform Find(string n) => map.TryGetValue(n, out var t) ? t : null;
            Transform Human(HumanBodyBones b, string fallback) => animator.isHuman ? animator.GetBoneTransform(b) : Find(fallback);
            if (creature)
            {
                Shoulder = Find("DEF-Shoulder.L"); Upper = Find("DEF-UpperArm.L");
                Lower = Find("DEF-LowerArm.L"); Wrist = Find("DEF-Wrist.L"); Hand = Find("DEF-Hand.L");
            }
            else
            {
                Shoulder = Human(HumanBodyBones.RightShoulder, "mixamorig:RightShoulder");
                Upper = Human(HumanBodyBones.RightUpperArm, "mixamorig:RightArm");
                Lower = Human(HumanBodyBones.RightLowerArm, "mixamorig:RightForeArm");
                Hand = Human(HumanBodyBones.RightHand, "mixamorig:RightHand"); Wrist = Hand;
            }
            if (Upper == null || Lower == null || Hand == null || Wrist == null) return;
            Reach = Vector3.Distance(Upper.position, Lower.position) +
                Vector3.Distance(Lower.position, Wrist.position) + (Wrist != Hand ? Vector3.Distance(Wrist.position, Hand.position) : 0f);
            var digits = new List<Transform>(); var children = new List<Transform>();
            var index = new List<int>(); var segments = new List<int>();
            string[] names = creature ? new[] { "A", "B", "C", "Thumb" } : new[] { "Index", "Middle", "Ring", "Little", "Thumb" };
            for (int d = 0; d < names.Length; d++)
                for (int s = 0; s < 3; s++)
                {
                    Transform joint, child;
                    if (creature)
                    {
                        string stem = names[d] == "Thumb" ? "DEF-Thumb.L.00" : "DEF-Finger." + names[d] + ".L.00";
                        joint = Find(stem + (s + 1));
                        child = Find(stem + (s + 2));
                    }
                    else
                    {
                        string part = s == 0 ? "Proximal" : s == 1 ? "Intermediate" : "Distal";
                        string next = s == 0 ? "Intermediate" : "Distal";
                        joint = Human((HumanBodyBones)Enum.Parse(typeof(HumanBodyBones), "Right" + names[d] + part), "mixamorig:RightHand" + (names[d] == "Little" ? "Pinky" : names[d]) + (s + 1));
                        child = s < 2 ? Human((HumanBodyBones)Enum.Parse(typeof(HumanBodyBones), "Right" + names[d] + next), "mixamorig:RightHand" + (names[d] == "Little" ? "Pinky" : names[d]) + (s + 2)) : null;
                    }
                    if (joint == null) continue;
                    digits.Add(joint); children.Add(child); index.Add(d); segments.Add(s);
                }
            _digits = digits.ToArray(); _children = children.ToArray(); _digitIndex = index.ToArray(); _segment = segments.ToArray();
            Transform a = creature ? Find("DEF-Finger.A.L.001") : Human(HumanBodyBones.RightIndexProximal, "mixamorig:RightHandIndex1");
            Transform b = creature ? Find("DEF-Finger.B.L.001") : Human(HumanBodyBones.RightMiddleProximal, "mixamorig:RightHandMiddle1");
            Transform c = creature ? Find("DEF-Finger.C.L.001") : Human(HumanBodyBones.RightLittleProximal, "mixamorig:RightHandPinky1");
            if (a == null || b == null || c == null) return;
            _palmForward = Hand.InverseTransformDirection((b.position - Hand.position).normalized);
            // Across A->C is mirrored on the left creature hand.
            Vector3 dorsal = Vector3.Cross(a.position - c.position, b.position - Hand.position).normalized * (creature ? -1f : 1f);
            _palmNormal = Hand.InverseTransformDirection(-dorsal);
            _palmCentre = Hand.InverseTransformPoint(Vector3.Lerp(Hand.position, (a.position + b.position + c.position) / 3f, .58f));
            Add(Shoulder); Add(Upper); Add(Lower); if (Wrist != Hand) Add(Wrist); Add(Hand);
            foreach (var t in _digits) Add(t);
            ReadBindFrames(animator);
            _base = new Quaternion[_owned.Count];
            _before = new Quaternion[_owned.Count];
            foreach (var t in map.Values)
            {
                int cut = t.name.IndexOf("__H14S", StringComparison.Ordinal);
                if (cut > 0 && map.TryGetValue(t.name.Substring(0, cut), out var original))
                    _duplicates.Add(new KeyValuePair<Transform, Transform>(original, t));
            }
        }

        private void Add(Transform t) { if (t != null && !_owned.Contains(t)) _owned.Add(t); }

        private void ReadBindFrames(Animator animator)
        {
            _lowerRest = Lower.localRotation;
            _wristRest = Quaternion.Inverse(Lower.rotation) * Hand.rotation;
            foreach (var skin in animator.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var bones = skin.bones; var mesh = skin.sharedMesh; if (mesh == null) continue;
                int u = Array.IndexOf(bones, Upper), l = Array.IndexOf(bones, Lower), h = Array.IndexOf(bones, Hand), w = Array.IndexOf(bones, Wrist);
                if (u < 0 || l < 0 || h < 0 || w < 0) continue;
                var binds = mesh.bindposes;
                if (Mathf.Max(u, l, h, w) >= binds.Length) continue;
                Matrix4x4 ub = binds[u].inverse, lb = binds[l].inverse, hb = binds[h].inverse, wb = binds[w].inverse;
                Vector3 up = ub.MultiplyPoint3x4(Vector3.zero), low = lb.MultiplyPoint3x4(Vector3.zero), end = (Wrist == Hand ? hb : wb).MultiplyPoint3x4(Vector3.zero);
                _hinge = ub.inverse.MultiplyVector(Vector3.Cross(low - up, end - low).normalized).normalized;
                _lowerRest = (ub.inverse * lb).rotation;
                _wristRest = (lb.inverse * hb).rotation;
                foreach (var bone in bones)
                {
                    if (bone == null || !(bone.name == "ForearmTwist1.R" || bone.name == "ForearmTwist2.R")) continue;
                    int i = Array.IndexOf(bones, bone);
                    _twists.Add(bone); _twistFrames.Add((lb.inverse * binds[i].inverse).rotation);
                    _twistWeights.Add(bone.name.EndsWith("1.R", StringComparison.Ordinal) ? .3f : .7f); Add(bone);
                }
                break;
            }
        }

        public void CaptureBase()
        {
            if (!Valid || _base == null) return;
            for (int i = 0; i < _owned.Count; i++) _base[i] = _owned[i].localRotation;
            _posed = true;
        }

        // Called in early Update, BEFORE the normal animation evaluation. Stops procedural accumulation
        // on culled/offscreen models without freezing or replacing their Animator.
        public void RestoreBase()
        {
            if (!_posed) return;
            for (int i = 0; i < _owned.Count; i++) if (_owned[i] != null) _owned[i].localRotation = _base[i];
            _posed = false;
        }

        public void Pose(Vector3 palmTarget, Vector3 fingers, Vector3 palmNormal, Vector3 bodyForward,
                         Vector3 bodyRight, float grip, float weight, float curlDegrees)
        {
            if (!Valid || weight <= 0f) return;
            fingers.Normalize(); palmNormal = Vector3.ProjectOnPlane(palmNormal, fingers).normalized;
            if (palmNormal.sqrMagnitude < .1f) return;
            Quaternion palmRotation = Quaternion.LookRotation(fingers, palmNormal) *
                Quaternion.Inverse(Quaternion.LookRotation(_palmForward, _palmNormal));
            for (int i = 0; i < _owned.Count; i++) _before[i] = _owned[i].localRotation;
            if (Shoulder != null)
            {
                Vector3 clavicle = Upper.position - Shoulder.position;
                Vector3 engaged = clavicle + bodyForward * (Reach * .10f) + Vector3.up * (Reach * .035f);
                Shoulder.rotation = Quaternion.RotateTowards(Quaternion.identity, Quaternion.FromToRotation(clavicle, engaged), 14f) * Shoulder.rotation;
            }
            Vector3 outward = bodyRight * (Creature ? -1f : 1f);
            Vector3 pole = outward * .65f - Vector3.up * .65f - bodyForward * .15f;
            // Palm offset is in the actual hand's scaled coordinates, not a hand-position patch.
            for (int pass = 0; pass < 3; pass++)
            {
                Hand.rotation = palmRotation;
                Vector3 target = palmTarget - (Palm - Hand.position);
                Solve(target, pole);
                Vector3 forearm = (Hand.position - Lower.position).normalized;
                fingers = Vector3.RotateTowards(forearm, fingers, (Creature ? 38f : 42f) * Mathf.Deg2Rad, 0f).normalized;
                palmNormal = Vector3.ProjectOnPlane(palmNormal, fingers).normalized;
                palmRotation = Quaternion.LookRotation(fingers, palmNormal) *
                    Quaternion.Inverse(Quaternion.LookRotation(_palmForward, _palmNormal));
            }
            Hand.rotation = palmRotation;
            FixHinge(pole);
            Quaternion twist = ForearmTwist();
            Quaternion handRotation = Hand.rotation;
            if (Creature)
            {
                Quaternion wristFrame = twist * Wrist.rotation;
                Lower.rotation = Quaternion.Slerp(Quaternion.identity, twist, .35f) * Lower.rotation;
                Wrist.rotation = wristFrame;
                Hand.rotation = handRotation;
            }
            else
                for (int i = 0; i < _twists.Count; i++)
                    _twists[i].rotation = Quaternion.Slerp(Quaternion.identity, twist, _twistWeights[i]) * Lower.rotation * _twistFrames[i];
            PoseDigits(fingers, palmNormal, grip, curlDegrees);
            for (int i = 0; i < _owned.Count; i++) _owned[i].localRotation = Quaternion.Slerp(_before[i], _owned[i].localRotation, weight);
            SyncDuplicates();
        }

        private void Solve(Vector3 target, Vector3 pole)
        {
            // Preserve the two-piece forearm's straight centreline. Never translate/scale a joint.
            if (Creature && Wrist != null)
            {
                Vector3 a = Wrist.position - Lower.position, b = Hand.position - Wrist.position;
                Wrist.rotation = Quaternion.FromToRotation(b, a) * Wrist.rotation;
            }
            float aLen = Vector3.Distance(Upper.position, Lower.position), bLen = Vector3.Distance(Lower.position, Hand.position);
            Vector3 offset = target - Upper.position; float distance = offset.magnitude;
            if (distance < .0001f) return;
            // Soft bend at maximum reach, and at least a 32 degree inside elbow at minimum reach.
            float max = Mathf.Sqrt(aLen * aLen + bLen * bLen + 2f * aLen * bLen * Mathf.Cos(18f * Mathf.Deg2Rad));
            float min = Mathf.Sqrt(aLen * aLen + bLen * bLen - 2f * aLen * bLen * Mathf.Cos(32f * Mathf.Deg2Rad));
            distance = Mathf.Clamp(distance, min, max);
            Vector3 dir = offset.normalized;
            Vector3 bend = Vector3.ProjectOnPlane(pole, dir).normalized;
            if (bend.sqrMagnitude < .1f) bend = Vector3.ProjectOnPlane(Vector3.down, dir).normalized;
            float along = (aLen * aLen + distance * distance - bLen * bLen) / (2f * distance);
            Vector3 elbow = Upper.position + dir * along + bend * Mathf.Sqrt(Mathf.Max(0f, aLen * aLen - along * along));
            Aim(Upper, Lower.position - Upper.position, elbow - Upper.position);
            Aim(Lower, Hand.position - Lower.position, Upper.position + dir * distance - Lower.position);
        }

        private void FixHinge(Vector3 pole)
        {
            if (_hinge.sqrMagnitude < .1f) return;
            Vector3 end = Hand.position, elbow = Lower.position;
            Quaternion hand = Hand.rotation;
            Vector3 axis = (elbow - Upper.position).normalized, plane = Vector3.Cross(axis, end - elbow);
            if (plane.sqrMagnitude < 1e-8f) return;
            Upper.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(Upper.TransformDirection(_hinge), plane, axis), axis) * Upper.rotation;
            Lower.localRotation = _lowerRest;
            Solve(end, elbow - Upper.position);
            Hand.rotation = hand;
        }

        private Quaternion ForearmTwist()
        {
            Vector3 axis = (Hand.position - Lower.position).normalized;
            Quaternion delta = Hand.rotation * Quaternion.Inverse(Lower.rotation * _wristRest);
            Vector3 v = Vector3.Project(new Vector3(delta.x, delta.y, delta.z), axis);
            Quaternion q = new Quaternion(v.x, v.y, v.z, delta.w);
            float length = Mathf.Sqrt(Quaternion.Dot(q, q));
            return length > .00001f ? new Quaternion(q.x / length, q.y / length, q.z / length, q.w / length) : Quaternion.identity;
        }

        private void PoseDigits(Vector3 forward, Vector3 palm, float grip, float curl)
        {
            for (int i = 0; i < _digits.Length; i++)
            {
                Transform t = _digits[i]; int segment = _segment[i], digit = _digitIndex[i];
                Vector3 along = _children[i] != null ? _children[i].position - t.position : t.up;
                float angle = Mathf.Lerp(4f + segment * 5f, curl * (segment == 0 ? .48f : segment == 1 ? 1.05f : 1.48f), grip) * Mathf.Deg2Rad;
                Vector3 across = Vector3.Cross(forward, palm);
                Vector3 wanted;
                if (Creature && digit == 3)
                {
                    // 939 has a separately named opposable thumb, not a fourth parallel finger.
                    // Open it outward, then oppose A/B/C around the object as the grip closes.
                    Vector3 open = forward * .70f + across * .65f - palm * .08f;
                    Vector3 closed = forward * .35f - across * .65f + palm * (.48f + segment * .13f);
                    wanted = Vector3.Lerp(open, closed, grip).normalized;
                }
                else if (!Creature && digit == 4)
                    wanted = (forward * .25f - across * .75f + palm * Mathf.Lerp(-.05f, .6f, grip)).normalized;
                else
                {
                    float spread = (digit - (Creature ? 1f : 1.5f)) * Mathf.Lerp(.16f, .035f, grip);
                    wanted = (forward * Mathf.Cos(angle) + palm * Mathf.Sin(angle) + across * spread).normalized;
                }
                Aim(t, along, wanted);
            }
        }

        private static void Aim(Transform t, Vector3 from, Vector3 to)
        {
            if (from.sqrMagnitude > 1e-9f && to.sqrMagnitude > 1e-9f)
                t.rotation = Quaternion.FromToRotation(from, to) * t.rotation;
        }

        public void SyncDuplicates()
        {
            foreach (var pair in _duplicates) if (pair.Key != null && pair.Value != null)
                pair.Value.SetPositionAndRotation(pair.Key.position, pair.Key.rotation);
            Scp939ModCompatibility.MatchWearable(_wearable);
        }
    }
}
