using System;
using System.Collections.Generic;
using UnityEngine;

namespace PlayerRoles.PlayableScps.Scp3114
{

public static class Scp3114MaterialTemplates
{
	public class MaterialPair
	{
		public Material Original;

		public Material Disguise;

		public Material Reveal;

		public Material FromType(Scp3114MaterialType type)
		{
			return type switch
			{
				Scp3114MaterialType.Original => Original, 
				Scp3114MaterialType.Disguise => Disguise, 
				Scp3114MaterialType.Reveal => Reveal, 
				_ => throw new NotImplementedException("Unknown material type"), 
			};
		}
	}

	private static readonly string[] TextureNamesToCopy = new string[3] { "_BaseColorMap", "_NormalMap", "_EmissionMap" };

	private static readonly string[] ColorNamesToCopy = new string[1] { "_Emission" };

	private static readonly Dictionary<Material, MaterialPair> GeneratedMaterialPairs = new Dictionary<Material, MaterialPair>();

	private static bool _cacheSet;

	private static int[] _textureIdsToCopy;

	private static int[] _colorIdsToCopy;

	private static Material _disguiseTemplate;

	private static Material _revealTemplate;

	public static MaterialPair GetTemplatePair(Material original)
	{
		if (!GeneratedMaterialPairs.TryGetValue(original, out var value))
		{
			ValidateCache();
			value = new MaterialPair
			{
				Original = original,
				Disguise = GenerateNewMaterial(original, _disguiseTemplate),
				Reveal = GenerateNewMaterial(original, _revealTemplate)
			};
			GeneratedMaterialPairs[original] = value;
		}
		return value;
	}

	public static Material GetVariantTemplate(Material original, Scp3114MaterialType matType)
	{
		return GetTemplatePair(original).FromType(matType);
	}

	private static void ValidateCache()
	{
		if (!_cacheSet)
		{
			if (!RoleTypeId.Scp3114.TryGetRoleTemplate<Scp3114Role>(out var result))
			{
				throw new InvalidOperationException("Unable to cache material - SCP-3114 role template not found.");
			}
			if (!result.SubroutineModule.TryGetSubroutine<Scp3114FakeModelManager>(out var subroutine))
			{
				throw new InvalidOperationException("Unable to cache material - Model manager not found.");
			}
			_disguiseTemplate = subroutine.DisguiseMaterialTemplate;
			_revealTemplate = subroutine.RevealMaterialTemplate;
			CachePropertyIds(TextureNamesToCopy, ref _textureIdsToCopy);
			CachePropertyIds(ColorNamesToCopy, ref _colorIdsToCopy);
			_cacheSet = true;
		}
		static void CachePropertyIds(string[] names, ref int[] ids)
		{
			ids = new int[names.Length];
			for (int i = 0; i < names.Length; i++)
			{
				ids[i] = Shader.PropertyToID(names[i]);
			}
		}
	}

    private static Material GenerateNewMaterial(Material original, Material template)
    {
        Material material = new Material(template);
        CopyTexture("_BaseColorMap", "_MainTex");
        CopyTexture("_NormalMap", "_BumpMap");
        CopyTexture("_EmissionMap", "_EmissionMap");
        Color baseColor = original.HasProperty("_BaseColor") ? original.GetColor("_BaseColor") : original.HasProperty("_Color") ? original.GetColor("_Color") : Color.white;
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", baseColor);
        if (material.HasProperty("_Color")) material.SetColor("_Color", baseColor);
        Color emission = original.HasProperty("_EmissionColor") ? original.GetColor("_EmissionColor") : original.HasProperty("_Emission") ? original.GetColor("_Emission") : Color.black;
        if (material.HasProperty("_Emission")) material.SetColor("_Emission", emission);
        if (material.HasProperty("_EmissionColor")) material.SetColor("_EmissionColor", emission);
        return material;
        void CopyTexture(string native, string legacy)
        {
            string source = original.HasProperty(native) && original.GetTexture(native) != null ? native : legacy;
            if (!original.HasProperty(source)) return;
            Texture texture = original.GetTexture(source);
            if (material.HasProperty(native))
            {
                material.SetTexture(native, texture);
                material.SetTextureScale(native, original.GetTextureScale(source));
                material.SetTextureOffset(native, original.GetTextureOffset(source));
            }
            if (legacy != native && material.HasProperty(legacy)) material.SetTexture(legacy, texture);
        }
    }
}
}
