using System.Runtime.InteropServices;

using Veldrid;
using Veldrid.SPIRV;


namespace Kyber.Graphics;

public interface ISpriteRenderer
{
	void Begin();

	void Draw(Color color, Rectangle destinationRectangle, Vector2 origin = default, float rotation = 0, byte layer = 0);
	void Draw(Color color, Vector2 position, Vector2 size, Vector2 origin = default, float rotation = 0, byte layer = 0);

	//void Draw(Texture texture, Rectangle destinationRectangle, Rectangle? sourceRectangle, Color? color, Vector2 origin = default, SpriteOptions options = SpriteOptions.None, float rotation = 0, byte layerDepth = 0);
	//void Draw(Texture texture, Vector2 position, Vector2 scale, Rectangle? sourceRectangle, Color? color, Vector2 origin = default, SpriteOptions options = SpriteOptions.None, float rotation = 0, byte layerDepth = 0);

	void End();
}

internal class SpriteRenderer : ISpriteRenderer, IDisposable
{
	private const int MAX_SPRITE_COUNT = 2048;
	private const int MAX_VERTEX_COUNT = MAX_SPRITE_COUNT * 4;
	private const int MAX_INDEX_COUNT = MAX_SPRITE_COUNT * 6;

	private readonly IWindow _window;
	private readonly GraphicsDevice _graphicsDevice;
	private readonly ILogger _logger;
	private readonly IEventListener _events;

	private DeviceBuffer? _matrixBuffer;
	private DeviceBuffer? _vertexBuffer;
	private DeviceBuffer? _indexBuffer;
	private ResourceSet? _viewProjResourceSet;
	private Shader[]? _shaders;
	private Pipeline? _pipeline;

	private int _batchStepSize = 256;

	private Sprite[] _sprites;
	private IntPtr[] _sortedSprites; // Sprite*[]

	private readonly BackToFrontComparer _backToFrontComparer = new();

	private VertexPositionColor[] _vertexArray;
	private ushort[] _indexArray;
	private uint _spriteCount = 0;
	private bool _beginCalled = false;
	
	private Matrix4x4 _projMatrix = Matrix4x4.Identity;

	private const string VertexCode = @"
#version 450

layout(location = 0) in vec3 Position;
layout(location = 1) in vec4 Color;

layout(set = 0, binding = 0) uniform MVP
{
    mat4 projection;
};

layout(location = 0) out vec4 fsin_Color;

void main()
{
    gl_Position = projection * vec4(Position.xy, 0, 1);
	fsin_Color = Color;
	//fsin_Color = vec4(Position.zzz, 1);
}";

	private const string FragmentCode = @"
#version 450

layout(location = 0) in vec4 fsin_Color;
layout(location = 0) out vec4 fsout_Color;

void main()
{
    fsout_Color = fsin_Color;
}";

	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	private struct VertexPositionColor
	{
		public Vector3 Position0;
		public Color Color0;
		public Vector3 Position1;
		public Color Color1;
		public Vector3 Position2;
		public Color Color2;
		public Vector3 Position3;
		public Color Color3;

		public const uint SizeInBytes = StrideInBytes * 4;
		public const uint StrideInBytes = 28;
	}

	public bool IsEnabled { get; set; } = true;

	public SpriteRenderer(IWindow window, IGraphicsDevice graphicsDevice, ILogger<SpriteRenderer> logger, IEventListener events)
	{
		_window = window;
		_graphicsDevice = (GraphicsDevice)graphicsDevice;
		_logger = logger;
		_events = events;

		_sprites = new Sprite[_batchStepSize];
		_sortedSprites = new IntPtr[_batchStepSize];

		_vertexArray = new VertexPositionColor[MAX_SPRITE_COUNT];
		_indexArray = _generateIndexArray();
	}

