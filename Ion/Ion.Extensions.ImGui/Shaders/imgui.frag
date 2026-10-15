#version 450
// Dear ImGui, fragment stage: texel times the vertex color, straight alpha (the pipeline blends SrcAlpha,
// OneMinusSrcAlpha). The texture and the sampler are separate bindings (WebGPU style); the GLES translation combines them.

layout(location = 0) in vec2 vUv;
layout(location = 1) in vec4 vColor;

layout(location = 0) out vec4 outColor;

layout(set = 1, binding = 0) uniform texture2D uTexture;
layout(set = 1, binding = 1) uniform sampler uSampler;

void main()
{
	outColor = texture(sampler2D(uTexture, uSampler), vUv) * vColor;
}
