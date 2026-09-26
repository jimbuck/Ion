using System.Collections.Generic;
using System.Linq;
using System.Text;

using Microsoft.CodeAnalysis;

namespace Ion.Extensions.Networking.Generators;

/// <summary>How a serialized member is written.</summary>
internal enum LeafKind
{
	Bool,
	Byte,
	SByte,
	Int16,
	UInt16,
	Int32,
	UInt32,
	Int64,
	UInt64,
	Char,
	Single,
	Double,
	Vector2,
	Vector3,
	Vector4,
	Quaternion,
	FixedString32,
	FixedString64,
	FixedString128,
	NetworkId,
	Enum,
	Struct,
}

/// <summary>One serialized member of a struct.</summary>
internal sealed class MemberModel
{
	public MemberModel(string name, LeafKind kind, string typeName, string typeDisplay, LeafKind underlying, StructModel? nested)
	{
		Name = name;
		Kind = kind;
		TypeName = typeName;
		TypeDisplay = typeDisplay;
		Underlying = underlying;
		Nested = nested;
	}

	/// <summary>The member name (field or property).</summary>
	public string Name { get; }

	public LeafKind Kind { get; }

	/// <summary>The fully qualified type (with <c>global::</c>).</summary>
	public string TypeName { get; }

	/// <summary>The type as it appears in the layout signature.</summary>
	public string TypeDisplay { get; }

	/// <summary>For an enum, the kind of its underlying type.</summary>
	public LeafKind Underlying { get; }

	/// <summary>For a nested struct, its model.</summary>
	public StructModel? Nested { get; }
}

/// <summary>A struct whose members are serialized (a network type, or a struct nested in one).</summary>
internal sealed class StructModel
{
	public StructModel(string typeName, string key, List<MemberModel> members)
	{
		TypeName = typeName;
		Key = key;
		Members = members;
	}

	/// <summary>The fully qualified type (with <c>global::</c>).</summary>
	public string TypeName { get; }

	/// <summary>An identifier unique in the generated file, for helper names.</summary>
	public string Key { get; }

	public List<MemberModel> Members { get; }

	/// <summary>The largest size of the full form.</summary>
	public int MaxSize => Members.Sum(static m => SizeOf(m));

	/// <summary>The layout signature: members and types, nested structs in braces.</summary>
	public string Layout
	{
		get
		{
			var builder = new StringBuilder();
			foreach (var member in Members)
			{
				if (builder.Length > 0) builder.Append(';');
				builder.Append(member.Name).Append(':');
				if (member.Nested is not null) builder.Append('{').Append(member.Nested.Layout).Append('}');
				else builder.Append(member.TypeDisplay);
			}

			return builder.ToString();
		}
	}

	public static int SizeOf(MemberModel member) => member.Kind switch
	{
		LeafKind.Bool or LeafKind.Byte or LeafKind.SByte => 1,
		LeafKind.Int16 or LeafKind.UInt16 or LeafKind.Char => 2,
		LeafKind.Int32 or LeafKind.UInt32 or LeafKind.Single => 4,
		LeafKind.Int64 or LeafKind.UInt64 or LeafKind.Double or LeafKind.Vector2 => 8,
		LeafKind.Vector3 => 12,
		LeafKind.Vector4 or LeafKind.Quaternion => 16,
		LeafKind.FixedString32 => 32,
		LeafKind.FixedString64 => 64,
		LeafKind.FixedString128 => 128,
		LeafKind.NetworkId => 5,
		LeafKind.Enum => SizeOf(new MemberModel("", member.Underlying, "", "", member.Underlying, null)),
		LeafKind.Struct => member.Nested!.MaxSize,
		_ => 0,
	};
}

/// <summary>A replicated component or network message to generate.</summary>
internal sealed class NetworkTypeModel
{
	public NetworkTypeModel(StructModel model, string name, bool isMessage)
	{
		Model = model;
		Name = name;
		IsMessage = isMessage;
	}

	public StructModel Model { get; }

	/// <summary>The registry name (full name without <c>global::</c>).</summary>
	public string Name { get; }

