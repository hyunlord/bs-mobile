Shader "Game/View/WorldShape"
{
    Properties
    {
        _InstanceColor ("Color", Color) = (1,1,1,1)
        _InstanceStyle ("Inner radius", Vector) = (-1,0,0,0)
        _WorldClip ("World clipping rectangle", Vector) = (0,0,0,0)
        _WorldClipEnabled ("World clipping enabled", Float) = 0
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
            float4 _WorldClip;
            float _WorldClipEnabled;
            UNITY_INSTANCING_BUFFER_START(ShapeProperties)
                UNITY_DEFINE_INSTANCED_PROP(float4, _InstanceColor)
                UNITY_DEFINE_INSTANCED_PROP(float4, _InstanceStyle)
            UNITY_INSTANCING_BUFFER_END(ShapeProperties)
            struct Attributes { float3 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 local : TEXCOORD0; float2 world : TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.local = input.positionOS.xy;
                output.world = TransformObjectToWorld(input.positionOS).xy;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                if (_WorldClipEnabled > .5)
                    clip(min(input.world - _WorldClip.xy, _WorldClip.zw - input.world));
                float innerRadius = UNITY_ACCESS_INSTANCED_PROP(ShapeProperties, _InstanceStyle).x;
                clip(length(input.local) - innerRadius);
                return UNITY_ACCESS_INSTANCED_PROP(ShapeProperties, _InstanceColor);
            }
            ENDHLSL
        }
    }
}
