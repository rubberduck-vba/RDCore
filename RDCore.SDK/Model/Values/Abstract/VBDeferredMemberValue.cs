using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;

namespace RDCore.SDK.Model.Values.Abstract;

public abstract record class VBDeferredMemberValue(Symbol Symbol) : VBTypedValue(VBVariantType.TypeInfo, Symbol)
{
    public override int Size => sizeof(int);

    public string Name { get; init; } = string.Empty;
    public VBDeferredMemberValue WithName(string name) => this with { Name = name };
}