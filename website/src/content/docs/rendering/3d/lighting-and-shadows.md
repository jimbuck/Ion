---
title: Lighting and shadows
description: Light a 3D scene with directional, point and spot lights, shadow maps, ambient light and a skybox that provides image-based ambient.
sidebar:
  order: 3
---

The 3D renderer lights PBR materials with:

- one **main directional light** (the sun), which can cast shadows,
- up to **8 local lights** per camera: point lights, spot lights and extra directional lights,
- **ambient light**: a flat color, plus image-based ambient from the **skybox** when there is one.

Unlit materials ignore all of it. Lights are submitted every frame, like meshes; the environment (ambient and skybox)
persists until you change it.

```csharp
[Render]
public void Draw(GameTime dt)
{
	renderer.SetCamera(new Camera(), Transform.LookAt(new Vector3(0, 4, 10), Vector3.Zero));

	// The sun, shining down and away from the camera.
	renderer.AddLight(new DirectionalLight(new Color(0xFF, 0xF4, 0xE0), intensity: 3f), new Vector3(-0.5f, -0.8f, -0.3f));

	// A warm point light near the ground.
	renderer.AddLight(new PointLight(new Color(0xFF, 0x90, 0x40), intensity: 2.5f, range: 5f), new Vector3(2.5f, 1.8f, 1.5f));

	renderer.Draw(_ground, _groundMaterial, Matrix4x4.Identity);
	renderer.Draw(_statue, _stone, Matrix4x4.CreateTranslation(0, 0, 0));
}
```

All light colors are sRGB `Color`s, converted to linear by the renderer and multiplied by the intensity.

## Directional lights

`DirectionalLight` models a distant light such as the sun: parallel rays, no position, no falloff.

