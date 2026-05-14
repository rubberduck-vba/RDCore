using RDCore.Parsing.Model.Values;

namespace RDCore.Parsing.Model.Types;

internal record class VBVoidType : VBType
{
    public static VBType TypeInfo { get; } = new VBVoidType();
    private VBVoidType() : base(typeof(void), string.Empty, isHidden: true) { }

    public override VBTypedValue DefaultValue => VBVoidValue.Void;
}