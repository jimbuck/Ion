using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using static Ion.Tests.TestConstants;

namespace Ion.Extensions.Networking.Generators.Tests;

internal static class Harness
{
	public const string UpdateGoldenVariable = "ION_UPDATE_GOLDEN";

	public static readonly ImmutableArray<MetadataReference> References = [.. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
		.Split(Path.PathSeparator)
		.Where(p => !Path.GetFileName(p).StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal) && !Path.GetFileName(p).StartsWith("xunit", StringComparison.Ordinal))
		.Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))];

	public static (CSharpCompilation Output, string Generated, ImmutableArray<Diagnostic> Diagnostics) Run(string source, OutputKind kind = OutputKind.DynamicallyLinkedLibrary, string assemblyName = "NetTest")
	{
		var parse = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);
		var compilation = CSharpCompilation.Create(assemblyName, [CSharpSyntaxTree.ParseText(source, parse, path: "Game.cs")], References,
			new CSharpCompilationOptions(kind, nullableContextOptions: NullableContextOptions.Enable, allowUnsafe: true));
		GeneratorDriver driver = CSharpGeneratorDriver.Create([new NetworkGenerator().AsSourceGenerator()], parseOptions: parse);
		driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
		var run = driver.GetRunResult();
		var generated = run.GeneratedTrees.Select(t => t.GetText().ToString()).SingleOrDefault() ?? "";
		return ((CSharpCompilation)output, generated, [.. run.Diagnostics.Concat(diagnostics).Distinct()]);
	}

	public static void AssertGolden(string name, string actual, [CallerFilePath] string callerPath = "")
	{
		var path = Path.Combine(Path.GetDirectoryName(callerPath)!, "Golden", name + ".g.cs");
		actual = actual.Replace("\r\n", "\n");
		if (Environment.GetEnvironmentVariable(UpdateGoldenVariable) == "1")
		{
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			File.WriteAllText(path, actual);
			return;
		}

		Assert.True(File.Exists(path), $"Golden file {path} is missing; run the tests with {UpdateGoldenVariable}=1 to create it.");
		Assert.Equal(File.ReadAllText(path).Replace("\r\n", "\n"), actual);
	}

	public static Assembly Load(Compilation compilation)
	{
		using var stream = new MemoryStream();
		var result = compilation.Emit(stream);
		Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
		stream.Position = 0;
		return new AssemblyLoadContext(compilation.AssemblyName, isCollectible: true).LoadFromStream(stream);
	}

	public static string[] Ids(ImmutableArray<Diagnostic> diagnostics) => [.. diagnostics.Where(d => d.Id.StartsWith("ION2", StringComparison.Ordinal)).Select(d => d.Id).Order()];
}

[Trait(CATEGORY, UNIT)]
public class GeneratorTests
{
	private const string Usings = """
		using System.Numerics;
		using Arch.Core;
		using Ion.Extensions.Networking;

		""";

	private const string ValidGame = Usings + """
		namespace Game;

		public enum Team : byte { Red, Blue }
		public record struct Inner(int A, Vector2 B);

		[Replicated]
		public record struct Health(int Current, int Max);

		[Replicated(Authority = Authority.Owner), Predicted, Interpolated]
		public record struct Body(Vector3 Position, Quaternion Rotation, Team Team, Inner Inner, FixedString32 Name, NetworkId Target);

		[NetworkMessage(Delivery = Delivery.Unreliable, Direction = MessageDirection.ClientToServer)]
		public record struct Input(Vector2 Move, bool Fire);

		public static class Use
		{
			public static void Create(World world) => world.Create(new Health(1, 2), new Body());
		}
		""";

