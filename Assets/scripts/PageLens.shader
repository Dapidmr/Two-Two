Shader "Custom/PageLens"
{
    Properties
    {
        _GibberishTex ("Gibberish", 2D) = "white" {}
        _ReadableTex ("Readable", 2D) = "white" {}
        _LensUV ("Lens UV", Vector) = (0.5, 0.5, 0, 0)
        _LensRadius ("Lens Radius", Float) = 0.12
        _LensOn ("Lens On", Float) = 0
        _Aspect ("Aspect (lebar/tinggi)", Float) = 0.75
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_GibberishTex); SAMPLER(sampler_GibberishTex);
            TEXTURE2D(_ReadableTex);  SAMPLER(sampler_ReadableTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _LensUV;
                float _LensRadius;
                float _LensOn;
                float _Aspect;
            CBUFFER_END

            struct Attributes { float4 posOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 posCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.posCS = TransformObjectToHClip(IN.posOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 g = SAMPLE_TEXTURE2D(_GibberishTex, sampler_GibberishTex, IN.uv);
                half4 r = SAMPLE_TEXTURE2D(_ReadableTex, sampler_ReadableTex, IN.uv);
                float2 d = (IN.uv - _LensUV.xy) * float2(_Aspect, 1.0);
                float inside = (1.0 - smoothstep(_LensRadius * 0.9, _LensRadius, length(d))) * _LensOn;
                return lerp(g, r, inside);
            }
            ENDHLSL
        }
    }
}