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
        
        // ===== GLOBAL VIND-OFFSET =====
        float windStrength = sin(iTime * 0.5) * 0.0; // MYCKET STARK!
        float windOffset = windStrength * iTime; // Ackumulerad rörelse
        float baseWindStrength = sin(iTime * 0.5) * 0.8;

        // Applicera på HELA gridet
        float2 windVector = float2(windOffset, 0.0);
        float2 grid = (uv + windVector / size) * float2(size, size * (iResolution.y / iResolution.x));
        // ===============================
        
        float2 id = floor(grid);
        float2 gUv = fract(grid) - 0.5;
        
        float h1 = hash(id.x * 123.0 + id.y * 456.0);
        float h2 = hash(h1 * 789.0);
        float h3 = hash(h2 * 321.0);
        
        float layerDepth = fract(h3 * 3.0);
        float depthScale = 0.6 + layerDepth * 0.4;
        
        float2 pOffset = float2(h2 - 0.5, h3 - 0.5) * 0.8;

        if (weatherType < 1.5) { // SNÖ
            float windStrength = baseWindStrength * 2.5;
            
            float speed = (3.4 + h1 * 0.4) * depthScale;
            
            // SNÖ: Stor sideways drift + rotation
            float sway = sin(iTime * 1.2 + h1 * 6.28) * 0.25; // Mer sway
            float drift = windStrength * 0.8; // Stor drift
            float pX = fract(pOffset.x + sway + drift + 0.5) - 0.5;
            float pY = fract(h1 + iTime * speed) - 0.5;
            
            float2 dv = gUv - float2(pX, pY);
            
            // ROTERA snöflingan med vinden!
            float rotation = windStrength * 2.0 + iTime + h1 * 6.28;
            float cosR = cos(rotation);
            float sinR = sin(rotation);
            float2 dvRot;
            dvRot.x = dv.x * cosR - dv.y * sinR;
            dvRot.y = dv.x * sinR + dv.y * cosR;
            
            float d = length(dvRot);
            float flakeSize = (0.03 + h2 * 0.04) * depthScale;
            
            if (d < flakeSize) {
                float t = d / flakeSize;
                t = saturate(t);
                float a = 1.0 - (t * t * (3.0 - 2.0 * t));
                
                // Brightness baserat på djup
                float brightness = 0.5 + depthScale * 0.5; // Närmare = ljusare
                pCol = half3(brightness * 0.9, brightness * 0.95, brightness);
                
                // Sparkle
                float sparkle = sin(iTime * 3.0 + h1 * 20.0) * 0.5 + 0.5;
                pCol = pCol * half(0.85 + sparkle * 0.15);
            }
        }

        else if (weatherType < 2.5) { // REGN
            float windStrength = baseWindStrength * 0.6; // Mindre påverkan
            
            float speed = (10.0 + h1 * 4.0) * depthScale;
            float pY = fract(h1 + iTime * speed) - 0.5;
            
            // REGN: Mindre sideways, mer lutning
            float pX = fract(pOffset.x + windStrength * 0.2 + 0.5) - 0.5;
            
            // Lutning påverkas av vind
            float windAngle = windStrength * 20.0; // Måttlig lutning
            float angle = (h2 - 0.5) * (15.0 * 3.1415926 / 180.0) + 
                          (windAngle * 3.1415926 / 180.0);
            float cosA = cos(angle);
            float sinA = sin(angle);

            float2 dv = gUv - float2(pX, pY);
            float2 dvRot;
            dvRot.x = dv.x * cosA - dv.y * sinA;
            dvRot.y = dv.x * sinA + dv.y * cosA;

            // Stretch påverkas av depthScale
            dvRot *= float2(25.0 * depthScale, 0.8);

            float radius = 0.28;
            float t = length(dvRot) / radius;
            t = saturate(t);
            
            // Alpha baserat på djup
            float a = (1.0 - (t * t * (3.0 - 2.0 * t))) * (0.5 + depthScale * 0.25);

            half3 col = half3(0.6, 0.7, 1.0);
            pCol = pCol * (1.0 - half(a)) + col * half(a);
        }

        else { // STJÄRNOR
            // Stjärnor påverkas INTE av wind/depth (de är i rymden)
            float blinkSpeed = 0.8 + h2 * 1.4;
            float shimmer = sin(iTime * blinkSpeed + h1 * 10.0) * 0.5 + 0.5;
            
            float starSize = 0.02 + h3 * 0.025;
            float dist = length(gUv - pOffset);
            
            if (dist < starSize) {
                float t = dist / starSize;
                t = saturate(t);
                float glow = 1.0 - (t * t * (3.0 - 2.0 * t));
                
                // Färgvariation
                float hue = h1;
                half3 starColor;
                if (hue < 0.3) {
                    starColor = half3(1.0, 1.0, 0.95); // Varm vit
                } else if (hue < 0.6) {
                    starColor = half3(0.95, 0.95, 1.0); // Kall vit
                } else {
                    starColor = half3(1.0, 0.98, 0.9); // Gulaktig
                }
                
                float brightness = shimmer * glow * (0.6 + h2 * 0.4);
                pCol = starColor * half(brightness);
            }
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