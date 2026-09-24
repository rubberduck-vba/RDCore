using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.SDK.Runtime.Shared;

/// <summary>
/// The hidden per-activation state a <c>For Each</c> loop's opener stashes for its own closer to read
/// back (<strong>MS-VBAL §5.4.2.4</strong>) — arrays only today; <c>Array</c>/<c>Index</c> are the
/// enumeration cursor over it.
/// </summary>
/// <param name="Control">The <c>bound-variable-expression</c> symbol the loop assigns each element to.</param>
/// <param name="ControlExpression">
/// The control variable's own expression node, reused as the location-bearing node for every
/// Let/Set-assignment step a <c>Next</c> performs — never a synthetic stand-in.
/// </param>
/// <param name="Array">The collection, evaluated once and never re-evaluated.</param>
/// <param name="Index">The flat (column-major) index of the element currently assigned to <see cref="Control"/>.</param>
public readonly record struct ForEachState(ITypedSymbol Control, ExpressionNode ControlExpression, VBArrayValue Array, int Index);
