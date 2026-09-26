#version 450
// Ion 3D: the metallic-roughness PBR material (glTF 2.0 core). Cook-Torrance specular with the GGX distribution,
// Smith-GGX height-correlated visibility and Schlick Fresnel; Lambert diffuse. The main directional light (with the shadow
// map), up to 8 point, spot and extra directional lights, and ambient light from the environment cube map (diffuse from
// its smallest mips, specular by roughness) or the flat ambient color.

#include "ion_view.glsl"
#include "ion_fragment_inputs.glsl"

layout(location = 0) out vec4 outColor;

// Mirrors Ion.Extensions.Rendering3D.MaterialUniforms (std140).
layout(set = 1, binding = 0) uniform Material
{
	// Linear RGBA.
	vec4 uBaseColor;
	// rgb: linear emissive color times intensity, w: normal scale.
	vec4 uEmissive;
	// x: metallic, y: roughness, z: occlusion strength, w: alpha cutoff.
	vec4 uParams;
	// x: alpha mode (0 opaque, 1 mask, 2 blend), y: 1 when double sided.
	vec4 uFlags;
};

layout(set = 1, binding = 1) uniform texture2D uBaseColorTexture;
layout(set = 1, binding = 2) uniform texture2D uMetallicRoughnessTexture;
layout(set = 1, binding = 3) uniform texture2D uNormalTexture;
layout(set = 1, binding = 4) uniform texture2D uOcclusionTexture;
layout(set = 1, binding = 5) uniform texture2D uEmissiveTexture;
layout(set = 1, binding = 7) uniform sampler uMaterialSampler;

const float PI = 3.14159265359;

float distributionGgx(float nDotH, float alpha)
{
	float a2 = alpha * alpha;
	float d = nDotH * nDotH * (a2 - 1.0) + 1.0;
	return a2 / (PI * d * d);
}

float visibilitySmithGgx(float nDotL, float nDotV, float alpha)
{
	float a2 = alpha * alpha;
	float v = nDotL * sqrt(nDotV * nDotV * (1.0 - a2) + a2);
	float l = nDotV * sqrt(nDotL * nDotL * (1.0 - a2) + a2);
	float sum = v + l;
	return sum > 0.0 ? 0.5 / sum : 0.0;
}

vec3 fresnelSchlick(float vDotH, vec3 f0)
{
	return f0 + (1.0 - f0) * pow(1.0 - vDotH, 5.0);
}

// The light reflected towards the viewer from one light of radiance `radiance` arriving from direction `l`.
vec3 shade(vec3 n, vec3 v, vec3 l, vec3 radiance, vec3 diffuseColor, vec3 f0, float alpha)
{
	float nDotL = clamp(dot(n, l), 0.0, 1.0);
	if (nDotL <= 0.0) return vec3(0.0);
	vec3 h = normalize(l + v);
	float nDotV = clamp(abs(dot(n, v)), 0.001, 1.0);
	float nDotH = clamp(dot(n, h), 0.0, 1.0);
	float vDotH = clamp(dot(v, h), 0.0, 1.0);
	vec3 f = fresnelSchlick(vDotH, f0);
	vec3 specular = f * distributionGgx(nDotH, alpha) * visibilitySmithGgx(nDotL, nDotV, alpha);
	vec3 diffuse = (1.0 - f) * diffuseColor / PI;
	return (diffuse + specular) * radiance * nDotL;
}

float rangeAttenuation(float distance, float range)
{
	// glTF KHR_lights_punctual's recommended falloff: inverse square, windowed to reach zero at the range.
	float ratio = distance / max(range, 0.0001);
	float window = clamp(1.0 - ratio * ratio * ratio * ratio, 0.0, 1.0);
	return window * window / max(distance * distance, 0.0001);
}

