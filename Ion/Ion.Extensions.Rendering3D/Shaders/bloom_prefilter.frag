#version 450
// Ion 3D bloom: the first, half-resolution level. Keeps what is brighter than the threshold, with a soft knee so the
// change is gradual, and clamps very bright texels so single pixels do not flicker.

#include "ion_post.glsl"

void main()
{
	vec3 c = ionPostDownsample(vUv);
	float brightness = max(c.r, max(c.g, c.b));
	float threshold = uParams.x;
	float knee = threshold * uParams.y + 1e-4;
	float soft = clamp(brightness - threshold + knee, 0.0, 2.0 * knee);
	soft = soft * soft / (4.0 * knee);
	float weight = max(soft, brightness - threshold) / max(brightness, 1e-4);
	outColor = vec4(min(c * weight, vec3(64.0)), 1.0);
}
