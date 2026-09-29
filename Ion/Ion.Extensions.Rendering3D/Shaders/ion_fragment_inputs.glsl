// Ion 3D: the fragment inputs written by mesh.vert.

layout(location = 0) in vec3 vWorldPosition;
layout(location = 1) in vec3 vNormal;
layout(location = 2) in vec4 vTangent;
layout(location = 3) in vec2 vUv0;
layout(location = 4) in vec2 vUv1;
layout(location = 5) in vec4 vColor;
layout(location = 6) in vec4 vFlags;

// The shadow factor of the main directional light at a world position with a world normal: 1 lit, 0 in shadow.
// 3x3 taps of a linear comparison sampler (each a 2x2 PCF in hardware), with a normal offset and a depth bias.
float ionShadow(vec3 worldPosition, vec3 normal)
{
	if (uShadowParams.x < 0.5 || vFlags.x < 0.5) return 1.0;
	vec3 lightDirection = -uLightDirection.xyz;
	float slope = 1.0 - clamp(dot(normal, lightDirection), 0.0, 1.0);
	vec3 offsetPosition = worldPosition + normal * uShadowParams.z * (0.5 + slope);
	vec4 shadow = uShadowMatrix * vec4(offsetPosition, 1.0);
	vec3 coords = shadow.xyz / shadow.w;
	if (coords.x <= 0.0 || coords.x >= 1.0 || coords.y <= 0.0 || coords.y >= 1.0 || coords.z >= 1.0) return 1.0;
	float depth = coords.z - uShadowParams.y;
	float texel = uShadowParams.w;
	float sum = 0.0;
	for (int y = -1; y <= 1; y++)
	{
		for (int x = -1; x <= 1; x++)
		{
			sum += texture(sampler2DShadow(uShadowMap, uShadowSampler), vec3(coords.xy + vec2(float(x), float(y)) * texel, depth));
		}
	}

	return sum / 9.0;
}
