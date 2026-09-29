using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

using Microsoft.Extensions.Logging;

using Ion.Extensions.Graphics;
using Ion.Extensions.Graphics.Rhi;

namespace Ion.Extensions.Rendering2D;

/// <summary>
/// The 2D renderer's <see cref="ISpriteBatch"/> on the RHI: sprites are recorded into one instance array per frame,
/// sorted per segment, uploaded once into the frame slot's instance buffer and drawn as ranges, one draw call per run of
/// equal textures (per texture with <see cref="SpriteSortMode.Texture"/>).
/// </summary>
/// <remarks>
/// <para>
/// The sprite batch system (<c>UseRendering2D</c>) opens the outermost segment at the start of every Render stage and
/// closes it at the end, which submits the frame. <see cref="Begin"/>/<see cref="End"/> nest inside it.
/// </para>
/// <para>
/// Textures must come from this renderer (<c>Load&lt;ITexture2D&gt;</c> with its loader registered, or
/// <see cref="RenderTarget2D"/>); fonts from its <see cref="IFontSet"/> loader. Text layouts are cached per font and
/// string, so drawing the same text every frame allocates nothing.
/// </para>
/// </remarks>
public sealed class SpriteBatch : ISpriteBatch, ISpriteBatchStatistics, IDisposable
{
	private static readonly SpriteTexture _placeholder = new PlaceholderTexture();

	private readonly IGraphicsFrame _frame;
	private readonly ILogger<SpriteBatch>? _logger;
	private readonly SpriteBatcher _batcher = new();
	private SpriteBatchOptions[] _stack = new SpriteBatchOptions[4];
	private int _depth;
	private ITexture? _target;
	private Color? _pendingClear;
	private SpriteRenderer? _renderer;
	private SpriteTexture _white = _placeholder;
	private long _frames;
	private Vector128<float> _lastTint;
	private uint _lastPacked = 0xFFFF_FFFFu;
	private bool _deferred;

	/// <summary>Creates a sprite batch drawing into <paramref name="frame"/>. GPU resources are created by <see cref="Initialize"/>.</summary>
	public SpriteBatch(IGraphicsFrame frame, ILogger<SpriteBatch>? logger = null)
	{
		ArgumentNullException.ThrowIfNull(frame);
		_frame = frame;
		_logger = logger;
	}

	/// <inheritdoc/>
	public SpriteBatchStatistics LastFrameStatistics { get; private set; } = new(-1, 0, 0, 0);

	/// <summary>True once <see cref="Initialize"/> created the GPU resources.</summary>
	public bool IsInitialized => _renderer is not null;

	/// <summary>
	/// When true, the outermost <see cref="End"/> closes the frame's segments but leaves the GPU submission to
	/// <see cref="SubmitDeferred"/>. Set by a renderer that composites the 2D overlay as a pass of its own frame (the 3D
	/// renderer's render graph draws it after the transparent pass); games never need it.
	/// </summary>
	public bool DeferSubmission { get; set; }

	/// <summary>True when a frame was closed with <see cref="DeferSubmission"/> on and has not been submitted yet.</summary>
	public bool HasDeferredSubmission => _deferred;

	/// <summary>Submits the frame closed while <see cref="DeferSubmission"/> was on (nothing when there is none).</summary>
	public void SubmitDeferred()
	{
		if (!_deferred) return;
		_deferred = false;
		_submit();
	}

	internal SpriteBatcher Batcher => _batcher;

	/// <summary>
	/// Creates the GPU resources (shaders, pipelines for the frame's format, samplers, the white texture). Called by the
	/// sprite batch system's Init step, after the device exists.
	/// </summary>
	public void Initialize()
	{
		if (_renderer is not null) return;
		var device = _frame.Device;
		_renderer = new SpriteRenderer(device);
		_white = _renderer.White;
		if (_frame.ColorFormat != TextureFormat.Undefined) _renderer.GetPipeline(SpriteBlendMode.AlphaBlend, _frame.ColorFormat);
		_logger?.LogInformation("Sprite batch ready on {Adapter} ({Backend}), {Frames} frames in flight.", device.AdapterName, device.Backend, device.FramesInFlight);
	}

