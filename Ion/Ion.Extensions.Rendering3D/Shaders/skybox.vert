#version 450
// Ion 3D: the background. A full-viewport triangle at the far plane (depth 1, drawn where the depth test LessEqual
// passes, behind everything) whose view direction is recovered from the inverse view-projection matrix.

#include "ion_view.glsl"

layout(location = 0) out vec3 vDirection;

void main()
{
	vec2 p = vec2(float((gl_VertexIndex << 1) & 2), float(gl_VertexIndex & 2)) * 2.0 - 1.0;
	vec4 farPoint = uInverseViewProjection * vec4(p, 1.0, 1.0);
	vDirection = farPoint.xyz / farPoint.w - uCameraPosition.xyz;
	gl_Position = vec4(p, 1.0, 1.0);
}
