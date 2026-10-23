#version 450
// Ion 3D post-processing: the last pass into the frame. Adds the bloom; for an HDR scene applies the exposure and the
// tone map and encodes sRGB when the target does not; for an LDR scene copies (the scene is already encoded as the
// target is).

#include "ion_post.glsl"

vec3 ionReinhard(vec3 c)
{
	return c / (1.0 + c);
}

// Krzysztof Narkowicz's fit of the ACES filmic curve.
vec3 ionAces(vec3 c)
{
	return clamp((c * (2.51 * c + 0.03)) / (c * (2.43 * c + 0.59) + 0.14), 0.0, 1.0);
}

vec3 ionEncodeSrgb(vec3 c)
{
	c = clamp(c, 0.0, 1.0);
	return mix(c * 12.92, 1.055 * pow(c, vec3(1.0 / 2.4)) - 0.055, step(vec3(0.0031308), c));
}

void main()
{
	vec3 c = ionPostSample(vUv);
	if (uFlags.x > 0.5) c += texture(sampler2D(uBloom, uSampler), vUv).rgb * uParams.y;
	if (uFlags.y > 0.5)
	{
		c *= uParams.x;
		if (uParams.z > 1.5) c = ionAces(c);
		else if (uParams.z > 0.5) c = ionReinhard(c);
		if (uParams.w > 0.5) c = ionEncodeSrgb(c);
	}

	outColor = vec4(clamp(c, 0.0, 1.0), 1.0);
}
