using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

using SourceGeneratorUtils;

namespace Kyber.Extensions.Coroutines.Generators;

[Generator]
public class CoroutinesGenerator : IIncrementalGenerator
{
	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
#if DEBUGGENERATORS
		if (!Debugger.IsAttached)
		{
			Debugger.Launch();
		}
#endif

		// Add the marker attribute to the compilation
		context.RegisterPostInitializationOutput(ctx => ctx.AddSource("CoroutineAttribute.g.cs", GetCoroutineAttribute()));
	}

	private static SourceText GetCoroutineAttribute()
	{
		return SourceText.From(@"
namespace Kyber.Extensions.Coroutines;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class CoroutineAttribute : Attribute
{
	public string MethodName { get; set; }

	public CoroutineAttribute(string methodName)
	{
		MethodName = methodName;
	}
}
", System.Text.Encoding.UTF8);
	}


}