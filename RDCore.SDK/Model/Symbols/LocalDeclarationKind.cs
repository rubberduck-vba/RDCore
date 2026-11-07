namespace RDCore.SDK.Model.Symbols;

/// <summary>
/// How a <see cref="VBLocalVariableSymbol"/> first entered its procedure scope.
/// </summary>
public enum LocalDeclarationKind
{
    /// <summary>
    /// An explicit <c>Dim</c> or <c>Static</c> declaration (MS-VBAL &#167;5.4.3.1). This is also the
    /// value carried by a parameter symbol, for which the distinction does not apply.
    /// </summary>
    Dim,

    /// <summary>
    /// An <em>implicit</em> declaration introduced by a <c>ReDim</c> statement whose unqualified
    /// target resolved to nothing (MS-VBAL &#167;5.4.3.3). Legal under <c>Option Explicit</c>; a
    /// later analysis pass flags it, and errors on it under <c>Option Strict</c>.
    /// </summary>
    ReDim,

    /// <summary>
    /// An <em>implicit</em> declaration introduced by a simple name expression that resolved to
    /// nothing in a module whose variable declaration mode is implicit: no <c>Option Explicit</c>
    /// (MS-VBAL &#167;5.6.10: "a new local variable is implicitly declared in the current procedure as
    /// if by a local variable declaration statement immediately preceding this statement").
    /// </summary>
    /// <remarks>
    /// Never produced for a module that declares <c>Option Explicit</c>, where the same expression is
    /// a compile error instead.
    /// </remarks>
    Implicit,
}

/// <summary>
/// Reads a <see cref="LocalDeclarationKind"/>.
/// </summary>
public static class LocalDeclarationKindExtensions
{
    /// <summary>
    /// Whether the variable was never actually declared: it came into being because something
    /// referred to it.
    /// </summary>
    /// <remarks>
    /// Both implicit kinds are legal VBA and both are worth reporting: a reader cannot tell a
    /// deliberate implicit variable from a misspelling of a real one.
    /// </remarks>
    /// <param name="kind">How the variable entered its procedure scope.</param>
    public static bool IsImplicit(this LocalDeclarationKind kind)
        => kind is LocalDeclarationKind.ReDim or LocalDeclarationKind.Implicit;
}
