Shader "UI/ForBackCenterLineY"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _Speed ("Line Scroll Speed", Float) = 0.25
        _LineCenter ("Line Center X", Range(0, 1)) = 0.5
        _LineWidth ("Line Width", Range(0, 1)) = 0.08
        _WhiteThreshold ("White Threshold", Range(0, 1)) = 0.85
        _RoadColor ("Road Fill Color", Color) = (0.42, 0.43, 0.41, 1)

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _TextureSampleAdd;
            fixed4 _Color;
            fixed4 _RoadColor;
            float _Speed;
            float _LineCenter;
            float _LineWidth;
            float _WhiteThreshold;
            float4 _ClipRect;

            v2f vert(appdata_t input)
            {
                v2f output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.worldPosition = input.vertex;
                output.vertex = UnityObjectToClipPos(output.worldPosition);
                output.texcoord = input.texcoord;
                output.color = input.color;
                return output;
            }

            float WhiteMask(fixed4 color)
            {
                float lowChannel = min(color.r, min(color.g, color.b));
                return smoothstep(_WhiteThreshold, 1.0, lowChannel) * color.a;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float2 uv = input.texcoord;
                fixed4 baseSample = tex2D(_MainTex, uv) + _TextureSampleAdd;

                float halfWidth = max(_LineWidth * 0.5, 0.0001);
                float lineArea = 1.0 - smoothstep(halfWidth, halfWidth + 0.01, abs(uv.x - _LineCenter));

                float baseLineMask = WhiteMask(baseSample) * lineArea;
                fixed4 color = lerp(baseSample, _RoadColor, baseLineMask);

                float2 lineUv = uv;
                lineUv.y = frac(lineUv.y + _Time.y * _Speed);
                fixed4 movingSample = tex2D(_MainTex, lineUv) + _TextureSampleAdd;
                float movingLineMask = WhiteMask(movingSample) * lineArea;

                color = lerp(color, movingSample, movingLineMask);
                color *= _Color * input.color;

                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(input.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif

                return color;
            }
            ENDCG
        }
    }
}
