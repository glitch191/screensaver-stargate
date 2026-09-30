#version 330 core
// Final image: scene + bloom with film treatment (gate weave, slight lens
// softness and chromatic aberration, red halation, flicker, grain, dust),
// hue preserving tone curve, then the black center line and the optional
// diagnostics text. The center line and the text are not affected by the film
// effects, so the line stays pure black and exactly centered.
out vec4 fragColor;

uniform sampler2D uScene;
uniform sampler2D uBloom;
uniform sampler2D uFont;
uniform vec2 uRes;
uniform float uBloomStrength;
uniform float uLineHalfWidth;   // pixels
uniform int uLineSoft;
uniform int uDiag;
uniform float uTextScale;
uniform float uTime;            // seconds of simulated time, wrapped
uniform float uFilmScale;       // screen height / 1080, so the grain looks the same at every resolution
uniform int uVertical;          // 1 = horizontal center line (walls above and below)

const int TEXT_COLS = 44;
const int TEXT_ROWS = 4;
uniform int uText[TEXT_COLS * TEXT_ROWS];

const float FILM_FPS = 24.0;        // grain, dust and flicker change at the film frame rate
const float GRAIN = 0.10;           // grain strength in display values
const float GRAIN_SIZE = 1.35;      // grain cell size in pixels at 1080p
const float WEAVE_PX = 1.1;         // gate weave amplitude in pixels at 1080p
const float ABERRATION = 0.0022;    // red and blue offset at the screen edge, in screen widths
const float HALATION = 0.55;        // share of the glow turned into a warm red halo

uint pcg(uint v)
{
    uint state = v * 747796405u + 2891336453u;
    uint word = ((state >> ((state >> 28u) + 4u)) ^ state) * 277803737u;
    return (word >> 22u) ^ word;
}

float rand3(uint a, uint b, uint c)
{
    return float(pcg(a ^ pcg(b ^ pcg(c)))) * (1.0 / 4294967296.0);
}

float noise1(float x, uint stream)
{
    float i = floor(x);
    float f = x - i;
    float a = rand3(stream, uint(int(i)), 7u);
    float b = rand3(stream, uint(int(i) + 1), 7u);
    return mix(a, b, f * f * (3.0 - 2.0 * f));
}

// Value noise on a pixel lattice, new pattern for every film frame.
float grainNoise(vec2 p, uint frame, uint stream)
{
    vec2 i = floor(p);
    vec2 f = p - i;
    vec2 t = f * f * (3.0 - 2.0 * f);
    uvec2 c = uvec2(ivec2(i) + 65536);
    float a = rand3(c.x ^ stream, c.y, frame);
    float b = rand3((c.x + 1u) ^ stream, c.y, frame);
    float d = rand3(c.x ^ stream, c.y + 1u, frame);
    float e = rand3((c.x + 1u) ^ stream, c.y + 1u, frame);
    return mix(mix(a, b, t.x), mix(d, e, t.x), t.y);
}

vec3 toSrgb(vec3 c)
{
    c = clamp(c, 0.0, 1.0);
    return mix(c * 12.92, 1.055 * pow(c, vec3(1.0 / 2.4)) - 0.055, step(0.0031308, c));
}

vec3 sceneAt(vec2 uv, vec2 ca)
{
    return vec3(texture(uScene, uv + ca).r, texture(uScene, uv).g, texture(uScene, uv - ca).b);
}

