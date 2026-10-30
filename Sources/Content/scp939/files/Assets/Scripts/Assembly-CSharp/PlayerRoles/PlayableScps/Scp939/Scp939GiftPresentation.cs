using System;
using System.Collections.Generic;
using InventorySystem;
using InventorySystem.Items;
using Mirror;
using PlayerRoles.FirstPersonControl;
using PlayerRoles.FirstPersonControl.Thirdperson;
using UnityEngine;
using UnityEngine.Rendering;

namespace PlayerRoles.PlayableScps.Scp939
{
    [DefaultExecutionOrder(1200)]
    public sealed class Scp939GiftPresentation : MonoBehaviour
    {
        public const float ContactAt = 2.25f, TransferAt = 2.65f, ReturnAt = 3.0f, CompleteAt = 4.65f;
        public Scp939GiftArm GiverArm { get; private set; }
        public Scp939GiftArm ReceiverArm { get; private set; }
        public Transform Prop { get; private set; }
        public bool Transferred { get; private set; }
        public bool Settled { get; private set; }
        public float Elapsed { get; private set; }
        public Vector3 ContactCentre { get; private set; }
        public ItemType GiftType { get; private set; }
        public uint Sequence { get; private set; }
        public bool Preview;
        public ReferenceHub ReceiverHub => _receiver;
        public ReferenceHub GiverHub => _giver;
        private ReferenceHub _giver, _receiver;
        private PlayerRoleBase _giverRole, _receiverRole;
        private Transform _giverModel, _receiverModel;
        private double _start;
        private bool _accepted, _commit, _cancelled, _finished;
        private float _cancelAt;
        private GameObject _contents;
        private readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>();
        private readonly List<Renderer> _fpHidden = new List<Renderer>();
        private readonly List<bool> _fpWasHidden = new List<bool>();
        private readonly List<SkinnedMeshRenderer> _firstPerson = new List<SkinnedMeshRenderer>();
        private Vector3 _giverHome, _receiverHome;
        private Vector3 _size;
        private bool _flat;
        private Quaternion _meshOrientation;
        private Matrix4x4 _receiverGrip;
        private bool _haveReceiverGrip, _fpReleased;
        private GameObject _normalItemRoot;
        private bool _normalItemsWereActive;
        private HumanCharacterModel _humanModel;

        public static Scp939GiftPresentation StartFor(ReferenceHub scp, ReferenceHub giver,
                                                     Scp939GiftAnimationMessage message)
        {
            var receiverModel = ModelOf(scp); var giverModel = ModelOf(giver);
            if (receiverModel == null || giverModel == null ||
                !InventoryItemLoader.AvailableItems.TryGetValue(message.Type, out var item)) return null;
            var go = new GameObject("939 gift transfer " + message.Sequence);
            var result = go.AddComponent<Scp939GiftPresentation>();
            result._giver = giver; result._receiver = scp;
            result._giverRole = giver.roleManager.CurrentRole; result._receiverRole = scp.roleManager.CurrentRole;
            result.Sequence = message.Sequence; result._start = message.StartTime; result._accepted = message.Accepted;
            if (!result.Setup(giverModel, receiverModel, item)) { Destroy(go); return null; }
            var human = giverModel.GetComponent<HumanCharacterModel>();
            result._humanModel = human;
            result.HideNormalItem();
            if (giver.isLocalPlayer) result.CreateFirstPersonArm();
            return result;
        }

        public static GameObject ModelOf(ReferenceHub hub)
        {
            var role = hub != null ? hub.roleManager.CurrentRole as IFpcRole : null;
            return role?.FpcModule.CharacterModelInstance != null ? role.FpcModule.CharacterModelInstance.gameObject : null;
        }

        // Also used by the editor pose gate, with the actual production models and item prefab.
        public bool Setup(GameObject giverModel, GameObject receiverModel, ItemBase item)
        {
            _giverModel = giverModel.transform; _receiverModel = receiverModel.transform;
            GiverArm = new Scp939GiftArm(giverModel.GetComponentInChildren<Animator>(true), false);
            ReceiverArm = new Scp939GiftArm(receiverModel.GetComponentInChildren<Animator>(true), true);
            if (!GiverArm.Valid || !ReceiverArm.Valid || item == null || item.PickupDropModel == null) return false;
            GiftType = item.ItemTypeId;
            _giverHome = GiverArm.Palm; _receiverHome = ReceiverArm.Palm;
            return CreateProp(item);
        }

