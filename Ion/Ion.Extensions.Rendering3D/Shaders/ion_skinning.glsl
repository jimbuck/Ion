// Ion 3D: skinning, for the skinned vertex stages (include after ion_mesh_inputs.glsl). Slot 2 is the mesh's skinning
// stream (Ion.Extensions.Rendering3D.SkinVertex, 24 bytes); the joint matrices of every skinned draw of the frame are in
// the palette texture (Ion.Extensions.Rendering3D.JointPalette): three texels per joint (the rows of the matrix, like the
// instance's world matrix), ION_PALETTE_ROW_JOINTS joints per row, the draw's first joint in the second normal row's w.

#define ION_PALETTE_ROW_JOINTS 256

// The four joints, as unorm16 (index / 65535), and their weights.
layout(location = 12) in vec4 inJoints;
layout(location = 13) in vec4 inWeights;

layout(set = 0, binding = 5) uniform texture2D uJoints;
layout(set = 0, binding = 6) uniform sampler uJointSampler;

vec4 ionJointRow(int joint, int row)
{
	ivec2 texel = ivec2((joint % ION_PALETTE_ROW_JOINTS) * 3 + row, joint / ION_PALETTE_ROW_JOINTS);
	return texelFetch(sampler2D(uJoints, uJointSampler), texel, 0);
}

// The vertex's skinning matrix, as three rows: its joints' matrices blended by its weights.
void ionSkinMatrix(out vec4 r0, out vec4 r1, out vec4 r2)
{
	int first = int(inNormal1.w + 0.5);
	ivec4 joints = ivec4(inJoints * 65535.0 + 0.5) + ivec4(first);
	r0 = inWeights.x * ionJointRow(joints.x, 0) + inWeights.y * ionJointRow(joints.y, 0) + inWeights.z * ionJointRow(joints.z, 0) + inWeights.w * ionJointRow(joints.w, 0);
	r1 = inWeights.x * ionJointRow(joints.x, 1) + inWeights.y * ionJointRow(joints.y, 1) + inWeights.z * ionJointRow(joints.z, 1) + inWeights.w * ionJointRow(joints.w, 1);
	r2 = inWeights.x * ionJointRow(joints.x, 2) + inWeights.y * ionJointRow(joints.y, 2) + inWeights.z * ionJointRow(joints.z, 2) + inWeights.w * ionJointRow(joints.w, 2);
}

vec3 ionSkinPoint(vec4 r0, vec4 r1, vec4 r2, vec3 p)
{
	vec4 h = vec4(p, 1.0);
	return vec3(dot(r0, h), dot(r1, h), dot(r2, h));
}

vec3 ionSkinDirection(vec4 r0, vec4 r1, vec4 r2, vec3 d)
{
	return vec3(dot(r0.xyz, d), dot(r1.xyz, d), dot(r2.xyz, d));
}
