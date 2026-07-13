using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;

namespace RDCore.SDK.Model.Values.Intrinsic;

public record class VBUserDefinedTypeValue : VBTypedValue,
    IVBTypedValue<VBUserDefinedTypeValue, VBLongPtrValue>
{
    public VBUserDefinedTypeValue(VBUserDefinedType typeInfo, Symbol symbol) : base(typeInfo, symbol) { }

    public VBLongPtrValue Value { get; } = VBLongPtrType_x64.Zero;

    // NOTE: this isn't accurate, there should be some padding involved.
    public override int Size => ((IVBMemberOwnerType)TypeInfo).Members.OfType<VBUserDefinedTypeMemberSymbol>()
        .Sum(member => member.ResolvedType!.DefaultValue.Size);

    public bool Equals(IVBTypedValue<VBUserDefinedTypeValue, VBLongPtrValue>? other) => Value == other?.Value;
    public override int GetHashCode() => Value.GetHashCode();
}
