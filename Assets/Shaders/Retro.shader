// The retro gothic look (2026-10-31, the user: "small retro gothic look"): a full-screen pass after post-processing.
// The world is drawn small (render scale, point upscale) and this pass snaps it to the same pixel grid, then
// posterises each channel with a 4x4 ordered dither so gradients break into the stippled bands of old software
// renderers, and sinks the near-blacks into ink. The HUD is IMGUI, drawn after, so text stays sharp.
// Globals from UI/RetroLook: _RetroOn (0/1), _RetroRes (grid size in pixels), _RetroLevels (steps per channel).
Shader "Deepbound/Retro"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Blend Off Cull Off

        Pass
        {
            Name "Retro"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _RetroOn;
            float4 _RetroRes;
            float _RetroLevels;
            float _RetroTint;
            float _Bright; // the Options brightness as a gamma exponent (0 = unset = 1); applied here whether the retro look is on or off

            // The gothic ramp, by brightness: ink, oxblood, candlelight, bone.
            float3 GothicRamp(float l)
            {
                float3 ink = float3(0.035, 0.03, 0.055), blood = float3(0.34, 0.16, 0.12), candle = float3(0.80, 0.60, 0.38), bone = float3(1.0, 0.95, 0.84);
                if (l < 0.35) return lerp(ink, blood, l / 0.35);
                if (l < 0.72) return lerp(blood, candle, (l - 0.35) / 0.37);
                return lerp(candle, bone, saturate((l - 0.72) / 0.28));
            }

            static const float Bayer[16] = {
                 0.0,  8.0,  2.0, 10.0,
                12.0,  4.0, 14.0,  6.0,
                 3.0, 11.0,  1.0,  9.0,
                15.0,  7.0, 13.0,  5.0 };

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord.xy;
                float bright = _Bright > 0.01 ? _Bright : 1.0;
                if (_RetroOn < 0.5 || _RetroRes.x < 1.0)
                {
                    half4 plain = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, uv, 0);
                    // A gamma exponent on gamma-space colour is the same exponent on linear colour.
                    plain.rgb = pow(saturate(plain.rgb), bright);
                    return plain;
                }

                // One sample per big pixel (the pass may run before or after the upscale: either way the grid holds).
                float2 cell = floor(uv * _RetroRes.xy);
                float2 centre = (cell + 0.5) / _RetroRes.xy;
                half4 src = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, centre, 0);
                float3 c = saturate(src.rgb);

                // Work in gamma so the dark end keeps its steps (linear banding would eat the shadows).
                c = pow(c, 1.0 / 2.2);
                c = pow(c, bright);
                // Ink: the last few percent of black goes fully black, so silhouettes read against the gloom.
                c = saturate((c - 0.025) / 0.975);
                // A gentle pull toward one gothic palette (strong colours - telegraphs, poison, frost - keep most of their hue).
                float lum = dot(c, float3(0.299, 0.587, 0.114));
                float chroma = max(c.r, max(c.g, c.b)) - min(c.r, min(c.g, c.b));
                c = lerp(c, GothicRamp(lum), _RetroTint * (1.0 - saturate(chroma * 1.6)));

                uint2 p = uint2(cell) & 3u;
                float threshold = (Bayer[p.y * 4 + p.x] + 0.5) / 16.0 - 0.5;
                float levels = max(2.0, _RetroLevels - 1.0);
                c = floor(c * levels + 0.5 + threshold) / levels;

                c = pow(saturate(c), 2.2);
                return half4(c, src.a);
            }
            ENDHLSL
        }
    }
}
