Shader "UI/RasterRoad"
{
    Properties
    {
        [PerRendererData] _MainTex ("赛道纹理", 2D) = "white" {}
        _Color ("整体颜色", Color) = (1, 1, 1, 1)
        _SkyColor ("天空颜色", Color) = (0.38, 0.72, 1, 1)
        _GroundColor ("地面颜色", Color) = (0.32, 0.82, 0.26, 1)
        _RoadColor ("道路颜色", Color) = (0.42, 0.42, 0.4, 1)
        _ShoulderColor ("路肩颜色", Color) = (0.06, 0.04, 0.55, 1)
        _LineColor ("中线颜色", Color) = (1, 1, 1, 1)

        _Speed ("滚动速度", Float) = 1
        _Curve ("弯道强度", Range(-2, 2)) = 0
        _RoadWidth ("道路宽度", Range(0.1, 3)) = 1
        _ShoulderWidth ("路肩宽度", Range(0, 1)) = 0.22
        _Horizon ("地平线高度", Range(0, 1)) = 0.42
        _Perspective ("透视强度", Range(0.1, 4)) = 1.25
        _LineWidth ("中线宽度", Range(0.001, 0.2)) = 0.035
        _DashLength ("虚线长度", Range(0.01, 1)) = 0.28
        _DashGap ("虚线间隔", Range(0.01, 1)) = 0.34
        _PixelSteps ("光栅行数", Range(0, 240)) = 96
        _UseRoadTexture ("混合赛道纹理", Range(0, 1)) = 0

        [HideInInspector] _StencilComp ("模板测试", Float) = 8
        [HideInInspector] _Stencil ("模板编号", Float) = 0
        [HideInInspector] _StencilOp ("模板操作", Float) = 0
        [HideInInspector] _StencilWriteMask ("模板写入遮罩", Float) = 255
        [HideInInspector] _StencilReadMask ("模板读取遮罩", Float) = 255
        [HideInInspector] _ColorMask ("颜色遮罩", Float) = 15
        [HideInInspector] [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("使用透明裁剪", Float) = 0
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
            fixed4 _SkyColor;
            fixed4 _GroundColor;
            fixed4 _RoadColor;
            fixed4 _ShoulderColor;
            fixed4 _LineColor;
            float _Speed;
            float _Curve;
            float _RoadWidth;
            float _ShoulderWidth;
            float _Horizon;
            float _Perspective;
            float _LineWidth;
            float _DashLength;
            float _DashGap;
            float _PixelSteps;
            float _UseRoadTexture;
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

            fixed4 frag(v2f input) : SV_Target
            {
                float2 uv = input.texcoord;

                if (_PixelSteps > 1.0)
                {
                    uv.y = floor(uv.y * _PixelSteps) / _PixelSteps;
                }

                fixed4 color = _SkyColor;

                if (uv.y > _Horizon)
                {
                    color = _SkyColor;
                }
                else
                {
                    float roadY = saturate((_Horizon - uv.y) / max(_Horizon, 0.0001));
                    float nearAmount = saturate(roadY);
                    float depth = pow(max(nearAmount, 0.001), _Perspective);
                    float invDepth = 1.0 / max(depth, 0.035);

                    float scroll = _Time.y * _Speed;
                    float curveOffset = _Curve * depth * depth * 0.65;
                    float roadCenter = 0.5 + curveOffset;

                    float halfRoadWidth = _RoadWidth * depth * 0.5;
                    float halfShoulderWidth = halfRoadWidth + _ShoulderWidth * depth;
                    float localX = uv.x - roadCenter;
                    float absX = abs(localX);

                    color = _GroundColor;

                    float shoulderMask = step(absX, halfShoulderWidth);
                    color = lerp(color, _ShoulderColor, shoulderMask);

                    if (absX < halfRoadWidth)
                    {
                        float roadU = localX / max(halfRoadWidth, 0.0001) * 0.5 + 0.5;
                        float roadV = invDepth * 0.22 + scroll;
                        fixed4 texColor = tex2D(_MainTex, float2(roadU, roadV)) + _TextureSampleAdd;

                        color = lerp(_RoadColor, texColor, saturate(texColor.a * _UseRoadTexture));

                        float dashCycle = max(_DashLength + _DashGap, 0.0001);
                        float dashPos = frac(roadV / dashCycle) * dashCycle;
                        float dashMask = step(dashPos, _DashLength);
                        float lineMask = step(abs(roadU - 0.5), _LineWidth);
                        color = lerp(color, _LineColor, dashMask * lineMask);
                    }
                }

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
