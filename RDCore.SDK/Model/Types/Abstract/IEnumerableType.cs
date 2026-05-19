using RDCore.SDK.Model.Symbols.Abstract;

namespace RDCore.SDK.Model.Types.Abstract;

/// <summary>
/// Represents a type that can be iterated in a <c>For Each...Next</c> loop.
/// </summary>
public interface IEnumerableType
{
    /// <summary>
    /// If <c>true</c>, flags <c>For Each</c> iteration as inefficient, for performance diagnostics.
    /// </summary>
    bool IsArray { get; }
}

public interface IEnumerableObject : IEnumerableType
{
    VBReturningMemberSymbol? NewEnumMember { get; }
}