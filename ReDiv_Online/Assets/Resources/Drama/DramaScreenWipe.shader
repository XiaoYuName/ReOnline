// 剧情全屏过场的「竖条」/「百叶窗」遮罩。
//
// 数值和分档全部照抄原工程的 ScreenEff（它是在 OnPostRender 里用
// Graphics.DrawTexture 画 10 个矩形，没有 Shader）：
//   竖条   —— 10 条竖直条带，每条从左边缘长出；相邻两条错开启动，
//             第 j 条在 j*_Stagger 开始、历时 _Span（都是总时长的比例）
//   百叶窗 —— 10 条水平横带，全部同步；偶数行贴左、奇数行贴右交错咬合，
//             揭开时左右反过来，所以撤离方向和进入方向相反
//
// 覆盖率用屏幕 UV 算而不是 sprite UV：遮罩是全屏的，这样既和原工程的
// 屏幕像素口径一致，也不受这张 Image 有没有贴图、贴图在图集哪个位置影响。
//
// 放在 Resources 下是为了保证进包 —— 这份 Shader 不被任何场景/预制体引用，
// 材质是运行时 new 出来的（见 FadeController），不放这儿会被裁掉。
Shader "XFramework/UI/DramaScreenWipe"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _Progress ("进度 0-1", Range(0, 1)) = 0
        [Enum(VenetianBlind,0,Comb,1)] _Mode ("样式", Float) = 0
        [Toggle] _Reveal ("揭开(而不是盖上)", Float) = 0
        _Count ("条数", Float) = 10
        _Stagger ("竖条错峰间隔", Float) = 0.0625
        _Span ("竖条单条历时", Float) = 0.3125

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255

        _ColorMask ("Color Mask", Float) = 15
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
                float4 screenPos     : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            fixed4 _Color;
            float4 _ClipRect;

            float _Progress;
            float _Mode;
            float _Reveal;
            float _Count;
            float _Stagger;
            float _Span;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);
                OUT.screenPos = ComputeScreenPos(OUT.vertex);
                OUT.color = v.color * _Color;
                return OUT;
            }

            // 竖条：把屏幕横向切成 _Count 段，每段自己算进度（错峰），
            // 盖上时黑块贴段的左边缘长出，揭开时残留黑块贴右边缘缩回去。
            float CoverageVenetianBlind(float2 uv, float t)
            {
                float n = max(_Count, 1);
                float x = uv.x * n;
                float col = floor(x);
                float fx = x - col;

                float p = saturate((t - col * _Stagger) / max(_Span, 1e-4));
                float v = 1 - p;   // 揭开时的剩余覆盖量，1 → 0

                return lerp(step(fx, p), step(1 - v, fx), _Reveal);
            }

            // 百叶窗：横向切成 _Count 段，所有横带同步伸缩，
            // 只有"从哪边长出来"按奇偶交错；揭开时奇偶互换。
            float CoverageComb(float2 uv, float t)
            {
                float n = max(_Count, 1);
                // 行号从屏幕【上】往下数 —— 原工程用的是 y 向下的屏幕坐标，
                // uv.y 是自下而上的，不翻过来整片梳齿会错开一行
                float odd = fmod(floor((1 - uv.y) * n), 2);

                float v = lerp(t, 1 - t, _Reveal);
                float fromLeft = step(uv.x, v);
                float fromRight = step(1 - v, uv.x);

                // 盖上：偶数行贴左；揭开：奇数行贴左
                float leftRow = lerp(1 - odd, odd, _Reveal);
                return lerp(fromRight, fromLeft, leftRow);
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                float2 uv = IN.screenPos.xy / max(IN.screenPos.w, 1e-6);

                float coverage = _Mode < 0.5
                    ? CoverageVenetianBlind(uv, _Progress)
                    : CoverageComb(uv, _Progress);

                half4 color = half4(IN.color.rgb, IN.color.a * coverage);

                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                return color;
            }
        ENDCG
        }
    }
}
