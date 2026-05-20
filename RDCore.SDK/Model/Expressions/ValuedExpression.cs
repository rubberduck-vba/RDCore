using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Model.Expressions.Abstract;
using RDCore.SDK.Semantics.Runtime;
using RDCore.SDK.Semantics.Runtime.Abstract;
using RDCore.SDK.Semantics.Static;
using RDCore.SDK.Semantics.Static.Abstract;

namespace RDCore.SDK.Model.Expressions;

/// <summary>
/// Represents any expression that can be statically evaluated to a <c>VBType</c>, and with runtime semantics to a <c>VBValue</c>.
/// </summary>
public abstract record class ValuedExpression(Location Location) : BoundExpression(Location) 
{
}

public record class LetCoercionExpression(Location Location) : ValuedExpression(Location)
{
    public override StaticSemantics StaticSemantics => LetCoercionStaticSemantics.Instance;

    public override RuntimeSemantics RuntimeSemantics => LetCoercionRuntimeSemantics.GetSemantics();
}