| Field | Default (`new DirectionalLight()`) | Meaning |
|---|---|---|
| `Color` | `Color.White` | sRGB |
| `Intensity` | `1` | 1 gives the color at full strength on a surface facing the light |
| `CastShadows` | `true` | Whether this light casts shadows (only the main light can) |
| `ShadowBias` | `0` (the renderer's default, 1.5 texels) | Depth bias in shadow map texels |

The constructor takes the same values: `new DirectionalLight(color, intensity: 1f, castShadows: true)`.

Two ways to submit one:

```csharp
// By direction: the way the light travels.
renderer.AddLight(new DirectionalLight(Color.White, 2f), direction: new Vector3(-0.3f, -1f, -0.2f));

// By world matrix: the light shines along the matrix's -Z axis.
var sun = new Transform(Vector3.Zero, Transform.LookRotation(new Vector3(-0.65f, -0.6f, -0.3f), Vector3.UnitY));
renderer.AddLight(new DirectionalLight(Color.White, 3f), sun.ToMatrix());
```

The direction is normalized for you; a zero direction becomes straight down.

**The first directional light submitted in a frame with `CastShadows` set is the main light**, and it is shaded with the
shadow map. When no directional light casts shadows (or `Ion:Rendering3D:Shadows` is off), the first directional light
submitted is the main light instead. The other directional lights are added to the local light list (without shadows).

## Point lights

A `PointLight` radiates from a position and fades to zero at `Range`:

| Field | Default | Meaning |
|---|---|---|
| `Color` | `Color.White` | sRGB |
| `Intensity` | `1` | Strength |
| `Range` | `10` | The distance at which the light has faded out (0 or less: 10) |

```csharp
renderer.AddLight(new PointLight(new Color(0x50, 0x90, 0xFF), intensity: 2.5f, range: 5f), position: new Vector3(-2.5f, 1.2f, 1f));
```

The falloff is glTF's windowed inverse square: physically plausible near the light, reaching exactly zero at `Range`.

## Spot lights

A `SpotLight` is a cone along the -Z axis of its world matrix, full strength inside the inner angle, fading to zero at
the outer angle and at `Range`:

| Field | Default | Meaning |
|---|---|---|
| `Color`, `Intensity` | white, `1` | |
| `Range` | `10` | Fade-out distance |
| `InnerConeAngle` | 20 degrees (`PI / 9`) | Half angle in radians of full strength |
| `OuterConeAngle` | 30 degrees (`PI / 6`) | Half angle in radians beyond which there is no light |

```csharp
// A flashlight held by the camera.
var flashlight = new SpotLight { Color = Color.White, Intensity = 4f, Range = 15f, InnerConeAngle = 0.2f, OuterConeAngle = 0.35f };
renderer.AddLight(flashlight, _cameraTransform.ToMatrix());
```

## How many lights

Per camera, the renderer fills up to **8 local lights**: extra directional lights first (they light everything), then
point and spot lights in submission order whose range sphere intersects the camera's frustum. Lights beyond 8 are
dropped for that camera. For scenes with many small lights, submit only the ones near the action, most important
first.

`LastFrameStatistics.Lights` reports the lights used.

## Shadows

The main directional light renders a shadow map when:

- `Ion:Rendering3D:Shadows` is `true` (the default),
- at least one directional light of the frame has `CastShadows` set (the default; the first such light gets the map), and
- at least one camera renders.

| Option (`Ion:Rendering3D:...`) | Default | Effect |
|---|---|---|
| `Shadows` | `true` | Turn shadows off entirely |
| `ShadowMapSize` | `2048` | Resolution in texels. Higher is sharper and costs more memory and fill rate |
| `ShadowDistance` | `40` | How far from the camera shadows reach, in world units |

How the shadow map is fitted: the renderer takes the first frame camera's view frustum up to `ShadowDistance`, covers
its bounding sphere with an orthographic projection along the light, and snaps it to whole shadow map texels, so shadow
edges do not shimmer as the camera moves. The depth range is extended back to every caster whose footprint overlaps
the area, so objects outside the view still cast shadows into it.

Sampling uses a comparison sampler with 3x3 taps, each a hardware 2x2 PCF, plus a normal offset and the depth bias, for
soft-edged shadows without acne.

Per object, `MeshRenderer.CastShadows` and `ReceiveShadows` control participation:

```csharp
// A ground plane that receives shadows but never casts them, and a glowing orb that casts none.
renderer.Submit(new MeshRenderer(_ground, _grass) { CastShadows = false }, Matrix4x4.Identity);
renderer.Submit(new MeshRenderer(_orb, _glow) { CastShadows = false, ReceiveShadows = false }, orbWorld);
```

### Tuning shadows

| Symptom | Fix |
|---|---|
| Blurry, blocky shadows | Lower `ShadowDistance` (the map covers less area) or raise `ShadowMapSize` |
| Shadows vanish in the distance | Raise `ShadowDistance` |
| Speckled self-shadowing ("acne") | Raise `DirectionalLight.ShadowBias` (texels, default 1.5) |
| Shadows detached from their casters ("peter-panning") | Lower `ShadowBias` |

The samples pick `ShadowDistance` to fit their scenes: 20 for the Model sample, 60 for the Cubes sample.

:::note[Shadow limits]
One shadowed directional light with one cascade. Point and spot lights cast no shadows. Blended (`AlphaMode.Blend`)
objects cast no shadow, and masked objects cast the shadow of their full, unmasked shape.
:::

## Ambient light and the environment

`SceneEnvironment` holds the frame's ambient light and optional skybox. Unlike everything else, it persists: set it once
(for example in `[Init]`) and change it when the mood changes.

| Field | Default (`new SceneEnvironment()`) | Meaning |
|---|---|---|
| `AmbientColor` | dim grey (0.25) | sRGB |
| `AmbientIntensity` | `1` | Multiplier of `AmbientColor` |
| `Skybox` | none | A cube map `TextureHandle` (from `ICubemap.Handle`) |
| `SkyboxIntensity` | `1` | Multiplier of the skybox, as drawn and as ambient light |

```csharp
renderer.SetEnvironment(new SceneEnvironment { AmbientColor = new Color(0x9C, 0xB0, 0xD0), AmbientIntensity = 0.35f });
```

Ambient light is darkened by a material's occlusion map (`PbrMaterial.Occlusion`).

### Skybox and image-based ambient

Load a cube map from a folder of six images and put its handle in the environment. Set the camera's `Clear` to
`CameraClear.Skybox` to draw it behind the scene:

```csharp
[Init]
public void Init(GameTime dt)
{
	var skybox = assets.Load<ICubemap>("Skybox");   // Assets/Skybox/px.png, nx.png, py.png, ny.png, pz.png, nz.png
	renderer.SetEnvironment(new SceneEnvironment
	{
		AmbientColor = new Color(0x40, 0x48, 0x58),
		AmbientIntensity = 0.15f,
		Skybox = skybox.Handle,
		SkyboxIntensity = 1f,
	});
}

[Render]
public void Draw(GameTime dt)
{
	renderer.SetCamera(new Camera { Clear = CameraClear.Skybox }, Transform.LookAt(new Vector3(0, 2, 6.5f), new Vector3(0, 1.1f, 0)));
	// ...
}
```

The skybox also **lights the scene**: the PBR shader adds diffuse ambient from the cube map's smallest mip (a blurred
average of the sky) and specular reflections along the reflected view direction, from a sharper or blurrier mip
depending on roughness. Polished metals mirror the sky; rough surfaces pick up its overall tint. This is added to the
flat ambient color, so lower `AmbientIntensity` when you use a skybox, as the Model sample does.

