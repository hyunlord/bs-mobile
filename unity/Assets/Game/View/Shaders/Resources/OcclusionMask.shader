// Review-only P5 measurement: redraws enemy sprites into the red channel and own weapon effects into the green channel of a
// small mask target (alpha coverage, max blend), so the CPU can count enemy pixels covered by effects. Never drawn in play.
Shader "Hidden/Game/OcclusionMask"
{
    Properties
    {
        _MainTex ("Atlas", 2D) = "white" {}
        _MaskChannel ("Channel", Vector) = (1,0,0,0)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Blend One One
            BlendOp Max
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
            float4 _MaskChannel;
            UNITY_INSTANCING_BUFFER_START(SpriteProperties)
                UNITY_DEFINE_INSTANCED_PROP(float4, _InstanceColor)
                UNITY_DEFINE_INSTANCED_PROP(float4, _InstanceUv)
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
                output.uv = rect.xy + input.uv * rect.zw;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                half4 color = UNITY_ACCESS_INSTANCED_PROP(SpriteProperties, _InstanceColor);
                half coverage = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).a * color.a;
                return _MaskChannel * coverage;
            }
            ENDHLSL
        }
    }
}
