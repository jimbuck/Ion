#version 450
// Textured quad, fragment stage. The texture and the sampler are separate bindings (WebGPU style); the GLES translation
// combines them into one sampler2D.

layout(location = 0) in vec2 vUv;

layout(location = 0) out vec4 outColor;

layout(set = 0, binding = 1) uniform texture2D uTexture;
layout(set = 0, binding = 2) uniform sampler uSampler;

void main()
{
	outColor = texture(sampler2D(uTexture, uSampler), vUv);
}
