Shader "Game/View/FallowGroundSprite"
{
    Properties
    {
        _MainTex ("Authored atlas", 2D) = "white" {}
        _InstanceColor ("Color", Color) = (1,1,1,1)
        _InstanceUv ("Atlas rectangle", Vector) = (0,0,1,1)
        _InstanceFlash ("Hit flash and edge style", Vector) = (0,0,1,0)
        _FallowContrast ("Surface contrast", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        UsePass "Game/View/FallowWorldSprite/FALLOWSPRITE"
    }
}
