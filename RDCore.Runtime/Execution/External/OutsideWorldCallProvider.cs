using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Runtime.Execution.External;

/// <summary>
/// Answers a call to the outside world in an <em>analysis</em>: it is not made, and what it yields is not known.
/// </summary>
/// <remarks>
/// <para>
/// A <c>Declare</c>d function, a call into a COM library, anything the platform does not run itself is something the analysis must neither perform nor
/// pretend to know the outcome of. The call yields a value of the declared return type that is not known, and every argument passed
/// <c>ByRef</c> is not known afterwards: the callee could have written to it.
/// </para>
/// <para>
/// Meant to be the last of the providers, after the ones that run what the platform implements (the standard library).
/// </para>
/// </remarks>
public sealed class OutsideWorldCallProvider : IExternalCallProvider
{
    /// <inheritdoc/>
    public bool CanDispatch(ExternalCallRequest request) => true;

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult Dispatch(ExternalCallRequest request, IRuntimeSymbolResolver resolver)
    {
        foreach (var argument in request.Arguments)
        {
            Forget(argument, resolver);
        }

        return RuntimeSemanticsEvaluationResult.Success(request.Member is ITypedSymbol { ResolvedType: { } returned } && returned is not VBVoidType
            ? returned.CreateIndeterminateValue()
            : VBVoidValue.Void);
    }

    // an argument passed by reference is the address of the variable it names, which the callee may write to - through any chain of references.
    private static void Forget(IRuntimeValue argument, IRuntimeSymbolResolver resolver)
    {
        var visited = new HashSet<MemoryAddress>();
        while (argument is VBRuntimeReference reference && visited.Add(reference.Value) && resolver.TryRead(reference.Value, out var cell))
        {
            if (cell is IKnowledgeBinding knowledge)
            {
                knowledge.Forget();
            }

            argument = cell.Value;
        }
    }
}
