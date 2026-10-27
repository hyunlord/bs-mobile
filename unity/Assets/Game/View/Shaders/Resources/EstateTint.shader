// Estate map restoration tint (#375). The vertex colour carries the state instead of a colour so every estate sprite shares one
// material and still batches: r = saturation, g = brightness, b = light (1 - darkness), a = opacity; white draws the art as is.
// Darkness follows the approved M2 RIM review formula: luminance L * (0.24, 0.27, 0.29) + (9, 10, 13) / 255.
Shader "Game/UI/EstateTint"
{
    Properties
    {
        [PerRendererData] _MainTex ("Art", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _EstateGround ("Pixel ground restoration", Float) = 0
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Alpha Clip", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            struct Attributes { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; float3 grid : TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 vertex : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; float4 local : TEXCOORD1; float3 grid : TEXCOORD2; UNITY_VERTEX_OUTPUT_STEREO };
            sampler2D _MainTex;
            fixed4 _Color, _TextureSampleAdd;
            float4 _MainTex_ST, _ClipRect;
            float _EstateGround;
            float _EstateCornerRadius, _EstateLightSpread;
            int _EstateOpenCount;
            float4 _EstateOpenAreas[32];
            fixed4 _EstateClosed;
            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.local = input.vertex; output.vertex = UnityObjectToClipPos(input.vertex);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex); output.color = input.color; output.grid = input.grid; return output;
            }
            fixed4 Frag(Varyings input) : SV_Target
            {
                fixed4 sample = tex2D(_MainTex, input.uv) + _TextureSampleAdd;
                if (_EstateGround > .5)
                {
                    float distance = 10000;
                    for (int i = 0; i < _EstateOpenCount; i++)
                    {
                        float4 area = _EstateOpenAreas[i];
                        float2 halfSize = (area.zw - area.xy) * .5;
                        float radius = min(halfSize.x, halfSize.y);
                        if (_EstateCornerRadius > 0) radius = min(radius, _EstateCornerRadius);
                        float2 q = abs(input.grid.xy - (area.xy + area.zw) * .5) - halfSize + radius;
                        distance = min(distance, length(max(q, 0)) + min(max(q.x, q.y), 0) - radius);
                    }
                    input.color.rgb = lerp(float3(1,1,1), _EstateClosed.rgb, smoothstep(-.5, 2.5, distance - _EstateLightSpread));
                }
                fixed3 source = sample.rgb;
                fixed luma = dot(sample.rgb, fixed3(.2126, .7152, .0722));
                fixed3 tinted = lerp(luma.xxx, sample.rgb, input.color.r) * input.color.g;
                fixed3 dark = luma * fixed3(.24, .27, .29) + fixed3(9, 10, 13) / 255.0;
                sample.rgb = lerp(dark, tinted, input.color.b);
                // Above-night landmarks retain contour and texture, never original-colour river banks.
                // The shared material batches these cool silhouettes with other estate sprites.
                if (input.grid.z > .5)
                {
                    sample.rgb = luma * fixed3(.65, .8, 1) * input.color.g;
                    // Source hue identifies water only; the dark overlay never restores its original colour.
                    if (input.grid.z > 1.5)
                    {
                        fixed water = smoothstep(.005, .035, min(source.g - source.r, source.b - source.r));
                        fixed3 deepWater = lerp(fixed3(.022, .055, .09), fixed3(.09, .19, .25), luma * luma);
                        sample.rgb = lerp(sample.rgb, deepWater, water);
                    }
                }
                sample.a *= input.color.a;
                #ifdef UNITY_UI_CLIP_RECT
                sample.a *= UnityGet2DClipping(input.local.xy, _ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(sample.a - .001);
                #endif
                return sample;
            }
            ENDCG
        }
    }
    Fallback "UI/Default"
}
