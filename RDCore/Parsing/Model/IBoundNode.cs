using RDCore.Parsing.Model.Types;

namespace RDCore.Parsing.Model;

internal interface IBoundNode
{
    VBType StaticDeclaredType { get; init; }
}
