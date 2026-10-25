using System.Buffers.Binary;
using System.Globalization;
using System.Text.RegularExpressions;

namespace RDCore.SDK.Workspace.Forms;

/// <summary>
/// Where a form module's designer data is, in the module's companion file.
/// </summary>
/// <param name="FileName">The companion file's name, as the module writes it (<c>ExampleView.frx</c>): relative to the
/// module's own file.</param>
/// <param name="Offset">The offset of the designer data in the companion file.</param>
public sealed record class FormDesignerReference(string FileName, long Offset);

/// <summary>
/// Reads what a form module's designer declares: the form's class and designer properties, and its controls.
/// </summary>
/// <remarks>
/// A form module (<c>.frm</c>) opens on a <c>VERSION 5.00</c> header line and a designer block:
/// <c>Begin &lt;class&gt; &lt;name&gt;</c> … <c>End</c>, before its code-behind. What the block holds depends on the
/// designer that wrote it:
/// <list type="bullet">
/// <item>A <b>VBA UserForm</b> (<c>Begin {C62A69F0-16DC-11CE-9E98-00AA00574A4F} UserForm1</c>) holds the form's own
/// properties only. Its controls are in the designer data Microsoft Forms persisted, which the VBE exports to the
/// companion <c>.frx</c> file at the <c>OleObjectBlob</c> property's offset: a 24-byte blob header, whose bytes 4 to 7 are
/// the length of what follows it, then a compound file ([MS-CFB]) holding the form's storage ([MS-OFORMS] 2.1.2). The blob
/// header is not documented; its layout is what the VBE writes.</item>
/// <item>A <b>Visual Basic 6.0</b> form (<c>Begin VB.Form Form1</c>) holds its controls too, as nested <c>Begin</c> …
/// <c>End</c> blocks, each with its properties; <c>BeginProperty</c> … <c>EndProperty</c> blocks group a property's own
/// properties (a <c>Font</c>). Its <c>.frx</c> file holds binary property values (pictures, lists), which the controls'
/// declarations do not need.</item>
/// </list>
/// The reader takes the companion file's content from its caller, which owns the file system:
/// <see cref="CompanionOf"/> says which file and where.
/// </remarks>
public static partial class FormDesigner
{
    /// <summary>
    /// The class identifier of the Microsoft Forms 2.0 designer, which names a VBA UserForm's designer block.
    /// </summary>
    public static readonly Guid MSFormsClassId = new("C62A69F0-16DC-11CE-9E98-00AA00574A4F");

    private const int BlobHeaderSize = 24;

    /// <summary>
    /// Where the designer data of the form module <paramref name="source"/> is, or <c>null</c> when the module is not a
    /// form module or keeps no designer data in a companion file (its <c>OleObjectBlob</c> property).
    /// </summary>
    /// <param name="source">The form module's source.</param>
    /// <exception cref="FormDesignerFormatException">The designer block is not terminated.</exception>
    public static FormDesignerReference? CompanionOf(string? source)
        => ReadBlock(source) is { } block ? CompanionOf(block) : null;

    private static FormDesignerReference? CompanionOf(DesignerBlock block)
        => block.Properties.GetValueOrDefault("OleObjectBlob") is { } blob && FileReference().Match(blob) is { Success: true } match
            ? new FormDesignerReference(
                match.Groups["file"].Value.Replace("\"\"", "\""),
                long.Parse(match.Groups["offset"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture))
            : null;

    /// <summary>
    /// What the form module <paramref name="source"/> declares, or <c>null</c> when it is not a form module (it has no
    /// designer block).
    /// </summary>
    /// <param name="source">The form module's source.</param>
    /// <param name="companion">The content of the module's companion file (<see cref="CompanionOf"/>), when the module
    /// keeps designer data there; ignored when it does not.</param>
    /// <exception cref="FormDesignerFormatException">The designer block is not terminated, the module keeps designer data in
    /// a companion file and <paramref name="companion"/> is empty, or that designer data cannot be read.</exception>
    public static FormDescriptor? Read(string? source, ReadOnlyMemory<byte> companion = default)
    {
        if (ReadBlock(source) is not { } block)
        {
            return null;
        }

        var (kind, @class) = ClassOf(block.Class);
        var controls = block.Children.Select(ToDescriptor).ToList();
        if (kind == FormDesignerKind.MSForms && CompanionOf(block) is { } reference)
        {
            controls = [.. MSFormsDesignerStorage.ReadControls(CompoundFile.Open(BlobAt(companion, reference.Offset)))];
        }

        return new FormDescriptor(block.Name, kind, @class, block.Properties, controls);
    }

    // the designer data at `offset` of a companion file: its blob header, then that many bytes
    private static ReadOnlyMemory<byte> BlobAt(ReadOnlyMemory<byte> companion, long offset)
    {
        if (companion.IsEmpty)
        {
            throw new FormDesignerFormatException(FormDesignerFormatProblem.MissingCompanion);
        }

        if (offset < 0 || offset > companion.Length - BlobHeaderSize)
        {
            throw new FormDesignerFormatException(FormDesignerFormatProblem.BlobOutOfRange);
        }

        var length = BinaryPrimitives.ReadUInt32LittleEndian(companion.Span[(int)(offset + 4)..]);
        var start = (int)offset + BlobHeaderSize;
        return length > companion.Length - start
            ? throw new FormDesignerFormatException(FormDesignerFormatProblem.BlobOutOfRange)
            : companion.Slice(start, (int)length);
    }

    private static (FormDesignerKind Kind, FormControlClass Class) ClassOf(string token)
    {
        if (Guid.TryParse(token, out var classId))
        {
            return classId == MSFormsClassId
                ? (FormDesignerKind.MSForms, new FormControlClass(MSFormsDesignerStorage.Library, "UserForm", classId))
                : (FormDesignerKind.Other, new FormControlClass(null, null, classId));
        }

        return (FormDesignerKind.VisualBasic6, ControlClassOf(token));
    }

    // `VB.CommandButton`, `MSComctlLib.ListView`: a library and a class; a bare name has no library
    private static FormControlClass ControlClassOf(string token)
    {
        if (Guid.TryParse(token, out var classId))
        {
            return new FormControlClass(null, null, classId);
        }

        var dot = token.IndexOf('.');
        return dot < 0 ? new FormControlClass(null, token) : new FormControlClass(token[..dot], token[(dot + 1)..]);
    }

    private static FormControlDescriptor ToDescriptor(DesignerBlock block)
        => new(
            block.Name,
            ControlClassOf(block.Class),
            IntegerOf(block.Properties.GetValueOrDefault("TabIndex")),
            IntegerOf(block.Properties.GetValueOrDefault("Index")),
            [.. block.Children.Select(ToDescriptor)]);

    private static int? IntegerOf(string? value)
        => int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var result) ? result : null;

