using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Ion.Extensions.Graphics;
using Ion.Extensions.Graphics.Rhi;

namespace Ion.Extensions.Rendering3D;

/// <summary>The CPU half of the frame: extract, prepare, queue and sort.</summary>
public sealed partial class Renderer3D
{
	private GrowableArray<RenderObject> _objects = new(1024);
	private GrowableArray<CameraEntry> _cameras = new(4);
	private GrowableArray<DirectionalEntry> _directional = new(4);
	private GrowableArray<LocalLightEntry> _local = new(16);
	private GrowableArray<InstanceData> _instances = new(1024);
	private GrowableArray<Batch> _shadowBatches = new(64);
	private readonly ViewData[] _views;
	private int _viewCount;
	private Aabb[] _worldBounds = new Aabb[1024];
	private Aabb[] _casterBounds = new Aabb[1024];
	private int[] _casters = new int[1024];
	private ulong[] _keys = new ulong[1024];
	private int[] _items = new int[1024];
	private ulong[] _transparentKeys = new ulong[256];
	private int[] _transparentItems = new int[256];
	private readonly int[] _cameraOrder = new int[64];
	private readonly int[] _targetsSeen = new int[64];
	private RenderGraph _graph;
	private readonly PassSet _passes;
	private DirectionalShadow _shadow;
	private bool _shadowActive;
	private int _mainLight = -1;
	private int _shadowCasterCount;
	private int _frameViewIndex = -1;
	private long _frameNumber;
	private int _defaultMaterial;

	/// <summary>The target size assumed for cameras when there is no frame (the CPU-only renderer).</summary>
	public (uint Width, uint Height) CpuTargetSize { get; set; } = (1280, 720);

	/// <summary>The number of views (cameras) of the last frame.</summary>
	internal int ViewCount => _viewCount;

	/// <summary>The views of the last frame.</summary>
	internal ReadOnlySpan<ViewData> Views => _views.AsSpan(0, _viewCount);

	/// <summary>The instance data of the last frame, in batch order.</summary>
	internal ReadOnlySpan<InstanceData> Instances => _instances.Span;

	/// <summary>The shadow batches of the last frame.</summary>
	internal ReadOnlySpan<Batch> ShadowBatches => _shadowBatches.Span;

	/// <summary>The directional shadow of the last frame (valid when <see cref="ShadowActive"/>).</summary>
	public DirectionalShadow Shadow => _shadow;

	/// <summary>Whether the last frame rendered a shadow map.</summary>
	public bool ShadowActive => _shadowActive;

	/// <summary>
	/// Starts a frame: forgets the previous frame's submissions (cameras, lights, mesh renderers). Called by the 3D
	/// renderer system when the Render stage opens.
	/// </summary>
	public void BeginFrame()
	{
		_objects.Clear();
		_cameras.Clear();
		_directional.Clear();
		_local.Clear();
	}

	/// <summary>
	/// Ends a frame: prepares, culls, sorts and batches what was submitted, then (with a device and a target) records and
	/// submits the render graph, the 2D overlay included. Called by the 3D renderer system when the Render stage closes.
	/// </summary>
	public void EndFrame()
	{
		var overlaySubmitted = false;
		try
		{
			Prepare();
			QueueAll();
			if (Device is not null && _frame is { IsRendering: true }) overlaySubmitted = RenderGpu();
		}
		finally
		{
			// The 2D overlay is part of the frame even when nothing 3D was drawn (or drawing failed).
			if (!overlaySubmitted) Overlay?.SubmitDeferred();
			_frameNumber++;
		}
	}

	/// <summary>Runs the CPU pipeline only (prepare, queue, sort and batch): for tests and benchmarks.</summary>
	internal void RunCpuPipeline()
	{
		Prepare();
		QueueAll();
		_frameNumber++;
	}

	// Prepare: world bounds, views, lights and the shadow fit.

