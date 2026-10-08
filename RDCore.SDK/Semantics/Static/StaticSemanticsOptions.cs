using RDCore.SDK.Model.Source;
using RDCore.SDK.Workspace;
using System.Collections.Immutable;

namespace RDCore.SDK.Semantics.Static;

/// <summary>
/// What the build and the language a body is analyzed for decide about which of its statements the static pass looks at, and which it rejects.
/// </summary>
/// <param name="DeadRanges">
/// The source ranges of the <c>#If</c>, <c>#ElseIf</c> and <c>#Else</c> branches that are excluded, which <strong>MS-VBAL §3.4.2</strong> logically
/// removes before the rest of the language sees them: a statement or a label inside one is not analyzed, and defines nothing. The default is no
/// conditional compilation.
/// </param>
/// <param name="Language">
/// The language the body is written in, which decides which statements exist at all - a bare <c>Print</c> is a statement of a BASIC and of no other
/// language. <see langword="null"/> - the default - states no language, and so applies no language's rules.
/// </param>
/// <param name="Blocks">
/// The <c>#If</c> blocks of the module, whatever they evaluate to, which say that a name declared in each branch of one is declared once. <see langword="null"/>
/// - the default - states none: a module with no conditional compilation.
/// </param>
public readonly record struct StaticSemanticsOptions(
    ImmutableArray<SourceRange> DeadRanges = default,
    SupportedLanguage? Language = null,
    ConditionalCompilationBlocks? Blocks = null)
{
    /// <summary>
    /// <see cref="DeadRanges"/>, never the default (uninitialized) array.
    /// </summary>
    public ImmutableArray<SourceRange> Dead => DeadRanges.IsDefault ? [] : DeadRanges;

    /// <summary>
    /// Whether a node written at <paramref name="range"/> is inside a branch that is excluded, at any depth.
    /// </summary>
    /// <param name="range">Where the node is written.</param>
    public bool IsDead(SourceRange range)
    {
        foreach (var dead in Dead)
        {
            if (dead.Start <= range.Start && range.End <= dead.End)
            {
                return true;
            }
        }

        return false;
    }
}