    private sealed record class DesignerBlock(string Class, string Name, Dictionary<string, string> Properties, List<DesignerBlock> Children);

    // the designer block after the VERSION header: null when the source has no VERSION header, or when what follows it is
    // not a `Begin <class> <name>` block (a class module's header is `BEGIN` alone)
    private static DesignerBlock? ReadBlock(string? source)
    {
        if (string.IsNullOrEmpty(source))
        {
            return null;
        }

        var lines = source.Split('\n');
        var index = 0;
        while (index < lines.Length && string.IsNullOrWhiteSpace(lines[index]))
        {
            index++;
        }

        if (index == lines.Length || !VersionHeader().IsMatch(lines[index]))
        {
            return null;
        }

        // a VB6 form lists the ActiveX controls it uses (`Object = "{…}#2.0#0"; "MSCOMCTL.OCX"`) before its block
        for (index++; index < lines.Length; index++)
        {
            var line = lines[index].Trim();
            if (line.Length > 0 && !line.StartsWith("Object", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }
        }

        if (index == lines.Length || BeginLine().Match(lines[index]) is not { Success: true } begin)
        {
            return null;
        }

        var root = new DesignerBlock(begin.Groups["class"].Value, begin.Groups["name"].Value, new(StringComparer.OrdinalIgnoreCase), []);
        var open = new Stack<DesignerBlock>([root]);
        var propertyDepth = 0;
        for (index++; index < lines.Length; index++)
        {
            // a grouped property's own properties (a Font's Name, Size) are not the control's
            var line = lines[index].Trim();
            if (line.StartsWith("BeginProperty", StringComparison.OrdinalIgnoreCase))
            {
                propertyDepth++;
                continue;
            }

            if (line.StartsWith("EndProperty", StringComparison.OrdinalIgnoreCase))
            {
                propertyDepth = Math.Max(0, propertyDepth - 1);
                continue;
            }

            if (propertyDepth > 0)
            {
                continue;
            }

            if (BeginLine().Match(lines[index]) is { Success: true } child)
            {
                var block = new DesignerBlock(child.Groups["class"].Value, child.Groups["name"].Value, new(StringComparer.OrdinalIgnoreCase), []);
                open.Peek().Children.Add(block);
                open.Push(block);
            }
            else if (EndLine().IsMatch(line))
            {
                open.Pop();
                if (open.Count == 0)
                {
                    return root;
                }
            }
            else if (PropertyLine().Match(line) is { Success: true } property)
            {
                open.Peek().Properties[property.Groups["name"].Value] = ValueOf(property.Groups["value"].Value);
            }
        }

        throw new FormDesignerFormatException(FormDesignerFormatProblem.UnterminatedBlock);
    }

    // a property's value as written, without its trailing comment (`1  'CenterOwner`); a string literal unquoted
    private static string ValueOf(string text)
    {
        var inString = false;
        var end = text.Length;
        for (var position = 0; position < text.Length; position++)
        {
            if (text[position] == '"')
            {
                inString = !inString;
            }
            else if (text[position] == '\'' && !inString)
            {
                end = position;
                break;
            }
        }

        var value = text[..end].Trim();
        return StringLiteral().Match(value) is { Success: true } literal
            ? literal.Groups["value"].Value.Replace("\"\"", "\"")
            : value;
    }

    [GeneratedRegex(@"^[ \t]*VERSION[ \t]+[0-9]+(?:\.[0-9]+)?[ \t]*\r?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VersionHeader();

    [GeneratedRegex(@"^[ \t]*Begin[ \t]+(?<class>\{[0-9A-Fa-f-]+\}|[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)?)[ \t]+(?<name>[A-Za-z_][A-Za-z0-9_]*)[ \t]*\r?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BeginLine();

    [GeneratedRegex(@"^End$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EndLine();

    [GeneratedRegex(@"^(?<name>[A-Za-z_][A-Za-z0-9_]*)[ \t]*=[ \t]*(?<value>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex PropertyLine();

    [GeneratedRegex(@"^""(?<value>(?:[^""]|"""")*)""$", RegexOptions.CultureInvariant)]
    private static partial Regex StringLiteral();

    [GeneratedRegex(@"^""(?<file>(?:[^""]|"""")*)"":(?<offset>[0-9A-Fa-f]+)$", RegexOptions.CultureInvariant)]
    private static partial Regex FileReference();
}
