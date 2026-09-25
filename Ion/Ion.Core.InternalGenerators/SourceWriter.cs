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

	public void CloseAllBlocks()
	{
		while (_indent > 0) CloseBlock();
	}

	public SourceText ToSourceText() => SourceText.From(_builder.ToString(), Encoding.UTF8);

	public override string ToString() => _builder.ToString();
}
