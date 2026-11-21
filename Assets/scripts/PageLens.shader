Shader "Custom/PageLensSimple"
{
    Properties
    {
        _GibberishTex ("Gibberish", 2D) = "white" {}
        _ReadableTex  ("Readable", 2D) = "white" {}
        _Aspect       ("Aspect (width/height)", Float) = 0.71
        _LensUV       ("Lens UV", Vector) = (0.5, 0.5, 0, 0)
        _LensRadius   ("Lens Radius", Float) = 0.15
        _LensOn       ("Lens On", Float) = 0
        _TexST        ("Texture Tiling(xy) Offset(zw)", Vector) = (1, 1, 0, 0)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass
        {
            Name "Unlit"
            Tags { "LightMode"="UniversalForward" }
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_GibberishTex); SAMPLER(sampler_GibberishTex);
            TEXTURE2D(_ReadableTex);  SAMPLER(sampler_ReadableTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _GibberishTex_ST;
                float4 _ReadableTex_ST;
                float  _Aspect;
                float4 _LensUV;
                float  _LensRadius;
                float  _LensOn;
                float4 _TexST;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert (Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                // UV untuk texture (tiling + offset), UV asli tetap dipakai untuk posisi lensa
                float2 tuv = i.uv * _TexST.xy + _TexST.zw;

                half4 g = SAMPLE_TEXTURE2D(_GibberishTex, sampler_GibberishTex, tuv);
                half4 r = SAMPLE_TEXTURE2D(_ReadableTex,  sampler_ReadableTex,  tuv);

                float2 d = i.uv - _LensUV.xy;
                d.x *= _Aspect;
                float dist = length(d);

                float inside = (1.0 - smoothstep(_LensRadius - 0.005, _LensRadius, dist)) * _LensOn;

                return lerp(g, r, inside);
            }
            ENDHLSL
        }
    }
}