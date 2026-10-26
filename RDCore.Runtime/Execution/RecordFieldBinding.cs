using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Runtime.Execution;

/// <summary>
/// The field of a record that is a record itself, as a variable a <c>ByRef</c> parameter is a second name for (<strong>MS-VBAL §5.3.1.11</strong>): what reads it
/// reads the record the field holds - the same one, whose own fields are written in place - and what assigns it replaces what the field holds.
/// </summary>
/// <remarks>
/// A field that is a record has no cell of its own, as a field of an intrinsic type does: the record it holds is the field. This is the cell it is given for as long as a
/// call it is passed to runs (MVVM's <c>ConnectToConnectionPoint Me, This.RefIID, ...</c>).
/// </remarks>
/// <param name="session">The session whose objects what the field holds are.</param>
/// <param name="record">The record whose field it is.</param>
/// <param name="index">The field's position in declaration order.</param>
/// <param name="fieldType">The record type the field is declared as.</param>
internal sealed class RecordFieldBinding(IRuntimeSession session, VBUserDefinedTypeValue record, int index, VBUserDefinedType fieldType) : IBindingHandle
{
    /// <inheritdoc/>
    public IRuntimeValue Value => record.StoredFieldAt(index) is { } held ? SymbolAddressTable.BoxedValue(held) : fieldType.DefaultValue.RuntimeValue;

    /// <inheritdoc/>
    public BindingCapabilities BindingCapabilities => BindingCapabilities.GetValue | BindingCapabilities.SetValue;

    /// <inheritdoc/>
    public IRuntimeValue GetValue(ISymbolResolver resolver) => Value;

    /// <inheritdoc/>
    public void SetValue(ISymbolResolver resolver, IRuntimeValue value)
    {
        // the field holds the record it is assigned, and what that record's fields hold is the field's now: what the record it held held is let go of.
        var before = ObjectReferences.HeldInField(record, index);
        _ = record.TrySetFieldAt(index, fieldType.CreateValue(new ValueBindingHandle(value)));
        ObjectReferences.RebindField(session, record, index, before);
    }

    /// <inheritdoc/>
    public IRuntimeValue Invoke(ISymbolResolver resolver, IRuntimeValue[] args) => throw new NotSupportedException();
}
