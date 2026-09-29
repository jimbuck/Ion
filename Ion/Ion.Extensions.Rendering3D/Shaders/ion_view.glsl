// Ion 3D: the view bind group (group 0), shared by every 3D shader and by custom material shaders.
// Mirrors Ion.Extensions.Rendering3D.ViewUniforms (std140). Matrices are uploaded from System.Numerics (row-vector
// convention), which GLSL reads as their transposes, so `matrix * vec4(p, 1.0)` applies them as the engine does.

#define ION_MAX_LIGHTS 8
#define ION_LIGHT_POINT 0.0
#define ION_LIGHT_SPOT 1.0
#define ION_LIGHT_DIRECTIONAL 2.0

struct IonLight
{
	// xyz: position, w: range.
	vec4 positionRange;
	// rgb: linear color times intensity, w: type (ION_LIGHT_*).
	vec4 colorType;
	// xyz: the direction the light travels (spot, directional), w: unused.
	vec4 direction;
	// x: cosine of the outer cone angle, y: 1 / (cos inner - cos outer) (spot).
	vec4 spot;
};

layout(set = 0, binding = 0) uniform View
{
	mat4 uViewProjection;
	mat4 uView;
	mat4 uProjection;
	mat4 uInverseViewProjection;
	// World to shadow map: xy the texture coordinates (row 0 at the top), z the depth to compare.
	mat4 uShadowMatrix;
	// xyz: camera position, w: time in seconds.
	vec4 uCameraPosition;
	// rgb: linear ambient color times intensity, w: environment map intensity (0: no environment map).
	vec4 uAmbient;
	// x: 1 when the shadow map is valid, y: depth bias, z: normal offset in world units, w: 1 / shadow map size.
	vec4 uShadowParams;
	// xyz: the direction the main directional light travels, w: 1 when there is one.
	vec4 uLightDirection;
	// rgb: linear color times intensity of the main directional light.
	vec4 uLightColor;
	// The clear color as written to the target (already encoded).
	vec4 uClearColor;
	// x: local light count, y: environment map mip count - 1, z: 1 to encode the output as sRGB, w: 1 when the background
	// is the clear color (no skybox for this camera).
	vec4 uCounts;
	IonLight uLights[ION_MAX_LIGHTS];
};

layout(set = 0, binding = 1) uniform texture2D uShadowMap;
layout(set = 0, binding = 2) uniform samplerShadow uShadowSampler;
layout(set = 0, binding = 3) uniform textureCube uEnvironment;
layout(set = 0, binding = 4) uniform sampler uEnvironmentSampler;

// Cube maps follow the GL/Vulkan face convention, which is left-handed: flipping z makes a camera looking down -Z (Ion's
// forward) see the +Z (front) face, unmirrored.
vec3 ionEnvironmentDirection(vec3 d)
{
	return vec3(d.x, d.y, -d.z);
}

vec3 ionSrgbToLinear(vec3 c)
{
	return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(vec3(0.04045), c));
}

vec3 ionLinearToSrgb(vec3 c)
{
	c = clamp(c, 0.0, 1.0);
	return mix(c * 12.92, 1.055 * pow(c, vec3(1.0 / 2.4)) - 0.055, step(vec3(0.0031308), c));
}

// The final color as stored in the target: sRGB encoded unless the target format encodes itself.
vec4 ionOutput(vec3 linearColor, float alpha)
{
	vec3 c = uCounts.z > 0.5 ? ionLinearToSrgb(linearColor) : clamp(linearColor, 0.0, 1.0);
	return vec4(c, alpha);
}
