using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Runtime.Execution;

/// <summary>
/// The field of a record that is a record itself, or a <c>Variant</c>, as a variable a <c>ByRef</c> parameter is a second name for (<strong>MS-VBAL §5.3.1.11</strong>):
/// what reads it reads what the field holds - the same record, whose own fields are written in place - and what assigns it replaces what the field holds.
/// </summary>
/// <remarks>
/// A field that is a record has no cell of its own, as a field of an intrinsic type does: the record it holds is the field. Nor is a <c>Variant</c>'s cell what reading the
/// field reads: the record holds the value. This is the cell either is given for as long as a call it is passed to runs (MVVM's
/// <c>ConnectToConnectionPoint Me, This.RefIID, ...</c>; <c>BumpV r.V</c>, observed in Excel).
/// </remarks>
/// <param name="session">The session whose objects what the field holds are.</param>
/// <param name="record">The record whose field it is.</param>
/// <param name="index">The field's position in declaration order.</param>
/// <param name="fieldType">The type the field is declared as: a record type, or <c>Variant</c>.</param>
internal sealed class RecordFieldBinding(IRuntimeSession session, VBUserDefinedTypeValue record, int index, VBType fieldType) : IBindingHandle
{
    /// <summary>
    /// The record whose field it is.
    /// </summary>
    public VBUserDefinedTypeValue Record => record;

    /// <inheritdoc/>
    public IRuntimeValue Value => record.StoredFieldAt(index) is { } held ? SymbolAddressTable.BoxedValue(held) : fieldType.DefaultValue.RuntimeValue;

    /// <inheritdoc/>
    public BindingCapabilities BindingCapabilities => BindingCapabilities.GetValue | BindingCapabilities.SetValue;

    /// <inheritdoc/>
    public IRuntimeValue GetValue(IRuntimeSymbolResolver resolver) => Value;

    /// <inheritdoc/>
    public void SetValue(IRuntimeSymbolResolver resolver, IRuntimeValue value)
    {
        // the field holds what it is assigned, and what that holds is the field's now: what it held is let go of.
        var before = ObjectReferences.HeldInField(record, index);
        _ = record.TrySetFieldAt(index, fieldType.CreateValue(new ValueBindingHandle(value)));
        ObjectReferences.RebindField(session, record, index, before);
    }

    /// <inheritdoc/>
    public IRuntimeValue Invoke(IRuntimeSymbolResolver resolver, IRuntimeValue[] args) => throw new NotSupportedException();
}
