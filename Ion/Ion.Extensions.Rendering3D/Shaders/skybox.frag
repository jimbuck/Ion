#version 450
// Ion 3D: the background. The environment cube map along the view direction, or (when there is no environment map,
// and for the clear triangle of a camera that shares its target) the view's clear color.

#include "ion_view.glsl"

layout(location = 0) in vec3 vDirection;
layout(location = 0) out vec4 outColor;

void main()
{
	if (uCounts.w > 0.5)
	{
		outColor = uClearColor;
		return;
	}

	vec3 sky = ionSrgbToLinear(texture(samplerCube(uEnvironment, uEnvironmentSampler), ionEnvironmentDirection(normalize(vDirection))).rgb) * uAmbient.w;
	outColor = ionOutput(sky, 1.0);
}