	/// <inheritdoc/>
	public void Begin(SpriteBatchOptions options = default)
	{
		if (_depth == 0)
		{
			// A deferred frame nobody submitted is dropped.
			_deferred = false;
			_batcher.Reset();
			_target = null;
			_pendingClear = null;
		}
		else
		{
			_batcher.CloseSegment();
		}

		if (_depth == _stack.Length) Array.Resize(ref _stack, _stack.Length * 2);
		_stack[_depth++] = options;
		_open(options);
	}

	/// <inheritdoc/>
	public void End()
	{
		if (_depth == 0) throw new InvalidOperationException("End was called without a matching Begin.");
		_batcher.CloseSegment();
		_depth--;
		if (_depth > 0)
		{
			_open(_stack[_depth - 1]);
			return;
		}

		if (DeferSubmission)
		{
			_deferred = true;
			return;
		}

		_submit();
	}

	/// <inheritdoc/>
	public void SetRenderTarget(ITexture? target, Color? clearColor = null)
	{
		_ensureBegun();
		_batcher.CloseSegment();
		_target = target;
		_pendingClear = clearColor;
		_open(_stack[_depth - 1]);
	}

	/// <summary>Renders the following draws into <paramref name="target"/> (see <see cref="SetRenderTarget(ITexture?, Color?)"/>).</summary>
	public void SetRenderTarget(RenderTarget2D? target, Color? clearColor = null) => SetRenderTarget(target?.Texture, clearColor);

	/// <inheritdoc/>
	public void Draw(ITexture2D texture, RectangleF destinationRectangle, RectangleF sourceRectangle = default, Color color = default, Vector2 origin = default, float rotation = 0, float depth = 0, SpriteEffect options = SpriteEffect.None)
	{
		// Fast path (no calls, so nothing is spilled): a loaded texture already used this frame, the same source rectangle
		// and tint as last time, no rotation or flip, room in the instance array.
		if (texture is Texture2D loaded && options == SpriteEffect.None
			&& SameSource(sourceRectangle, loaded.CachedSource)
			&& color.ToVector4().AsVector128() == _lastTint
			&& _batcher.TryAdd(loaded, destinationRectangle.X, destinationRectangle.Y, destinationRectangle.Width, destinationRectangle.Height, origin.X, origin.Y, rotation, loaded.CachedUv, _lastPacked, depth))
		{
			return;
		}

		_drawSlow(texture, destinationRectangle.X, destinationRectangle.Y, destinationRectangle.Width, destinationRectangle.Height, sourceRectangle, color, origin, rotation, depth, options);
	}

