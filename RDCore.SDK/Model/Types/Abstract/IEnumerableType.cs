using RDCore.SDK.Model.Symbols.Abstract;

namespace RDCore.SDK.Model.Types.Abstract;

public interface IEnumerableType { }

public interface IEnumerableObject : IEnumerableType
{
    VBReturningMemberSymbol? NewEnumMember { get; }
}