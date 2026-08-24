Shader "Cygames/VariantCardShader"
{
    Properties
    {
        _MainTex ("Base (RGB)", 2D) = "white" {}
        _Color ("Hide: Color", Color) = (1, 1, 1, 1)

        [HideInInspector] _FlagFront1Flash ("Hide: Flag (Front 1 Flash)", Float) = 0
        [HideInInspector] _FlagFront1Distortion ("Hide: Flag (Front 1 Distortion)", Float) = 0
        [HideInInspector] _FlagFront1RenderType ("Hide: Flag (Front 1 Render Type)", Float) = 0
        [HideInInspector] _FlagFront1MoveType ("Hide: Flag (Front 1 Move Type)", Float) = 0
        [HideInInspector] _FlagFront2Flash ("Hide: Flag (Front 2 Flash)", Float) = 0
        [HideInInspector] _FlagFront2Distortion ("Hide: Flag (Front 2 Distortion)", Float) = 0
        [HideInInspector] _FlagFront2RenderType ("Hide: Flag (Front 2 Render Type)", Float) = 0
        [HideInInspector] _FlagFront2MoveType ("Hide: Flag (Front 2 Move Type)", Float) = 0
        [HideInInspector] _FlagBack1Flash ("Hide: Flag (Back 1 Flash)", Float) = 0
        [HideInInspector] _FlagBack1Distortion ("Hide: Flag (Back 1 Distortion)", Float) = 0
        [HideInInspector] _FlagBack1RenderType ("Hide: Flag (Back 1 Render Type)", Float) = 0
        [HideInInspector] _FlagBack1MoveType ("Hide: Flag (Back 1 Move Type)", Float) = 0
        [HideInInspector] _FlagBack2Flash ("Hide: Flag (Back 2 Flash)", Float) = 0
        [HideInInspector] _FlagBack2Distortion ("Hide: Flag (Back 2 Distortion)", Float) = 0
        [HideInInspector] _FlagBack2RenderType ("Hide: Flag (Back 2 Render Type)", Float) = 0
        [HideInInspector] _FlagBack2MoveType ("Hide: Flag (Back 2 Move Type)", Float) = 0
        [HideInInspector] _FlagDistortionMoveType ("Hide: Flag (Distortion Move Type)", Float) = 0

        [Space] _MaskTex ("Mask (RGB)", 2D) = "white" {}

        [Space] _Front1Color ("Color", Color) = (0.5, 0.5, 0.5, 1)
        _Front1Tex ("Texture (RGB)", 2D) = "black" {}
        _Front1ScrollU ("Move Type: Scroll U", Range(-10, 10)) = 0
        _Front1ScrollV ("Move Type: Scroll V", Range(-10, 10)) = 0
        _Front1ScrollAngle ("Move Type: Scroll Angle", Range(-180, 180)) = 0
        _Front1Rotate ("Move Type: Rotate", Range(-10, 10)) = 0
        _Front1Spiral ("Move Type: Spiral", Range(-10, 10)) = 0
        _Front1FlashSpeed ("Flash: Speed", Range(0.1, 100)) = 1
        _Front1FlashMin ("Flash: Min", Range(0, 0.999)) = 0
        _Front1FlashMax ("Flash: Max", Range(0.001, 1)) = 1
        [Space][Enum(Additive,1,Subtract,2,Transparent,3)] _Front1_RenderType ("Render Type", Float) = 1
        [Enum(Fixed,1,Scroll,2,Rotate,3,Polar,4)] _Front1_MoveType ("Move Type", Float) = 1
        [Enum(Off,0,On,1)] _Front1_Flash ("Flash", Float) = 0
        [Enum(Off,0,On,1)] _Front1_Distortion ("Distortion", Float) = 0

        [Space] _Front2Color ("Color", Color) = (0.5, 0.5, 0.5, 1)
        _Front2Tex ("Texture (RGB)", 2D) = "black" {}
        _Front2ScrollU ("Move Type: Scroll U", Range(-10, 10)) = 0
        _Front2ScrollV ("Move Type: Scroll V", Range(-10, 10)) = 0
        _Front2ScrollAngle ("Move Type: Scroll Angle", Range(-180, 180)) = 0
        _Front2Rotate ("Move Type: Rotate", Range(-10, 10)) = 0
        _Front2Spiral ("Move Type: Spiral", Range(-10, 10)) = 0
        _Front2FlashSpeed ("Flash: Speed", Range(0.1, 100)) = 1
        _Front2FlashMin ("Flash: Min", Range(0, 0.999)) = 0
        _Front2FlashMax ("Flash: Max", Range(0.001, 1)) = 1
        [Space][Enum(Additive,1,Subtract,2,Transparent,3)] _Front2_RenderType ("Render Type", Float) = 1
        [Enum(Fixed,1,Scroll,2,Rotate,3,Polar,4)] _Front2_MoveType ("Move Type", Float) = 1
        [Enum(Off,0,On,1)] _Front2_Flash ("Flash", Float) = 0
        [Enum(Off,0,On,1)] _Front2_Distortion ("Distortion", Float) = 0

        [Space] _Back1Color ("Color", Color) = (0.5, 0.5, 0.5, 1)
        _Back1Tex ("Texture (RGB)", 2D) = "black" {}
        _Back1ScrollU ("Move Type: Scroll U", Range(-10, 10)) = 0
        _Back1ScrollV ("Move Type: Scroll V", Range(-10, 10)) = 0
        _Back1ScrollAngle ("Move Type: Scroll Angle", Range(-180, 180)) = 0
        _Back1Rotate ("Move Type: Rotate", Range(-10, 10)) = 0
        _Back1Spiral ("Move Type: Spiral", Range(-10, 10)) = 0
        _Back1FlashSpeed ("Flash: Speed", Range(0.1, 100)) = 1
        _Back1FlashMin ("Flash: Min", Range(0, 0.999)) = 0
        _Back1FlashMax ("Flash: Max", Range(0.001, 1)) = 1
        [Space][Enum(Additive,1,Subtract,2,Transparent,3)] _Back1_RenderType ("Render Type", Float) = 1
        [Enum(Fixed,1,Scroll,2,Rotate,3,Polar,4)] _Back1_MoveType ("Move Type", Float) = 1
        [Enum(Off,0,On,1)] _Back1_Flash ("Flash", Float) = 0
        [Enum(Off,0,On,1)] _Back1_Distortion ("Distortion", Float) = 0

        [Space] _Back2Color ("Color", Color) = (0.5, 0.5, 0.5, 1)
        _Back2Tex ("Texture (RGB)", 2D) = "black" {}
        _Back2ScrollU ("Move Type: Scroll U", Range(-10, 10)) = 0
        _Back2ScrollV ("Move Type: Scroll V", Range(-10, 10)) = 0
        _Back2ScrollAngle ("Move Type: Scroll Angle", Range(-180, 180)) = 0
        _Back2Rotate ("Move Type: Rotate", Range(-10, 10)) = 0
        _Back2Spiral ("Move Type: Spiral", Range(-10, 10)) = 0
        _Back2FlashSpeed ("Flash: Speed", Range(0.1, 100)) = 1
        _Back2FlashMin ("Flash: Min", Range(0, 0.999)) = 0
        _Back2FlashMax ("Flash: Max", Range(0.001, 1)) = 1
        [Space][Enum(Additive,1,Subtract,2,Transparent,3)] _Back2_RenderType ("Render Type", Float) = 1
        [Enum(Fixed,1,Scroll,2,Rotate,3,Polar,4)] _Back2_MoveType ("Move Type", Float) = 1
        [Enum(Off,0,On,1)] _Back2_Flash ("Flash", Float) = 0
        [Enum(Off,0,On,1)] _Back2_Distortion ("Distortion", Float) = 0

        [Space] _DistortionTex ("Texture (RGB)", 2D) = "black" {}
        _DistortionScrollU ("Move Type: Scroll U", Range(-10, 10)) = 0
        _DistortionScrollV ("Move Type: Scroll V", Range(-10, 10)) = 0
        _DistortionScrollAngle ("Move Type: Scroll Angle", Range(-180, 180)) = 0
        _DistortionRotate ("Move Type: Rotate", Range(-10, 10)) = 0
        _DistortionSpiral ("Move Type: Spiral", Range(-10, 10)) = 0
        _DistortionFlashSpeed ("Flash: Speed", Range(0.1, 100)) = 1
        _DistortionFlashMin ("Flash: Min", Range(0, 0.999)) = 0
        _DistortionFlashMax ("Flash: Max", Range(0.001, 1)) = 1
        _DistortionIntensityU ("Distortion: Intensity U", Range(-1, 1)) = 0
        _DistortionIntensityV ("Distortion: Intensity V", Range(-1, 1)) = 0
        [Space][Enum(Fixed,1,Scroll,2,Rotate,3,Polar,4)] _Distortion_MoveType ("Move Type", Float) = 1
        [Enum(Off,0,On,1)] _Distortion_Flash ("Flash", Float) = 0
    }

    SubShader
    {
        LOD 100
        Tags
        {
            "Queue" = "Geometry"
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "VariantCard"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_local NO_FRONT2 USE_FRONT2
            #pragma multi_compile_local NO_BACK2 USE_BACK2
            #pragma multi_compile_local NO_FRONT_1_2_FLASH USE_FRONT_1_2_FLASH
            #pragma multi_compile_local NO_BACK_1_2_FLASH USE_BACK_1_2_FLASH
            #pragma multi_compile_local NO_DISTORTION_FLASH USE_DISTORTION_FLASH
            #pragma multi_compile_local NO_FRONT_BACK_1_2_DISTORTION USE_FRONT_BACK_1_2_DISTORTION

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);       SAMPLER(sampler_MainTex);
            TEXTURE2D(_MaskTex);       SAMPLER(sampler_MaskTex);
            TEXTURE2D(_Front1Tex);     SAMPLER(sampler_Front1Tex);
            TEXTURE2D(_Front2Tex);     SAMPLER(sampler_Front2Tex);
            TEXTURE2D(_Back1Tex);      SAMPLER(sampler_Back1Tex);
            TEXTURE2D(_Back2Tex);      SAMPLER(sampler_Back2Tex);
            TEXTURE2D(_DistortionTex); SAMPLER(sampler_DistortionTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _MaskTex_ST;
                float4 _Front1Tex_ST;
                float4 _Front2Tex_ST;
                float4 _Back1Tex_ST;
                float4 _Back2Tex_ST;
                float4 _DistortionTex_ST;

                half4 _Color;
                half4 _Front1Color;
                half4 _Front2Color;
                half4 _Back1Color;
                half4 _Back2Color;

                float _FlagFront1Flash;
                float _FlagFront1Distortion;
                float _FlagFront1RenderType;
                float _FlagFront1MoveType;
                float _FlagFront2Flash;
                float _FlagFront2Distortion;
                float _FlagFront2RenderType;
                float _FlagFront2MoveType;
                float _FlagBack1Flash;
                float _FlagBack1Distortion;
                float _FlagBack1RenderType;
                float _FlagBack1MoveType;
                float _FlagBack2Flash;
                float _FlagBack2Distortion;
                float _FlagBack2RenderType;
                float _FlagBack2MoveType;
                float _FlagDistortionMoveType;

                float _Front1ScrollU;
                float _Front1ScrollV;
                float _Front1ScrollAngle;
                float _Front1Rotate;
                float _Front1Spiral;
                float _Front1FlashSpeed;
                float _Front1FlashMin;
                float _Front1FlashMax;
                float _Front1_RenderType;
                float _Front1_MoveType;
                float _Front1_Flash;
                float _Front1_Distortion;

                float _Front2ScrollU;
                float _Front2ScrollV;
                float _Front2ScrollAngle;
                float _Front2Rotate;
                float _Front2Spiral;
                float _Front2FlashSpeed;
                float _Front2FlashMin;
                float _Front2FlashMax;
                float _Front2_RenderType;
                float _Front2_MoveType;
                float _Front2_Flash;
                float _Front2_Distortion;

                float _Back1ScrollU;
                float _Back1ScrollV;
                float _Back1ScrollAngle;
                float _Back1Rotate;
                float _Back1Spiral;
                float _Back1FlashSpeed;
                float _Back1FlashMin;
                float _Back1FlashMax;
                float _Back1_RenderType;
                float _Back1_MoveType;
                float _Back1_Flash;
                float _Back1_Distortion;

                float _Back2ScrollU;
                float _Back2ScrollV;
                float _Back2ScrollAngle;
                float _Back2Rotate;
                float _Back2Spiral;
                float _Back2FlashSpeed;
                float _Back2FlashMin;
                float _Back2FlashMax;
                float _Back2_RenderType;
                float _Back2_MoveType;
                float _Back2_Flash;
                float _Back2_Distortion;

                float _DistortionScrollU;
                float _DistortionScrollV;
                float _DistortionScrollAngle;
                float _DistortionRotate;
                float _DistortionSpiral;
                float _DistortionFlashSpeed;
                float _DistortionFlashMin;
                float _DistortionFlashMax;
                float _DistortionIntensityU;
                float _DistortionIntensityV;
                float _Distortion_MoveType;
                float _Distortion_Flash;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float2 RotateUV(float2 value, float angle)
            {
                float sineValue;
                float cosineValue;
                sincos(angle, sineValue, cosineValue);
                return float2(
                    cosineValue * value.x - sineValue * value.y,
                    sineValue * value.x + cosineValue * value.y);
            }

            float2 SafeScale(float2 value)
            {
                const float epsilon = 0.000001;
                return float2(
                    abs(value.x) < epsilon ? (value.x < 0.0 ? -epsilon : epsilon) : value.x,
                    abs(value.y) < epsilon ? (value.y < 0.0 ? -epsilon : epsilon) : value.y);
            }

            float2 AnimatedUV(
                float2 mainUV,
                float4 textureST,
                float scrollU,
                float scrollV,
                float scrollAngle,
                float rotateSpeed,
                float spiral,
                float moveFlag)
            {
                float2 scale = SafeScale(textureST.xy);
                float2 layerUV = mainUV * textureST.xy + textureST.zw;

                // Original custom inspector stores MoveType - 2 in the hidden flag:
                // -1 Fixed, 0 Scroll, 1 Rotate, 2 Polar.
                if (moveFlag < -0.5)
                    return layerUV;

                float2 scrollOffset = frac(-_Time.y * float2(scrollU, scrollV));

                if (moveFlag < 0.5)
                {
                    float2 normalizedUV = layerUV / scale;
                    normalizedUV = RotateUV(normalizedUV, radians(scrollAngle));
                    return normalizedUV * textureST.xy + scrollOffset;
                }

                if (moveFlag < 1.5)
                {
                    float2 centeredUV = layerUV - textureST.xy * 0.5;
                    return RotateUV(centeredUV, _Time.y * rotateSpeed) + textureST.xy * 0.5;
                }

                // The GLES3 program uses atan2(center.x, center.y), not atan2(y, x).
                float2 center = textureST.xy * 0.5 - layerUV;
                float radius = length(center);
                float angle = atan2(center.x, center.y);
                return float2(radius, angle - radius * spiral + PI) * INV_TWO_PI + scrollOffset;
            }

            float FlashValue(float speed, float minimumValue, float maximumValue)
            {
                if (abs(speed) < 0.000001 || abs(maximumValue - minimumValue) < 0.000001)
                    return 0.0;

                float wave = sin(_Time.y * speed) * 0.5 + 0.5;
                if (maximumValue > minimumValue)
                {
                    float denominator = max(maximumValue * (1.0 - minimumValue), 0.000001);
                    return saturate((wave + maximumValue - 1.0) / denominator);
                }

                float denominator = max(minimumValue, 0.000001);
                float inverseWidth = max(1.0 - maximumValue, 0.000001);
                return saturate((1.0 - wave / inverseWidth) / denominator);
            }

            half4 CompositeLayer(
                half4 baseColor,
                half4 textureColor,
                half4 tint,
                half maskValue,
                float renderFlag,
                float flashFlag,
                float flashValue)
            {
                half flashMultiplier = flashFlag > 0.5 ? (half)flashValue : 1.0h;
                half alpha = textureColor.a * maskValue * tint.a * flashMultiplier;
                half4 transparentTarget = half4(textureColor.rgb * tint.rgb * 2.0h, alpha);
                half4 transparentResult = lerp(baseColor, transparentTarget, alpha);

                half4 effect = textureColor * (maskValue * tint) * (2.0h * flashMultiplier);
                half4 additiveResult = baseColor + effect;
                half4 subtractResult = baseColor - effect;

                // Original custom inspector stores RenderType - 2:
                // -1 Additive, 0 Subtract, 1 Transparent.
                if (renderFlag > 0.5)
                    return transparentResult;
                if (renderFlag < -0.5)
                    return additiveResult;
                return subtractResult;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv * _MainTex_ST.xy + _MainTex_ST.zw;
                output.color = input.color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half3 mask = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, input.uv).rgb;

                float2 distortionUV = AnimatedUV(
                    input.uv, _DistortionTex_ST,
                    _DistortionScrollU, _DistortionScrollV,
                    _DistortionScrollAngle, _DistortionRotate,
                    _DistortionSpiral, _FlagDistortionMoveType);
                half2 distortionSample = SAMPLE_TEXTURE2D(_DistortionTex, sampler_DistortionTex, distortionUV).rg;
                float distortionFlash = 1.0;
                #if defined(USE_DISTORTION_FLASH)
                    if (_Distortion_Flash > 0.5)
                        distortionFlash = FlashValue(_DistortionFlashSpeed, _DistortionFlashMin, _DistortionFlashMax);
                #endif
                float2 distortionOffset = ((float2)distortionSample - 0.5)
                    * float2(_DistortionIntensityU, _DistortionIntensityV)
                    * mask.b * distortionFlash;

                half4 result = SAMPLE_TEXTURE2D_BIAS(_MainTex, sampler_MainTex, input.uv + distortionOffset, -0.5);

                float2 back1UV = AnimatedUV(
                    input.uv, _Back1Tex_ST,
                    _Back1ScrollU, _Back1ScrollV,
                    _Back1ScrollAngle, _Back1Rotate,
                    _Back1Spiral, _FlagBack1MoveType);
                #if defined(USE_FRONT_BACK_1_2_DISTORTION)
                    if (_FlagBack1Distortion > 0.5)
                        back1UV += distortionOffset;
                #endif
                half4 back1 = SAMPLE_TEXTURE2D(_Back1Tex, sampler_Back1Tex, back1UV);
                float back1Flash = 1.0;
                #if defined(USE_BACK_1_2_FLASH)
                    back1Flash = FlashValue(_Back1FlashSpeed, _Back1FlashMin, _Back1FlashMax);
                #endif
                result = CompositeLayer(result, back1, _Back1Color, mask.g,
                    _FlagBack1RenderType, _FlagBack1Flash, back1Flash);

                #if defined(USE_BACK2)
                    float2 back2UV = AnimatedUV(
                        input.uv, _Back2Tex_ST,
                        _Back2ScrollU, _Back2ScrollV,
                        _Back2ScrollAngle, _Back2Rotate,
                        _Back2Spiral, _FlagBack2MoveType);
                    #if defined(USE_FRONT_BACK_1_2_DISTORTION)
                        if (_FlagBack2Distortion > 0.5)
                            back2UV += distortionOffset;
                    #endif
                    half4 back2 = SAMPLE_TEXTURE2D(_Back2Tex, sampler_Back2Tex, back2UV);
                    float back2Flash = 1.0;
                    #if defined(USE_BACK_1_2_FLASH)
                        back2Flash = FlashValue(_Back2FlashSpeed, _Back2FlashMin, _Back2FlashMax);
                    #endif
                    result = CompositeLayer(result, back2, _Back2Color, mask.g,
                        _FlagBack2RenderType, _FlagBack2Flash, back2Flash);
                #endif

                float2 front1UV = AnimatedUV(
                    input.uv, _Front1Tex_ST,
                    _Front1ScrollU, _Front1ScrollV,
                    _Front1ScrollAngle, _Front1Rotate,
                    _Front1Spiral, _FlagFront1MoveType);
                #if defined(USE_FRONT_BACK_1_2_DISTORTION)
                    if (_FlagFront1Distortion > 0.5)
                        front1UV += distortionOffset;
                #endif
                half4 front1 = SAMPLE_TEXTURE2D(_Front1Tex, sampler_Front1Tex, front1UV);
                float front1Flash = 1.0;
                #if defined(USE_FRONT_1_2_FLASH)
                    front1Flash = FlashValue(_Front1FlashSpeed, _Front1FlashMin, _Front1FlashMax);
                #endif
                result = CompositeLayer(result, front1, _Front1Color, mask.r,
                    _FlagFront1RenderType, _FlagFront1Flash, front1Flash);

                #if defined(USE_FRONT2)
                    float2 front2UV = AnimatedUV(
                        input.uv, _Front2Tex_ST,
                        _Front2ScrollU, _Front2ScrollV,
                        _Front2ScrollAngle, _Front2Rotate,
                        _Front2Spiral, _FlagFront2MoveType);
                    #if defined(USE_FRONT_BACK_1_2_DISTORTION)
                        if (_FlagFront2Distortion > 0.5)
                            front2UV += distortionOffset;
                    #endif
                    half4 front2 = SAMPLE_TEXTURE2D(_Front2Tex, sampler_Front2Tex, front2UV);
                    float front2Flash = 1.0;
                    #if defined(USE_FRONT_1_2_FLASH)
                        front2Flash = FlashValue(_Front2FlashSpeed, _Front2FlashMin, _Front2FlashMax);
                    #endif
                    result = CompositeLayer(result, front2, _Front2Color, mask.r,
                        _FlagFront2RenderType, _FlagFront2Flash, front2Flash);
                #endif

                return result * _Color * input.color;
            }
            ENDHLSL
        }
    }

    FallBack Off
    CustomEditor "ReDivVariantCardShaderGUI"
}
