#version 450
// Dear ImGui, vertex stage: ImDrawVert (position in pixels, uv, straight-alpha RGBA8 color) to clip space. Clip space is
// the RHI's (WebGPU): y up, depth in [0, 1]; the projection flips y so pixel rows grow downwards, as ImGui lays out.

layout(location = 0) in vec2 inPosition;
layout(location = 1) in vec2 inUv;
layout(location = 2) in vec4 inColor;

layout(location = 0) out vec2 vUv;
layout(location = 1) out vec4 vColor;

layout(set = 0, binding = 0) uniform Projection
{
	// Display pixels to clip space.
	mat4 uProjection;
};

void main()
{
	vUv = inUv;
	vColor = inColor;
	gl_Position = uProjection * vec4(inPosition, 0.0, 1.0);
}
