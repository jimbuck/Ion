using System.Text;

using Microsoft.CodeAnalysis.Text;

namespace Ion.Generators;

/// <summary>
/// Minimal indented source builder used by the Ion source generators.
/// Shared between generator projects as a linked source file.
/// </summary>
internal sealed class SourceWriter
{
	private const string INDENT = "\t";

	private readonly StringBuilder _builder = new();
	private int _indent;

	public void WriteLine(string line)
	{
		if (line.Length > 0)
		{
			for (var i = 0; i < _indent; i++) _builder.Append(INDENT);
		}

		_builder.Append(line).Append('\n');
	}

	public void WriteEmptyLines(int count)
	{
		for (var i = 0; i < count; i++) _builder.Append('\n');
	}

	public void OpenBlock()
	{
		WriteLine("{");
		_indent++;
	}

	public void CloseBlock()
	{
		if (_indent == 0) throw new InvalidOperationException("No open block to close.");

		_indent--;
		WriteLine("}");
	}

	/// <summary>Closes a block, writing <paramref name="suffix"/> after the brace (for example <c>,</c> or <c>);</c>).</summary>
	public void CloseBlock(string suffix)
	{
		if (_indent == 0) throw new InvalidOperationException("No open block to close.");

		_indent--;
		WriteLine("}" + suffix);
	}

	/// <summary>Indents the following lines without writing a brace.</summary>
	public void OpenIndent() => _indent++;

	/// <summary>Removes one level of indentation opened with <see cref="OpenIndent"/>.</summary>
	public void CloseIndent()
	{
		if (_indent == 0) throw new InvalidOperationException("No open indent to close.");
		_indent--;
	}

	public void CloseAllBlocks()
	{
		while (_indent > 0) CloseBlock();
	}

	public SourceText ToSourceText() => SourceText.From(_builder.ToString(), Encoding.UTF8);

	public override string ToString() => _builder.ToString();
}
