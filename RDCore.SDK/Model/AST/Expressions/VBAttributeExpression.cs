using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace RDCore.SDK.Model.AST.Expressions;

/// <summary>
/// Represents a <c>VB_Attribute</c> semantic expression node.
/// </summary>
/// <remarks>
/// <strong>MS-VBAL 4.2 Modules</strong> defines the static semantics of this expression.
/// </remarks>
public record class VBAttributeExpression : ValuedExpression
{
    public VBAttributeExpression(Location location, ValuedExpression name, ValuedExpression value)
        : base(location)
    {
        NameExpression = name;
        ValueExpression = value;
    }

    /// <summary>
    /// The name of the attribute.
    /// </summary>
    public ValuedExpression NameExpression { get; init; }
    /// <summary>
    /// 
    /// </summary>
    public ValuedExpression ValueExpression { get; init; }
}