using RDCore.SDK.Model.Source;

namespace RDCore.SDK.Model.Symbols.Abstract;

/// <summary>
/// The conditional-compilation state of a single <see cref="SymbolDefinition"/> — whether the
/// <c>#If</c> branch the declaration site sits in is the one that compiles.
/// </summary>
public enum DefinitionState
{
    /// <summary>
    /// The branch has not been evaluated. A later precompiler-evaluation pass resolves it to
    /// <see cref="Live"/> or <see cref="Dead"/>; until then every site of a multi-branch symbol is
    /// <see cref="Unknown"/>.
    /// </summary>
    Unknown,

    /// <summary>
    /// The branch compiles: this is a live declaration site.
    /// </summary>
    Live,

    /// <summary>
    /// The branch does not compile: this declaration site is dead code, but it is still tracked so a
    /// rename or move affects it alongside the live site.
    /// </summary>
    Dead,
}

/// <summary>
/// One source site where a <see cref="BoundSymbol"/> is declared. A symbol carries more than one only
/// when the same name is declared in several conditional-compilation branches
/// (<c>#If VBA7 Then … #Else … #End If</c>); collapsing the branches to a single symbol lets a
/// rename or refactor see and rewrite every site — live or dead — instead of blanking the inactive
/// branch and losing it.
/// </summary>
/// <param name="Range">The full source span of this declaration site.</param>
/// <param name="SelectionRange">
/// The span to select when navigating to this site — typically the identifier token.
/// </param>
/// <param name="State">
/// Whether this site's conditional-compilation branch compiles. <see cref="DefinitionState.Unknown"/>
/// until a precompiler-evaluation pass resolves it.
/// </param>
public sealed record class SymbolDefinition(SourceRange Range, SourceRange SelectionRange, DefinitionState State = DefinitionState.Unknown);
