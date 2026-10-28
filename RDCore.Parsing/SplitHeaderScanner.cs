using System.Text.RegularExpressions;

namespace RDCore.Parsing;

/// <summary>
/// Finds the procedure headers a module writes once in each branch of a <c>#If</c> block, around one body (<strong>MS-VBAL §3.4</strong>): the branches the
/// main parse is not to read, because each opens the same procedure the branch it does read opens.
/// </summary>
/// <remarks>
/// <para>
/// Every branch of a block is in the source, and the parse is of every branch, whatever the constants evaluate to. A block each of whose branches opens a
/// procedure and leaves it open - <c>#If VBA7 Then</c> / <c>Function F(p As LongPtr) As LongPtr</c> / <c>#Else</c> / <c>Function F(p As Long) As Long</c>
/// / <c>#End If</c>, then one body and one <c>End Function</c> - is read as one procedure: the main parse reads one of its headers, and each other branch is
/// an alternative of it, parsed on its own. MS-VBA compiles the live one (recorded in Excel: oracle\cc-split S01, S02, S09-S11).
/// </para>
/// <para>
/// The main parse reads the branch whose header is of the kind of the statement that closes the procedure, and the first one when none is: a <c>Sub</c>
/// header in one branch and a <c>Function</c> header in the other, closed by an <c>End Function</c>, compiles where the <c>Function</c> branch is live and is
/// "Expected End Sub" where the other is (S03, S04) - which the alternatives let whoever knows the constants decide.
/// </para>
/// <para>
/// It reads lines only - a directive, a procedure header, an <c>End Sub</c>, <c>End Function</c> or <c>End Property</c> - with what is in strings and comments
/// left out; what it does not recognize it leaves to the parse as it is.
/// </para>
/// </remarks>
internal static partial class SplitHeaderScanner
{
    /// <summary>
    /// A branch that is an alternative of another: its lines, from the first after its directive to the next directive, and the statement that closes the
    /// procedure its header opens.
    /// </summary>
    /// <param name="StartLine">The first line of the branch, after its directive.</param>
    /// <param name="EndLine">The line of the next directive of its block, which is not the branch's.</param>
    /// <param name="Closer">The statement that closes the procedure the branch's header opens: <c>End Sub</c>, <c>End Function</c>, <c>End Property</c>.</param>
    public readonly record struct Alternative(int StartLine, int EndLine, string Closer);

    /// <summary>
    /// The branches of <paramref name="content"/> that are alternatives of a procedure header the main parse reads, in source order.
    /// </summary>
    /// <param name="content">The module's source, its line endings <c>\n</c>.</param>
    /// <returns>None when no block's branches each open a procedure, or when its directives do not nest.</returns>
    public static IReadOnlyList<Alternative> Find(string content)
    {
        var lines = content.Split('\n');
        var module = new Branch(0, lines.Length);
        var open = new Stack<(Block Block, Branch Branch)>();
        var current = module;
        for (var line = 0; line < lines.Length; line++)
        {
            var first = line;
            var text = LogicalLine(lines, ref line);
            if (DirectivePattern().Match(text) is { Success: true } directive)
            {
                var keyword = directive.Groups["keyword"].Value;
                if (keyword.Equals("If", StringComparison.OrdinalIgnoreCase))
                {
                    var block = new Block();
                    current.Items.Add(block);
                    open.Push((block, current));
                    current = block.Open(line + 1);
                }
                else if (keyword.StartsWith("Else", StringComparison.OrdinalIgnoreCase))
                {
                    if (open.Count == 0)
                    {
                        return [];
                    }

                    current.EndLine = first;
                    current = open.Peek().Block.Open(line + 1);
                }
                else if (!keyword.Equals("Const", StringComparison.OrdinalIgnoreCase))
                {
                    if (open.Count == 0)
                    {
                        return [];
                    }

                    current.EndLine = first;
                    var (block, outer) = open.Pop();
                    block.EndLine = line;
                    current = outer;
                }

                continue;
            }

            var code = Code(text);
            var opens = HeaderPattern().Match(code) is { Success: true } header ? KindOf(header.Groups["kind"].Value) : null;
            var closes = CloserPattern().Count(code);
            if (opens is not null || closes > 0)
            {
                current.Items.Add(new CodeLine((opens is null ? 0 : 1) - closes, opens));
            }
        }

        if (open.Count > 0)
        {
            return [];
        }

        var alternatives = new List<Alternative>();
        _ = Resolve(module, lines, alternatives);
        return [.. alternatives.OrderBy(alternative => alternative.StartLine)];
    }

    private sealed record class CodeLine(int Net, string? Opens);

    private sealed class Branch(int startLine, int endLine)
    {
        public int StartLine { get; } = startLine;

        public int EndLine { get; set; } = endLine;

        public List<object> Items { get; } = [];
    }

    private sealed class Block
    {
        public List<Branch> Branches { get; } = [];

        public int EndLine { get; set; }

        public Branch Open(int startLine)
        {
            var branch = new Branch(startLine, startLine);
            Branches.Add(branch);
            return branch;
        }
    }