	public bool IsMessage { get; }

	public int Authority { get; set; }

	public bool Predicted { get; set; }

	public bool Interpolated { get; set; }

	public int Delivery { get; set; } = 3;

	public int Direction { get; set; }
}

/// <summary>Builds <see cref="StructModel"/>s from symbols, reporting the members that cannot be serialized.</summary>
internal sealed class StructAnalyzer
{
	/// <summary>The most bytes one entity's component may take in a snapshot (ION203).</summary>
	public const int MaxComponentBytes = 1024;

	private const int MaxDepth = 8;

	private readonly Compilation _compilation;
	private readonly List<Diagnostic> _diagnostics;
	private readonly Dictionary<ITypeSymbol, StructModel> _cache = new(SymbolEqualityComparer.Default);
	private readonly INamedTypeSymbol? _ignore;
	private readonly HashSet<string> _keys = [];

	public StructAnalyzer(Compilation compilation, List<Diagnostic> diagnostics)
	{
		_compilation = compilation;
		_diagnostics = diagnostics;
		_ignore = compilation.GetTypeByMetadataName("Ion.Extensions.Networking.NetworkIgnoreAttribute");
	}

	/// <summary>Every nested struct model created, for emitting their helpers.</summary>
	public IEnumerable<StructModel> Nested => _nested;

	private readonly List<StructModel> _nested = [];

	/// <summary>The model of <paramref name="type"/>, or null when a member cannot be serialized (reported at <paramref name="location"/>).</summary>
	public StructModel? Analyze(INamedTypeSymbol type, Location location) => Build(type, location, type.Name, 0, topLevel: true);

	private StructModel? Build(ITypeSymbol type, Location location, string root, int depth, bool topLevel)
	{
		if (!topLevel && _cache.TryGetValue(type, out var cached)) return cached;

		var members = new List<MemberModel>();
		var ok = true;
		var display = type.ToDisplayString();

		if (type is INamedTypeSymbol { IsGenericType: true })
		{
			Report(NetworkDiagnostics.UnsupportedMember, location, root, display, "generic structs are not supported");
			return null;
		}

		foreach (var symbol in type.GetMembers())
		{
			if (symbol is not IFieldSymbol field || field.IsStatic || field.IsConst) continue;

			ISymbol member = field;
			ITypeSymbol memberType = field.Type;
			bool writable;
			if (field.AssociatedSymbol is IPropertySymbol property)
			{
				member = property;
				memberType = property.Type;
				writable = property.SetMethod is not null && IsAccessible(property.SetMethod) && IsAccessible(property);
			}
			else
			{
				writable = !field.IsReadOnly && IsAccessible(field);
			}

			if (field.IsFixedSizeBuffer)
			{
				Report(NetworkDiagnostics.UnsupportedMember, location, root, member.Name, "fixed-size buffers are not supported (use FixedString32/64/128 or separate members)");
				ok = false;
				continue;
			}

			if (HasIgnore(member) || HasIgnore(field)) continue;

			if (memberType.ToDisplayString() == "Arch.Core.Entity")
			{
				_diagnostics.Add(Diagnostic.Create(NetworkDiagnostics.EntityMember, location, root, member.Name));
				continue;
			}

			if (!writable)
			{
				Report(NetworkDiagnostics.UnsupportedMember, location, root, member.Name, "it is not writable from this assembly (a readonly field, a get-only or private property, or an inaccessible member); make it writable or mark it [NetworkIgnore]");
				ok = false;
				continue;
			}

			var model = Member(member.Name, memberType, location, root, depth);
			if (model is null)
			{
				ok = false;
				continue;
			}

			members.Add(model);
		}

		if (members.Count > 64)
		{
			Report(NetworkDiagnostics.UnsupportedMember, location, root, display, "more than 64 serialized members (the change mask is 64 bits); group members into nested structs");
			ok = false;
		}

		if (!ok) return null;

		var result = new StructModel(type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), UniqueKey(type), members);
		if (!topLevel)
		{
			_cache[type] = result;
			_nested.Add(result);
		}

