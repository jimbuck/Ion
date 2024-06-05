using Ion;
using Ion.Core;

[assembly: System.Reflection.Metadata.MetadataUpdateHandler(typeof(HotReloadService))]


internal static class HotReloadService
{
	public static IonApplication ActiveApplication { get; set; } = default!;
	public static GameLoop ActiveGameLoop { get; set; } = default!;

	public static void ClearCache(Type[]? types)
	{
		//Console.WriteLine($"HotReloadService::ClearCache");
		if (types is not null)
		{
			foreach (var type in types) Console.WriteLine("  " + type.FullName);
		}
	}

	public static void UpdateApplication(Type[]? types)
	{
		//Console.WriteLine($"HotReloadService::UpdateApplication ({types?.Length ?? 0})");
		ActiveGameLoop.Rebuild = true;
	}
}