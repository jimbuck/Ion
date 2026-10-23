#version 450
// A soft round dot, faded by life; drawn with additive blending, so dense regions glow.

layout(location = 0) in vec2 vCorner;
layout(location = 1) in vec4 vColor;

layout(location = 0) out vec4 outColor;

void main()
{
	float falloff = clamp(1.0 - dot(vCorner, vCorner), 0.0, 1.0);
	// Dim each dot: tens of thousands overlap near the emitter.
	outColor = vec4(vColor.rgb, falloff * falloff * vColor.a * 0.35);
}
