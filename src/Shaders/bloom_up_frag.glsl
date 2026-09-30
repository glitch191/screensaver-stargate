#version 330 core
// Dual Kawase upsample, added to the downsampled level of the same size.
in vec2 vUv;
out vec4 fragColor;

uniform sampler2D uSrc;     // lower resolution level
uniform sampler2D uAdd;     // same resolution level from the downsample chain
uniform vec2 uHalfPixel;    // 0.5 / destination size
uniform float uSpread;      // weight of the wider levels, below 1 keeps the glow tight

void main()
{
    vec2 o = uHalfPixel;
    vec3 sum = texture(uSrc, vUv + vec2(-2.0 * o.x, 0.0)).rgb;
    sum += texture(uSrc, vUv + vec2(-o.x, o.y)).rgb * 2.0;
    sum += texture(uSrc, vUv + vec2(0.0, 2.0 * o.y)).rgb;
    sum += texture(uSrc, vUv + vec2(o.x, o.y)).rgb * 2.0;
    sum += texture(uSrc, vUv + vec2(2.0 * o.x, 0.0)).rgb;
    sum += texture(uSrc, vUv + vec2(o.x, -o.y)).rgb * 2.0;
    sum += texture(uSrc, vUv + vec2(0.0, -2.0 * o.y)).rgb;
    sum += texture(uSrc, vUv + vec2(-o.x, -o.y)).rgb * 2.0;
    fragColor = vec4(sum / 12.0 * uSpread + texture(uAdd, vUv).rgb, 1.0);
}
