using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.SDK.Model.Values.Intrinsic;

/// <summary>
/// Represents a <see cref="VBObjectType"/> value, i.e. an object reference.
/// </summary>
/// <remarks>
/// The default value of a <c>VBObjectValue</c> is <see cref="VBNothingValue"/>.
/// </remarks>
public record class VBObjectValue : VBTypedValue,
    IVBTypedValue<VBObjectValue, MemoryAddress>
{
    private static readonly Lazy<VBObjectValue> _nothing = new(() 
        => new VBNothingValue(), LazyThreadSafetyMode.PublicationOnly);
    public static VBObjectValue Nothing => _nothing.Value;

    public VBObjectValue(IBindingHandle handle)
        : base(VBObjectType.TypeInfo) 
    {
        Handle = handle;
    }
    public VBObjectValue(MemoryAddress reference) : this(new ValueBindingHandle(new VBRuntimeReference(reference))) { }

    public MemoryAddress Value => ((VBRuntimeReference)RuntimeValue).Value;
    public override int Size => sizeof(int);

    public bool IsNothing() => Value == Nothing.Value;

    public bool Equals(IVBTypedValue<VBObjectValue, MemoryAddress>? other) => Value.Value.Equals(other?.Value.Value);
}
