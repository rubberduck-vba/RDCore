using RDCore.SDK.Model.Source;
using RDCore.SDK.Semantics;
using System.Collections.Immutable;

namespace RDCore.Runtime.Semantics.Precompiler;

/// <summary>
/// The platforms each position of a module is compiled on: those whose predefined conditional-compilation constants make live every <c>#If</c> branch the
/// position is written in (<strong>MS-VBAL §3.4.2</strong>), as <see cref="PrecompilerLiveBranchEvaluator.CompiledPlatformsOf"/> evaluated them.
/// </summary>
public sealed class ModulePlatforms
{
    private readonly ImmutableArray<(CompiledPlatforms Platform, ImmutableArray<SourceRange> Dead, ImmutableArray<SourceRange> Undecided)> _each;

    internal ModulePlatforms(ImmutableArray<(CompiledPlatforms Platform, ImmutableArray<SourceRange> Dead, ImmutableArray<SourceRange> Undecided)> each)
        => _each = each;

    /// <summary>
    /// A module with no <c>#If</c> block: every position of it is compiled on every platform.
    /// </summary>
    public static ModulePlatforms Everywhere { get; } = new([]);

    /// <summary>
    /// The platforms the code at <paramref name="position"/> is compiled on.
    /// </summary>
    /// <param name="position">A position in the module.</param>
    /// <returns>
    /// <see cref="CompiledPlatforms.All"/> outside any <c>#If</c> block; <see langword="null"/> in a chain whose conditions cannot all be evaluated on some
    /// platform.
    /// </returns>
    public CompiledPlatforms? At(SourcePosition position)
    {
        if (_each.IsEmpty)
        {
            return CompiledPlatforms.All;
        }

        var platforms = CompiledPlatforms.None;
        foreach (var (platform, dead, undecided) in _each)
        {
            if (undecided.Any(range => Contains(range, position)))
            {
                return null;
            }

            if (!dead.Any(range => Contains(range, position)))
            {
                platforms |= platform;
            }
        }

        return platforms;
    }

    private static bool Contains(SourceRange range, SourcePosition position) => position.CompareTo(range.Start) >= 0 && position.CompareTo(range.End) <= 0;
}
