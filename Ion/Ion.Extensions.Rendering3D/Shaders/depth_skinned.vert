#version 450
// Ion 3D: the skinned depth-only vertex stage (shadow map and depth prepass): depth.vert with every vertex first moved by
// its joints (ion_skinning.glsl). Both view layouts carry the joint palette at bindings 5 and 6.

#include "ion_mesh_inputs.glsl"
#include "ion_skinning.glsl"

layout(set = 0, binding = 0) uniform View
{
	mat4 uViewProjection;
};

void main()
{
	vec4 r0, r1, r2;
	ionSkinMatrix(r0, r1, r2);
	gl_Position = uViewProjection * vec4(ionWorldPosition(ionSkinPoint(r0, r1, r2, inPosition)), 1.0);
}
