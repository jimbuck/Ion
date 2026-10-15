#version 450
// A full-target triangle (3 vertices, no vertex buffer) with UVs whose row 0 is the top row of the target.

layout(location = 0) out vec2 vUv;

void main()
{
	vec2 p = vec2(float((gl_VertexIndex << 1) & 2), float(gl_VertexIndex & 2));
	vUv = vec2(p.x, 1.0 - p.y);
	gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);
}
