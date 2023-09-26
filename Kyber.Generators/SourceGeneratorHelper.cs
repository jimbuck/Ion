using System.Text;

namespace Kyber.Generators;

public static class SourceGenerationHelper
{
	public const string Attributes = @$"namespace Kyber;

[System.AttributeUsage(System.AttributeTargets.Class)]
public class SceneAttribute<T> : System.Attribute {{ }}

[System.AttributeUsage(System.AttributeTargets.Class)]
public class SystemAttribute<T> : System.Attribute {{ }}

[System.AttributeUsage(System.AttributeTargets.Method)]
public class UpdateAttribute : System.Attribute {{ }}

[System.AttributeUsage(System.AttributeTargets.Method)]
public class DrawAttribute : System.Attribute {{ }}
";

	public static string GenerateExtensionClass(List<SceneToGenerate> scenesToGenerate)
	{
		var sb = new StringBuilder();
		sb.Append(@"
namespace Kyber;
");
		foreach (var sceneToGenerate in scenesToGenerate)
		{
			sb.Append($@"
public partial class {sceneToGenerate.Name}
{{
	public void Update(GameTime dt)
	{{");
			foreach(var system in sceneToGenerate.UpdateCalls)
			{
				sb.Append($@"
		this.{system}(dt);");
			}
			sb.Append($@"
	}}
}}");
		}

		sb.Append(@"
    }
}");

		return sb.ToString();
	}

	private static string _propsAndCtor(string name, List<string> services)
	{
		var sb = new StringBuilder();

		foreach (var service in services)
		{
			sb.AppendLine("protected readonly " + service + " " + _toPrivateName(service) + ";");
		}

		sb.Append(@$"
public ${name} (
");

		foreach(var service in services)
		{
			sb.AppendLine(service + " " + _toCamelCase(service) + ",");
		}

		sb.AppendLine(") {");

		foreach (var service in services)
		{
			sb.AppendLine(service + " " + _toCamelCase(service) + ",");
		}

		sb.AppendLine("}");

		return sb.ToString();
	}

	private static string _toCamelCase(string name) => name.ToLower()[0] + name.Substring(1);
	private static string _toPrivateName(string name) => "_" + _toCamelCase(name);
}


public partial class Example
{
	public Example()
	{

	}

	public void Update(float dt)
	{

	}

	public void Draw(float dt)
	{

	}
}