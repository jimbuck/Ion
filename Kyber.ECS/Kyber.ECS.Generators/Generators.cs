namespace Kyber.ECS.Generators;

/// <summary>
/// Generates the entity create and query foreach functions
/// Use to increase the default limits
/// </summary>
[Generator]
public sealed class CodeGenerator : ISourceGenerator
{
	public void Initialize(GeneratorInitializationContext context) { }

	public void Execute(GeneratorExecutionContext context)
	{
		var queryForeachFunctions = _createQueryFunctions(32);
		context.AddSource($"QueryForeachFunctions.g.cs", queryForeachFunctions);
	}

	private string _pattern(string value, int count, string sep = ", ")
	{
		string result = "";
		for (int i = 1; i < count; ++i)
		{
			result += value.Replace("#", i.ToString());
			result += sep;
		}
		result += value.Replace("#", (count).ToString());
		return result;
	}


	/// <summary>
	/// Generates all query.Foreach functions
	/// </summary>
	/// <param name="component_count">Maximum number of components that can be used in queries</param>
	private string _createQueryFunctions(int component_count)
	{
		var writer = new StringBuilder();
		writer.AppendLine($@"namespace Kyber.ECS;
");
		writer.AppendLine("public partial class Query");
		writer.Append("{");

		for (int c = 1; c < component_count + 1; ++c) // components
		{
			var delegateName = $"C{c}Query";
			var genericDef = _pattern("C#", c);

			writer.AppendLine($@"
	public delegate void {delegateName}<{genericDef}>({_pattern("ref C# c#", c)});

	/// <summary>
    /// Iterates through entities within all valid archetypes.
    /// </summary>
	public void ForEach<{genericDef}>(in {delegateName}<{genericDef}> action)
	{{
		ComponentId {_pattern("cId# = ComponentId.From<C#>()", c)};

		foreach (var arch in _world.Archetypes(_with, _without))
		{{
			if ({_pattern("arch.Components[cId#].TryGet<C#>(out var c#)", c, " && ")})
			{{
				foreach (var i in arch.RowIndex.Values)
				{{
					action({_pattern("ref c#[i]", c)});
				}}
			}}
		}}
	}}");
		}

		writer.AppendLine("}");

		return writer.ToString();
	}
}