namespace Ion.Extensions.Graphics.Tests;

public class NullAssetTests
{
	[Fact, Trait(CATEGORY, UNIT)]
	public void NullTexture2DLoader_ReadsSizeFromThePngHeader()
	{
		using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Assets", "rgbt_2x2.png"));

		var texture = NullTexture2DLoader.Read("rgbt_2x2.png", stream);

		Assert.Equal(2u, texture.Width);
		Assert.Equal(2u, texture.Height);
		Assert.Equal(2u, texture.MipLevels);
		Assert.Equal("rgbt_2x2.png", texture.Name);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void NullTexture2DLoader_RejectsDataThatIsNotAnImage()
	{
		using var stream = new MemoryStream("not an image"u8.ToArray());

		Assert.Throws<InvalidDataException>(() => NullTexture2DLoader.Read("bad.png", stream));
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void LoadITexture2D_UsesTheNullLoaderAndCaches()
	{
		using var app = TestApp.CreateNullGraphics(configure: services => services.AddAssets());
		using var scope = app.Services.CreateScope();
		var assets = scope.ServiceProvider.GetRequiredService<IAssetManager>();

		var texture = assets.Load<ITexture2D>("rgbt_2x2.png");

		Assert.IsType<NullTexture2D>(texture);
		Assert.Equal(2u, texture.Width);
		Assert.Equal(2u, texture.Height);
		Assert.Same(texture, assets.Load<ITexture2D>("rgbt_2x2.png"));
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void LoadITexture2D_MissingFileThrowsFileNotFound()
	{
		using var app = TestApp.CreateNullGraphics(configure: services => services.AddAssets());
		using var scope = app.Services.CreateScope();
		var assets = scope.ServiceProvider.GetRequiredService<IAssetManager>();

		Assert.Throws<FileNotFoundException>(() => assets.Load<ITexture2D>("missing.png"));
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void NullFont_MeasureStringIsDeterministic()
	{
		var set = new NullFontSet("font", ["font.ttf"]);
		var font = set.CreateStyle(20);

		Assert.Equal(new Vector2(30, 20), font.MeasureString("abc"));
		Assert.Equal(font.MeasureString("Score: 100"), set.CreateStyle(20).MeasureString("Score: 100"));
		Assert.Equal(new Vector2(10 * 5, 20 * 2), font.MeasureString("ab\r\nabcde"));
		Assert.Equal(Vector2.Zero, font.MeasureString(""));
		Assert.Equal(20, font.LineHeight);
		Assert.Same(set, font.FontSet);

		// Scales linearly with the font size.
		Assert.Equal(font.MeasureString("abc") * 2, set.CreateStyle(40).MeasureString("abc"));
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void LoadIFontSet_ChecksTheFileAndReturnsANullFontSet()
	{
		using var app = TestApp.CreateNullGraphics(configure: services => services.AddAssets());
		using var scope = app.Services.CreateScope();
		var assets = scope.ServiceProvider.GetRequiredService<IAssetManager>();

		var single = assets.Load<IFontSet>("Bungee-Regular.ttf");
		Assert.IsType<NullFontSet>(single);
		Assert.IsType<NullFont>(single.CreateStyle(24));

		var combined = assets.LoadFontSet("Combined", "Bungee-Regular.ttf", "Bungee-Regular.ttf");
		Assert.Equal(["Bungee-Regular.ttf", "Bungee-Regular.ttf"], ((NullFontSet)combined).Fonts);
		Assert.Same(combined, assets.LoadFontSet("Combined", "Bungee-Regular.ttf"));

		Assert.Throws<FileNotFoundException>(() => assets.Load<IFontSet>("Missing.ttf"));
	}
}
