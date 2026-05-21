using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;

namespace RDCore.SDK.Model.Expressions.Abstract;

/// <summary>
/// Represents any expression that can be statically evaluated to a <c>VBType</c>, and with runtime semantics to a <c>VBValue</c>.
/// </summary>
/// <param name="Location">The document location of the expression.</param>
public abstract record class BoundExpression(Location Location)
{
    public Location Location { get; init; } = Location;

    public VBType? ResolvedType { get; init; }
    public VBTypedValue? ResolvedValue { get; init; }

    public BoundExpression WithLocation(Location location) => this with { Location = location };
}
