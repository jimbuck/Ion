#version 450
// The lower-left half of the target: one triangle whose long edge runs from the top-left corner to the bottom-right.

void main()
{
	vec2 corners[3] = vec2[3](vec2(-1.0, -1.0), vec2(1.0, -1.0), vec2(-1.0, 1.0));
	gl_Position = vec4(corners[gl_VertexIndex], 0.0, 1.0);
}
