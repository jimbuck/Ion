#version 450
// Sprite batch v2, vertex stage. One instance per sprite from a per-instance vertex buffer (no storage buffer, so the
// GLES 3.10 translation works on Mali-G31); the quad's four corners come from gl_VertexIndex (triangle strip, 4 vertices).
// Clip space is the RHI's (WebGPU): y up, depth in [0, 1].

// The sprite: its local (0, 0) corner in world units, and the two edges of the quad (size and rotation folded in).
layout(location = 0) in vec2 inPosition;
layout(location = 1) in vec2 inAxisX;
layout(location = 2) in vec2 inAxisY;
// The source rectangle in normalized texture coordinates: (u0, v0, u1, v1), swapped for flips.
layout(location = 3) in vec4 inUv;
// The tint, straight alpha.
layout(location = 4) in vec4 inColor;
// The depth (a sort key; written to gl_Position.z clamped to [0, 1]).
layout(location = 5) in float inDepth;

layout(location = 0) out vec2 vUv;
layout(location = 1) out vec4 vColor;

layout(set = 0, binding = 0) uniform Camera
{
	// World to clip space.
	mat4 uTransform;
	// x: 1 to premultiply the tint by its alpha (every blend mode but NonPremultiplied).
	vec4 uParams;
};

void main()
{
	vec2 corner = vec2(float(gl_VertexIndex & 1), float(gl_VertexIndex >> 1));
	vec2 world = inPosition + inAxisX * corner.x + inAxisY * corner.y;

	vUv = mix(inUv.xy, inUv.zw, corner);
	vec4 color = inColor;
	if (uParams.x > 0.5)
	{
		color.rgb *= color.a;
	}
	vColor = color;

	gl_Position = uTransform * vec4(world, 0.0, 1.0);
	gl_Position.z = clamp(inDepth, 0.0, 1.0) * gl_Position.w;
}