	/// <inheritdoc/>
	public void Draw(ITexture2D texture, Vector2 position, Vector2 size, RectangleF sourceRectangle = default, Color color = default, Vector2 origin = default, float rotation = 0, float depth = 0, SpriteEffect options = SpriteEffect.None)
	{
		// Fast path: see the other overload.
		if (texture is Texture2D loaded && options == SpriteEffect.None
			&& SameSource(sourceRectangle, loaded.CachedSource)
			&& color.ToVector4().AsVector128() == _lastTint
			&& _batcher.TryAdd(loaded, position.X, position.Y, size.X, size.Y, origin.X, origin.Y, rotation, loaded.CachedUv, _lastPacked, depth))
		{
			return;
		}

		_drawSlow(texture, position.X, position.Y, size.X, size.Y, sourceRectangle, color, origin, rotation, depth, options);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void _drawSlow(ITexture2D texture, float x, float y, float width, float height, RectangleF sourceRectangle, Color color, Vector2 origin, float rotation, float depth, SpriteEffect options)
	{
		_ensureBegun();
		var sprite = _sprite(texture);
		_add(sprite, x, y, width, height, sourceRectangle, color, origin, rotation, depth, options);
	}

	/// <inheritdoc/>
	public void DrawRect(Color color, RectangleF destinationRectangle, Vector2 origin = default, float rotation = 0, float depth = 0)
	{
		_ensureBegun();
		_batcher.Add(_white, destinationRectangle.X, destinationRectangle.Y, destinationRectangle.Width, destinationRectangle.Height, origin.X, origin.Y, rotation, SpriteInstance.FullUv, SpriteInstance.PackColor(color.ToVector4()), depth);
	}

	/// <inheritdoc/>
	public void DrawRect(Color color, Vector2 position, Vector2 size, Vector2 origin = default, float rotation = 0, float depth = 0)
	{
		_ensureBegun();
		_batcher.Add(_white, position.X, position.Y, size.X, size.Y, origin.X, origin.Y, rotation, SpriteInstance.FullUv, SpriteInstance.PackColor(color.ToVector4()), depth);
	}

	/// <inheritdoc/>
	public void DrawPoint(Color color, Vector2 position, float depth = 0) => DrawPoint(color, position, Vector2.One, depth);

	/// <inheritdoc/>
	public void DrawPoint(Color color, Vector2 position, Vector2 size, float depth = 0)
	{
		_ensureBegun();
		_batcher.Add(_white, position.X, position.Y, size.X, size.Y, 0.5f, 0.5f, 0f, SpriteInstance.FullUv, SpriteInstance.PackColor(color.ToVector4()), depth);
	}

	/// <inheritdoc/>
	public void DrawLine(Color color, Vector2 pointA, Vector2 pointB, float thickness = 1, float depth = 0)
	{
		var diff = pointB - pointA;
		DrawLine(color, pointA, diff.Length(), MathF.Atan2(diff.Y, diff.X), thickness, depth);
	}

	/// <inheritdoc/>
	public void DrawLine(Color color, Vector2 start, float length, float angle, float thickness = 1, float depth = 0)
	{
		_ensureBegun();
		_batcher.Add(_white, start.X, start.Y, length, thickness, 0f, 0.5f, angle, SpriteInstance.FullUv, SpriteInstance.PackColor(color.ToVector4()), depth);
	}

	/// <inheritdoc/>
	/// <remarks>
	/// The string is laid out once per font and cached (see <see cref="IFont"/>); later calls with the same text transform
	/// the cached glyph quads. <paramref name="origin"/> is in unscaled text pixels; <paramref name="options"/> is ignored.
	/// </remarks>
	public void DrawString(IFont font, string text, Vector2 textPosition, Color color = default, float depth = 0, Vector2 origin = default, float rotation = 0, float scale = 1, SpriteEffect options = SpriteEffect.None)
	{
		_ensureBegun();
		ArgumentNullException.ThrowIfNull(font);
		if (string.IsNullOrEmpty(text)) return;
		if (font is not Font rhiFont)
		{
			throw new ArgumentException($"Font '{font.FontSet.Name}' ({font.GetType().Name}) was not created by the 2D renderer's font loader.", nameof(font));
		}

		if (color == default) color = Color.White;
		var packed = SpriteInstance.PackColor(color.ToVector4());
		var layout = rhiFont.GetLayout(text, _frames);

		if (rotation == 0f)
		{
			foreach (ref readonly var glyph in layout.Glyphs.AsSpan())
			{
				var position = textPosition + (glyph.Position - origin) * scale;
				var size = glyph.Size * scale;
				_batcher.Add(glyph.Page, position, new Vector2(size.X, 0), new Vector2(0, size.Y), glyph.Uv, packed, depth);
			}

			return;
		}

		var (sin, cos) = MathF.SinCos(rotation);
		var right = new Vector2(cos, sin);
		var down = new Vector2(-sin, cos);
		foreach (ref readonly var glyph in layout.Glyphs.AsSpan())
		{
			var local = (glyph.Position - origin) * scale;
			var position = textPosition + right * local.X + down * local.Y;
			var size = glyph.Size * scale;
			_batcher.Add(glyph.Page, position, right * size.X, down * size.Y, glyph.Uv, packed, depth);
		}
	}

	/// <summary>Releases the GPU resources of the batch (not the textures it drew).</summary>
	public void Dispose()
	{
		_renderer?.Dispose();
		_renderer = null;
		_white = _placeholder;
	}

	private void _open(in SpriteBatchOptions options)
	{
		_batcher.OpenSegment(options, _target, _pendingClear);
		_pendingClear = null;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void _add(SpriteTexture texture, float x, float y, float width, float height, in RectangleF source, Color color, Vector2 origin, float rotation, float depth, SpriteEffect options)
	{
		// The UV rectangle of the last source rectangle drawn from this texture is cached on it (most draws repeat one).
		ulong uv;
		if (options == SpriteEffect.None && SameSource(source, texture.CachedSource)) uv = texture.CachedUv;
		else uv = _uv(texture, source, options);

		// default(Color) == Color.Transparent bitwise; it means "no tint" (see ISpriteBatch.Draw remarks). The last tint is cached.
		var tint = color.ToVector4().AsVector128();
		if (tint != _lastTint)
		{
			_lastTint = tint;
			_lastPacked = tint == Vector128<float>.Zero ? 0xFFFF_FFFFu : SpriteInstance.PackColor(color.ToVector4());
		}

		_batcher.Add(texture, x, y, width, height, origin.X, origin.Y, rotation, uv, _lastPacked, depth);
	}

	// Field by field (building a vector from the fields would stall on store forwarding); NaN never matches.
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool SameSource(in RectangleF a, in RectangleF b) => a.X == b.X && a.Y == b.Y && a.Width == b.Width && a.Height == b.Height;

	private static ulong _uv(SpriteTexture texture, in RectangleF source, SpriteEffect options)
	{
		float u0, v0, u1, v1;
		if (source.X == 0 && source.Y == 0 && source.Width == 0 && source.Height == 0)
		{
			u0 = v0 = 0f;
			u1 = v1 = 1f;
		}
		else
		{
			u0 = source.X * texture.InverseWidth;
			v0 = source.Y * texture.InverseHeight;
			u1 = (source.X + source.Width) * texture.InverseWidth;
			v1 = (source.Y + source.Height) * texture.InverseHeight;
		}

		if ((options & SpriteEffect.FlipHorizontally) != 0) (u0, u1) = (u1, u0);
		if ((options & SpriteEffect.FlipVertically) != 0) (v0, v1) = (v1, v0);
		var uv = SpriteInstance.PackUv(u0, v0, u1, v1);
		if (options == SpriteEffect.None)
		{
			texture.CachedSource = source;
			texture.CachedUv = uv;
		}

		return uv;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static SpriteTexture _sprite(ITexture2D texture)
	{
		// Loaded textures are the sealed Texture2D: an exact type check, no cast helper call.
		if (texture is Texture2D loaded) return loaded;
		return texture as SpriteTexture ?? _foreign(texture);
	}

	private static SpriteTexture _foreign(ITexture2D texture)
	{
		ArgumentNullException.ThrowIfNull(texture);
		throw new ArgumentException($"Texture '{texture.Name}' ({texture.GetType().Name}) was not created by the 2D renderer (load it with Load<ITexture2D> on a rendering backend, or use RenderTarget2D).", nameof(texture));
	}

	private void _ensureBegun()
	{
		if (_depth == 0) throw new InvalidOperationException("Begin must be called before drawing (the sprite batch system opens a segment around every Render stage).");
	}

	private void _submit()
	{
		var frame = _frames++;
		var drawCalls = 0;
		if (_renderer is not null && _batcher.Segments.Length > 0) drawCalls = _renderer.Submit(_batcher, _frame);
		var sprites = drawCalls > 0 ? _batcher.Count : 0;
		LastFrameStatistics = new SpriteBatchStatistics(frame, drawCalls, sprites, sprites * 2);
		_target = null;
	}

	private sealed class PlaceholderTexture() : SpriteTexture("Uninitialized white", null, 1, 1, 1, ownsTexture: false);
}
