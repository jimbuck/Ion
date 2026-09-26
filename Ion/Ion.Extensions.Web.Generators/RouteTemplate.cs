using System.Collections.Immutable;
using System.Text;

namespace Ion.Extensions.Web.Generators;

internal enum SegmentKind
{
	Literal,
	Parameter,
	CatchAll,
}

internal readonly record struct RouteSegment(SegmentKind Kind, string Name);

/// <summary>The route template rules of <c>WebRoute</c>, checked at compile time (kept in step with the runtime parser).</summary>
internal static class RouteTemplate
{
	public static bool TryParse(string template, out ImmutableArray<RouteSegment> segments, out string? error)
	{
		segments = ImmutableArray<RouteSegment>.Empty;
		error = null;
		if (string.IsNullOrEmpty(template) || template[0] != '/')
		{
			error = "it must start with '/'";
			return false;
		}

		if (template.IndexOf('?') >= 0 || template.IndexOf('#') >= 0)
		{
			error = "it cannot contain a query or a fragment";
			return false;
		}

		var body = template.Length > 1 && template[template.Length - 1] == '/' ? template.Substring(1, template.Length - 2) : template.Substring(1);
		if (body.Length == 0) return true;
		var parts = body.Split('/');
		var builder = ImmutableArray.CreateBuilder<RouteSegment>(parts.Length);
		var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		for (var i = 0; i < parts.Length; i++)
		{
			var part = parts[i];
			if (part.Length == 0)
			{
				error = "it has an empty segment ('//')";
				return false;
			}

			if (part[0] == '{')
			{
				if (part[part.Length - 1] != '}' || part.Length < 3)
				{
					error = $"segment '{part}' must be '{{name}}' or '{{*name}}'";
					return false;
				}

				var catchAll = part[1] == '*';
				var name = part.Substring(catchAll ? 2 : 1, part.Length - (catchAll ? 3 : 2));
				if (name.Length == 0 || !IsIdentifier(name))
				{
					error = $"'{name}' in '{part}' is not a parameter name";
					return false;
				}

				if (!names.Add(name))
				{
					error = $"parameter '{name}' appears twice";
					return false;
				}

				if (catchAll && i != parts.Length - 1)
				{
					error = $"the catch-all '{part}' must be the last segment";
					return false;
				}

				builder.Add(new RouteSegment(catchAll ? SegmentKind.CatchAll : SegmentKind.Parameter, name));
			}
			else
			{
				if (part.IndexOf('{') >= 0 || part.IndexOf('}') >= 0)
				{
					error = $"segment '{part}' mixes a literal and a parameter";
					return false;
				}

				foreach (var c in part)
				{
					if (c <= 0x20 || c >= 0x7F)
					{
						error = $"segment '{part}' has a character that must be percent-encoded";
						return false;
					}
				}

				builder.Add(new RouteSegment(SegmentKind.Literal, part));
			}
		}

		segments = builder.MoveToImmutable();
		return true;
	}

	/// <summary>The template with parameter names erased and literals in lower case: equal for templates that match the same paths.</summary>
	public static string Shape(ImmutableArray<RouteSegment> segments)
	{
		if (segments.Length == 0) return "/";
		var builder = new StringBuilder();
		foreach (var s in segments)
		{
			builder.Append('/');
			builder.Append(s.Kind switch
			{
				SegmentKind.Literal => s.Name.ToLowerInvariant(),
				SegmentKind.Parameter => "{}",
				_ => "{*}",
			});
		}

		return builder.ToString();
	}

	public static bool IsValidSocketPath(string path, out string? error)
	{
		error = null;
		if (string.IsNullOrEmpty(path) || path[0] != '/')
		{
			error = "it must start with '/'";
			return false;
		}

		if (path.Contains("//"))
		{
			error = "it has an empty segment ('//')";
			return false;
		}

		foreach (var c in path)
		{
			if (c is '{' or '}' or '?' or '#' || c <= 0x20 || c >= 0x7F)
			{
				error = "WebSocket paths are literal (no parameters, query or special characters)";
				return false;
			}
		}

		return true;
	}

	private static bool IsIdentifier(string name)
	{
		if (!(char.IsLetter(name[0]) || name[0] == '_')) return false;
		foreach (var c in name)
		{
			if (!(char.IsLetterOrDigit(c) || c == '_')) return false;
		}

		return true;
	}
}