		return result;
	}

	private MemberModel? Member(string name, ITypeSymbol type, Location location, string root, int depth)
	{
		var full = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
		var display = type.ToDisplayString();

		var kind = Leaf(type);
		if (kind is LeafKind leaf) return new MemberModel(name, leaf, full, display, leaf, null);

		if (type.TypeKind == TypeKind.Enum && type is INamedTypeSymbol { EnumUnderlyingType: { } underlying } && Leaf(underlying) is LeafKind underlyingKind)
		{
			return new MemberModel(name, LeafKind.Enum, full, display, underlyingKind, null);
		}

		if (!type.IsValueType || type.IsReferenceType)
		{
			Report(NetworkDiagnostics.UnsupportedMember, location, root, name, $"'{display}' is not a value type");
			return null;
		}

		if (type.TypeKind != TypeKind.Struct || type.SpecialType != SpecialType.None || type is INamedTypeSymbol { IsGenericType: true })
		{
			Report(NetworkDiagnostics.UnsupportedMember, location, root, name, $"'{display}' is not a supported type (primitives, enums, System.Numerics vectors and quaternions, FixedString32/64/128, NetworkId, or a struct of those)");
			return null;
		}

		if (depth >= MaxDepth)
		{
			Report(NetworkDiagnostics.UnsupportedMember, location, root, name, "structs are nested too deeply");
			return null;
		}

		var nested = Build(type, location, root, depth + 1, topLevel: false);
		return nested is null ? null : new MemberModel(name, LeafKind.Struct, full, display, LeafKind.Struct, nested);
	}

	private static LeafKind? Leaf(ITypeSymbol type)
	{
		switch (type.SpecialType)
		{
			case SpecialType.System_Boolean: return LeafKind.Bool;
			case SpecialType.System_Byte: return LeafKind.Byte;
			case SpecialType.System_SByte: return LeafKind.SByte;
			case SpecialType.System_Int16: return LeafKind.Int16;
			case SpecialType.System_UInt16: return LeafKind.UInt16;
			case SpecialType.System_Int32: return LeafKind.Int32;
			case SpecialType.System_UInt32: return LeafKind.UInt32;
			case SpecialType.System_Int64: return LeafKind.Int64;
			case SpecialType.System_UInt64: return LeafKind.UInt64;
			case SpecialType.System_Char: return LeafKind.Char;
			case SpecialType.System_Single: return LeafKind.Single;
			case SpecialType.System_Double: return LeafKind.Double;
		}

		return type.ToDisplayString() switch
		{
			"System.Numerics.Vector2" => LeafKind.Vector2,
			"System.Numerics.Vector3" => LeafKind.Vector3,
			"System.Numerics.Vector4" => LeafKind.Vector4,
			"System.Numerics.Quaternion" => LeafKind.Quaternion,
			"Ion.Extensions.Networking.FixedString32" => LeafKind.FixedString32,
			"Ion.Extensions.Networking.FixedString64" => LeafKind.FixedString64,
			"Ion.Extensions.Networking.FixedString128" => LeafKind.FixedString128,
			"Ion.Extensions.Networking.NetworkId" => LeafKind.NetworkId,
			_ => null,
		};
	}

	private bool IsAccessible(ISymbol symbol) => _compilation.IsSymbolAccessibleWithin(symbol, _compilation.Assembly);

	private bool HasIgnore(ISymbol symbol) =>
		_ignore is not null && symbol.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, _ignore));

	private string UniqueKey(ITypeSymbol type)
	{
		var builder = new StringBuilder();
		foreach (var c in type.ToDisplayString()) builder.Append(char.IsLetterOrDigit(c) ? c : '_');
		var key = builder.ToString();
		var unique = key;
		for (var i = 2; !_keys.Add(unique); i++) unique = key + "_" + i;
		return unique;
	}

	private void Report(DiagnosticDescriptor descriptor, Location location, params object[] args) =>
		_diagnostics.Add(Diagnostic.Create(descriptor, location, args));
}
