namespace AmosLikeBasic
{
    public static class Shader
    {
        public const string RasterShaderCode = @"
uniform shader inputTexture;
uniform float2 iResolution;
uniform float2 iScreenResolution;
uniform float iTime;
uniform vec4 uParams[2];

uniform float uPositions[22];
uniform float uHeights[22];
uniform float uRasterColorCount[22];
uniform float4 uColors[22];
uniform float4 uColorsTo[22];
uniform float4 uRasterColors[176];

float hash(float n) { return fract(sin(n) * 43758.5453); }

half4 main(float2 fragCoord) {
    float2 scroll = float2(uParams[0].x, uParams[0].y);
    float2 wrappedCoord = fragCoord + scroll;

    // Wrap manually
    wrappedCoord.x = wrappedCoord.x - iResolution.x * floor(wrappedCoord.x / iResolution.x);
    wrappedCoord.y = wrappedCoord.y - iResolution.y * floor(wrappedCoord.y / iResolution.y);

    // Sample the texture
    half4 mask = sample(inputTexture, wrappedCoord);
    float y = fragCoord.y;
    float2 uv = fragCoord / iScreenResolution.xy;
    
    float mode = uPositions[21]; 
    float weatherType = uParams[1].x;
    float weatherDensity = uParams[1].y;

    // 1. RASTERS (Slot 0-20)
        float h0 = uHeights[0];
        float dist0 = y - uPositions[0];
        half3 finalRGB = uColors[0].rgb;
        bool hasR = (h0 > 0.1);

        if (hasR && dist0 >= 0.0 && dist0 <= h0) {
            float t = dist0 / h0;
            // Linjär gradient från färg A till B över hela höjden
            finalRGB = mix(uColors[0].rgb, uColorsTo[0].rgb, half(t));
        }


    for (int i = 1; i < 21; i++) {
        float h = uHeights[i];
        if (h > 0.1) {
            float dist = y - uPositions[i];
            if (dist >= 0.0 && dist <= h) {
                //float barT = 1.0 - abs((dist / h) * 2.0 - 1.0);
                float barT = dist / h;
                finalRGB = mix(uColors[i].rgb, uColorsTo[i].rgb, half(barT));
                hasR = true;
            }
        }
    }

    // 2. WEATHER (Nu med slumpmässiga positioner)
    half3 pCol = half3(0.0);
    if (weatherType > 0.5) {
        float size = 15.0 + weatherDensity;
        float2 uv = fragCoord / iResolution.xy;
        float2 grid = uv * float2(size, size * (iResolution.y / iResolution.x));
        float2 id = floor(grid);
        float2 gUv = fract(grid) - 0.5;
        float h = hash(id.x * 123.0 + id.y * 456.0);

        
        // Skapa tre olika slumptal baserat på cellens ID
        float h1 = hash(id.x * 123.0 + id.y * 456.0);
        float h2 = hash(h1 * 789.0);
        float h3 = hash(h2 * 321.0);
        
        // Slumpmässig offset inuti cellen (-0.4 till 0.4)
        float2 pOffset = float2(h2 - 0.5, h3 - 0.5) * 0.8;

        if (weatherType < 1.5) { // SNÖ
            float speed = 3.4 + h1 * 0.4;
            float pX = pOffset.x + sin(iTime + h1 * 6.28) * 0.2;
            float pY = fract(h1 + iTime * speed) - 0.5;
            if (length(gUv - float2(pX, pY)) < 0.05) {
                float2 dv = gUv - float2(pX, pY);
                float d = length(dv);
                float radius = 0.05;
                if (d < radius)
                {
                    float t = d / radius;
                    t = saturate(t);

                    float a = 1.0 - (t * t * (3.0 - 2.0 * t));
                    pCol = half3(0.8, 0.9, 1.0);
                }
            }
        } 

        else if (weatherType < 2.5) { // REGN
            float speed = 8.0;
            float pY = fract(h1 + iTime * speed) - 0.5;
            float pX = fract(h1 * 12.34) - 0.5;

            // Lutning ca -10° till +10°
            float angle = (fract(h1 * 7.89) - 0.5) * (10.0 * 3.1415926 / 180.0);
            float cosA = cos(angle);
            float sinA = sin(angle);

            float2 dv = gUv - float2(pX, pY);
            float2 dvRot;
            dvRot.x = dv.x * cosA - dv.y * sinA;
            dvRot.y = dv.x * sinA + dv.y * cosA;

            // Stretch droppar horisontellt
            dvRot *= float2(15.0, 1.0);

            // Mjuk kant
            float radius = 0.25;
        float t = length(dvRot) / radius;
        t = saturate(t);
        float a = (1.0 - (t*t*(3.0 - 2.0*t))) * 0.5; // alpha

        // --- Metal-säker färg-blend ---
        half3 col = half3(0.6, 0.7, 1.0);
        pCol = pCol * (1.0 - half(a)) + col * half(a);
    }
        else { // STJÄRNOR (Nu helt slumpade och skimrande)
            float shimmer = sin(iTime * 1.5 + h1 * 10.0) * 0.5 + 0.5;
            // Vi använder pOffset för att placera stjärnan slumpmässigt i cellen
            if (length(gUv - pOffset) < 0.03) pCol = half3(half(shimmer * h1));
        }
    }

    // 3. FINAL MIX
    if (mask.a < 0.01 && !hasR && weatherType < 0.5) return half4(0.0, 0.0, 0.0, 0.0);

    half3 combinedBG = hasR ? finalRGB + pCol : pCol;
    if (mode > 0.5) {
        return half4(mask.rgb * combinedBG, mask.a);
    } else {
        if (mask.a > 0.1) return mask;
        float outA = (hasR || weatherType > 2.5) ? 1.0 : half(length(pCol));
        return half4(combinedBG, outA);
    }
}";
    }
}