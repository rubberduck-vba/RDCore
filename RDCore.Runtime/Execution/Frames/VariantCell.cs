using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Runtime.Execution.Frames;

/// <summary>
/// The cell of a <c>Variant</c> variable that holds a value of a scalar type as a payload (IR lowering S1.4): what an expression evaluated on payloads reads
/// and writes, the payload's type being the Variant's subtype - and anything else (<c>Empty</c>, <c>Null</c>, a <c>String</c>, an object, an array,
/// <c>Missing</c>) as the runtime value it is.
/// </summary>
/// <remarks>
/// It is a cell like any other to whatever reads it as one: <see cref="Value"/> is the <see cref="VBRuntimeVariantValue"/> of the payload, made once for
/// each payload written, and a value stored as a runtime value is read back as that very value.
/// </remarks>
internal sealed class VariantCell : IKnowledgeBinding
{
    private IRuntimeValue? _boxed;
    private ScalarPayload _payload;
    private bool _hasPayload;

    private VariantCell()
    {
    }

    /// <summary>
    /// A cell for <paramref name="value"/>, if it is a Variant's runtime value.
    /// </summary>
    /// <param name="type">The declared type of the variable, which is <c>Variant</c> for there to be a cell.</param>
    /// <param name="value">What it holds to begin with.</param>
    /// <param name="cell">The cell.</param>
    public static bool TryCreate(VBType type, IRuntimeValue value, out VariantCell? cell)
    {
        cell = null;
        if (type is not VBVariantType || value is not VBRuntimeVariantValue)
        {
            return false;
        }

        cell = new VariantCell();
        cell.Store(value, isKnown: true);
        return true;
    }

    /// <summary>
    /// A Variant cell holding <paramref name="payload"/>: a known value of the payload's type, as a Let of it to a Variant makes it hold (MS-VBAL
    /// §5.5.1.2.12) - what a Variant parameter passed a scalar by value starts as (IR lowering S1.7).
    /// </summary>
    public static VariantCell Of(ScalarPayload payload)
    {
        var cell = new VariantCell();
        cell.Write(payload);
        return cell;
    }

    /// <inheritdoc/>
    public bool IsKnown { get; private set; } = true;

    /// <inheritdoc/>
    public IRuntimeValue Value => _boxed ??= Box(_payload);

    /// <inheritdoc/>
    public BindingCapabilities BindingCapabilities => BindingCapabilities.GetValue | BindingCapabilities.SetValue;

    /// <summary>
    /// The payload, when the Variant holds one and its value is known: of the Variant's subtype.
    /// </summary>
    public bool TryRead(out ScalarPayload payload)
    {
        payload = _payload;
        return _hasPayload && IsKnown;
    }

    /// <summary>
    /// Whether what the Variant holds is nothing an object is counted for (<see cref="ObjectReferences"/>): a payload, <c>Empty</c>, <c>Null</c>, a
    /// <c>String</c>. A payload is written over it as it is; over an object, an array or a record, the boxed assignment lets go of what it held.
    /// </summary>
    public bool HoldsNoObject { get; private set; } = true;

    /// <summary>
    /// Writes a payload: the Variant holds a value of the payload's type (a Let to a Variant keeps the value's type, MS-VBAL §5.5.1.2.12), known, and boxed
    /// when it is read as a value.
    /// </summary>
    public void Write(ScalarPayload payload)
    {
        _payload = payload;
        _hasPayload = true;
        _boxed = null;
        IsKnown = true;
        HoldsNoObject = true;
    }

    /// <inheritdoc/>
    public void Store(IRuntimeValue value, bool isKnown)
    {
        _boxed = value;
        _hasPayload = TryPayloadOf(value, out _payload);
        IsKnown = isKnown;
        HoldsNoObject = _hasPayload || value is VBRuntimeVariantValue { WrappedValue: var held } && !ObjectReferences.CanHoldAnObject(held.TypeInfo);
    }

    /// <inheritdoc/>
    public void Forget() => IsKnown = false;

    /// <inheritdoc/>
    public IRuntimeValue GetValue(ISymbolResolver resolver) => Value;

    /// <inheritdoc/>
    public void SetValue(ISymbolResolver resolver, IRuntimeValue value) => Store(value, isKnown: true);

    /// <inheritdoc/>
    public IRuntimeValue Invoke(ISymbolResolver resolver, IRuntimeValue[] args) => throw new NotSupportedException();

    // what a Variant holds, when it is a known value of a scalar type.
    private static bool TryPayloadOf(IRuntimeValue value, out ScalarPayload payload)
    {
        payload = default;
        return value is VBRuntimeVariantValue { WrappedValue: { IsIndeterminate: false } held }
            && ScalarPayload.IsScalar(held.TypeInfo)
            && ScalarPayload.TryOf(held.TypeInfo, held.RuntimeValue, out payload);
    }

    private static VBRuntimeVariantValue Box(ScalarPayload payload)
        => new(payload.Type.VarType(), payload.Type.CreateValue(new ValueBindingHandle(payload.Box())));
}
