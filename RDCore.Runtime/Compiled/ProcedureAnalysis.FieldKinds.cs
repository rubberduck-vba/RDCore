using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;

namespace RDCore.Runtime.Compiled;

// what compiled code holds a field it reaches as (IR lowering S2.6c): the kind rule, one case for each kind of field, with its run-time side in
// CompiledActivation.CellOfKind. Which fields compiled code reaches is ProcedureAnalysis.Fields.cs's.
internal sealed partial class ProcedureAnalysis
{
    // whether compiled code holds field, numbered slot, and how: false leaves it the interpreter's, which runs what reads or writes it.
    private bool TakesFieldOfKind(ITypedSymbol field, int slot)
    {
        // a scalar is held in its cell (IR lowering S2.5e, CellSlots), which compiled code reads and writes it in, as it does a parameter passed by reference.
        if (ScalarKinds.Of(field.ResolvedType) is { } kind)
        {
            _slots[slot] = kind;
            _ = _cellSlots.Add(slot);
            return true;
        }

        // a record is held by its cell, its holder (RecordSlots), which compiled code finds the record and its fields in each time.
        if (field.ResolvedType is VBUserDefinedType)
        {
            _ = _cellSlots.Add(slot);
            _ = _recordSlots.Add(slot);
            return true;
        }

        return false;
    }
}
