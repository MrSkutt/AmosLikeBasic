
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

// ✅ UPPDATERAT: 50 slots istället för 22
uniform float uPositions[50];
uniform float uHeights[50];
uniform float4 uColors[50];
uniform float4 uColorsTo[50];

float hash(float n) { return fract(sin(n) * 43758.5453); }

half4 main(float2 fragCoord) {
    float2 scroll = float2(uParams[0].x, uParams[0].y);
    float2 wrappedCoord = fragCoord + scroll;

    //float ampX = 0.0   //100.0;
    //float freqX = 0.0 //0.022;
    
    //float ampY = 0.0 //20.0;
    //float freqY = 0.0 //0.015;

    //wrappedCoord.x += sin(wrappedCoord.y * freqX + iTime * 2.0) * ampX;
    //wrappedCoord.y += sin(wrappedCoord.x * freqY + iTime * 1.5) * ampY;

    // Wrap manually
    wrappedCoord.x = wrappedCoord.x - iResolution.x * floor(wrappedCoord.x / iResolution.x);
    wrappedCoord.y = wrappedCoord.y - iResolution.y * floor(wrappedCoord.y / iResolution.y);

    // Sample the texture
    //half4 mask = sample(inputTexture, wrappedCoord);
    half4 mask = inputTexture.eval(wrappedCoord);
    float y = fragCoord.y;
    float2 uv = fragCoord / iScreenResolution.xy;
    
    // ✅ Mode-flagga flyttad till uParams för att frigöra slot 21
    float mode = uParams[0].z;
    float weatherType = uParams[1].x;
    float weatherDensity = uParams[1].y;

// ✅ STEG 1: Bakgrunds-rasters (slot 0-9, originalen)
float h0 = uHeights[0];
float dist0 = y - uPositions[0];
half3 finalRGB = uColors[0].rgb;
bool hasR = (h0 > 0.1);

if (hasR && dist0 >= 0.0 && dist0 <= h0) {
    float t = dist0 / h0;
    finalRGB = mix(uColors[0].rgb, uColorsTo[0].rgb, half(t));
}

for (int i = 1; i < 10; i++) {
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

// ✅ STEG 2: Wrap-kopior av bakgrunds-rasters (slot 25-34)
for (int i = 25; i < 35; i++) {
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

// ✅ STEG 3: Overlay raster bars (slot 10-24, skärmfasta)
for (int i = 10; i < 25; i++) {
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

// ✅ STEG 4: Wrap-kopior av overlay (slot 35-49)
for (int i = 35; i < 50; i++) {
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

    // 2. WEATHER (Oförändrad)
    half3 pCol = half3(0.0);
    if (weatherType > 0.5) {
        float size = 15.0 + weatherDensity;
        float2 uv = fragCoord / iResolution.xy;
        
        float windStrength = sin(iTime * 0.5) * 0.0;
        float windOffset = windStrength * iTime;
        float baseWindStrength = sin(iTime * 0.5) * 0.8;

        float2 windVector = float2(windOffset, 0.0);
        float2 grid = (uv + windVector / size) * float2(size, size * (iResolution.y / iResolution.x));
        
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
            
            float sway = sin(iTime * 1.2 + h1 * 6.28) * 0.25;
            float drift = windStrength * 0.8;
            float pX = fract(pOffset.x + sway + drift + 0.5) - 0.5;
            float pY = fract(h1 + iTime * speed) - 0.5;
            
            float2 dv = gUv - float2(pX, pY);
            
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
                
                float brightness = 0.5 + depthScale * 0.5;
                pCol = half3(brightness * 0.9, brightness * 0.95, brightness);
                
                float sparkle = sin(iTime * 3.0 + h1 * 20.0) * 0.5 + 0.5;
                pCol = pCol * half(0.85 + sparkle * 0.15);
            }
        }

        else if (weatherType < 2.5) { // REGN
            float windStrength = baseWindStrength * 0.6;
            float speed = (10.0 + h1 * 4.0) * depthScale;
            float pY = fract(h1 + iTime * speed) - 0.5;
            
            float pX = fract(pOffset.x + windStrength * 0.2 + 0.5) - 0.5;
            
            float windAngle = windStrength * 20.0;
            float angle = (h2 - 0.5) * (15.0 * 3.1415926 / 180.0) + 
                          (windAngle * 3.1415926 / 180.0);
            float cosA = cos(angle);
            float sinA = sin(angle);

            float2 dv = gUv - float2(pX, pY);
            float2 dvRot;
            dvRot.x = dv.x * cosA - dv.y * sinA;
            dvRot.y = dv.x * sinA + dv.y * cosA;

            dvRot *= float2(25.0 * depthScale, 0.8);

            float radius = 0.28;
            float t = length(dvRot) / radius;
            t = saturate(t);
            
            float a = (1.0 - (t * t * (3.0 - 2.0 * t))) * (0.5 + depthScale * 0.25);

            half3 col = half3(0.6, 0.7, 1.0);
            pCol = pCol * (1.0 - half(a)) + col * half(a);
        }

        else { // STJÄRNOR
            float blinkSpeed = 0.8 + h2 * 1.4;
            float shimmer = sin(iTime * blinkSpeed + h1 * 10.0) * 0.5 + 0.5;
            
            float starSize = 0.02 + h3 * 0.025;
            float dist = length(gUv - pOffset);
            
            if (dist < starSize) {
                float t = dist / starSize;
                t = saturate(t);
                float glow = 1.0 - (t * t * (3.0 - 2.0 * t));
                
                float hue = h1;
                half3 starColor;
                if (hue < 0.3) {
                    starColor = half3(1.0, 1.0, 0.95);
                } else if (hue < 0.6) {
                    starColor = half3(0.95, 0.95, 1.0);
                } else {
                    starColor = half3(1.0, 0.98, 0.9);
                }
                
                float brightness = shimmer * glow * (0.6 + h2 * 0.4);
                pCol = starColor * half(brightness);
            }
        }
    }

    half3 combinedBG = hasR ? finalRGB + pCol : pCol;
    
if (mode > 0.5) {
    // GFX-mode: visa alltid texturen, multiplicera med raster om det finns
    if (mask.a < 0.01) return half4(0.0, 0.0, 0.0, 0.0);
    if (hasR) return half4(mask.rgb * combinedBG, mask.a);
    return mask;  // Ingen raster → passthrough
} else {
    // Bakgrundsmode
    if (mask.a > 0.1) return mask;  // ← FÖRST: visa textur om den finns
    if (!hasR && weatherType < 0.5) return half4(0.0, 0.0, 0.0, 0.0);
    float outA = (hasR || weatherType > 2.5) ? 1.0 : half(length(pCol));
    return half4(combinedBG, outA);
}
}";
        
        
        public const string RasterShaderCode2 = @"        
// Star Nest - Avalonia Skia RuntimeEffect
// Based on Star Nest by Pablo Roman Andrioli
// This content is under the MIT License.

uniform shader inputTexture;
uniform float2 iResolution;
uniform float iTime;

const int iterations = 17;
const float formuparam = 0.53;
const int volsteps = 20;
const float stepsize = 0.1;
const float zoom  = 0.800;
const float tile  = 0.850;
const float speed = 0.010;
const float brightness = 0.0015;
const float darkmatter = 0.300;
const float distfading = 0.730;
const float saturation = 0.850;

vec3 modVec3(vec3 x, vec3 y) {
    return x - y * floor(x / y);
}

half4 main(vec2 fragCoord)
{
    // get coords and direction
    vec2 uv = fragCoord.xy / iResolution.xy - 0.5;
    uv.y *= iResolution.y / iResolution.x;
    vec3 dir = vec3(uv * zoom, 1.0);
    float time = iTime * speed + 0.25;
    
    // mouse rotation (fixed angles since no mouse in Avalonia)
    float a1 = 0.5;
    float a2 = 0.8;
    mat2 rot1 = mat2(cos(a1), sin(a1), -sin(a1), cos(a1));
    mat2 rot2 = mat2(cos(a2), sin(a2), -sin(a2), cos(a2));
    dir.xz *= rot1;
    dir.xy *= rot2;
    
    vec3 from = vec3(1.0, 0.5, 0.5);
    from += vec3(time * 2.0, time, -2.0);
    from.xz *= rot1;
    from.xy *= rot2;
    
    // volumetric rendering
    float s = 0.1, fade = 1.0;
    vec3 v = vec3(0.0);
    for (int r = 0; r < volsteps; r++) {
        vec3 p = from + s * dir * 0.5;
        p = abs(vec3(tile) - modVec3(p, vec3(tile * 2.0))); // tiling fold
        float pa, a = pa = 0.0;
        for (int i = 0; i < iterations; i++) { 
            p = abs(p) / dot(p, p) - formuparam; // the magic formula
            a += abs(length(p) - pa); // absolute sum of average change
            pa = length(p);
        }
        float dm = max(0.0, darkmatter - a * a * 0.001); // dark matter
        a *= a * a; // add contrast
        if (r > 6) fade *= 1.0 - dm; // dark matter, don't render near
        v += fade;
        v += vec3(s, s * s, s * s * s * s) * a * brightness * fade; // coloring based on distance
        fade *= distfading; // distance fading
        s += stepsize;
    }
    v = mix(vec3(length(v)), v, saturation); // color adjust
    return half4(v * 0.01, 1.0);
}

";


        public const string RasterShaderCode3 = @"        
uniform shader inputTexture;
uniform float2 iResolution;
uniform float iTime;

const float cloudscale = 1.1;
const float speed = 0.03;
const float clouddark = 0.5;
const float cloudlight = 0.3;
const float cloudcover = 0.2;
const float cloudalpha = 8.0;
const float skytint = 0.5;

const float3 skycolour1 = float3(0.2, 0.4, 0.6);
const float3 skycolour2 = float3(0.4, 0.7, 1.0);

const float2x2 m = float2x2(1.6, 1.2,
                            -1.2, 1.6);

// --------------------------------------------

float2 hash(float2 p)
{
    p = float2(dot(p,float2(127.1,311.7)),
               dot(p,float2(269.5,183.3)));
    return -1.0 + 2.0 * fract(sin(p) * 43758.5453123);
}

float noise(float2 p)
{
    const float K1 = 0.366025404;
    const float K2 = 0.211324865;

    float2 i = floor(p + (p.x+p.y)*K1);
    float2 a = p - i + (i.x+i.y)*K2;
    float2 o = (a.x>a.y) ? float2(1.0,0.0) : float2(0.0,1.0);
    float2 b = a - o + K2;
    float2 c = a - 1.0 + 2.0*K2;

    float3 h = max(0.5 - float3(dot(a,a), dot(b,b), dot(c,c)), 0.0);

    float3 n = h*h*h*h * float3(
        dot(a, hash(i+0.0)),
        dot(b, hash(i+o)),
        dot(c, hash(i+1.0))
    );

    return dot(n, float3(70.0));
}

float fbm(float2 n)
{
    float total = 0.0;
    float amplitude = 0.1;

    for (int i = 0; i < 7; i++)
    {
        total += noise(n) * amplitude;
        n = m * n;
        amplitude *= 0.4;
    }
    return total;
}

// --------------------------------------------

half4 main(float2 fragCoord)
{
    float2 p = fragCoord / iResolution;
    float2 uv = p * float2(iResolution.x / iResolution.y, 1.0);

    float time = iTime * speed;

    float q = fbm(uv * cloudscale * 0.5);

    // --- Ridged noise shape ---
    float r = 0.0;
    uv *= cloudscale;
    uv -= q - time;

    float weight = 0.8;
    for (int i = 0; i < 8; i++)
    {
        r += abs(weight * noise(uv));
        uv = m * uv + time;
        weight *= 0.7;
    }

    // --- Base noise ---
    float f = 0.0;
    uv = p * float2(iResolution.x / iResolution.y, 1.0);
    uv *= cloudscale;
    uv -= q - time;

    weight = 0.7;
    for (int i = 0; i < 8; i++)
    {
        f += weight * noise(uv);
        uv = m * uv + time;
        weight *= 0.6;
    }

    f *= r + f;

    // --- Colour noise ---
    float c = 0.0;
    time = iTime * speed * 2.0;

    uv = p * float2(iResolution.x / iResolution.y, 1.0);
    uv *= cloudscale * 2.0;
    uv -= q - time;

    weight = 0.4;
    for (int i = 0; i < 7; i++)
    {
        c += weight * noise(uv);
        uv = m * uv + time;
        weight *= 0.6;
    }

    // --- Ridge colour ---
    float c1 = 0.0;
    time = iTime * speed * 3.0;

    uv = p * float2(iResolution.x / iResolution.y, 1.0);
    uv *= cloudscale * 3.0;
    uv -= q - time;

    weight = 0.4;
    for (int i = 0; i < 7; i++)
    {
        c1 += abs(weight * noise(uv));
        uv = m * uv + time;
        weight *= 0.6;
    }

    c += c1;

    // --- Sky & cloud colour ---
    float3 skycolour = mix(skycolour2, skycolour1, p.y);

    float3 cloudcolour =
        float3(1.1, 1.1, 0.9) *
        clamp(clouddark + cloudlight * c, 0.0, 1.0);

    f = cloudcover + cloudalpha * f * r;

    float cloudMask = clamp(f + c, 0.0, 1.0);

    float3 result =
        mix(skycolour,
            clamp(skytint * skycolour + cloudcolour, 0.0, 1.0),
            cloudMask);

    // Extra safety clamp (RuntimeEffect gillar detta)
    result = clamp(result, 0.0, 1.0);

    return half4(result, 1.0);
}
";
    }
}