using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;

namespace RDCore.SDK.Model.AST.Abstract;

/// <summary>
/// The base AST expression node. 
/// Represents any expression that can be statically evaluated to a <c>VBType</c>, and with runtime semantics to a <c>VBValue</c>.
/// </summary>
/// <param name="Location">The document location of the expression.</param>
public abstract record class BoundExpression(Location Location)
{
    public Location Location { get; init; } = Location;

    /// <summary>
    /// The <em>declared type</em> of the expression.
    /// </summary>
    public VBType DeclaredType { get; init; } = VBVoidType.TypeInfo;
    public VBTypedValue ExpressionValue { get; init; } = VBVoidValue.Void;

    public BoundExpression WithLocation(Location location) => this with { Location = location };
}
