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
/// The default value of a <c>VBObjectValue</c> is <see cref="VBNothingValue"/>. Unlike
/// <see cref="VBUserDefinedTypeValue"/>/<see cref="VBArrayValue"/>, identity here is a
/// <see cref="VBRuntimeObjectId"/> minted by <c>ISessionObjects.CreateObject</c> — a live object's
/// instance fields are addressed by their own, per-instance table, not by one flat
/// <see cref="MemoryAddress"/> block, so there is no single address to reference here.
/// </remarks>
public record class VBObjectValue : VBTypedValue,
    IVBTypedValue<VBObjectValue, VBRuntimeObjectId>
{
    private static readonly Lazy<VBObjectValue> _nothing = new(()
        => new VBNothingValue(), LazyThreadSafetyMode.PublicationOnly);
    public static VBObjectValue Nothing => _nothing.Value;

    public VBObjectValue(IBindingHandle handle)
        : base(VBObjectType.TypeInfo)
    {
        Handle = handle;
    }
    public VBObjectValue(VBRuntimeObjectId objectId) : this(new ValueBindingHandle(new VBRuntimeValue<VBRuntimeObjectId>(objectId))) { }

    public VBRuntimeObjectId Value => ((VBRuntimeValue<VBRuntimeObjectId>)RuntimeValue).StoredValue;
    public override int Size => sizeof(int);

    public bool IsNothing() => Value.Equals(Nothing.Value);

    public bool Equals(IVBTypedValue<VBObjectValue, VBRuntimeObjectId>? other) => Value.Equals(other?.Value);
}
