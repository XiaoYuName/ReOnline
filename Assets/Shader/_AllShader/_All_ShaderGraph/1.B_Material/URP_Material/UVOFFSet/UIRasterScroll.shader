// 光栅滚动（Raster Scroll / 逐扫描线横向偏移）
//
// 原理：红白机图像逐行输出，程序在每行输出完毕的瞬间改一次背景 X 寄存器，
// 于是每一横行的画面被推开不同距离，一张「直的」底图就被掰成了弯道/波纹/立体路面。
// 本质是「横向偏移量 = 行号的函数」，在 fragment shader 里每个像素天然知道自己的 v，
// 直接 u += f(v) 即可，无需任何中断/时序，且可自由量化成 N 条扫描线还原颗粒感。
//
// 偏移量由四项相加得到（都可单独关掉，值为 0 即不生效）：
//   1. 偏移表 _OffsetTex —— 一维查找表，一个纹素 = 一条扫描线，等价于红白机的 HDMA 偏移表，
//                            可由 UIRasterScroll 组件从 AnimationCurve 烘焙，或每帧上传实时算的弯道
//   2. 弯道 _CurveAmount —— 按「离地平线的远近」加权的整体推移，喂一张直路图即得弯道
//   3. 波动 _WaveAmp    —— 正弦，水面倒影 / 热浪 / 旗帜抖动
//   4. 滚动 _ScrollX    —— 随时间推移，远处可用 _ScrollDepthScale 减速做视差
//
// 用法：挂在 Image / RawImage / Sprite 上。底图请用 Point 过滤 + 关 Mipmap（像素风），
//       打了图集的 Sprite 需要 _UVRect 指向该 Sprite 在图集里的区域（UIRasterScroll 组件会自动填）。
Shader "XFramework/UI/RasterScroll"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        [Header(Scanline)]
        _ScanLineCount ("扫描线条数 (0=不量化)", Float) = 240

        [Header(Offset Table)]
        [NoScaleOffset] _OffsetTex ("逐行偏移表 (R 通道, 0.5=不偏)", 2D) = "grey" {}
        _OffsetScale ("偏移表幅度", Range(-1, 1)) = 0

        [Header(Curve)]
        _CurveAmount ("弯道强度", Range(-1, 1)) = 0
        _Horizon ("地平线高度 (v)", Range(0.001, 1)) = 1
        _DepthPow ("远近权重指数", Range(0.1, 6)) = 2

        [Header(Wave)]
        _WaveAmp ("波动幅度", Range(0, 0.5)) = 0
        _WaveFreq ("波动频率 (周期数)", Float) = 3
        _WaveSpeed ("波动速度", Float) = 1

        [Header(Scroll)]
        _ScrollX ("横向滚动速度", Float) = 0
        _ScrollY ("纵向滚动速度", Float) = 0
        _ScrollDepthScale ("远处滚动倍率", Range(0, 4)) = 1

        [Header(Center Line)]
        [Toggle(_CENTERLINE_ON)] _CenterLine ("启用路面虚线", Float) = 0
        _LineColor ("虚线颜色", Color) = (1,1,1,1)
        _LineWidth ("虚线宽度 (近处)", Range(0, 0.1)) = 0.008
        _LineDensity ("虚线密度", Float) = 0.3
        _LineSpeed ("虚线推进速度 (Track 模式下由 _LinePhase 接管)", Float) = 1
        _LineRatio ("实线占比", Range(0.05, 0.95)) = 0.5
        [HideInInspector] _LinePhase ("虚线相位 (由 UIRasterScroll 按里程写入)", Float) = 0
        [HideInInspector] _DepthFloor ("深度下限 (=1/最远深度，由 UIRasterScroll 写入)", Float) = 0.0833
        _LineFade ("近地平线淡出起点", Range(0, 1)) = 0.9
        _LineErase ("擦除原图虚线宽度 (0=不擦)", Range(0, 0.1)) = 0

        [Header(Sampling)]
        [KeywordEnum(Repeat, Clamp, Mirror, Clip)] _Wrap ("越界方式", Float) = 0
        _UVRect ("图集内 UV 区域 (xy=起点 zw=尺寸)", Vector) = (0,0,1,1)

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
            #pragma target 3.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #pragma shader_feature_local _WRAP_REPEAT _WRAP_CLAMP _WRAP_MIRROR _WRAP_CLIP
            #pragma shader_feature_local _CENTERLINE_ON

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
            sampler2D _OffsetTex;

            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;

            float _ScanLineCount;
            float _OffsetScale;
            float _CurveAmount;
            float _Horizon;
            float _DepthPow;
            float _WaveAmp;
            float _WaveFreq;
            float _WaveSpeed;
            float _ScrollX;
            float _ScrollY;
            float _ScrollDepthScale;
            float4 _UVRect;
            fixed4 _LineColor;
            float _LineWidth;
            float _LineDensity;
            float _LineSpeed;
            float _LineRatio;
            float _LinePhase;
            float _DepthFloor;
            float _LineFade;
            float _LineErase;

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

            // 越界处理：只在 Sprite 局部 UV 空间内做，避免图集串图
            float2 WrapUV(float2 uv)
            {
                #if defined(_WRAP_CLAMP) || defined(_WRAP_CLIP)
                    return saturate(uv);
                #elif defined(_WRAP_MIRROR)
                    float2 t = frac(uv * 0.5) * 2.0;
                    return min(t, 2.0 - t);
                #else // _WRAP_REPEAT
                    return frac(uv);
                #endif
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                // 图集 UV -> Sprite 局部 UV（0..1），后续所有计算都在局部空间
                float2 luv = (IN.texcoord - _UVRect.xy) / max(_UVRect.zw, 1e-6);

                // 量化到 N 条扫描线：取所在行的中心 v，同一行内偏移量完全一致，
                // 这一步是「红白机味」的来源，_ScanLineCount<1 时退化为逐像素连续偏移
                float rowV = _ScanLineCount >= 1.0
                    ? (floor(luv.y * _ScanLineCount) + 0.5) / _ScanLineCount
                    : luv.y;

                // 远近权重：0=画面底部(最近) 1=地平线(最远)
                float depth01 = saturate(rowV / _Horizon);
                float w = pow(depth01, _DepthPow);

                // 偏移表：一个纹素一条扫描线，rowV 直接当索引；
                // 行号是阶梯函数，必须用 tex2Dlod 显式指定 LOD，否则行交界处导数爆炸会选到糊掉的 mip
                float table = tex2Dlod(_OffsetTex, float4(rowV, 0.5, 0, 0)).r * 2.0 - 1.0;

                float offsetX = 0.0;
                offsetX += table * _OffsetScale;
                offsetX += _CurveAmount * w;
                offsetX += sin((rowV * _WaveFreq + _Time.y * _WaveSpeed) * UNITY_TWO_PI) * _WaveAmp;
                offsetX += _Time.y * _ScrollX * lerp(1.0, _ScrollDepthScale, w);

                float2 suv = float2(luv.x + offsetX, luv.y + _Time.y * _ScrollY);

                #if defined(_WRAP_CLIP)
                    if (any(suv < 0.0) || any(suv > 1.0))
                        return fixed4(0, 0, 0, 0);
                #endif

                float2 wuv = WrapUV(suv);

                // near：1=最近(画面底部) 0=地平线。路面上的一切（虚线宽度、间距）都随它收缩。
                // 必须和 C# 端 BuildTrack 里的软饱和曲线逐字一致——中线的透视要和路面几何完全同源，
                // 缝纫拖尾才能按同一套公式算出严丝合缝的位置。_DepthFloor 由 UIRasterScroll 写入 (=1/最远深度)
                float near = 1.0 - depth01;
                float nearSoft = _DepthFloor + near * near / (near + _DepthFloor);

                #if defined(_CENTERLINE_ON)
                    // 采样前先把「原图里烘死的中央虚线」挤出取样范围：
                    // 中线走廊内的像素改去走廊外侧取色，取到的就是干净的路面灰
                    float side = wuv.x - 0.5;
                    float lineX = abs(side);
                    wuv.x = 0.5 + sign(side) * max(lineX, _LineErase * nearSoft);
                #endif

                float2 atlasUV = wuv * _UVRect.zw + _UVRect.xy;

                // 用「未偏移」的 UV 求导数：偏移量逐行跳变，直接采样会在行交界处误判 mip / 各向异性
                half4 tex = tex2Dgrad(_MainTex, atlasUV, ddx(IN.texcoord), ddy(IN.texcoord)) + _TextureSampleAdd;

                #if defined(_CENTERLINE_ON)
                    // 深度取 1/nearSoft，与偏移表同源；乘 metersPerDepth 就是「前方多少米」。
                    // 相位 = z*密度 + _LinePhase，而 _LinePhase 由 C# 按 Travel 每帧写入，
                    // 于是整条虚线严格锚定在赛道里程上：车速变化不会让虚线跳相，
                    // 拖尾也能直接算出每一段实线对应的世界里程。
                    // （_LineSpeed 留给不跑赛道的场合，Track 模式下由 C# 置 0）
                    float z = 1.0 / nearSoft;
                    float dash = step(frac(z * _LineDensity + _Time.y * _LineSpeed + _LinePhase), _LineRatio);

                    float halfW = _LineWidth * nearSoft;
                    float aa = max(fwidth(luv.x), 1e-5);   // 用连续 UV 求导，避开回绕接缝
                    float mask = 1.0 - smoothstep(halfW - aa, halfW + aa, lineX);

                    mask *= dash;
                    mask *= 1.0 - smoothstep(_LineFade, 1.0, depth01);  // 地平线附近虚线短于一个像素，淡出防摩尔纹
                    mask *= step(0.5, tex.a);                            // 只画在不透明的路面上

                    tex.rgb = lerp(tex.rgb, _LineColor.rgb, mask * _LineColor.a);
                #endif

                half4 color = tex * IN.color;

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
