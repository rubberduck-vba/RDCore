using RDCore.SDK.Model.Source;
using System.Collections.Immutable;

namespace RDCore.SDK.Semantics.Instructions;

/// <summary>
/// What lowering needs to know about the build a procedure is being lowered for.
/// </summary>
/// <remarks>
/// A growable record rather than a parameter list: lowering already had to be told which
/// conditional-compilation branches are dead, the build now also decides whether <c>Debug</c>
/// statements exist at all, and each later answer of this kind is a property here rather than another
/// parameter on every call site.
/// <para>
/// Everything here is a property of the <em>build</em>, not of the source — two builds of the same
/// procedure can legitimately lower to different instructions, which is the point.
/// </para>
/// </remarks>
/// <param name="DeadRanges">
/// The source ranges of every <c>#If</c>/<c>#ElseIf</c>/<c>#Else</c> branch that is not live. Empty
/// when the body has no <c>#If</c> at all. A child lexically inside one is never lowered, at any depth.
/// </param>
/// <param name="IsReleaseBuild">
/// Whether this is a release build, in which <c>Debug.Print</c> and <c>Debug.Assert</c> are not lowered
/// at all — they leave no instruction behind, not a no-op one, so a release build pays nothing for a
/// <c>Debug.Print</c> left in the source. Driven by the <c>DEBUG</c> conditional compilation constant,
/// so that <c>#If DEBUG Then</c> and "does this <c>Debug.Print</c> run" are the same fact rather than
/// two settings that can disagree.
/// <para>
/// Stated negatively on purpose. This is a <c>record struct</c>, so <c>default</c> zeroes every field
/// regardless of the constructor's own defaults — the state a caller gets by saying nothing has to be
/// the safe one, and the safe one is a debug build that keeps the statements.
/// </para>
/// </param>
/// <param name="Language">
/// The language the body is written in, which decides which statements exist at all - a bare <c>Print</c> is a statement of a BASIC and of
/// no other language, so lowering one for another is an error. <see langword="null"/> - the default - states no language, and so applies
/// no language's rules: the body is lowered as written.
/// </param>
public readonly record struct InstructionLoweringOptions(
    ImmutableArray<SourceRange> DeadRanges = default,
    bool IsReleaseBuild = false,
    Workspace.SupportedLanguage? Language = null)
{
    /// <summary>
    /// A debug build of a body with no conditional compilation in it.
    /// </summary>
    public static readonly InstructionLoweringOptions Default = new();

    /// <summary>
    /// <see cref="DeadRanges"/>, never the default (uninitialized) array.
    /// </summary>
    public ImmutableArray<SourceRange> Dead => DeadRanges.IsDefault ? [] : DeadRanges;

    /// <summary>
    /// Whether <c>Debug</c> statements are lowered — every build but a release one.
    /// </summary>
    public bool IncludeDebugStatements => !IsReleaseBuild;
}