	public void Initialize()
	{
		if (_graphicsDevice.Internal == null)
		{
			IsEnabled = false;
			_logger.LogWarning($"{nameof(SpriteRenderer)} automically disabled due to GraphicsDevice not being set.");
			return;
		}

		var factory = _graphicsDevice.Factory;

		VertexLayoutDescription vertexLayout = new(
			new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
			new VertexElementDescription("Color", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4));

		ShaderDescription vertexShaderDesc = new(ShaderStages.Vertex, System.Text.Encoding.UTF8.GetBytes(VertexCode), "main");
		ShaderDescription fragmentShaderDesc = new(ShaderStages.Fragment, System.Text.Encoding.UTF8.GetBytes(FragmentCode), "main");

		_matrixBuffer = factory.CreateBuffer(new(64, BufferUsage.UniformBuffer));
		ResourceLayout projectionViewLayout = factory.CreateResourceLayout(new(
					new ResourceLayoutElementDescription("MVP", ResourceKind.UniformBuffer, ShaderStages.Vertex)
				));

		_viewProjResourceSet = factory.CreateResourceSet(new(projectionViewLayout, _matrixBuffer));

		_shaders = factory.CreateFromSpirv(vertexShaderDesc, fragmentShaderDesc);

		_pipeline = factory.CreateGraphicsPipeline(new()
		{
			BlendState = BlendStateDescription.SingleAlphaBlend,
			DepthStencilState = new DepthStencilStateDescription(depthTestEnabled: true, depthWriteEnabled: true, comparisonKind: ComparisonKind.LessEqual),
			RasterizerState = new RasterizerStateDescription(cullMode: FaceCullMode.Back, fillMode: PolygonFillMode.Solid, frontFace: FrontFace.Clockwise, depthClipEnabled: true, scissorTestEnabled: false),
			PrimitiveTopology = PrimitiveTopology.TriangleList,
			ResourceLayouts = new ResourceLayout[] { projectionViewLayout },
			ShaderSet = new ShaderSetDescription(new VertexLayoutDescription[] { vertexLayout }, _shaders, new[] { new SpecializationConstant(0, _graphicsDevice.Internal.IsClipSpaceYInverted) }),
			Outputs = _graphicsDevice.Internal.SwapchainFramebuffer.OutputDescription
		});

		_vertexBuffer = factory.CreateBuffer(new((uint)(_vertexArray.Length * VertexPositionColor.SizeInBytes), BufferUsage.VertexBuffer));
		_indexBuffer = factory.CreateBuffer(new((uint)(_indexArray.Length * sizeof(ushort)), BufferUsage.IndexBuffer));

		_graphicsDevice.Internal.UpdateBuffer(_indexBuffer, 0, _indexArray);
		_graphicsDevice.Internal.UpdateBuffer(_vertexBuffer, 0, _vertexArray);

		_updateMatricies();
	}

	public void Begin()
	{
		if (_graphicsDevice.Internal == null || _graphicsDevice.CommandList == null) throw new InvalidOperationException("Begin cannot be called until the GraphicsDevice has been initialized.");

		if (_beginCalled) throw new InvalidOperationException("Begin cannot be called again until End has been successfully called.");

		_spriteCount = 0;
		_beginCalled = true;
	}

	public void Draw(Color color, Rectangle destinationRectangle, Vector2 origin = default, float rotation = 0, byte layer = 0)
	{
		if (!_beginCalled) throw new InvalidOperationException("Begin must be called before calling Draw.");

		_addSprite(color, default, destinationRectangle, origin, rotation, layer);
	}

	public unsafe void Draw(Color color, Vector2 position, Vector2 size, Vector2 origin = default, float rotation = 0, byte layer = 0)
	{
		if (!_beginCalled) throw new InvalidOperationException("Begin must be called before calling Draw.");

		_addSprite(color, default, new Rectangle((int)position.X, (int)position.Y, (int)size.X, (int)size.Y), origin, rotation, layer);
	}

	//public void Draw(Texture texture, Rectangle destinationRectangle, Rectangle? sourceRectangle, Color? color, Vector2 origin = default, SpriteOptions options = SpriteOptions.None, float rotation = 0, float layerDepth = 0)
	//{
	//	throw new NotImplementedException();
	//}

