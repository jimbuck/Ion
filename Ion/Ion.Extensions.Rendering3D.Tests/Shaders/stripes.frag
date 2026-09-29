#version 450
// A custom material for the extension point test: the material's two colors in vertical stripes of world x (1 unit
// wide), unlit. Uses the built-in mesh.vert (its outputs) and the view block from the renderer's include files.

#include "../../Ion.Extensions.Rendering3D/Shaders/ion_view.glsl"
#include "../../Ion.Extensions.Rendering3D/Shaders/ion_fragment_inputs.glsl"

layout(location = 0) out vec4 outColor;

layout(set = 1, binding = 0) uniform Material
{
	vec4 uColorA;
	vec4 uColorB;
};

layout(set = 1, binding = 7) uniform sampler uMaterialSampler;

void main()
{
	float stripe = mod(floor(vWorldPosition.x), 2.0);
	outColor = mix(uColorA, uColorB, stripe);
}