    // how many procedures the branch leaves open, and the kind of the first it opens; the alternatives of the blocks in it are added.
    private static (int Net, string? Opens) Resolve(Branch branch, string[] lines, List<Alternative> alternatives)
    {
        var net = 0;
        string? opens = null;
        foreach (var item in branch.Items)
        {
            var (itemNet, itemOpens) = item switch
            {
                Block block => Resolve(block, lines, alternatives),
                CodeLine code => (code.Net, code.Opens),
                _ => (0, null),
            };

            net += itemNet;
            opens ??= itemOpens;
        }

        return (net, opens);
    }

    // a block whose branches each open a procedure - two at least, and none more than one or closing one - is one procedure: the branch the main parse reads
    // is its procedure as far as what is in it goes, and every other is an alternative, parsed on its own with what is in it.
    private static (int Net, string? Opens) Resolve(Block block, string[] lines, List<Alternative> alternatives)
    {
        var resolved = block.Branches.Select(branch =>
        {
            var inner = new List<Alternative>();
            var (net, opens) = Resolve(branch, lines, inner);
            return (Branch: branch, Net: net, Opens: opens, Inner: inner);
        }).ToList();

        var opening = resolved.Where(branch => branch.Net == 1 && branch.Opens is not null).ToList();
        if (opening.Count < 2 || resolved.Any(branch => branch.Net is not (0 or 1)))
        {
            foreach (var branch in resolved)
            {
                alternatives.AddRange(branch.Inner);
            }

            return (resolved[0].Net, resolved[0].Opens);
        }

        var closer = CloserAfter(lines, block.EndLine + 1);
        var main = opening.FirstOrDefault(branch => string.Equals(branch.Opens, closer, StringComparison.OrdinalIgnoreCase));
        if (main.Branch is null)
        {
            main = opening[0];
        }

        alternatives.AddRange(main.Inner);
        foreach (var other in opening.Where(branch => !ReferenceEquals(branch.Branch, main.Branch)))
        {
            alternatives.Add(new Alternative(other.Branch.StartLine, other.Branch.EndLine, $"End {other.Opens}"));
        }

        // a branch that opens nothing is no alternative of the header: what it holds is the parse's as it is.
        foreach (var other in resolved.Where(branch => branch.Net == 0))
        {
            alternatives.AddRange(other.Inner);
        }

        return (1, main.Opens);
    }

    // the kind of the statement that closes the procedure the block opens: the first End Sub, End Function or End Property after it, before another header.
    private static string? CloserAfter(string[] lines, int from)
    {
        for (var line = from; line < lines.Length; line++)
        {
            var text = LogicalLine(lines, ref line);
            if (DirectivePattern().IsMatch(text))
            {
                continue;
            }

            var code = Code(text);
            if (CloserPattern().Match(code) is { Success: true } closer)
            {
                return KindOf(closer.Groups["kind"].Value);
            }

            if (HeaderPattern().IsMatch(code))
            {
                return null;
            }
        }

        return null;
    }

    private static string KindOf(string keyword)
        => keyword.StartsWith("Property", StringComparison.OrdinalIgnoreCase) ? "Property"
            : keyword.Equals("Sub", StringComparison.OrdinalIgnoreCase) ? "Sub"
            : "Function";

    // a physical line and those a line-continuation joins to it (MS-VBAL §3.2.2); line is left on the last of them.
    private static string LogicalLine(string[] lines, ref int line)
    {
        var text = lines[line];
        while (ContinuationPattern().IsMatch(text) && line + 1 < lines.Length)
        {
            text = ContinuationPattern().Replace(text, " ") + lines[++line];
        }

        return text;
    }

    // what of a line is code: not what is in a string, nor a comment.
    private static string Code(string text)
    {
        var code = new System.Text.StringBuilder(text.Length);
        var inString = false;
        foreach (var character in text)
        {
            if (character == '"')
            {
                inString = !inString;
                code.Append(' ');
                continue;
            }

            if (!inString && character == '\'')
            {
                break;
            }

            code.Append(inString ? ' ' : character);
        }

        var stripped = code.ToString();
        return RemPattern().IsMatch(stripped) ? string.Empty : stripped;
    }

    [GeneratedRegex(@"^[ \t]*#[ \t]*(?<keyword>If|ElseIf|Else|End[ \t]*If|Const)\b", RegexOptions.IgnoreCase)]
    private static partial Regex DirectivePattern();

    // a procedure's header: its access, Static, then Sub, Function or Property Get/Let/Set and a name - a line number or a label before it allowed. A Declare is
    // no header: it is a statement of one line.
    [GeneratedRegex(@"^[ \t]*(?:\w+:[ \t]*|\d+[ \t]+)?(?:(?:Public|Private|Friend|Global)[ \t]+)?(?:Static[ \t]+)?(?<kind>Sub|Function|Property[ \t]+(?:Get|Let|Set))[ \t]+[A-Za-z_\[]",
        RegexOptions.IgnoreCase)]
    private static partial Regex HeaderPattern();

    [GeneratedRegex(@"\bEnd[ \t]+(?<kind>Sub|Function|Property)\b", RegexOptions.IgnoreCase)]
    private static partial Regex CloserPattern();

    [GeneratedRegex(@"[ \t]_[ \t]*$")]
    private static partial Regex ContinuationPattern();

    [GeneratedRegex(@"^[ \t]*(?:\d+[ \t]+)?Rem\b", RegexOptions.IgnoreCase)]
    private static partial Regex RemPattern();
}
