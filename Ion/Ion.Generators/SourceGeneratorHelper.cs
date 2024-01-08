using Microsoft.CodeAnalysis.Text;
using SourceGeneratorUtils;

namespace Ion.Generators;

internal static class SourceGenerationHelper
{
	public const string Attributes = @$"namespace Ion;

[System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true)]
public class SceneAttribute<T> : System.Attribute {{ }}

[System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true)]
public class SystemAttribute<T> : System.Attribute {{ }}

[System.AttributeUsage(System.AttributeTargets.Method)]
public class UpdateAttribute : System.Attribute {{
}}

[System.AttributeUsage(System.AttributeTargets.Method)]
public class UpdateDependsOnAttribute<T> : System.Attribute
{{
	public UpdateDependsOnAttribute() {{  }}
}}

[System.AttributeUsage(System.AttributeTargets.Method)]
public class DrawAttribute : System.Attribute {{
}}

[System.AttributeUsage(System.AttributeTargets.Method)]
public class DrawDependsOnAttribute<T> : System.Attribute
{{
	public DrawDependsOnAttribute() {{  }}
}}
";
	public static SourceText GenerateSceneClass(SceneClass scene)
	{
		var source = new SourceWriter();

		source.WriteLine($"namespace {scene.Namespace};");

		source.WriteLine($"public partial class {scene.ClassName}");
		source.OpenBlock();

		_propsAndCtor(source, scene.ClassName, scene.Systems);

		#region Update Calls
		source.WriteLine($"public void Update(GameTime dt)");
		source.OpenBlock();

		foreach (var updateCall in scene.UpdateCalls)
		{
			source.WriteLine($"{updateCall.System.InstanceName}.{updateCall.MethodName}(dt);");
		}

		source.CloseBlock();
		#endregion

		#region Draw Calls
		source.WriteLine($"public void Draw(GameTime dt)");
		source.OpenBlock();

		foreach (var drawCall in scene.DrawCalls)
		{
			source.WriteLine($"{drawCall.System.InstanceName}.{drawCall.MethodName}(dt);");
		}

		source.CloseBlock();
		#endregion

		source.CloseBlock();

		return source.ToSourceText();
	}

	private static void _propsAndCtor(SourceWriter source, string className, ICollection<SystemClass> systems)
	{
		foreach (var system in systems)
		{
			source.WriteLine($"protected readonly {system.FullName} {system.InstanceName};");
		}

		source.WriteLine($"public {className}(");
		source.Indentation++;

		for (var i=0;i<systems.Count;i++)
		{
			var system = systems.ElementAt(i);
			var notLast = i < systems.Count - 1;
			source.WriteLine($"{system.FullName} {ToCamelCase(system.ClassName)}{(notLast ? "," : string.Empty)}");
		}

		source.Indentation--;
		source.WriteLine(")");
		source.OpenBlock();

		foreach (var system in systems)
		{
			source.WriteLine($"{system.InstanceName} = {ToCamelCase(system.ClassName)};");
		}

		source.CloseBlock();
	}

	public static string ToCamelCase(string name) => name.ToLower()[0] + name.Substring(1);
	public static string ToPrivateName(string name) => "_" + ToCamelCase(name);
}