	[Fact]
	public void AValidGameGeneratesCompilableSerializersAndMatchesTheGolden()
	{
		var (output, generated, diagnostics) = Harness.Run(ValidGame);
		Assert.Empty(Harness.Ids(diagnostics));
		Assert.Empty(output.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning));
		Harness.AssertGolden("ValidGame", generated);
	}

	[Fact]
	public void TheGeneratedCodeRegistersAndRoundTripsAtRuntime()
	{
		var (output, _, _) = Harness.Run(ValidGame, assemblyName: "NetRuntimeTest");
		var assembly = Harness.Load(output);
		var registration = assembly.GetType("Ion.Generated.Networking.NetworkTypes_NetRuntimeTest")!;
		registration.GetMethod("Register")!.Invoke(null, null);

		var table = NetworkRegistry.Table;
		var body = table.Components.Single(c => c.Name == "Game.Body");
		Assert.Equal(Authority.Owner, ((ReplicatedTypeInfo)body).Authority);
		Assert.True(((ReplicatedTypeInfo)body).Predicted);
		Assert.Equal("Position:System.Numerics.Vector3;Rotation:System.Numerics.Quaternion;Team:Game.Team;Inner:{A:int;B:System.Numerics.Vector2};Name:Ion.Extensions.Networking.FixedString32;Target:Ion.Extensions.Networking.NetworkId", body.Layout);
		var input = (MessageTypeInfo)table.Messages.Single(m => m.Name == "Game.Input");
		Assert.Equal(Delivery.Unreliable, input.Delivery);
		Assert.True(input.ClientMaySend);
		Assert.False(input.ServerMaySend);
	}

	[Theory]
	[InlineData("[Replicated] public record struct Named(string Name);", "ION201")]
	[InlineData("[NetworkMessage] public record struct Named(string Name);", "ION201")]
	[InlineData("[Replicated, Predicted] public record struct Moves(Vector2 P);", "ION202")]
	[InlineData("[Replicated] public unsafe struct Big { public Block16 A, B, C, D, E, F, G, H, I; } public struct Block16 { public Vector4 A, B, C, D, E, F, G, H; }", "ION203")]
	[InlineData("[Replicated] public record struct Unused(int A);", "ION204")]
	[InlineData("[Replicated] public struct Readonly { public readonly int A; }", "ION205")]
	[InlineData("[Replicated] public unsafe struct Buffer { public fixed byte A[4]; }", "ION205")]
	[InlineData("[Replicated] public record struct Pointer(System.IntPtr A);", "ION205")]
	[InlineData("[Predicted] public record struct Loose(int A);", "ION209")]
	[InlineData("[Interpolated] public record struct Loose(int A);", "ION209")]
	[InlineData("[Replicated] public record struct Link(Entity Other, int A);", "ION210")]
	public void EachRuleIsReported(string declaration, string id)
	{
		var source = Usings + "namespace Game;\n" + declaration + """

			public static class Use
			{
				public static void Create(World world)
				{
					world.Create(new Named(), new Moves(), new Big(), new Readonly(), new Buffer(), new Pointer(), new Link());
				}
			}

			public record struct Named; public record struct Moves; public record struct Readonly; public record struct Pointer; public record struct Link;
			public struct Big; public struct Buffer;
			""";

		// The stubs in Use collide with the declaration under test; drop the duplicate one.
		var name = declaration.Split("struct ")[1].Split(['(', ' ', '{'])[0];
		source = source.Replace($"public record struct {name};", "").Replace($"public struct {name};", "").Replace(" " + name + ";", ";");
		var (_, _, diagnostics) = Harness.Run(source);
		Assert.Contains(id, Harness.Ids(diagnostics));
	}

	[Fact]
	public void ReadersCreatedInStageMethodsAndOneSidedMessagesAreReportedInAnExecutable()
	{
		var source = Usings + """
			using Ion;
			namespace Game;

			[NetworkMessage] public record struct OnlySent(int A);
			[NetworkMessage] public record struct OnlyRead(int A);
			[NetworkMessage] public record struct Both(int A);

			public sealed class Chat(INetworkMessages net)
			{
				private NetworkReader<Both> _both = net.Reader<Both>();

				[Update]
				public void Update(GameTime dt)
				{
					var reader = net.Reader<OnlyRead>();
					reader.TryRead(out _, out _);
					_both.TryRead(out _, out _);
					net.Broadcast(new OnlySent(1));
					net.Broadcast(new Both(1));
				}
			}

			public static class Program { public static void Main() { } }
			""";

		var (_, _, diagnostics) = Harness.Run(source, OutputKind.ConsoleApplication);
		Assert.Equal(["ION206", "ION207", "ION208"], Harness.Ids(diagnostics));
		Assert.Contains(diagnostics, d => d.Id == "ION207" && d.GetMessage().Contains("OnlySent"));
		Assert.Contains(diagnostics, d => d.Id == "ION208" && d.GetMessage().Contains("OnlyRead"));

		// In a library the other side may live elsewhere: only the reader rule applies.
		var (_, _, library) = Harness.Run(source.Replace("public static class Program { public static void Main() { } }", ""));
		Assert.Equal(["ION206"], Harness.Ids(library));
	}

	[Fact]
	public void AComponentDeclaredElsewhereIsReplicatedByAnAssemblyAttribute()
	{
		var source = Usings + """
			using Ion.Extensions.Ecs;
			[assembly: ReplicateComponent(typeof(Transform2D), Interpolated = true)]
			""";
		var (output, generated, diagnostics) = Harness.Run(source);
		Assert.Empty(Harness.Ids(diagnostics));
		Assert.Empty(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
		Assert.Contains("ReplicatedTypeInfo<global::Ion.Extensions.Ecs.Transform2D>", generated);
		Assert.Contains("NetLerp.Lerp(a.Position, b.Position, t)", generated);
	}

	[Fact]
	public void NothingIsGeneratedWithoutNetworkTypes()
	{
		var (_, generated, diagnostics) = Harness.Run(Usings + "namespace Game; public record struct Plain(int A);");
		Assert.Equal("", generated);
		Assert.Empty(Harness.Ids(diagnostics));
	}
}
