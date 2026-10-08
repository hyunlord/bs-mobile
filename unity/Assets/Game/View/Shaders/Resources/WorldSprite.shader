Shader "Game/View/WorldSprite"
{
    Properties
    {
        _MainTex ("Authored atlas", 2D) = "white" {}
        _InstanceColor ("Color", Color) = (1,1,1,1)
        _InstanceUv ("Atlas rectangle", Vector) = (0,0,1,1)
        _InstanceFlash ("Hit flash", Vector) = (0,0,0,0)
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
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.positionCS = TransformObjectToHClip(input.positionOS);
                float4 rect = UNITY_ACCESS_INSTANCED_PROP(SpriteProperties, _InstanceUv);
                float2 inset = _MainTex_TexelSize.xy * 0.5 * sign(rect.zw);
                output.uv = lerp(rect.xy + inset, rect.xy + rect.zw - inset, input.uv);
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                half4 sample = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                half4 color = UNITY_ACCESS_INSTANCED_PROP(SpriteProperties, _InstanceColor);
                sample.rgb = lerp(sample.rgb, half3(1,1,1), UNITY_ACCESS_INSTANCED_PROP(SpriteProperties, _InstanceFlash).x);
                return sample * color;
            }
            ENDHLSL
        }
    }
}
