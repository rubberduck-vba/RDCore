using RDCore.Runtime.Semantics;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Runtime.Execution;

/// <summary>
/// An <see cref="IProcedureInvoker"/> that runs nothing: a call to a procedure returns a value that is not known, of the type the procedure is declared to return.
/// </summary>
/// <remarks>
/// <para>
/// What an analysis of one procedure knows about another is what it declares. Running the callee would state a fact about the callee's code
/// at a line of the callee's own, for the arguments of this call, and the callee is analyzed in its own right with arguments that are not known; and what
/// it returns for these would be a value the analysis of the caller cannot, in general, trust (<see cref="Semantics.RuntimeExpressionEvaluator.AssumesVariables"/>).
/// </para>
/// <para>
/// 👉 The effects of the call are not made either. What the call writes through a <c>ByRef</c> argument is not trusted by the reads that follow,
/// which no read of a variable is in an analysis that assumes them.
/// </para>
/// </remarks>
/// <param name="observation">Told that the call was assumed rather than made: what the callee raises is not known not to be raised.</param>
public sealed class AssumedProcedureInvoker(AnalysisObservation? observation = null) : IProcedureInvoker
{
    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult Invoke(VBTypeMemberSymbol procedure, IRuntimeSymbolResolver resolver, IRuntimeValue[] arguments)
    {
        observation?.Assume();
        return RuntimeSemanticsEvaluationResult.Success(
            procedure is VBReturningMemberSymbol returning ? returning.ResolvedType.CreateIndeterminateValue() : VBVoidValue.Void);
    }
}
