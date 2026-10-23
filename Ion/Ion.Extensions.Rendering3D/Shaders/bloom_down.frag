#version 450
// Ion 3D bloom: one downsampling step of the chain (each level half the size of the one before).

#include "ion_post.glsl"

void main()
{
	outColor = vec4(ionPostDownsample(vUv), 1.0);
}