The cube map faces are expected as `px nx py ny pz nz` (or `right left top bottom front back`) in `.png` or `.jpg`,
square and of one size. A camera looking down -Z (Ion's forward) sees the `pz` ("front") image unmirrored, `px` on its
right and `py` above. See [Models and glTF](/Ion/rendering/3d/models-gltf/#cube-maps) for the loader.

:::tip
To make the sun match a skybox's painted sun, point the directional light along the direction from the sun's position
in the sky toward the scene.
:::

## With the ECS

As components, lights take their position and direction from the entity's global transform:

| Component | Submitted as |
|---|---|
| `DirectionalLight` | Shining along the entity's -Z |
| `PointLight` | At the entity's position |
| `SpotLight` | At the entity's position, along its -Z |

The environment is a per-world singleton set with `world.SetEnvironment(...)` (read it back with `TryGetEnvironment`,
remove it with `RemoveEnvironment`). The extraction passes it to the renderer only when it changes.

```csharp
world.SetEnvironment(new SceneEnvironment { AmbientColor = new Color(0x9C, 0xB0, 0xD0), AmbientIntensity = 0.35f });

world.Create(new Transform(Vector3.Zero, Transform.LookRotation(new Vector3(-0.65f, -0.6f, -0.3f), Vector3.UnitY)),
	new DirectionalLight(new Color(0xFF, 0xF4, 0xE0), intensity: 3f), new EntityName("sun"));

world.Create(new Transform(new Vector3(2.5f, 1.8f, 1.5f)),
	new PointLight(new Color(0xFF, 0x90, 0x40), intensity: 2.5f, range: 5f), new EntityName("warm light"));
```

Because the shadow map goes to the first shadow-casting directional light submitted, in an ECS world with several
directional lights that cast shadows, which one gets it depends on query order. Set `CastShadows = false` on every
directional light except the one that should cast (fill and rim lights, for example), and it gets the shadow map
whatever the order.

## See also

- [Meshes and materials](/Ion/rendering/3d/meshes-and-materials/): PBR parameters
- [3D cameras](/Ion/rendering/3d/cameras/): clear modes and multiple cameras
- [Models and glTF](/Ion/rendering/3d/models-gltf/): loading cube maps
- [Model example](/Ion/examples/model/)
