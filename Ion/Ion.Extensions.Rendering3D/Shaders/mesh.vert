#version 450
// Ion 3D: the standard mesh vertex stage, shared by the unlit, PBR and custom material shaders. Transforms by the
// instance's world matrix and passes world-space attributes to the fragment stage.

#include "ion_view.glsl"
#include "ion_mesh_inputs.glsl"

layout(location = 0) out vec3 vWorldPosition;
layout(location = 1) out vec3 vNormal;
layout(location = 2) out vec4 vTangent;
layout(location = 3) out vec2 vUv0;
layout(location = 4) out vec2 vUv1;
layout(location = 5) out vec4 vColor;
// x: 1 when the object receives shadows.
layout(location = 6) out vec4 vFlags;

void main()
{
	vec3 world = ionWorldPosition(inPosition);
	vWorldPosition = world;
	vNormal = ionWorldNormal(inNormal);
	vTangent = vec4(ionWorldDirection(inTangent.xyz), inTangent.w);
	vUv0 = inUv0;
	vUv1 = inUv1;
	vColor = inColor;
	vFlags = vec4(inNormal0.w, 0.0, 0.0, 0.0);
	gl_Position = uViewProjection * vec4(world, 1.0);
}
