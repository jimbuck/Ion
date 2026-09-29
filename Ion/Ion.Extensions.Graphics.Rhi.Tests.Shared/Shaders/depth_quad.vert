#version 450
// Positions in clip space with their depth, for depth-only passes.

layout(location = 0) in vec3 inPosition;

void main()
{
	gl_Position = vec4(inPosition, 1.0);
}
