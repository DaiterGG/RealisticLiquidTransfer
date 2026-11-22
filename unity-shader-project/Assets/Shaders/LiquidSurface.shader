Shader "RealisticLiquidTransfer/LiquidSurface"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _LiquidColorTex ("Liquid Color Texture", 2D) = "white" {}
        _Color ("Liquid Color", Color) = (0.16, 0.5, 0.95, 1)
        _ColorTransitionSharpness ("Color Transition Sharpness", Float) = 2

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
            "RenderPipeline" = "UniversalPipeline"
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
            Name "LiquidSurface"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            sampler2D _MainTex;
            sampler2D _LiquidColorTex;
            float4 _MainTex_TexelSize;
            float4 _Color;
            float4 _ClipRect;
            float _ColorTransitionSharpness;

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
            };

            v2f vert(appdata input)
            {
                v2f output;
                output.worldPosition = input.vertex;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.uv = input.uv;
                output.color = input.color * _Color;
                return output;
            }

            void ConsiderLiquidColorBoundary(
                float2 uv,
                float2 offset,
                inout float strongestContrast,
                inout float4 colorBefore,
                inout float4 colorAfter)
            {
                float4 before = tex2D(_LiquidColorTex, uv - offset);
                float4 after = tex2D(_LiquidColorTex, uv + offset);
                float3 colorDifference = after.rgb - before.rgb;
                float contrast = dot(colorDifference, colorDifference);
                if (contrast > strongestContrast)
                {
                    strongestContrast = contrast;
                    colorBefore = before;
                    colorAfter = after;
                }
            }

            float4 SharpenLiquidColorTransition(
                float2 uv,
                float2 screenDx,
                float2 screenDy,
                float4 centerColor)
            {
                const float boundaryRadiusPixels = 6.0;
                const float inverseSqrtTwo = 0.70710678;
                float2 horizontalOffset = screenDx * boundaryRadiusPixels;
                float2 verticalOffset = screenDy * boundaryRadiusPixels;
                float2 diagonalOffset = (screenDx + screenDy)
                    * (boundaryRadiusPixels * inverseSqrtTwo);
                float2 oppositeDiagonalOffset = (screenDx - screenDy)
                    * (boundaryRadiusPixels * inverseSqrtTwo);
                float strongestContrast = 0.0;
                float4 colorBefore = centerColor;
                float4 colorAfter = centerColor;
                ConsiderLiquidColorBoundary(
                    uv,
                    horizontalOffset,
                    strongestContrast,
                    colorBefore,
                    colorAfter);
                ConsiderLiquidColorBoundary(
                    uv,
                    verticalOffset,
                    strongestContrast,
                    colorBefore,
                    colorAfter);
                ConsiderLiquidColorBoundary(
                    uv,
                    diagonalOffset,
                    strongestContrast,
                    colorBefore,
                    colorAfter);
                ConsiderLiquidColorBoundary(
                    uv,
                    oppositeDiagonalOffset,
                    strongestContrast,
                    colorBefore,
                    colorAfter);

                if (strongestContrast < 0.0001)
                {
                    return centerColor;
                }

                float3 colorSpan = colorAfter.rgb - colorBefore.rgb;
                float blend = saturate(
                    dot(centerColor.rgb - colorBefore.rgb, colorSpan)
                    / strongestContrast);
                float sharpness = saturate(_ColorTransitionSharpness / 3.0);
                float transitionWidth = lerp(1.0, 0.08, sharpness);
                float splineBlend = smoothstep(
                    0.5 - transitionWidth * 0.5,
                    0.5 + transitionWidth * 0.5,
                    blend);
                return float4(
                    lerp(colorBefore.rgb, colorAfter.rgb, splineBlend),
                    centerColor.a);
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float density = tex2D(_MainTex, input.uv).r;
                float4 sampledLiquidColor = tex2D(_LiquidColorTex, input.uv);
                float2 screenDx = ddx(input.uv);
                float2 screenDy = ddy(input.uv);
                if (_ColorTransitionSharpness > 0.0)
                {
                    sampledLiquidColor = SharpenLiquidColorTransition(
                        input.uv,
                        screenDx,
                        screenDy,
                        sampledLiquidColor);
                }

                float leftDensity = tex2D(_MainTex, input.uv - float2(_MainTex_TexelSize.x, 0.0)).r;
                float rightDensity = tex2D(_MainTex, input.uv + float2(_MainTex_TexelSize.x, 0.0)).r;
                float downDensity = tex2D(_MainTex, input.uv - float2(0.0, _MainTex_TexelSize.y)).r;
                float upDensity = tex2D(_MainTex, input.uv + float2(0.0, _MainTex_TexelSize.y)).r;
                float2 gradient = float2(rightDensity - leftDensity, upDensity - downDensity);
                float3 normal = normalize(float3(-gradient * 5.0, 0.8));
                float2 encodedVelocity = tex2D(_MainTex, input.uv).gb * 2.0 - 1.0;
                float velocityMagnitude = saturate(length(encodedVelocity));
                float2 velocityDirection = encodedVelocity / max(length(encodedVelocity), 0.0001);
                float trailingEdge = saturate(dot(normal.xy, -velocityDirection)) * velocityMagnitude;
                float3 lightDirection = normalize(float3(-0.45, 0.65, 0.8));
                float diffuse = 0.58 + 0.42 * saturate(dot(normal, lightDirection));
                float3 halfDirection = normalize(lightDirection + float3(0.0, 0.0, 1.0));
                float specular = pow(saturate(dot(normal, halfDirection)), 28.0);
                float alpha = smoothstep(0.44, 0.56, density);
                float edge = 1.0 - smoothstep(0.55, 0.8, density);

                fixed4 color = fixed4(
                    sampledLiquidColor.rgb,
                    input.color.a * sampledLiquidColor.a);
                color.rgb *= diffuse;
                color.rgb = lerp(color.rgb * 0.62, color.rgb, 1.0 - edge * 0.35);
                color.rgb = lerp(color.rgb, color.rgb * float3(0.62, 0.78, 0.96), trailingEdge * 0.35);
                color.rgb += specular * 0.28;
                color.a *= alpha;

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
