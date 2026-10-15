#version 450
// Ion 3D: the skinned mesh vertex stage: mesh.vert with every vertex first moved by its joints (ion_skinning.glsl), so
// the unlit, PBR and custom fragment stages draw skinned meshes unchanged. Normals and tangents are moved by the blended
// matrix's 3x3 part (exact for rotations and uniform scales; the fragment stage normalizes).

#include "ion_view.glsl"
#include "ion_mesh_inputs.glsl"
#include "ion_skinning.glsl"

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
	vec4 r0, r1, r2;
	ionSkinMatrix(r0, r1, r2);
	vec3 world = ionWorldPosition(ionSkinPoint(r0, r1, r2, inPosition));
	vWorldPosition = world;
	vNormal = ionWorldNormal(ionSkinDirection(r0, r1, r2, inNormal));
	vTangent = vec4(ionWorldDirection(ionSkinDirection(r0, r1, r2, inTangent.xyz)), inTangent.w);
	vUv0 = inUv0;
	vUv1 = inUv1;
	vColor = inColor;
	vFlags = vec4(inNormal0.w, 0.0, 0.0, 0.0);
	gl_Position = uViewProjection * vec4(world, 1.0);
}