	private void Prepare()
	{
		var objectCount = _objects.Count;
		if (_worldBounds.Length < objectCount) _worldBounds = new Aabb[Math.Max(objectCount, _worldBounds.Length * 2)];

		var meshes = CollectionsMarshal.AsSpan(_meshes);
		var objects = _objects.Span;
		var bounds = _worldBounds.AsSpan(0, objectCount);
		for (var i = 0; i < objects.Length; i++)
		{
			ref readonly var o = ref objects[i];
			var mesh = (uint)o.Mesh < (uint)meshes.Length ? meshes[o.Mesh] : null;
			bounds[i] = mesh is null ? Aabb.Empty : mesh.Bounds.Transform(o.World);
		}

		PrepareViews();
		PrepareLights();
	}

	private void PrepareViews()
	{
		// Order the cameras by priority, then submission order (insertion sort: a handful of cameras).
		var cameraCount = Math.Min(_cameras.Count, Math.Min(_views.Length, _cameraOrder.Length));
		var cameras = _cameras.Span;
		for (var i = 0; i < cameraCount; i++)
		{
			var j = i;
			while (j > 0 && cameras[_cameraOrder[j - 1]].Camera.Priority > cameras[i].Camera.Priority)
			{
				_cameraOrder[j] = _cameraOrder[j - 1];
				j--;
			}

			_cameraOrder[j] = i;
		}

		_viewCount = 0;
		_frameViewIndex = -1;
		var targetsSeen = 0;
		for (var c = 0; c < cameraCount; c++)
		{
			ref readonly var entry = ref cameras[_cameraOrder[c]];
			var camera = entry.Camera;
			uint width, height;
			var target = camera.Target.IsValid && camera.Target.Id < _renderTargets.Count && _renderTargets[camera.Target.Id] is { } rt ? camera.Target.Id : 0;
			if (target != 0)
			{
				var slot = _renderTargets[target]!;
				(width, height) = (slot.Width, slot.Height);
			}
			else if (_frame is not null)
			{
				(width, height) = (_frame.Width, _frame.Height);
			}
			else
			{
				(width, height) = CpuTargetSize;
			}

			if (width == 0 || height == 0) continue;

			var view = _views[_viewCount];
			view.CameraIndex = _cameraOrder[c];
			view.Camera = camera;
			view.Target = target;
			view.TargetWidth = width;
			view.TargetHeight = height;
			var viewport = camera.EffectiveViewport;
			view.ViewportX = MathF.Round(Math.Clamp(viewport.X, 0, 1) * width);
			view.ViewportY = MathF.Round(Math.Clamp(viewport.Y, 0, 1) * height);
			view.ViewportWidth = MathF.Max(1, MathF.Round(Math.Clamp(viewport.Width, 0, 1) * width));
			view.ViewportHeight = MathF.Max(1, MathF.Round(Math.Clamp(viewport.Height, 0, 1) * height));
			view.ViewportWidth = MathF.Min(view.ViewportWidth, width - view.ViewportX);
			view.ViewportHeight = MathF.Min(view.ViewportHeight, height - view.ViewportY);
			if (view.ViewportWidth <= 0 || view.ViewportHeight <= 0) continue;

			view.View = Camera.GetViewMatrix(entry.World);
			view.Projection = camera.GetProjectionMatrix(view.ViewportWidth / view.ViewportHeight);
			view.ViewProjection = view.View * view.Projection;
			view.Frustum = Frustum.FromMatrix(view.ViewProjection);
			view.Position = entry.World.Translation;
			var forward = -new Vector3(entry.World.M31, entry.World.M32, entry.World.M33);
			view.Forward = forward.LengthSquared() > 1e-12f ? Vector3.Normalize(forward) : -Vector3.UnitZ;
			view.Far = camera.EffectiveFar;

			var first = true;
			for (var t = 0; t < targetsSeen; t++)
			{
				if (_targetsSeen[t] == target) { first = false; break; }
			}

			if (first && targetsSeen < _targetsSeen.Length) _targetsSeen[targetsSeen++] = target;
			view.FirstOnTarget = first;
			view.DrawSkybox = camera.Clear == CameraClear.Skybox && _environment.Skybox.IsValid;
			view.DrawClearQuad = !first && (camera.Clear == CameraClear.Color || (camera.Clear == CameraClear.Skybox && !view.DrawSkybox));
			if (target == 0 && _frameViewIndex < 0) _frameViewIndex = _viewCount;
			_viewCount++;
		}
	}

