using RDCore.SDK.Model.Symbols.Abstract;

namespace RDCore.Runtime.Compiled;

// what compiled code holds a field it reaches as: the kind rule, one case for each kind of field, with its run-time side in CompiledActivation.CellOfKind. Which fields compiled code reaches is ProcedureAnalysis.Fields.cs's.
internal sealed partial class ProcedureAnalysis
{
    // whether compiled code holds field, numbered slot, and how: false leaves it the interpreter's, which runs what reads or writes it.
    private bool TakesFieldOfKind(ITypedSymbol field, int slot)
    {
        // a scalar is held in its cell (CellSlots), which compiled code reads and writes it in, as it does a parameter passed by reference.
        if (ScalarKinds.Of(field.ResolvedType) is { } kind)
        {
            _slots[slot] = kind;
            _ = _cellSlots.Add(slot);
            return true;
        }

        return false;
    }
}
