
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

// ✅ UPDATED: 50 slots instead of 22
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
    
    // ✅ Mode flag moved to uParams to free slot 21
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

// ✅ STEP 2: Wrap copies of the background rasters (slot 25-34)
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

// ✅ STEP 3: Overlay raster bars (slot 10-24, fixed to the screen)
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

// ✅ STEP 4: Wrap copies of the overlay (slot 35-49)
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

    // 2. WEATHER: PARTICLE 1 = snow, 2 = rain, 3 = space. Three layers each, far to near, for depth.
    // Every cell of a grid holds at most one flake, drop or star, placed so that it never crosses
    // the cell's edge, so each pixel only has to look at its own cell in each layer.
    half3 pCol = half3(0.0);
    if (weatherType > 0.5) {
        float amount = clamp(weatherDensity, 1.0, 30.0) / 10.0;     // 1 = the usual amount
        float gust = sin(iTime * 0.23) * 0.6 + sin(iTime * 0.61 + 1.3) * 0.4;   // slow wind gusts, -1 .. 1

        if (weatherType < 1.5) { // SNOW
            for (int L = 0; L < 3; L++) {
                float near = float(L) / 2.0;                         // 0 far .. 1 near
                float cell = mix(46.0, 74.0, near) / sqrt(amount);
                // pixels per second; heavier snow falls faster (x1.3 at the usual amount 10, x1.8 at 20)
                float speed = mix(22.0, 70.0, near) * (0.8 + 0.5 * amount);
                float2 p = fragCoord;
                p.y -= iTime * speed;
                p.x -= iTime * gust * mix(6.0, 26.0, near) + float(L) * 37.0;
                float2 id = floor(p / cell);
                float2 f = p - id * cell;
                float h = hash(id.x * 127.1 + id.y * 311.7 + float(L) * 74.7);
                if (h > 0.55 + 0.1 * near) continue;                 // not every cell has a flake
                float r = mix(1.1, 3.4, near) * (0.7 + 0.6 * hash(h * 91.3));
                float m = r + 6.0;                                   // room for the sway and the glow
                float2 c = float2(m + hash(h * 13.7) * (cell - 2.0 * m), m + hash(h * 57.1) * (cell - 2.0 * m));
                c.x += sin(iTime * (0.8 + h * 1.2) + h * 40.0) * 4.0;   // each flake sways
                float d = length(f - c);
                float core = 1.0 - smoothstep(r * 0.55, r, d);
                float glow = (1.0 - smoothstep(r, r * 2.6, d)) * 0.35;
                float bright = mix(0.45, 1.0, near);
                pCol += half3(0.86, 0.92, 1.0) * half((core + glow) * bright);
            }
        }

        else if (weatherType < 2.5) { // RAIN
            float slant = 0.18 + gust * 0.1;                         // x per y: the rain leans with the wind
            for (int L = 0; L < 3; L++) {
                float near = float(L) / 2.0;
                float cellX = mix(9.0, 16.0, near) / sqrt(amount);
                float cellY = mix(120.0, 220.0, near);
                // heavier rain falls faster (x0.7 at the usual amount 10, x0.95 at 20); the streaks
                // are as long as the drop moves in a short moment, so they follow the speed
                float pace = 0.45 + 0.25 * amount;
                float speed = mix(520.0, 1050.0, near) * pace;
                float len = mix(10.0, 34.0, near) * (0.5 + 0.5 * pace);
                float2 p = fragCoord;
                p.x -= p.y * slant;                                  // lean the columns
                p.y -= iTime * speed;
                p.x -= iTime * speed * slant;
                float2 id = floor(float2(p.x / cellX, p.y / cellY));
                float h = hash(id.x * 157.3 + id.y * 113.9 + float(L) * 31.1);
                if (h > 0.45) continue;
                float2 f = float2(p.x - id.x * cellX, p.y - id.y * cellY);
                float cx = cellX * (0.3 + 0.4 * hash(h * 71.9));
                float cy = len + hash(h * 23.3) * (cellY - 2.0 * len);
                float dx = abs(f.x - cx);
                float t = (f.y - cy) / len;                          // 0 at the tail, 1 at the head
                if (t < 0.0 || t > 1.0) continue;
                float w = mix(0.6, 1.1, near);
                float line = 1.0 - smoothstep(w * 0.4, w, dx);
                float a = line * t * t * mix(0.35, 0.8, near);       // brighter towards the head
                pCol += half3(0.62, 0.72, 0.95) * half(a);
            }
        }

        else { // SPACE: stars drifting sideways at three distances, over a faint nebula
            float2 q = fragCoord / iResolution.y;
            float n = sin(q.x * 3.1 + iTime * 0.02) * sin(q.y * 2.3 - iTime * 0.015)
                    + 0.5 * sin(q.x * 7.3 - q.y * 5.1 + iTime * 0.03);
            n = clamp(n * 0.5 + 0.3, 0.0, 1.0);
            pCol += half3(0.10, 0.03, 0.16) * half(n) + half3(0.0, 0.05, 0.10) * half(1.0 - n) * 0.6;

            for (int L = 0; L < 3; L++) {
                float near = float(L) / 2.0;
                float cell = mix(26.0, 70.0, near) / sqrt(amount);
                float2 p = fragCoord;
                p.x += iTime * mix(4.0, 24.0, near);                 // parallax: near stars move faster
                float2 id = floor(p / cell);
                float2 f = p - id * cell;
                float h = hash(id.x * 91.7 + id.y * 271.3 + float(L) * 19.1);
                if (h > 0.6) continue;
                float2 c = float2(4.0 + hash(h * 33.3) * (cell - 8.0), 4.0 + hash(h * 77.7) * (cell - 8.0));
                float2 dv = f - c;
                float d = length(dv);
                float size = mix(0.6, 1.6, near) * (0.6 + hash(h * 5.1));
                float twinkle = 0.75 + 0.25 * sin(iTime * (1.5 + h * 4.0) + h * 60.0);
                float star = exp(-d * d / (size * size)) * twinkle;
                // A small cross of light on the brightest
                if (near > 0.9 && h < 0.12)
                    star += (exp(-abs(dv.y) * 1.5) * exp(-abs(dv.x) * 0.35) + exp(-abs(dv.x) * 1.5) * exp(-abs(dv.y) * 0.35)) * 0.35 * twinkle;
                float k = hash(h * 9.9);
                half3 col = k < 0.25 ? half3(0.70, 0.80, 1.0) : k < 0.6 ? half3(1.0, 1.0, 1.0) : k < 0.85 ? half3(1.0, 0.92, 0.70) : half3(1.0, 0.65, 0.55);
                pCol += col * half(star * mix(0.5, 1.0, near));
            }
        }
    }

    half3 combinedBG = hasR ? finalRGB + pCol : pCol;
    
