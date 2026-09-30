#version 330 core
// Scene + bloom, hue preserving tone curve, black center line, sRGB output,
// and the optional diagnostics text overlay.
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

const int TEXT_COLS = 44;
const int TEXT_ROWS = 4;
uniform int uText[TEXT_COLS * TEXT_ROWS];

vec3 toSrgb(vec3 c)
{
    c = clamp(c, 0.0, 1.0);
    return mix(c * 12.92, 1.055 * pow(c, vec3(1.0 / 2.4)) - 0.055, step(0.0031308, c));
}

float dither(vec2 p)
{
    return fract(52.9829189 * fract(dot(p, vec2(0.06711056, 0.00583715)))) - 0.5;
}

void main()
{
    vec2 uv = gl_FragCoord.xy / uRes;
    vec3 c = texelFetch(uScene, ivec2(gl_FragCoord.xy), 0).rgb;
    c += texture(uBloom, uv).rgb * uBloomStrength;

    // Compress highlights on the largest channel only, so hues stay saturated.
    float m = max(c.r, max(c.g, c.b));
    const float knee = 0.75;
    if (m > knee)
    {
        float t = knee + (1.0 - knee) * (1.0 - exp(-(m - knee) / (1.0 - knee)));
        c *= t / m;
    }

    // Center line coverage, drawn over the walls and the bloom.
    float cx = 0.5 * uRes.x;
    float cover;
    if (uLineSoft == 1)
    {
        float dist = abs(gl_FragCoord.x - cx);
        cover = 1.0 - smoothstep(0.4 * uLineHalfWidth, 1.6 * uLineHalfWidth, dist);
    }
    else
    {
        float left = floor(gl_FragCoord.x);
        cover = clamp(min(left + 1.0, cx + uLineHalfWidth) - max(left, cx - uLineHalfWidth), 0.0, 1.0);
    }
    // Applied after sRGB encoding so the perceived width matches the setting.
    vec3 outc = toSrgb(c) * (1.0 - cover) + dither(gl_FragCoord.xy) / 255.0 * (1.0 - cover);

    if (uDiag == 1)
    {
        vec2 cellSize = vec2(6.0, 10.0) * uTextScale;
        vec2 p = vec2(gl_FragCoord.x, uRes.y - gl_FragCoord.y) - vec2(12.0 * uTextScale);
        vec2 cell = floor(p / cellSize);
        if (p.x >= -uTextScale && p.y >= -uTextScale && cell.x < float(TEXT_COLS) && cell.y < float(TEXT_ROWS))
        {
            outc *= 0.35;
            vec2 local = floor((p - cell * cellSize) / uTextScale);
            if (p.x >= 0.0 && p.y >= 0.0 && local.x < 5.0 && local.y < 7.0)
            {
                int ch = uText[int(cell.y) * TEXT_COLS + int(cell.x)];
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
