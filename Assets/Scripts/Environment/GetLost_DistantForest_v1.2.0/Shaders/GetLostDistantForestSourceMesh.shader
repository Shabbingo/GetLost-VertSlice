Shader "Hidden/GetLost/Distant Forest Source Mesh"
{
    Properties
    {
        _BaseMap("Base Map", 2D) = "white" {}
        _BaseColor("Base Color", Color) = (1,1,1,1)
        _Cutoff("Alpha Cutoff", Range(0,1)) = 0.35
        _SourceColourMultiplier("Source Colour Multiplier", Color) = (1,1,1,1)
        _AtmosphereTint("Atmosphere Tint", Color) = (0.48,0.62,0.56,1)
        _AtmosphereTintStrength("Atmosphere Tint Strength", Range(0,1)) = 0.32
        _Brightness("Brightness", Range(0.25,2)) = 1.05
        _ColourVariation("Colour Variation", Range(0,0.35)) = 0.08
        _TextureMipBias("Texture Mip Bias", Range(0,3)) = 1
        _EdgeSoftness("Edge Softness", Range(0.25,6)) = 2
        _AlphaCutoffOffset("Alpha Cutoff Offset", Range(-0.25,0.25)) = -0.04
        _SceneLightingStrength("Scene Lighting Strength", Range(0,1)) = 0.55
        _CustomFogIntegration("Forest Fog Strength", Range(0.05,1.5)) = 1
        _HeightFogSoftness("Height Fog Softness", Range(1,4)) = 1.8
        _HeightFogBlendAboveTop("Height Fog Blend Above Top", Float) = 18
        _TreeBaseFogging("Tree Base Fogging", Range(0,1)) = 0.72
        _TreeBaseFogHeightBias("Tree Base Fog Height Bias", Float) = -2
        _HeightFogCompensation("Height Fog Compensation", Range(0,1.5)) = 0.55
        _HeightFogCompensationLimit("Height Fog Compensation Limit", Range(0,1)) = 0.35
        _HeightFogColourInfluence("Height Fog Colour Influence", Range(0,1)) = 0.28
        _HorizonFadeStart("Horizon Fade Start", Range(0,1)) = 0.48
        _HorizonFadeStrength("Horizon Fade Strength", Range(0,1)) = 0.32
        _FadeInStart("Fade In Start", Float) = 450
        _FullyVisibleDistance("Fully Visible Distance", Float) = 650
        _FadeOutStart("Fade Out Start", Float) = 2200
        _MaximumDistance("Maximum Distance", Float) = 3000
        [HideInInspector] _ForestStencilRef("Forest Stencil Ref", Float) = 23
    }

    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "Distant Forest Source Mesh"
            Tags { "LightMode"="UniversalForward" }

            Cull Off
            ZWrite On
            ZTest LEqual

            // Mark only visible distant-forest pixels. The fullscreen fog pass
            // skips this stencil value so forest-aware fog is applied exactly once.
            Stencil
            {
                Ref [_ForestStencilRef]
                WriteMask 255
                Comp Always
                Pass Replace
            }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float _Cutoff;
                float4 _SourceColourMultiplier;
                float4 _AtmosphereTint;
                float _AtmosphereTintStrength;
                float _Brightness;
                float _ColourVariation;
                float _TextureMipBias;
                float _EdgeSoftness;
                float _AlphaCutoffOffset;
                float _SceneLightingStrength;
                float _CustomFogIntegration;
                float _HeightFogSoftness;
                float _HeightFogBlendAboveTop;
                float _TreeBaseFogging;
                float _TreeBaseFogHeightBias;
                float _HeightFogCompensation;
                float _HeightFogCompensationLimit;
                float _HeightFogColourInfluence;
                float _HorizonFadeStart;
                float _HorizonFadeStrength;
                float _FadeInStart;
                float _FullyVisibleDistance;
                float _FadeOutStart;
                float _MaximumDistance;
            CBUFFER_END

            // Environment Controller 3 globals.
            // Enabled defaults to 0 when no controller is present, which preserves
            // the old forest brightness instead of making the forest black.
            float _GLDistantForestLightingEnabled;
            float _GLDistantForestBrightness;
            float4 _GLDistantForestColorMultiplier;


            float _GLFogEnabled;
            float _GLFogStartDistance;
            float _GLFogFullDistance;
            float _GLFogDistanceFalloff;
            float _GLFogDistanceStrength;
            float _GLFogHeight;
            float _GLFogHeightFalloff;
            float _GLFogHeightStrength;
            float _GLFogHeightStartDistance;
            float4 _GLFogColor;
            float _GLFogDesaturation;
            float _GLFogColorBlend;
            float4 _GLFogSunDirection;
            float4 _GLFogSunColor;
            float _GLFogSunStrength;
            float _GLFogSunPower;
            float _GLFogMaximumOpacity;

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
                float treeBaseY : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float SmoothBand(float value, float a, float b)
            {
                float t = saturate((value - a) / max(0.01, b - a));
                return t * t * (3.0 - 2.0 * t);
            }

            float GetDistanceFade(float distanceToCamera)
            {
                float fadeIn = SmoothBand(distanceToCamera, _FadeInStart, _FullyVisibleDistance);
                float fadeOut = 1.0 - SmoothBand(distanceToCamera, _FadeOutStart, _MaximumDistance);
                return saturate(fadeIn * fadeOut);
            }

            float GetStandardHeightFog(float worldY, float distanceToCamera)
            {
                float belowFogTop = _GLFogHeight - worldY;
                float verticalAmount = saturate(belowFogTop / max(0.1, _GLFogHeightFalloff));
                verticalAmount = verticalAmount * verticalAmount * (3.0 - 2.0 * verticalAmount);
                float distanceGate = saturate(
                    (distanceToCamera - _GLFogHeightStartDistance) /
                    max(1.0, _GLFogHeightFalloff));
                return verticalAmount * distanceGate * _GLFogHeightStrength;
            }

            float GetForestHeightFog(float sampleY, float distanceToCamera)
            {
                float softFalloff = max(0.1, _GLFogHeightFalloff * max(1.0, _HeightFogSoftness));
                float upperBlend = max(0.0, _HeightFogBlendAboveTop);
                float belowSoftTop = (_GLFogHeight + upperBlend) - sampleY;
                float verticalAmount = saturate(belowSoftTop / max(0.1, softFalloff + upperBlend));
                verticalAmount = verticalAmount * verticalAmount * (3.0 - 2.0 * verticalAmount);
                float distanceGate = saturate(
                    (distanceToCamera - _GLFogHeightStartDistance) /
                    max(1.0, softFalloff));
                return verticalAmount * distanceGate * _GLFogHeightStrength;
            }

            void GetForestFogAmounts(
                float3 worldPos,
                float treeBaseY,
                float distanceToCamera,
                out float standardFogAmount,
                out float forestFogAmount,
                out float extraHeightFraction)
            {
                if (_GLFogEnabled < 0.5)
                {
                    standardFogAmount = 0.0;
                    forestFogAmount = 0.0;
                    extraHeightFraction = 0.0;
                    return;
                }

                float range = max(0.01, _GLFogFullDistance - _GLFogStartDistance);
                float normalizedDistance =
                    saturate((distanceToCamera - _GLFogStartDistance) / range);

                normalizedDistance =
                    pow(normalizedDistance, max(0.1, _GLFogDistanceFalloff));

                float distanceFog =
                    saturate(normalizedDistance * _GLFogDistanceStrength);

                float standardHeightFog =
                    saturate(GetStandardHeightFog(worldPos.y, distanceToCamera));

                float baseSampleY = lerp(
                    worldPos.y,
                    treeBaseY + _TreeBaseFogHeightBias,
                    saturate(_TreeBaseFogging));

                float softenedHeightFog =
                    saturate(GetForestHeightFog(baseSampleY, distanceToCamera));

                // Height Fog Compensation now means "how much of the forest-aware
                // height result replaces the normal per-leaf result".
                float customHeightFog = lerp(
                    standardHeightFog,
                    softenedHeightFog,
                    saturate(_HeightFogCompensation));

                float maxExtra =
                    saturate(_HeightFogCompensationLimit);

                customHeightFog = min(
                    customHeightFog,
                    standardHeightFog + maxExtra);

                standardFogAmount = 1.0 -
                    ((1.0 - distanceFog) * (1.0 - standardHeightFog));

                forestFogAmount = 1.0 -
                    ((1.0 - distanceFog) * (1.0 - saturate(customHeightFog)));

                standardFogAmount =
                    min(standardFogAmount, saturate(_GLFogMaximumOpacity));

                forestFogAmount =
                    min(forestFogAmount, saturate(_GLFogMaximumOpacity));

                float extraFog =
                    max(0.0, forestFogAmount - standardFogAmount);

                extraHeightFraction =
                    extraFog / max(0.0001, forestFogAmount);
            }

            float3 ApplyFogColour(
                float3 sourceColor,
                float fogAmount,
                float extraHeightFraction,
                float3 worldPos)
            {
                fogAmount =
                    min(saturate(fogAmount), saturate(_GLFogMaximumOpacity));

                if (fogAmount <= 0.0001)
                    return sourceColor;

                float luminance =
                    dot(sourceColor, float3(0.2126, 0.7152, 0.0722));

                float3 desaturated = lerp(
                    sourceColor,
                    luminance.xxx,
                    saturate(_GLFogDesaturation * fogAmount));

                // Normal distance/height fog gets the full colour blend.
                // Only the EXTRA forest compensation is reduced by this control.
                float extraColourScale = lerp(
                    1.0,
                    saturate(_HeightFogColourInfluence),
                    saturate(extraHeightFraction));

                float fogColourAmount = saturate(
                    fogAmount *
                    _GLFogColorBlend *
                    extraColourScale);

                float3 fogged =
                    lerp(desaturated, _GLFogColor.rgb, fogColourAmount);

                float3 viewDirection =
                    normalize(worldPos - _WorldSpaceCameraPos);

                float sunAlignment = saturate(
                    dot(viewDirection, normalize(_GLFogSunDirection.xyz)));

                float scatter =
                    pow(sunAlignment, max(1.0, _GLFogSunPower));

                // Do not let the extra tree-base compensation create a white halo.
                float scatterScale =
                    lerp(1.0, 0.35, saturate(extraHeightFraction));

                scatter *=
                    _GLFogSunStrength *
                    fogAmount *
                    scatterScale;

                fogged += _GLFogSunColor.rgb * scatter;
                return fogged;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 positionWS = TransformObjectToWorld(input.positionOS);
                output.positionWS = positionWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);

                // y=0 in the source mesh is a useful cheap approximation of the tree base.
                // The profile bias exists for prefabs whose foliage mesh pivot sits higher/lower.
                float3 estimatedBaseWS = TransformObjectToWorld(float3(input.positionOS.x, 0.0, input.positionOS.z));
                output.treeBaseY = estimatedBaseWS.y;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 tex = SAMPLE_TEXTURE2D_BIAS(_BaseMap, sampler_BaseMap, input.uv, _TextureMipBias);
                half alpha = tex.a * _BaseColor.a;

                float effectiveCutoff = saturate(_Cutoff + _AlphaCutoffOffset);
                float alphaWidth = max(fwidth(alpha) * _EdgeSoftness, 0.0025);
                float coverage = smoothstep(effectiveCutoff - alphaWidth, effectiveCutoff + alphaWidth, alpha);
                float edgeDither = Hash21(floor(input.positionCS.xy));
                clip(coverage - edgeDither);

                float distanceToCamera = distance(input.positionWS, _WorldSpaceCameraPos);
                float fade = GetDistanceFade(distanceToCamera);
                float fadeDither = Hash21(floor(input.positionCS.xy * 0.5) + 17.37);
                clip(fade - fadeDither);

                float3 colour = tex.rgb * _BaseColor.rgb * _SourceColourMultiplier.rgb;
                float worldVariation = Hash21(floor(input.positionWS.xz * 0.04));
                colour *= lerp(1.0 - _ColourVariation, 1.0 + _ColourVariation, worldVariation);
                colour *= _Brightness;

                half3 normalWS = normalize(input.normalWS);
                Light mainLight = GetMainLight();
                half wrappedNdotL = saturate(dot(normalWS, mainLight.direction) * 0.5h + 0.5h);
                half3 ambient = max(SampleSH(normalWS), 0.0h);
                half3 simpleLighting = ambient + mainLight.color * wrappedNdotL * 0.65h;
                colour = lerp(colour, colour * simpleLighting, saturate(_SceneLightingStrength));

                // The source tree materials/SH lighting can remain surprisingly bright
                // after sunset. Environment Controller 3 provides an explicit day/night
                // multiplier so far trees match the darkness of the real scene.
                float environmentLightingEnabled =
                    saturate(_GLDistantForestLightingEnabled);

                float environmentBrightness =
                    lerp(
                        1.0,
                        max(0.0, _GLDistantForestBrightness),
                        environmentLightingEnabled);

                float3 environmentColourMultiplier =
                    lerp(
                        1.0.xxx,
                        max(_GLDistantForestColorMultiplier.rgb, 0.0.xxx),
                        environmentLightingEnabled);

                colour *=
                    environmentBrightness *
                    environmentColourMultiplier;

                float atmosphericDistance =
                    SmoothBand(distanceToCamera, _FadeInStart, _MaximumDistance);

                float tintDistanceWeight =
                    lerp(0.35, 1.0, atmosphericDistance);

                float tintAmount =
                    saturate(_AtmosphereTintStrength * tintDistanceWeight);

                // Hue-shift the forest without flattening it into a solid pale tint.
                float originalLum =
                    max(0.0001, dot(colour, float3(0.2126, 0.7152, 0.0722)));

                float3 multipliedTint =
                    colour * lerp(1.0.xxx, _AtmosphereTint.rgb, tintAmount);

                float tintedLum =
                    max(0.0001, dot(multipliedTint, float3(0.2126, 0.7152, 0.0722)));

                multipliedTint *=
                    min(1.15, originalLum / tintedLum);

                colour = lerp(colour, multipliedTint, tintAmount);

                if (_GLFogEnabled > 0.5)
                {
                    float standardFogAmount;
                    float forestFogAmount;
                    float extraHeightFraction;

                    GetForestFogAmounts(
                        input.positionWS,
                        input.treeBaseY,
                        distanceToCamera,
                        standardFogAmount,
                        forestFogAmount,
                        extraHeightFraction);

                    float3 fogged = ApplyFogColour(
                        colour,
                        forestFogAmount,
                        extraHeightFraction,
                        input.positionWS);

                    colour = lerp(
                        colour,
                        fogged,
                        saturate(_CustomFogIntegration));
                }

                float horizonStartDistance = lerp(
                    _FadeInStart,
                    _MaximumDistance,
                    saturate(_HorizonFadeStart));

                float horizonAmount =
                    SmoothBand(
                        distanceToCamera,
                        horizonStartDistance,
                        _MaximumDistance);

                horizonAmount *= saturate(_HorizonFadeStrength);

                // Horizon blend now removes fine contrast instead of blending the
                // forest directly toward the (often very pale) fog colour.
                float horizonLuminance =
                    dot(colour, float3(0.2126, 0.7152, 0.0722));

                float3 softenedHorizon =
                    lerp(colour, horizonLuminance.xxx, 0.28);

                colour = lerp(
                    colour,
                    softenedHorizon,
                    horizonAmount);

                return half4(colour, 1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
