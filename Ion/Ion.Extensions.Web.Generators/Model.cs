using System.Collections;
using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Ion.Extensions.Web.Generators;

/// <summary>An immutable array with value equality (for incremental caching).</summary>
internal readonly struct EquatableArray<T>(ImmutableArray<T> items) : IEquatable<EquatableArray<T>>, IEnumerable<T> where T : IEquatable<T>
{
	private readonly ImmutableArray<T> _items = items;

	public ImmutableArray<T> Items => _items.IsDefault ? ImmutableArray<T>.Empty : _items;

	public int Count => Items.Length;

	public bool Equals(EquatableArray<T> other) => Items.SequenceEqual(other.Items);

	public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

	public override int GetHashCode()
	{
		var hash = 17;
		foreach (var item in Items) hash = hash * 31 + (item?.GetHashCode() ?? 0);
		return hash;
	}

	public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)Items).GetEnumerator();

	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>A source location that survives incremental caching.</summary>
internal sealed record LocationInfo(string Path, TextSpan Span, LinePositionSpan Lines)
{
	public static LocationInfo? From(Location? location) =>
		location is null || location.SourceTree is null ? null : new LocationInfo(location.SourceTree.FilePath, location.SourceSpan, location.GetLineSpan().Span);

	public Location ToLocation() => Location.Create(Path, Span, Lines);
}

/// <summary>A diagnostic to report.</summary>
internal sealed record DiagnosticInfo(string Id, LocationInfo? Location, string Message);

/// <summary>One [Http] route: what the table entry needs and the invoker's body.</summary>
internal sealed record HttpRouteModel(string Verb, string Template, string ShapeKey, int Access, string TypeName, string DisplayName, string Body, LocationInfo? Location);

/// <summary>One [WebSocket] endpoint.</summary>
internal sealed record SocketRouteModel(string Path, int Access, string TypeName, string DisplayName, string Call, LocationInfo? Location);

/// <summary>What one attributed method contributes.</summary>
internal sealed record EndpointResult(EquatableArray<HttpRouteModel> Routes, EquatableArray<SocketRouteModel> Sockets, EquatableArray<DiagnosticInfo> Diagnostics);
