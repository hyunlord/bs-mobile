Shader "Game/View/WorldSprite"
{
    Properties
    {
        _MainTex ("Authored atlas", 2D) = "white" {}
        _InstanceColor ("Color", Color) = (1,1,1,1)
        _InstanceUv ("Atlas rectangle", Vector) = (0,0,1,1)
        _InstanceFlash ("Hit flash and edge style", Vector) = (0,0,1,0)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
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
            UNITY_INSTANCING_BUFFER_START(SpriteProperties)
                UNITY_DEFINE_INSTANCED_PROP(float4, _InstanceColor)
                UNITY_DEFINE_INSTANCED_PROP(float4, _InstanceUv)
                UNITY_DEFINE_INSTANCED_PROP(float4, _InstanceFlash)
            UNITY_INSTANCING_BUFFER_END(SpriteProperties)
            struct Attributes { float3 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float2 local : TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID };
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
                return sample * color;
            }
            ENDHLSL
        }
    }
}