	//public void Draw(Texture texture, Vector2 position, Vector2 scale, Rectangle? sourceRectangle, Color? color, Vector2 origin = default, SpriteOptions options = SpriteOptions.None, float rotation = 0, float layerDepth = 0)
	//{
	//	throw new NotImplementedException();
	//}

	public void End()
	{
		if (!_beginCalled) throw new InvalidOperationException("Begin must be called before calling End.");

		_beginCalled = false;

		if (_spriteCount == 0) return;

		if (_events.On<WindowResizeEvent>()) _updateMatricies();

		_updateVertexAndIndexArrays();

		_graphicsDevice.CommandList.SetVertexBuffer(0, _vertexBuffer);
		_graphicsDevice.CommandList.SetIndexBuffer(_indexBuffer, IndexFormat.UInt16);

		_graphicsDevice.CommandList.SetPipeline(_pipeline);
		_graphicsDevice.CommandList.SetGraphicsResourceSet(0, _viewProjResourceSet);
		_graphicsDevice.CommandList.DrawIndexed(_spriteCount * 6, _spriteCount * 2, 0, 0, 0);
	}

	public void Dispose()
	{
		_pipeline?.Dispose();
		if (_shaders != null) foreach (var shader in _shaders) shader.Dispose();

		_vertexBuffer?.Dispose();
		_indexBuffer?.Dispose();
	}

	private static ushort[] _generateIndexArray()
	{
		var indicies = new ushort[MAX_INDEX_COUNT];
		for (int i = 0, j = 0; i < MAX_INDEX_COUNT; i += 6, j += 4)
		{
			indicies[i + 0] = (ushort)(j + 0);
			indicies[i + 1] = (ushort)(j + 1);
			indicies[i + 2] = (ushort)(j + 2);
			indicies[i + 3] = (ushort)(j + 0);
			indicies[i + 4] = (ushort)(j + 2);
			indicies[i + 5] = (ushort)(j + 3);
		}
		return indicies;
	}

	private void _updateMatricies()
	{
		Console.WriteLine("Updating Matricies!");
		_projMatrix = _graphicsDevice.CreateOrthographic(0, _window.Width, _window.Height, 0, -1, 0);
		_graphicsDevice.CommandList.UpdateBuffer(_matrixBuffer, 0, _projMatrix);
	}

	private unsafe void _addSprite(Color color, Rectangle sourceRect, Rectangle destinationRect, Vector2 origin, float rotation, byte layer)
	{
		if (_spriteCount >= _sprites.Length)
		{
			Array.Resize(ref _sprites, _sprites.Length + _batchStepSize);
			Array.Resize(ref _sortedSprites, _sortedSprites.Length + _batchStepSize);
		}

		fixed (Sprite* sprite = &_sprites[_spriteCount])
		{
			sprite->Color = color;
			sprite->SourceRect = sourceRect;
			sprite->DestinationRect = destinationRect;
			sprite->Origin = origin;
			sprite->RotationSin = MathF.Sin(rotation);
			sprite->RotationCos = MathF.Cos(rotation);
			sprite->Layer = layer;
		}

		_spriteCount++;
	}

	private unsafe void _updateVertexAndIndexArrays()
	{
		fixed (IntPtr* sortedSprites = &_sortedSprites[0])
		fixed (VertexPositionColor* verticies = &_vertexArray[0])
		fixed (Sprite* sprites = &_sprites[0])
		{
			for (int i = 0; i < _spriteCount; i++) _sortedSprites[i] = (IntPtr)(&sprites[i]);

			Array.Sort(_sortedSprites, 0, (int)_spriteCount, _backToFrontComparer);

			for (int i = 0; i < _spriteCount; i += 1)
			{
				Sprite* sprite = (Sprite*)_sortedSprites[i];

				_generateVertexInfo(
					&verticies[i],
					sprite->SourceRect,
					sprite->DestinationRect,
					sprite->Color,
					sprite->Origin,
					sprite->RotationSin,
					sprite->RotationCos,
					sprite->Layer
				);
			}

			_graphicsDevice.Internal.UpdateBuffer(_vertexBuffer, 0, (IntPtr)(&verticies[0]), _spriteCount * VertexPositionColor.SizeInBytes);
		}
	}

