#version 330 core
// Star Gate walls: two surfaces left and right of the screen center, covered
// with continuous neon lines, columns of short bars and glowing patches, in
// large color fields that fade into new neon hues. Perspective: the walls run
// in depth and converge to a bright seam at the center. Flat: the same walls
// seen straight on, scrolling sideways. Output is linear light; values above
// 1.0 feed the bloom.

out vec4 fragColor;

uniform vec2 uRes;          // render size in pixels
uniform int uMode;          // 0 = perspective, 1 = flat
uniform float uScroll;      // scroll phase in color keys, wrapped by PERIOD
uniform float uColorPhase;  // color evolution phase in color keys, wrapped by PERIOD
uniform float uDensity;     // lines per wall unit (perspective) or per half screen height (flat)
uniform float uThickness;   // line width as a fraction of the line spacing
uniform uint uSeed;

const float PERIOD = 256.0;         // must match Simulation.Period
const float STREAK_FREQ = 6.0;      // light streaks per key along a line (integer)
const float WALL_DIST = 1.0;        // distance from the camera to each wall
const float KEY_LENGTH_P = 3.0;     // key length along the depth, wall units
const float LOG_DEPTH = 6.0;        // beyond this depth, keys stretch with the distance
const float KEY_LENGTH_F = 0.6;     // key length in flat mode, half screen heights
const float LADDER_LINES = 8.0;     // height of a bar column cell, in lines
const float SEGMENT = 2.0;          // keys per wall texture segment (PERIOD / SEGMENT is an integer)
const float NEON_L = 0.72;          // OKLCH lightness
const float NEON_C = 0.21;          // OKLCH chroma, constant so fades never turn grey

uint pcg(uint v)
{
    uint state = v * 747796405u + 2891336453u;
    uint word = ((state >> ((state >> 28u) + 4u)) ^ state) * 277803737u;
    return (word >> 22u) ^ word;
}

float rand(uint a, uint b)
{
    return float(pcg(a ^ pcg(b ^ uSeed))) * (1.0 / 4294967296.0);
}

uint row(uint s, float j)
{
    return pcg(s ^ (uint(int(j)) * 0x9e3779b9u));
}

vec3 oklchToLinearSrgb(float L, float C, float hueTurns)
{
    float a = C * cos(6.28318531 * hueTurns);
    float b = C * sin(6.28318531 * hueTurns);
    float l_ = L + 0.3963377774 * a + 0.2158037573 * b;
    float m_ = L - 0.1055613458 * a - 0.0638541728 * b;
    float s_ = L - 0.0894841775 * a - 1.2914855480 * b;
    float l = l_ * l_ * l_;
    float m = m_ * m_ * m_;
    float s = s_ * s_ * s_;
    return max(vec3(
         4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s,
        -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s,
        -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s), 0.0);
}

// Smooth value noise, periodic with `period` along x (an integer), unbounded along y.
float vnoise(uint s, vec2 p, float period)
{
    vec2 i = floor(p);
    vec2 f = p - i;
    vec2 t = f * f * (3.0 - 2.0 * f);
    uint x0 = uint(mod(i.x, period));
    uint x1 = uint(mod(i.x + 1.0, period));
    uint r0 = row(s, i.y);
    uint r1 = row(s, i.y + 1.0);
    return mix(mix(rand(r0, x0), rand(r0, x1), t.x), mix(rand(r1, x0), rand(r1, x1), t.x), t.y);
}

// Random key hues joined by eased shortest-arc fades; continuous, periodic with PERIOD.
float hueTrack(uint stream, float x)
{
    float i = floor(x);
    float f = x - i;
    float h0 = rand(stream, uint(mod(i, PERIOD)));
    float h1 = rand(stream, uint(mod(i + 1.0, PERIOD)));
    float d = h1 - h0;
    d -= floor(d + 0.5);
    return h0 + d * (f * f * (3.0 - 2.0 * f));
}

// Integral of a periodic pulse of width w centered on integers.
float pulseIntegral(float x, float w)
{
    float y = x + 0.5;
    return floor(y) * w + clamp(fract(y) - (0.5 - 0.5 * w), 0.0, w);
}

