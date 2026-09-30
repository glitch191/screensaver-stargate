#version 330 core
// Dual Kawase downsample. The first pass also extracts the bright parts.
in vec2 vUv;
out vec4 fragColor;

uniform sampler2D uSrc;
uniform vec2 uHalfPixel;    // 0.5 / destination size
uniform int uPrefilter;
uniform float uThreshold;

vec3 prefilter(vec3 c)
{
    float b = max(c.r, max(c.g, c.b));
    float knee = uThreshold * 0.5;
    float soft = clamp(b - uThreshold + knee, 0.0, 2.0 * knee);
    soft = soft * soft / (4.0 * knee + 1e-5);
    return c * (max(soft, b - uThreshold) / max(b, 1e-5));
}

void main()
{
    vec2 o = uHalfPixel;
    vec3 sum = texture(uSrc, vUv).rgb * 4.0;
    sum += texture(uSrc, vUv - o).rgb;
    sum += texture(uSrc, vUv + o).rgb;
    sum += texture(uSrc, vUv + vec2(o.x, -o.y)).rgb;
    sum += texture(uSrc, vUv - vec2(o.x, -o.y)).rgb;
    vec3 c = sum * 0.125;
    if (uPrefilter == 1)
        c = prefilter(c);
    fragColor = vec4(c, 1.0);
}
