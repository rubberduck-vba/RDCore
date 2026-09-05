using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;

namespace RDCore.SDK.Model.Values.Intrinsic;

public record class VBUserDefinedTypeValue : VBTypedValue,
    IVBTypedValue<VBUserDefinedTypeValue, VBLongPtrValue>
{
    public VBUserDefinedTypeValue(VBUserDefinedType typeInfo) : base(typeInfo) { }

    /// <summary>
    /// Creates a UDT value bound to <paramref name="handle"/>. The per-member store is not yet
    /// handle-backed, so the handle is currently inert (see the complex-value follow-up).
    /// </summary>
    public VBUserDefinedTypeValue(IBindingHandle handle, VBUserDefinedType typeInfo) : base(typeInfo)
    {
        Handle = handle;
    }

    public VBLongPtrValue Value { get; } = VBLongPtrType_x64.Zero;

    // NOTE: this isn't accurate, there should be some padding involved.
    public override int Size => ((IVBMemberOwnerType)TypeInfo).Members.OfType<VBUserDefinedTypeMemberSymbol>()
        .Sum(member => member.ResolvedType!.DefaultValue.Size);

    public bool Equals(IVBTypedValue<VBUserDefinedTypeValue, VBLongPtrValue>? other) => Value == other?.Value;
    public override int GetHashCode() => Value.GetHashCode();
}