        private bool CreateProp(ItemBase item)
        {
            var root = new GameObject("Gift visual (no pickup or network identity)");
            root.transform.SetParent(transform, false); Prop = root.transform;
            _contents = new GameObject("Native pickup meshes"); _contents.transform.SetParent(Prop, false);
            var source = item.PickupDropModel.transform;
            var lodRenderers = new HashSet<Renderer>(); var allLodRenderers = new HashSet<Renderer>();
            foreach (var group in source.GetComponentsInChildren<LODGroup>(true))
            {
                var lods = group.GetLODs();
                for (int i = 0; i < lods.Length; i++) foreach (var r in lods[i].renderers)
                { allLodRenderers.Add(r); if (i == 0) lodRenderers.Add(r); }
            }
            bool gotBounds = false; var bounds = new Bounds(); int count = 0;
            foreach (var renderer in source.GetComponentsInChildren<Renderer>(true))
            {
                bool visible = renderer.enabled;
                for (Transform node = renderer.transform; node != null && node != source; node = node.parent)
                    visible &= node.gameObject.activeSelf;
                if (!visible || allLodRenderers.Contains(renderer) && !lodRenderers.Contains(renderer)) continue;
                Mesh mesh = null;
                if (renderer is MeshRenderer) mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                else if (renderer is SkinnedMeshRenderer skin && skin.sharedMesh != null)
                {
                    // Keep baked vertices in renderer-local space. The copied child below
                    // applies the source transform once; the default bake would apply its scale twice.
                    mesh = new Mesh(); skin.BakeMesh(mesh, true); _owned.Add(mesh);
                }
                if (mesh == null) continue;
                var child = new GameObject(renderer.name, typeof(MeshFilter), typeof(MeshRenderer));
                child.transform.SetParent(_contents.transform, false);
                Matrix4x4 matrix = source.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                child.transform.localPosition = matrix.GetColumn(3); child.transform.localRotation = matrix.rotation;
                child.transform.localScale = matrix.lossyScale;
                child.GetComponent<MeshFilter>().sharedMesh = mesh;
                var copy = child.GetComponent<MeshRenderer>(); copy.sharedMaterials = renderer.sharedMaterials;
                copy.shadowCastingMode = ShadowCastingMode.On;
                foreach (Vector3 corner in Corners(mesh.bounds))
                {
                    Vector3 p = matrix.MultiplyPoint3x4(corner);
                    if (!gotBounds) { bounds = new Bounds(p, Vector3.zero); gotBounds = true; } else bounds.Encapsulate(p);
                }
                count++;
            }
            if (!gotBounds || count == 0) return false;
            // Native pickup size is retained throughout. No scaled generic proxy/collider is spawned.
            _contents.transform.localPosition = -bounds.center;
            _size = bounds.size;
            _flat = item.ItemTypeId == ItemType.Medkit || item.Category == ItemCategory.Keycard;
            // Item-native longest axis is mapped once; anatomical hand frames are independent of it.
            Vector3 major = _size.x >= _size.y && _size.x >= _size.z ? Vector3.right : _size.y >= _size.z ? Vector3.up : Vector3.forward;
            _meshOrientation = Quaternion.FromToRotation(major, _flat ? Vector3.right : Vector3.up);
            Vector3 orientedSize = _meshOrientation * _size;
            _size = new Vector3(Mathf.Abs(orientedSize.x), Mathf.Abs(orientedSize.y), Mathf.Abs(orientedSize.z));
            return true;
        }

