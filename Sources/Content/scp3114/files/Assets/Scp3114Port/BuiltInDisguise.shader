Shader "Scp3114/BuiltInDisguise"
{
    Properties
    {
        _BaseColorMap ("Albedo", 2D) = "white" {}
        _MainTex ("Legacy Albedo", 2D) = "white" {}
        _NormalMap ("Normal", 2D) = "bump" {}
        _BumpMap ("Legacy Normal", 2D) = "bump" {}
        _BaseColor ("Tint", Color) = (1,1,1,1)
        _Color ("Legacy Tint", Color) = (1,1,1,1)
        _Emission ("Emission", Color) = (0,0,0,1)
        _EmissionMap ("Emission Map", 2D) = "white" {}
        _Smoothness ("Smoothness", Range(0,1)) = 0.22
        _Metallic ("Metallic", Range(0,1)) = 0
        _NormalScale ("Normal Scale", Range(0,2)) = 1
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.05
        _Fade ("Visibility", Range(0,1)) = 1
        _Progress ("Skin Progress", Range(0,1)) = 1
        _Reverse ("Reveal Skin", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        LOD 250
        Cull Back
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows addshadow
        #pragma target 3.0
        #include "UnityStandardUtils.cginc"
        sampler2D _BaseColorMap, _NormalMap, _EmissionMap;
        fixed4 _BaseColor, _Color, _Emission;
        half _Smoothness, _Metallic, _NormalScale, _Cutoff, _Fade, _Progress, _Reverse;
        struct Input { float2 uv_BaseColorMap; float3 worldPos; };
        float StableNoise(float3 worldPosition)
        {
            float3 cells = floor(worldPosition * 125.0);
            return frac(sin(dot(cells, float3(12.9898,78.233,37.719))) * 43758.5453);
        }
        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 sampled = tex2D(_BaseColorMap, IN.uv_BaseColorMap) * _BaseColor;
            float coverage = saturate(lerp(_Progress, 1.0-_Progress, saturate(_Reverse))) * saturate(_Fade);
            clip(coverage - 0.0001);
            clip(coverage - StableNoise(IN.worldPos));
            clip(sampled.a - _Cutoff);
            o.Albedo = sampled.rgb;
            o.Normal = UnpackScaleNormal(tex2D(_NormalMap, IN.uv_BaseColorMap), _NormalScale);
            o.Emission = tex2D(_EmissionMap, IN.uv_BaseColorMap).rgb * _Emission.rgb;
            o.Metallic = _Metallic;
            o.Smoothness = _Smoothness;
            o.Alpha = 1;
        }
        ENDCG
    }
    Fallback "Diffuse"
}
