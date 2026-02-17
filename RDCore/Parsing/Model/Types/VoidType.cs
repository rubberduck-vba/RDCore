using RDCore.Parsing.Model.Types.Abstract;
using RDCore.Parsing.Model.Values;

namespace RDCore.Parsing.Model.Types;

internal record class VoidType : VBType
{
    public static VBType VBType { get; } = new VoidType();
    private VoidType() : base(typeof(void), string.Empty, isHidden: true) { }

    public override VBTypedValue DefaultValue => VBVoidValue.Void;
}