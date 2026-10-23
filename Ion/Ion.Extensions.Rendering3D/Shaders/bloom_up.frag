#version 450
// Ion 3D bloom: one upsampling step: a 3x3 tent of the smaller level, added (additive blending) onto the larger one.

#include "ion_post.glsl"

void main()
{
	vec2 t = uTexel.xy;
	vec3 sum = ionPostSample(vUv) * 4.0;
	sum += (ionPostSample(vUv + vec2(-t.x, 0.0)) + ionPostSample(vUv + vec2(t.x, 0.0)) + ionPostSample(vUv + vec2(0.0, -t.y)) + ionPostSample(vUv + vec2(0.0, t.y))) * 2.0;
	sum += ionPostSample(vUv + vec2(-t.x, -t.y)) + ionPostSample(vUv + vec2(t.x, -t.y)) + ionPostSample(vUv + vec2(-t.x, t.y)) + ionPostSample(vUv + vec2(t.x, t.y));
	outColor = vec4(sum / 16.0, 1.0);
}