if (mode > 1.5) {
    // Colour key (RASTER COLOUR): the raster shows only where the layer has the key colour, like
    // changing one palette colour line by line on the Amiga. uParams[1].z is the colour as
    // 0xRRGGBB, uParams[1].w how far a colour may be from it (0 – 1). Everything else is untouched.
    if (mask.a < 0.01 || !hasR) return mask;
    float key = uParams[1].z;
    half3 keyRGB = half3(floor(key / 65536.0), mod(floor(key / 256.0), 256.0), mod(key, 256.0)) / 255.0;
    half3 rgb = mask.rgb / mask.a;   // the texture has premultiplied alpha
    half3 diff = abs(rgb - keyRGB);
    float d = max(diff.r, max(diff.g, diff.b));
    float tol = uParams[1].w;
    float w = 1.0 - smoothstep(tol, tol + 0.06, d);   // soft at the edges of the colour
    return half4(mix(rgb, combinedBG, half(w)) * mask.a, mask.a);
}
if (mode > 0.5) {
    // GFX mode: always show the texture, multiply by the raster if there is one
    if (mask.a < 0.01) return half4(0.0, 0.0, 0.0, 0.0);
    if (hasR) return half4(mask.rgb * combinedBG, mask.a);
    return mask;  // No raster → passthrough
} else {
    // Bakgrundsmode
    if (mask.a > 0.1) return mask;  // ← FIRST: show the texture if there is one
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

    // Extra safety clamp (RuntimeEffect likes this)
    result = clamp(result, 0.0, 1.0);

    return half4(result, 1.0);
}
";
    }
}