// Box-filtered periodic pulse: exact coverage of the pixel footprint fw.
float pulse(float x, float w, float fw)
{
    fw = max(fw, 1e-4);
    return (pulseIntegral(x + 0.5 * fw, w) - pulseIntegral(x - 0.5 * fw, w)) / fw;
}

// Box-filtered coverage of the interval [a, a + w] by the footprint [x - fw/2, x + fw/2].
float band(float x, float a, float w, float fw)
{
    fw = max(fw, 1e-4);
    return (clamp(x + 0.5 * fw - a, 0.0, w) - clamp(x - 0.5 * fw - a, 0.0, w)) / fw;
}

// Extra light of one wall texture type, box filtered so dense areas show their average.
float pattern(int type, float s, float yl, float fs, float fy, float merged, uint line)
{
    if (type == 1)
    {
        // Grid of light dots, some of them off.
        float gx = s * 10.0;
        float on = step(rand(line ^ 0x27d4eb2fu, uint(mod(floor(gx), PERIOD * 10.0))), 0.7);
        on = mix(on, 0.7, max(merged, smoothstep(0.3, 0.8, fs * 10.0)));
        return pulse(gx, 0.35, fs * 10.0) * pulse(yl, 0.5, fy) * on * 2.2;
    }
    if (type == 2)
    {
        // Slanted hatching across the lines.
        float h = yl * 0.2 + s * 5.0;
        return pulse(h, 0.45, fwidth(h)) * 0.8;
    }
    if (type == 3)
    {
        // Rows of bars at fixed depths.
        return pulse(s * 8.0, 0.3, fs * 8.0) * (0.4 + 0.6 * pulse(yl, 0.7, fy)) * 1.2;
    }
    return 0.0; // lines only
}

