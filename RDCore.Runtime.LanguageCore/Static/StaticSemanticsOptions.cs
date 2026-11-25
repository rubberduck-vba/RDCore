using RDCore.SDK.Model.Source;
using RDCore.SDK.Workspace;
using System.Collections.Immutable;
using RDCore.SDK.Semantics;

namespace RDCore.Runtime.LanguageCore.Static;

// MS-VBAL §3.4.2. DrawsOnMe: the body is the code of a Visual Basic 6.0 form, whose graphics statements written with no object draw on the form, Me.
public readonly record struct StaticSemanticsOptions(
    ImmutableArray<SourceLocation> DeadRanges = default,
    SupportedLanguage? Language = null,
    ConditionalCompilationBlocks? Blocks = null,
    bool DrawsOnMe = false)
{
    public ImmutableArray<SourceLocation> Dead => DeadRanges.IsDefault ? [] : DeadRanges;

    // a branch contains what is written in its own file only: a module of several files may have a branch of each at the same range.
    public bool IsDead(SourceLocation location)
    {
        foreach (var dead in Dead)
        {
            if (dead.Uri == location.Uri && dead.Range.Start <= location.Range.Start && location.Range.End <= dead.Range.End)
            {
                return true;
            }
        }

        return false;
    }

    // the excluded branch of the document the position is in, its bounds included; none when the position is in live code.
    public SourceRange? DeadRangeAt(Uri document, SourcePosition position)
    {
        foreach (var dead in Dead)
        {
            if (dead.Uri == document && dead.Range.Start <= position && position <= dead.Range.End)
            {
                return dead.Range;
            }
        }

        return null;
    }
}
