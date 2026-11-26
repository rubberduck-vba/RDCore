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
    private readonly ImmutableArray<(CompiledPlatforms Platform, ImmutableArray<SourceLocation> Dead, ImmutableArray<SourceLocation> Undecided)> _each;

    internal ModulePlatforms(ImmutableArray<(CompiledPlatforms Platform, ImmutableArray<SourceLocation> Dead, ImmutableArray<SourceLocation> Undecided)> each)
        => _each = each;

    /// <summary>
    /// A module with no <c>#If</c> block: every position of it is compiled on every platform.
    /// </summary>
    public static ModulePlatforms Everywhere { get; } = new([]);

    /// <summary>
    /// The platforms the code at <paramref name="location"/> is compiled on.
    /// </summary>
    /// <param name="location">Where the code is written in the module: its file, and a range that starts at the code.</param>
    /// <returns>
    /// <see cref="CompiledPlatforms.All"/> outside any <c>#If</c> block; <see langword="null"/> in a chain whose conditions cannot all be evaluated on some
    /// platform.
    /// </returns>
    public CompiledPlatforms? At(SourceLocation location)
    {
        if (_each.IsEmpty)
        {
            return CompiledPlatforms.All;
        }

        var platforms = CompiledPlatforms.None;
        foreach (var (platform, dead, undecided) in _each)
        {
            if (undecided.Any(branch => Contains(branch, location)))
            {
                return null;
            }

            if (!dead.Any(branch => Contains(branch, location)))
            {
                platforms |= platform;
            }
        }

        return platforms;
    }

    // a module of several files may have a branch of each at the same range: a branch contains what is written in its own file only.
    private static bool Contains(SourceLocation branch, SourceLocation location)
        => branch.Uri == location.Uri
            && location.Range.Start.CompareTo(branch.Range.Start) >= 0 && location.Range.Start.CompareTo(branch.Range.End) <= 0;
}
