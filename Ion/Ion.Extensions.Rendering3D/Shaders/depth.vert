#version 450
// Ion 3D: depth-only vertex stage for the shadow map and the depth prepass (no fragment stage). Only the view block's
// first matrix is read, so the shadow pass binds a smaller view layout with just the uniform block.

#include "ion_mesh_inputs.glsl"

layout(set = 0, binding = 0) uniform View
{
	mat4 uViewProjection;
};

void main()
{
	gl_Position = uViewProjection * vec4(ionWorldPosition(inPosition), 1.0);
}