        private static IEnumerable<Vector3> Corners(Bounds b)
        {
            for (int i = 0; i < 8; i++) yield return b.center + Vector3.Scale(b.extents,
                new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
        }

        public void ConfirmTransfer() { _commit = true; }
        public void ConfirmFinish() { _finished = true; }
        public void RetireForReplacement()
        {
            // Stage 0 is authoritative: no older session for these participants is active.
            // Restore before the next presentation captures its starting pose. A deferred fade
            // would leave two solvers writing the same bones for the first frames of a repeat gift.
            GiverArm?.RestoreBase(); ReceiverArm?.RestoreBase(); ReleaseFirstPerson();
            gameObject.SetActive(false);
            Destroy(gameObject);
        }

        public void Cancel()
        {
            if (_cancelled) return;
            _cancelled = true; _cancelAt = Time.time;
        }

        private void Update()
        {
            GiverArm?.RestoreBase(); ReceiverArm?.RestoreBase();
            if (Preview) return;
            if (_giver == null || _receiver == null || _giver.roleManager.CurrentRole != _giverRole ||
                _receiver.roleManager.CurrentRole != _receiverRole) { Destroy(gameObject); return; }
            foreach (var renderer in _fpHidden) if (renderer != null) renderer.forceRenderingOff = true;
            if (_cancelled && Time.time - _cancelAt >= .4f) Destroy(gameObject);
        }

        private void LateUpdate()
        {
            if (Preview || !GiverArm.Valid || !ReceiverArm.Valid) return;
            // Native item holstering can keep the previous third-person model alive briefly.
            // The gift prop is the only visible copy, including when that item was selected.
            HideNormalItem();
            float time = Mathf.Max(0f, (float)(NetworkTime.time - _start));
            if (!_commit && time >= TransferAt) time = TransferAt - .001f;
            if (_finished && time >= CompleteAt) Settled = true;
            float fade = _cancelled ? 1f - Smooth((Time.time - _cancelAt) / .4f) : 1f;
            Evaluate(time, _accepted, fade);
            if (time >= CompleteAt) ReleaseFirstPerson();
        }

        public void Evaluate(float seconds, bool accepted, float weight = 1f)
        {
            Elapsed = seconds;
            GiverArm.CaptureBase(); ReceiverArm.CaptureBase();
            Vector3 forward = _receiverModel.position - _giverModel.position; forward.y = 0f; forward.Normalize();
            if (seconds >= CompleteAt) { forward = -_receiverModel.forward; forward.y = 0f; forward.Normalize(); }
            if (forward.sqrMagnitude < .5f) forward = _giverModel.forward;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            float reach = Mathf.Min(GiverArm.Reach, ReceiverArm.Reach);
            Vector3 centre = Vector3.Lerp(GiverArm.Upper.position, ReceiverArm.Upper.position, .5f) - Vector3.up * (reach * .33f);
            ContactCentre = centre;
            // Back of the human hand remains toward the camera for compact vertical items.
            Vector3 normal = _flat ? Vector3.up : right;
            Vector3 fingers = _flat ? forward : (forward * .65f + Vector3.up * .75f).normalized;
            float radius = _flat ? _size.y * .5f : _size.x * .5f;
            radius = Mathf.Max(.004f, radius);
            Vector3 humanContact = centre - normal * (radius + .018f);
            Vector3 scpContact = centre + normal * (radius + .024f);
            bool bottle = GiftType == ItemType.SCP207;
            if (bottle)
            {
                // Share the bottle, not the same patch of its surface: the human supports
                // its lower body while the longer creature digits close around the neck.
                float separation = Mathf.Clamp(_size.y * .20f, .035f, .075f);
                humanContact -= Vector3.up * separation;
                scpContact += Vector3.up * separation - normal * (radius * .32f);
            }
            if (_flat) scpContact += forward * (ReceiverArm.Reach * .10f) + Vector3.up * .018f;
            float offer = Smooth((seconds - .20f) / 1.25f);
            float receive = Smooth((seconds - 1.05f) / 1.2f);
            float receiverGrip = Smooth((seconds - ContactAt) / (TransferAt - ContactAt));
            float giverRelease = accepted ? Smooth((seconds - TransferAt) / .30f) : 0f;
            float withdraw = Smooth((seconds - ReturnAt) / 1.25f);
            float humanWeight = Smooth(seconds / .25f) * (1f - withdraw) * weight;
            float creatureWeight = Smooth((seconds - .85f) / .45f) * (accepted ? 1f : 1f - withdraw) * weight;
            Vector3 giverTarget = Vector3.Lerp(_giverHome + Vector3.up * (reach * .18f), humanContact, offer);
            Vector3 receivingTarget = Vector3.Lerp(_receiverHome, scpContact, receive);
            Vector3 cradle = ReceiverArm.Upper.position - Vector3.up * (ReceiverArm.Reach * .46f) - forward * (ReceiverArm.Reach * .36f);
            if (accepted) receivingTarget = Vector3.Lerp(receivingTarget, cradle, withdraw);
            float humanCurl = _flat ? 32f : GiftType == ItemType.GrenadeHE || GiftType == ItemType.GrenadeFlash ? 64f : 51f;
            float scpCurl = _flat ? 62f : bottle ? 74f : 55f;
            GiverArm.Pose(giverTarget, fingers, normal, forward, right, 1f - giverRelease, humanWeight, humanCurl);
            ReceiverArm.Pose(receivingTarget, _flat ? -forward : (-forward * .85f + Vector3.up * .5f).normalized,
                -normal, -forward, -right, receiverGrip * (accepted ? 1f : .35f), creatureWeight, scpCurl);

            Quaternion offerRotation = Quaternion.LookRotation(forward, Vector3.up) * _meshOrientation;
            if (seconds < TransferAt || !accepted)
            {
                Quaternion offset = Quaternion.Inverse(Quaternion.LookRotation(fingers, normal)) * offerRotation;
                Prop.SetPositionAndRotation(GiverArm.Palm + GiverArm.PalmNormal * (radius + .018f), GiverArm.PalmFrame * offset);
            }
            else
            {
                if (!_haveReceiverGrip)
                {
                    Quaternion offset = Quaternion.Inverse(Quaternion.LookRotation(fingers, normal)) * offerRotation;
                    Prop.SetPositionAndRotation(GiverArm.Palm + GiverArm.PalmNormal * (radius + .018f), GiverArm.PalmFrame * offset);
                    _receiverGrip = ReceiverArm.Hand.worldToLocalMatrix * Prop.localToWorldMatrix; _haveReceiverGrip = true;
                }
                Matrix4x4 pose = ReceiverArm.Hand.localToWorldMatrix * _receiverGrip;
                Prop.SetPositionAndRotation(pose.GetColumn(3), pose.rotation); Transferred = true;
            }
            // One prop, constant world scale, continuously follows the actual hand that owns it.
            foreach (var renderer in _firstPerson) if (renderer != null) renderer.forceRenderingOff = weight < .01f;
            if (!accepted && seconds >= CompleteAt) Destroy(gameObject);
        }

        private static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * t * (t * (6f * t - 15f) + 10f); }

