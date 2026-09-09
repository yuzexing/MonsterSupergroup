Shader "HellMaiden/Presentation/Ovid Summon Shadow"
{
    Properties
    {
        [PerRendererData] _MainTex ("Original Shadow Sprite", 2D) = "white" {}
        _Color ("Original Tint", Color) = (1,1,1,1)
        _ShadowUVRect ("Original Sprite UV Rectangle", Vector) = (0,0,1,1)
        _ShadowFadeStart ("Approximate Long Axis Fade Start", Range(0,1)) = 0.16
        _ShadowFadeEnd ("Approximate Long Axis Fade End", Range(0,1)) = 1
        // Retained source values document the missing SSU effect. Its original coordinate/noise
        // equation was not exported, so these are deliberately not presented as an exact reimplementation.
        _EnableDirectionalAlphaFade ("Source SSU Enabled", Float) = 1
        _DirectionalAlphaFadeFade ("Source SSU Fade", Float) = 2.61
        _DirectionalAlphaFadeRotation ("Source SSU Rotation", Float) = 263
        _DirectionalAlphaFadeWidth ("Source SSU Width", Float) = 0.84
        _DirectionalAlphaFadeNoiseFactor ("Source SSU Noise Factor", Float) = 0.2
        _DirectionalAlphaFadeNoiseScale ("Source SSU Noise Scale", Vector) = (0.3,0.3,0,0)
        _DirectionalAlphaFadeInvert ("Source SSU Invert", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "CanUseSpriteAtlas"="True" }
        Cull Off
        ZWrite Off
        // Alpha-aware multiplication: alpha=0 leaves destination RGB unchanged.
        Blend DstColor OneMinusSrcAlpha
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            fixed4 _Color;
            float4 _ShadowUVRect;
            float _ShadowFadeStart, _ShadowFadeEnd;
            struct Attributes { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct Varyings { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            Varyings vert(Attributes input)
            {
                Varyings output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.uv = input.uv;
                output.color = input.color * _Color;
                return output;
            }
            fixed4 frag(Varyings input) : SV_Target
            {
                fixed4 color = tex2D(_MainTex, input.uv) * input.color;
                // The original stretched Shadow sprite's local +X axis points away from the summon.
                // Approximate the unavailable directional fade over its last source-width fraction.
                float alongShadow = saturate((input.uv.x - _ShadowUVRect.x) / max(0.0001, _ShadowUVRect.z - _ShadowUVRect.x));
                float coverage = 1.0 - smoothstep(_ShadowFadeStart, max(_ShadowFadeStart + 0.0001, _ShadowFadeEnd), alongShadow);
                color.a *= coverage;
                // Fade before premultiplication. Fading only alpha after multiplying RGB brightens transparent pixels.
                color.rgb *= color.a;
                return color;
            }
            ENDCG
        }
    }
}
