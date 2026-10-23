// Ion 3D post-processing: the bind group of every post pass (group 0). Mirrors Ion.Extensions.Rendering3D.PostUniforms.

layout(set = 0, binding = 0) uniform Post
{
	// xy: the size of one source texel in UV units.
	vec4 uTexel;
	// Bloom: x threshold, y soft knee (a fraction of the threshold). Composite: x exposure, y bloom intensity, z tone map
	// (0 none, 1 Reinhard, 2 ACES), w 1 to encode the output as sRGB.
	vec4 uParams;
	// Composite: x 1 when there is bloom, y 1 when the scene is HDR (exposure and tone mapping apply).
	vec4 uFlags;
};

layout(set = 0, binding = 1) uniform texture2D uSource;
layout(set = 0, binding = 2) uniform sampler uSampler;
layout(set = 0, binding = 3) uniform texture2D uBloom;

layout(location = 0) in vec2 vUv;
layout(location = 0) out vec4 outColor;

vec3 ionPostSample(vec2 uv)
{
	return texture(sampler2D(uSource, uSampler), uv).rgb;
}

// Dual-filter downsample: the center and four diagonal half-texel taps (each a bilinear 2x2 average).
vec3 ionPostDownsample(vec2 uv)
{
	vec2 h = uTexel.xy * 0.5;
	vec3 sum = ionPostSample(uv) * 4.0;
	sum += ionPostSample(uv + vec2(-h.x, -h.y));
	sum += ionPostSample(uv + vec2(h.x, -h.y));
	sum += ionPostSample(uv + vec2(-h.x, h.y));
	sum += ionPostSample(uv + vec2(h.x, h.y));
	return sum / 8.0;
}
