using RDCore.Runtime.Execution.Frames;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values.Bindings;

namespace RDCore.Runtime.Compiled;

// what compiled code takes the cell of a field for: the kind rule, one case for each kind of field ProcedureAnalysis.TakesFieldOfKind takes. Where the cell
// is found is CompiledActivation.Fields.cs's.
public sealed partial class CompiledActivation
{
    // the cell compiled code reads and writes field in, of what the interpreter resolved it to; null when it holds the field in no cell of its kind.
    private static object? CellOfKind(CompiledField field, IBindingHandle held) => held switch
    {
        // a scalar is a ScalarCell of its own type.
        ScalarCell cell when cell.Type.Equals(((ITypedSymbol)field.Symbol).ResolvedType) => cell,

        // an object is the ValueBindingHandle that holds its reference, which a Set stores in and counts, as a variable of the activation's is.
        ValueBindingHandle cell when ((ITypedSymbol)field.Symbol).ResolvedType is VBObjectType or VBClassType => cell,
        _ => null,
    };
}
