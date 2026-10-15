using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.SDK.Services.VerboseMessages;

using RDCore.SDK.Model;

namespace RDCore.Runtime.Semantics.Operators.Arithmetic;

/// <summary>
/// MS-VBAL 5.6.9.3.6 Binary '\' Operator and 'Mod' Operator (runtime semantics)
/// </summary>
public sealed record class BinaryModuloOperatorRuntimeSemantics(
    ILetCoercionRuntimeSemanticsProvider LetCoercionProvider,
    IVerboseMessageBuilder FormatterService)
    : BinaryIntegerDivisionOperatorRuntimeSemantics(LetCoercionProvider, FormatterService)
{
    // the plan and the evaluation pipeline are shared with '\'; only the managed operation differs.
    protected override string Operator => Tokens.ModuloOp;

    protected override T EvaluateManagedNumericOp<T>(T lhs, T rhs) => lhs % rhs;
}
