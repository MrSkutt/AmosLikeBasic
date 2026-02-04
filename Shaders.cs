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
    
    wrappedCoord.x = wrappedCoord.x - iResolution.x * floor(wrappedCoord.x / iResolution.x);
    wrappedCoord.y = wrappedCoord.y - iResolution.y * floor(wrappedCoord.y / iResolution.y);

    half4 mask = sample(inputTexture, wrappedCoord);
    float y = fragCoord.y;
    float2 uv = fragCoord / iScreenResolution.xy;
    
    float mode = uPositions[21]; 
    float weatherType = uParams[1].x;
    float weatherDensity = uParams[1].y;

    float h0 = uHeights[0];
    float dist0 = y - uPositions[0];
    half3 finalRGB = uColors[0].rgb;
    bool hasR = (h0 > 0.1);

    if (hasR && dist0 >= 0.0 && dist0 <= h0) {
        float t = dist0 / h0;
        finalRGB = mix(uColors[0].rgb, uColorsTo[0].rgb, half(t));
    }

    for (int i = 1; i < 21; i++) {
        float h = uHeights[i];
        if (h > 0.1) {
            float dist = y - uPositions[i];
            if (dist >= 0.0 && dist <= h) {
                float barT = dist / h;
                finalRGB = mix(uColors[i].rgb, uColorsTo[i].rgb, half(barT));
                hasR = true;
            }
        }
    }

    half3 pCol = half3(0.0);
    if (weatherType > 0.5) {
        float size = 15.0 + weatherDensity;
        float2 uv = fragCoord / iResolution.xy;
        float2 grid = uv * float2(size, size * (iResolution.y / iResolution.x));
        float2 id = floor(grid);
        float2 gUv = fract(grid) - 0.5;
        float h = hash(id.x * 123.0 + id.y * 456.0);
        // resten av shadern...
    }

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