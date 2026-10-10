Shader "Game/View/FallowWorldSprite"
{
    Properties
    {
        _MainTex ("Authored atlas", 2D) = "white" {}
        _InstanceColor ("Color", Color) = (1,1,1,1)
        _InstanceUv ("Atlas rectangle", Vector) = (0,0,1,1)
        _InstanceFlash ("Hit flash and edge style", Vector) = (0,0,1,0)
        _FallowContrast ("Surface contrast", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "FallowSprite"
            Tags { "LightMode"="Universal2D" }
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off
            ZWrite Off
            ZTest Always
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma instancing_options forcemaxcount:511
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float4 _MainTex_TexelSize;
            float _FallowContrast;
            TEXTURE2D(_FallowMask);
            SAMPLER(sampler_FallowMask);
            float4 _FallowMap;
            UNITY_INSTANCING_BUFFER_START(SpriteProperties)
                UNITY_DEFINE_INSTANCED_PROP(float4, _InstanceColor)
                UNITY_DEFINE_INSTANCED_PROP(float4, _InstanceUv)
                UNITY_DEFINE_INSTANCED_PROP(float4, _InstanceFlash)
            UNITY_INSTANCING_BUFFER_END(SpriteProperties)
            struct Attributes { float3 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float2 local : TEXCOORD1; float2 world : TEXCOORD2; UNITY_VERTEX_INPUT_INSTANCE_ID };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.positionCS = TransformObjectToHClip(input.positionOS);
                float4 rect = UNITY_ACCESS_INSTANCED_PROP(SpriteProperties, _InstanceUv);
                float2 inset = _MainTex_TexelSize.xy * 0.5 * sign(rect.zw);
                output.uv = lerp(rect.xy + inset, rect.xy + rect.zw - inset, input.uv);
                output.local = input.uv;
                output.world = TransformObjectToWorld(input.positionOS).xy;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                half4 sample = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                half4 color = UNITY_ACCESS_INSTANCED_PROP(SpriteProperties, _InstanceColor);
                float4 style = UNITY_ACCESS_INSTANCED_PROP(SpriteProperties, _InstanceFlash);
                if (style.y > 0)
                {
                    float4 rect = UNITY_ACCESS_INSTANCED_PROP(SpriteProperties, _InstanceUv);
                    float2 low = min(rect.xy, rect.xy + rect.zw) + _MainTex_TexelSize.xy * 0.5;
                    float2 high = max(rect.xy, rect.xy + rect.zw) - _MainTex_TexelSize.xy * 0.5;
                    float2 step = max(_MainTex_TexelSize.xy * style.y, fwidth(input.uv) * style.w);
                    half inner = min(min(
                        SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, clamp(input.uv + float2(step.x, 0), low, high)).a,
                        SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, clamp(input.uv - float2(step.x, 0), low, high)).a), min(
                        SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, clamp(input.uv + float2(0, step.y), low, high)).a,
                        SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, clamp(input.uv - float2(0, step.y), low, high)).a));
                    sample.a = saturate(sample.a - inner);
                    sample.rgb = half3(1, 1, 1);
                }
                sample.a *= lerp(1, style.z, smoothstep(0.25, 0.65, input.local.y));
                sample.rgb = lerp(sample.rgb, half3(1,1,1), style.x);
                sample *= color;
                half4 restoration = SAMPLE_TEXTURE2D(_FallowMask, sampler_FallowMask, input.world * _FallowMap.xy);
                float2 paper = input.world * 29;
                half grain = frac(sin(dot(floor(paper), float2(12.9898, 78.233))) * 43758.5453);
                half coverage = smoothstep(.09 + grain * .12, .60 + grain * .13, restoration.a);
                half luminance = dot(sample.rgb, half3(.2126, .7152, .0722));
                half3 ash = lerp(.40 + luminance * .35, luminance, _FallowContrast) * half3(.91, .94, .95);
                half weight = max(.001, restoration.r + restoration.g + restoration.b);
                half3 pigment = (restoration.r * half3(.66, .79, .42) + restoration.g * half3(1, .78, .32) + restoration.b * half3(.92, .66, .40)) / weight;
                half3 restored = lerp(sample.rgb, sample.rgb * pigment * 1.3, .30);
                half3 restoredSoil = lerp(.22 + restored * .62, pigment * (.48 + luminance * .38), .58);
                restored = lerp(restoredSoil, restored, _FallowContrast);
                sample.rgb = lerp(ash, restored, coverage);
                return sample;
            }
            ENDHLSL
        }
    }
}
