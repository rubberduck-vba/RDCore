using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Runtime.Execution.Frames;

/// <summary>
/// The cell of a variable of a scalar type (<c>Byte</c>, <c>Integer</c>, <c>Long</c>, <c>LongLong</c>, <c>LongPtr</c>, <c>Single</c>, <c>Double</c>,
/// <c>Boolean</c>, <c>Currency</c> or <c>Date</c>) that holds its value as a payload rather than a boxed runtime value: what an expression
/// evaluated on payloads reads and writes, without making a value of it.
/// </summary>
/// <remarks>
/// <para>
/// It is a cell like any other to whatever reads it as one: <see cref="Value"/> boxes the payload, once for each value written, and a value stored as a
/// runtime value is read back as that very value.
/// </para>
/// <para>
/// A value it cannot hold as a payload - one of another representation than its type's - is held as it is, and the payload is not valid until a payload
/// is written again: <see cref="TryRead"/> says so.
/// </para>
/// </remarks>
internal sealed class ScalarCell : ICopyableBinding
{
    private IRuntimeValue? _boxed;
    private ScalarPayload _payload;
    private bool _hasPayload;

    private ScalarCell(VBType type) => Type = type;

    /// <summary>
    /// A cell for <paramref name="value"/>, if its type is one a payload holds.
    /// </summary>
    /// <param name="type">The declared type of the variable.</param>
    /// <param name="value">What it holds to begin with.</param>
    /// <param name="cell">The cell.</param>
    public static bool TryCreate(VBType type, IRuntimeValue value, out ScalarCell? cell)
    {
        cell = null;
        if (!ScalarPayload.IsScalar(type))
        {
            return false;
        }

        cell = new ScalarCell(type);
        cell.Store(value, isKnown: true);
        return true;
    }

    /// <summary>
    /// A cell holding <paramref name="payload"/>, of the payload's type: a known value, boxed when it is first read as one.
    /// </summary>
    public static ScalarCell Of(ScalarPayload payload)
    {
        var cell = new ScalarCell(payload.Type);
        cell.Write(payload);
        return cell;
    }

    // a copy of an element of an array that is assigned is a payload cell too: a scalar is immutable, so the boxed value, if any, is shared.
    public IBindingHandle Copy()
        => new ScalarCell(Type) { _boxed = _boxed, _payload = _payload, _hasPayload = _hasPayload, IsKnown = IsKnown };

    /// <summary>The declared type of the variable, which its payload is of.</summary>
    public VBType Type { get; }

    /// <summary>
    /// The address the cell is bound at in the session's memory (<see cref="Memory.SessionStorage"/>), if it is: what a second name for its variable -
    /// a <c>ByRef</c> parameter given the cell (<see cref="CellReference"/>) - finds the variable's address by. A copy is bound nowhere.
    /// </summary>
    public RDCore.SDK.Runtime.Shared.MemoryAddress? Address { get; set; }

    /// <summary>
    /// Whether compiled code made the cell of a value given to a parameter passed by reference (MS-VBAL §5.3.1.11), a value that is no variable of
    /// the caller's. The activation the cell is given to takes it as its own and clears the flag, as the interpreter's callee owns the cell it makes
    /// of such a value.
    /// </summary>
    public bool IsGivenValue { get; set; }

    /// <inheritdoc/>
    public bool IsKnown { get; private set; } = true;

    /// <inheritdoc/>
    public IRuntimeValue Value => _boxed ??= _payload.Box();

    /// <inheritdoc/>
    public BindingCapabilities BindingCapabilities => BindingCapabilities.GetValue | BindingCapabilities.SetValue;

    /// <summary>
    /// The payload, when the cell holds one and its value is known.
    /// </summary>
    public bool TryRead(out ScalarPayload payload)
    {
        payload = _payload;
        return _hasPayload && IsKnown;
    }

    /// <summary>
    /// Writes a payload of the cell's type: the value is known, and is boxed when it is read as a value.
    /// </summary>
    public void Write(ScalarPayload payload)
    {
        _payload = payload;
        _hasPayload = true;
        _boxed = null;
        IsKnown = true;
    }

