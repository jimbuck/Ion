// Ion 3D: the vertex inputs of every mesh draw. Slot 0 is the mesh's vertex buffer (Ion.Extensions.Graphics.MeshVertex,
// 60 bytes), slot 1 the frame's per-instance ring (Ion.Extensions.Rendering3D.InstanceData, 96 bytes). Instance data
// comes through an instance-rate vertex buffer, not a storage buffer, so the GLES 3.x translation runs on Mali-G31.

layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec3 inNormal;
layout(location = 2) in vec4 inTangent;
layout(location = 3) in vec2 inUv0;
layout(location = 4) in vec2 inUv1;
layout(location = 5) in vec4 inColor;

// The world matrix's first three columns (world = (dot(c0, p), dot(c1, p), dot(c2, p)) for p = (position, 1)).
layout(location = 6) in vec4 inWorld0;
layout(location = 7) in vec4 inWorld1;
layout(location = 8) in vec4 inWorld2;
// The normal matrix's rows (the cofactors of the world matrix's 3x3 part, normalized after use); w of the first: flags
// (1: receives shadows).
layout(location = 9) in vec4 inNormal0;
layout(location = 10) in vec4 inNormal1;
layout(location = 11) in vec4 inNormal2;

vec3 ionWorldPosition(vec3 p)
{
	vec4 h = vec4(p, 1.0);
	return vec3(dot(inWorld0, h), dot(inWorld1, h), dot(inWorld2, h));
}

vec3 ionWorldNormal(vec3 n)
{
	return vec3(dot(inNormal0.xyz, n), dot(inNormal1.xyz, n), dot(inNormal2.xyz, n));
}

vec3 ionWorldDirection(vec3 d)
{
	return vec3(dot(inWorld0.xyz, d), dot(inWorld1.xyz, d), dot(inWorld2.xyz, d));
}
