#version 330 core
// Two walls of continuous neon lines, left and right of the screen center.
// Perspective: lines run along the depth of two vertical walls and converge
// to a vanishing point at the center. Flat: parallel horizontal lines.
// Output is linear light, values above 1.0 feed the bloom.

out vec4 fragColor;

uniform vec2 uRes;          // render size in pixels
uniform int uMode;          // 0 = perspective, 1 = flat
uniform float uScroll;      // scroll phase in color keys, wrapped by PERIOD
uniform float uColorPhase;  // color evolution phase in color keys, wrapped by PERIOD
uniform float uDensity;     // lines per wall unit (perspective) or per half screen height (flat)
uniform float uThickness;   // line width as a fraction of the line spacing
uniform uint uSeed;

const float PERIOD = 256.0;         // must match Simulation.Period
const float STREAK_FREQ = 4.0;      // brightness features per color key (integer)
const float BAND = 6.0;             // neighboring lines sharing a color family
const float WALL_DIST = 1.0;        // distance from the camera to each wall
const float WALL_HEIGHT = 3.0;      // half height of each wall, wall units
const float KEY_LENGTH_P = 2.5;     // color key length along the depth, wall units
const float LOG_DEPTH = 6.0;        // beyond this depth, color keys stretch with the distance
const float KEY_LENGTH_F = 0.45;    // color key length in flat mode, half screen heights
const float FOG_DIST = 25.0;        // depth scale of the fade toward the vanishing point
const float NEON_L = 0.74;          // OKLCH lightness
const float NEON_C = 0.20;          // OKLCH chroma, constant so fades never turn grey

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
    return vec3(
         4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s,
        -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s,
        -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s);
}

// Random key hues at integer positions, joined by eased shortest-arc fades.
// Continuous in x, periodic with PERIOD.
float hueTrack(uint stream, float x)
{
    float i = floor(x);
    float f = x - i;
    float h0 = rand(stream, uint(mod(i, PERIOD)));
    float h1 = rand(stream, uint(mod(i + 1.0, PERIOD)));
    float d = h1 - h0;
    d -= floor(d + 0.5);
    float t = f * f * (3.0 - 2.0 * f);
    return h0 + d * t;
}

// Smooth value noise along a line, periodic with PERIOD * STREAK_FREQ.
float streak(uint stream, float x)
{
    float p = PERIOD * STREAK_FREQ;
    float i = floor(x);
    float f = x - i;
    float a = rand(stream, uint(mod(i, p)));
    float b = rand(stream, uint(mod(i + 1.0, p)));
    return mix(a, b, f * f * (3.0 - 2.0 * f));
}

// Integral of a periodic pulse of width w centered on integers.
float pulseIntegral(float x, float w)
{
    float y = x + 0.5;
    return floor(y) * w + clamp(fract(y) - (0.5 - 0.5 * w), 0.0, w);
}

void main()
{
    // Height-normalized coordinates: y in [-1, 1], x grows with the aspect ratio.
    vec2 q = (gl_FragCoord.xy - 0.5 * uRes) / (0.5 * uRes.y);
    float ax = abs(q.x);
    uint side = q.x < 0.0 ? 0u : 1u;

    float u;        // across the lines, line centers at integers
    float keyPos;   // along the lines, in color keys
    float fade = 1.0;
    if (uMode == 0)
    {
        float z = WALL_DIST / max(ax, 1e-4);
        u = q.y * z * uDensity;
        // Linear in depth up close, logarithmic far away (continuous slope), so the
        // colors stay readable almost up to the vanishing point.
        float k = z < LOG_DEPTH ? z : LOG_DEPTH * (1.0 + log(z / LOG_DEPTH));
        keyPos = k / KEY_LENGTH_P + uScroll;       // features move toward smaller depth
        float zf = z / FOG_DIST;
        fade = exp(-zf * zf);
        // Finite wall height: the walls form two wedges meeting at the center.
        float edge = WALL_HEIGHT * uDensity;
        fade *= 1.0 - smoothstep(edge - 3.0, edge + 0.5, abs(u));
    }
    else
    {
        u = q.y * uDensity;
        keyPos = ax / KEY_LENGTH_F - uScroll;      // features move toward the screen edges
    }

    float n = floor(u + 0.5);
    float d = u - n;
    uint line = (uint(int(n) + 65536) << 1) | side;
    uint base = pcg(line);
    uint band = pcg((uint(int(floor(n / BAND)) + 65536) << 1) | side);

    // Box-filtered line coverage: converges to the average when lines get thinner than a pixel.
    float fw = max(fwidth(u), 1e-4);
    float w = uThickness;
    float coverage = (pulseIntegral(u + 0.5 * fw, w) - pulseIntegral(u - 0.5 * fw, w)) / fw;
    float core = 1.0 - 0.45 * smoothstep(0.0, 1.0, abs(d) / (0.5 * w));
    float wide = clamp(w / fw * 0.25 - 0.25, 0.0, 1.0);
    float intensity = coverage * mix(0.8, core, wide);

    // Fade out only where a pixel spans several color families or color keys.
    float fk = fwidth(keyPos);
    fade *= 1.0 - smoothstep(0.25 * BAND, 0.9 * BAND, fw);
    fade *= 1.0 - smoothstep(0.35, 1.0, fk);

    // Hue: a color family shared by a few neighboring lines, with a small
    // per-line shift in position and hue, drifting along the line and in time.
    float lineShift = rand(base, 1u) * 0.35;
    float offset = rand(band, 1u) * PERIOD;
    float hue = hueTrack(band ^ 0x68e31da4u, keyPos + offset + lineShift)
              + hueTrack(band ^ 0xb5297a4du, uColorPhase + rand(band, 2u) * PERIOD + lineShift)
              + (rand(base, 4u) - 0.5) * 0.12;

    float s = streak(base ^ 0x1b56c4e9u, (keyPos + offset) * STREAK_FREQ);
    s = mix(s, 0.5, smoothstep(0.3, 0.7, fk * STREAK_FREQ));
    float brightness = mix(0.6, 1.2, s) * (0.75 + 0.25 * rand(base, 3u));

    vec3 color = max(oklchToLinearSrgb(NEON_L, NEON_C, hue), 0.0);
    fragColor = vec4(color * (intensity * brightness * fade), 1.0);
}