        private void HideNormalItem()
        {
            if (_fpReleased) return;
            // The recovered native socket can be assigned after the role's first frame.
            if (_normalItemRoot == null && _humanModel != null && _humanModel.ItemSpawnpoint != null)
            {
                _normalItemRoot = _humanModel.ItemSpawnpoint.gameObject;
                _normalItemsWereActive = _normalItemRoot.activeSelf;
            }
            if (_normalItemRoot != null) _normalItemRoot.SetActive(false);
        }

        private void CreateFirstPersonArm()
        {
            // Use the SAME skinned shoulder/upperarm/forearm/hand, so world contact and FP cannot diverge.
            // Only arm triangles are copied. No floating hand or replacement character model.
            foreach (var skin in _giverModel.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!EligibleFirstPersonSkin(skin) || skin.sharedMesh == null ||
                    skin.name.Contains("LOD1") || skin.name.Contains("LOD2") || skin.name.Contains("LOD3")) continue;
                var preparedArm = Scp939GiftArmMeshes.Find(skin.sharedMesh);
                if (preparedArm != null) { CreateFirstPersonSkin(skin, preparedArm); continue; }
                if (!skin.sharedMesh.isReadable) continue;
                var mesh = skin.sharedMesh; var bones = skin.bones; var weights = mesh.boneWeights;
                var armBones = new bool[bones.Length];
                for (int i = 0; i < bones.Length; i++) armBones[i] = bones[i] != null &&
                    (bones[i] == GiverArm.Upper || bones[i].IsChildOf(GiverArm.Upper) || bones[i] == GiverArm.Shoulder);
                bool ArmVertex(int i)
                {
                    var w = weights[i]; float sum = 0f;
                    if (w.boneIndex0 < armBones.Length && armBones[w.boneIndex0]) sum += w.weight0;
                    if (w.boneIndex1 < armBones.Length && armBones[w.boneIndex1]) sum += w.weight1;
                    if (w.boneIndex2 < armBones.Length && armBones[w.boneIndex2]) sum += w.weight2;
                    if (w.boneIndex3 < armBones.Length && armBones[w.boneIndex3]) sum += w.weight3;
                    return sum > .65f;
                }
                Mesh armMesh = Instantiate(mesh); bool hasTriangles = false;
                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    int[] triangles = mesh.GetTriangles(s); var selected = new List<int>();
                    for (int t = 0; t < triangles.Length; t += 3)
                        if (ArmVertex(triangles[t]) && ArmVertex(triangles[t + 1]) && ArmVertex(triangles[t + 2]))
                        { selected.Add(triangles[t]); selected.Add(triangles[t + 1]); selected.Add(triangles[t + 2]); }
                    armMesh.SetTriangles(selected, s); hasTriangles |= selected.Count > 0;
                }
                if (!hasTriangles) { Destroy(armMesh); continue; }
                _owned.Add(armMesh);
                CreateFirstPersonSkin(skin, armMesh);
            }
            var hands = SharedHandsController.Singleton;
            if (hands != null && hands.Hands != null)
                foreach (var renderer in hands.Hands.GetComponentsInChildren<Renderer>(true))
                { _fpHidden.Add(renderer); _fpWasHidden.Add(renderer.forceRenderingOff); }
        }

        private bool EligibleFirstPersonSkin(SkinnedMeshRenderer skin)
        {
            return Scp939ModCompatibility.EligibleSkin(_giverModel, skin, _giver);
        }

        private void CreateFirstPersonSkin(SkinnedMeshRenderer skin, Mesh armMesh)
        {
                var go = new GameObject("Gift FP complete right arm");
                // Source wearable GameObjects are inactive for their owner. The arm has an independent
                // renderer under the model root, while retaining the same native skinning coordinates.
                go.transform.SetParent(_giverModel, false);
                Matrix4x4 matrix = _giverModel.worldToLocalMatrix * skin.transform.localToWorldMatrix;
                go.transform.localPosition = matrix.GetColumn(3); go.transform.localRotation = matrix.rotation; go.transform.localScale = matrix.lossyScale;
                var renderer = go.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = armMesh; renderer.bones = skin.bones; renderer.rootBone = skin.rootBone;
                renderer.localBounds = skin.localBounds; renderer.updateWhenOffscreen = true;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                Material[] materials = skin.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    if (materials[i] == null) continue;
                    materials[i] = new Material(materials[i]);
                    if (materials[i].HasProperty("_Fade")) materials[i].SetFloat("_Fade", 1f);
                    _owned.Add(materials[i]);
                }
                renderer.sharedMaterials = materials; _firstPerson.Add(renderer);
        }

        private void OnDestroy()
        {
            GiverArm?.RestoreBase(); ReceiverArm?.RestoreBase();
            ReleaseFirstPerson();
            foreach (var owned in _owned) if (owned != null)
            { if (Application.isPlaying) Destroy(owned); else DestroyImmediate(owned); }
        }

        private void ReleaseFirstPerson()
        {
            if (_fpReleased) return;
            _fpReleased = true;
            if (_normalItemRoot != null) _normalItemRoot.SetActive(_normalItemsWereActive);
            _normalItemRoot = null;
            for (int i = 0; i < _fpHidden.Count; i++) if (_fpHidden[i] != null) _fpHidden[i].forceRenderingOff = _fpWasHidden[i];
            foreach (var skin in _firstPerson) if (skin != null) Destroy(skin.gameObject);
            _fpHidden.Clear(); _fpWasHidden.Clear(); _firstPerson.Clear();
        }
    }
}
