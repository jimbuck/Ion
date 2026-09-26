#version 450
// Sprite batch v2, fragment stage: texel times tint. No discard (scissoring is a render state of the batch), so early
// depth and tile-based hardware stay fast. The texture and the sampler are separate bindings (WebGPU style); the GLES
// translation combines them into one sampler2D.

layout(location = 0) in vec2 vUv;
layout(location = 1) in vec4 vColor;

layout(location = 0) out vec4 outColor;

layout(set = 1, binding = 0) uniform texture2D uTexture;
layout(set = 1, binding = 1) uniform sampler uSampler;

void main()
{
	outColor = texture(sampler2D(uTexture, uSampler), vUv) * vColor;
}
