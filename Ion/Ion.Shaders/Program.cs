using Ion.Shaders;

// Usage: Ion.Shaders --out <dir> [--gles] [--no-optimize] <shader>...
// For every shader (.vert/.frag/.comp, GLSL 4.5 Vulkan dialect) writes <dir>/<file>.spv and, with --gles,
// <dir>/<file>.es.glsl (GLSL ES 3.10). Exit code 1 on the first compilation error, printed in MSBuild's error format.

string? outDir = null;
var gles = false;
var optimize = true;
var inputs = new List<string>();
for (var i = 0; i < args.Length; i++)
{
	switch (args[i])
	{
		case "--out": outDir = args[++i]; break;
		case "--gles": gles = true; break;
		case "--no-optimize": optimize = false; break;
		default: inputs.Add(args[i]); break;
	}
}

if (outDir is null || inputs.Count == 0)
{
	Console.Error.WriteLine("usage: Ion.Shaders --out <dir> [--gles] [--no-optimize] <shader>...");
	return 2;
}

Directory.CreateDirectory(outDir);
foreach (var input in inputs)
{
	var name = Path.GetFileName(input);
	try
	{
		var spirv = ShaderCompiler.CompileToSpirV(File.ReadAllText(input), ShaderCompiler.KindOf(input), name, optimize);
		File.WriteAllBytes(Path.Combine(outDir, name + ".spv"), spirv);
		if (gles) File.WriteAllText(Path.Combine(outDir, name + ".es.glsl"), ShaderCompiler.TranslateToGlslEs(spirv));
		Console.WriteLine($"Ion.Shaders: {name} -> {name}.spv ({spirv.Length} bytes){(gles ? $", {name}.es.glsl" : "")}");
	}
	catch (Exception ex) when (ex is ShaderCompilationException or ArgumentException or IOException)
	{
		// Shaderc reports "file:line: error: ..."; prefix with the full path so MSBuild and IDEs link to the file.
		foreach (var line in ex.Message.Split('\n', StringSplitOptions.RemoveEmptyEntries))
		{
			Console.Error.WriteLine($"{Path.GetFullPath(input)}: error ION_SHADER: {line.Trim()}");
		}

		return 1;
	}
}

return 0;
