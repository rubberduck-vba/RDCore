using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.SDK.Workspace;

/// <summary>
/// The text of a program of the platform's BASIC: a <c>.rdc</c> file, and the module it is as far as the language is concerned.
/// </summary>
/// <remarks>
/// A program of the BASIC is lines - <c>100 X = 1</c>, <c>110 PRINT X</c> - with no procedure around them and no header: the lines are the program, and a line
/// number is a line label, which is real MS-VBAL syntax (<strong>MS-VBAL §5.4.1.1</strong>). What the language makes of the text is a module: the lines are the
/// body of its one procedure, <see cref="EntryPointName"/>, which is what running the program runs. This is that module, and where its lines are in the text.
/// </remarks>
public static class BasicProgramText
{
    /// <summary>The extension of the files a program of the BASIC is saved in.</summary>
    public const string Extension = ".rdc";

    /// <summary>The name of the procedure the lines of a program make up.</summary>
    public const string EntryPointName = "Main";

    // VBA source is CRLF, and the parser's own line/column positions are reported against it.
    private const string NewLine = "\r\n";

    /// <summary>The header of the module a program is: the procedure its lines are the body of.</summary>
    public static string Header { get; } = $"Public Sub {EntryPointName}(){NewLine}";

    /// <summary>The footer of the module a program is: the end of its procedure.</summary>
    public static string Footer { get; } = $"End Sub{NewLine}";

    /// <summary>
    /// How many lines the module has before the first line of the text, which is the header's: a line of the module is this many lines further than the
    /// line of the text it is.
    /// </summary>
    public static int HeaderLines => 1;

    /// <summary>
    /// Whether a document is a program of the BASIC, by the file it is.
    /// </summary>
    /// <param name="path">The path or the name of the file.</param>
    public static bool IsProgram(string path) => string.Equals(Path.GetExtension(path), Extension, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The module a program is.
    /// </summary>
    /// <param name="text">The text of the program: its lines.</param>
    public static string ToModuleSource(string text)
        => text.Length == 0 ? Header + Footer : Header + (text.EndsWith('\n') || text.EndsWith('\r') ? text : text + NewLine) + Footer;

    /// <summary>
    /// Where in the text of a program a range of its module is.
    /// </summary>
    /// <param name="range">A range of the module.</param>
    /// <returns>The range of the text; a range that is the header's or the footer's is the start of the text.</returns>
    public static Range ToTextRange(Range range)
        => new(ToTextPosition(range.Start), ToTextPosition(range.End));

    private static Position ToTextPosition(Position position)
        => position.Line < HeaderLines ? new Position(0, 0) : new Position(position.Line - HeaderLines, position.Character);
}