    /// <inheritdoc/>
    public void Store(IRuntimeValue value, bool isKnown)
    {
        _boxed = value;
        _hasPayload = ScalarPayload.TryOf(Type, value, out _payload);
        IsKnown = isKnown;
    }

    /// <inheritdoc/>
    public void Forget() => IsKnown = false;

    /// <inheritdoc/>
    public IRuntimeValue GetValue(IRuntimeSymbolResolver resolver) => Value;

    /// <inheritdoc/>
    public void SetValue(IRuntimeSymbolResolver resolver, IRuntimeValue value) => Store(value, isKnown: true);

    /// <inheritdoc/>
    public IRuntimeValue Invoke(IRuntimeSymbolResolver resolver, IRuntimeValue[] args) => throw new NotSupportedException();
}

/// <summary>
/// A value of a scalar type, as the payload of a <see cref="ScalarCell"/> or of an expression evaluated on payloads: an integer type in
/// <see cref="Integral"/> - a <c>Boolean</c> as its -1 or 0, a <c>Currency</c> as its ten-thousandths - and a floating-point one in <see cref="Floating"/>,
/// a <c>Date</c> as its serial.
/// </summary>
/// <param name="Type">The type of the value: <c>Byte</c>, <c>Integer</c>, <c>Long</c>, <c>LongLong</c>, <c>LongPtr</c>, <c>Single</c>, <c>Double</c>,
/// <c>Boolean</c>, <c>Currency</c> or <c>Date</c>.</param>
/// <param name="Integral">The value of an integer type, of a <c>Boolean</c>, or of a <c>Currency</c> scaled by 10,000.</param>
/// <param name="Floating">The value of a floating-point type, or the serial of a <c>Date</c>.</param>
internal readonly record struct ScalarPayload(VBType Type, long Integral, double Floating)
{
    /// <summary>What a <c>Currency</c> is scaled by, to be held in <see cref="Integral"/>.</summary>
    public const long CurrencyScale = 10000;

    /// <summary>
    /// Whether a value of <paramref name="type"/> is held as a payload. A <c>LongPtr</c> is: it is the integer type it is an alias of (MS-VBAL §2.2), which
    /// the session's environment decides when the declaration is resolved.
    /// </summary>
    public static bool IsScalar(VBType type)
        => type is VBByteType or VBIntegerType or VBLongType or VBLongLongType or VBSingleType or VBDoubleType or VBBooleanType or VBCurrencyType or VBDateType
            or VBLongPtrType_x64 or VBLongPtrType_x86;

    /// <summary>
    /// The type a value of <paramref name="type"/> is held as outside a variable of its own: a <c>LongPtr</c>'s is the type it is an alias of (MS-VBAL §2.2),
    /// which a <c>Variant</c> holds of it, as the let-coercion of one makes it; any other type is its own.
    /// </summary>
    public static VBType AliasOf(VBType type) => type switch
    {
        VBLongPtrType_x64 => VBLongLongType.TypeInfo,
        VBLongPtrType_x86 => VBLongType.TypeInfo,
        _ => type,
    };

    /// <summary>
    /// The payload as a <c>Variant</c> holds it: of the type <see cref="AliasOf"/> gives of its type, the type a <c>LongPtr</c> is an alias of.
    /// </summary>
    public ScalarPayload HeldByAVariant() => this with { Type = AliasOf(Type) };

    /// <summary>Whether the value is held in <see cref="Integral"/>: one of the integer types, a <c>Boolean</c> or a <c>Currency</c>.</summary>
    public bool IsIntegral => Type is not (VBSingleType or VBDoubleType or VBDateType);

    /// <summary>Whether the value is not zero: what it is let-coerced to a <c>Boolean</c> as (MS-VBAL §5.5.1.2.2, §5.5.1.2.3).</summary>
    public bool IsTrue => IsIntegral ? Integral != 0 : Floating != 0;

    /// <summary>
    /// The payload of <paramref name="value"/>, a runtime value of <paramref name="type"/>.
    /// </summary>
    /// <returns><see langword="false"/> when the value is not of the representation of the type.</returns>
    public static bool TryOf(VBType type, IRuntimeValue value, out ScalarPayload payload)
    {
        payload = (type, value) switch
        {
            (VBByteType, VBRuntimeValue<byte> b) => new(type, b.Value, 0),
            (VBIntegerType, VBRuntimeValue<short> i) => new(type, i.Value, 0),
            (VBLongType, VBRuntimeValue<int> l) => new(type, l.Value, 0),
            (VBLongLongType, VBRuntimeValue<long> ll) => new(type, ll.Value, 0),
            (VBSingleType, VBRuntimeValue<float> s) => new(type, 0, s.Value),
            (VBDoubleType, VBRuntimeValue<double> d) => new(type, 0, d.Value),
            (VBBooleanType, VBRuntimeBooleanValue b) => new(type, b.StoredValue, 0),
            (VBCurrencyType, VBRuntimeValue<VBRuntimeCurrencyValue> c) => new(type, c.Value.StoredValue, 0),
            (VBDateType, VBRuntimeValue<double> d) => new(type, 0, d.Value),
            (VBLongPtrType_x64, VBRuntimeValue<long> p) => new(type, p.Value, 0),
            (VBLongPtrType_x86, VBRuntimeValue<int> p) => new(type, p.Value, 0),
            _ => default,
        };

        return payload.Type is not null;
    }

    /// <summary>
    /// The payload of an integer of <paramref name="type"/>, if it is in its range.
    /// </summary>
    public static bool TryIntegral(VBType type, long value, out ScalarPayload payload)
    {
        payload = new(type, value, 0);
        return type switch
        {
            VBByteType => value is >= byte.MinValue and <= byte.MaxValue,
            VBIntegerType => value is >= short.MinValue and <= short.MaxValue,
            VBLongType or VBLongPtrType_x86 => value is >= int.MinValue and <= int.MaxValue,
            VBLongLongType or VBLongPtrType_x64 => true,
            VBBooleanType => value is 0 or -1,
            _ => false,
        };
    }

    /// <summary>The <c>Boolean</c> payload of <paramref name="value"/>.</summary>
    public static ScalarPayload OfBoolean(bool value) => new(VBBooleanType.TypeInfo, value ? -1 : 0, 0);

    /// <summary>The <c>Currency</c> payload of a value scaled by <see cref="CurrencyScale"/>, which any <c>long</c> is.</summary>
    public static ScalarPayload OfCurrency(long scaled) => new(VBCurrencyType.TypeInfo, scaled, 0);

    /// <summary>
    /// The payload of a floating-point value of <paramref name="type"/>, if it is a finite one in its range - a date's serial, for a <c>Date</c>
    /// (<see cref="VBDateType.IsValidSerial"/>).
    /// </summary>
    public static bool TryFloating(VBType type, double value, out ScalarPayload payload)
    {
        payload = new(type, 0, type is VBSingleType ? (float)value : value);
        return type switch
        {
            VBSingleType or VBDoubleType => double.IsFinite(payload.Floating),
            VBDateType => VBDateType.IsValidSerial(value),
            _ => false,
        };
    }

    /// <summary>The runtime value the payload is the value of.</summary>
    public IRuntimeValue Box() => Type switch
    {
        VBByteType => new VBRuntimeValue<byte>((byte)Integral),
        VBIntegerType => new VBRuntimeValue<short>((short)Integral),
        VBLongType or VBLongPtrType_x86 => new VBRuntimeValue<int>((int)Integral),
        VBLongLongType or VBLongPtrType_x64 => new VBRuntimeValue<long>(Integral),
        VBSingleType => new VBRuntimeValue<float>((float)Floating),
        VBBooleanType => new VBRuntimeBooleanValue((short)Integral),
        VBCurrencyType => new VBRuntimeValue<VBRuntimeCurrencyValue>(new VBRuntimeCurrencyValue(Integral)),
        // a Double, or the serial of a Date.
        _ => new VBRuntimeValue<double>(Floating),
    };

    /// <summary>
    /// The value of <see cref="Type"/> the payload is: a <c>LongPtr</c> of the environment's width, made from the integer it is, as
    /// <see cref="DeclaredValues"/> makes one.
    /// </summary>
    public VBTypedValue Typed() => Type switch
    {
        VBLongPtrType_x86 => new VBLongPtrValue((int)Integral),
        VBLongPtrType_x64 => new VBLongPtrValue(Integral),
        _ => Type.CreateValue(new ValueBindingHandle(Box())),
    };
}