void main()
{
    // Height-normalized coordinates: y in [-1, 1], x grows with the aspect ratio.
    vec2 q = (gl_FragCoord.xy - 0.5 * uRes) / (0.5 * uRes.y);
    float ax = abs(q.x);
    uint side = q.x < 0.0 ? 0u : 1u;
    uint wall = pcg(uSeed ^ (side * 0x85ebca6bu + 17u));

    float s;        // along the lines, in keys (periodic with PERIOD)
    float yl;       // across the lines, in line units (line centers at integers)
    float cy;       // bounded vertical coordinate for the large color fields
    float envelope; // overall light falloff toward the screen edges
    if (uMode == 0)
    {
        float z = WALL_DIST / max(ax, 1e-4);
        float yw = q.y * z;
        // Linear in depth up close, logarithmic far away (continuous slope).
        float k = z < LOG_DEPTH ? z : LOG_DEPTH * (1.0 + log(z / LOG_DEPTH));
        s = k / KEY_LENGTH_P + uScroll;            // features move toward the viewer
        yl = yw * uDensity;
        cy = atan(yw / WALL_DIST) * (2.0 / 3.14159265);
        envelope = exp(-0.9 * ax) * exp(-0.35 * q.y * q.y);
    }
    else
    {
        s = ax / KEY_LENGTH_F - uScroll;           // features move toward the screen edges
        yl = q.y * uDensity;
        cy = q.y * 0.6;
        envelope = exp(-0.45 * ax) * exp(-0.3 * q.y * q.y);
    }

    // Organic lines: a slight waviness, as in hand-made slit-scan artwork.
    yl += 0.14 * (vnoise(wall ^ 0x4f1bbcdcu, vec2(s * 3.0, yl * 0.08), PERIOD * 3.0) - 0.5);

    float fs = fwidth(s);
    float fy = fwidth(yl);
    // Once lines are thinner than a pixel, per-line randomness would alias: use its mean.
    float merged = smoothstep(0.35, 1.0, fy);

    // Large color fields per wall, drifting along the wall and changing over time.
    float field = vnoise(wall ^ 0x68e31da4u, vec2(s * 0.5, cy * 3.0), PERIOD * 0.5);
    float detail = vnoise(wall ^ 0x2c1b3c6du, vec2(s * 2.0, cy * 9.0), PERIOD * 2.0);
    float hue = rand(wall, 5u) + 0.55 * field + 0.12 * detail
              + hueTrack(wall ^ 0xb5297a4du, uColorPhase);

    // Continuous lines with light streaks moving along them.
    float n = floor(yl + 0.5);
    uint line = pcg((uint(int(n) + 65536) << 1) | side);
    // Line width varies slowly along each line.
    float wi = floor(s * 2.0);
    float wf = s * 2.0 - wi;
    float wv = mix(rand(line ^ 0x6a09e667u, uint(mod(wi, PERIOD * 2.0))), rand(line ^ 0x6a09e667u, uint(mod(wi + 1.0, PERIOD * 2.0))), wf * wf * (3.0 - 2.0 * wf));
    float w = uThickness * mix(0.7 + 0.6 * wv, 1.0, merged);
    float cover = pulse(yl, w, fy);
    float core = 1.0 - 0.5 * smoothstep(0.0, 1.0, abs(yl - n) / (0.5 * w));
    cover *= mix(0.75, core, clamp(w / fy * 0.25 - 0.25, 0.0, 1.0));
    float i = floor(s * STREAK_FREQ);
    float f = s * STREAK_FREQ - i;
    float per = PERIOD * STREAK_FREQ;
    float st = mix(rand(line, uint(mod(i, per))), rand(line, uint(mod(i + 1.0, per))), f * f * (3.0 - 2.0 * f));
    st = 0.35 + 1.2 * st * st;
    st = mix(st, 0.75, max(smoothstep(0.3, 0.8, fs * STREAK_FREQ), merged));
    float lineLight = cover * st * mix(0.6 + 0.4 * rand(line, 3u), 0.8, merged);
    vec3 lineColor = oklchToLinearSrgb(NEON_L, NEON_C, hue + (rand(line, 4u) - 0.5) * 0.06 * (1.0 - merged));

    // The wall texture changes by segments along the depth, with crossfades:
    // lines only, grids of light dots, slanted hatching, or rows of bars.
    float seg = s / SEGMENT;
    float gi = floor(seg);
    float segCount = PERIOD / SEGMENT;
    int typeA = int(rand(wall ^ 0x3c6ef372u, uint(mod(gi, segCount))) * 4.0);
    int typeB = int(rand(wall ^ 0x3c6ef372u, uint(mod(gi + 1.0, segCount))) * 4.0);
    float xf = smoothstep(0.75, 1.0, seg - gi);
    float extra = mix(pattern(typeA, s, yl, fs, fy, merged, line), pattern(typeB, s, yl, fs, fy, merged, line), xf);
    lineLight += extra;

    // Columns of short bars at a fixed depth ("ladders"), one per active cell.
    float cyl = yl / LADDER_LINES;
    float ci = floor(s);
    float cj = floor(cyl);
    uint cell = row(wall ^ 0x1b56c4e9u, cj);
    uint ck = uint(mod(ci, PERIOD));
    float active = step(rand(cell, ck), 0.3);
    float c0 = 0.12 + 0.6 * rand(cell ^ 0x51u, ck);
    float cw = 0.05 + 0.15 * rand(cell ^ 0x93u, ck);
    float column = band(fract(s), c0, cw, fs);
    float rows = smoothstep(0.05, 0.15, fract(cyl)) * (1.0 - smoothstep(0.85, 0.95, fract(cyl)));
    float bars = pulse(yl + 0.5, 0.5, fy);
    float ladder = active * column * rows * bars * 1.5;
    ladder *= 1.0 - smoothstep(0.3, 0.9, fwidth(cyl)); // too dense to read: leave the lines only
    vec3 ladderColor = oklchToLinearSrgb(NEON_L + 0.04, NEON_C, hue + 0.08);

    // Glowing patches and dark gaps.
    float blob = vnoise(wall ^ 0x7feb352du, vec2(s * 1.0, cy * 7.0), PERIOD);
    float patches = 0.3 + 1.1 * blob * blob;

    vec3 color = (lineColor * lineLight + ladderColor * ladder) * patches * envelope;

    // Hot core along the center seam, where the far walls pile up into light.
    float hot = exp(-ax / 0.06);
    color *= 1.3 + 3.0 * hot;
    color += vec3(max(color.r, max(color.g, color.b))) * 0.15 * hot;

    fragColor = vec4(color, 1.0);
}
