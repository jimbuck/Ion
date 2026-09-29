#version 450
// Ion 3D: the unlit material. Color times texture times vertex color; no lighting.

#include "ion_view.glsl"
#include "ion_fragment_inputs.glsl"

layout(location = 0) out vec4 outColor;

// Mirrors Ion.Extensions.Rendering3D.MaterialUniforms (std140).
layout(set = 1, binding = 0) uniform Material
{
	// Linear RGBA.
	vec4 uBaseColor;
	vec4 uEmissive;
	// w: alpha cutoff.
	vec4 uParams;
	// x: alpha mode (0 opaque, 1 mask, 2 blend).
	vec4 uFlags;
};

layout(set = 1, binding = 1) uniform texture2D uBaseColorTexture;
layout(set = 1, binding = 7) uniform sampler uMaterialSampler;

void main()
{
	vec4 texel = texture(sampler2D(uBaseColorTexture, uMaterialSampler), vUv0);
	vec4 color = uBaseColor * vec4(ionSrgbToLinear(texel.rgb), texel.a) * vec4(ionSrgbToLinear(vColor.rgb), vColor.a);
	if (uFlags.x > 0.5 && uFlags.x < 1.5 && color.a < uParams.w) discard;
	float alpha = uFlags.x > 1.5 ? color.a : 1.0;
	outColor = ionOutput(color.rgb, alpha);
}
