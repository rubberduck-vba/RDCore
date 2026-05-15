using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Parsing.Model.Types;

namespace RDCore.Parsing.Model;

internal abstract record class BoundExpression(Location Location) : IBoundNode
{
    public Location Location { get; init; } = Location;
    public VBType StaticDeclaredType { get; init; } = UnresolvedType.TypeInfo;

    public BoundExpression WithResultType(VBType type) => this with { StaticDeclaredType = type };
    public BoundExpression WithLocation(Location location) => this with { Location = location };
}
