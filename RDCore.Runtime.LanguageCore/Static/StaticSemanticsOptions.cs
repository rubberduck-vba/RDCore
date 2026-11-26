using RDCore.SDK.Model.Source;
using RDCore.SDK.Workspace;
using System.Collections.Immutable;
using RDCore.SDK.Semantics;

namespace RDCore.Runtime.LanguageCore.Static;

// MS-VBAL §3.4.2. DrawsOnMe: the body is the code of a Visual Basic 6.0 form, whose graphics statements written with no object draw on the form, Me.
// DeadAt: whether a location is in an excluded branch, as the environment host evaluated the module's directives; it stands for DeadRanges where a caller has
// the host's evaluation of another parse of the module than the one it checks.
public readonly record struct StaticSemanticsOptions(
    ImmutableArray<SourceRange> DeadRanges = default,
    SupportedLanguage? Language = null,
    ConditionalCompilationBlocks? Blocks = null,
    bool DrawsOnMe = false,
    Func<SourceLocation, bool>? DeadAt = null)
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

    // whether the location is in an excluded branch: as DeadAt says, when it is given; otherwise when its start is in one of DeadRanges.
    public bool IsDeadAt(SourceLocation location) => DeadAt?.Invoke(location) ?? DeadRangeAt(location.Range.Start) is not null;
}
