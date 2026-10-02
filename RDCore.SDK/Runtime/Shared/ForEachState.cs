using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.SDK.Runtime.Shared;

/// <summary>
/// The hidden per-activation state a <c>For Each</c> loop's opener stashes for its own closer to read
/// back (<strong>MS-VBAL §5.4.2.4</strong>): the enumeration cursor over the collection - an array, or an object that is enumerated.
/// </summary>
/// <param name="Control">The <c>bound-variable-expression</c> symbol the loop assigns each element to.</param>
/// <param name="ControlExpression">
/// The control variable's own expression node, reused as the location-bearing node for every
/// Let/Set-assignment step a <c>Next</c> performs — never a synthetic stand-in.
/// </param>
/// <param name="Array">The collection when it is an array, evaluated once and never re-evaluated; <see langword="null"/> when it is an object.</param>
/// <param name="Index">For an array, the flat (column-major) index of the element currently assigned to <see cref="Control"/>. Unused for an object.</param>
/// <param name="Enumerator">
/// The object that enumerates the collection when it is an object - what its enumeration member (<c>_NewEnum</c>) returned, once, before the first member was
/// asked for; <see langword="null"/> when the collection is an array. It is positioned at the member currently assigned to <see cref="Control"/>.
/// </param>
public readonly record struct ForEachState(
    ITypedSymbol Control, ExpressionNode ControlExpression, VBArrayValue? Array, int Index, VBObjectValue? Enumerator = null);