void main()
{
    uint frame = uint(floor(uTime * FILM_FPS));

    // Gate weave: the whole picture drifts by about a pixel, like film in a projector gate.
    vec2 weave = (vec2(noise1(uTime * 1.3, 11u), noise1(uTime * 0.9, 23u)) - 0.5) * 2.0 * WEAVE_PX * uFilmScale;
    vec2 uv = (gl_FragCoord.xy + weave) / uRes;

    // Lens: softness from two half-pixel taps, and a radial red/blue split toward the edges.
    vec2 dc = uv - 0.5;
    vec2 ca = dc * (ABERRATION * 2.0) * vec2(1.0, uRes.x / uRes.y);
    vec2 soft = vec2(0.8, 0.6) * uFilmScale / uRes;
    vec3 c = 0.5 * (sceneAt(uv + soft, ca) + sceneAt(uv - soft, ca));

    // Glow with a warm halation component.
    vec3 glow = texture(uBloom, uv).rgb * uBloomStrength;
    float glowLum = dot(glow, vec3(0.3, 0.55, 0.15));
    c += glow * (1.0 - HALATION * 0.5) + vec3(1.0, 0.32, 0.12) * glowLum * HALATION;

    // Flicker of the projection lamp.
    c *= 1.0 + 0.05 * (noise1(uTime * 7.0, 37u) - 0.5) + 0.025 * (rand3(frame, 41u, 3u) - 0.5);

    // Compress highlights on the largest channel only, so hues stay saturated.
    float m = max(c.r, max(c.g, c.b));
    const float knee = 0.75;
    if (m > knee)
    {
        float t = knee + (1.0 - knee) * (1.0 - exp(-(m - knee) / (1.0 - knee)));
        c *= t / m;
    }

    // Soft vignette.
    float r = length(dc * vec2(uRes.x / uRes.y, 1.0));
    c *= 1.0 - 0.3 * smoothstep(0.45, 1.1, r);

    vec3 outc = toSrgb(c);

    // Grain: mostly luminance, a little color, strongest in the mid tones.
    vec2 gp = gl_FragCoord.xy / (GRAIN_SIZE * uFilmScale);
    float g = grainNoise(gp, frame, 0x9e37u) + 0.5 * grainNoise(gp * 2.3, frame, 0x85ebu) - 0.75;
    vec3 gc = vec3(grainNoise(gp * 1.7, frame, 0xc2b2u), grainNoise(gp * 1.7, frame, 0x27d4u), grainNoise(gp * 1.7, frame, 0x1656u)) - 0.5;
    float lum = dot(outc, vec3(0.2126, 0.7152, 0.0722));
    float amp = GRAIN * (0.35 + 0.65 * sqrt(lum)) * (1.0 - 0.5 * lum * lum);
    outc += (vec3(g) + gc * 0.35) * amp;

    // Dust: a few soft specks on some film frames, dark (dirt) or light (scratched emulsion).
    float cellSize = 40.0 * uFilmScale;
    vec2 cell = floor(gl_FragCoord.xy / cellSize);
    uvec2 uc = uvec2(ivec2(cell) + 65536);
    if (rand3(uc.x, uc.y, frame) < 0.0012)
    {
        vec2 center = (cell + vec2(rand3(uc.x, uc.y, frame + 7u), rand3(uc.y, uc.x, frame + 9u))) * cellSize;
        float radius = (0.8 + 2.2 * rand3(uc.x, frame, uc.y)) * uFilmScale;
        float speck = 1.0 - smoothstep(radius * 0.4, radius, length(gl_FragCoord.xy - center));
        vec3 tone = rand3(uc.y, frame, uc.x) < 0.7 ? vec3(0.0) : vec3(0.85, 0.8, 0.7);
        outc = mix(outc, tone, speck * 0.7);
    }

    // Center line coverage, over everything above, applied in display values
    // so the perceived width matches the setting.
    // The line is vertical at the exact horizontal center, or horizontal at the exact
    // vertical center in the vertical orientation.
    float pos = uVertical == 1 ? gl_FragCoord.y : gl_FragCoord.x;
    float cx = 0.5 * (uVertical == 1 ? uRes.y : uRes.x);
    float cover;
    if (uLineSoft == 1)
    {
        float dist = abs(pos - cx);
        cover = 1.0 - smoothstep(0.4 * uLineHalfWidth, 1.6 * uLineHalfWidth, dist);
    }
    else
    {
        float left = floor(pos);
        cover = clamp(min(left + 1.0, cx + uLineHalfWidth) - max(left, cx - uLineHalfWidth), 0.0, 1.0);
    }
    outc = max(outc, 0.0) * (1.0 - cover);

    if (uDiag == 1)
    {
        vec2 cellSizeText = vec2(6.0, 10.0) * uTextScale;
        vec2 p = vec2(gl_FragCoord.x, uRes.y - gl_FragCoord.y) - vec2(12.0 * uTextScale);
        vec2 tcell = floor(p / cellSizeText);
        if (p.x >= -uTextScale && p.y >= -uTextScale && tcell.x < float(TEXT_COLS) && tcell.y < float(TEXT_ROWS))
        {
            outc *= 0.35;
            vec2 local = floor((p - tcell * cellSizeText) / uTextScale);
            if (p.x >= 0.0 && p.y >= 0.0 && local.x < 5.0 && local.y < 7.0)
            {
                int ch = uText[int(tcell.y) * TEXT_COLS + int(tcell.x)];
                if (ch > 0)
                {
                    float bit = texelFetch(uFont, ivec2(ch * 5 + int(local.x), int(local.y)), 0).r;
                    outc = mix(outc, vec3(1.0), bit);
                }
            }
        }
    }

    fragColor = vec4(outc, 1.0);
}
