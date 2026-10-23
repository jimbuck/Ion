using Arch.Core;

using Microsoft.Extensions.DependencyInjection;

using Ion.Extensions.Graphics;
using Ion.Testing;

namespace Ion.Extensions.Ecs.Tests;

public record struct Velocity(Vector2 Value);
public record struct Frozen;
public record struct Health(int Value);
public record struct Marker;

internal static class Hosts
{
	/// <summary>A headless host with the ECS module (root schedule), and the render extraction when asked.</summary>
	public static IonTestHost Ecs(bool rendering = false)
	{
		var host = new IonTestHost()
			.Configure(services =>
			{
				services.AddEcs();
				if (rendering) Ion.Extensions.Ecs.Rendering.EcsRenderingBuilderExtensions.AddEcsRendering(services);
			})
			.ConfigureApp(app =>
			{
				app.UseEcs();
				if (rendering) Ion.Extensions.Ecs.Rendering.EcsRenderingBuilderExtensions.UseEcsRendering(app);
			});
		return host;
	}

	public static NullTexture2D Texture(uint width = 32, uint height = 16) => new("test.png", width, height);
}

/// <summary>Collects what steps saw, in order.</summary>
public sealed class Journal
{
	public List<string> Entries { get; } = [];

	public void Add(string entry) => Entries.Add(entry);
}

internal static class ServiceCollectionTestExtensions
{
	public static IServiceCollection AddJournal(this IServiceCollection services) => services.AddSingleton<Journal>();
}

internal static class WorldTestExtensions
{
	public static int Count<T>(this World world)
	{
		var query = new QueryDescription().WithAll<T>();
		return world.CountEntities(in query);
	}
}
