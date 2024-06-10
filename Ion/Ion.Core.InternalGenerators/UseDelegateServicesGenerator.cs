﻿using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using SourceGeneratorUtils;

using System.Collections.Immutable;

using System.Diagnostics;

namespace Ion.Core.InternalGenerators;

[Generator]
public class UseDelegateServicesGenerator : IIncrementalGenerator
{
	private const int MAX_SERVICES = 8;
	private static readonly string[] STAGES = ["Init", "First", "FixedUpdate", "Update", "Render", "Last", "Destroy"];

	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
#if DEBUGGENERATORS
		//if (!Debugger.IsAttached)
		//{
		//	Debugger.Launch();
		//}
#endif


		// Add the marker attribute to the compilation
		context.RegisterPostInitializationOutput(ctx => ctx.AddSource("UseDelegateServiceExtensions.g.cs", _getUseDelegateServiceExtensions()));
	}

	private static SourceText _getUseDelegateServiceExtensions()
	{
		var source = new SourceWriter();
		source.WriteLine("using Microsoft.Extensions.DependencyInjection;");
		source.WriteEmptyLines(1);
		source.WriteLine($"namespace Ion;");

		source.WriteLine($"public static class UseDelegateServiceExtensions");
		source.OpenBlock();

		foreach(var stage in STAGES)
		{
			source.WriteLine($"#region {stage} Extensions");

			for (var svcCount = 1; svcCount <= MAX_SERVICES; svcCount++)
			{
				source.WriteLine($"public static IIonApplication Use{stage}<{_getGenericArgs(svcCount)}>(this IIonApplication app, Func<GameLoopDelegate, {_getGenericArgs(svcCount)}, GameLoopDelegate> middleware) {_getGenericConstraints(svcCount)}");
				source.OpenBlock();

				source.WriteLine($"return app.Use{stage}(next =>");
				source.OpenBlock();

				for(var index = 0; index < svcCount; index++)
				{
					source.WriteLine($"var service{index} = app.Services.GetRequiredService<TService{index}>();");
				}

				source.WriteLine($"return {_getMiddlewareCall(svcCount)};");

				source.CloseBlock();
				source.WriteLine(");");

				source.CloseBlock();
			}

			source.WriteLine("#endregion");
			source.WriteEmptyLines(1);
		}

		source.CloseAllBlocks();

		return source.ToSourceText();
	}

	private static string _getGenericArgs(int count)
	{
		return string.Join(", ", Enumerable.Range(0, count).Select(i => $"TService{i}"));
	}

	private static string _getGenericConstraints(int count)
	{
		return string.Join(" ", Enumerable.Range(0, count).Select(i => $"where TService{i} : notnull"));
	}

	private static string _getMiddlewareCall(int count)
	{
		return $"middleware(next, {string.Join(", ", Enumerable.Range(0, count).Select(i => $"service{i}"))})";
	}
}