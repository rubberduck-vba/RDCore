using RDCore.Parsing.Model.Symbols.Abstract;

namespace RDCore.Parsing.Model.Types.Abstract;

internal interface IVBDeclaredType
{
    Symbol Declaration { get; init; }
    Symbol[]? Definitions { get; init; }
}
