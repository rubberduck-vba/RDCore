using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.SDK.Model.Values.Intrinsic;

/// <summary>
/// Represents a value of a <see cref="VBUserDefinedType"/>. Like <see cref="VBObjectValue"/> the
/// value is a <see cref="MemoryAddress"/> — a UDT has location identity and is never passed by value.
/// </summary>
public record class VBUserDefinedTypeValue : VBTypedValue,
    IVBTypedValue<VBUserDefinedTypeValue, MemoryAddress>
{
    public VBUserDefinedTypeValue(IBindingHandle handle, VBUserDefinedType typeInfo) : base(typeInfo)
    {
        Handle = handle;
    }

    public VBUserDefinedTypeValue(MemoryAddress reference, VBUserDefinedType typeInfo)
        : this(new ValueBindingHandle(new VBRuntimeReference(reference)), typeInfo) { }

    public VBUserDefinedTypeValue(VBUserDefinedType typeInfo)
        : this(MemoryAddress.Zero, typeInfo) { }

    public MemoryAddress Value => ((VBRuntimeReference)RuntimeValue).Value;

    // A flat, unpadded sum of field sizes in declaration order — RDCore does not model native
    // struct alignment.
    public override int Size => ((IVBMemberOwnerType)TypeInfo).Members.OfType<VBUserDefinedTypeFieldSymbol>()
        .Sum(member => member.ResolvedType!.DefaultValue.Size);

    public bool Equals(IVBTypedValue<VBUserDefinedTypeValue, MemoryAddress>? other) => Value.Value.Equals(other?.Value.Value);
}
