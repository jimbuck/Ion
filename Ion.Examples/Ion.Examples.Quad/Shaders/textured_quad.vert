#version 450
// Textured quad, vertex stage. Clip space is the RHI's (WebGPU): y up, depth in [0, 1].

layout(location = 0) in vec2 inPosition;
layout(location = 1) in vec2 inUv;

layout(location = 0) out vec2 vUv;

layout(set = 0, binding = 0) uniform Transform
{
	mat4 uTransform;
};

void main()
{
	vUv = inUv;
	gl_Position = uTransform * vec4(inPosition, 0.0, 1.0);
}
