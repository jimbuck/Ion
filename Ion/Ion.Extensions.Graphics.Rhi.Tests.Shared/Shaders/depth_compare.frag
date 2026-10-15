#version 450
// Compares a depth texture with 0.5 through a comparison sampler: 1 where the comparison passes, in the red channel.

layout(location = 0) in vec2 vUv;
layout(location = 0) out vec4 outColor;

layout(set = 0, binding = 0) uniform texture2D uDepth;
layout(set = 0, binding = 1) uniform samplerShadow uCompare;

void main()
{
	float passed = texture(sampler2DShadow(uDepth, uCompare), vec3(vUv, 0.5));
	outColor = vec4(passed, 0.0, 0.0, 1.0);
}
