using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Runtime.Execution.Frames;

/// <summary>
/// The cell of a variable of a numeric scalar type - <c>Byte</c>, <c>Integer</c>, <c>Long</c>, <c>LongLong</c>, <c>Single</c>, <c>Double</c> - that holds
/// its value as a payload rather than a boxed runtime value (IR lowering S1.2): what an expression evaluated on payloads reads and writes, without making a
/// value of it.
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
internal sealed class ScalarCell : IKnowledgeBinding
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

    /// <summary>The declared type of the variable, which its payload is of.</summary>
    public VBType Type { get; }

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
    public IRuntimeValue GetValue(ISymbolResolver resolver) => Value;

    /// <inheritdoc/>
    public void SetValue(ISymbolResolver resolver, IRuntimeValue value) => Store(value, isKnown: true);

    /// <inheritdoc/>
    public IRuntimeValue Invoke(ISymbolResolver resolver, IRuntimeValue[] args) => throw new NotSupportedException();
}

/// <summary>
/// A value of a numeric scalar type, as the payload of a <see cref="ScalarCell"/> or of an expression evaluated on payloads: an integer type in
/// <see cref="Integral"/>, a floating-point one in <see cref="Floating"/>.
/// </summary>
/// <param name="Type">The type of the value: <c>Byte</c>, <c>Integer</c>, <c>Long</c>, <c>LongLong</c>, <c>Single</c> or <c>Double</c>.</param>
/// <param name="Integral">The value of an integer type.</param>
/// <param name="Floating">The value of a floating-point type.</param>
internal readonly record struct ScalarPayload(VBType Type, long Integral, double Floating)
{
    /// <summary>Whether a value of <paramref name="type"/> is held as a payload.</summary>
    public static bool IsScalar(VBType type) => type is VBByteType or VBIntegerType or VBLongType or VBLongLongType or VBSingleType or VBDoubleType;

    /// <summary>Whether the type is one of the integer types.</summary>
    public bool IsIntegral => Type is VBByteType or VBIntegerType or VBLongType or VBLongLongType;

    /// <summary>The value as a <c>double</c>, whatever its type.</summary>
    public double AsDouble => IsIntegral ? Integral : Floating;

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
            VBLongType => value is >= int.MinValue and <= int.MaxValue,
            VBLongLongType => true,
            _ => false,
        };
    }

    /// <summary>
    /// The payload of a floating-point value of <paramref name="type"/>, if it is a finite one in its range.
    /// </summary>
    public static bool TryFloating(VBType type, double value, out ScalarPayload payload)
    {
        payload = new(type, 0, type is VBSingleType ? (float)value : value);
        return type is VBSingleType or VBDoubleType && double.IsFinite(payload.Floating);
    }

    /// <summary>The runtime value the payload is the value of.</summary>
    public IRuntimeValue Box() => Type switch
    {
        VBByteType => new VBRuntimeValue<byte>((byte)Integral),
        VBIntegerType => new VBRuntimeValue<short>((short)Integral),
        VBLongType => new VBRuntimeValue<int>((int)Integral),
        VBLongLongType => new VBRuntimeValue<long>(Integral),
        VBSingleType => new VBRuntimeValue<float>((float)Floating),
        _ => new VBRuntimeValue<double>(Floating),
    };
}
