using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Parsing.Model.Types;
using RDCore.Parsing.Model.Types.Abstract;

namespace RDCore.Parsing.Model.Abstract;

internal abstract record class BoundExpression(Location Location) : IBoundNode
{
    public Location SelectionRange { get; init; } = Location;
    public VBType ResultType { get; init; } = UnresolvedType.VBType;

    public BoundExpression WithResultType(VBType type) => this with { ResultType = type };
}
