using RDCore.Parsing.Model.Values;

namespace RDCore.Parsing.Model.Types;

internal sealed record class UnresolvedType : VBType
{
    public static VBType VBType { get; } = new UnresolvedType();

    public override VBTypedValue DefaultValue => VBVoidValue.Void;

    public UnresolvedType() : base(typeof(object), nameof(UnresolvedType)) { }
}