	private unsafe void _generateVertexInfo(
			VertexPositionColor* sprite,
			Rectangle source,
			Rectangle destination,
			Color color,
			Vector2 origin,
			float rotationSin,
			float rotationCos,
			int layer
		)
	{
		float cornerX = -origin.X * destination.Width;
		float cornerY = -origin.Y * destination.Height;
		sprite->Position0.X = (
			(-rotationSin * cornerY) +
			(rotationCos * cornerX) +
			destination.X
		);
		sprite->Position0.Y = (
			(rotationCos * cornerY) +
			(rotationSin * cornerX) +
			destination.Y
		);
		cornerX = (1.0f - origin.X) * destination.Width;
		cornerY = -origin.Y * destination.Height;
		sprite->Position1.X = (
			(-rotationSin * cornerY) +
			(rotationCos * cornerX) +
			destination.X
		);
		sprite->Position1.Y = (
			(rotationCos * cornerY) +
			(rotationSin * cornerX) +
			destination.Y
		);
		cornerX = (1.0f - origin.X) * destination.Width;
		cornerY = (1.0f - origin.Y) * destination.Height;
		sprite->Position2.X = (
			(-rotationSin * cornerY) +
			(rotationCos * cornerX) +
			destination.X
		);
		sprite->Position2.Y = (
			(rotationCos * cornerY) +
			(rotationSin * cornerX) +
			destination.Y
		);
		cornerX = -origin.X * destination.Width;
		cornerY = (1.0f - origin.Y) * destination.Height;
		sprite->Position3.X = (
			(-rotationSin * cornerY) +
			(rotationCos * cornerX) +
			destination.X
		);
		sprite->Position3.Y = (
			(rotationCos * cornerY) +
			(rotationSin * cornerX) +
			destination.Y
		);
		//fixed (float* flipX = &CornerOffset.X[0])
		//{
		//	fixed (float* flipY = &CornerOffsetY[0])
		//	{
		//		sprite->TextureCoordinate0.X = (flipX[0 ^ effects] * sourceW) + sourceX;
		//		sprite->TextureCoordinate0.Y = (flipY[0 ^ effects] * sourceH) + sourceY;
		//		sprite->TextureCoordinate1.X = (flipX[1 ^ effects] * sourceW) + sourceX;
		//		sprite->TextureCoordinate1.Y = (flipY[1 ^ effects] * sourceH) + sourceY;
		//		sprite->TextureCoordinate2.X = (flipX[2 ^ effects] * sourceW) + sourceX;
		//		sprite->TextureCoordinate2.Y = (flipY[2 ^ effects] * sourceH) + sourceY;
		//		sprite->TextureCoordinate3.X = (flipX[3 ^ effects] * sourceW) + sourceX;
		//		sprite->TextureCoordinate3.Y = (flipY[3 ^ effects] * sourceH) + sourceY;
		//	}
		//}
		sprite->Position0.Z = layer;
		sprite->Position1.Z = layer;
		sprite->Position2.Z = layer;
		sprite->Position3.Z = layer;
		sprite->Color0 = color;
		sprite->Color1 = color;
		sprite->Color2 = color;
		sprite->Color3 = color;

		var pos = Vector3.Transform(sprite->Position0, _projMatrix);
	}

	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	private struct Sprite
	{
		public Color Color;
		public Rectangle SourceRect;
		public Rectangle DestinationRect;
		public Vector2 Origin;
		public float RotationSin;
		public float RotationCos;
		public int Layer;
	}

	private class BackToFrontComparer : IComparer<IntPtr>
	{
		public unsafe int Compare(IntPtr i1, IntPtr i2)
		{
			Sprite* p1 = (Sprite*)i1;
			Sprite* p2 = (Sprite*)i2;
			return p2->Layer.CompareTo(p1->Layer);
		}
	}
}

