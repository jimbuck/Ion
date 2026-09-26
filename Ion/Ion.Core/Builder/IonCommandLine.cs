namespace Ion;

/// <summary>
/// Short command line switches that <see cref="IonApplication.CreateBuilder(string[])"/> rewrites into configuration keys
/// before binding, so <c>dotnet run -- --headless --remote</c> works like the long <c>--Ion:...=value</c> form.
/// </summary>
/// <remarks>
/// <list type="table">
/// <item><term><c>--headless</c></term><description><c>--Ion:Headless=true</c></description></item>
/// <item><term><c>--headless-render</c></term><description><c>--Ion:Headless=true --Ion:Headless:Render=true</c></description></item>
/// <item><term><c>--remote</c></term><description><c>--Ion:Remote:Enabled=true</c></description></item>
/// <item><term><c>--remote-allow-mutations</c></term><description><c>--Ion:Remote:Enabled=true --Ion:Remote:AllowMutations=true</c></description></item>
/// <item><term><c>--remote-stdio</c></term><description><c>--Ion:Remote:Enabled=true --Ion:Remote:Transport=Stdio</c></description></item>
/// </list>
/// Every other argument is passed through unchanged.
/// </remarks>
public static class IonCommandLine
{
	/// <summary>Rewrites the short switches of <paramref name="args"/> (see remarks).</summary>
	public static string[] Normalize(string[] args)
	{
		ArgumentNullException.ThrowIfNull(args);
		var result = new List<string>(args.Length);
		foreach (var arg in args)
		{
			switch (arg)
			{
				case "--headless":
					result.Add("--Ion:Headless=true");
					break;
				case "--headless-render":
					result.Add("--Ion:Headless=true");
					result.Add("--Ion:Headless:Render=true");
					break;
				case "--remote":
					result.Add("--Ion:Remote:Enabled=true");
					break;
				case "--remote-allow-mutations":
					result.Add("--Ion:Remote:Enabled=true");
					result.Add("--Ion:Remote:AllowMutations=true");
					break;
				case "--remote-stdio":
					result.Add("--Ion:Remote:Enabled=true");
					result.Add("--Ion:Remote:Transport=Stdio");
					break;
				default:
					result.Add(arg);
					break;
			}
		}

		return [.. result];
	}
}
