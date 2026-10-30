using UnityEngine;

namespace PlayerRoles.PlayableScps.Scp3114
{
    /// <summary>Only on the dressed corpse: clothing follows the native corpse's visibility fade.</summary>
    public sealed class Scp3114DressRagdollFade : MonoBehaviour
    {
        public Renderer SkeletonRenderer;
        public Renderer[] OutfitRenderers;
        private MaterialPropertyBlock _block;
        private static readonly int Fade = Shader.PropertyToID("_Fade");

        private void LateUpdate()
        {
            if (SkeletonRenderer == null || SkeletonRenderer.sharedMaterial == null) return;
            float value = SkeletonRenderer.sharedMaterial.HasProperty(Fade)
                ? SkeletonRenderer.sharedMaterial.GetFloat(Fade) : 1f;
            if (_block == null) _block = new MaterialPropertyBlock();
            foreach (Renderer item in OutfitRenderers)
            {
                if (item == null) continue;
                item.GetPropertyBlock(_block);
                _block.SetFloat(Fade, value);
                item.SetPropertyBlock(_block);
            }
        }
    }
}
