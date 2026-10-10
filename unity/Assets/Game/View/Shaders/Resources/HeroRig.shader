Shader "Game/View/HeroRig"
{
    Properties
    {
        _MainTex ("Layered authored atlas", 2D) = "white" {}
        _InstanceColor ("Color", Color) = (1,1,1,1)
        _InstanceUv ("Atlas rectangle", Vector) = (0,0,1,1)
        _InstanceFlash ("Hit flash and edge style", Vector) = (0,0,1,0)
        _RigFrame ("Pose inside each sheet", Vector) = (0,.75,.25,.25)
        _RigUpperFrame ("Upper body pose inside each sheet", Vector) = (0,.75,.25,.25)
        _RigFarDelta ("Far foot continuous correction", Vector) = (0,0,0,0)
        _RigNearDelta ("Near foot continuous correction", Vector) = (0,0,0,0)
        _RigWarp ("Hip and full correction lines", Vector) = (.4609375,.1875,0,0)
        _RigCanvas ("Unpainted lower contact coverage", Vector) = (-.125,1.125,0,0)
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
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            float4 _MainTex_TexelSize, _RigFrame, _RigUpperFrame, _RigFarDelta, _RigNearDelta, _RigWarp, _RigCanvas;
            UNITY_INSTANCING_BUFFER_START(SpriteProperties)
                UNITY_DEFINE_INSTANCED_PROP(float4, _InstanceColor)
                UNITY_DEFINE_INSTANCED_PROP(float4, _InstanceUv)
                UNITY_DEFINE_INSTANCED_PROP(float4, _InstanceFlash)
            UNITY_INSTANCING_BUFFER_END(SpriteProperties)
            struct Attributes { float3 positionOS:POSITION; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS:SV_POSITION; float2 local:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            Varyings Vert(Attributes input)
            {
                Varyings output; UNITY_SETUP_INSTANCE_ID(input); UNITY_TRANSFER_INSTANCE_ID(input,output);
                input.positionOS.y=input.positionOS.y*_RigCanvas.y+_RigCanvas.x;
                output.positionCS=TransformObjectToHClip(input.positionOS);
                output.local=float2(input.uv.x,input.uv.y*_RigCanvas.y+_RigCanvas.x);return output;
            }
            float2 InverseWarp(float2 point,float2 delta)
            {
                float h=_RigWarp.x,f=_RigWarp.y,r=h-f;
                if(point.y>=h)return point;
                if(point.y<=f+delta.y)return point-delta;
                float y=(point.y-h*delta.y/r)/(1-delta.y/r);
                return float2(point.x-(h-y)/r*delta.x,y);
            }
            half4 Layer(float2 local,float2 sheet,float4 frame)
            {
                if(any(local<0)||any(local>1))return 0;
                float2 low=sheet+.5*frame.xy;
                float2 extent=.5*frame.zw;
                float2 uv=clamp(low+extent*local,low+_MainTex_TexelSize.xy*.5,low+extent-_MainTex_TexelSize.xy*.5);
                return SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,uv);
            }
            half4 Over(half4 under,half4 above)
            {
                return half4(above.rgb*above.a+under.rgb*(1-above.a),above.a+under.a*(1-above.a));
            }
            half4 Frag(Varyings input):SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                half4 sample=Layer(input.local,float2(0,.5),_RigUpperFrame);sample.rgb*=sample.a;
                sample=Over(sample,Layer(InverseWarp(input.local,_RigFarDelta.xy),float2(.5,.5),_RigFrame));
                sample=Over(sample,Layer(InverseWarp(input.local,_RigNearDelta.xy),float2(0,0),_RigFrame));
                sample=Over(sample,Layer(input.local,float2(.5,0),_RigUpperFrame));
                sample.rgb=sample.a>0?sample.rgb/sample.a:0;
                half4 color=UNITY_ACCESS_INSTANCED_PROP(SpriteProperties,_InstanceColor);
                float4 style=UNITY_ACCESS_INSTANCED_PROP(SpriteProperties,_InstanceFlash);
                sample.rgb=lerp(sample.rgb,half3(1,1,1),style.x);
                sample.a*=lerp(1,style.z,smoothstep(.25,.65,input.local.y));
                return sample*color;
            }
            ENDHLSL
        }
    }
}
