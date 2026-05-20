using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;

namespace RDCore.SDK.Model.Values.Deffered;

public abstract record class VBDeferredMemberValue(Symbol Symbol) : VBTypedValue(VBVariantType.TypeInfo, Symbol)
{
    public override int Size => sizeof(int);

    public string Name { get; init; } = string.Empty;
    public VBDeferredMemberValue WithName(string name) => this with { Name = name };
}