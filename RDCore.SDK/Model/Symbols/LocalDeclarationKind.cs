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
}
