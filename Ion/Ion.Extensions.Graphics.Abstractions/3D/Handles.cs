namespace Ion.Extensions.Graphics;

/*
 * Handles to renderer-owned resources. They are plain integer ids (not references) so the components that hold them
 * (MeshRenderer, Camera, SceneEnvironment) stay unmanaged and can live in ECS chunks. Id 0 is "none"; the renderer hands
 * out ids from 1 and never reuses an id while the resource lives.
 */

/// <summary>A mesh created by <see cref="IRenderer3D.CreateMesh"/>. <c>default</c> is no mesh.</summary>
/// <param name="Id">The renderer's id (0: none).</param>
public readonly record struct MeshHandle(int Id)
{
	/// <summary>No mesh.</summary>
	public static MeshHandle None => default;

	/// <summary>True for a handle that names a mesh.</summary>
	public bool IsValid => Id > 0;
}

/// <summary>A material created by one of the <c>IRenderer3D.CreateMaterial</c> overloads. <c>default</c> is no material (the renderer's default white material is used).</summary>
/// <param name="Id">The renderer's id (0: none).</param>
public readonly record struct MaterialHandle(int Id)
{
	/// <summary>No material.</summary>
	public static MaterialHandle None => default;

	/// <summary>True for a handle that names a material.</summary>
	public bool IsValid => Id > 0;
}

/// <summary>A texture (2D or cube map) registered with <see cref="IRenderer3D"/>. <c>default</c> is no texture.</summary>
/// <param name="Id">The renderer's id (0: none).</param>
public readonly record struct TextureHandle(int Id)
{
	/// <summary>No texture.</summary>
	public static TextureHandle None => default;

	/// <summary>True for a handle that names a texture.</summary>
	public bool IsValid => Id > 0;
}

/// <summary>A render target registered with <see cref="IRenderer3D.CreateRenderTarget(uint, uint, string?)"/>. <c>default</c> is the frame (the window or offscreen target).</summary>
/// <param name="Id">The renderer's id (0: the frame).</param>
public readonly record struct RenderTargetHandle(int Id)
{
	/// <summary>The frame's own target.</summary>
	public static RenderTargetHandle None => default;

	/// <summary>True for a handle that names a render target (false for the frame).</summary>
	public bool IsValid => Id > 0;
}

/// <summary>A custom material shader created by <see cref="IRenderer3D.CreateMaterialShader"/>. <c>default</c> is none.</summary>
/// <param name="Id">The renderer's id (0: none).</param>
public readonly record struct MaterialShaderHandle(int Id)
{
	/// <summary>True for a handle that names a shader.</summary>
	public bool IsValid => Id > 0;
}
