using RDCore.SDK.Model.Symbols.Abstract;

namespace RDCore.SDK.Model.Types.Abstract;

public interface IVBDeclaredType
{
    Symbol Declaration { get; init; }
    Symbol[]? Definitions { get; init; }
}
