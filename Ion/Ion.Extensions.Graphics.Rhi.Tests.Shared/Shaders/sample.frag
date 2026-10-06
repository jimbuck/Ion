#version 450
// Samples a 2D texture over the whole target.

layout(location = 0) in vec2 vUv;
layout(location = 0) out vec4 outColor;

layout(set = 0, binding = 0) uniform texture2D uTexture;
layout(set = 0, binding = 1) uniform sampler uSampler;

void main()
{
	outColor = texture(sampler2D(uTexture, uSampler), vUv);
}