	private void PrepareLights()
	{
		// The main light: the first directional light (it gets the shadow map when it casts shadows).
		_mainLight = _directional.Count > 0 ? 0 : -1;
		_shadowActive = false;
		_shadowCasterCount = 0;

		for (var v = 0; v < _viewCount; v++)
		{
			var view = _views[v];
			var count = 0;
			// Extra directional lights first (they light everything), then point and spot lights inside the frustum.
			for (var d = 1; d < _directional.Count && count < ViewUniforms.MaxLights; d++)
			{
				ref readonly var light = ref _directional.Items[d];
				var color = ColorSpace.ToLinear(light.Light.Color) * light.Light.Intensity;
				view.Lights[count++] = new LightUniform
				{
					ColorType = new Vector4(color.X, color.Y, color.Z, LightUniform.TypeDirectional),
					Direction = new Vector4(light.Direction, 0),
				};
			}

			for (var l = 0; l < _local.Count && count < ViewUniforms.MaxLights; l++)
			{
				ref readonly var light = ref _local.Items[l];
				if (!view.Frustum.Intersects(light.Bounds)) continue;
				view.Lights[count++] = light.Uniform;
			}

			view.LightCount = count;
		}

		if (_mainLight < 0 || !Options.Shadows || _viewCount == 0) return;
		ref readonly var main = ref _directional.Items[_mainLight];
		if (!main.Light.CastShadows) return;

		// Shadow casters: opaque and masked objects that cast shadows.
		var objectCount = _objects.Count;
		if (_casters.Length < objectCount)
		{
			_casters = new int[Math.Max(objectCount, _casters.Length * 2)];
			_casterBounds = new Aabb[_casters.Length];
		}

		var objects = _objects.Span;
		var materials = CollectionsMarshal.AsSpan(_materials);
		var casters = 0;
		for (var i = 0; i < objects.Length; i++)
		{
			ref readonly var o = ref objects[i];
			if (!o.CastShadows || !_worldBounds[i].IsValid) continue;
			var material = ResolveMaterial(materials, o.Material);
			if (material.AlphaMode == AlphaMode.Blend) continue;
			_casters[casters] = i;
			_casterBounds[casters] = _worldBounds[i];
			casters++;
		}

		if (casters == 0) return;

		var fitView = _views[_frameViewIndex >= 0 ? _frameViewIndex : 0];
		var cameraView = fitView.View;
		_shadow = ShadowMath.Fit(fitView.Camera, cameraView, fitView.ViewportWidth / fitView.ViewportHeight, main.Direction,
			Options.ShadowDistance, Math.Max(16, Options.ShadowMapSize), _casterBounds.AsSpan(0, casters));
		_shadowActive = true;
		_shadowCasterCount = casters;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private MaterialSlot ResolveMaterial(Span<MaterialSlot?> materials, int id)
	{
		if ((uint)id < (uint)materials.Length && materials[id] is { } slot) return slot;
		return materials[_defaultMaterial]!;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private int ResolveMaterialId(Span<MaterialSlot?> materials, int id) => (uint)id < (uint)materials.Length && materials[id] is not null ? id : _defaultMaterial;

	// Queue and sort.

	private void QueueAll()
	{
		_instances.Clear();
		_shadowBatches.Clear();
		var objectCount = _objects.Count;
		_instances.EnsureCapacity(objectCount * (_viewCount + (_shadowActive ? 1 : 0)));
		if (_keys.Length < objectCount)
		{
			_keys = new ulong[Math.Max(objectCount, _keys.Length * 2)];
			_items = new int[_keys.Length];
		}

		if (_shadowActive) QueueShadow();
		for (var v = 0; v < _viewCount; v++) QueueView(_views[v]);
		WriteStatistics();
	}

	private void QueueShadow()
	{
		// Casters inside the light's frustum, grouped by mesh (the depth-only pipeline ignores materials).
		var lightFrustum = Frustum.FromMatrix(_shadow.ViewProjection);
		var count = 0;
		for (var c = 0; c < _shadowCasterCount; c++)
		{
			var i = _casters[c];
			if (!lightFrustum.Intersects(_casterBounds[c])) continue;
			_keys[count] = (ulong)(uint)_objects.Items[i].Mesh << 32 | (uint)i;
			count++;
		}

		var keys = _keys.AsSpan(0, count);
		keys.Sort();
		var objects = _objects.Items;
		var lastMesh = -1;
		for (var k = 0; k < keys.Length; k++)
		{
			var i = (int)(uint)keys[k];
			ref readonly var o = ref objects[i];
			if (o.Mesh != lastMesh)
			{
				ref var batch = ref _shadowBatches.Add();
				batch.Mesh = o.Mesh;
				batch.Material = 0;
				batch.FirstInstance = _instances.Count;
				batch.InstanceCount = 0;
				lastMesh = o.Mesh;
			}

			InstanceData.Write(ref _instances.Add(), o.World, 0);
			_shadowBatches.Items[_shadowBatches.Count - 1].InstanceCount++;
		}

		_shadowCasterCount = count;
	}

	private void QueueView(ViewData view)
	{
		view.Opaque.Clear();
		view.Transparent.Clear();
		view.Culled = 0;

		var objects = _objects.Span;
		var bounds = _worldBounds;
		var materials = CollectionsMarshal.AsSpan(_materials);
		var mask = view.Camera.CullingMask == 0 ? uint.MaxValue : view.Camera.CullingMask;
		var frustum = view.Frustum;
		var position = view.Position;
		var forward = view.Forward;
		var inverseFar = 1f / MathF.Max(view.Far, 1e-3f);
		var keys = _keys;
		var items = _items;
		var opaque = 0;
		var transparent = 0;
		var culled = 0;

		for (var i = 0; i < objects.Length; i++)
		{
			ref readonly var o = ref objects[i];
			if ((o.Layers & mask) == 0) continue;
			ref readonly var box = ref bounds[i];
			if (!box.IsValid) continue;
			if (!frustum.Intersects(box))
			{
				culled++;
				continue;
			}

			var materialId = ResolveMaterialId(materials, o.Material);
			var material = materials[materialId]!;
			var depth = Vector3.Dot((box.Min + box.Max) * 0.5f - position, forward) * inverseFar;
			depth = depth < 0 ? 0 : depth > 1 ? 1 : depth;
			if (material.AlphaMode == AlphaMode.Blend)
			{
				if (transparent == _transparentKeys.Length)
				{
					Array.Resize(ref _transparentKeys, transparent * 2);
					Array.Resize(ref _transparentItems, transparent * 2);
				}

				_transparentKeys[transparent] = TransparentKey(depth, material.PipelineSortId, materialId, o.Mesh);
				_transparentItems[transparent++] = i;
			}
			else
			{
				keys[opaque] = OpaqueKey(depth, material.PipelineSortId, materialId, o.Mesh);
				items[opaque++] = i;
			}
		}

		view.Culled = culled;
		view.OpaqueObjects = opaque;
		view.TransparentObjects = transparent;

		MemoryExtensions.Sort(keys.AsSpan(0, opaque), items.AsSpan(0, opaque));
		BuildBatches(ref view.Opaque, items.AsSpan(0, opaque), materials);
		MemoryExtensions.Sort(_transparentKeys.AsSpan(0, transparent), _transparentItems.AsSpan(0, transparent));
		BuildBatches(ref view.Transparent, _transparentItems.AsSpan(0, transparent), materials);
	}

	/// <summary>
	/// The sort key of an opaque or masked draw: bins by pipeline (shader, alpha mode, culling), then material, then mesh,
	/// and front to back inside a bin (24 bits of view depth).
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static ulong OpaqueKey(float depth, int pipeline, int material, int mesh) =>
		((ulong)(uint)(pipeline & 0xFF) << 56) | ((ulong)(uint)(material & 0xFFFF) << 40) | ((ulong)(uint)(mesh & 0xFFFF) << 24) | (ulong)(uint)(depth * 0xFFFFFF);

	/// <summary>
	/// The sort key of a blended draw: back to front first (32 bits of inverted view depth), then pipeline, material and
	/// mesh so that equal neighbours still batch.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static ulong TransparentKey(float depth, int pipeline, int material, int mesh) =>
		((ulong)(uint.MaxValue - (uint)(depth * (double)uint.MaxValue)) << 32) | ((ulong)(uint)(pipeline & 0xFF) << 24) | ((ulong)(uint)(material & 0xFFF) << 12) | (ulong)(uint)(mesh & 0xFFF);

	private void BuildBatches(ref GrowableArray<Batch> batches, ReadOnlySpan<int> items, Span<MaterialSlot?> materials)
	{
		var objects = _objects.Items;
		var lastMesh = -1;
		var lastMaterial = -1;
		var current = -1;
		foreach (var i in items)
		{
			ref readonly var o = ref objects[i];
			var material = ResolveMaterialId(materials, o.Material);
			if (o.Mesh != lastMesh || material != lastMaterial)
			{
				ref var batch = ref batches.Add();
				batch.Mesh = o.Mesh;
				batch.Material = material;
				batch.FirstInstance = _instances.Count;
				batch.InstanceCount = 0;
				lastMesh = o.Mesh;
				lastMaterial = material;
				current = batches.Count - 1;
			}

			InstanceData.Write(ref _instances.Add(), o.World, o.ReceiveShadows ? InstanceData.ReceiveShadows : 0);
			batches.Items[current].InstanceCount++;
		}
	}

	private void WriteStatistics()
	{
		var meshes = CollectionsMarshal.AsSpan(_meshes);
		var batches = 0;
		var drawCalls = 0;
		var triangles = 0L;
		var visible = 0;
		var culled = 0;

		void Count(ReadOnlySpan<Batch> list, Span<MeshSlot?> meshes, ref int batches, ref int drawCalls, ref long triangles)
		{
			foreach (ref readonly var batch in list)
			{
				var mesh = meshes[batch.Mesh]!;
				batches++;
				drawCalls += mesh.SubMeshes.Length;
				triangles += (long)mesh.Triangles * batch.InstanceCount;
			}
		}

		Count(_shadowBatches.Span, meshes, ref batches, ref drawCalls, ref triangles);
		for (var v = 0; v < _viewCount; v++)
		{
			var view = _views[v];
			Count(view.Opaque.Span, meshes, ref batches, ref drawCalls, ref triangles);
			Count(view.Transparent.Span, meshes, ref batches, ref drawCalls, ref triangles);
			if (Options.DepthPrepass)
			{
				foreach (ref readonly var batch in view.Opaque.Span)
				{
					if (_materials[batch.Material]!.AlphaMode != AlphaMode.Opaque) continue;
					var mesh = meshes[batch.Mesh]!;
					drawCalls += mesh.SubMeshes.Length;
					triangles += (long)mesh.Triangles * batch.InstanceCount;
				}
			}

			if (view.DrawSkybox) drawCalls++;
			if (view.DrawClearQuad) drawCalls++;
			visible += view.OpaqueObjects + view.TransparentObjects;
			culled += view.Culled;
		}

		var lights = (_directional.Count > 0 ? 1 : 0);
		var localLights = 0;
		for (var v = 0; v < _viewCount; v++) localLights = Math.Max(localLights, _views[v].LightCount);
		LastFrameStatistics = new Rendering3DStatistics(_frameNumber, _viewCount, _objects.Count, visible, culled, batches, drawCalls,
			(int)Math.Min(int.MaxValue, triangles), _shadowCasterCount, lights + localLights);
	}
}
