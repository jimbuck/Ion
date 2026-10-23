#version 450
// One quad per particle (a 4-vertex triangle strip per instance). The particle buffer is bound as an instance-rate vertex
// buffer, not read as a storage buffer: OpenGL ES 3.1 does not guarantee storage buffers in the vertex stage.

layout(location = 0) in vec4 inState;   // position, velocity
layout(location = 1) in vec4 inColor;   // color, life

layout(set = 0, binding = 0) uniform View
{
	vec2 scale;   // clip space per world unit (x divided by the aspect ratio)
	float size;   // half size of a quad in world units at full life
	float pad;
} uView;

layout(location = 0) out vec2 vCorner;
layout(location = 1) out vec4 vColor;

void main()
{
	vec2 corner = vec2(float(gl_VertexIndex & 1) * 2.0 - 1.0, float((gl_VertexIndex >> 1) & 1) * 2.0 - 1.0);
	float life = clamp(inColor.a, 0.0, 1.0);
	vec2 position = inState.xy + corner * uView.size * (0.35 + 0.65 * life);
	vCorner = corner;
	vColor = vec4(inColor.rgb, life);
	gl_Position = vec4(position * uView.scale, 0.0, 1.0);
}
