using RDCore.SDK.Model.Source;
using RDCore.SDK.Workspace;
using System.Collections.Immutable;
using RDCore.SDK.Semantics;

namespace RDCore.Runtime.LanguageCore.Static;

// MS-VBAL §3.4.2
public readonly record struct StaticSemanticsOptions(
    ImmutableArray<SourceRange> DeadRanges = default,
    SupportedLanguage? Language = null,
    ConditionalCompilationBlocks? Blocks = null)
{
    public ImmutableArray<SourceRange> Dead => DeadRanges.IsDefault ? [] : DeadRanges;

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

    // the excluded branch the position is in, its bounds included; none when the position is in live code.
    public SourceRange? DeadRangeAt(SourcePosition position)
    {
        foreach (var dead in Dead)
        {
            if (dead.Start <= position && position <= dead.End)
            {
                return dead;
            }
        }

        return null;
    }
}
