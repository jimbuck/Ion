// Ion 3D: the fragment inputs written by mesh.vert.

layout(location = 0) in vec3 vWorldPosition;
layout(location = 1) in vec3 vNormal;
layout(location = 2) in vec4 vTangent;
layout(location = 3) in vec2 vUv0;
layout(location = 4) in vec2 vUv1;
layout(location = 5) in vec4 vColor;
layout(location = 6) in vec4 vFlags;

// The tile of the shadow map atlas a cascade uses (minimum and maximum texture coordinates): the whole map for a single
// cascade, a quarter (2x2) otherwise.
vec4 ionCascadeRect(int cascade, int count)
{
	if (count <= 1) return vec4(0.0, 0.0, 1.0, 1.0);
	vec2 tile = vec2(float(cascade % 2), float(cascade / 2)) * 0.5;
	return vec4(tile, tile + 0.5);
}

// One cascade's shadow factor: 3x3 taps of a linear comparison sampler (each a 2x2 PCF in hardware), with a normal offset
// and a depth bias, kept inside the cascade's tile. 1 (lit) outside it.
float ionShadowCascade(int cascade, int count, vec3 worldPosition, vec3 normal, float slope)
{
	vec4 params = uCascadeParams[cascade];
	vec3 offsetPosition = worldPosition + normal * params.y * (0.5 + slope);
	vec4 shadow = uShadowMatrices[cascade] * vec4(offsetPosition, 1.0);
	vec3 coords = shadow.xyz / shadow.w;
	vec4 rect = ionCascadeRect(cascade, count);
	if (coords.x <= rect.x || coords.x >= rect.z || coords.y <= rect.y || coords.y >= rect.w || coords.z >= 1.0) return 1.0;
	float texel = uShadowParams.w;
	coords.xy = clamp(coords.xy, rect.xy + vec2(1.5 * texel), rect.zw - vec2(1.5 * texel));
	float depth = coords.z - params.x;
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

// The view depth where a cascade ends (no dynamic vector indexing: some GLES translators mishandle it).
float ionCascadeEnd(int cascade)
{
	if (cascade == 0) return uCascadeSplits.x;
	if (cascade == 1) return uCascadeSplits.y;
	if (cascade == 2) return uCascadeSplits.z;
	return uCascadeSplits.w;
}

// The cascade a world position falls in (by its view depth from the camera the shadow is fitted to), or the count when
// it is beyond the last. The splits ascend, so the index is the number of them the depth is past.
int ionShadowCascadeIndex(vec3 worldPosition, int count)
{
	float depth = dot(uShadowDepth.xyz, worldPosition) + uShadowDepth.w;
	return int(depth > uCascadeSplits.x) + int(count > 1 && depth > uCascadeSplits.y) + int(count > 2 && depth > uCascadeSplits.z)
		+ int(count > 3 && depth > uCascadeSplits.w);
}

// The shadow factor of the main directional light at a world position with a world normal: 1 lit, 0 in shadow. Picks
// the cascade by view depth; near a cascade's end blends into the next one (the last fades out).
float ionShadow(vec3 worldPosition, vec3 normal)
{
	int count = int(uShadowParams.x + 0.5);
	if (count == 0 || vFlags.x < 0.5) return 1.0;
	int cascade = ionShadowCascadeIndex(worldPosition, count);
	if (cascade >= count) return 1.0;
	vec3 lightDirection = -uLightDirection.xyz;
	float slope = 1.0 - clamp(dot(normal, lightDirection), 0.0, 1.0);
	float lit = ionShadowCascade(cascade, count, worldPosition, normal, slope);
	float depth = dot(uShadowDepth.xyz, worldPosition) + uShadowDepth.w;
	float blendStart = uCascadeParams[cascade].z;
	float end = ionCascadeEnd(cascade);
	if (depth > blendStart && end > blendStart)
	{
		float next = cascade + 1 < count ? ionShadowCascade(cascade + 1, count, worldPosition, normal, slope) : 1.0;
		lit = mix(lit, next, clamp((depth - blendStart) / (end - blendStart), 0.0, 1.0));
	}

	return lit;
}

// A tint showing which cascade covers a world position (red, green, blue, yellow from near to far; white beyond), when
// the renderer's ShowShadowCascades option is on.
vec3 ionShadowCascadeTint(vec3 worldPosition)
{
	int count = int(uShadowParams.x + 0.5);
	if (uShadowParams.y < 0.5 || count == 0) return vec3(1.0);
	int cascade = ionShadowCascadeIndex(worldPosition, count);
	if (cascade == 0) return vec3(1.0, 0.55, 0.55);
	if (cascade == 1) return vec3(0.55, 1.0, 0.55);
	if (cascade == 2) return vec3(0.55, 0.55, 1.0);
	if (cascade == 3) return vec3(1.0, 1.0, 0.5);
	return vec3(1.0);
}
