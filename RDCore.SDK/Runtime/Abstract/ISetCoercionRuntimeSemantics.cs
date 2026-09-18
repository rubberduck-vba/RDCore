using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.SDK.Runtime.Abstract;

/// <summary>
/// <strong>MS-VBAL 5.5.2.2</strong> Set-coercion (run-time semantics) — coerces a value to an object
/// reference of a given destination declared type, wherever an object value is expected (an
/// assignment RHS, an argument, ...). Unlike <see cref="ILetCoercionRuntimeSemantics"/>, Set-coercion
/// has no per-destination-type strategy fan-out (MS-VBAL 5.5.2.1's static table only ever demands both
/// sides be object-ish) — this single entry point covers the whole run-time table.
/// </summary>
public interface ISetCoercionRuntimeSemantics
{
    /// <summary>
    /// Coerces <paramref name="source"/> to an object reference of <paramref name="destinationType"/>.
    /// </summary>
    /// <param name="session">
    /// The active run-time session — needed to resolve a live object's actual class (<see cref="IRuntimeSession.TryGetInstance"/>),
    /// since a <see cref="RDCore.SDK.Model.Values.Intrinsic.VBObjectValue"/>'s own <c>TypeInfo</c> is
    /// always the generic <c>Object</c> type, never a specific class.
    /// </param>
    /// <param name="expression">The expression the coerced value is attributed to, for error reporting.</param>
    /// <param name="source">The value being coerced.</param>
    /// <param name="destinationType">The destination's declared type — expected to be a specific class, <c>Object</c>, or <c>Variant</c>.</param>
    SetCoercionResult EvaluateSetCoercion(IRuntimeSession session, ExpressionNode expression, VBTypedValue source, VBType destinationType);
}
