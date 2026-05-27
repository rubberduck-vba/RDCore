using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Runtime;
using RDCore.SDK.Semantics.Runtime.Abstract;

namespace RDCore.Runtime;

/// <summary>
/// Represents and encapsulates the execution environment and memory space.
/// </summary>
public sealed class VBExecutionContext(IVirtualHeap memory) : IVBExecutionContext
{
    required public bool Is64Bit { get; init; }

    IVirtualHeap IVBExecutionContext.Memory { get; } = memory;
    private IVirtualHeap Memory => ((IVBExecutionContext)this).Memory;
    private Stack<CallStackFrame> Frames { get; } = [];

    public RuntimeSemanticsEvaluationResult Call(VBTypeMemberSymbol symbol)
    {
        var value = Memory.GetValue(symbol);
        switch (symbol.ScopeKind)
        {
            case ScopeKind.Unallocated:
            case ScopeKind.Local:
                break;

            case ScopeKind.Global:
                break;
            case ScopeKind.Module:
                break;
            case ScopeKind.Instance:
                break;

            case ScopeKind.External:
                // TODO 🦄
                break;
        }

        // if we haven't returned yet, we never could.
        return RuntimeSemanticsEvaluationResult.InternalError(); // TODO throw 
    }

    public void AnalyzeCall()
    {
        // TODO analyze a Call context
        // 💡 analsis context should include a TimeSpan with the time elapsed during execution.
    }
}
