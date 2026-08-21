// 只显示图片的边缘轮廓：按精灵 alpha 做边缘检测，主体镂空，只留一圈描边。
// 用法：用本 Shader 建一个 Material，赋给 Image.material 即可（不需要改图集/贴图）。
//   _OutlineWidth 是像素宽度，轮廓骑在 alpha 边界上（内外各占一半）；
//   _FillAlpha 默认 0 = 纯轮廓，调大可以让原图以半透明打底。
// 注意：轮廓要向外扩散,素材四周需要留出透明边;Image 勾了 Use Sprite Mesh(Tight) 会把外半圈裁掉。
Shader "XFramework/UI/OutlineOnly"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _OutlineColor ("Outline Color", Color) = (1,1,1,1)
        _OutlineWidth ("Outline Width (px)", Range(0,16)) = 2
        _AlphaThreshold ("Alpha Threshold", Range(0.01,1)) = 0.1
        _FillAlpha ("Fill Alpha (0=只显示轮廓)", Range(0,1)) = 0

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
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex        : SV_POSITION;
                fixed4 color         : COLOR;
                float2 texcoord      : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _MainTex_TexelSize;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            fixed4 _OutlineColor;
            float _OutlineWidth;
            float _AlphaThreshold;
            float _FillAlpha;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);
                OUT.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                OUT.color = v.color * _Color;
                return OUT;
            }

            // 环形 16 方向采样，返回邻域 alpha 的 (最大值, 最小值)。
            // 邻域里同时存在不透明和透明像素 => 该点落在轮廓上。
            half2 RingAlphaMinMax(float2 uv, float2 o)
            {
                half maxA = 0;
                half minA = 1;

                #define SAMPLE_RING(dx, dy) \
                    { half a = tex2D(_MainTex, uv + float2(o.x * (dx), o.y * (dy))).a; \
                      maxA = max(maxA, a); minA = min(minA, a); }

                // 4 正交
                SAMPLE_RING( 1,  0)
                SAMPLE_RING(-1,  0)
                SAMPLE_RING( 0,  1)
                SAMPLE_RING( 0, -1)
                // 4 对角 (45°)
                SAMPLE_RING( 0.70711,  0.70711)
                SAMPLE_RING(-0.70711,  0.70711)
                SAMPLE_RING( 0.70711, -0.70711)
                SAMPLE_RING(-0.70711, -0.70711)
                // 8 半角 (22.5° / 67.5°)，填满圆周让轮廓更顺滑
                SAMPLE_RING( 0.92388,  0.38268)
                SAMPLE_RING(-0.92388,  0.38268)
                SAMPLE_RING( 0.92388, -0.38268)
                SAMPLE_RING(-0.92388, -0.38268)
                SAMPLE_RING( 0.38268,  0.92388)
                SAMPLE_RING(-0.38268,  0.92388)
                SAMPLE_RING( 0.38268, -0.92388)
                SAMPLE_RING(-0.38268, -0.92388)

                #undef SAMPLE_RING

                return half2(maxA, minA);
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                half4 sprite = tex2D(_MainTex, IN.texcoord) + _TextureSampleAdd;

                float2 o = _MainTex_TexelSize.xy * max(_OutlineWidth, 0);
                half2 ring = RingAlphaMinMax(IN.texcoord, o);
                // 自身也算进最大值：比轮廓还细的部件不会整条消失
                half maxA = max(ring.x, sprite.a);
                half minA = ring.y;

                // 邻域有不透明像素 且 有透明像素 => 边缘（阈值附近做平滑，抗锯齿）
                half hasSolid = smoothstep(_AlphaThreshold * 0.5, _AlphaThreshold, maxA);
                half hasEmpty = 1 - smoothstep(_AlphaThreshold * 0.5, _AlphaThreshold, minA);
                half edge = saturate(hasSolid * hasEmpty);

                half4 fill = half4(sprite.rgb, sprite.a * _FillAlpha);
                half lineA = edge * _OutlineColor.a;

                half4 color;
                color.a = max(fill.a, lineA);
                // 轮廓压在打底的原图之上
                color.rgb = lerp(fill.rgb, _OutlineColor.rgb, lineA / max(color.a, 0.0001));

                color *= IN.color;

                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
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