void main()
{
	vec4 baseTexel = texture(sampler2D(uBaseColorTexture, uMaterialSampler), vUv0);
	vec4 baseColor = uBaseColor * vec4(ionSrgbToLinear(baseTexel.rgb), baseTexel.a) * vec4(ionSrgbToLinear(vColor.rgb), vColor.a);
	if (uFlags.x > 0.5 && uFlags.x < 1.5 && baseColor.a < uParams.w) discard;

	vec4 mr = texture(sampler2D(uMetallicRoughnessTexture, uMaterialSampler), vUv0);
	float metallic = clamp(uParams.x * mr.b, 0.0, 1.0);
	float roughness = clamp(uParams.y * mr.g, 0.04, 1.0);
	float alpha = roughness * roughness;

	// The normal: the interpolated one, flipped for back faces of double-sided materials, perturbed by the normal map.
	vec3 n = normalize(vNormal);
	if (uFlags.y > 0.5 && !gl_FrontFacing) n = -n;
	vec3 t = vTangent.xyz - n * dot(n, vTangent.xyz);
	if (dot(t, t) > 1e-8)
	{
		t = normalize(t);
		vec3 b = cross(n, t) * (vTangent.w < 0.0 ? -1.0 : 1.0);
		vec3 mapped = texture(sampler2D(uNormalTexture, uMaterialSampler), vUv0).xyz * 2.0 - 1.0;
		mapped.xy *= uEmissive.w;
		n = normalize(mat3(t, b, n) * mapped);
	}

	vec3 v = normalize(uCameraPosition.xyz - vWorldPosition);
	vec3 f0 = mix(vec3(0.04), baseColor.rgb, metallic);
	vec3 diffuseColor = baseColor.rgb * (1.0 - metallic);

	vec3 color = vec3(0.0);

	// The main directional light, with the shadow map.
	if (uLightDirection.w > 0.5)
	{
		vec3 l = -normalize(uLightDirection.xyz);
		color += shade(n, v, l, uLightColor.rgb, diffuseColor, f0, alpha) * ionShadow(vWorldPosition, normalize(vNormal));
	}

	// Point, spot and extra directional lights.
	int count = int(uCounts.x);
	for (int i = 0; i < ION_MAX_LIGHTS; i++)
	{
		if (i >= count) break;
		IonLight light = uLights[i];
		vec3 l;
		float attenuation = 1.0;
		if (light.colorType.w > 1.5)
		{
			l = -normalize(light.direction.xyz);
		}
		else
		{
			vec3 toLight = light.positionRange.xyz - vWorldPosition;
			float distance = length(toLight);
			l = toLight / max(distance, 0.0001);
			attenuation = rangeAttenuation(distance, light.positionRange.w);
			if (light.colorType.w > 0.5)
			{
				float cosAngle = dot(-l, normalize(light.direction.xyz));
				float cone = clamp((cosAngle - light.spot.x) * light.spot.y, 0.0, 1.0);
				attenuation *= cone * cone;
			}
		}

		color += shade(n, v, l, light.colorType.rgb * attenuation, diffuseColor, f0, alpha);
	}

	// Ambient: the environment map (diffuse from its blurriest mip, specular along the reflection by roughness) or the
	// flat ambient color, darkened by the occlusion map.
	float occlusion = mix(1.0, texture(sampler2D(uOcclusionTexture, uMaterialSampler), vUv0).r, uParams.z);
	float nDotV = clamp(dot(n, v), 0.0, 1.0);
	vec3 fAmbient = f0 + (max(vec3(1.0 - roughness), f0) - f0) * pow(1.0 - nDotV, 5.0);
	vec3 ambientDiffuse = uAmbient.rgb;
	vec3 ambientSpecular = uAmbient.rgb;
	if (uAmbient.w > 0.0)
	{
		float maxMip = uCounts.y;
		vec3 r = reflect(-v, n);
		ambientDiffuse += ionSrgbToLinear(textureLod(samplerCube(uEnvironment, uEnvironmentSampler), ionEnvironmentDirection(n), maxMip).rgb) * uAmbient.w;
		ambientSpecular += ionSrgbToLinear(textureLod(samplerCube(uEnvironment, uEnvironmentSampler), ionEnvironmentDirection(r), roughness * maxMip).rgb) * uAmbient.w;
	}

	color += (diffuseColor * ambientDiffuse * (1.0 - fAmbient) + ambientSpecular * fAmbient) * occlusion;

	vec3 emissive = uEmissive.rgb * ionSrgbToLinear(texture(sampler2D(uEmissiveTexture, uMaterialSampler), vUv0).rgb);
	color += emissive;

	outColor = ionOutput(color, uFlags.x > 1.5 ? baseColor.a : 1.0);
}
