using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Runtime;

namespace RDCore.SDK.Model.Values.Intrinsic;

/// <summary>
/// Represents an <c>Error</c> value.
/// </summary>
/// <remarks>
/// 👉 <see cref="VBMissingValue"/> is one too: the value of an omitted optional <c>Variant</c> parameter is an error value that only
/// <c>IsMissing</c> tells apart from the others.
/// </remarks>
public record class VBErrorValue : VBTypedValue,
    IVBTypedValue<VBErrorValue, int>
{
    public VBErrorValue(IBindingHandle handle) : this(VBErrorType.TypeInfo, handle) { }
    public VBErrorValue(int value = 0) : this(new ValueBindingHandle(new VBRuntimeValue<int>(value))) { }

    /// <summary>
    /// Creates an error value whose <c>Variant</c> subtype is <paramref name="typeInfo"/> rather than <c>Error</c> itself.
    /// </summary>
    /// <param name="typeInfo">The data type the value reports.</param>
    /// <param name="handle">The binding holding the error code.</param>
    protected VBErrorValue(VBType typeInfo, IBindingHandle handle) : base(typeInfo)
    {
        Handle = handle;
    }

    public int Value => ((VBRuntimeValue<int>)RuntimeValue).StoredValue;
    public override int Size => sizeof(int);

    public bool Equals(IVBTypedValue<VBErrorValue, int>? other) => Value == other?.Value;
}
