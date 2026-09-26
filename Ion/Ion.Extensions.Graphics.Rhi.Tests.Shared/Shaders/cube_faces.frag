#version 450
// Samples one face of a cube map per sixth of the target's width, left to right: +X, -X, +Y, -Y, +Z, -Z.

layout(location = 0) in vec2 vUv;
layout(location = 0) out vec4 outColor;

layout(set = 0, binding = 0) uniform textureCube uCube;
layout(set = 0, binding = 1) uniform sampler uSampler;

void main()
{
	int face = int(vUv.x * 6.0);
	vec3 direction = vec3(0.0, 0.0, -1.0);
	if (face == 0) direction = vec3(1.0, 0.0, 0.0);
	else if (face == 1) direction = vec3(-1.0, 0.0, 0.0);
	else if (face == 2) direction = vec3(0.0, 1.0, 0.0);
	else if (face == 3) direction = vec3(0.0, -1.0, 0.0);
	else if (face == 4) direction = vec3(0.0, 0.0, 1.0);
	outColor = texture(samplerCube(uCube, uSampler), direction);
}
