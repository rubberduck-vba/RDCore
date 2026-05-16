using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Parsing.Model.Types;
using RDCore.Parsing.Model.Types.Abstract;
using RDCore.Parsing.Model.Values.Abstract;
using RDCore.Semantics.Runtime.Abstract;
using RDCore.Semantics.Static.Abstract;

namespace RDCore.Parsing.Model.Expressions.Abstract;

/// <summary>
/// Represents any expression that can be statically evaluated to a <c>VBType</c>, and with runtime semantics to a <c>VBValue</c>.
/// </summary>
internal abstract record class BoundExpression(Location Location) : ISemanticNode
{
    public Location Location { get; init; } = Location;

    public VBType StaticType { get; init; } = UnresolvedVBType.TypeInfo;
    public VBType RuntimeType { get; init; } = UnresolvedVBType.TypeInfo;

    public abstract StaticSemantics StaticSemantics { get; }
    public abstract RuntimeSemantics RuntimeSemantics { get; }

    public VBTypedValue? StaticValue { get; init; }
    public VBTypedValue? RuntimeValue { get; init; }

    public BoundExpression WithLocation(Location location) => this with { Location = location };

    public BoundExpression WithStaticType(VBType type) => this with { StaticType = type };
    public BoundExpression WithStaticValue(VBTypedValue value) => this with { StaticValue = value, StaticType = value.TypeInfo };

    public BoundExpression WithRuntimeType(VBType type) => this with { RuntimeType = type };
    public BoundExpression WithRuntimeValue(VBTypedValue value) => this with { RuntimeValue = value, RuntimeType = value.TypeInfo };
}
